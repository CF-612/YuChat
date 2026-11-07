using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Globalization;

namespace LingChatXR.Chat
{
    // 适配 LingChat GameDialog：底部通透渐变、70% 内容宽度、细线与无底色导航。
    public sealed class XrChatView : MonoBehaviour
    {
        private XrChatController controller;
        private UIDocument document;
        private PanelSettings panelSettings;
        // TextCore 按字体家族缓存绘制资料。视图销毁时释放字体会留下仍被引用的材质，
        // 因此字体由进程级缓存持有，切场景/退出 Play/重建视图均复用同一份。
        private static Font font;
        private static UnityEngine.TextCore.Text.FontAsset fontAsset;
        private const string SharedFontName="LingChatXR.SharedUiFont";
        private Texture2D fade;
        private RenderTexture testRender;
        private VisualElement historyPanel, content, dialogue;
        private Button expand,toyToggle,takeCoin,storeCoin;
        private VisualElement toyPanel;
        private Label toyState;
        private bool toyOpen;
        private VisualElement walkBar;private Label walkHint;private Button walkStop;
#if UNITY_EDITOR
        Button ardyButton;
#endif
        public bool DialogueCollapsed { get; private set; }
        public XrSettingsView Settings { get; private set; }
        private XrSizeEditView sizeEdit;
        public XrMotionWindow MotionWindow {get;private set;}
        public static bool SettingsAvailable => active?.Settings!=null;
        private Label status, placeholder, motion, emotion, waiting, hint;
        private VisualElement subtitle;
        private Image bubble;
        private ScrollView speech;
        private Texture2D[] thinkingFrames;
        private float[] frameEnds;
        private float bubbleStart=-100;
        private bool wasThinking;
        private string renderedText="";
        private int revision=-1;
        private readonly List<(Label label,float started)> rising=new List<(Label,float)>();
        private Button auto;
        private ScrollView history;
        private TextField input;
        private Button send, retry, stop, reconnect;
        private int renderedRows = -1,historyPageRevision;
        private ChatRow firstRenderedRow;
        private static XrChatView active;
        private void Awake()
        {
            active = this; controller = GetComponent<XrChatController>();
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1600, 900);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = .5f; panelSettings.sortingOrder = 100;
            panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("XrChatTheme");
            if (System.Environment.GetEnvironmentVariable("XR_CAPTURE_UI") == "1")
            { testRender = new RenderTexture(1600, 900, 24); testRender.Create(); panelSettings.targetTexture = testRender; panelSettings.clearColor=true;panelSettings.colorClearValue=Color.clear; }
            document = gameObject.AddComponent<UIDocument>(); document.panelSettings = panelSettings;
            EnsureFont();
            var root = document.rootVisualElement; root.style.flexGrow = 1; root.pickingMode = PickingMode.Ignore;
            root.style.unityFontDefinition=FontDefinition.FromSDFFont(fontAsset);root.style.color = Color.white;
            root.styleSheets.Add(Resources.Load<StyleSheet>("XrChat"));
            dialogue = Element(root, "dialogue");
            fade = new Texture2D(1, 128, TextureFormat.RGBA32, false); fade.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 128; y++)
            {
                float t = y / 127f;
                float alpha = t < .75f ? Mathf.Lerp(.66f, .48f, t / .75f) : Mathf.Lerp(.48f, 0, (t - .75f) / .25f);
                fade.SetPixel(0, y, new Color(0, 14f / 255, 39f / 255, alpha));
            }
            fade.Apply(); dialogue.style.backgroundImage = fade;
            content = Element(dialogue, "dialogue-content");
            var heading = Element(content, "heading");
            Text(heading, "小肥鱼", "character-name"); Text(heading, "陪你聊一会儿", "character-subtitle"); emotion=Text(heading,"","emotion");
            Element(heading, "spacer"); status = Text(heading, "", "status");
            stop = Nav(heading, "停止", "stop", controller.Stop);
            auto=Nav(heading,"AUTO","play",()=>{controller.AutoAdvance=!controller.AutoAdvance;controller.SavePreferences();Refresh();});
            retry = Nav(heading, "重试", "retry", controller.RetryVoice);
            Nav(heading, "历史", "history", () => { Toggle(historyPanel); Settings.Close(); });
            Nav(heading, "设置", "settings", () => { Settings.Open(); historyPanel.style.display = DisplayStyle.None; }).name="chat-settings";
            Nav(heading, "", "collapse", () => SetDialogueCollapsed(true)).tooltip="收起对话 · 空格";
            expand=Nav(root,"对话","expand",()=>SetDialogueCollapsed(false));expand.name="chat-expand";
            expand.AddToClassList("dialogue-expand");expand.style.display=DisplayStyle.None;expand.tooltip="展开对话 · 空格";
            var divider = Element(content, "divider"); Element(divider, "divider-light");
            speech = new ScrollView(); speech.AddToClassList("speech"); content.Add(speech);
            waiting=Text(speech,"","waiting");
            subtitle=Element(speech,"subtitle-flow");motion=Text(speech,"","motion");
            speech.RegisterCallback<ClickEvent>(_=>controller.ContinueDialogue());
            speech.focusable=true;
            speech.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Return){controller.ContinueDialogue();e.StopPropagation();}});
            bubble=new Image{scaleMode=ScaleMode.ScaleToFit};bubble.AddToClassList("thinking-bubble");root.Add(bubble);bubble.pickingMode=PickingMode.Ignore;
            thinkingFrames=Resources.LoadAll<Texture2D>("LocalLingChatEffects/Thinking");
            System.Array.Sort(thinkingFrames,(a,b)=>string.CompareOrdinal(a.name,b.name));
            if(thinkingFrames.Length>0)bubble.style.height=110f*thinkingFrames[0].height/thinkingFrames[0].width;
            var durations=Resources.Load<TextAsset>("LocalLingChatEffects/thinking-frames");
            if(durations!=null){var data=JsonUtility.FromJson<FrameDurations>(durations.text);frameEnds=new float[data.durations.Length];float sum=0;for(int i=0;i<frameEnds.Length;i++)frameEnds[i]=sum+=data.durations[i]/1000f;}
            var compose = Element(content, "compose"); var field = Element(compose, "field");
            input = new TextField { multiline = false, maxLength = 2000 }; input.AddToClassList("chat-input"); field.Add(input);
            placeholder = Text(field, "想对小肥鱼说些什么…", "placeholder"); placeholder.pickingMode = PickingMode.Ignore;
            input.RegisterValueChangedCallback(_ => placeholder.style.display = string.IsNullOrEmpty(input.value) ? DisplayStyle.Flex : DisplayStyle.None);
            input.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return && string.IsNullOrEmpty(Input.compositionString)) { Send(); e.StopPropagation(); } });
            hint=Text(compose, "Enter 发送", "input-hint");
            send = Nav(compose, "", "send", Send); send.AddToClassList("send"); send.tooltip = "发送消息";
            reconnect = Nav(content, "重新连接", "retry", controller.Connect); reconnect.AddToClassList("reconnect");
            historyPanel = Element(root, "history-panel"); historyPanel.AddToClassList("floating-panel");
            var hh = Element(historyPanel, "panel-heading"); Text(hh, "对话记录", "panel-title");
            Element(hh, "spacer"); Nav(hh, "", "close", () => historyPanel.style.display = DisplayStyle.None);
            Element(historyPanel, "divider"); history = new ScrollView(); history.AddToClassList("history-scroll"); historyPanel.Add(history);
            historyPanel.style.display = DisplayStyle.None;
            Settings=new XrSettingsView(root,controller);
            MotionWindow=new XrMotionWindow(root);
            Settings.OpenMotions=MotionWindow.Open;
            MotionWindow.VisibilityChanged=open=>Settings.Root.style.visibility=open?Visibility.Hidden:Visibility.Visible;
            sizeEdit=new XrSizeEditView(root);
            walkBar=Element(root,"walk-toolbar");walkHint=Text(walkBar,"点桌面空白处行走","walk-hint");
            walkStop=Nav(walkBar,"停下","stop",()=>{
                var scene=FindAnyObjectByType<FeiyuPreview>();
#if UNITY_EDITOR
                var demo=scene?.GetComponent<LingChatXR.Editor.ArdyPlaytest>();
                if(demo!=null && demo.Active){demo.End();return;}
#endif
                scene?.Locomotion?.Cancel();
            });walkBar.style.display=DisplayStyle.None;
