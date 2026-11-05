using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LingChatXR.Chat
{
    // 结构/文案/交互迁移自 LingChat VrmActionPreview.vue + VrmMotionLibrary.vue（AGPL-3.0）。
    // 直接检查场景角色；动作许可草稿仍由素材库单独保存。
    public sealed class XrMotionWindow : IDisposable
    {
        public VisualElement Root{get;}
        public bool IsOpen=>Root.style.display==DisplayStyle.Flex;
        public FeiyuAvatar PreviewAvatar=>avatar;
        public string SelectedId=>selected;
        public Action<bool> VisibilityChanged{get;set;}
        readonly ScrollView controls;
        readonly Label status;
        Button playButton;
        readonly Dictionary<string,string> choices=new Dictionary<string,string>();
        FeiyuAvatar avatar;
        FeiyuPreview live; XrMotionPreferences draft;
        XrMotionAsset[] catalog=Array.Empty<XrMotionAsset>();
        string sha,selected="",query="",scope="current",usage="speaking",source="generic",selectedFace;
        bool library,preparing;
        int revision;
        public string LoadStatus=>status.text;
        public bool Preparing=>preparing;
        public XrMotionWindow(VisualElement root)
        {
            Root=Box(root,"motion-overlay");Root.name="motion-window";Root.focusable=true;Root.style.display=DisplayStyle.None;
            var shell=Box(Root,"motion-window");
            var aside=Box(shell,"motion-aside");var top=Box(aside,"motion-tabs");
            Button(top,"动作预览",()=>{library=false;Render();},"motion-tab").name="motion-preview-tab";Button(top,"动作许可",ShowLibrary,"motion-tab").name="motion-library-tab";Button(top,"×",Close,"motion-close");
            controls=new ScrollView();controls.AddToClassList("motion-controls");aside.Add(controls);
            status=Text(aside,"","motion-status");
            Root.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Escape){Close();e.StopPropagation();}});
        }
        public async void Open()
        {
            if(IsOpen)return;
            live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
            if(live==null || !live.Ready)return;
            avatar=live.Avatar; sha=avatar.Package.model.sha256;
            Root.EnableInClassList("review-on-left",live.ViewCamera.WorldToViewportPoint(avatar.transform.position).x>.55f);
            draft=JObject.FromObject(avatar.Motions.Preferences).ToObject<XrMotionPreferences>();
            Root.style.display=DisplayStyle.Flex;
            VisibilityChanged?.Invoke(true);
            library=false;preparing=true;controls.Clear();status.text="读取动作清单…";
            int ticket=++revision;
            try
            {
                await avatar.Motions.CatalogReady;
                if(ticket!=revision || !IsOpen || live==null || live.Avatar!=avatar)return;
                catalog=avatar.Motions.Catalog;
                selected=choices.TryGetValue(sha,out var prior)?prior:catalog.FirstOrDefault(a=>a.kind=="wave")?.id;
                if(!catalog.Any(x=>x.id==selected))selected=catalog.FirstOrDefault()?.id;
                selectedFace=avatar.InspectionExpressions.FirstOrDefault();
                preparing=false;Render();
            }
            catch(Exception error){if(ticket==revision){preparing=false;status.text=error.Message;}}
        }
        public void Tick()
        {
            if(!IsOpen)return;
            if(live==null || !live.Ready || live.Avatar!=avatar){Close();return;}
            if(preparing)return;
            bool placed=live.Interaction.State==Core.PetState.Idle;
            status.text=!placed?"请先将角色放稳":avatar.InspectedExpression!=null?"正在检查表情 · "+avatar.InspectedExpression:avatar.Motions.Status;
            playButton?.SetEnabled(placed && !string.IsNullOrEmpty(selected));
        }
        void Render()
        {
            Root.EnableInClassList("motion-library-mode",library);
            Root.Q<Button>("motion-preview-tab").EnableInClassList("selected",!library);Root.Q<Button>("motion-library-tab").EnableInClassList("selected",library);
            controls.Clear();playButton=null;if(library)Library();else Preview();
        }
        public void ShowLibrary(){library=true;Render();}
        void Preview()
        {
            Text(controls,"场景角色检查","motion-selection");
            Select(controls,"可预览动作（"+catalog.Length+" 段）",catalog.Select(a=>a.label).ToList(),Mathf.Max(0,Array.FindIndex(catalog,a=>a.id==selected)),i=>SelectAction(catalog[i].id,true));
            var nav=Box(controls,"motion-row");Button(nav,"← 上一个",()=>Step(-1));Button(nav,"下一个 →",()=>Step(1));
            var item=catalog.FirstOrDefault(a=>a.id==selected);
            Text(controls,item?.label??"暂未安装动作","motion-selection");
            Text(controls,item==null?"":item.builtin?"LingChat 标准动作 · 仅占用头部":System.IO.Path.GetFileName(item.asset)+" · 原速 / 原地","motion-muted");
            Text(controls,item?.loop==true?"循环素材 · 试播一个周期":"待机 → 动作 → 收势 → 当前待机","motion-description");
            playButton=Button(controls,"在角色上播放",()=>SelectAction(selected,true),"settings-primary");
            var faces=avatar.InspectionExpressions.ToList();
            Select(controls,"模型表情",faces,Mathf.Max(0,faces.IndexOf(selectedFace)),i=>SelectExpression(faces[i]));
            Button(controls,"显示所选表情",()=>SelectExpression(selectedFace));
            Button(controls,"结束检查，恢复角色",StopInspection);
        }
        public bool SelectExpression(string name)
        {
            if(avatar==null || live?.Interaction?.State!=Core.PetState.Idle)return false;
            selectedFace=name;return avatar.InspectExpression(name);
        }
        void StopInspection(){if(avatar==null)return;avatar.Motions.EndInspection();avatar.EndExpressionInspection();}
        public void SelectAction(string id,bool play)
        {
            if(!catalog.Any(a=>a.id==id))return;
            selected=id;
            if(play && avatar!=null && live.Interaction.State==Core.PetState.Idle)_=avatar.Motions.InspectAction(id);
            Render();
        }
        void Step(int direction){if(catalog.Length==0)return;int i=Array.FindIndex(catalog,a=>a.id==selected);SelectAction(catalog[(i+direction+catalog.Length)%catalog.Length].id,true);}
        void Library()
        {
            Text(controls,"动作风格与素材库","motion-selection");
            Toggle(controls,"允许场景动作",draft.enabled,v=>draft.enabled=v);
            Toggle(controls,"自然说话微动",draft.conversation,v=>draft.conversation=v);
            Toggle(controls,"按对话语境选择已许可动作",draft.director,v=>draft.director=v);
            Text(controls,"语境选择复用现有对话服务；仅使用下方允许的动作。","motion-muted");
            var idleAssets=catalog.Where(a=>a.loop).ToArray();var idleNames=new List<string>{"基础自然待机"};idleNames.AddRange(idleAssets.Select(a=>a.label));
            Select(controls,"默认待机",idleNames,Array.FindIndex(idleAssets,a=>a.id==draft.idle)+1,i=>{draft.idle=i==0?"none":idleAssets[i-1].id;if(i>0)draft.items[draft.idle]=true;});
            Select(controls,"动作风格",new List<string>{"含蓄","普通","活泼"},Array.IndexOf(new[]{"reserved","normal","lively"},draft.style),i=>{draft.style=new[]{"reserved","normal","lively"}[i];Render();});
            Select(controls,"素材显示范围",new List<string>{"当前风格","全部风格"},scope=="current"?0:1,i=>{scope=i==0?"current":"all";Render();});
            var usages=Box(controls,"motion-row");Button(usages,"自然说话",()=>{usage="speaking";Render();});Button(usages,"场景动作",()=>{usage="scene";Render();});
            if(usage=="speaking"){Toggle(controls,"允许自动穿插说话素材",draft.automatic,v=>draft.automatic=v);Text(controls,"跟随实际人声的句间节奏；已有动作与手部互动优先，明确拒绝动作时不自动补动作。","motion-muted");}
            var sources=Box(controls,"motion-row");Button(sources,"角色原生（0）",()=>{source="native";Render();});Button(sources,"通用动作（"+catalog.Count(a=>!a.loop)+"）",()=>{source="generic";Render();});
            var group=catalog.Where(a=>!a.loop&&source=="generic"&&a.Speaking==(usage=="speaking")&&(scope=="all"||a.MatchesStyle(draft.style))).ToArray();
            Text(controls,"本组 "+group.Count(a=>draft.ItemEnabled(a.id))+" / "+group.Length+" 已启用","motion-muted");
            var search=new TextField{value=query};search.AddToClassList("motion-search");search.label="搜索本组";controls.Add(search);
            var list=new VisualElement();
            void Rows()
            {
                list.Clear();foreach(var item in group.Where(a=>string.IsNullOrEmpty(query)||a.label.Contains(query)||a.id.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0))
                {
                    var row=Box(list,"motion-item");var line=Box(row,"motion-row");
                    Toggle(line,item.label,draft.ItemEnabled(item.id),v=>draft.items[item.id]=v);
                    Button(line,"预览",()=>{library=false;SelectAction(item.id,true);});
                    Text(row,item.Speaking?"说话素材 · "+item.style:"场景动作 · "+item.style,"motion-muted");
                }
                if(list.childCount==0)Text(list,source=="native"?"当前模型没有兼容的原生动作包。":"没有匹配的动作。","motion-muted");
            }
            search.RegisterValueChangedCallback(e=>{query=e.newValue;Rows();});
            var bulk=Box(controls,"motion-row");Button(bulk,"启用本组",()=>{foreach(var a in group)draft.items[a.id]=true;Render();});Button(bulk,"停用本组",()=>{foreach(var a in group)draft.items[a.id]=false;Render();});
            controls.Add(list);Rows();
            Text(controls,"勾选是当前模型的本机使用许可，不代表上游兼容或公开分发已获确认。预览选择不会改变这些开关。","motion-muted");
            Button(controls,"保存并应用",()=>Save(),"settings-primary");
        }
        public bool Save()
        {
            if(live==null||!live.Ready||live.Avatar!=avatar || live.Avatar.Package.model.sha256!=sha)return false;
            try{draft.Save(sha);live.Avatar.Motions.ApplyPreferences(JObject.FromObject(draft).ToObject<XrMotionPreferences>());status.text="已保存并应用 · 预览保持打开";return true;}
            catch(Exception e)when(PreferenceFiles.IsStorageError(e)){status.text="保存失败，原设置已保留";return false;}
        }
        public void Close()
        {
            if(!IsOpen)return;
            ++revision;preparing=false;
            if(sha!=null && draft!=null)choices[sha]=selected;
            StopInspection();avatar=null;
            Root.style.display=DisplayStyle.None;VisibilityChanged?.Invoke(false);
        }
        public void Dispose()=>Close();
        static VisualElement Box(VisualElement parent,string css){var e=new VisualElement();e.AddToClassList(css);parent.Add(e);return e;}
        static Label Text(VisualElement parent,string text,string css){var e=new Label(text){enableRichText=false};e.AddToClassList(css);parent.Add(e);return e;}
        static Button Button(VisualElement parent,string text,Action action,string css="settings-secondary"){var e=new Button(action){text=text};e.AddToClassList(css);parent.Add(e);return e;}
        static void Toggle(VisualElement parent,string text,bool value,Action<bool> change){var e=new Toggle(text){value=value};parent.Add(e);e.RegisterValueChangedCallback(v=>change(v.newValue));}
        static void Select(VisualElement parent,string label,List<string> choices,int index,Action<int> change)
        {if(choices.Count==0)return;var e=new DropdownField(label,choices,Mathf.Clamp(index,0,choices.Count-1));parent.Add(e);e.RegisterValueChangedCallback(v=>{int i=choices.IndexOf(v.newValue);if(i>=0)change(i);});}
    }
}
