// 从 LingChat providers/qwen.rs 抽取的原始请求/响应函数；AGPL-3.0-only。
use serde_json::{Value as JsonValue,json};
use tracing::warn;
use super::{provider_types::Hotword,qwen_models::{qwen_uses_legacy_audio_content,qwen_supports_inline_vocabulary}};
pub(crate) fn hotwords_to_vocabulary_json(hotwords: &[Hotword]) -> JsonValue {
    let mut map = serde_json::Map::new();
    for h in hotwords {
        let text = h.text.trim();
        if text.is_empty() {
            continue;
        }
        map.entry(text.to_string())
            .or_insert_with(|| json!(h.weight));
    }
    JsonValue::Object(map)
}

/// 构造同步端点（multimodal-generation）的请求体。
///
/// 该端点的 `parameters` 只有 format / sample_rate / vocabulary（无 language_hints）。
/// 热词在此门控：仅 qwen-audio-3.x 系支持 `vocabulary`，对其余模型发它很可能直接 400。
pub(crate) fn build_batch_body(model: &str, data_url: &str, hotwords: &[Hotword]) -> JsonValue {
    // 音频内容格式按模型分流：qwen-audio-3.x 系用 input_audio；历史 Fun-ASR-Realtime
    // 用旧格式 audio。一刀切会让老配置从「能用」变成「未知错误」。
    let legacy = qwen_uses_legacy_audio_content(model);
    let audio_content = if legacy {
        json!({ "audio": data_url })
    } else {
        json!({ "type": "input_audio", "input_audio": { "data": data_url } })
    };
    let mut body = json!({
        "model": model,
        "input": {
            "messages": [{
                "role": "user",
                "content": [audio_content]
            }]
        },
        "parameters": {
            "format": "wav",
            "sample_rate": 16000
        }
    });
    if legacy {
        // 旧协议实测带这个字段；新协议按官方示例不带
        body["resources"] = json!([]);
    }
    if !hotwords.is_empty() {
        if qwen_supports_inline_vocabulary(model) {
            body["parameters"]["vocabulary"] = hotwords_to_vocabulary_json(hotwords);
        } else {
            warn!(
                "[ASR] 模型 {model} 不支持即时热词，已忽略 {} 条热词（如需热词请改用 qwen-audio-3.x 系）",
                hotwords.len()
            );
        }
    }
    body
}

pub(crate) fn parse_qwen_text(body: &str) -> Option<String> {
    let value: JsonValue = serde_json::from_str(body).ok()?;
    // Fun-ASR-Realtime：output.output.text（sentence 内也有一份）
    if let Some(s) = value
        .get("output")
        .and_then(|v| v.get("output"))
        .and_then(|v| v.get("text"))
        .and_then(|v| v.as_str())
    {
        return Some(s.to_string());
    }
    if let Some(s) = value
        .get("output")
        .and_then(|v| v.get("output"))
        .and_then(|v| v.get("sentence"))
        .and_then(|v| v.get("text"))
        .and_then(|v| v.as_str())
    {
        return Some(s.to_string());
    }
    // OpenAI 风格：output.choices[0].message.content（content 可能是数组）
    if let Some(content) = value
        .get("output")
        .and_then(|v| v.get("choices"))
        .and_then(|v| v.as_array())
        .and_then(|arr| arr.first())
        .and_then(|v| v.get("message"))
        .and_then(|v| v.get("content"))
    {
        if let Some(s) = content.as_str() {
            return Some(s.to_string());
        }
        if let Some(arr) = content.as_array() {
            let joined: String = arr
                .iter()
                .filter_map(|part| part.get("text").and_then(|t| t.as_str()))
                .collect();
            if !joined.is_empty() {
                return Some(joined);
            }
        }
    }
    if let Some(s) = value.get("text").and_then(|v| v.as_str()) {
        return Some(s.to_string());
    }
    if let Some(s) = value
        .get("output")
        .and_then(|v| v.get("text"))
        .and_then(|v| v.as_str())
    {
        return Some(s.to_string());
    }
    if let Some(s) = value
        .get("result")
        .and_then(|v| v.get("text"))
        .and_then(|v| v.as_str())
    {
        return Some(s.to_string());
    }
    None
}
