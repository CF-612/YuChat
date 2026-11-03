use std::{collections::HashMap, path::PathBuf, sync::Arc};
use axum::{extract::{State, Query, Path}, http::{HeaderMap, StatusCode}, response::{IntoResponse, Response}, Json};
use serde::{Serialize, Deserialize};
use serde_json::{json, Value};
use tokio::sync::{Mutex,RwLock};
use uuid::Uuid;
use futures_util::StreamExt;
use crate::ai_service::{llm::{LlmClient,LlmChunk}, translator::Translator, types::LlmMessage, tts::provider::TtsAdapter, message_system::processor::EmotionSegment};
#[derive(Clone, Serialize, Deserialize, Default)]
#[serde(rename_all="camelCase")]
pub struct Sentence { pub turn_id:String, pub index:usize, pub message:String, pub tts_text:String, pub audio_file:String, #[serde(default)] pub emotion:String, #[serde(default)] pub motion:String }
#[derive(Clone, Serialize, Deserialize)]
pub struct History { pub speaker:String, pub text:String }
#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all="camelCase")]
pub struct Event { pub cursor:usize, pub session_id:String, pub role_id:String, pub turn_id:String, pub generation:u64, #[serde(default)] pub sentence_end_exclusive:usize, pub index:usize, pub kind:String, pub message:String, pub audio_file:String, pub emotion:String, pub motion:String, #[serde(default)] pub stage_action:Option<crate::stage_action::Decision>, #[serde(default)] pub stage_request_id:String }
#[derive(Serialize, Deserialize)]
#[serde(rename_all="camelCase")]
pub struct Session { pub id:String, pub role_id:String, pub generation:u64, pub history:Vec<History>, pub sentences:Vec<Sentence>, #[serde(skip)] pub events:Vec<Event>, #[serde(skip)] pub busy:bool, #[serde(skip)] cancel:Option<tokio::sync::oneshot::Sender<()>> }
pub struct VoiceQueue { slots:tokio::sync::Semaphore, engine:Mutex<()> }
impl Default for VoiceQueue {
    fn default()->Self {Self{slots:tokio::sync::Semaphore::new(8),engine:Mutex::new(())}}
}
pub struct Host { pub store_writer:Mutex<()>, pub voice_queue:VoiceQueue, pub token:String, pub root:PathBuf, pub llm:RwLock<Arc<LlmClient>>, pub settings_path:Option<PathBuf>, pub translator:Translator, pub voice:Arc<dyn TtsAdapter>, pub sessions:Mutex<HashMap<String,Session>> }
pub type Shared = Arc<Host>;
type ApiResult = Result<Json<Value>,(StatusCode,&'static str)>;
pub fn auth(h:&Host,headers:&HeaderMap)->Result<(),(StatusCode,&'static str)> {
    if headers.contains_key("origin") || headers.get("authorization").and_then(|v|v.to_str().ok()) != Some(&format!("Bearer {}",h.token)) {return Err((StatusCode::UNAUTHORIZED,"Unauthorized"));} Ok(())
}
impl Host {
    async fn current(&self,id:&str,g:u64)->bool {self.sessions.lock().await.get(id).is_some_and(|s|s.generation==g)}
    async fn event(&self,id:&str,g:u64,turn:&str,index:usize,kind:&str,message:&str,audio:&str) {
        let mut sessions=self.sessions.lock().await;
        if let Some(s)=sessions.get_mut(id).filter(|s|s.generation==g) {
            let cursor=s.events.last().map_or(1,|e|e.cursor+1);
            let sentence=s.sentences.iter().rev().find(|x|x.turn_id==turn&&x.index==index);
            let emotion=sentence.map(|x|x.emotion.clone()).unwrap_or_default();let motion=sentence.map(|x|x.motion.clone()).unwrap_or_default();
            let sentence_end_exclusive=if kind=="streamEnd" {s.sentences.iter().rev().take_while(|x|x.turn_id==turn).map(|x|x.index+1).max().unwrap_or(0)}else{0};
            s.events.push(Event{cursor,sentence_end_exclusive,session_id:id.into(),role_id:s.role_id.clone(),turn_id:turn.into(),generation:g,index,kind:kind.into(),message:message.into(),audio_file:audio.into(),emotion,motion,stage_action:None,stage_request_id:String::new()});
            if s.events.len()>1024 {s.events.remove(0);}
        }
    }
    async fn persist(&self) -> anyhow::Result<()> {
        // 先取得写入顺序再取快照，避免慢写入覆盖较新的会话。
        let _writer=self.store_writer.lock().await;
        let snapshot:HashMap<_,_>={
            let sessions=self.sessions.lock().await;
            sessions.iter().map(|(key,s)|(key.clone(),Session {
                id:s.id.clone(),role_id:s.role_id.clone(),generation:s.generation,
                history:s.history.clone(),sentences:s.sentences.clone(),
                events:Vec::new(),busy:false,cancel:None,
            })).collect()
        };
        let data=tokio::task::spawn_blocking(move ||serde_json::to_vec(&snapshot)).await??;
        let path=self.root.join("sessions.json");let temp=self.root.join("sessions.tmp");
        tokio::fs::write(&temp,data).await?;tokio::fs::rename(temp,path).await?; Ok(())
    }
    async fn finish(&self,id:&str,g:u64,turn:&str) {
        self.event(id,g,turn,0,"streamEnd","","").await;
        if let Some(s)=self.sessions.lock().await.get_mut(id).filter(|s|s.generation==g){s.busy=false;}
        if self.persist().await.is_err(){self.event(id,g,turn,0,"error","历史记录保存失败","").await;}
    }
}
#[derive(Deserialize)] #[serde(rename_all="camelCase")]
pub struct Open {role_id:String, history_limit:Option<usize>}
pub async fn open(State(h):State<Shared>,headers:HeaderMap,Json(req):Json<Open>)->ApiResult {
    auth(&h,&headers)?;
    if req.role_id!="feiyu" && req.role_id!="ling-demo" {return Err((StatusCode::BAD_REQUEST,"Unknown role"));}
    let mut all=h.sessions.lock().await;
    let id=all.values().find(|s|s.role_id==req.role_id).map(|s|s.id.clone()).unwrap_or_else(||Uuid::new_v4().to_string());
    let s=all.entry(id.clone()).or_insert_with(||Session{id,role_id:req.role_id,generation:0,history:vec![],sentences:vec![],events:vec![],cancel:None,busy:false});
    let start=req.history_limit.map_or(0,|limit|s.history.len().saturating_sub(limit.clamp(1,100)));
    Ok(Json(json!({"sessionId":s.id,"roleId":s.role_id,"generation":s.generation,"history":&s.history[start..],"historyBefore":start,"cursor":s.events.last().map_or(0,|e|e.cursor)})))
}

#[derive(Deserialize)] #[serde(rename_all="camelCase")]
pub struct HistoryPage { pub session_id:String, pub before:usize }
pub async fn history_page(State(h):State<Shared>,headers:HeaderMap,Query(req):Query<HistoryPage>)->ApiResult {
    auth(&h,&headers)?;
    let sessions=h.sessions.lock().await;
    let s=sessions.get(&req.session_id).ok_or((StatusCode::NOT_FOUND,"Session missing"))?;
    let end=req.before.min(s.history.len());
    let start=end.saturating_sub(50);
    Ok(Json(json!({"historyBefore":start,"history":&s.history[start..end]})))
}
#[derive(Deserialize)] #[serde(rename_all="camelCase")]
pub struct Turn {pub session_id:String, #[serde(default)] pub text:String, #[serde(default)] pub retry_turn:String, #[serde(default)] pub retry_index:usize, #[serde(default)] pub test_voice:bool, #[serde(default)] pub director:bool, #[serde(default)] pub available:Vec<crate::stage_action::Candidate>, #[serde(default)] pub style:String, #[serde(default)] pub previous_pose:String}
pub async fn turn(State(h):State<Shared>,headers:HeaderMap,Json(req):Json<Turn>)->ApiResult {
    auth(&h,&headers)?;
    if !crate::stage_action::valid_candidates(&req.available){return Err((StatusCode::BAD_REQUEST,"Invalid motion capabilities"));}
    if req.text.chars().count()>2000 || (req.text.trim().is_empty() && req.retry_turn.is_empty() && !req.test_voice){return Err((StatusCode::BAD_REQUEST,"Empty or oversized input"));}
    let mut all=h.sessions.lock().await;
    let s=all.get_mut(&req.session_id).ok_or((StatusCode::NOT_FOUND,"Session missing"))?;
    if s.busy {return Err((StatusCode::CONFLICT,"Session busy"));}
    let retry=if !req.retry_turn.is_empty(){Some(s.sentences.iter().find(|x|x.turn_id==req.retry_turn&&x.index==req.retry_index).cloned().ok_or((StatusCode::NOT_FOUND,"Sentence missing"))?)}else{None};
    s.generation+=1; s.busy=true;
    let g=s.generation; let id=s.id.clone(); let role=s.role_id.clone(); let history=s.history.clone();
    let turn=Uuid::new_v4().to_string();
    if retry.is_none()&&!req.test_voice {s.history.push(History{speaker:"user".into(),text:req.text.clone()});}
    let (cancel,stopped)=tokio::sync::oneshot::channel();
    s.cancel=Some(cancel);
    drop(all);let answer=json!({"turnId":turn,"generation":g});
    tokio::spawn(async move {
        tokio::select! {
            biased;
            _=stopped=>{},
            _=run(h.clone(),id.clone(),g,turn.clone(),role,history,req,retry)=>{},
        }
        h.finish(&id,g,&turn).await;
    });
    Ok(Json(answer))
}
#[derive(Deserialize)] #[serde(rename_all="camelCase")]
pub struct StopSession { pub session_id:String, pub expected_generation:Option<u64> }
pub async fn stop(State(h):State<Shared>,headers:HeaderMap,Json(req):Json<StopSession>)->ApiResult {
    auth(&h,&headers)?; let mut all=h.sessions.lock().await;
    let s=all.get_mut(&req.session_id).ok_or((StatusCode::NOT_FOUND,"Session missing"))?;
    if req.expected_generation.is_some_and(|g|g>0 && g!=s.generation){return Ok(Json(json!({"generation":s.generation})));}
    if let Some(cancel)=s.cancel.take(){let _=cancel.send(());}
    s.generation+=1;s.busy=false;Ok(Json(json!({"generation":s.generation})))
}
#[derive(Deserialize)] #[serde(rename_all="camelCase")]
pub struct OpenSession {pub session_id:String, #[serde(default)] pub after:usize}
pub async fn events(State(h):State<Shared>,headers:HeaderMap,Query(req):Query<OpenSession>)->ApiResult {
    auth(&h,&headers)?;let all=h.sessions.lock().await;
    let s=all.get(&req.session_id).ok_or((StatusCode::NOT_FOUND,"Session missing"))?;
    let newest=s.events.last().map_or(0,|e|e.cursor);
    let oldest=s.events.first().map_or(0,|e|e.cursor);
    let gap=req.after>newest || req.after.saturating_add(1)<oldest;
    if gap {
        return Ok(Json(json!({"cursorExpired":true,"oldestCursor":oldest,"cursor":newest,"history":s.history,"events":[]})));
    }
    Ok(Json(json!({"oldestCursor":oldest,"cursor":newest,"events":s.events.iter().filter(|e|e.cursor>req.after).collect::<Vec<_>>()})))
}
pub async fn audio(State(h):State<Shared>,headers:HeaderMap,Path(id):Path<String>)->Response {
    if let Err(e)=auth(&h,&headers){return e.into_response();}
    if !id.ends_with(".wav")||Uuid::parse_str(id.trim_end_matches(".wav")).is_err(){return StatusCode::BAD_REQUEST.into_response();}
    match tokio::fs::read(h.root.join("audio").join(id)).await {Ok(bytes)=>([("content-type","audio/wav")],bytes).into_response(),Err(_)=>StatusCode::NOT_FOUND.into_response()}
}
pub async fn health(State(h):State<Shared>,headers:HeaderMap)->ApiResult {auth(&h,&headers)?;Ok(Json(json!({"ready":true,"voice":"ling-v2","language":"ja","model":h.llm.read().await.config().model,"requiresPc":true,"stageActions":true,"protocolVersion":2}))) }
// 只读取宿主启动时固定的 PC 配置路径，不接收设备提交的路径或凭据。
pub async fn reload_settings(State(h):State<Shared>,headers:HeaderMap)->ApiResult {
    auth(&h,&headers)?;
    let sessions=h.sessions.lock().await;
    if sessions.values().any(|s|s.busy){return Err((StatusCode::CONFLICT,"Conversation busy"));}
    let path=h.settings_path.as_ref().ok_or((StatusCode::SERVICE_UNAVAILABLE,"Settings unavailable"))?;
    let (chat,translation)=crate::settings::load(path).map_err(|_|(StatusCode::BAD_REQUEST,"Invalid XR service settings"))?;
    *h.llm.write().await=chat;*h.translator.slot().write().await=Some(translation);
    drop(sessions);Ok(Json(json!({"applied":true})))
}
// Adapted from LingChat processor's emotion/body/<spoken text> contract.
pub fn parse(text:&str)->Vec<Sentence> {
    let tags=regex::Regex::new(r"【([^】]*)】([^【】]*)").unwrap();let jp=regex::Regex::new(r"<([^>]*)>").unwrap();let strip=regex::Regex::new(r"<[^>]*>|（[^）]*）").unwrap();let motion=regex::Regex::new(r"（([^）]*)）").unwrap();
    let normalized=text.replace('＜',"<").replace('＞',">");let input=if normalized.contains('【'){normalized}else{format!("【平静】{normalized}")};
    tags.captures_iter(&input).filter_map(|c|{let body=&c[2];let message=strip.replace_all(body,"").trim().to_string();if message.is_empty(){return None;} Some(Sentence{message,emotion:c[1].to_string(),motion:motion.captures_iter(body).map(|x|x[1].to_string()).collect::<Vec<_>>().join(" | "),tts_text:jp.captures(body).map(|x|x[1].trim().into()).unwrap_or_default(),..Default::default()})}).enumerate().map(|(i,mut s)|{s.index=i;s}).collect()
}
pub fn japanese(text:&str)->bool {!text.trim().is_empty()&&text.chars().any(|c|('\u{3040}'..='\u{30ff}').contains(&c))}
async fn run(h:Shared,id:String,g:u64,turn:String,role:String,history:Vec<History>,req:Turn,retry:Option<Sentence>) {
    let replay=retry.is_some();
    if let Some(mut s)=retry {synthesize(&h,&id,g,&turn,&mut s,true).await;return;}
    if req.test_voice {let mut s=Sentence{message:"你好，我是小肥鱼。很高兴见到你。".into(),tts_text:"こんにちは。会えてうれしいです。".into(),emotion:"开心".into(),..Default::default()};synthesize(&h,&id,g,&turn,&mut s,false).await;return;}
    {
        h.event(&id,g,&turn,0,"thinking","思考中","").await;
        let name=if role=="feiyu"{"小肥鱼"}else{"钦灵（测试会话）"};
        let mut messages=vec![LlmMessage::system(format!("你是{name}，友好、自然、简短地用中文与用户聊天。这是独立 XR 试玩会话。每轮回复一到三句，每句按 LingChat 格式输出：【情绪】中文正文（可选的简短动作）<对应的自然日语译文>。情绪可为正常、开心、悲伤、害羞、惊讶、思考。只扮演自己，不代替用户发言，不输出分析或工具调用。"))];
        for row in history.iter().rev().take(30).collect::<Vec<_>>().into_iter().rev(){messages.push(if row.speaker=="user"{LlmMessage::user(&row.text)}else{LlmMessage::assistant(&row.text)});}
        messages.push(LlmMessage::user(req.text.clone()));
        // 完整句段到达即进入有界合成队列；后续 LLM 流继续读取，不等整轮结束。
        let (tx,mut rx)=tokio::sync::mpsc::channel::<Sentence>(8);
        let producer=async {
            let output=tokio::time::timeout(std::time::Duration::from_secs(150),async {
                let client=h.llm.read().await.clone();let mut stream=client.complete_stream(&messages).await?;let mut tail=String::new();let mut bytes=0;let mut index=0;let mut thinking=0;
                while let Some(chunk)=stream.next().await {
                    if !h.current(&id,g).await {anyhow::bail!("cancelled");}
                    match chunk? {
                        LlmChunk::Reasoning(s)=>{thinking+=s.chars().count();h.event(&id,g,&turn,0,"thinkingProgress",&thinking.to_string(),"").await;},
                        LlmChunk::Content(s)=>{
                            bytes+=s.len();anyhow::ensure!(bytes<=32000,"response limit");tail.push_str(&s.replace('＜',"<").replace('＞',">"));
                            while let Some(end)=tail.find('>') {
                                let complete:String=tail.drain(..end+1).collect();
                                for mut sentence in parse(&complete) {sentence.index=index;index+=1;tx.send(sentence).await?;}
                            }
                        },_=>{}
                    }
                }
                for mut sentence in parse(&tail) {sentence.index=index;index+=1;tx.send(sentence).await?;}
                anyhow::ensure!(index>0,"empty response");Ok::<_,anyhow::Error>(())
            }).await;
            drop(tx);
            if !matches!(output,Ok(Ok(()))) {h.event(&id,g,&turn,0,"error","对话服务失败，已生成的内容保留","").await;}
        };
        let consumer=async {
            let mut decisions=tokio::task::JoinSet::new();
            while let Some(mut sentence)=rx.recv().await {
                if !h.current(&id,g).await {break;}
                if sentence.index==0&&req.director&&!req.available.is_empty() {
                    let host=h.clone();let session=id.clone();let turn=turn.clone();let user=req.text.clone();let speech=sentence.message.clone();let motion=sentence.motion.clone();let available=req.available.clone();let style=req.style.clone();let previous=req.previous_pose.clone();
                    decisions.spawn(async move {
                        let client=host.llm.read().await.clone();
                        let decision=crate::stage_action::select(&client,&user,&speech,&motion,&available,&style,&previous).await;
                        let mut all=host.sessions.lock().await;
                        if let Some(s)=all.get_mut(&session).filter(|s|s.generation==g){
                            let cursor=s.events.last().map_or(1,|e|e.cursor+1);
                            s.events.push(Event{cursor,sentence_end_exclusive:0,session_id:session,role_id:s.role_id.clone(),turn_id:turn.clone(),generation:g,index:0,kind:"stageAction".into(),message:String::new(),audio_file:String::new(),emotion:String::new(),motion:String::new(),stage_action:Some(decision),stage_request_id:format!("{turn}:0")});
                        }
                    });
                }
                synthesize(&h,&id,g,&turn,&mut sentence,replay).await;
            }
            let _=decisions.join_next().await;
        };
        tokio::join!(producer,consumer);
    }
}
async fn synthesize(h:&Shared,id:&str,g:u64,turn:&str,sentence:&mut Sentence,replay:bool) {
        if !h.current(id,g).await{return;}sentence.turn_id=turn.into();
        if !replay {if let Some(s)=h.sessions.lock().await.get_mut(id).filter(|s|s.generation==g){s.history.push(History{speaker:"assistant".into(),text:sentence.message.clone()});}}
        h.event(id,g,turn,sentence.index,"text",&sentence.message,"").await;
        if !japanese(&sentence.tts_text) {
            h.event(&id,g,&turn,sentence.index,"translating","准备语音","").await;
            let mut segments=vec![EmotionSegment{following_text:sentence.message.clone(),..Default::default()}];
            let result=tokio::time::timeout(std::time::Duration::from_secs(120),h.translator.translate_segments_to(&mut segments,false,"ja")).await;
            if matches!(result,Ok(Ok(true)))&&japanese(&segments[0].japanese_text){sentence.tts_text=segments[0].japanese_text.clone();}
        }
        if !h.current(&id,g).await{return;}let mut error="";
        if !japanese(&sentence.tts_text){error="翻译未完成，中文已保留，可重试语音";}else if sentence.audio_file.is_empty() {
            h.event(&id,g,&turn,sentence.index,"synthesizing","合成中","").await;
            if let Ok(_slot)=h.voice_queue.slots.try_acquire() {
                let _engine=h.voice_queue.engine.lock().await;
                if !h.current(id,g).await{return;}
                match h.voice.generate_voice(&sentence.tts_text,"").await {
                    Ok(bytes) if bytes.starts_with(b"RIFF")=> {
                        if !h.current(id,g).await{return;}
                        let file=format!("{}.wav",Uuid::new_v4());
                        if tokio::fs::write(h.root.join("audio").join(&file),bytes).await.is_ok(){sentence.audio_file=file;}
                        else{error="语音缓存写入失败";}
                    }
                    _=>error="语音合成失败，文字已保留，可重试语音"
                }
            } else {
                error="语音队列已满，文字已保留，可重试语音";
            }
        }
        if !h.current(&id,g).await{return;}
        if let Some(s)=h.sessions.lock().await.get_mut(id).filter(|s|s.generation==g){s.sentences.push(sentence.clone());}
        if error.is_empty(){h.event(&id,g,&turn,sentence.index,"audio",&sentence.message,&sentence.audio_file).await;}else{h.event(&id,g,&turn,sentence.index,"sentenceError",error,"").await;}
}
#[cfg(test)] mod tests {
    use super::*;
    #[test] fn body_and_spoken_text_stay_paired(){let s=parse("【开心】你好。<こんにちは。>【平静】今天如何？<今日はどう？>");assert_eq!(s.len(),2);assert_eq!(s[1].message,"今天如何？");assert_eq!(s[0].tts_text,"こんにちは。");}
    #[test] fn missing_translation_does_not_become_chinese_speech(){assert!(!japanese(&parse("【平静】只有中文")[0].tts_text));assert!(!japanese("只有中文"));}
    #[test] fn empty_segments_do_not_leave_queue_gaps(){let s=parse("【平静】<はい>【开心】你好<こんにちは>【平静】");assert_eq!(s.len(),1);assert_eq!(s[0].index,0);}
}
#[cfg(test)] #[path="host_tests.rs"] mod protocol_tests;
