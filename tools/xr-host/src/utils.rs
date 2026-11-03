pub mod onnx {
    pub fn onnx_available() -> bool { std::env::var_os("ORT_DYLIB_PATH").is_some_and(|p| std::path::Path::new(&p).is_file()) }
}
// Do not retain private prompts or provider request bodies in this host's logs.
pub mod llm_request_logger {
    pub fn file_log_enabled() -> bool { false }
    pub fn log_request_body(_: &str, _: &serde_json::Value) {}
    pub fn push_request_summary(_: String) {}
}
