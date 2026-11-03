// 故障注入只用于协议回归；真实服务验收由 verify-live.ps1 单独记录。
use super::*;
use crate::ai_service::llm::{LlmConfig,LlmProvider,ChunkStream};
use std::sync::atomic::{AtomicUsize,Ordering};
use async_trait::async_trait;
use tokio::sync::{RwLock,Notify};
struct TextFixture { replies: std::sync::Mutex<Vec<String>> }
#[async_trait] impl LlmProvider for TextFixture {
    async fn complete(&self,_:&reqwest::Client,_:&[LlmMessage])->anyhow::Result<String>{Ok(self.replies.lock().unwrap().remove(0))}
    async fn complete_stream(&self,_:&reqwest::Client,_:&[LlmMessage])->anyhow::Result<ChunkStream>{anyhow::bail!("A voice retry must not regenerate the conversation")}
}
struct VoiceFixture { calls:AtomicUsize, fail_first:bool, gate:Option<Arc<Notify>> }
#[async_trait] impl TtsAdapter for VoiceFixture {
    async fn generate_voice(&self,_:&str,_:&str)->anyhow::Result<Vec<u8>>{
        let call=self.calls.fetch_add(1,Ordering::SeqCst);
        if let Some(gate)=&self.gate {gate.notified().await;}
        if self.fail_first&&call==0 {anyhow::bail!("Injected TTS failure")}
        Ok(b"RIFF protocol fixture only".to_vec())
    }
    fn get_params(&self)->HashMap<String,Value>{HashMap::new()}
}
async fn fixture(replies:Vec<&str>,voice:Arc<VoiceFixture>)->Shared {
    let config=LlmConfig{provider:"fixture".into(),model:"fixture".into(),api_key:"".into(),base_url:"".into(),timeout_secs:1,temperature:None,top_p:None,enable_thinking:false,reasoning_effort:None,fast_mode:false,support_vision:false};
    let client=Arc::new(LlmClient::new(config,reqwest::Client::new(),Box::new(TextFixture{replies:std::sync::Mutex::new(replies.into_iter().map(String::from).collect())})));
    let root=std::env::temp_dir().join(format!("xr-host-test-{}",Uuid::new_v4()));tokio::fs::create_dir_all(root.join("audio")).await.unwrap();
    let mut sessions=HashMap::new();
    for role in ["feiyu","ling-demo"] {sessions.insert(role.into(),Session{id:role.into(),role_id:role.into(),generation:1,history:vec![],sentences:vec![],events:vec![],cancel:None,busy:true});}
    Arc::new(Host{store_writer:Mutex::new(()),voice_queue:Default::default(),token:"fixture-only".into(),root,llm:RwLock::new(client.clone()),settings_path:None,translator:Translator::new(Arc::new(RwLock::new(Some(client))),true),voice,sessions:Mutex::new(sessions)})
}
fn request()->Turn {Turn{session_id:"feiyu".into(),text:"".into(),retry_turn:"".into(),retry_index:0,test_voice:false,director:false,available:vec![],style:"normal".into(),previous_pose:String::new()}}

