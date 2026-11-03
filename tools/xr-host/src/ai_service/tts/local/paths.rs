// XR host adapter: same read-only model layout, no Tauri directory creation/import.
use std::path::PathBuf;
#[derive(Clone, Debug)]
pub struct LocalTtsPaths { pub root: PathBuf }
impl LocalTtsPaths {
    pub fn deberta_dir(&self) -> PathBuf { self.root.join("assets/deberta") }
    pub fn voice_dir(&self, id: &str) -> PathBuf { self.root.join("voices").join(id) }
}
