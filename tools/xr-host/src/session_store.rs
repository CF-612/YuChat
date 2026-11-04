//! 有序追加会话增量；磁盘操作不持有宿主会话锁。旧 JSON 保留不覆盖。
use std::{collections::{HashMap,HashSet}, fs::{self, OpenOptions}, io::{Write,BufRead,BufReader}, path::{Path,PathBuf}, sync::Arc};
use serde::{Serialize,Deserialize};
use tokio::sync::Mutex;
use crate::host::{Session,History,Sentence};

#[derive(Serialize,Deserialize)]
struct Record {
    version:u32, id:String, role_id:String, generation:u64,
    history_from:usize, sentences_from:usize, history:Vec<History>, sentences:Vec<Sentence>,
}
// 按已提交的换行边界读取，不把整份日志或未完成尾行读入会话。
fn scan_records(path:&Path,mut accept:impl FnMut(Record)->anyhow::Result<()>)->anyhow::Result<(u64,bool)> {
    let mut reader=BufReader::new(fs::File::open(path)?);let mut line=Vec::new();
    let mut complete=reader.read_until(b'\n',&mut line)? as u64;
    anyhow::ensure!(line.last()==Some(&b'\n')&&serde_json::from_slice::<serde_json::Value>(&line)?["sessionStoreVersion"]==1,"Invalid journal header");
    loop {
        line.clear();let count=reader.read_until(b'\n',&mut line)?;
        if count==0{return Ok((complete,false));}
        if line.last()!=Some(&b'\n'){return Ok((complete,true));}
        let record:Record=serde_json::from_slice(&line)?;
        anyhow::ensure!(record.version==1,"Unsupported record");accept(record)?;complete+=count as u64;
    }
}
#[derive(Debug,Serialize,Default)]
#[serde(rename_all="camelCase")]
pub struct AudioInventory {
    pub referenced_files:usize,pub referenced_bytes:u64,pub missing_references:usize,
    pub unreferenced_files:usize,pub unreferenced_bytes:u64,pub invalid_references:usize,pub skipped_entries:usize,
}
pub struct SessionStore { path:PathBuf, cursors:Arc<Mutex<HashMap<String,(usize,usize)>>> }
impl SessionStore {
    #[cfg(test)]
    pub async fn hold_writer_for_test(&self)->tokio::sync::OwnedMutexGuard<HashMap<String,(usize,usize)>> {
        self.cursors.clone().lock_owned().await
    }
    /// 离线导出给旧版使用；输入必须是停止写入后的日志或其一致性副本。
    /// 不迁移、不修复输入，不覆盖既有输出。
    pub fn export_legacy(root:&Path,output:&Path)->anyhow::Result<usize> {
        let mut sessions:HashMap<String,Session>=HashMap::new();
        let (_,incomplete)=scan_records(&root.join("sessions-v1.jsonl"),|r| {
            let s=sessions.entry(r.id.clone()).or_insert_with(||Session::empty(r.id.clone(),r.role_id.clone()));
            anyhow::ensure!(s.role_id==r.role_id&&s.history.len()==r.history_from&&s.sentences.len()==r.sentences_from,"Invalid journal sequence");
            s.generation=r.generation;s.history.extend(r.history);s.sentences.extend(r.sentences);
            Ok(())
        })?;
        anyhow::ensure!(!incomplete,"Incomplete journal; export a consistent copy");
        let data=serde_json::to_vec(&sessions)?;
        let mut file=OpenOptions::new().create_new(true).write(true).open(output)?;
        file.write_all(&data)?;file.sync_all()?;Ok(sessions.len())
    }
    /// 只盘点，不删除，不输出聊天正文或音频内容。建议使用离线副本。
    pub fn audio_inventory(root:&Path)->anyhow::Result<AudioInventory> {
        let mut refs=HashSet::new();let mut result=AudioInventory::default();
        let mut collect=|sentences:Vec<Sentence>| {
            for s in sentences {
                if s.audio_file.is_empty(){continue;}
                let name=s.audio_file.to_ascii_lowercase();
                if name.contains(['/', '\\', ':'])||!name.ends_with(".wav")||name==".wav"{result.invalid_references+=1;continue;}
                refs.insert(name);
            }
        };
        let journal=root.join("sessions-v1.jsonl");
        if journal.exists(){let (_,incomplete)=scan_records(&journal,|r|{collect(r.sentences);Ok(())})?;anyhow::ensure!(!incomplete,"Incomplete journal; audit a consistent copy");}
        else if root.join("sessions.json").exists(){let sessions:HashMap<String,Session>=serde_json::from_reader(fs::File::open(root.join("sessions.json"))?)?;for s in sessions.into_values(){collect(s.sentences);}}
        result.referenced_files=refs.len();let mut found=HashSet::new();
        if root.join("audio").exists(){for entry in fs::read_dir(root.join("audio"))? {
            let entry=entry?;let kind=entry.file_type()?;let name=entry.file_name().to_string_lossy().to_ascii_lowercase();
            if !kind.is_file()||!name.ends_with(".wav"){result.skipped_entries+=1;continue;}
            let size=entry.metadata()?.len();
            if refs.contains(&name){found.insert(name);result.referenced_bytes+=size;}else{result.unreferenced_files+=1;result.unreferenced_bytes+=size;}
        }}
        result.missing_references=refs.difference(&found).count();Ok(result)
    }
    pub fn open(root:&Path)->anyhow::Result<(Self,HashMap<String,Session>)> {
        fs::create_dir_all(root)?;
        let path=root.join("sessions-v1.jsonl");
        let mut sessions=HashMap::new();
        if !path.exists() {
            let old=root.join("sessions.json");
            if old.exists() {
                let data=fs::read(&old)?;
                sessions=serde_json::from_slice::<HashMap<String,Session>>(&data)?;
                let backup=root.join("sessions.pre-journal.json");
                // 旧实现可能留下半份备份；只恢复可确认是原文件前缀的中断写入。
                // 不同内容的备份可能属于另一次迁移，必须保留并拒绝覆盖。
                if backup.exists() {
                    let existing=fs::read(&backup)?;
                    if existing!=data {
                        anyhow::ensure!(data.starts_with(&existing),"Migration backup differs from source; both files preserved");
                        fs::rename(&backup,root.join(format!("sessions.pre-journal-incomplete-{}.json",uuid::Uuid::new_v4())))?;
                    }
                }
                if !backup.exists(){
                    let pending=root.join(format!("sessions.pre-journal-{}.pending",uuid::Uuid::new_v4()));
                    let mut f=OpenOptions::new().create_new(true).write(true).open(&pending)?;
                    f.write_all(&data)?;f.sync_all()?;drop(f);fs::rename(pending,&backup)?;
                }
            }
            // 完整迁移写临时文件再提交；已提交 journal 本身就是迁移标记。
            let tmp=root.join("sessions-v1.migrating");
            let mut file=fs::File::create(&tmp)?;
            file.write_all(b"{\"sessionStoreVersion\":1}\n")?;
            for s in sessions.values() {
                let record=Record{version:1,id:s.id.clone(),role_id:s.role_id.clone(),generation:s.generation,history_from:0,sentences_from:0,history:s.history.clone(),sentences:s.sentences.clone()};
                file.write_all(&serde_json::to_vec(&record)?)?;file.write_all(b"\n")?;
            }
            file.sync_all()?;drop(file);fs::rename(tmp,&path)?;
        } else {
            let (complete,incomplete)=scan_records(&path,|r| {
                let s=sessions.entry(r.id.clone()).or_insert_with(||Session::empty(r.id.clone(),r.role_id.clone()));
                anyhow::ensure!(s.role_id==r.role_id&&s.history_end()==r.history_from&&s.sentences_end()==r.sentences_from,"Session journal sequence mismatch");
                s.generation=r.generation;s.history.extend(r.history);s.sentences.extend(r.sentences);
                s.trim_saved_history(s.history_end());
                s.trim_saved_sentences(s.sentences_end());
                Ok(())
            })?;
            if incomplete {
                // 只有最后一个未提交的半行允许恢复；完整损坏行拒绝启动，不静默丢历史。
                let backup=root.join(format!("sessions-incomplete-{}.jsonl",uuid::Uuid::new_v4()));
                fs::copy(&path,&backup)?;OpenOptions::new().write(true).open(&backup)?.sync_all()?;
                let file=OpenOptions::new().write(true).open(&path)?;file.set_len(complete)?;file.sync_all()?;
            }
        }
        let cursors=sessions.iter().map(|(id,s)|(id.clone(),(s.history_end(),s.sentences_end()))).collect();
        for s in sessions.values_mut(){s.trim_saved_history(s.history_end());s.trim_saved_sentences(s.sentences_end());}
        Ok((Self{path,cursors:Arc::new(Mutex::new(cursors))},sessions))
    }
    pub async fn history_page(&self,id:&str,before:usize,limit:usize,sessions:&Mutex<HashMap<String,Session>>)->anyhow::Result<(usize,Vec<History>)> {
        let writer=self.cursors.clone().lock_owned().await;
        let (start,end,base,tail)={
            let all=sessions.lock().await;let s=all.get(id).ok_or_else(||anyhow::anyhow!("Session missing"))?;
            let end=before.min(s.history_end());let start=end.saturating_sub(limit);
            let a=start.max(s.history_base).min(end);let tail=if end>s.history_base{s.history[a-s.history_base..end-s.history_base].to_vec()}else{vec![]};
            (start,end,s.history_base,tail)
        };
        if start>=base {return Ok((start,tail));}
        let path=self.path.clone();let id=id.to_owned();
        tokio::task::spawn_blocking(move ||->anyhow::Result<(usize,Vec<History>)> {
            let _writer=writer;let mut rows=Vec::new();
            let mut next=0;
            let (_,incomplete)=scan_records(&path,|r| {
                if r.id!=id{return Ok(());}
                anyhow::ensure!(r.history_from==next,"History journal sequence mismatch");
                next+=r.history.len();
                for (offset,row) in r.history.into_iter().enumerate(){let index=r.history_from+offset;if index>=start&&index<end.min(base){rows.push(row);}}
                Ok(())
            })?;
            anyhow::ensure!(!incomplete,"Incomplete history journal");
            rows.extend(tail);anyhow::ensure!(rows.len()==end-start,"History page gap");Ok((start,rows))
        }).await?
    }
    pub async fn save(&self,id:&str,sessions:&Mutex<HashMap<String,Session>>)->anyhow::Result<()> {
        let mut cursors=self.cursors.clone().lock_owned().await;
        let (h,n)=cursors.get(id).copied().unwrap_or_default();
        let record={
            let all=sessions.lock().await;
            let s=all.get(id).ok_or_else(||anyhow::anyhow!("Session missing"))?;
            anyhow::ensure!(h>=s.history_base&&h<=s.history_end()&&n>=s.sentences_base&&n<=s.sentences_end(),"Session history was truncated");
            Record{version:1,id:s.id.clone(),role_id:s.role_id.clone(),generation:s.generation,history_from:h,sentences_from:n,history:s.history[h-s.history_base..].to_vec(),sentences:s.sentences[n-s.sentences_base..].to_vec()}
        };
        let next=(h+record.history.len(),n+record.sentences.len());
        let path=self.path.clone();
        let saved_id=id.to_owned();
        tokio::task::spawn_blocking(move ||->anyhow::Result<()> {
            let mut data=serde_json::to_vec(&record)?;data.push(b'\n');
            let mut file=OpenOptions::new().append(true).open(path)?;
            let length=file.metadata()?.len();
            if let Err(error)=file.write_all(&data).and_then(|_|file.sync_all()) {
                file.set_len(length)?;file.sync_all()?;return Err(error.into());
            }
            // 等待者取消也不能提前释放写入顺序或丢失已经落盘的游标。
            cursors.insert(saved_id,next);Ok(())
        }).await??;
        if let Some(s)=sessions.lock().await.get_mut(id){s.trim_saved_history(next.0);s.trim_saved_sentences(next.1);}
        Ok(())
    }
    pub async fn find_sentence(&self,id:&str,turn:&str,index:usize,sessions:&Mutex<HashMap<String,Session>>)->anyhow::Result<Option<Sentence>> {
        let writer=self.cursors.clone().lock_owned().await;
        {
            let all=sessions.lock().await;let s=all.get(id).ok_or_else(||anyhow::anyhow!("Session missing"))?;
            if let Some(row)=s.sentences.iter().find(|x|x.turn_id==turn&&x.index==index){return Ok(Some(row.clone()));}
            if s.sentences_base==0{return Ok(None);}
        }
        let path=self.path.clone();let id=id.to_owned();let turn=turn.to_owned();
        tokio::task::spawn_blocking(move || {
            let _writer=writer;let mut found=None;
            let (_,incomplete)=scan_records(&path,|r| {
                if r.id==id&&found.is_none(){found=r.sentences.into_iter().find(|s|s.turn_id==turn&&s.index==index);}Ok(())
            })?;
            anyhow::ensure!(!incomplete,"Incomplete sentence journal");Ok(found)
        }).await?
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn root()->PathBuf {std::env::temp_dir().join(format!("xr-store-{}",uuid::Uuid::new_v4()))}
    #[test]
    fn migration_recovers_partial_backup_without_discarding_it() {
        let root=root();fs::create_dir_all(&root).unwrap();
        let old=serde_json::to_vec(&HashMap::from([("s",Session::empty("s".into(),"feiyu".into()))])).unwrap();
        let partial=&old[..old.len()/2];
        fs::write(root.join("sessions.json"),&old).unwrap();
        fs::write(root.join("sessions.pre-journal.json"),partial).unwrap();
        let (_,sessions)=SessionStore::open(&root).unwrap();assert!(sessions.contains_key("s"));
        assert_eq!(fs::read(root.join("sessions.json")).unwrap(),old);
        assert_eq!(fs::read(root.join("sessions.pre-journal.json")).unwrap(),old);
        let archived=fs::read_dir(&root).unwrap().map(|x|x.unwrap().path()).find(|x|x.file_name().unwrap().to_string_lossy().starts_with("sessions.pre-journal-incomplete-")).unwrap();
        assert_eq!(fs::read(archived).unwrap(),partial);
        assert!(SessionStore::open(&root).is_ok());
    }
    #[test]
    fn migration_refuses_conflicting_backup_without_committing_journal() {
        let root=root();fs::create_dir_all(&root).unwrap();
        let old=b"{}";let backup=b"{\"other\":true}";
        fs::write(root.join("sessions.json"),old).unwrap();fs::write(root.join("sessions.pre-journal.json"),backup).unwrap();
        assert!(SessionStore::open(&root).is_err());
        assert_eq!(fs::read(root.join("sessions.json")).unwrap(),old);
        assert_eq!(fs::read(root.join("sessions.pre-journal.json")).unwrap(),backup);
        assert!(!root.join("sessions-v1.jsonl").exists());
    }
    #[tokio::test]
    async fn historical_pages_reject_corruption_and_uncommitted_tail_without_mutation() {
        let root=root();let (store,_)=SessionStore::open(&root).unwrap();
        let mut s=Session::empty("s".into(),"feiyu".into());
        s.history=(0..150).map(|i|History{speaker:"user".into(),text:i.to_string()}).collect();
        let sessions=Mutex::new(HashMap::from([("s".into(),s)]));store.save("s",&sessions).await.unwrap();
        let valid=fs::read_to_string(&store.path).unwrap();
        let tail=serde_json::to_string(&Record{version:1,id:"s".into(),role_id:"feiyu".into(),generation:0,history_from:150,sentences_from:0,history:vec![],sentences:vec![]}).unwrap();
        for corrupt in [format!("{valid}{tail}"),valid.replacen("sessionStoreVersion\":1","sessionStoreVersion\":2",1),valid.replacen("\"version\":1","\"version\":2",1),valid.replacen("\"history_from\":0","\"history_from\":1",1)] {
            fs::write(&store.path,&corrupt).unwrap();
            assert!(store.history_page("s",25,25,&sessions).await.is_err());
            assert_eq!(fs::read_to_string(&store.path).unwrap(),corrupt);
        }
        fs::write(&store.path,valid).unwrap();
        assert_eq!(store.history_page("s",25,25,&sessions).await.unwrap().1[24].text,"24");
    }
    #[tokio::test]
    async fn sentence_cache_preserves_current_turn_and_recovers_old_rows_from_disk() {
        let root=root();let (store,_)=SessionStore::open(&root).unwrap();
        let mut s=Session::empty("s".into(),"feiyu".into());
        s.sentences=(0..1000).map(|i|Sentence{turn_id:format!("turn-{i}"),index:7,message:format!("正文 {i}"),audio_file:format!("{i}.wav"),..Default::default()}).collect();
        let sessions=Mutex::new(HashMap::from([("s".into(),s)]));
        store.save("s",&sessions).await.unwrap();
        assert_eq!(sessions.lock().await["s"].sentences.len(),100);
        for i in [0,899,900,999] {
            let row=store.find_sentence("s",&format!("turn-{i}"),7,&sessions).await.unwrap().unwrap();
            assert_eq!(row.message,format!("正文 {i}"));assert_eq!(row.audio_file,format!("{i}.wav"));
        }
        assert!(store.find_sentence("s","turn-0",0,&sessions).await.unwrap().is_none());
        {
            let mut all=sessions.lock().await;let s=all.get_mut("s").unwrap();
            s.sentences.extend((0..150).map(|index|Sentence{turn_id:"active".into(),index,..Default::default()}));
            s.trim_saved_sentences(1000);
            assert_eq!(s.sentences.len(),150);assert_eq!(s.sentences_base,1000);
        }
        store.save("s",&sessions).await.unwrap();
        let (reopened,all)=SessionStore::open(&root).unwrap();
        assert_eq!(all["s"].sentences_end(),1150);assert_eq!(all["s"].sentences.len(),150);
        assert_eq!(reopened.find_sentence("s","turn-0",7,&Mutex::new(all)).await.unwrap().unwrap().audio_file,"0.wav");
        let output=root.join("export.json");SessionStore::export_legacy(&root,&output).unwrap();
        let exported:HashMap<String,Session>=serde_json::from_slice(&fs::read(output).unwrap()).unwrap();
        assert_eq!(exported["s"].sentences.len(),1150);assert_eq!(exported["s"].sentences[1149].index,149);
    }
    #[tokio::test]
    async fn audio_inventory_counts_unique_references_without_deleting_files() {
        let root=root();let (store,_)=SessionStore::open(&root).unwrap();fs::create_dir(root.join("audio")).unwrap();
        let mut s=Session::empty("s".into(),"feiyu".into());
        for file in ["used.wav","USED.wav","missing.wav","../outside.wav",""]{s.sentences.push(Sentence{audio_file:file.into(),..Default::default()});}
        let sessions=Mutex::new(HashMap::from([("s".into(),s)]));store.save("s",&sessions).await.unwrap();
        fs::write(root.join("audio/used.wav"),b"1234").unwrap();fs::write(root.join("audio/orphan.wav"),b"123456").unwrap();fs::write(root.join("audio/note.txt"),b"keep").unwrap();
        let inventory=SessionStore::audio_inventory(&root).unwrap();
        assert_eq!(inventory.referenced_files,2);assert_eq!(inventory.referenced_bytes,4);assert_eq!(inventory.missing_references,1);
        assert_eq!(inventory.unreferenced_files,1);assert_eq!(inventory.unreferenced_bytes,6);assert_eq!(inventory.invalid_references,1);assert_eq!(inventory.skipped_entries,1);
        assert_eq!(fs::read_dir(root.join("audio")).unwrap().count(),3);
    }
    #[test]
    fn export_and_inventory_refuse_uncommitted_tail_without_repairing_input() {
        let root=root();let (store,_)=SessionStore::open(&root).unwrap();
        let tail=br#"{"version":1,"id":"s","role_id":"feiyu","generation":0,"history_from":0,"sentences_from":0,"history":[],"sentences":[]}"#;
        OpenOptions::new().append(true).open(&store.path).unwrap().write_all(tail).unwrap();
        let before=fs::read(&store.path).unwrap();let output=root.join("export.json");
        assert!(SessionStore::export_legacy(&root,&output).is_err());assert!(!output.exists());
        assert!(SessionStore::audio_inventory(&root).is_err());assert_eq!(fs::read(&store.path).unwrap(),before);
    }
    #[tokio::test]
    async fn migration_restart_and_delta_preserve_order_and_audio() {
        let root=root();fs::create_dir_all(&root).unwrap();
        let mut s=Session::empty("s".into(),"feiyu".into());
        s.history.push(History{speaker:"user".into(),text:"旧记录".into()});
        s.sentences.push(Sentence{audio_file:"old.wav".into(),..Default::default()});
        let old=serde_json::to_vec(&HashMap::from([("s",s)])).unwrap();fs::write(root.join("sessions.json"),&old).unwrap();
        fs::write(root.join("sessions-v1.migrating"),b"interrupted").unwrap();
        let (store,all)=SessionStore::open(&root).unwrap();let sessions=Mutex::new(all);
        let initial=fs::metadata(&store.path).unwrap().len();
        sessions.lock().await.get_mut("s").unwrap().history.push(History{speaker:"assistant".into(),text:"新增".into()});
        store.save("s",&sessions).await.unwrap();store.save("s",&sessions).await.unwrap();
        let data=fs::read(&store.path).unwrap();assert!(!String::from_utf8_lossy(&data[initial as usize..]).contains("旧记录"));
        for _ in 0..2 {let (_,all)=SessionStore::open(&root).unwrap();assert_eq!(all["s"].history.len(),2);assert_eq!(all["s"].sentences[0].audio_file,"old.wav");}
        assert_eq!(fs::read(root.join("sessions.pre-journal.json")).unwrap(),old);
        assert_eq!(fs::read(root.join("sessions.json")).unwrap(),old);
        let output=root.join("export.json");SessionStore::export_legacy(&root,&output).unwrap();
        let exported:HashMap<String,Session>=serde_json::from_slice(&fs::read(&output).unwrap()).unwrap();
        assert_eq!(exported["s"].history.len(),2);assert_eq!(exported["s"].sentences[0].audio_file,"old.wav");
        assert!(SessionStore::export_legacy(&root,&output).is_err());
    }
    #[test]
    fn incomplete_tail_recovers_but_complete_corruption_is_not_ignored() {
        let root=root();let (store,_)=SessionStore::open(&root).unwrap();
        OpenOptions::new().append(true).open(&store.path).unwrap().write_all(b"{incomplete").unwrap();
        assert!(SessionStore::open(&root).is_ok());
        assert!(fs::read_dir(&root).unwrap().any(|x|x.unwrap().file_name().to_string_lossy().starts_with("sessions-incomplete-")));
        OpenOptions::new().append(true).open(&store.path).unwrap().write_all(b"invalid\n").unwrap();
        assert!(SessionStore::open(&root).is_err());
    }
    #[tokio::test]
    async fn failed_append_can_retry_without_advancing_cursor() {
        let root=root();let (store,_)=SessionStore::open(&root).unwrap();let sessions=Mutex::new(HashMap::from([("s".into(),Session::empty("s".into(),"feiyu".into()))]));
        let moved=root.join("held-journal");fs::rename(&store.path,&moved).unwrap();
        assert!(store.save("s",&sessions).await.is_err());assert!(store.cursors.lock().await.is_empty());
        fs::rename(moved,&store.path).unwrap();store.save("s",&sessions).await.unwrap();assert!(SessionStore::open(&root).unwrap().1.contains_key("s"));
    }
    #[tokio::test]
    async fn long_history_append_is_small_and_waiting_writer_does_not_lock_sessions() {
        let root=root();let (store,_)=SessionStore::open(&root).unwrap();let store=Arc::new(store);
        let mut s=Session::empty("s".into(),"feiyu".into());
        s.history=(0..50_000).map(|i|History{speaker:"user".into(),text:format!("历史记录 {i}")}).collect();
        let sessions=Arc::new(Mutex::new(HashMap::from([("s".into(),s)])));
        store.save("s",&sessions).await.unwrap();let before=fs::metadata(&store.path).unwrap().len();
        let writer=store.cursors.lock().await;
        let pending=tokio::spawn({let store=store.clone();let sessions=sessions.clone();async move{store.save("s",&sessions).await}});
        tokio::task::yield_now().await;
        tokio::time::timeout(std::time::Duration::from_secs(1),async {
            let mut all=sessions.lock().await;let s=all.get_mut("s").unwrap();s.generation+=1;s.busy=false;
            s.history.push(History{speaker:"assistant".into(),text:"新记录".into()});
        }).await.expect("Waiting storage blocked session state");
        assert!(!pending.is_finished());drop(writer);pending.await.unwrap().unwrap();
        let growth=fs::metadata(&store.path).unwrap().len()-before;assert!(growth<512,"Appended {growth} bytes for one row");
        let (reopened,loaded)=SessionStore::open(&root).unwrap();assert_eq!(loaded["s"].history_end(),50_001);assert_eq!(loaded["s"].history.len(),100);
        let loaded=Mutex::new(loaded);
        for before in [50_001,49_940,49_905,200,1,0] {
            let (start,page)=reopened.history_page("s",before,50,&loaded).await.unwrap();
            assert_eq!(start,before.saturating_sub(50));assert_eq!(page.len(),before-start);
            for (offset,row) in page.iter().enumerate(){let index=start+offset;assert_eq!(row.text,if index==50_000{"新记录".into()}else{format!("历史记录 {index}")});}
        }
    }
}