struct DirectorFixture { gate:Arc<Notify>, calls:Arc<AtomicUsize> }
#[async_trait] impl LlmProvider for DirectorFixture {
    async fn complete(&self,_:&reqwest::Client,_:&[LlmMessage])->anyhow::Result<String>{anyhow::bail!("not used")}
    async fn complete_stream(&self,_:&reqwest::Client,_:&[LlmMessage])->anyhow::Result<ChunkStream>{
        Ok(Box::pin(futures_util::stream::iter(vec![Ok(LlmChunk::Content("【开心】你好。<こんにちは。>【开心】再见。<またね。>".into()))])))
    }
    async fn complete_with_tools(&self,_:&reqwest::Client,_:&[LlmMessage],tools:&[crate::ai_service::types::ToolDefinition],_:Option<&str>)->anyhow::Result<crate::ai_service::llm::LlmResponseWithTools>{
        assert_eq!(tools[0].function.name,"select_stage_action");self.calls.fetch_add(1,Ordering::SeqCst);self.gate.notified().await;
        Ok(crate::ai_service::llm::LlmResponseWithTools{content:None,usage:None,tool_calls:Some(vec![crate::ai_service::types::ToolCall{id:"fixture".into(),type_:"function".into(),function:crate::ai_service::types::FunctionCall{name:"select_stage_action".into(),arguments:r#"{"action":"nod","reason":"当前执行"}"#.into()}}])})
    }
}
#[tokio::test] async fn director_does_not_delay_audio_and_late_results_are_cancelled(){
    for cancel in [false,true] {
        let h=fixture(vec![],Arc::new(VoiceFixture{calls:AtomicUsize::new(0),fail_first:false,gate:None})).await;
        let gate=Arc::new(Notify::new());let calls=Arc::new(AtomicUsize::new(0));
        let client=Arc::new(LlmClient::new(h.llm.read().await.config().clone(),reqwest::Client::new(),Box::new(DirectorFixture{gate:gate.clone(),calls:calls.clone()})));
        *h.llm.write().await=client;let mut req=request();req.director=true;req.available=vec![crate::stage_action::Candidate{id:"nod".into(),label:"点头".into(),persistent:false}];
        let worker=h.clone();let task=tokio::spawn(async move{run(worker,"feiyu".into(),1,"director".into(),"feiyu".into(),vec![],req,None).await;});
        tokio::time::timeout(std::time::Duration::from_secs(3),async {loop{if h.sessions.lock().await["feiyu"].events.iter().filter(|e|e.kind=="audio").count()==2&&calls.load(Ordering::SeqCst)==1{break;}tokio::task::yield_now().await;}}).await.expect("Director blocked TTS fragments");
        assert!(!task.is_finished());if cancel{h.sessions.lock().await.get_mut("feiyu").unwrap().generation+=1;}
        gate.notify_one();task.await.unwrap();let all=h.sessions.lock().await;
        let decisions:Vec<_>=all["feiyu"].events.iter().filter(|e|e.kind=="stageAction").collect();
        assert_eq!(decisions.len(),if cancel{0}else{1});if !cancel{assert_eq!(decisions[0].stage_action.as_ref().unwrap().action,"nod");assert_eq!(decisions[0].stage_request_id,"director:0");}
    }
}
#[tokio::test] async fn private_settings_reload_is_authenticated_atomic_and_refuses_busy_sessions(){
    let voice=Arc::new(VoiceFixture{calls:AtomicUsize::new(0),fail_first:false,gate:None});
    let mut h=fixture(vec![],voice).await;
    let path=h.root.join("xr-settings.json");Arc::get_mut(&mut h).unwrap().settings_path=Some(path.clone());
    let mut headers=HeaderMap::new();headers.insert("authorization","Bearer fixture-only".parse().unwrap());
    assert_eq!(reload_settings(State(h.clone()),HeaderMap::new()).await.unwrap_err().0,StatusCode::UNAUTHORIZED);
    assert_eq!(reload_settings(State(h.clone()),headers.clone()).await.unwrap_err().0,StatusCode::CONFLICT);
    for session in h.sessions.lock().await.values_mut(){session.busy=false;}
    std::fs::write(&path,b"{invalid").unwrap();
    assert_eq!(reload_settings(State(h.clone()),headers.clone()).await.unwrap_err().0,StatusCode::BAD_REQUEST);
    assert_eq!(h.llm.read().await.config().model,"fixture");
    let config=json!({"llm.providers":[{"id":"new","provider":"openai","model":"new-fixture","api_key":"never-return-this-key"}],"llm.chat_provider_id":"new","llm.translate_provider_id":"new"});
    std::fs::write(&path,serde_json::to_vec(&config).unwrap()).unwrap();
    let reply=reload_settings(State(h.clone()),headers).await.unwrap().0;
    assert_eq!(reply,json!({"applied":true}));assert!(!reply.to_string().contains("never-return"));
    assert_eq!(h.llm.read().await.config().model,"new-fixture");
    assert_eq!(h.translator.slot().read().await.as_ref().unwrap().config().model,"new-fixture");
}
struct StreamingFixture { gate:Arc<Notify> }
#[async_trait] impl LlmProvider for StreamingFixture {
    async fn complete(&self,_:&reqwest::Client,_:&[LlmMessage])->anyhow::Result<String>{anyhow::bail!("stream only")}
    async fn complete_stream(&self,_:&reqwest::Client,_:&[LlmMessage])->anyhow::Result<ChunkStream>{
        let gate=self.gate.clone();
        Ok(Box::pin(futures_util::stream::unfold((0,gate),|(stage,gate)|async move {
            let text=match stage {0=>"【开心】你好（挥手）<こん",1=>"にちは。>",2=>{gate.notified().await;"【正常】再见。<またね。>"},_=>return None};
            Some((Ok(LlmChunk::Content(text.into())),(stage+1,gate)))
        })))
    }
}
#[tokio::test] async fn first_sentence_synthesizes_before_llm_finishes_and_preserves_metadata(){
    let voice=Arc::new(VoiceFixture{calls:AtomicUsize::new(0),fail_first:false,gate:None});
    let h=fixture(vec![],voice.clone()).await;let gate=Arc::new(Notify::new());
    let client=Arc::new(LlmClient::new(h.llm.read().await.config().clone(),reqwest::Client::new(),Box::new(StreamingFixture{gate:gate.clone()})));
    *h.llm.write().await=client;
    let worker=h.clone();let task=tokio::spawn(async move {run(worker,"feiyu".into(),1,"streaming".into(),"feiyu".into(),vec![],request(),None).await;});
    tokio::time::timeout(std::time::Duration::from_secs(3),async{loop {
        if h.sessions.lock().await["feiyu"].events.iter().any(|e|e.kind=="audio"){break;}
        tokio::task::yield_now().await;
    }}).await.expect("first audio blocked until full LLM response");
    assert!(!task.is_finished());
    {let all=h.sessions.lock().await;let event=all["feiyu"].events.iter().find(|e|e.kind=="audio").unwrap();assert_eq!(event.message,"你好");assert_eq!(event.emotion,"开心");assert_eq!(event.motion,"挥手");}
    gate.notify_one();task.await.unwrap();
    let all=h.sessions.lock().await;let audio:Vec<_>=all["feiyu"].events.iter().filter(|e|e.kind=="audio").collect();assert_eq!(audio.len(),2);assert_eq!(audio[1].index,1);
    assert_eq!(voice.calls.load(Ordering::SeqCst),2);
}
async fn replay(h:Shared,g:u64,turn:&str,s:Sentence){run(h,"feiyu".into(),g,turn.into(),"feiyu".into(),vec![],request(),Some(s)).await;}
#[tokio::test] async fn translation_failure_preserves_chinese_and_retry_recovers(){
    let voice=Arc::new(VoiceFixture{calls:AtomicUsize::new(0),fail_first:false,gate:None});
    let h=fixture(vec!["<余分です><多すぎます>","<余分です><多すぎます>","<こんにちは>"],voice.clone()).await;
    replay(h.clone(),1,"a",Sentence{message:"你好".into(),..Default::default()}).await;
    let saved=h.sessions.lock().await["feiyu"].sentences[0].clone();
    assert_eq!(saved.message,"你好");assert!(saved.audio_file.is_empty());assert_eq!(voice.calls.load(Ordering::SeqCst),0);
    h.sessions.lock().await.get_mut("feiyu").unwrap().generation=2;
    replay(h.clone(),2,"b",saved).await;
    let all=h.sessions.lock().await;
    assert!(all["feiyu"].events.iter().any(|e|e.kind=="sentenceError"));
    assert!(all["feiyu"].events.iter().any(|e|e.kind=="audio"&&e.generation==2));
    assert!(all["ling-demo"].events.is_empty());assert!(all["feiyu"].history.is_empty());
}
#[tokio::test] async fn translation_format_repair_does_not_duplicate_synthesis(){
    let voice=Arc::new(VoiceFixture{calls:AtomicUsize::new(0),fail_first:false,gate:None});
    let h=fixture(vec!["<こんにちは><またね>","<こんにちは。またね。>"],voice.clone()).await;
    replay(h.clone(),1,"repair",Sentence{message:"你好。再见。".into(),..Default::default()}).await;
    let all=h.sessions.lock().await;
    assert_eq!(voice.calls.load(Ordering::SeqCst),1);
    assert!(all["feiyu"].events.iter().any(|e|e.kind=="audio"));
    assert!(!all["feiyu"].events.iter().any(|e|e.kind=="sentenceError"));
}
#[tokio::test] async fn tts_failure_retry_and_cached_retry_do_not_repeat_llm_or_history(){
    let voice=Arc::new(VoiceFixture{calls:AtomicUsize::new(0),fail_first:true,gate:None});
    let h=fixture(vec![],voice.clone()).await;
    replay(h.clone(),1,"a",Sentence{message:"你好".into(),tts_text:"こんにちは".into(),..Default::default()}).await;
    let saved=h.sessions.lock().await["feiyu"].sentences[0].clone();
    h.sessions.lock().await.get_mut("feiyu").unwrap().generation=2;
    replay(h.clone(),2,"b",saved).await;
    let saved=h.sessions.lock().await["feiyu"].sentences[1].clone();assert!(!saved.audio_file.is_empty());
    h.sessions.lock().await.get_mut("feiyu").unwrap().generation=3;
    replay(h.clone(),3,"c",saved).await;
    assert_eq!(voice.calls.load(Ordering::SeqCst),2);assert!(h.sessions.lock().await["feiyu"].history.is_empty());
}
#[tokio::test] async fn stop_during_synthesis_discards_late_audio_and_old_finish(){
    let gate=Arc::new(Notify::new());let voice=Arc::new(VoiceFixture{calls:AtomicUsize::new(0),fail_first:false,gate:Some(gate.clone())});
    let h=fixture(vec![],voice.clone()).await;let worker=h.clone();
    let task=tokio::spawn(async move {replay(worker,1,"old",Sentence{message:"你好".into(),tts_text:"こんにちは".into(),..Default::default()}).await;});
    while voice.calls.load(Ordering::SeqCst)==0 {tokio::task::yield_now().await;}
    h.sessions.lock().await.get_mut("feiyu").unwrap().generation=2;
    gate.notify_one();task.await.unwrap();h.finish("feiyu",1,"old").await;
    let all=h.sessions.lock().await;assert!(all["feiyu"].busy);assert!(!all["feiyu"].events.iter().any(|e|e.kind=="audio"||e.kind=="streamEnd"));assert!(all["feiyu"].sentences.is_empty());
}

