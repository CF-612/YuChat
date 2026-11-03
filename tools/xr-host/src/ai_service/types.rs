use serde::{Deserialize, Serialize};
use serde_json::Value;


// ==========================================
// LLM 消息 & Function Calling
// ==========================================

/// Function call 请求参数。
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct FunctionCall {
    pub name: String,
    pub arguments: String, // JSON 字符串
}

/// LLM 返回的 tool call。
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct ToolCall {
    pub id: String,
    #[serde(rename = "type", default = "default_tool_type")]
    pub type_: String,
    pub function: FunctionCall,
}

fn default_tool_type() -> String {
    "function".to_string()
}

/// 注册给 LLM 的 tool / function 定义。
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct ToolDefinition {
    #[serde(rename = "type")]
    pub type_: String,
    pub function: FunctionSchema,
    /// Tool RAG 的检索示例问句。只在本机做索引，不随请求下发给模型。
    #[serde(skip)]
    pub rag_examples: Vec<String>,
}

impl ToolDefinition {
    pub fn new(
        name: impl Into<String>,
        description: impl Into<String>,
        parameters: serde_json::Value,
    ) -> Self {
        Self {
            type_: "function".to_string(),
            function: FunctionSchema {
                name: name.into(),
                description: description.into(),
                parameters,
            },
            rag_examples: Vec::new(),
        }
    }

    /// 挂上 Tool RAG 的检索示例问句；没有配置时语料回退到描述。
    pub fn with_rag_examples<I, S>(mut self, examples: I) -> Self
    where
        I: IntoIterator<Item = S>,
        S: Into<String>,
    {
        self.rag_examples = examples.into_iter().map(Into::into).collect();
        self
    }
}

/// Function 的 JSON Schema 描述。
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct FunctionSchema {
    pub name: String,
    pub description: String,
    pub parameters: serde_json::Value,
}

pub(crate) fn parse_tool_args(raw: &str) -> Value {
    let mut args: Value = serde_json::from_str(raw).unwrap_or(Value::Null);

    if let Some(s) = args.as_str() {
        if let Ok(parsed) = serde_json::from_str::<Value>(s) {
            args = parsed;
        }
    }

    if let Value::Object(map) = &args {
        if map.len() == 1 {
            if let Some(inner) = map.get("arguments").or_else(|| map.get("params")) {
                if inner.is_object() {
                    args = inner.clone();
                }
            }
        }
    }

    if !args.is_object() {
        args = serde_json::json!({});
    }
    args
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct LlmMessage {
    pub role: String,
    pub content: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub tool_calls: Option<Vec<ToolCall>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub tool_call_id: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub image_data_url: Option<String>,
}

impl LlmMessage {
    pub fn system(content: impl Into<String>) -> Self {
        Self {
            role: "system".into(),
            content: content.into(),
            tool_calls: None,
            tool_call_id: None,
            image_data_url: None,
        }
    }
    pub fn user(content: impl Into<String>) -> Self {
        Self {
            role: "user".into(),
            content: content.into(),
            tool_calls: None,
            tool_call_id: None,
            image_data_url: None,
        }
    }
    pub fn assistant(content: impl Into<String>) -> Self {
        Self {
            role: "assistant".into(),
            content: content.into(),
            tool_calls: None,
            tool_call_id: None,
            image_data_url: None,
        }
    }
    /// 助手消息携带 tool calls（LLM 请求调用工具）。
    pub fn tool(tool_calls: Vec<ToolCall>) -> Self {
        Self {
            role: "assistant".into(),
            content: String::new(),
            tool_calls: Some(tool_calls),
            tool_call_id: None,
            image_data_url: None,
        }
    }
    /// 工具调用结果（role = "tool"）。
    pub fn tool_result(tool_call_id: impl Into<String>, content: impl Into<String>) -> Self {
        Self {
            role: "tool".into(),
            content: content.into(),
            tool_calls: None,
            tool_call_id: Some(tool_call_id.into()),
            image_data_url: None,
        }
    }
    pub fn user_with_image(content: impl Into<String>, image_data_url: String) -> Self {
        Self {
            role: "user".into(),
            content: content.into(),
            tool_calls: None,
            tool_call_id: None,
            image_data_url: Some(image_data_url),
        }
    }
}

// ==========================================