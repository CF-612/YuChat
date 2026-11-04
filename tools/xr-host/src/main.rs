#![allow(dead_code)]
mod ai_service;
mod utils;
mod host;
mod session_store;
mod settings;
mod stage_action;
mod gateway;
mod asr;
mod reaction_export;
mod asr_source;
use std::{sync::Arc,path::PathBuf};
use ai_service::{translator::Translator,tts::local::{LocalTtsEngine,LocalTtsPaths,adapter::LocalTtsAdapter}};
use axum::{Router,routing::{get,post}};
use serde_json::json;
use tokio::sync::{Mutex,RwLock};
use ai_service::tts::provider::TtsAdapter;
#[tokio::main]
async fn main()->anyhow::Result<()> {
    if std::env::args().any(|a|a=="--pairing-gateway"){return gateway::run().await;}
    let settings=PathBuf::from(std::env::var("XR_LINGCHAT_SETTINGS")?);
    let models=PathBuf::from(std::env::var("XR_TTS_MODELS")?);
    let root=PathBuf::from(std::env::var("XR_HOST_DATA")?);
    std::fs::create_dir_all(root.join("audio"))?;
    let (chat_client,translation_client)=settings::load(&settings)?;
    let llm=RwLock::new(chat_client);
    if std::env::args().any(|a|a=="--verify-stage-action") {
        let available=vec![stage_action::Candidate{id:"nod".into(),label:"点头".into(),persistent:false},stage_action::Candidate{id:"wave_normal_right_661".into(),label:"自然右手挥手".into(),persistent:false}];
        let client=llm.read().await.clone();
        let yes=stage_action::select(&client,"请现在向我挥手打个招呼。","你好呀！","抬起右手向你挥手",&available,"normal","").await;
        let no=stage_action::select(&client,"你昨天挥手了吗？","昨天我向朋友挥了挥手，现在不做动作。","",&available,"normal","").await;
        std::fs::write(root.join("stage-verification.json"),serde_json::to_vec_pretty(&json!({"currentExecution":yes,"pastNarrative":no,"historyChanged":false}))?)?;
        anyhow::ensure!(yes.action=="wave_normal_right_661"&&no.action=="none","Director semantic verification failed; inspect private result");
        println!("XR_STAGE_ACTION_VERIFIED current=wave past=none");return Ok(());
    }
    let translator=Translator::new(Arc::new(RwLock::new(Some(translation_client))),true);
    let voice=Arc::new(LocalTtsAdapter::with_params(Arc::new(LocalTtsEngine::new()),"ling-v2".into(),0,0,0.,1.,LocalTtsPaths{root:models}));
    if std::env::args().any(|a|a=="--export-reactions") { return reaction_export::export(voice, &root.parent().unwrap().join("reaction-clips")).await; }
    if std::env::args().any(|a|a=="--verify-translation") {
        let mut segments=vec![ai_service::message_system::processor::EmotionSegment{following_text:"今天也很高兴见到你，我们慢慢聊吧。".into(),..Default::default()}];
        anyhow::ensure!(translator.translate_segments_to(&mut segments,false,"ja").await?,"Translation incomplete");
        anyhow::ensure!(host::japanese(&segments[0].japanese_text),"Japanese translation missing");
        let bytes=voice.generate_voice(&segments[0].japanese_text,"").await?;
        anyhow::ensure!(bytes.starts_with(b"RIFF")&&bytes.len()>1000,"WAV missing");
        std::fs::write(root.join("translation-verification.wav"),&bytes)?;
        let report=json!({"result":"passed","realTranslator":true,"realLocalTts":true,"voice":"ling-v2","audioBytes":bytes.len(),"userListeningAccepted":false});
        std::fs::write(root.join("translation-verification.json"),serde_json::to_vec_pretty(&report)?)?;
        println!("XR_REAL_TRANSLATION_AND_TTS_PASSED");return Ok(());
    }
    let (store,mut sessions)=session_store::SessionStore::open(&root)?;
    for s in sessions.values_mut(){s.generation+=1;s.busy=false;}
    let token=uuid::Uuid::new_v4().to_string();
    let asr_settings=std::env::var("XR_ASR_SETTINGS").map(PathBuf::from).unwrap_or_else(|_|settings.clone());
    let asr_routes=asr::router(token.clone(), &asr_settings);
    let h=Arc::new(host::Host{store,voice_queue:Default::default(),token:token.clone(),root:root.clone(),llm,translator,voice,settings_path:Some(settings),sessions:Mutex::new(sessions)});
    let port:u16=std::env::var("XR_HOST_PORT").unwrap_or_else(|_|"17861".into()).parse()?;
    let listener=tokio::net::TcpListener::bind(("127.0.0.1",port)).await?;
    std::fs::write(root.parent().unwrap().join("xr-host-connection.json"),serde_json::to_vec(&json!({"endpoint":format!("http://127.0.0.1:{port}"),"token":token}))?)?;
    let app=Router::new().route("/health",get(host::health)).route("/playtest/direct",post(host::playtest)).route("/reload-settings",post(host::reload_settings)).route("/session",post(host::open)).route("/history",get(host::history_page)).route("/turn",post(host::turn)).route("/stop",post(host::stop)).route("/events",get(host::events)).route("/audio/:id",get(host::audio)).layer(axum::extract::DefaultBodyLimit::max(16384)).with_state(h);
    println!("XR_HOST_READY loopback={port} voice=ling-v2 (PC required)");
    axum::serve(listener,app.merge(asr_routes)).await?;Ok(())
}
