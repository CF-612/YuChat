// 限定私网接口的配对转接；原宿主仍只监听 loopback，密钥/模型不离开 PC。
use std::{sync::Arc, net::Ipv4Addr, time::{Duration,Instant}, path::PathBuf};
use axum::{Router,extract::{State,Request},http::{StatusCode,HeaderMap},response::{Response,IntoResponse},routing::post,Json,body::{Body,to_bytes}};
use serde::{Serialize,Deserialize};
use tokio::sync::Mutex;
use uuid::Uuid;
#[derive(Clone,Serialize,Deserialize)]
struct Connection {endpoint:String,token:String}
struct StateData {upstream:Connection,token:String,code:String,expires:Instant,attempts:Mutex<(u8,bool)>,client:reqwest::Client}
type Shared=Arc<StateData>;
pub fn private_address(ip:Ipv4Addr)->bool {let a=ip.octets();a[0]==10||(a[0]==172&&(16..=31).contains(&a[1]))||(a[0]==192&&a[1]==168)}
fn gateway(upstream:Connection)->Shared {
    Arc::new(StateData{upstream,token:Uuid::new_v4().to_string(),code:format!("{:08}",Uuid::new_v4().as_u128()%100_000_000),expires:Instant::now()+Duration::from_secs(300),attempts:Mutex::new((0,false)),client:reqwest::Client::builder().no_proxy().redirect(reqwest::redirect::Policy::none()).timeout(Duration::from_secs(25)).build().unwrap()})
}
#[derive(Deserialize)] struct Pair {code:String}
async fn pair(State(s):State<Shared>,headers:HeaderMap,Json(p):Json<Pair>)->Response {
    if headers.contains_key("origin"){return StatusCode::UNAUTHORIZED.into_response();}
    let mut a=s.attempts.lock().await;
    if a.1||a.0>=5||Instant::now()>=s.expires{return StatusCode::GONE.into_response();}
    if p.code!=s.code{a.0+=1;return StatusCode::UNAUTHORIZED.into_response();}
    a.1=true;Json(serde_json::json!({"token":s.token})).into_response()
}
fn allowed(method:&str,path:&str)->bool {
    match (method,path){("GET","/health"|"/history"|"/events"|"/asr")|("POST","/session"|"/turn"|"/stop"|"/asr")=>true,
    ("GET",p)=>p.strip_prefix("/audio/").and_then(|p|p.strip_suffix(".wav")).is_some_and(|p|Uuid::parse_str(p).is_ok()),_=>false}
}
async fn forward(State(s):State<Shared>,req:Request)->Response {
    if req.headers().contains_key("origin")||req.headers().get("authorization").and_then(|h|h.to_str().ok())!=Some(&format!("Bearer {}",s.token)){return StatusCode::UNAUTHORIZED.into_response();}
    if !allowed(req.method().as_str(),req.uri().path()){return StatusCode::NOT_FOUND.into_response();}
    let method=match reqwest::Method::from_bytes(req.method().as_str().as_bytes()){Ok(m)=>m,Err(_)=>return StatusCode::METHOD_NOT_ALLOWED.into_response()};
    let uri=req.uri().path_and_query().unwrap().as_str().to_owned();
    let is_audio=req.method()=="POST"&&req.uri().path()=="/asr";
    let cloud_asr=is_audio&&req.headers().get("x-xr-asr-cloud").and_then(|h|h.to_str().ok())==Some("1");
    if is_audio&&req.headers().get("content-type").and_then(|h|h.to_str().ok())!=Some("audio/wav"){return StatusCode::UNSUPPORTED_MEDIA_TYPE.into_response();}
    let body=match to_bytes(req.into_body(),if is_audio{crate::asr::MAX_WAV}else{16384}).await{Ok(b)=>b,Err(_)=>return StatusCode::PAYLOAD_TOO_LARGE.into_response()};
    let mut request=s.client.request(method,format!("{}{}",s.upstream.endpoint,uri)).bearer_auth(&s.upstream.token).header("content-type",if is_audio{"audio/wav"}else{"application/json"}).body(body);
    if cloud_asr{request=request.header("x-xr-asr-cloud","1");}
    let reply=request.send().await;
    match reply {
        Ok(reply)=>{let status=reply.status().as_u16();let kind=reply.headers().get("content-type").and_then(|v|v.to_str().ok()).unwrap_or("application/json").to_owned();match reply.bytes().await{Ok(body)=>Response::builder().status(status).header("content-type",kind).header("cache-control","no-store").body(Body::from(body)).unwrap(),Err(_)=>StatusCode::BAD_GATEWAY.into_response()}},
        Err(_)=>StatusCode::BAD_GATEWAY.into_response()
    }
}
fn router(s:Shared)->Router {Router::new().route("/pair",post(pair)).fallback(forward).layer(axum::extract::DefaultBodyLimit::max(16384)).with_state(s)}
pub async fn run()->anyhow::Result<()> {
    let bind:Ipv4Addr=std::env::var("XR_PAIRING_BIND")?.parse()?;anyhow::ensure!(private_address(bind),"Choose one explicit RFC1918 IPv4 interface; wildcard/public/loopback not allowed");
    let path=PathBuf::from(std::env::var("XR_PAIRING_CONNECTION")?);let upstream:Connection=serde_json::from_slice(&std::fs::read(path)?)?;
    let uri:reqwest::Url=upstream.endpoint.parse()?;anyhow::ensure!(uri.scheme()=="http"&&uri.host_str()==Some("127.0.0.1")&&uri.path()=="/"&&uri.query().is_none()&&uri.username().is_empty(),"Upstream must be the existing loopback host");
    let port:u16=std::env::var("XR_PAIRING_PORT")?.parse()?;anyhow::ensure!(port>0,"Pairing port required");
    let listener=tokio::net::TcpListener::bind((bind,port)).await?;let s=gateway(upstream);
    let status=PathBuf::from(std::env::var("XR_PAIRING_STATUS")?);
    std::fs::write(status,serde_json::to_vec(&serde_json::json!({"endpoint":format!("http://{bind}:{port}"),"code":s.code,"expiresAt":(chrono::Utc::now()+chrono::Duration::minutes(5)).to_rfc3339(),"pid":std::process::id()}))?)?;
    println!("XR_PAIRING_READY: private interface only; code is in the private status file");
    axum::serve(listener,router(s)).await?;Ok(())
}
#[cfg(test)] mod tests {
    use super::*;
    #[test] fn only_explicit_private_addresses_and_expected_routes(){
        for ip in ["127.0.0.1","0.0.0.0","8.8.8.8","169.254.1.1","172.32.0.1"]{assert!(!private_address(ip.parse().unwrap()));}
        for ip in ["10.0.0.5","192.168.1.5","172.16.0.5","172.31.255.1"]{assert!(private_address(ip.parse().unwrap()));}
        assert!(!allowed("POST","/reload-settings"));assert!(!allowed("GET","/audio/../settings.json"));assert!(!allowed("GET","http://example.com"));assert!(allowed("POST","/turn"));
    }
    async fn serve(app:Router)->(String,tokio::task::JoinHandle<()>) {let listener=tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();let address=format!("http://{}",listener.local_addr().unwrap());let task=tokio::spawn(async move{axum::serve(listener,app).await.unwrap()});(address,task)}
    #[tokio::test] async fn pairing_authentication_proxy_and_revoke_on_restart(){
        let upstream=Router::new().route("/health",axum::routing::get(|headers:HeaderMap|async move{assert_eq!(headers["authorization"],"Bearer upstream-private");Json(serde_json::json!({"ready":true}))}))
            .route("/turn",post(|Json(body):Json<serde_json::Value>|async move{Json(body)}))
            .route("/asr",axum::routing::get(||async{Json(serde_json::json!({"configured":true}))}).post(|headers:HeaderMap,body:axum::body::Bytes|async move{assert_eq!(headers["content-type"],"audio/wav");assert_eq!(headers["authorization"],"Bearer upstream-private");assert_eq!(body.len(),32044);Json(serde_json::json!({"text":"识别文字"}))}))
            .route("/audio/00000000-0000-0000-0000-000000000001.wav",axum::routing::get(||async {([("content-type","audio/wav")],b"RIFF fixture".to_vec())}));
        let (url,worker)=serve(upstream).await;let s=gateway(Connection{endpoint:url,token:"upstream-private".into()});let (url,proxy)=serve(router(s.clone())).await;
        let c=reqwest::Client::builder().no_proxy().build().unwrap();
        assert_eq!(c.get(format!("{url}/health")).send().await.unwrap().status(),401);
        assert_eq!(c.post(format!("{url}/pair")).json(&serde_json::json!({"code":"wrong"})).send().await.unwrap().status(),401);
        assert_eq!(c.post(format!("{url}/pair")).header("origin","https://example.com").json(&serde_json::json!({"code":s.code})).send().await.unwrap().status(),401);
        let paired:serde_json::Value=c.post(format!("{url}/pair")).json(&serde_json::json!({"code":s.code})).send().await.unwrap().json().await.unwrap();let token=paired["token"].as_str().unwrap();assert_ne!(token,"upstream-private");
        assert_eq!(c.post(format!("{url}/pair")).json(&serde_json::json!({"code":s.code})).send().await.unwrap().status(),410);
        assert!(c.get(format!("{url}/health")).bearer_auth(token).send().await.unwrap().json::<serde_json::Value>().await.unwrap()["ready"].as_bool().unwrap());
        assert_eq!(c.post(format!("{url}/reload-settings")).bearer_auth(token).send().await.unwrap().status(),404);
        let payload=serde_json::json!({"sessionId":"fixture","text":"中文请求保持原样"});
        assert_eq!(c.post(format!("{url}/turn")).bearer_auth(token).json(&payload).send().await.unwrap().json::<serde_json::Value>().await.unwrap(),payload);
        let audio=c.get(format!("{url}/audio/00000000-0000-0000-0000-000000000001.wav")).bearer_auth(token).send().await.unwrap();assert_eq!(audio.headers()["content-type"],"audio/wav");assert_eq!(audio.bytes().await.unwrap().as_ref(),b"RIFF fixture");
        assert_eq!(c.post(format!("{url}/turn")).bearer_auth(token).body(vec![0;20000]).send().await.unwrap().status(),413);
        assert_eq!(c.post(format!("{url}/asr")).bearer_auth(token).header("content-type","audio/wav").body(vec![0;32044]).send().await.unwrap().json::<serde_json::Value>().await.unwrap()["text"],"识别文字");
        assert_eq!(c.post(format!("{url}/asr")).bearer_auth(token).header("content-type","audio/wav").body(vec![0;crate::asr::MAX_WAV+1]).send().await.unwrap().status(),413);
        assert_eq!(c.post(format!("{url}/asr")).bearer_auth(token).json(&serde_json::json!({})).send().await.unwrap().status(),415);
        let replacement=gateway(s.upstream.clone());assert_ne!(replacement.token,token);
        let (url2,p2)=serve(router(replacement)).await;assert_eq!(c.get(format!("{url2}/health")).bearer_auth(token).send().await.unwrap().status(),401);
        proxy.abort();p2.abort();worker.abort();
    }
    #[tokio::test] async fn expired_and_guessed_codes_are_closed(){
        for expire in [true,false] {
            let mut s=gateway(Connection{endpoint:"http://127.0.0.1:1".into(),token:"fixture".into()});
            if expire{Arc::get_mut(&mut s).unwrap().expires=Instant::now()-Duration::from_secs(1);}else{*s.attempts.lock().await=(5,false);}
            assert_eq!(pair(State(s.clone()),HeaderMap::new(),Json(Pair{code:s.code.clone()})).await.status(),410);
        }
    }
}
