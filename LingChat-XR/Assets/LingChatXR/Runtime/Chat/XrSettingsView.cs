using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LingChatXR.Chat
{
    // 仅负责草稿与视图；应用、保存和校验继续由原业务控制器完成。
    public sealed class XrSettingsView
    {
        public VisualElement Root { get; }
        public Action OpenMotions{get;set;}
        readonly XrChatController chat;
        FeiyuPreview preview;
        readonly ScrollView body;
        readonly Label notice,connection;
        readonly Button save;
        readonly List<Button> tabs=new List<Button>();
        readonly bool[] dirty=new bool[4];
        XrPreferences spatial,interaction;
        DialoguePreferences dialogue;
        JObject services;
        string serviceError,folder;
        int group,providerIndex;
        bool saving;
        public bool IsSaving=>saving;
        public bool IsOpen=>Root.style.display==DisplayStyle.Flex;
        public XrSettingsView(VisualElement parent,XrChatController controller)
        {
            chat=controller;Root=Box(parent,"settings-overlay");Root.name="application-settings";Root.focusable=true;Root.style.display=DisplayStyle.None;
            var shell=Box(Root,"settings-shell");
            var top=Box(shell,"settings-top");Label(top,"LingChat XR","settings-eyebrow");Label(top,"设置","settings-title");Button(top,"×",Close,"settings-close");
            var main=Box(shell,"settings-main");var rail=Box(main,"settings-rail");
            string[] labels={"角色与空间","对话与声音","互动与动作","本地服务"};
            for(int i=0;i<labels.Length;i++){int index=i;var b=Button(rail,labels[i],()=>{group=index;Render();},"settings-tab");tabs.Add(b);}
            body=new ScrollView();body.AddToClassList("settings-body");main.Add(body);
            var footer=Box(shell,"settings-footer");notice=Label(footer,"","settings-notice");
            var buttons=Box(footer,"settings-actions");Button(buttons,"恢复本组默认",RestoreGroupDraft,"settings-secondary").name="settings-defaults";
            Button(buttons,"取消",Close,"settings-secondary").name="settings-cancel";
            save=Button(buttons,"保存本组并关闭",()=>Save(),"settings-primary");save.name="settings-save";
            connection=Label(shell,"","settings-connection");
            Root.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Escape){Close();e.StopPropagation();}});
        }
        public void Open(int section=0)
        {
            if(saving)return;
            preview=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();preview?.CancelSizeEdit();spatial=preview?.ReadPreferences()??new XrPreferences();interaction=preview?.ReadPreferences()??new XrPreferences();
            dialogue=chat.ReadDialogue();folder=preview?.ActivePackageFolder;serviceError=null;
            try{services=XrServiceSettings.Read();}catch(Exception e)when(PreferenceFiles.IsStorageError(e)||e is InvalidOperationException){services=null;serviceError="服务配置无法读取，原文件已保留；请先重新连接，或检查本地文件权限";}
            Array.Clear(dirty,0,dirty.Length);group=Mathf.Clamp(section,0,3);providerIndex=0;Root.style.display=DisplayStyle.Flex;Root.schedule.Execute(()=>{if(IsOpen)Root.Focus();});notice.text="更改先保留为草稿；保存仅影响当前分组。";Render();
        }
        public void Close(){if(saving)return;Root.style.display=DisplayStyle.None;services=null;Array.Clear(dirty,0,dirty.Length);}
        public void RefreshStatus(){if(IsOpen)connection.text=chat.Status;}
        void Changed(){dirty[group]=true;notice.text="有未保存更改 · 取消会丢弃草稿";}
        void Render()
        {
            body.Clear();for(int i=0;i<tabs.Count;i++)tabs[i].EnableInClassList("selected",i==group);
            save.SetEnabled(!saving);RefreshStatus();
            if(group==0)Spatial();else if(group==1)Dialogue();else if(group==2)Interaction();else Service();
        }
        void Heading(string title,string subtitle){Label(body,title,"settings-section-title");Label(body,subtitle,"settings-description");}
        void Spatial()
        {
            Heading("角色与空间","外观与对话角色分别管理。每个模型保留自己的身高和活动范围。");
            var card=Box(body,"settings-card");Label(card,"对话角色  ·  "+chat.RoleLabel,"settings-card-title");
            Label(card,"当前声线："+chat.VoiceLabel+" · 中文正文与历史\n切换下方外观不会更改人设、声线或对话历史。","settings-description");
            if(preview==null){Label(body,"此场景未载入模型；声音和服务设置仍可使用。","settings-description");save.SetEnabled(false);return;}
            var folders=preview.PackageFolders;var labels=preview.PackageLabels;
            var choices=labels.Select((s,i)=>s+"  ["+(i+1)+"]").ToList();
            var model=new DropdownField("模型外观",choices,Mathf.Max(0,Array.IndexOf(folders,folder)));body.Add(model);model.name="settings-model";
            model.RegisterValueChangedCallback(e=>{int i=choices.IndexOf(e.newValue);if(i>=0){folder=folders[i];Changed();Render();}});
            bool changing=folder!=preview.ActivePackageFolder;
            if(changing)Label(body,"保存将切换外观并读取它的独立设置；随后需重新确认桌面。","settings-description");
            var dimensions=Box(body,"settings-card");dimensions.SetEnabled(!changing&&preview.Ready);
            Float(dimensions,"身高（cm）",spatial.heightMeters*100,v=>spatial.heightMeters=v/100,"settings-height");
            Float(dimensions,"活动区宽（cm）",spatial.halfWidth*200,v=>spatial.halfWidth=v/200,"settings-width");
            Float(dimensions,"活动区深（cm）",spatial.halfDepth*200,v=>spatial.halfDepth=v/200,"settings-depth");
            Label(dimensions,"身高 12–45 cm；活动区宽 40–120 cm、深 40–80 cm。保存后重新确认空间。","settings-description");
            var row=Box(body,"settings-inline");
            Button(row,"直观调整大小",()=>{if(dirty[0]){notice.text="空间分组还有未保存更改，请先保存或取消。";return;}if(preview.BeginSizeEdit(out _))Close();else notice.text=preview.Status;},"settings-primary").SetEnabled(preview.Ready);
            Button(row,"确认桌面并放置",()=>{if(preview.Place(new Vector3(0,.74f,0)))Close();else notice.text=preview.Status;},"settings-secondary").SetEnabled(preview.Ready);
            Button(row,"重新放置",()=>{preview.ResetPlacement();notice.text="空间已重置，请重新确认桌面。";},"settings-secondary").SetEnabled(preview.Ready);
            Button(row,"重置视角",()=>preview.HandleViewInput(Vector2.zero,0,true),"settings-secondary");
        }
        void Dialogue()
        {
            Heading("对话与声音","从思考的停顿，到一句话的节奏。试听使用已应用的音量。");
            var card=Box(body,"settings-card");
            var entries=chat.AvailableRoles.ToList();
            if(!entries.Any(r=>r.id==dialogue.role))entries.Add(new DialogueRole{id=dialogue.role,label="暂不可用 · "+dialogue.role});
            var roles=entries.Select(r=>r.label+"  ["+r.id+"]").ToList();
            var role=new DropdownField("对话角色",roles,entries.FindIndex(r=>r.id==dialogue.role));card.Add(role);
            role.RegisterValueChangedCallback(e=>{int index=roles.IndexOf(e.newValue);if(index>=0){dialogue.role=entries[index].id;Changed();}});
            Label(card,"切换对话角色会切换人设和历史，保留模型外观；可用角色与声线由当前 PC 服务提供。","settings-description");
            var mode = new DropdownField("角色声音",new System.Collections.Generic.List<string>{"拟声短声","静音","原日语 TTS"},dialogue.voiceMode=="tts"?2:dialogue.voiceMode=="muted"?1:0);
            mode.name="settings-voice-mode"; card.Add(mode);
            mode.RegisterValueChangedCallback(e=>{dialogue.voiceMode=mode.index==2?"tts":mode.index==1?"muted":"clips";Changed();});
            Slider(card,"语音音量",dialogue.voice,v=>dialogue.voice=v,"settings-voice");Slider(card,"音效音量",dialogue.effects,v=>dialogue.effects=v,"settings-effects");
            Toggle(card,"思考与对话音效",dialogue.effectEnabled,v=>dialogue.effectEnabled=v,"settings-fx-enabled");
            var speed=new SliderInt("中文出字速度",1,100){value=dialogue.speed,showInputField=true,name="settings-speed"};card.Add(speed);speed.RegisterValueChangedCallback(e=>{dialogue.speed=e.newValue;Changed();});
            Toggle(card,"AUTO · 语音和文字结束后自动继续",dialogue.auto,v=>dialogue.auto=v,"settings-auto");
            var row=Box(body,"settings-inline");Button(row,"试听当前声线",chat.TestVoice,"settings-primary").SetEnabled(chat.VoiceMode!=LingChatXR.Speech.CharacterVoiceMode.Tts || chat.CanSend);Button(row,"停止播放",chat.ShortVoice.StopVoice,"settings-secondary");
            Label(body,"拟声短声在本机播放，可直接更换素材。原日语 TTS 需连接 PC；正文与历史显示中文。","settings-description");
        }
        void Interaction()
        {
            Heading("互动与动作","从轻轻摸头，到一个招呼。先在独立窗口试播，再为当前角色开启动作。");
            var card=Box(body,"settings-card");Toggle(card,"角色视线跟随",interaction.gazeEnabled,v=>interaction.gazeEnabled=v,"settings-gaze");Toggle(card,"头部触摸反馈",interaction.touchEnabled,v=>interaction.touchEnabled=v,"settings-touch");
            var movement=new Slider("移动速度（倍）",.75f,3f){value=interaction.movementSpeed,showInputField=true,name="settings-movement-speed"};card.Add(movement);
            movement.RegisterValueChangedCallback(e=>{interaction.movementSpeed=e.newValue;Changed();});
            Label(card,"1 倍为轻快蹦跳；提高速度会自然切换跑步。连续点选桌面可临时加速，停止催促后恢复设置速度。","settings-description");
            card.SetEnabled(preview!=null&&preview.Ready);
            var motion=Box(body,"settings-card");Label(motion,"动作风格与素材库","settings-card-title");
            Label(motion,"查看全部动作、切换待机、完整试播，并按用途与风格管理当前角色的使用许可。","settings-description");
            Button(motion,"打开动作预览与素材库",()=>OpenMotions?.Invoke(),"settings-primary").SetEnabled(preview!=null&&preview.Ready);
            Label(body,"动作与播放速度跟随实际移动；原普通走路素材保留备用。","settings-description");
            save.SetEnabled(preview!=null&&preview.Ready&&!saving);
        }
        void Service()
        {
            Heading("本地服务","服务凭据仅保存在 PC 的 XR 私有配置中；不会写回 LingChat。");
            var actions=Box(body,"settings-inline");Button(actions,"重新连接",chat.Connect,"settings-secondary");Button(actions,"应用已保存服务配置",chat.ReloadHostSettings,"settings-secondary");
            Label(body,"本地 TTS：ling-v2（日语） · 模型与引擎沿用现有安装\nmy-neuro 中文声线尚未安装，本组不提供空声线选择。","settings-description");
            if(services==null||!XrServiceSettings.Editable){Label(body,serviceError??"头显只管理配对；请在 PC 编辑服务配置。","settings-unavailable");save.SetEnabled(false);return;}
            var providers=(JArray)services["llm.providers"];
            var names=providers.OfType<JObject>().Select((p,i)=>((string)p["label"]??(string)p["model"]??"未命名服务")+" ["+(i+1)+"]").ToList();
            if(names.Count>0)
            {
                foreach(var item in new[]{("对话服务","llm.chat_provider_id"),("翻译服务","llm.translate_provider_id")})
                {
                    string id=(string)services[item.Item2];if(string.IsNullOrEmpty(id))id=(string)services["llm.chat_provider_id"];
                    int selected=providers.ToList().FindIndex(p=>(string)p["id"]==id);
                    var dd=new DropdownField(item.Item1,names,Mathf.Max(0,selected));body.Add(dd);dd.RegisterValueChangedCallback(e=>{services[item.Item2]=providers[names.IndexOf(e.newValue)]["id"].DeepClone();Changed();});
                }
                providerIndex=Mathf.Clamp(providerIndex,0,providers.Count-1);
                var edit=new DropdownField("编辑服务",names,providerIndex);body.Add(edit);edit.RegisterValueChangedCallback(e=>{providerIndex=names.IndexOf(e.newValue);Render();});
                var provider=(JObject)providers[providerIndex];var card=Box(body,"settings-card");
                Text(card,"名称",(string)provider["label"],v=>provider["label"]=v);
                var kinds=new List<string>{"openai","deepseek","gemini","lmstudio"};string current=(string)provider["provider"];if(!kinds.Contains(current))kinds.Add(current??"未选择");
                var kind=new DropdownField("接口类型",kinds,Mathf.Max(0,kinds.IndexOf(current)));card.Add(kind);kind.RegisterValueChangedCallback(e=>{provider["provider"]=e.newValue;Changed();});
                Text(card,"模型名称",(string)provider["model"],v=>provider["model"]=v.Trim());Text(card,"服务地址（可选）",(string)provider["base_url"],v=>provider["base_url"]=v.Trim());
                var key=new TextField("新 API 密钥（留空保留）"){isPasswordField=true};card.Add(key);key.RegisterValueChangedCallback(e=>{if(!string.IsNullOrEmpty(e.newValue)){provider["api_key"]=e.newValue.Trim();Changed();}});
                Label(card,string.IsNullOrEmpty((string)provider["api_key"])?"尚未保存密钥":"已有密钥 · 不回显","settings-description");
                Button(card,"清除该服务密钥（保存后生效）",()=>{provider["api_key"]="";Changed();Render();},"settings-secondary");
            }
            Button(body,"添加服务",()=>{var p=new JObject{{"id",Guid.NewGuid().ToString("N")},{"label","新服务"},{"provider","openai"},{"model",""},{"base_url",""},{"api_key",""}};providers.Add(p);if(providers.Count==1){services["llm.chat_provider_id"]=p["id"].DeepClone();services["llm.translate_provider_id"]=p["id"].DeepClone();}providerIndex=providers.Count-1;Changed();Render();},"settings-secondary");
        }
        public async void Save()
        {
            if(saving)return;
            if(!dirty[group]){Close();return;}
            if(group==0&&preview!=null&&folder!=preview.ActivePackageFolder&&dirty[2]){notice.text="互动分组还有当前模型的草稿，请先保存或取消后再换模型。";return;}
            saving=true;Root.SetEnabled(false);save.SetEnabled(false);string error=null;bool applied=false;
            try
            {
                if(group==0)
                {
                    if(preview==null)error="当前场景没有角色";
                    else if(folder!=preview.ActivePackageFolder){error=await preview.SelectModelAsync(folder);if(error==null){spatial=preview.ReadPreferences();interaction=preview.ReadPreferences();dirty[2]=false;}}
                    else error=await preview.SaveSpatialPreferencesAsync(spatial);
                }
                else if(group==1){if(!chat.SaveDialogue(dialogue,out error)){} }
                else if(group==2)error=preview?.SaveInteractionPreferences(interaction)??(preview==null?"当前场景没有角色":null);
                else {error=services==null?"服务配置未加载":XrServiceSettings.Save(services);if(error==null)chat.ReloadHostSettings();}
                if(error==null){dirty[group]=false;applied=true;}else notice.text=error;
            }
            catch(Exception e)when(PreferenceFiles.IsStorageError(e)||e is InvalidOperationException){notice.text="应用失败，原设置与文件已保留；请重试";}
            finally{saving=false;Root.SetEnabled(true);if(applied)Close();else save.SetEnabled(true);}
        }
        public void RestoreGroupDraft()
        {
            if(group==0){spatial=new XrPreferences{modelSha256=spatial.modelSha256};folder=preview?.ActivePackageFolder;}
            else if(group==1)dialogue=new DialoguePreferences();
            else if(group==2)interaction=new XrPreferences{modelSha256=interaction.modelSha256};
            else{notice.text="服务凭据没有通用默认值；请编辑指定服务。未修改其他分组。";return;}
            Changed();Render();
        }
        static VisualElement Box(VisualElement parent,string css){var e=new VisualElement();e.AddToClassList(css);parent.Add(e);return e;}
        static Label Label(VisualElement parent,string text,string css){var e=new Label(text){enableRichText=false};e.AddToClassList(css);parent.Add(e);return e;}
        static Button Button(VisualElement parent,string text,Action action,string css){var e=new Button(action){text=text};e.AddToClassList(css);parent.Add(e);return e;}
        void Float(VisualElement p,string text,float value,Action<float> change,string name){var e=new FloatField(text){value=value,name=name};p.Add(e);e.RegisterValueChangedCallback(v=>{change(v.newValue);Changed();});}
        void Slider(VisualElement p,string text,float value,Action<float> change,string name){var e=new Slider(text,0,100){value=value*100,showInputField=true,name=name};p.Add(e);e.RegisterValueChangedCallback(v=>{change(v.newValue/100);Changed();});}
        void Toggle(VisualElement p,string text,bool value,Action<bool> change,string name){var e=new Toggle(text){value=value,name=name};p.Add(e);e.RegisterValueChangedCallback(v=>{change(v.newValue);Changed();});}
        void Text(VisualElement p,string text,string value,Action<string> change){var e=new TextField(text){value=value??""};p.Add(e);e.RegisterValueChangedCallback(v=>{change(v.newValue);Changed();});}
    }
}