#[tokio::test]
async fn full_voice_queue_rejects_overflow_and_skips_cancelled_jobs() {
    let voice=Arc::new(VoiceFixture{calls:AtomicUsize::new(0),fail_first:false,gate:None});
    let h=fixture(vec![],voice.clone()).await;
    let engine=h.voice_queue.engine.lock().await;
    let mut waiting=Vec::new();
    for i in 0..8 {
        let host=h.clone();
        waiting.push(tokio::spawn(async move {
            let mut sentence=Sentence{index:i,tts_text:"こんにちは".into(),..Default::default()};
            synthesize(&host,"feiyu",1,"old",&mut sentence,false).await;
        }));
    }
    tokio::time::timeout(std::time::Duration::from_secs(3),async {
        while h.voice_queue.slots.available_permits()!=0 {tokio::task::yield_now().await;}
    }).await.unwrap();
    let mut next=Sentence{index:8,tts_text:"こんにちは".into(),..Default::default()};
    synthesize(&h,"feiyu",1,"old",&mut next,false).await;
    assert!(h.sessions.lock().await["feiyu"].events.iter().any(|e|e.kind=="sentenceError" && e.message.contains("队列已满")));
    h.sessions.lock().await.get_mut("feiyu").unwrap().generation=2;
    drop(engine);
    for task in waiting {task.await.unwrap();}
    assert_eq!(voice.calls.load(Ordering::SeqCst),0);
    synthesize(&h,"feiyu",2,"new",&mut next,false).await;
    assert_eq!(voice.calls.load(Ordering::SeqCst),1);
    assert!(!next.audio_file.is_empty());
}

