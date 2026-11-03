//! LingChat 本地 llama-asr / DashScope 整句协议的 XR 适配；不启动模型、不写配置、不自动发聊天。
//! 来源 providers/llama.rs 的 multipart 与 parse_llama_text，沿用 AGPL-3.0-only。
use std::{path::Path, sync::Arc, time::Duration};
use axum::{body::Bytes, extract::{State, DefaultBodyLimit}, http::{HeaderMap, StatusCode}, routing::get, Json, Router};
use serde_json::{json, Value};
use tokio::sync::Semaphore;

pub const MAX_WAV: usize = 44 + 16000 * 2 * 30;
#[derive(Clone)]
struct Config { endpoint: String, model: String, key: String, cloud: bool }
pub struct Service { token: String, config: Option<Config>, client: reqwest::Client, slot: Semaphore }
type Shared = Arc<Service>;
type Reply = Result<Json<Value>, (StatusCode, &'static str)>;

fn config(value: &Value) -> Option<Config> {
    if value.get("ASR_PREFS").and_then(|p|p.get("voice_input_enabled")).and_then(Value::as_bool)==Some(false){return None;}
    let provider=value.get("ASR_ACTIVE_PROVIDER_ID")?.as_str()?;
    if provider=="qwen-asr" {
        use crate::asr_source::{region::DashScopeRegion,qwen_models::{qwen_default_model,qwen_is_streaming_model},provider_types::ProviderCredentials};
        let p=value.get("ASR_PROVIDERS")?.get(provider)?;
        let get=|name|p.get(name).and_then(Value::as_str).unwrap_or("").trim().to_string();
        let raw_endpoint=get("endpoint");let raw_region=get("region");
        let region=if raw_region.is_empty(){DashScopeRegion::from_endpoint_hint(&raw_endpoint).unwrap_or(DashScopeRegion::DEFAULT)}else{DashScopeRegion::parse(&raw_region)};
        let key=p.get("api_keys").and_then(|v|v.get(region.id())).and_then(Value::as_str).filter(|s|!s.trim().is_empty()).map(str::to_string).unwrap_or_else(||get("api_key"));
        if key.trim().is_empty(){return None;}
        let credentials=ProviderCredentials{endpoint:raw_endpoint,region:region.id().into(),..Default::default()};
        let endpoint=credentials.effective_http_endpoint();let uri=reqwest::Url::parse(&endpoint).ok()?;
        // 云端 Key 只交给既有 HTTPS 配置，不允许重定向或 URL 内嵌凭据。
        if uri.scheme()!="https"||!uri.username().is_empty()||uri.password().is_some()||uri.fragment().is_some(){return None;}
        let mut model=get("model");if model.is_empty()||qwen_is_streaming_model(&model){model=qwen_default_model(false,region).into();}
        return Some(Config{endpoint,model,key,cloud:true});
    }
    if provider!="llama-asr" { return None; }
    let p = value.get("ASR_PROVIDERS")?.get("llama-asr")?;
    let raw = p.get("endpoint").and_then(Value::as_str).unwrap_or("").trim().trim_end_matches('/');
    let endpoint = if raw.is_empty() { "http://127.0.0.1:8080" } else { raw };
    let url = reqwest::Url::parse(endpoint).ok()?;
    if url.scheme() != "http" || !matches!(url.host_str(), Some("127.0.0.1" | "[::1]" | "localhost")) || url.path() != "/" || url.query().is_some() || url.fragment().is_some() || !url.username().is_empty() || url.password().is_some() { return None; }
    let model = p.get("model").and_then(Value::as_str).unwrap_or("").trim();
    Some(Config { endpoint: endpoint.into(), model: if model.is_empty() { "models/Qwen3-ASR-1.7B-Q8_0.gguf" } else { model }.into(), key: p.get("api_key").and_then(Value::as_str).unwrap_or("").into(), cloud:false })
}
pub fn router(token: String, path: &Path) -> Router {
    let cfg = std::fs::read(path).ok().and_then(|b| serde_json::from_slice::<Value>(&b).ok()).and_then(|v| config(&v));
    routes(service(token, cfg))
}
fn service(token: String, config: Option<Config>) -> Shared {
    Arc::new(Service { token, config, client: reqwest::Client::builder().no_proxy().redirect(reqwest::redirect::Policy::none()).timeout(Duration::from_secs(20)).build().unwrap(), slot: Semaphore::new(1) })
}
fn routes(s: Shared) -> Router {
    Router::new().route("/asr", get(status).post(recognize)).layer(DefaultBodyLimit::max(MAX_WAV)).with_state(s)
}
fn auth(s: &Service, h: &HeaderMap) -> Result<(), (StatusCode, &'static str)> {
    if h.contains_key("origin") || h.get("authorization").and_then(|v| v.to_str().ok()) != Some(&format!("Bearer {}", s.token)) { return Err((StatusCode::UNAUTHORIZED, "Unauthorized")); }
    Ok(())
}
async fn status(State(s): State<Shared>, headers: HeaderMap) -> Reply {
    auth(&s, &headers)?;
    Ok(Json(json!({"configured": s.config.is_some(), "cloud": s.config.as_ref().is_some_and(|c|c.cloud), "maxSeconds": 30})))
}
// 固定来自 Unity 的 PCM16/16k/mono WAV；拒绝伪头、截断、过长与非有限时长。
fn valid_wav(b: &[u8]) -> bool {
    if b.len() < 44 + 3200 || b.len() > MAX_WAV || (b.len()-44)%2 != 0 { return false; }
    let u16_at = |n| u16::from_le_bytes(b[n..n+2].try_into().unwrap());
    let u32_at = |n| u32::from_le_bytes(b[n..n+4].try_into().unwrap());
    &b[..4] == b"RIFF" && u32_at(4) as usize == b.len()-8 && &b[8..16] == b"WAVEfmt " && u32_at(16) == 16 && u16_at(20) == 1 && u16_at(22) == 1 && u32_at(24) == 16000 && u32_at(28) == 32000 && u16_at(32) == 2 && u16_at(34) == 16 && &b[36..40] == b"data" && u32_at(40) as usize == b.len()-44
}
// 与 LingChat parse_llama_text 相同的 Qwen3-ASR 标记处理。
fn parse_text(body: &str) -> Option<String> {
    let v: Value = serde_json::from_str(body).ok()?;
    let raw = v.get("text")?.as_str()?;
    Some(raw.split_once("<asr_text>").map_or(raw, |(_, text)| text).trim().to_string())
}
async fn recognize(State(s): State<Shared>, headers: HeaderMap, body: Bytes) -> Reply {
    auth(&s, &headers)?;
    let cfg = s.config.as_ref().ok_or((StatusCode::SERVICE_UNAVAILABLE, "Local ASR is not configured"))?;
    if cfg.cloud&&headers.get("x-xr-asr-cloud").and_then(|h|h.to_str().ok())!=Some("1"){return Err((StatusCode::PRECONDITION_FAILED,"Refresh ASR capability before using the configured cloud provider"));}
    if headers.get("content-type").and_then(|h| h.to_str().ok()) != Some("audio/wav") || !valid_wav(&body) { return Err((StatusCode::BAD_REQUEST, "Expected PCM16 mono 16000Hz WAV, 0.1 to 30 seconds")); }
    let _permit = s.slot.try_acquire().map_err(|_| (StatusCode::CONFLICT, "ASR busy"))?;
    let mut request = if cfg.cloud {
        use base64::Engine as _;
        let data_url=format!("data:audio/wav;base64,{}",base64::engine::general_purpose::STANDARD.encode(&body));
        s.client.post(&cfg.endpoint).header("X-DashScope-SSE","disable").json(&crate::asr_source::qwen_batch::build_batch_body(&cfg.model,&data_url,&[]))
    } else {
        let form = reqwest::multipart::Form::new().text("model", cfg.model.clone()).text("response_format", "json")
            .part("file", reqwest::multipart::Part::bytes(body.to_vec()).file_name("audio.wav").mime_str("audio/wav").unwrap());
        s.client.post(format!("{}/v1/audio/transcriptions", cfg.endpoint)).multipart(form)
    };
    if !cfg.key.trim().is_empty() { request = request.bearer_auth(&cfg.key); }
    let response = request.send().await.map_err(|_| (StatusCode::BAD_GATEWAY, "Local ASR unavailable"))?;
    if !response.status().is_success() { return Err((StatusCode::BAD_GATEWAY, "Local ASR failed")); }
    // 不把服务错误正文、地址、模型路径或 key 返回设备。
    if response.content_length().is_some_and(|n| n > 65536) { return Err((StatusCode::BAD_GATEWAY, "ASR response too large")); }
    use futures_util::StreamExt;
    let mut stream = response.bytes_stream(); let mut bytes = Vec::new();
    while let Some(chunk) = stream.next().await { let chunk = chunk.map_err(|_| (StatusCode::BAD_GATEWAY, "ASR response failed"))?; if bytes.len()+chunk.len()>65536 { return Err((StatusCode::BAD_GATEWAY, "ASR response too large")); } bytes.extend_from_slice(&chunk); }
    let text = std::str::from_utf8(&bytes).ok().and_then(|body|if cfg.cloud {crate::asr_source::qwen_batch::parse_qwen_text(body)}else{parse_text(body)}).ok_or((StatusCode::BAD_GATEWAY, "Invalid ASR response"))?;
    if text.chars().count() > 2000 { return Err((StatusCode::BAD_GATEWAY, "ASR text too long")); }
    Ok(Json(json!({"text": text})))
}

#[cfg(test)] mod tests {
    use super::*;
    fn wave() -> Vec<u8> {
        let mut b=Vec::new();b.extend(b"RIFF");b.extend(32036u32.to_le_bytes());b.extend(b"WAVEfmt ");b.extend(16u32.to_le_bytes());
        for n in [1u16,1]{b.extend(n.to_le_bytes());}b.extend(16000u32.to_le_bytes());b.extend(32000u32.to_le_bytes());
        for n in [2u16,16]{b.extend(n.to_le_bytes());}b.extend(b"data");b.extend(32000u32.to_le_bytes());b.resize(32044,0);b
    }
    #[test] fn local_configuration_and_llama_text_contract() {
        let mut v=json!({"ASR_ACTIVE_PROVIDER_ID":"llama-asr","ASR_PROVIDERS":{"llama-asr":{"endpoint":"http://127.0.0.1:8080","model":"actual-model-name","api_key":"private"}}});
        let c=config(&v).unwrap();assert_eq!(c.model,"actual-model-name");assert_eq!(c.key,"private");
        for endpoint in ["https://cloud.example","http://192.168.1.3:8080","http://127.0.0.1:80/path","http://secret@127.0.0.1:8080"]{v["ASR_PROVIDERS"]["llama-asr"]["endpoint"]=json!(endpoint);assert!(config(&v).is_none());}
        v["ASR_ACTIVE_PROVIDER_ID"]=json!("qwen");assert!(config(&v).is_none());
        assert_eq!(parse_text(r#"{"text":"language Chinese<asr_text>你好"}"#).unwrap(),"你好");
        assert_eq!(parse_text(r#"{"text":"language None<asr_text>"}"#).unwrap(),"");
        assert_eq!(parse_text(r#"{"text":"普通文字"}"#).unwrap(),"普通文字");assert!(parse_text("bad").is_none());
    }
    #[test] fn wav_format_and_duration_bounds() {
        let b=wave();assert!(valid_wav(&b));assert!(!valid_wav(&b[..b.len()-2]));assert!(!valid_wav(&vec![0;MAX_WAV+1]));
        for (offset,value) in [(22,2),(24,0),(34,32),(40,1)]{let mut bad=b.clone();bad[offset]=value;assert!(!valid_wav(&bad));}
    }
    #[test] fn cloud_uses_existing_regional_key_and_batch_model() {
        let settings=json!({"ASR_ACTIVE_PROVIDER_ID":"qwen-asr","ASR_PROVIDERS":{"qwen-asr":{"region":"ap-southeast-1","api_keys":{"cn-beijing":"cn-key","ap-southeast-1":"sg-key"},"api_key":"legacy","model":"paraformer-realtime-v2"}}});
        let cfg=config(&settings).unwrap();assert!(cfg.cloud);assert_eq!(cfg.key,"sg-key");assert!(cfg.endpoint.starts_with("https://dashscope-intl.aliyuncs.com/"));assert_eq!(cfg.model,"qwen-audio-3.0-asr-flash");
        let mut disabled=settings.clone();disabled["ASR_PREFS"]=json!({"voice_input_enabled":false});assert!(config(&disabled).is_none());
        let mut invalid=settings.clone();invalid["ASR_PROVIDERS"]["qwen-asr"]["endpoint"]=json!("http://public.example");assert!(config(&invalid).is_none());
        use crate::asr_source::qwen_batch::{build_batch_body,parse_qwen_text};
        assert_eq!(build_batch_body("fun-asr-realtime","data:fixture",&[])["input"]["messages"][0]["content"][0]["audio"],"data:fixture");
        for body in [r#"{"output":{"output":{"text":"你好"}}}"#,r#"{"output":{"choices":[{"message":{"content":[{"text":"你好"}]}}]}}"#]{assert_eq!(parse_qwen_text(body).unwrap(),"你好");}
    }
    #[tokio::test] async fn cloud_protocol_fixture_never_calls_cloud() {
        let upstream=Router::new().route("/recognize",axum::routing::post(|headers:HeaderMap,Json(body):Json<Value>|async move{
            use base64::Engine as _;
            assert_eq!(headers["authorization"],"Bearer fixture-secret");assert_eq!(headers["x-dashscope-sse"],"disable");
            assert_eq!(body["model"],"qwen-audio-3.0-asr-flash");let data=body["input"]["messages"][0]["content"][0]["input_audio"]["data"].as_str().unwrap();
            assert_eq!(base64::engine::general_purpose::STANDARD.decode(data.strip_prefix("data:audio/wav;base64,").unwrap()).unwrap(),wave());
            Json(json!({"output":{"output":{"text":"联网协议本机夹具"}}}))
        }));
        let (up,worker)=serve(upstream).await;let (url,host)=serve(routes(service("test".into(),Some(Config{endpoint:up+"/recognize",key:"fixture-secret".into(),model:"qwen-audio-3.0-asr-flash".into(),cloud:true})))).await;
        let client=reqwest::Client::builder().no_proxy().build().unwrap();
        let capability:Value=client.get(format!("{url}/asr")).bearer_auth("test").send().await.unwrap().json().await.unwrap();assert_eq!(capability["cloud"],true);
        assert_eq!(client.post(format!("{url}/asr")).bearer_auth("test").header("content-type","audio/wav").body(wave()).send().await.unwrap().status(),412);
        let result:Value=client.post(format!("{url}/asr")).bearer_auth("test").header("content-type","audio/wav").header("x-xr-asr-cloud","1").body(wave()).send().await.unwrap().json().await.unwrap();
        assert_eq!(result["text"],"联网协议本机夹具");host.abort();worker.abort();
    }
    async fn serve(app: Router) -> (String,tokio::task::JoinHandle<()>) {
        let listener=tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();let url=format!("http://{}",listener.local_addr().unwrap());
        (url,tokio::spawn(async move {axum::serve(listener,app).await.unwrap()}))
    }
    #[tokio::test] async fn http_auth_multipart_limits_and_failure_recovery() {
        use std::sync::atomic::{AtomicBool,Ordering};
        let fail=Arc::new(AtomicBool::new(false));
        let upstream=Router::new().route("/v1/audio/transcriptions",axum::routing::post(|State(fail):State<Arc<AtomicBool>>,headers:HeaderMap,body:Bytes|async move {
            if fail.load(Ordering::SeqCst){return (StatusCode::SERVICE_UNAVAILABLE,Json(json!({"secret":"must not forward"})));}
            assert_eq!(headers["authorization"],"Bearer private-model-key");assert!(headers["content-type"].to_str().unwrap().starts_with("multipart/form-data;"));
            let text=String::from_utf8_lossy(&body);assert!(text.contains("name=\"model\"\r\n\r\nactual-model"));assert!(text.contains("filename=\"audio.wav\""));assert!(body.windows(12).any(|w|w==b"WAVEfmt \x10\x00\x00\x00"));
            (StatusCode::OK,Json(json!({"text":"language Chinese<asr_text>这是一段识别文字。"})))
        })).with_state(fail.clone());
        let (up,up_task)=serve(upstream).await;
        let s=service("client-token".into(),Some(Config{endpoint:up,model:"actual-model".into(),key:"private-model-key".into(),cloud:false}));
        let (url,task)=serve(routes(s.clone())).await;let client=reqwest::Client::builder().no_proxy().build().unwrap();
        assert_eq!(client.get(format!("{url}/asr")).send().await.unwrap().status(),401);
        let response:Value=client.get(format!("{url}/asr")).bearer_auth("client-token").send().await.unwrap().json().await.unwrap();assert_eq!(response["configured"],true);assert!(!response.to_string().contains("private-model-key"));
        let send=|body:Vec<u8>|client.post(format!("{url}/asr")).bearer_auth("client-token").header("content-type","audio/wav").body(body);
        assert_eq!(send(wave()).header("origin","https://example.com").send().await.unwrap().status(),401);
        assert_eq!(send(vec![0;50]).send().await.unwrap().status(),400);
        assert_eq!(send(vec![0;MAX_WAV+1]).send().await.unwrap().status(),413);
        let permit=s.slot.acquire().await.unwrap();assert_eq!(send(wave()).send().await.unwrap().status(),409);drop(permit);
        let text:Value=send(wave()).send().await.unwrap().json().await.unwrap();assert_eq!(text["text"],"这是一段识别文字。");
        fail.store(true,Ordering::SeqCst);let failed=send(wave()).send().await.unwrap();assert_eq!(failed.status(),502);assert!(!failed.text().await.unwrap().contains("secret"));
        assert!(s.slot.try_acquire().is_ok());fail.store(false,Ordering::SeqCst);assert_eq!(send(wave()).send().await.unwrap().status(),200);task.abort();up_task.abort();
        let (url,task)=serve(routes(service("client-token".into(),None))).await;
        assert_eq!(client.post(format!("{url}/asr")).bearer_auth("client-token").header("content-type","audio/wav").body(wave()).send().await.unwrap().status(),503);task.abort();
    }
}
