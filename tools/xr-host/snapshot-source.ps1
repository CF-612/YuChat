param([string]$SourceRoot = 'D:/GameCreator/Document/LingChat/.codex-worktrees/vrm-mvp')
$ErrorActionPreference = 'Stop'
$dest = Join-Path $PSScriptRoot 'src/ai_service'
$records = @()
function Snapshot([string]$relative, [string]$target, [scriptblock]$adapt = { param($s) $s }) {
    $source = Join-Path $SourceRoot "crates/ling-chat-main/src/ai_service/$relative"
    $text = [IO.File]::ReadAllText($source).Replace("`r`n", "`n")
    $result = & $adapt $text
    $path = Join-Path $dest $target
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path, $result)
    $script:records += @{ source = "crates/ling-chat-main/src/ai_service/$relative"; target = "src/ai_service/$target"; sha256 = (Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant() }
}
Snapshot 'tts/local/engine.rs' 'tts/local/engine.rs'
Snapshot 'tts/local/adapter.rs' 'tts/local/adapter.rs'
Snapshot 'tts/provider.rs' 'tts/provider.rs' { param($s)
    "use std::collections::HashMap;`nuse anyhow::Result;`nuse async_trait::async_trait;`nuse serde_json::Value as JsonValue;`n" + $s.Substring($s.IndexOf('/// TTS 一次合成'), $s.IndexOf('/// 连续合成失败') - $s.IndexOf('/// TTS 一次合成'))
}
Snapshot 'llm/provider.rs' 'llm/provider.rs'
Snapshot 'llm/providers/genai_provider.rs' 'llm/providers/genai_provider.rs'
Snapshot 'llm/mod.rs' 'llm/mod.rs' { param($s)
    $s = $s -replace '(?m)^pub mod (codex|error|factory|provider_config|vision);\n',''
    $s = $s -replace '(?m)^pub use (codex::auth as codex_auth|factory::create_llm_client);\n',''
    $s.Replace('mod providers;', 'pub mod providers;')
}
Snapshot 'translator.rs' 'translator.rs' { param($s)
    # XR adaptation: reject malformed/extra/empty translation segments atomically.
    $s.Substring(0, $s.IndexOf('fn apply_translation_result')) + @'
fn apply_translation_result(response: &str, segments: &mut [EmotionSegment]) -> usize {
    let re = regex::Regex::new(r"<([^<>]+)>").unwrap();
    let values: Vec<_> = re.captures_iter(response).map(|c| c[1].trim().to_string()).collect();
    if values.len() != segments.len() || values.iter().any(|s|s.is_empty()) ||
        !re.replace_all(response, "").trim().is_empty() { return 0; }
    for (segment, value) in segments.iter_mut().zip(values) { segment.japanese_text = value; }
    segments.len()
}
#[cfg(test)] mod xr_tests {
    use super::*;
    #[test] fn translation_count_is_exact_and_atomic() {
        let mut s=vec![EmotionSegment{japanese_text:"old".into(),..Default::default()}];
        for text in ["", "<>", "<a><b>", "<a", "extra<a>"] {
            assert_eq!(apply_translation_result(text,&mut s),0); assert_eq!(s[0].japanese_text,"old");
        }
        assert_eq!(apply_translation_result("<こんにちは>",&mut s),1);
    }
}
'@
}
Snapshot 'types.rs' 'types.rs' { param($s)
    $s.Substring(0, $s.IndexOf('// 台词基础结构')).TrimEnd().TrimEnd('/') -replace '(?m)^use (std::collections::HashMap|std::path::PathBuf|crate::db::entities::line::LineAttribute);\n',''
}
Snapshot 'message_system/processor.rs' 'message_system/processor.rs' { param($s)
    # F2 only consumes the source sentence contract. Desktop analysis/emotion model remain in LingChat.
    "use serde::{Deserialize, Serialize};`n" + $s.Substring($s.IndexOf('/// 单个情绪片段'), $s.IndexOf('/// 处理用户消息的结果') - $s.IndexOf('/// 单个情绪片段'))
}
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'licenses') | Out-Null
Copy-Item -LiteralPath (Join-Path $SourceRoot 'LICENSE') -Destination (Join-Path $PSScriptRoot 'licenses/LingChat-AGPL-3.0.txt')
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'vendor') | Out-Null
Copy-Item -LiteralPath (Join-Path $SourceRoot 'src-tauri/patches/esaxx-rs') -Destination (Join-Path $PSScriptRoot 'vendor') -Recurse -Force
$records | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PSScriptRoot 'source-snapshot.json') -Encoding utf8
