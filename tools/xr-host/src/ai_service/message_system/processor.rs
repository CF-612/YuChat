use serde::{Deserialize, Serialize};
/// 单个情绪片段。字段与旧版 `parse_and_classify_emotional_segments` 返回一致。
#[derive(Debug, Clone, Serialize, Deserialize, Default)]
pub struct EmotionSegment {
    pub index: usize,
    pub original_tag: String,
    pub following_text: String,
    pub motion_text: String,
    pub japanese_text: String,
    /// 情绪分类器的预测 label。未启用时等于 `original_tag`。
    pub predicted: String,
    pub confidence: f64,
    pub voice_file: String,

    /// 关联角色信息（由 consumer 填充）
    pub character: Option<String>,
    pub role_id: Option<i32>,
}

