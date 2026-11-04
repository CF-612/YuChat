//! 有序追加会话增量；磁盘操作不持有宿主会话锁。旧 JSON 保留不覆盖。
use std::{collections::HashMap, fs::{self, OpenOptions}, io::Write, path::{Path,PathBuf}, sync::Arc};
use serde::{Serialize,Deserialize};
use tokio::sync::Mutex;
use crate::host::{Session,History,Sentence};

#[derive(Serialize,Deserialize)]
struct Record {
    version:u32, id:String, role_id:String, generation:u64,
    history_from:usize, sentences_from:usize, history:Vec<History>, sentences:Vec<Sentence>,
}
pub struct SessionStore { path:PathBuf, cursors:Arc<Mutex<HashMap<String,(usize,usize)>>> }
impl SessionStore {
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
                if !backup.exists(){let mut f=OpenOptions::new().create_new(true).write(true).open(backup)?;f.write_all(&data)?;f.sync_all()?;}
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
            let data=fs::read(&path)?;
            let complete=data.iter().rposition(|b|*b==b'\n').map_or(0,|i|i+1);
            let mut lines=data[..complete].split(|b|*b==b'\n').filter(|line|!line.is_empty());
            anyhow::ensure!(serde_json::from_slice::<serde_json::Value>(lines.next().ok_or_else(||anyhow::anyhow!("Missing journal header"))?)?["sessionStoreVersion"]==1,"Unsupported session store");
            for line in lines {
                let r:Record=serde_json::from_slice(line)?;
                anyhow::ensure!(r.version==1,"Unsupported record");
                let s=sessions.entry(r.id.clone()).or_insert_with(||Session::empty(r.id.clone(),r.role_id.clone()));
                anyhow::ensure!(s.role_id==r.role_id&&s.history.len()==r.history_from&&s.sentences.len()==r.sentences_from,"Session journal sequence mismatch");
                s.generation=r.generation;s.history.extend(r.history);s.sentences.extend(r.sentences);
            }
            if complete<data.len() {
                // 只有最后一个未提交的半行允许恢复；完整损坏行拒绝启动，不静默丢历史。
                let backup=root.join(format!("sessions-incomplete-{}.jsonl",uuid::Uuid::new_v4()));
                fs::write(backup,&data)?;
                let file=OpenOptions::new().write(true).open(&path)?;file.set_len(complete as u64)?;file.sync_all()?;
            }
        }
        let cursors=sessions.iter().map(|(id,s)|(id.clone(),(s.history.len(),s.sentences.len()))).collect();
        Ok((Self{path,cursors:Arc::new(Mutex::new(cursors))},sessions))
    }
    pub async fn save(&self,id:&str,sessions:&Mutex<HashMap<String,Session>>)->anyhow::Result<()> {
        let mut cursors=self.cursors.clone().lock_owned().await;
        let (h,n)=cursors.get(id).copied().unwrap_or_default();
        let record={
            let all=sessions.lock().await;
            let s=all.get(id).ok_or_else(||anyhow::anyhow!("Session missing"))?;
            anyhow::ensure!(h<=s.history.len()&&n<=s.sentences.len(),"Session history was truncated");
            Record{version:1,id:s.id.clone(),role_id:s.role_id.clone(),generation:s.generation,history_from:h,sentences_from:n,history:s.history[h..].to_vec(),sentences:s.sentences[n..].to_vec()}
        };
        let next=(h+record.history.len(),n+record.sentences.len());
        let path=self.path.clone();
        let id=id.to_owned();
        tokio::task::spawn_blocking(move ||->anyhow::Result<()> {
            let mut data=serde_json::to_vec(&record)?;data.push(b'\n');
            let mut file=OpenOptions::new().append(true).open(path)?;
            let length=file.metadata()?.len();
            if let Err(error)=file.write_all(&data).and_then(|_|file.sync_all()) {
                file.set_len(length)?;file.sync_all()?;return Err(error.into());
            }
            // 等待者取消也不能提前释放写入顺序或丢失已经落盘的游标。
            cursors.insert(id,next);Ok(())
        }).await??;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn root()->PathBuf {std::env::temp_dir().join(format!("xr-store-{}",uuid::Uuid::new_v4()))}
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
        let (_,loaded)=SessionStore::open(&root).unwrap();assert_eq!(loaded["s"].history.len(),50_001);
    }
}
