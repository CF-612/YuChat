use std::sync::Arc;
use std::path::Path;
use anyhow::{Result, ensure};
use crate::ai_service::tts::provider::TtsAdapter;

// 临时声线通过现有适配器离线导出，游戏只加载 WAV。
pub async fn export(voice: Arc<dyn TtsAdapter>, output: &Path) -> Result<()> {
    std::fs::create_dir_all(output)?;
    let lines = [
        ("happy_1", "happy", "うん！"), ("happy_2", "happy", "えへへ。"),
        ("curious_1", "curious", "ん？"), ("curious_2", "curious", "え？"),
        ("surprised_1", "surprised", "わあ！"), ("surprised_2", "surprised", "きゃっ！"),
        ("protest_1", "protest", "むう。"), ("protest_2", "protest", "もう！"),
    ];
    let mut entries = Vec::new();
    for (id, group, text) in lines {
        let started = std::time::Instant::now();
        let audio = voice.generate_voice(text, "").await?;
        ensure!(audio.starts_with(b"RIFF") && audio.len() > 44, "Invalid WAV: {id}");
        std::fs::write(output.join(format!("{id}.wav")), &audio)?;
        entries.push(serde_json::json!({"id":id,"group":group,"text":text,"bytes":audio.len(),
            "generationSeconds":started.elapsed().as_secs_f64(),"source":"existing ling-v2 TTS","temporary":true}));
        println!("REACTION_EXPORTED {id} bytes={}", audio.len());
    }
    std::fs::write(output.join("sources.json"), serde_json::to_vec_pretty(&entries)?)?;
    Ok(())
}
