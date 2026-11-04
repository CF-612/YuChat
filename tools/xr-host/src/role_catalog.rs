//! 人设及可用声线在宿主集中声明；设备只能选已注册身份，不能提交 provider 配置。
use std::{collections::HashSet,path::Path};
use serde::Deserialize;
use serde_json::{json,Value};

#[derive(Clone,Deserialize)]
#[serde(rename_all="camelCase",deny_unknown_fields)]
pub struct Role {pub id:String,pub label:String,pub persona:String,pub voice_profile:String,pub spoken_language:String}
#[derive(Deserialize)]
#[serde(rename_all="camelCase",deny_unknown_fields)]
pub struct RoleCatalog {schema_version:u32,roles:Vec<Role>}
impl Default for RoleCatalog {fn default()->Self{Self::parse(include_str!("../roles.default.json")).expect("Built-in role catalog must be valid")}}
impl RoleCatalog {
    pub fn load(root:&Path)->anyhow::Result<Self> {
        let path=root.join("roles.json");if !path.exists(){return Ok(Self::default());}
        anyhow::ensure!(path.metadata()?.len()<=65536,"Role catalog too large");Self::parse(&std::fs::read_to_string(path)?)
    }
    pub fn parse(text:&str)->anyhow::Result<Self> {
        let result:Self=serde_json::from_str(text)?;
        anyhow::ensure!(result.schema_version==1&&!result.roles.is_empty()&&result.roles.len()<=32,"Unsupported role catalog");
        let mut ids=HashSet::new();
        for r in &result.roles {
            anyhow::ensure!(!r.id.is_empty()&&r.id.len()<=64&&r.id.bytes().all(|b|b.is_ascii_alphanumeric()||b==b'-'||b==b'_')&&ids.insert(&r.id),"Invalid or duplicate role id");
            anyhow::ensure!(!r.label.trim().is_empty()&&r.label.chars().count()<=80&&!r.persona.trim().is_empty()&&r.persona.chars().count()<=4000,"Invalid role text");
            // 当前只注册这一条真实声线；不能配置一个不存在的 provider 并假装可用。
            anyhow::ensure!(r.voice_profile=="ling-v2"&&r.spoken_language=="ja","Unregistered voice profile or language");
        }
        Ok(result)
    }
    pub fn get(&self,id:&str)->Option<&Role>{self.roles.iter().find(|r|r.id==id)}
    pub fn public(&self)->Value {
        json!({"schemaVersion":1,"roles":self.roles.iter().map(|r|json!({"id":r.id,"label":r.label,"voiceProfile":r.voice_profile,"spokenLanguage":r.spoken_language})).collect::<Vec<_>>(),"voices":[{"id":"ling-v2","label":"LingChat 默认日语","language":"ja"}]})
    }
}
#[cfg(test)] mod tests {
    use super::*;
    #[test] fn additional_role_is_config_only_and_private_persona_is_not_exposed(){
        let mut value:Value=serde_json::from_str(include_str!("../roles.default.json")).unwrap();
        let mut role=value["roles"][0].clone();role["id"]=json!("fixture-third");role["persona"]=json!("private-persona-fixture");value["roles"].as_array_mut().unwrap().push(role);
        let catalog=RoleCatalog::parse(&value.to_string()).unwrap();assert!(catalog.get("fixture-third").is_some());assert_eq!(catalog.public()["roles"].as_array().unwrap().len(),3);assert!(!catalog.public().to_string().contains("private-persona"));
        for field in ["voiceProfile","spokenLanguage","id"]{let mut invalid=value.clone();invalid["roles"][2][field]=json!(if field=="id"{"feiyu"}else{"unknown"});assert!(RoleCatalog::parse(&invalid.to_string()).is_err());}
    }
}
