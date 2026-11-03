use std::{path::Path, sync::Arc};
use serde_json::Value;
use crate::ai_service::llm::{LlmClient,LlmConfig,providers::genai_provider::GenaiProvider};

// 沿用 LingChat provider/slot 字段；只读取 XR 私有副本，不修改原工程配置。
pub fn client(config:&Value,slot:&str)->anyhow::Result<Arc<LlmClient>> {
    let id=config[slot].as_str().filter(|s|!s.is_empty()).or(config["llm.chat_provider_id"].as_str()).unwrap_or("");
    let p=config["llm.providers"].as_array().and_then(|a|a.iter().find(|p|p["id"]==id)).ok_or_else(||anyhow::anyhow!("Provider slot unavailable"))?;
    let string=|key:&str|p[key].as_str().unwrap_or("").to_string();
    anyhow::ensure!(!string("model").trim().is_empty(),"Model required");
    let base=string("base_url");
    if !base.is_empty(){let url=reqwest::Url::parse(&base)?;anyhow::ensure!(["http","https"].contains(&url.scheme())&&url.username().is_empty()&&url.password().is_none(),"Invalid service URL");}
    let cfg=LlmConfig{provider:string("provider"),model:string("model"),api_key:string("api_key"),base_url:base,timeout_secs:120,temperature:p["temperature"].as_f64(),top_p:p["top_p"].as_f64(),enable_thinking:p["enable_thinking"].as_bool().unwrap_or(false),reasoning_effort:p["reasoning_effort"].as_str().map(String::from),fast_mode:false,support_vision:false};
    let http=reqwest::Client::builder().timeout(std::time::Duration::from_secs(150)).build()?;
    let provider=GenaiProvider::new(&cfg,http.clone())?;
    Ok(Arc::new(LlmClient::new(cfg,http,Box::new(provider))))
}
pub fn load(path:&Path)->anyhow::Result<(Arc<LlmClient>,Arc<LlmClient>)>{
    let config:Value=serde_json::from_slice(&std::fs::read(path)?)?;
    Ok((client(&config,"llm.chat_provider_id")?,client(&config,"llm.translate_provider_id")?))
}

#[cfg(test)] mod tests {
    use super::*;use serde_json::json;
    #[test] fn slots_validate_without_sending_requests(){
        let data=json!({"llm.providers":[{"id":"a","provider":"openai","model":"fixture","api_key":"fixture-only"}],"llm.chat_provider_id":"a"});
        assert_eq!(client(&data,"llm.translate_provider_id").unwrap().config().model,"fixture");
        let mut bad=data.clone();bad["llm.providers"][0]["base_url"]=json!("file:///private");assert!(client(&bad,"llm.chat_provider_id").is_err());
        bad=data.clone();bad["llm.providers"][0]["provider"]=json!("unsupported");assert!(client(&bad,"llm.chat_provider_id").is_err());
        bad=data;bad["llm.providers"][0]["model"]=json!("");assert!(client(&bad,"llm.chat_provider_id").is_err());
    }
}
