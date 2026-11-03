using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LingChatXR.Chat;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace LingChatXR.Editor
{
    public static class C0SettingsProbe
    {
        const string Pending="XR.C0";
        static IEnumerator work;static double deadline;static FeiyuPreview preview;static XrChatController chat;static XrChatView view;
        static readonly List<string> checks=new List<string>();
        static string Evidence=>Environment.GetEnvironmentVariable("XR_C0_EVIDENCE");
        static string Store=>Environment.GetEnvironmentVariable("XR_PREFERENCES_DIRECTORY");
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
        public static void Run()
        {
            Directory.CreateDirectory(Evidence);EditorSceneManager.OpenScene(PreviewProject.ScenePath);
            Unity.Collections.NativeLeakDetection.Mode=Unity.Collections.NativeLeakDetectionMode.EnabledWithStackTrace;
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            SessionState.SetBool(Pending,true);deadline=EditorApplication.timeSinceStartup+300;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;EditorApplication.EnterPlaymode();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Configure()
        {
            if(!SessionState.GetBool(Pending,false))return;
            var p=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();p.ManualInput=true;
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("C0 timeout");
                if(!EditorApplication.isPlaying)return;
                if(work==null)work=RunChecks();if(!work.MoveNext())Finish(0);
            }
            catch(Exception e){File.WriteAllText(Path.Combine(Evidence,"failure.txt"),e.ToString());Finish(1);}
        }
        static IEnumerator Frames(int count){for(int i=0;i<count;i++){int f=Time.frameCount;while(f==Time.frameCount)yield return null;}}
        static IEnumerator RunChecks()
        {
            while(preview==null||chat==null){preview=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();chat=UnityEngine.Object.FindAnyObjectByType<XrChatController>();yield return null;}
            while(!preview.Ready)yield return null;view=chat.GetComponent<XrChatView>();var ui=view.Settings;
            if(Environment.GetEnvironmentVariable("XR_C0_MODE")=="UiRecovery")
            {
                while(!chat.Connected)yield return null;
                Check(chat.Connected,"product-automatic-powershell-start-and-session");
                Check(preview.Place(new Vector3(0,.74f,0)),"place-before-settings");
                var avatar=preview.Avatar;var interaction=preview.Interaction;var root=avatar.transform.position;
                for(int i=0;i<12;i++)
                {
                    ui.Open(i%4);var frames=Frames(4);while(frames.MoveNext())yield return null;
                    ui.Save();while(ui.IsSaving)yield return null;
                    Check(!ui.IsOpen&&preview.Avatar==avatar&&preview.Interaction==interaction&&interaction.PlaneConfirmed&&avatar.transform.position==root,"unchanged-save-closes-no-reimport-"+i);
                    frames=Frames(4);while(frames.MoveNext())yield return null;
                }
                float changedVolume=chat.Voice.Volume<.5f?.61f:.43f;
                ui.Open(1);ui.Root.Q<Slider>("settings-voice").value=changedVolume*100;ui.Save();while(ui.IsSaving)yield return null;
                Check(!ui.IsOpen&&Mathf.Abs(chat.Voice.Volume-changedVolume)<.001f,"edited-save-applies-and-closes");
                ui.Open(0);ui.Root.Q<FloatField>("settings-height").value=99;ui.Save();while(ui.IsSaving)yield return null;
                Check(ui.IsOpen&&preview.Avatar==avatar,"invalid-save-keeps-draft-and-avatar");ui.Close();
                var input=view.GetComponent<UIDocument>().rootVisualElement.Q<TextField>(className:"chat-input");input.value="这段草稿保留，中文与空格都正常";
                for(int i=0;i<20;i++){view.SetDialogueCollapsed(i%2==0);var frames=Frames(3);while(frames.MoveNext())yield return null;}
                Check(input.value=="这段草稿保留，中文与空格都正常","collapse-retains-input-draft");
                view.SetDialogueCollapsed(false);var waitUi=Frames(10);while(waitUi.MoveNext())yield return null;CaptureCombined(Path.Combine(Evidence,"dialogue-expanded.png"));
                bool played=false;float amplitude=0;var samples=new float[512];chat.AutoAdvance=true;chat.TestVoice();
                while(chat.Busy)
                {
                    if(chat.Voice.IsPlaying){played=true;amplitude=Mathf.Max(amplitude,chat.Voice.SampleAmplitude(samples));view.SetDialogueCollapsed(true);}
                    yield return null;
                }
                Check(played&&amplitude>.0001f&&view.DialogueCollapsed,"real-local-voice-continues-when-collapsed");
                waitUi=Frames(10);while(waitUi.MoveNext())yield return null;CaptureCombined(Path.Combine(Evidence,"dialogue-collapsed.png"));
                view.SetDialogueCollapsed(false);
                // 覆盖销毁顺序，确保文档先释放对运行时字体的引用。
                for(int i=0;i<4;i++)
                {
                    UnityEngine.Object.Destroy(view);waitUi=Frames(6);while(waitUi.MoveNext())yield return null;
                    view=chat.gameObject.AddComponent<XrChatView>();waitUi=Frames(12);while(waitUi.MoveNext())yield return null;
                    view.Settings.Open(i);waitUi=Frames(4);while(waitUi.MoveNext())yield return null;view.Settings.Save();
                    Check(view.Settings!=null&&!view.Settings.IsOpen,"view-destroy-recreate-font-lifecycle-"+i);
                }
                int cachedFonts=0;foreach(var asset in Resources.FindObjectsOfTypeAll<UnityEngine.TextCore.Text.FontAsset>())if(asset.name=="LingChatXR.SharedUiFont")cachedFonts++;
                Check(cachedFonts==1,"one-shared-font-after-rebuilds");
                view.Settings.Open(2);bool originalGaze=preview.ReadPreferences().gazeEnabled;
                view.Settings.Root.Q<Toggle>("settings-gaze").value=!originalGaze;view.Settings.OpenMotions();
                while(view.MotionWindow.PreviewAvatar==null)yield return null;
                view.MotionWindow.Close();
                Check(view.Settings.IsOpen&&view.Settings.Root.Q<Toggle>("settings-gaze").value==!originalGaze,"motion-preview-close-preserves-settings-draft");
                Check(preview.ReadPreferences().gazeEnabled==originalGaze,"motion-preview-does-not-save-parent-draft");view.Settings.Close();
                yield break;
            }
            if(Environment.GetEnvironmentVariable("XR_C0_MODE")=="Voice")
            {
                while(!chat.Connected)yield return null;
                Check(preview.Place(new Vector3(0,.74f,0))&&preview.BeginSizeEdit(out _),"voice-size-edit-start");
                int sizeGeneration=preview.SizeEditGeneration;var sameAvatar=preview.Avatar;float initialSize=sameAvatar.HeightMeters;
                bool played=false,gated=false;float amplitude=0;
                void Voice(LingChatXR.Speech.VoiceKind kind,LingChatXR.Speech.VoiceOutcome outcome,long token)
                {if(kind==LingChatXR.Speech.VoiceKind.Dialogue&&outcome==LingChatXR.Speech.VoiceOutcome.Started){played=chat.Voice.IsPlaying;gated=chat.IsTyping&&!chat.Thinking&&chat.Subtitle.Length<chat.FullSubtitle.Length;}}
                chat.Voice.PlaybackEvent+=Voice;chat.AutoAdvance=true;chat.TestVoice();var samples=new float[512];
                while(chat.Busy){if(chat.Voice.IsPlaying){amplitude=Mathf.Max(amplitude,chat.Voice.SampleAmplitude(samples));preview.PreviewSize(sizeGeneration,initialSize+Mathf.Sin(Time.unscaledTime*2)*.015f);}yield return null;}
                chat.Voice.PlaybackEvent-=Voice;Check(played&&gated&&amplitude>.0001f,"new-host-real-local-audio-and-text-gate");
                Check(preview.Avatar==sameAvatar&&!sameAvatar.FreezePresentation,"resize-during-speech-keeps-avatar-and-presentation");preview.CancelSizeEdit();
                ui.Open(3);chat.ReloadHostSettings();while(chat.Status!="XR 服务配置已应用")yield return null;Check(true,"product-settings-hot-reload");
                var sampleFrames=Frames(12);while(sampleFrames.MoveNext())yield return null;view.CaptureTestUi(Path.Combine(Evidence,"settings-live-service.png"));
                yield break;
            }
            if(Environment.GetEnvironmentVariable("XR_C0_MODE")=="Resize")
            {
                Time.captureFramerate=60;
                foreach(string folder in new[]{"local-physics-reference","local-smallbluefish-alpha2"})
                {
                    var load=preview.SwitchCharacterAsync(folder);while(!load.IsCompleted)yield return null;Check(load.Result,"resize-load-"+folder);
                    var active=preview.Avatar;float before=active.HeightMeters;Vector3 initialScale=active.Instance.transform.localScale;
                    Check(preview.Place(new Vector3(0,.74f,0))&&preview.BeginSizeEdit(out _),"resize-start-"+folder);
                    int token=preview.SizeEditGeneration;var root=active.transform.position;
                    float minBlink=1,maxBlink=0;
                    for(int frame=0;frame<240;frame++)
                    {
                        float height=frame<120?Mathf.Lerp(before,.36f,frame/119f):Mathf.Lerp(.36f,.18f,(frame-120)/119f);
                        Check(preview.PreviewSize(token,height),"resize-frame-"+folder+"-"+frame);
                        int f=Time.frameCount;while(f==Time.frameCount)yield return null;
                        float blink=active.Instance.Runtime.Expression.GetWeight(UniVRM10.ExpressionKey.Blink);minBlink=Mathf.Min(minBlink,blink);maxBlink=Mathf.Max(maxBlink,blink);
                        if(frame%30==0){Alpha2Probe.Capture(preview.ViewCamera,Path.Combine(Evidence,folder+"-"+frame+".png"));view.CaptureTestUi(Path.Combine(Evidence,folder+"-ui-"+frame+".png"));CaptureCombined(Path.Combine(Evidence,folder+"-combined-"+frame+".png"));}
                    }
                    Check(preview.Avatar==active&&!active.FreezePresentation&&active.transform.position==root,"resize-preserves-instance-presentation-root-"+folder);
                    Check(maxBlink-minBlink>.1f,"resize-keeps-live-blinking-"+folder);
                    Check(Mathf.Abs(active.GrabProxy.height-.18f*.65f)<1e-5f&&Mathf.Abs(preview.Interaction.RadiusMeters-active.FootprintRadius)<1e-6f,"resize-updates-proxies-and-footprint-"+folder);
                    preview.CancelSizeEdit();Check(Mathf.Abs(active.HeightMeters-before)<1e-5f&&Vector3.Distance(active.Instance.transform.localScale,initialScale)<1e-5f,"resize-cancel-restores-"+folder);
                    Check(!preview.PreviewSize(token,.4f),"cancel-rejects-stale-resize-"+folder);
                    preview.BeginSizeEdit(out token);preview.PreviewSize(token,.31f);ui.Open();Check(!preview.SizeEditing&&Mathf.Abs(active.HeightMeters-before)<1e-5f,"open-settings-cancels-size-preview-"+folder);ui.Close();
                    Check(preview.BeginSizeEdit(out token)&&preview.PreviewSize(token,.28f)&&preview.CommitSizeEdit(token),"resize-commit-"+folder);
                    Check(Mathf.Abs(preview.ConfiguredHeight-.28f)<1e-5f&&preview.Interaction.PlaneConfirmed,"resize-commit-keeps-confirmed-space-"+folder);
                    // 复原测试自己的尺寸，避免影响其他恢复用例。
                    preview.BeginSizeEdit(out token);preview.PreviewSize(token,before);preview.CommitSizeEdit(token);
                }
                preview.BeginSizeEdit(out int stale);var next=preview.SwitchCharacterAsync("local-physics-reference");while(!next.IsCompleted)yield return null;
                Check(next.Result&&!preview.PreviewSize(stale,.4f)&&!preview.SizeEditing,"switch-invalidates-size-edit");
                preview.Place(new Vector3(0,.74f,0));preview.BeginSizeEdit(out int lost);float start=preview.Avatar.HeightMeters;preview.PreviewSize(lost,.32f);preview.SimulateTrackingLoss();
                Check(!preview.SizeEditing&&!preview.Interaction.PlaneConfirmed&&Mathf.Abs(preview.Avatar.HeightMeters-start)<1e-5f,"tracking-loss-cancels-size-preview");
                Time.captureFramerate=0;yield break;
            }
            if(Environment.GetEnvironmentVariable("XR_C0_MODE")=="Restore")
            {
                Check(Mathf.Abs(chat.Voice.Volume-.43f)<.001f&&chat.TextSpeed==32&&chat.AutoAdvance,"restart-dialogue-preferences");
                Check(preview.ActivePackageFolder=="local-physics-reference"&&Mathf.Abs(preview.ConfiguredHeight-.24f)<.001f,"restart-selected-model-independent-size");
                Check(!preview.Interaction.PlaneConfirmed,"restart-requires-placement");
                ui.Open();var frames=Frames(12);while(frames.MoveNext())yield return null;view.CaptureTestUi(Path.Combine(Evidence,"settings-restored.png"));yield break;
            }
            Check(preview.ActivePackageFolder=="local-smallbluefish-alpha2","original-scene-default-model");
            string prefs=Path.Combine(Store,"xr-dialogue-preferences.json");PreferenceFiles.Save(prefs,new JObject{{"futureField",new JObject{{"retain",true}}}});
            ui.Open(1);float old=chat.Voice.Volume;ui.Root.Q<Slider>("settings-voice").value=11;ui.Close();Check(Mathf.Approximately(chat.Voice.Volume,old),"cancel-has-no-runtime-effect");Check(PreferenceFiles.Read(prefs)["voice"]==null,"cancel-does-not-save");
            ui.Open(1);ui.Root.Q<Slider>("settings-voice").value=43;ui.Root.Q<SliderInt>("settings-speed").value=32;ui.Root.Q<Toggle>("settings-auto").value=true;ui.Save();while(ui.IsSaving)yield return null;
            Check(Mathf.Abs(chat.Voice.Volume-.43f)<.001f&&chat.TextSpeed==32&&chat.AutoAdvance,"dialogue-save-applies");Check((bool)PreferenceFiles.Read(prefs)["futureField"]["retain"],"unknown-field-preserved");
            ui.Open(1);var wait=Frames(12);while(wait.MoveNext())yield return null;view.CaptureTestUi(Path.Combine(Evidence,"settings-sound.png"));
            ui.Close();ui.Open(0);ui.Root.Q<FloatField>("settings-height").value=999;ui.Save();while(ui.IsSaving)yield return null;Check(Mathf.Abs(preview.ConfiguredHeight-.24f)<.001f,"invalid-size-rejected");
            ui.Root.Q<FloatField>("settings-height").value=30;ui.Root.Q<FloatField>("settings-width").value=100;ui.Save();while(ui.IsSaving)yield return null;
            Check(preview.Ready&&Mathf.Abs(preview.ConfiguredHeight-.3f)<.001f&&!preview.Interaction.PlaneConfirmed,"save-size-reloads-and-invalidates-space");
            string alpha=preview.Avatar.Package.model.sha256;PreferenceFiles.Save(Path.Combine(Store,"xr-"+alpha+".json"),new JObject{{"futureModelField","keep"}});
            ui.Close();ui.Open(2);ui.Root.Q<Toggle>("settings-touch").value=false;ui.Root.Q<Toggle>("settings-gaze").value=false;ui.Save();while(ui.IsSaving)yield return null;
            Check(!preview.ReadPreferences().touchEnabled&&!preview.ReadPreferences().gazeEnabled,"interaction-save-applies");Check((string)PreferenceFiles.Read(Path.Combine(Store,"xr-"+alpha+".json"))["futureModelField"]=="keep","model-unknown-field-preserved");
            ui.RestoreGroupDraft();ui.Close();Check(!preview.ReadPreferences().touchEnabled,"cancel-defaults-keeps-saved-preference");
            ui.Open(2);ui.RestoreGroupDraft();ui.Save();while(ui.IsSaving)yield return null;Check(preview.ReadPreferences().touchEnabled&&Mathf.Abs(preview.ConfiguredHeight-.3f)<.001f&&Mathf.Abs(chat.Voice.Volume-.43f)<.001f,"restore-defaults-affects-only-interaction-group");
            ui.Open(2);ui.Root.Q<Toggle>("settings-touch").value=false;ui.Root.Q<Toggle>("settings-gaze").value=false;ui.Save();while(ui.IsSaving)yield return null;
            Check(preview.Place(new Vector3(0,.74f,0)),"configured-placement");wait=Frames(3);while(wait.MoveNext())yield return null;
            var camera=preview.ViewCamera;var ray=new Ray(camera.transform.position,preview.Avatar.TouchProxy.bounds.center-camera.transform.position);preview.HandlePointer(ray,false,true,true,false,false,0);
            Check(preview.Interaction.State==LingChatXR.Core.PetState.Idle&&!preview.Avatar.GazeEnabled,"disabled-touch-and-gaze-observed");
            var switchTask=preview.SelectModelAsync("local-physics-reference");while(!switchTask.IsCompleted)yield return null;Check(switchTask.Result==null&&Mathf.Abs(preview.ConfiguredHeight-.24f)<.001f&&preview.ReadPreferences().touchEnabled,"model-switch-isolation");
            switchTask=preview.SwitchCharacterAsync("local-smallbluefish-alpha2").ContinueWith(t=>t.Result?null:"failed");while(!switchTask.IsCompleted)yield return null;Check(switchTask.Result==null&&Mathf.Abs(preview.ConfiguredHeight-.3f)<.001f&&!preview.ReadPreferences().touchEnabled,"model-specific-restoration");
            ui.Close();ui.Open(0);wait=Frames(12);while(wait.MoveNext())yield return null;view.CaptureTestUi(Path.Combine(Evidence,"settings-space.png"));
            // 保存失败不改运行值、不覆盖损坏文件。
            string good=File.ReadAllText(prefs);File.WriteAllText(prefs,"{broken");var draft=chat.ReadDialogue();draft.voice=.9f;Check(!chat.SaveDialogue(draft,out _)&&Mathf.Abs(chat.Voice.Volume-.43f)<.001f&&File.ReadAllText(prefs)=="{broken","corrupt-file-rejected-preserved");File.WriteAllText(prefs,good);
            var service=XrServiceSettings.Read();service["futureService"]="keep";PreferenceFiles.Write(XrServiceSettings.FilePath,service);service["llm.providers"][0]["model"]="fixture-updated";Check(XrServiceSettings.Save(service)==null,"independent-service-save");Check((string)XrServiceSettings.Read()["futureService"]=="keep","service-unknown-field-preserved");
            service["llm.providers"][0]["base_url"]="file:///invalid";Check(XrServiceSettings.Save(service)!=null,"invalid-provider-url-rejected");
            service["llm.providers"][0]["base_url"]="";service.Remove("llm.translate_provider_id");service["llm.providers"][0]["provider"]="OpenAI";
            Check(XrServiceSettings.Validate(service)==null,"legacy-translation-slot-fallback-and-provider-case");
            ui.Close();ui.Open(3);wait=Frames(12);while(wait.MoveNext())yield return null;view.CaptureTestUi(Path.Combine(Evidence,"settings-service.png"));
            switchTask=preview.SelectModelAsync("local-physics-reference");while(!switchTask.IsCompleted)yield return null;Check(switchTask.Result==null,"persist-model-selection-for-restart");
        }
        // 将同一状态的真实相机与透明 UI 输出叠合，便于审阅控件与角色的位置关系。
        // 原始 world/UI 两份图同时保留；不冒称来自设备或人工操作的屏幕截图。
        internal static void CaptureCombined(string path,XrChatView targetView=null,FeiyuPreview targetPreview=null)
        {
            var ui=(targetView??view).GetComponent<UIDocument>().panelSettings.targetTexture;
            var camera=(targetPreview??preview).ViewCamera;var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
            var background=new RenderTexture(ui.width,ui.height,24);background.Create();
            var image=new Texture2D(ui.width,ui.height,TextureFormat.RGBA32,false);
            var overlay=new Texture2D(ui.width,ui.height,TextureFormat.RGBA32,false);
            try
            {
                camera.targetTexture=background;camera.Render();RenderTexture.active=background;image.ReadPixels(new Rect(0,0,ui.width,ui.height),0,0);image.Apply();
                RenderTexture.active=ui;overlay.ReadPixels(new Rect(0,0,ui.width,ui.height),0,0);overlay.Apply();
                var scene=image.GetPixels();var panel=overlay.GetPixels();
                for(int i=0;i<scene.Length;i++){float remain=1-panel[i].a;scene[i]=new Color(panel[i].r+scene[i].r*remain,panel[i].g+scene[i].g*remain,panel[i].b+scene[i].b*remain,1);}
                image.SetPixels(scene);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(overlay);background.Release();UnityEngine.Object.Destroy(background);}
        }
        static void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){File.WriteAllText(Path.Combine(Evidence,"error.txt"),text+"\n"+trace);Finish(1);}}
        static void Finish(int code){SessionState.SetBool(Pending,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;File.WriteAllText(Path.Combine(Evidence,"settings-checks.json"),new JObject{{"checks",new JArray(checks)},{"technicalPassed",code==0},{"visualAccepted",false},{"questTested",false}}.ToString());Debug.Log(code==0?"XR_C0_SETTINGS_PASSED":"XR_C0_SETTINGS_FAILED");EditorApplication.Exit(code);}
    }
}
