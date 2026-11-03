pub mod llm;
pub mod types;
pub mod translator;
pub mod message_system { pub mod processor; }
pub mod tts {
    pub mod provider;
    pub mod local {
        pub mod engine;
        pub mod adapter;
        pub mod paths;
        pub use engine::{LocalTtsEngine, SynthesizeRequest};
        pub use paths::LocalTtsPaths;
    }
}
