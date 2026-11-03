use std::collections::HashMap;
use anyhow::Result;
use async_trait::async_trait;
use serde_json::Value as JsonValue;
/// TTS 一次合成返回原始音频字节。
#[async_trait]
pub trait TtsAdapter: Send + Sync {
    /// `emo` 参数由 IndexTTS2 和 Fish S2 使用，其它 adapter 忽略。
    async fn generate_voice(&self, text: &str, emo: &str) -> Result<Vec<u8>>;

    /// 返回当前适配器参数（对应 Python `get_params`）。
    #[allow(dead_code)]
    fn get_params(&self) -> HashMap<String, JsonValue>;

    /// 流式语音合成。默认返回 `None`（不支持流式）。
    #[allow(dead_code)]
    async fn generate_voice_stream(&self, _text: &str) -> Option<Vec<u8>> {
        None
    }
}

