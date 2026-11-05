using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using LingChatXR.Chat;

namespace LingChatXR.Editor
{
    public sealed class DirectorPlaytestWindow : EditorWindow
    {
        static readonly HttpClient http=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(40)};
        static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        string input="我要走啦，下次见。",decision="",outcome="",prompt="",cache="",action="",submitted="",state="",catalog="";
        string directorStatus="待检查",generatorStatus="待检查";
        int revision,seed,index,interaction; FeiyuAvatar owner;
        double startedAt;
        bool faceTesting;int faceChoice;
        static readonly string[] FaceLabels={"开心","害羞","心动","疑问","惊讶","生气","悲伤","安心"};
        bool watching;string previous="";
        Vector2 scroll;
        [MenuItem("YuChat/动作实验/自然语言导演试玩")]
        static void Open()=>GetWindow<DirectorPlaytestWindow>("小肥鱼动作导演");
        public static void LaunchPlaytest()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            Open();
            EditorApplication.EnterPlaymode();
        }
        static FeiyuPreview Live=>UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
        static JObject Connection()=>JObject.Parse(File.ReadAllText(Path.Combine(Root,".cache/xr-host-connection.json")));
        async void OnEnable(){
            try{var c=Connection();using(var req=Request(c,"/health"))using(var res=await http.SendAsync(req)){directorStatus=res.IsSuccessStatusCode?"宿主就绪，模型调用待验证":res.StatusCode.ToString();}}
            catch(Exception e){directorStatus=e.Message;}
            try{var json=JObject.Parse(await http.GetStringAsync("http://127.0.0.1:17880/api/health"));generatorStatus=(bool?)json["ready"]==true?"就绪":"准备中";}catch(Exception e){generatorStatus=e.Message;}
            Repaint();
        }
        static HttpRequestMessage Request(JObject c,string path){
            var uri=new Uri((string)c["endpoint"]);if(!uri.IsLoopback)throw new InvalidDataException("试玩只连接本机宿主");
            var req=new HttpRequestMessage(path=="/health"?HttpMethod.Get:HttpMethod.Post,new Uri(uri,path));
            req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",(string)c["token"]);return req;
        }
        void OnDisable()
        {
            revision++;
            if(watching&&owner)owner.Motions.Stop();
            watching=false;
            var demo=Live?Live.GetComponent<ArdyPlaytest>():null;
            if(demo!=null){demo.End();UnityEngine.Object.Destroy(demo);}
        }
        internal string ProbeInput {set=>input=value;}
        internal string Selected=>action;
        internal string Executed=>outcome;
        internal string CachedPackage=>cache;
        void OnInspectorUpdate(){
            var live=Live;
            if(watching && (!live||!live.Ready||live.Avatar!=owner||live.Interaction.Generation!=interaction)){
                revision++;watching=false;outcome="已过期或被交互打断";cache="";
            }
            Repaint();
        }
        static void DrawArdy(FeiyuPreview live)
        {
            var demo=live.GetComponent<ArdyPlaytest>();
            EditorGUILayout.LabelField("Core8 路径试玩",EditorStyles.boldLabel);
            if(demo==null || !demo.Active)
            {
                using(new EditorGUI.DisabledScope(live.Interaction.State!=Core.PetState.Idle))
                    if(GUILayout.Button("固定走路 → ARDY 接步"))
                        (demo!=null?demo:live.gameObject.AddComponent<ArdyPlaytest>()).Begin(live);
            }
            else
            {
                EditorGUILayout.LabelField("控制者 / 目标",demo.Owner+" / "+demo.Target);
                EditorGUILayout.LabelField("请求往返 / 缓冲",demo.RoundTripMs.ToString("0")+" ms / "+demo.Buffered+" 帧");
                if(GUILayout.Button("切换前方 / 侧方目标")) demo.ChangeTarget();
                if(GUILayout.Button("停止路径试玩")){demo.End();UnityEngine.Object.Destroy(demo);}
            }
            if(demo!=null) EditorGUILayout.LabelField(demo.Status);
        }
        void OnGUI(){
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("PC 导演",directorStatus);EditorGUILayout.LabelField("T2M",generatorStatus);
            if(GUILayout.Button("刷新服务状态"))OnEnable();
            var live=Live;
            if(!EditorApplication.isPlaying||!live||!live.Ready){EditorGUILayout.HelpBox("打开预览场景并进入 Play Mode，等待角色就绪。",MessageType.Info);}
            else{
                var player=live.Avatar.Motions;
                EditorGUILayout.LabelField("实际状态",live.Interaction.State.ToString());
                DrawArdy(live);
                if(!live.Interaction.PlaneConfirmed && GUILayout.Button("放置到桌面中心"))live.Place(Vector3.zero);
                if((!player.Preferences.enabled||!player.Preferences.director||!player.Preferences.automatic||!player.Preferences.conversation||player.Preferences.items.ContainsValue(false))&&GUILayout.Button("恢复默认许可（全部开启）"))
                {
                    var prefs=player.Preferences;
                    prefs.enabled=prefs.director=prefs.automatic=prefs.conversation=true;
                    prefs.items.Clear();
                    prefs.Save(live.Avatar.Package.model.sha256);
                    player.ApplyPreferences(prefs);
                }
                faceTesting=EditorGUILayout.Foldout(faceTesting,"表情测试 · 离线语音",true);
                if(faceTesting){
                    var avatar=live.Avatar;
                    var voice=UnityEngine.Object.FindAnyObjectByType<XrChatController>()?.Voice;
                    if(GUILayout.Button("播放样句（含停顿）")&&voice!=null){
                        var clip=Resources.Load<AudioClip>("LocalLingChatEffects/faceSample");
                        if(clip){voice.Stop();clip.LoadAudioData();if(!voice.PlayReaction(clip))outcome=voice.LastStatus;}else outcome="缺少本机离线语音样句";
                    }
                    if(GUILayout.Button("停止样句")&&voice!=null)voice.Stop();
                    faceChoice=EditorGUILayout.Popup("手动表情",faceChoice,FaceLabels);
                    if(GUILayout.Button("预览表情"))avatar.PreviewEmotion=FaceLabels[faceChoice];
                    if(GUILayout.Button("清除手动预览"))avatar.PreviewEmotion=null;
                    EditorGUILayout.HelpBox("眼："+avatar.EyeOwner+"　嘴："+avatar.MouthOwner+"　"+(avatar.UsesMouth24Face?"局部眼嘴配方":"缺少局部互动键，使用标准 VRM 通道"),MessageType.Info);
                }
                input=EditorGUILayout.TextArea(input,GUILayout.MinHeight(48));
                if(GUILayout.Button(outcome=="请求中"?"重新提交给导演":"让导演决定"))_=Run(live,0);
                if(outcome=="请求中"||outcome=="等待生成")
                    EditorGUILayout.HelpBox((outcome=="请求中"?"已收到输入，正在等待导演":"导演已返回，正在等待本地生成")+" · "+(EditorApplication.timeSinceStartup-startedAt).ToString("0.0")+" 秒",MessageType.Info);
                if(GUILayout.Button("停止")){revision++;watching=false;player.Stop();outcome="玩家已停止，待返回请求已过期";}
                using(new EditorGUI.DisabledScope(owner!=live.Avatar||live.Interaction.State!=Core.PetState.Idle||string.IsNullOrEmpty(action))){
                    if(GUILayout.Button("重播本次结果"))_=Run(live,1);
                    if(action=="generate"&&GUILayout.Button("同意图换种子"))_=Run(live,2);
                }
                var candidates=player.Capabilities();
                if(candidates.Length>0){
                    index=Mathf.Clamp(index,0,candidates.Length-1);index=EditorGUILayout.Popup("原库对照",index,candidates.Select(x=>x.label).ToArray());
                    if(GUILayout.Button("直接预览选中片段")){revision++;if(watching)player.Stop();watching=false;PreviewLibrary(player,candidates[index].id,revision);}
                }
                else EditorGUILayout.HelpBox("请在动作许可页启用动作与允许的片段。",MessageType.Info);
            }
            EditorGUILayout.LabelField("本轮输入 / 状态",submitted+" / "+state);
            EditorGUILayout.HelpBox("候选："+catalog+"\n导演："+decision+"\n英文动作："+prompt+"\n种子："+seed+"\n执行："+outcome,MessageType.None);
            if(previous.Length>0)EditorGUILayout.LabelField("上轮记录",previous);
            EditorGUILayout.EndScrollView();
        }
        async void PreviewLibrary(XrMotionPlayer player,string id,int ticket){bool ok=await player.Play(id);if(ticket==revision)outcome=ok?"库内对照："+id:"未播放："+player.Status;}
        bool Current(int ticket,FeiyuPreview live)=>ticket==revision&&live&&live.Ready&&live.Avatar==owner&&live.Interaction.Generation==interaction;
        internal async Task Run(FeiyuPreview live,int mode){
            if(mode==0&&submitted.Length>0)previous=submitted+" · "+outcome+(watching?"（已由新输入取代）":"");
            if(watching&&owner==live.Avatar&&live.Interaction.State==Core.PetState.Idle)owner.Motions.Stop();
            int ticket=++revision;owner=live.Avatar;interaction=live.Interaction.Generation;watching=true;
            var player=owner.Motions;var allowed=player.Capabilities();
            bool generate=player.Preferences.enabled && player.Preferences.director && !player.Preferences.items.Any(x=>!x.Value);
            try{
                startedAt=EditorApplication.timeSinceStartup;
                if(mode==0){
                    submitted=input.Trim();state=live.Interaction.State.ToString();action="";cache="";prompt="";decision="等待导演";outcome="请求中";Repaint();
                    await Task.Yield();
                    if(!Current(ticket,live))return;
                    if(!player.Preferences.director)throw new InvalidOperationException("请先启用导演许可");
                    catalog=string.Join("、",allowed.Select(x=>x.label+" ("+x.id+")"));
                    using(var req=Request(Connection(),"/playtest/direct")){
                        var payload=new JObject{["input"]=submitted,["state"]=state,["generate"]=generate,["available"]=new JArray(allowed.Select(x=>new JObject{["id"]=x.id,["label"]=x.label,["persistent"]=x.poseLoop!=null}))};
                        req.Content=new StringContent(payload.ToString(),Encoding.UTF8,"application/json");
                        using(var res=await http.SendAsync(req)){
                            string body=await res.Content.ReadAsStringAsync();if(!Current(ticket,live))return;
                            if(!res.IsSuccessStatusCode)throw new InvalidOperationException("导演："+res.StatusCode+" "+body);
                            var d=JObject.Parse(body);action=(string)d["action"];decision=action+" · "+(string)d["reason"];prompt=(string)d["prompt"];directorStatus="真实决策已返回";
                        }
                    }
                }
                if(action=="none"){outcome="保持当前状态";watching=false;return;}
                if(live.Interaction.State!=Core.PetState.Idle)throw new InvalidOperationException("当前空间状态禁止接管");
                if(action!="generate"){if(!allowed.Any(x=>x.id==action))throw new InvalidOperationException("动作许可已变化");bool ok=await player.Play(action);if(Current(ticket,live))outcome=ok?"库内播放："+action:"未播放："+player.Status;return;}
                if(!generate)throw new InvalidOperationException("当前许可禁止生成");
                string folder=cache;
                if(mode!=1){seed=Guid.NewGuid().GetHashCode()&int.MaxValue;folder=Path.Combine(Root,".cache/my-neuro-prototype","director-"+Guid.NewGuid().ToString("N"));outcome="等待生成";startedAt=EditorApplication.timeSinceStartup;Repaint();}
                else if(string.IsNullOrEmpty(folder))throw new InvalidOperationException("本次没有可重播的缓存");
                var package=mode==1?Task.FromResult(folder):T2mPrototypeMenu.Generate(Root,folder,prompt,seed);
                bool played=await player.TryGeneratedPrototype(package);
                if(!Current(ticket,live))return;
                if(played){cache=await package;outcome=(mode==1?"缓存重播":"生成播放")+" · "+Path.GetFileName(Path.GetDirectoryName(cache));generatorStatus="就绪";}
                else {outcome="已过期或失败："+player.PackageLastError;if(package.IsFaulted)generatorStatus="生成失败："+package.Exception.GetBaseException().Message;}
            }catch(Exception e){if(Current(ticket,live))outcome=e.Message;}
        }
    }
}