#[tokio::test]
async fn stopping_blocked_turn_releases_voice_lane_for_next_turn() {
    let gate=Arc::new(Notify::new());
    let voice=Arc::new(VoiceFixture{calls:AtomicUsize::new(0),fail_first:false,gate:Some(gate.clone())});
    let h=fixture(vec![],voice.clone()).await;
    h.sessions.lock().await.get_mut("feiyu").unwrap().busy=false;
    let mut headers=HeaderMap::new();
    headers.insert("authorization","Bearer fixture-only".parse().unwrap());
    for expected in 1..=3 {
        let mut req=request();req.test_voice=true;
        let _=turn(State(h.clone()),headers.clone(),Json(req)).await.unwrap();
        tokio::time::timeout(std::time::Duration::from_secs(3),async {
            while voice.calls.load(Ordering::SeqCst)<expected {tokio::task::yield_now().await;}
        }).await.unwrap();
        if expected<3 {
            let _=stop(State(h.clone()),headers.clone(),Json(StopSession{session_id:"feiyu".into(),expected_generation:None})).await.unwrap();
        } else {gate.notify_one();}
    }
    tokio::time::timeout(std::time::Duration::from_secs(3),async {
        while h.sessions.lock().await["feiyu"].busy {tokio::task::yield_now().await;}
    }).await.unwrap();
    let all=h.sessions.lock().await;
    assert_eq!(all["feiyu"].events.iter().filter(|e|e.kind=="audio").count(),1);
}

#[tokio::test]
async fn expired_cursor_returns_history_instead_of_incomplete_event_stream() {
    let voice=Arc::new(VoiceFixture{calls:AtomicUsize::new(0),fail_first:false,gate:None});
    let h=fixture(vec![],voice).await;
    for _ in 0..1026 {h.event("feiyu",1,"turn",0,"text","正文","").await;}
    let mut headers=HeaderMap::new();
    headers.insert("authorization","Bearer fixture-only".parse().unwrap());
    for (after,gap) in [(0,true),(1,true),(2,false),(1026,false),(1027,true)] {
        let Json(reply)=events(State(h.clone()),headers.clone(),Query(OpenSession{session_id:"feiyu".into(),after})).await.unwrap();
        assert_eq!(reply["cursorExpired"].as_bool().unwrap_or(false),gap);
        assert_eq!(reply["oldestCursor"],3);
        if gap {assert!(reply["history"].is_array());assert_eq!(reply["events"].as_array().unwrap().len(),0);}
    }
}
