//! LingChat stage_action / select_stage_action 的独立宿主适配；沿用同一工具契约。
//! Tauri/GodAgentCore 的服务注入由已存在的 LlmClient 替代，不匹配聊天关键词。
use serde::{Deserialize,Serialize};
use serde_json::{json,Value};
use crate::ai_service::{llm::LlmClient,types::{LlmMessage,ToolDefinition}};

#[derive(Debug,Clone,Serialize,Deserialize)]
pub struct Candidate {pub id:String,pub label:String,#[serde(default)] pub persistent:bool}
#[derive(Debug,Clone,Serialize,Deserialize)]
pub struct Decision {pub action:String,pub reason:String}
impl Decision {pub fn none()->Self{Self{action:"none".into(),reason:"无可用动作或决策未完成".into()}}}
pub fn valid_candidates(items:&[Candidate])->bool {
    items.len()<=64&&items.iter().all(|a| !a.id.is_empty()&&a.id.len()<=80&&a.id.bytes().all(|c|c.is_ascii_alphanumeric()||c==b'_')&&a.label.chars().count()<=100)
}
pub fn parse(arguments:&str,available:&[Candidate])->Option<Decision> {
    let mut value:Value=serde_json::from_str(arguments).ok()?;
    if let Some(s)=value.as_str(){value=serde_json::from_str(s).ok()?;}
    if let Some(args)=value.get("arguments"){value=args.clone();}
    let d:Decision=serde_json::from_value(value).ok()?;
    if d.action!="none"&&!available.iter().any(|a|a.id==d.action){return None;} Some(d)
}
pub async fn select(client:&LlmClient,user:&str,speech:&str,motion:&str,available:&[Candidate],style:&str,previous:&str)->Decision {
    if available.is_empty()||!valid_candidates(available){return Decision::none();}
    let tool=ToolDefinition::new("select_stage_action","为当前角色选择一个已许可动作，或 none。",json!({"type":"object","properties":{"action":{"type":"string"},"reason":{"type":"string"}},"required":["action","reason"]}));
    let messages=[LlmMessage::system(include_str!("stage-action-prompt.txt")),LlmMessage::user(json!({"user":user,"speech":speech,"performance":motion,"available":available,"style":style,"previousPoseDecision":previous}).to_string())];
    // 与 LingChat tools/agent.rs 一致；思考模式不接受强制 required。
    match tokio::time::timeout(std::time::Duration::from_secs(8),client.complete_with_tools(&messages,&[tool],Some("auto"))).await {
        Ok(Ok(result))=>result.tool_calls.unwrap_or_default().iter().find(|c|c.function.name=="select_stage_action").and_then(|c|parse(&c.function.arguments,available)).unwrap_or(Decision{action:"none".into(),reason:"服务未返回有效的结构化动作工具输出".into()}),
        Ok(Err(error))=>{
            let message=error.to_string().to_lowercase();
            let reason=if message.contains("401")||message.contains("403"){"动作服务认证失败"}else if message.contains("400")||message.contains("tool_choice"){"服务拒绝动作工具请求"}else if message.contains("connect")||message.contains("transport")||message.contains("send") {"动作服务连接失败"}else{"动作服务请求失败"};
            Decision{action:"none".into(),reason:reason.into()}
        },
        Err(_)=>Decision{action:"none".into(),reason:"动作决策超时".into()}
    }
}
#[cfg(test)]mod tests {
    use super::*;
    #[test]fn exact_ids_only_no_text_keyword_fallback(){let c=vec![Candidate{id:"nod".into(),label:"点头".into(),persistent:false}];assert!(parse(r#"{"action":"nod","reason":"当前执行"}"#,&c).is_some());assert!(parse(r#"{"action":"wave","reason":"点头"}"#,&c).is_none());assert!(parse("挥手",&c).is_none());assert!(parse(r#"{"action":"none","reason":"拒绝"}"#,&c).is_some());}
}
