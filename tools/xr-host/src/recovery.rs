//! 当前轮的恢复数据，不决定客户端是否重播音频。
use serde::Serialize;
use crate::host::Sentence;

#[derive(Clone,Serialize)]
#[serde(rename_all="camelCase")]
pub struct RecoverySentence {
    pub index:usize,pub message:String,pub emotion:String,pub motion:String,
    pub audio_state:String,pub audio_file:String,pub error:String,
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn terminal_failure_keeps_ready_audio_and_closes_unfinished_sentences() {
        let mut snapshot=TurnSnapshot::new("turn".into(),3,4);
        for index in [4,5,6]{snapshot.begin_sentence(&Sentence{index,message:"保留正文".into(),..Default::default()});}
        snapshot.event(4,"audio","","ready.wav");
        snapshot.event(5,"sentenceError","合成失败","");
        snapshot.event(0,"error","上游断线","");
        snapshot.event(0,"streamEnd","","");
        assert_eq!(snapshot.sentences[0].audio_state,"ready");assert_eq!(snapshot.sentences[0].audio_file,"ready.wav");
        assert_eq!(snapshot.sentences[1].error,"合成失败");assert_eq!(snapshot.sentences[2].audio_state,"failed");
        assert_eq!(snapshot.sentences[2].message,"保留正文");assert_eq!(snapshot.error,"上游断线");
        assert!(snapshot.ended);assert_eq!(snapshot.sentence_end_exclusive,7);
    }
}
#[derive(Clone,Serialize)]
#[serde(rename_all="camelCase")]
pub struct TurnSnapshot {
    pub turn_id:String,pub generation:u64,pub ended:bool,
    pub sentence_start:usize,pub sentence_end_exclusive:usize,
    pub sentences:Vec<RecoverySentence>,pub error:String,
}
impl TurnSnapshot {
    pub fn new(turn_id:String,generation:u64,start:usize)->Self {
        Self{turn_id,generation,ended:false,sentence_start:start,sentence_end_exclusive:start,sentences:vec![],error:String::new()}
    }
    pub fn begin_sentence(&mut self,s:&Sentence) {
        if self.sentences.iter().any(|x|x.index==s.index){return;}
        self.sentence_start=self.sentence_start.min(s.index);
        self.sentence_end_exclusive=self.sentence_end_exclusive.max(s.index+1);
        self.sentences.push(RecoverySentence{index:s.index,message:s.message.clone(),emotion:s.emotion.clone(),motion:s.motion.clone(),audio_state:"pending".into(),audio_file:String::new(),error:String::new()});
    }
    pub fn event(&mut self,index:usize,kind:&str,message:&str,audio:&str) {
        if kind=="streamEnd"{self.ended=true;}
        if kind=="error"{self.error=message.into();}
        if let Some(s)=self.sentences.iter_mut().find(|s|s.index==index) {
            match kind {
                "audio"=>{s.audio_state="ready".into();s.audio_file=audio.into();},
                "silent"=>s.audio_state="silent".into(),
                "sentenceError"=>{s.audio_state="failed".into();s.error=message.into();},
                _=>{}
            }
        }
        if self.ended {for s in &mut self.sentences {if s.audio_state=="pending"{s.audio_state="failed".into();s.error="句段未完成，可重试".into();}}}
    }
}
