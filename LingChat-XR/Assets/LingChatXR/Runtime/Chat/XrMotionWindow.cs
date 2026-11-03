using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LingChatXR.Chat
{
    // 结构/文案/交互迁移自 LingChat VrmActionPreview.vue + VrmMotionLibrary.vue（AGPL-3.0）。
    // Web 渲染器由独立 Unity Camera 替代，临时试播不写入主角色许可。
    public sealed class XrMotionWindow : IDisposable
    {
        public VisualElement Root{get;}
        public bool IsOpen=>Root.style.display==DisplayStyle.Flex;
        public FeiyuAvatar PreviewAvatar=>avatar;
        public string SelectedId=>selected;
        public Action LeaveSettings{get;set;}
        readonly ScrollView controls;
        readonly Image image;
        readonly Label status,caption;
        Button playButton;string playLabel;
        readonly Dictionary<string,(string preferences,string idle,string action)> choices=new Dictionary<string,(string,string,string)>();
        CancellationTokenSource lifetime;GameObject stage;Camera camera;RenderTexture target;FeiyuAvatar avatar;
        FeiyuPreview live;XrMotionPreferences draft;XrMotionAsset[] catalog=Array.Empty<XrMotionAsset>();
        string sha,selected="",idleChoice="",query="",scope="current",usage="speaking",source="generic";
        bool library,dragging;Vector2 pointer;float yaw,zoom=.18f;int revision;int oldMask;int capturedPointer=-1;
        bool preparing,failed,maskChanged;float loadStarted;string loadPhase;
        string warmAttemptedSha;
        public string LoadStatus=>status.text;
        public bool Preparing=>preparing;
        public XrMotionWindow(VisualElement root)
        {
            Root=Box(root,"motion-overlay");Root.name="motion-window";Root.focusable=true;Root.style.display=DisplayStyle.None;
            var shell=Box(Root,"motion-window");var model=Box(shell,"motion-model");
            var heading=Box(model,"motion-heading");Text(heading,"动作预览","motion-title");Text(heading,"MOTION LIBRARY","motion-eyebrow");
            image=new Image{scaleMode=ScaleMode.ScaleToFit};image.AddToClassList("motion-stage");model.Add(image);
            caption=Text(model,"拖动旋转 · 滚轮缩放","motion-camera-hint");
            image.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;dragging=true;pointer=e.position;capturedPointer=e.pointerId;image.CapturePointer(e.pointerId);e.StopPropagation();});
            image.RegisterCallback<PointerMoveEvent>(e=>{if(!dragging)return;yaw+=(e.position.x-pointer.x)*.35f;pointer=e.position;PositionCamera();});
            image.RegisterCallback<PointerUpEvent>(e=>{dragging=false;image.ReleasePointer(e.pointerId);capturedPointer=-1;});
            image.RegisterCallback<WheelEvent>(e=>{zoom=Mathf.Clamp(zoom+e.delta.y*.005f,.1f,.4f);PositionCamera();e.StopPropagation();});
            var aside=Box(shell,"motion-aside");var top=Box(aside,"motion-tabs");
            Button(top,"动作预览",()=>{library=false;Render();},"motion-tab").name="motion-preview-tab";Button(top,"动作许可",ShowLibrary,"motion-tab").name="motion-library-tab";Button(top,"×",Close,"motion-close");
            controls=new ScrollView();controls.AddToClassList("motion-controls");aside.Add(controls);
            status=Text(aside,"正在准备模型…","motion-status");
            Root.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Escape){Close();e.StopPropagation();}});
        }
        public async void Open()
        {
            if(IsOpen)return;live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();if(live==null||!live.Ready)return;
            string currentSha=live.Avatar.Package.model.sha256;
            if(preparing&&sha==currentSha){Root.style.display=DisplayStyle.Flex;Root.schedule.Execute(()=>{if(IsOpen)Root.Focus();});return;}
            bool reuse=avatar!=null&&sha==currentSha;
            if(!reuse)ReleaseStage();
            Root.style.display=DisplayStyle.Flex;Root.schedule.Execute(()=>{if(IsOpen)Root.Focus();});int ticket=++revision;lifetime?.Dispose();lifetime=new CancellationTokenSource();
            sha=live.Avatar.Package.model.sha256;draft=JObject.FromObject(live.Avatar.Motions.Preferences).ToObject<XrMotionPreferences>();
            warmAttemptedSha=sha;
            if(reuse)
            {
                failed=false;stage.SetActive(true);image.image=target;oldMask=live.ViewCamera.cullingMask;live.ViewCamera.cullingMask&=~(1<<30);maskChanged=true;
                avatar.Motions.Stop(true);_=avatar.Motions.SetIdle(string.IsNullOrEmpty(idleChoice)?draft.idle:idleChoice);Render();status.text="预览已就绪";return;
            }
            library=false;controls.Clear();status.text="正在准备独立预览…";catalog=Array.Empty<XrMotionAsset>();
            preparing=true;failed=false;loadStarted=Time.realtimeSinceStartup;loadPhase="读取动作目录";
            try
            {
                await live.Avatar.Motions.CatalogReady;if(ticket!=revision)return;
                catalog=live.Avatar.Motions.Catalog;
                if(catalog.Length==0)throw new InvalidOperationException("本地动作目录未安装或无法读取");
                if(choices.TryGetValue(sha,out var choice)&&choice.preferences==JObject.FromObject(draft).ToString()){idleChoice=choice.idle;selected=choice.action;}
                else{idleChoice="";selected=catalog.FirstOrDefault(a=>a.kind=="wave")?.id??catalog.FirstOrDefault()?.id??"";}
                // 目录已就绪即可浏览；只等待试播模型，不把整个面板留成空白。
                Render();
                stage=new GameObject("Motion review stage");stage.transform.position=new Vector3(50,0,0);
                oldMask=live.ViewCamera.cullingMask;live.ViewCamera.cullingMask&=~(1<<30);
                maskChanged=true;
                camera=new GameObject("Motion review camera").AddComponent<Camera>();camera.transform.SetParent(stage.transform,false);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.10f,.15f);camera.orthographic=true;camera.cullingMask=1<<30;
                camera.nearClipPlane=.01f;camera.farClipPlane=4;camera.depth=-10;
                target=new RenderTexture(1000,1000,24);target.Create();camera.targetTexture=target;image.image=target;PositionCamera();
                var model=new GameObject("Motion review avatar");model.transform.SetParent(stage.transform,false);
                var pending=model.AddComponent<FeiyuAvatar>();pending.MotionPreviewMode=true;
                await pending.LoadAsync(Application.streamingAssetsPath+"/Characters/"+live.ActivePackageFolder,.24f,lifetime.Token,
                    progress:phase=>{if(ticket==revision)loadPhase=phase;},sharedSource:live.Avatar);
                if(ticket!=revision||pending==null)return;
                loadPhase="应用渲染与待机";avatar=pending;AnimePresentation.Apply(avatar);
                foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
                await avatar.Motions.CatalogReady;if(ticket!=revision)return;
                await avatar.Motions.SetIdle(string.IsNullOrEmpty(idleChoice)?draft.idle:idleChoice);
                if(ticket!=revision)return;preparing=false;status.text="预览已就绪";
                Render();
                if(!IsOpen){stage.SetActive(false);RestoreMainCamera();}
            }
            catch(OperationCanceledException){}
            catch(Exception e){if(ticket==revision)FailLoading(e.GetType().Name+": "+e.Message);}
        }
        void FailLoading(string reason)
        {
            bool visible=IsOpen;string phase=loadPhase;ReleaseStage();failed=true;
            if(!visible)return;
            Root.style.display=DisplayStyle.Flex;
            status.text="预览准备失败（"+phase+"），可重试";controls.Clear();
            Text(controls,"模型未能完成加载。关闭此窗口仍可使用主场景。","motion-intro");
            Button(controls,"重新加载预览",()=>{Close();Open();},"settings-primary");
            Debug.LogWarning("XR_MOTION_PREVIEW_LOAD_FAILED stage="+phase+" cause="+reason);
        }
        void PositionCamera()
        {
            if(camera==null)return;camera.orthographicSize=zoom;
            Vector3 center=stage.transform.position+Vector3.up*.13f;
            camera.transform.position=center+Quaternion.Euler(0,yaw,0)*new Vector3(0,.015f,.8f);camera.transform.LookAt(center);
        }
        public void Tick()
        {
            var current=live!=null?live:UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
            if((preparing||stage!=null)&&(current==null||!current.Ready||current.Avatar.Package.model.sha256!=sha))
            {Close();ReleaseStage();return;}
            // 每个当前模型最多一份缓存；主角色就绪后后台准备，关闭时不让它继续演算。
            if(!IsOpen&&!preparing&&avatar==null&&current!=null&&current.Ready&&warmAttemptedSha!=current.Avatar.Package.model.sha256)
            {Open();Root.style.display=DisplayStyle.None;}
            if(preparing&&Time.realtimeSinceStartup-loadStarted>45){FailLoading("timeout");return;}
            if(!IsOpen)return;
            if(failed)return;
            if(preparing){float elapsed=Time.realtimeSinceStartup-loadStarted;status.text=loadPhase+" · "+Mathf.FloorToInt(elapsed)+" 秒";if(elapsed>45)FailLoading("timeout");return;}
            if(avatar!=null){avatar.Present(camera.transform.position);status.text=avatar.Motions.Status;if(playButton!=null){playButton.SetEnabled(!avatar.Motions.Busy);playButton.text=avatar.Motions.Busy?"播放中…":playLabel;}}
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
            Text(controls,"先试一遍，再决定是否使用。","motion-intro");
            var idles=new List<string>{"跟随角色设置","基础自然待机"};idles.AddRange(catalog.Where(a=>a.loop).Select(a=>a.label));
            var idleIds=new List<string>{"","none"};idleIds.AddRange(catalog.Where(a=>a.loop).Select(a=>a.id));
            Select(controls,"待机姿态",idles,Mathf.Max(0,idleIds.IndexOf(idleChoice)),async i=>{idleChoice=idleIds[i];if(avatar!=null)await avatar.Motions.SetIdle(string.IsNullOrEmpty(idleChoice)?draft.idle:idleChoice);});
            Select(controls,"可预览动作（"+catalog.Length+" 段）",catalog.Select(a=>a.label).ToList(),Mathf.Max(0,Array.FindIndex(catalog,a=>a.id==selected)),i=>SelectAction(catalog[i].id,true));
            var nav=Box(controls,"motion-row");Button(nav,"← 上一个",()=>Step(-1));Button(nav,"下一个 →",()=>Step(1));
            var item=catalog.FirstOrDefault(a=>a.id==selected);
            Text(controls,item?.label??"暂未安装动作","motion-selection");
            Text(controls,item==null?"":item.builtin?"LingChat 标准动作 · 仅占用头部":System.IO.Path.GetFileName(item.asset)+" · 原速 / 原地","motion-muted");
            Text(controls,item?.loop==true?"进入待机 → 循环保持 → 停止恢复":"待机 → 动作 → 收势 → 当前待机","motion-description");
            playLabel=item?.loop==true?"进入并保持待机":"播放完整过程";
            playButton=Button(controls,playLabel,()=>{if(avatar!=null)_=avatar.Motions.Play(selected);},"settings-primary");playButton.SetEnabled(avatar!=null);
            Button(controls,"停止并回到待机",()=>{if(avatar==null)return;avatar.Motions.Stop();string wanted=string.IsNullOrEmpty(idleChoice)?draft.idle:idleChoice;if(avatar.Motions.IdleId!=wanted)_=avatar.Motions.SetIdle(wanted);}).SetEnabled(avatar!=null);
            Button(controls,"重置预览视角",()=>{yaw=0;zoom=.18f;PositionCamera();});
            Text(controls,"试播不会自动开放聊天动作。当前两模型没有声明可持续保持的原生姿势；相关微调在有兼容资源后开放。","motion-muted");
            var pose=Box(controls,"motion-pose");Text(pose,"持续姿势微调","motion-description");
            pose.Add(new FloatField("过渡时长倍率"){value=1});pose.Add(new FloatField("保持时长倍率"){value=1});pose.SetEnabled(false);
            Text(controls,"本机素材 · 发布许可待确认","motion-muted");
        }
        public void SelectAction(string id,bool play)
        {
            if(!catalog.Any(a=>a.id==id))return;selected=id;
            if(avatar!=null){avatar.Motions.Stop();if(play)_=avatar.Motions.Play(id);}Render();
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
            Button(controls,"在主场景播放已选动作",PlayInScene);
        }
        async void PlayInScene()
        {
            if(!Save()||live?.Ready!=true)return;int ticket=revision;
            bool played=await live.Avatar.Motions.Play(selected);
            if(ticket!=revision)return;
            if(played){Close();LeaveSettings?.Invoke();}else caption.text=live.Avatar.Motions.Status;
        }
        public bool Save()
        {
            if(live==null||!live.Ready||live.Avatar.Package.model.sha256!=sha)return false;
            try{draft.Save(sha);live.Avatar.Motions.ApplyPreferences(JObject.FromObject(draft).ToObject<XrMotionPreferences>());caption.text="已保存并应用 · 预览保持打开";return true;}
            catch(Exception e)when(PreferenceFiles.IsStorageError(e)){caption.text="保存失败，原设置已保留";return false;}
        }
        public void Close()
        {
            if(!IsOpen)return;if(sha!=null&&draft!=null)choices[sha]=(JObject.FromObject(draft).ToString(),idleChoice,selected);
            if(capturedPointer>=0)image.ReleasePointer(capturedPointer);capturedPointer=-1;dragging=false;
            if(preparing)ReleaseStage();
            else{avatar?.Motions.Stop(true);if(stage!=null)stage.SetActive(false);RestoreMainCamera();}
            image.image=null;Root.style.display=DisplayStyle.None;
        }
        void RestoreMainCamera()
        {if(maskChanged&&live!=null&&live.ViewCamera!=null)live.ViewCamera.cullingMask=oldMask;maskChanged=false;}
        void ReleaseStage()
        {
            ++revision;lifetime?.Cancel();lifetime?.Dispose();lifetime=null;preparing=false;
            avatar?.Motions.Dispose();avatar=null;image.image=null;
            if(camera!=null)camera.targetTexture=null;if(target!=null){target.Release();UnityEngine.Object.Destroy(target);}target=null;
            if(stage!=null)UnityEngine.Object.Destroy(stage);stage=null;camera=null;
            RestoreMainCamera();
        }
        public void Dispose(){Close();ReleaseStage();}
        static VisualElement Box(VisualElement parent,string css){var e=new VisualElement();e.AddToClassList(css);parent.Add(e);return e;}
        static Label Text(VisualElement parent,string text,string css){var e=new Label(text){enableRichText=false};e.AddToClassList(css);parent.Add(e);return e;}
        static Button Button(VisualElement parent,string text,Action action,string css="settings-secondary"){var e=new Button(action){text=text};e.AddToClassList(css);parent.Add(e);return e;}
        static void Toggle(VisualElement parent,string text,bool value,Action<bool> change){var e=new Toggle(text){value=value};parent.Add(e);e.RegisterValueChangedCallback(v=>change(v.newValue));}
        static void Select(VisualElement parent,string label,List<string> choices,int index,Action<int> change)
        {if(choices.Count==0)return;var e=new DropdownField(label,choices,Mathf.Clamp(index,0,choices.Count-1));parent.Add(e);e.RegisterValueChangedCallback(v=>{int i=choices.IndexOf(v.newValue);if(i>=0)change(i);});}
    }
}