#if UNITY_EDITOR
            ardyButton=new Button(()=>{
                var scene=FindAnyObjectByType<FeiyuPreview>();
                var demo=scene?.GetComponent<LingChatXR.Editor.ArdyPlaytest>();
                if(demo!=null && demo.Active)demo.ChangeTarget();
                else LingChatXR.Editor.ArdyPlaytest.StartOnTable(scene);
            }){text="测试 ARDY 行走",name="ardy-game-start"};
            ardyButton.AddToClassList("settings-primary");walkBar.Add(ardyButton);
#endif
            toyToggle=new Button(()=>toyOpen=!toyOpen){text="玩具箱",name="toy-box-toggle"};
            toyToggle.AddToClassList("settings-primary");walkBar.Add(toyToggle);
            toyPanel=Element(root,"toy-panel");
            Text(toyPanel,"Token 硬币","character-subtitle");
            var toyActions=Element(toyPanel,"toy-actions");
            takeCoin=new Button(()=>FindAnyObjectByType<FeiyuPreview>()?.CoinToy?.Take()){text="取出硬币",name="toy-box-take"};
            storeCoin=new Button(()=>FindAnyObjectByType<FeiyuPreview>()?.CoinToy?.Store()){text="收回",name="toy-box-store"};
            takeCoin.AddToClassList("settings-primary");toyActions.Add(takeCoin);toyActions.Add(storeCoin);
            Text(toyPanel,"桌面左键拖放滚币 · 按住 H 移动模拟手","walk-hint");
            toyState=Text(toyPanel,"","walk-hint");
            toyPanel.style.display=DisplayStyle.None;
            root.RegisterCallback<GeometryChangedEvent>(_ => {
                bool narrow = root.resolvedStyle.width < 1100;
                content.style.left = content.style.right = Length.Percent(narrow ? 6 : 15);
                heading.EnableInClassList("narrow", narrow);
            });
            controller.MessageAccepted += Accepted; controller.Changed += Refresh; Refresh();
            // 首次绘制前加入固定文案，避免动态字体图集扩充时保留旧文字几何。
            root.Query<TextElement>().ForEach(e=>fontAsset.TryAddCharacters(e.text));
            KeepFontResources();
        }
        static bool FontReady(UnityEngine.TextCore.Text.FontAsset value)=>value!=null&&value.material!=null&&value.sourceFontFile!=null&&value.atlasTextures!=null&&System.Array.TrueForAll(value.atlasTextures,t=>t!=null);
        static void KeepFontResources()
        {
            if(fontAsset==null)return;
            if(fontAsset.material!=null)fontAsset.material.hideFlags=HideFlags.HideAndDontSave;
            foreach(var texture in fontAsset.atlasTextures)if(texture!=null)texture.hideFlags=HideFlags.HideAndDontSave;
        }
        private static void EnsureFont()
        {
            if(FontReady(fontAsset))return;
            // 域重载只清空托管静态字段，不重复分配仍存活的 HideAndDontSave 字体。
            foreach(var cached in Resources.FindObjectsOfTypeAll<UnityEngine.TextCore.Text.FontAsset>())
                if(cached.name==SharedFontName&&cached.hideFlags==HideFlags.HideAndDontSave&&FontReady(cached)){fontAsset=cached;KeepFontResources();return;}
            font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Noto Sans CJK SC","Arial"},22);
            // 系统字体名称用于查找字体家族，不能改成缓存标识。
            font.hideFlags=HideFlags.HideAndDontSave;
            fontAsset=UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(font);
            fontAsset.name=SharedFontName;fontAsset.hideFlags=HideFlags.HideAndDontSave;
            fontAsset.isMultiAtlasTexturesEnabled=true;
            KeepFontResources();
        }
        private static VisualElement Element(VisualElement parent, string css) { var e = new VisualElement(); e.AddToClassList(css); parent.Add(e); return e; }
        private static Label Text(VisualElement parent, string text, string css) { var e = new Label(text){enableRichText=false}; e.AddToClassList(css); parent.Add(e); return e; }
        private Button Nav(VisualElement parent, string text, string icon, System.Action action)
        {
            var b = new Button(action); b.AddToClassList("nav");
            b.Add(new LineIcon(icon)); if (!string.IsNullOrEmpty(text)) Text(b, text, "nav-text"); parent.Add(b); return b;
        }
        private static void Toggle(VisualElement panel) => panel.style.display = panel.resolvedStyle.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
        public void SetDialogueCollapsed(bool collapsed)
        {
            DialogueCollapsed=collapsed;input.Blur();
            dialogue.style.display=collapsed?DisplayStyle.None:DisplayStyle.Flex;
            expand.style.display=collapsed?DisplayStyle.Flex:DisplayStyle.None;
            if(collapsed)historyPanel.style.display=DisplayStyle.None;
        }
        private void Send() { if(controller.Busy)controller.ContinueDialogue();else if (controller.CanSend && !string.IsNullOrWhiteSpace(input.value)) controller.Send(input.value); }
        private void Accepted(string text) { if (input.value == text) input.value = ""; }
        private void Refresh()
        {
            RenderSubtitle();motion.text=controller.MotionText; emotion.text=controller.Emotion;
            status.text = controller.Busy || !controller.Connected ? controller.Status : "";
            waiting.text=controller.Thinking?controller.Status:"";waiting.style.display=controller.Thinking?DisplayStyle.Flex:DisplayStyle.None;
            send.SetEnabled(controller.CanSend || (controller.Busy&&!controller.Thinking)); stop.style.display = controller.Busy ? DisplayStyle.Flex : DisplayStyle.None;
            input.isReadOnly=controller.Busy;placeholder.text=controller.Busy?"点击正文或按 Enter 继续":"想对小肥鱼说些什么…";
            hint.text=controller.IsTyping?"Enter 补全":controller.Busy?"Enter 继续":"Enter 发送";
            auto.EnableInClassList("auto-on",controller.AutoAdvance);
            if(controller.Thinking&&!wasThinking)bubbleStart=Time.unscaledTime;
            wasThinking=controller.Thinking;
            retry.style.display = controller.CanRetry ? DisplayStyle.Flex : DisplayStyle.None;
            Settings?.RefreshStatus(); reconnect.style.display = controller.Connected ? DisplayStyle.None : DisplayStyle.Flex;
            var first=controller.History.Count>0?controller.History[0]:null;
            if (renderedRows == controller.History.Count && firstRenderedRow==first) return;
            firstRenderedRow=first;
            bool older=controller.HistoryPageRevision!=historyPageRevision;
            historyPageRevision=controller.HistoryPageRevision;
            history.Clear(); renderedRows = controller.History.Count;
            if(controller.HasEarlierHistory)history.Add(new Button(controller.LoadEarlierHistory){text="查看更早的对话"});
            if (renderedRows == 0) Text(history, "从第一句问候开始。", "empty-history");
            foreach (var row in controller.History)
            {
                var item = Element(history, "history-row");
                Text(item, row.speaker == "user" ? "你" : "小肥鱼", row.speaker == "user" ? "user-name" : "assistant-name");
                Text(item, row.text, "history-body");
            }
            history.schedule.Execute(() => history.scrollOffset = new Vector2(0, older?0:float.MaxValue));
        }
        [System.Serializable] private class FrameDurations { public int[] durations; }
        private void RenderSubtitle()
        {
            string text=controller.Subtitle??"";
            if(revision!=controller.PresentationRevision || !text.StartsWith(renderedText,System.StringComparison.Ordinal))
            {subtitle.Clear();rising.Clear();renderedText="";revision=controller.PresentationRevision;}
            string added=text.Substring(renderedText.Length);var chars=StringInfo.GetTextElementEnumerator(added);
            bool animate=StringInfo.ParseCombiningCharacters(added).Length==1;
            while(chars.MoveNext())
            {
                var label=new Label(chars.GetTextElement()){enableRichText=false};label.AddToClassList("subtitle-char");subtitle.Add(label);
                if(animate){label.style.opacity=0;label.transform.position=new Vector3(0,8.4f,0);rising.Add((label,Time.unscaledTime));}
            }
            renderedText=text;
            if(added.Length>0)speech.schedule.Execute(()=>speech.scrollOffset=new Vector2(0,float.MaxValue));
        }
        private void Update()
        {
            sizeEdit?.Update();
            MotionWindow?.Tick();
            bool modal=Settings.IsOpen||MotionWindow.IsOpen||sizeEdit.Active;
            dialogue.style.display=DialogueCollapsed||modal?DisplayStyle.None:DisplayStyle.Flex;
            expand.style.display=DialogueCollapsed&&!modal?DisplayStyle.Flex:DisplayStyle.None;
            // 沿用 LingChat：空格收放，输入中文/空格时不触发；相机右键仍留给场景。
            if(Input.GetKeyDown(KeyCode.Space)&&!HasTextFocus&&!sizeEdit.Active)
                SetDialogueCollapsed(!DialogueCollapsed);
            for(int i=rising.Count-1;i>=0;i--){var item=rising[i];float t=Mathf.Clamp01((Time.unscaledTime-item.started)/.28f);float eased=1-Mathf.Pow(1-t,3);item.label.style.opacity=eased;item.label.transform.position=new Vector3(0,8.4f*(1-eased),0);if(t>=1)rising.RemoveAt(i);}
            float elapsed=Time.unscaledTime-bubbleStart;
            var p=FindAnyObjectByType<FeiyuPreview>();
            bool walkVisible=p!=null&&p.Ready&&p.Interaction.PlaneConfirmed&&!modal;
#if UNITY_EDITOR
            if(p!=null && !p.ExternalInput && p.Ready&&!modal)walkVisible=true;
#endif
            bool toysAvailable=walkVisible && p.CoinToy!=null && p.Interaction.PlaneConfirmed;
            toyToggle.style.display=toysAvailable?DisplayStyle.Flex:DisplayStyle.None;
            toyPanel.style.display=toysAvailable&&toyOpen?DisplayStyle.Flex:DisplayStyle.None;
            if(toysAvailable && toyOpen)
            {
                takeCoin.text=p.CoinToy.Deployed?"取回硬币":"取出硬币";
                storeCoin.SetEnabled(p.CoinToy.Deployed);
                toyState.text=p.CoinToy.Status??"";
            }
            walkBar.style.display=walkVisible?DisplayStyle.Flex:DisplayStyle.None;
            if(walkVisible){walkHint.text=p.Locomotion?.Status??"点桌面空白处行走";walkStop.style.display=p.Locomotion!=null&&p.Locomotion.Active?DisplayStyle.Flex:DisplayStyle.None;}
#if UNITY_EDITOR
            if(walkVisible)
            {
                ardyButton.style.display=p.ExternalInput?DisplayStyle.None:DisplayStyle.Flex;
                var demo=p.GetComponent<LingChatXR.Editor.ArdyPlaytest>();
                bool testing=demo!=null&&demo.Active;
                ardyButton.text=!testing?"测试 ARDY 行走":demo.Submitted==0?"准备生成中":demo.Target=="A 前方"?"改为侧方目标":"改为前方目标";
                ardyButton.SetEnabled(testing?demo.Submitted>0:(!p.Interaction.PlaneConfirmed || p.Interaction.State==Core.PetState.Idle));
                if(testing){walkHint.text=demo.WorkerPhase;walkStop.style.display=DisplayStyle.Flex;}
                else if(demo!=null)walkHint.text=demo.Status;
                else if(!p.Interaction.PlaneConfirmed)walkHint.text="点击右侧按钮自动放置并试玩";
            }
#endif
            bool visible=controller.Thinking&&elapsed<2&&p!=null&&p.Ready&&p.Interaction.PlaneConfirmed&&thinkingFrames.Length>0;
            bubble.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            if(!visible)return;
            int f=0;if(frameEnds!=null&&frameEnds.Length>0){float stamp=elapsed%frameEnds[frameEnds.Length-1];while(f<frameEnds.Length-1&&stamp>=frameEnds[f])f++;}
            bubble.image=thinkingFrames[Mathf.Min(f,thinkingFrames.Length-1)];
            Vector3 screen=p.ViewCamera.WorldToViewportPoint(p.Avatar.transform.position+Vector3.up*(p.Avatar.HeightMeters+.025f));
            if(screen.z<=0){bubble.style.display=DisplayStyle.None;return;}
            var point=new Vector2(screen.x*document.rootVisualElement.resolvedStyle.width,(1-screen.y)*document.rootVisualElement.resolvedStyle.height);
            bubble.style.left=Mathf.Clamp(point.x-150,12,Mathf.Max(12,document.rootVisualElement.resolvedStyle.width-122));
            bubble.style.top=Mathf.Clamp(point.y-bubble.resolvedStyle.height*.5f,12,Mathf.Max(12,document.rootVisualElement.resolvedStyle.height-bubble.resolvedStyle.height-12));
        }
        public static bool BlocksSceneInput(Vector3 mouse)
        {
            if (active == null || active.document.rootVisualElement.panel == null) return false;
            var panel = active.document.rootVisualElement.panel;
            Vector2 p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(mouse.x, Screen.height - mouse.y));
            if(active.MotionWindow.IsOpen)return active.MotionWindow.Root.worldBound.Contains(p);
            bool hit=(!active.DialogueCollapsed&&active.content.worldBound.Contains(p)) || VisibleHit(active.walkBar,p) || VisibleHit(active.toyPanel,p) || VisibleHit(active.expand,p) || VisibleHit(active.historyPanel, p) || active.Settings.IsOpen || active.sizeEdit.Active;
            if(!hit&&(Input.GetMouseButtonDown(0)||Input.GetMouseButtonDown(1)||Input.GetMouseButtonDown(2)))active.input.Blur();
            return hit;
        }
        public static bool HasTextFocus => active!=null && (active.Settings.IsOpen || active.MotionWindow?.IsOpen==true || (active.document.rootVisualElement.panel?.focusController.focusedElement is VisualElement e && (e==active.input||active.input.Contains(e))));
        private static bool VisibleHit(VisualElement e, Vector2 p) => e.resolvedStyle.display != DisplayStyle.None && e.worldBound.Contains(p);
        public void CaptureTestUi(string path)
        {
            if (testRender == null) return;
            var old = RenderTexture.active; RenderTexture.active = testRender;
            var pixels = new Texture2D(1600, 900, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply();
            System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG()); Destroy(pixels); RenderTexture.active = old;
        }
        private void OnDestroy()
        {
            KeepFontResources();
            if (controller != null) { controller.Changed -= Refresh; controller.MessageAccepted -= Accepted; }
            if (active == this) active = null;
            MotionWindow?.Dispose();
            // 先解除面板对字体/纹理的引用，再释放本视图拥有的运行时资源。
            if(document!=null){document.enabled=false;document.rootVisualElement?.Clear();document.panelSettings=null;Destroy(document);}
            if (testRender != null) { testRender.Release(); Destroy(testRender); }
            if (panelSettings != null) Destroy(panelSettings);if (fade != null) Destroy(fade);
        }
        private sealed class LineIcon : VisualElement
        {
            public LineIcon(string kind)
            {
                AddToClassList("line-icon"); pickingMode = PickingMode.Ignore;
                generateVisualContent += context => {
                    var p = context.painter2D; p.strokeColor = resolvedStyle.color; p.fillColor = resolvedStyle.color; p.lineWidth = 1.6f;
                    void Line(params Vector2[] pts) { p.BeginPath(); p.MoveTo(pts[0]); for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]); p.Stroke(); }
                    if (kind == "close") { Line(new Vector2(5,5),new Vector2(17,17)); Line(new Vector2(17,5),new Vector2(5,17)); }
                    else if (kind == "stop") Line(new Vector2(5,5),new Vector2(17,5),new Vector2(17,17),new Vector2(5,17),new Vector2(5,5));
                    else if(kind=="play") {p.BeginPath();p.MoveTo(new Vector2(7,4));p.LineTo(new Vector2(17,11));p.LineTo(new Vector2(7,18));p.ClosePath();p.Stroke();}
                    else if (kind == "send") { p.BeginPath();p.MoveTo(new Vector2(3,6));p.LineTo(new Vector2(19,6));p.LineTo(new Vector2(11,16));p.ClosePath();p.Fill(); }
                    else if (kind == "volume") { Line(new Vector2(3,8),new Vector2(7,8),new Vector2(12,4),new Vector2(12,18),new Vector2(7,14),new Vector2(3,14),new Vector2(3,8)); Line(new Vector2(16,6),new Vector2(19,9),new Vector2(19,13),new Vector2(16,16)); }
                    else if(kind=="collapse") Line(new Vector2(5,8),new Vector2(11,14),new Vector2(17,8));
                    else if(kind=="expand") Line(new Vector2(5,14),new Vector2(11,8),new Vector2(17,14));
                    else if(kind=="settings")
                    {
                        p.BeginPath();
                        for(int i=0;i<48;i++)
                        {
                            float a=(i*7.5f-90)*Mathf.Deg2Rad;
                            float radius=(i%6==1||i%6==2||i%6==3)?9:7;
                            var v=new Vector2(11+Mathf.Cos(a)*radius,11+Mathf.Sin(a)*radius);
                            if(i==0)p.MoveTo(v);else p.LineTo(v);
                        }
                        p.ClosePath();p.Stroke();p.BeginPath();p.Arc(new Vector2(11,11),3,0,360);p.Stroke();
                    }
                    else { p.BeginPath();p.Arc(new Vector2(11,11),8,30,320);p.Stroke(); if (kind == "history") Line(new Vector2(11,6),new Vector2(11,11),new Vector2(15,13));else Line(new Vector2(14,3),new Vector2(18,4),new Vector2(17,8)); }
                };
            }
        }
    }
}
