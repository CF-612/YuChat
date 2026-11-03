using System;
using LingChatXR.Chat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Samples.SpatialKeyboard;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.UI.BodyUI;

namespace LingChatXR.XR
{
    // 复用 LingChat 的角色标题、深蓝透明底、细分隔线和轻量导航；状态仍由原控制器提供。
    public sealed class XrWorldPanel : MonoBehaviour
    {
        static readonly Color Ink=new Color(.88f,.94f,1),Muted=new Color(.57f,.69f,.79f),Accent=new Color(.43f,.71f,1);
        QuestEntry entry;XrChatController chat;Text status,subtitle,service,sizeHint,palmHint,spaceHint,spaceTitle;
        Font font,displayFont;Canvas canvas;GameObject menuHost,dialogPage,spacePage;
        Button retry,sizeButton,palmSize,palmPlace,palmWrite,dialogTab,spaceTab;bool? palmWasPlaced;float menuHiddenAt=-1;bool wasFailed;bool space;
        XRKeyboard chestKeyboard;string draftText="";TouchScreenKeyboard systemKeyboard;Canvas composer;InputField draft;Button composeSend;
        public bool ComposerVisible=>composer!=null&&composer.gameObject.activeInHierarchy;
        public InputField ComposerInput=>draft; public Button ComposerSend=>composeSend;Text composeStatus,composeReply;GameObject shortcuts;GameObject header;Text heightValue;Canvas scaleFrame;RectTransform scaleHandle;
        Button earlierHistory;int historyPageRevision;
        Canvas placementHud;Text placementInstruction;
        GameObject historyPage,pairPage;RectTransform historyContent;ScrollRect historyScroll;InputField messageInput,addressInput,codeInput;Button sendButton,pairButton,historyTab,pairTab;Text pairStatus;int page;string historySnapshot="";
        public InputField MessageInput=>messageInput;
        public Button SendButton=>sendButton;
        public XrPushToTalk VoiceInput{get;private set;}
        Button talkButton;Text talkLabel;
        Text movementSpeedLabel;string movementError;
        public Button MoveLightButton{get;private set;}
        public Button MoveJogButton{get;private set;}
        public Button MoveFastButton{get;private set;}
        public Canvas ScaleFrame=>scaleFrame;
        public Vector3 ScaleHandlePosition=>scaleHandle.position;
        public Button SaveSizeButton{get;private set;}
        public Button CancelSizeButton{get;private set;}
        public Canvas Canvas=>canvas;
        public Canvas PalmCanvas{get;private set;}
        public HandMenu PalmMenu{get;private set;}
        public bool IsExpanded=>canvas!=null&&canvas.gameObject.activeSelf;
        public Button ConfirmButton{get;private set;}
        public string DisplayStatus=>status!=null?status.text:"";
        public string DisplaySubtitle=>subtitle!=null?subtitle.text:"";
        public void Configure(QuestEntry value,XrChatController controller)
        {
            entry=value;chat=controller;font=Resources.Load<Font>("Fonts/NotoSansSC");
            displayFont=Resources.Load<Font>("Fonts/MaokenAssortedSans");
            if(font==null)throw new InvalidOperationException("缺少头显中文字体 NotoSansSC");
            var events=FindAnyObjectByType<EventSystem>();if(events==null)events=new GameObject("XR UI EventSystem").AddComponent<EventSystem>();
            foreach(var module in events.GetComponents<BaseInputModule>())if(!(module is XRUIInputModule))module.enabled=false;
            if(events.GetComponent<XRUIInputModule>()==null)events.gameObject.AddComponent<XRUIInputModule>();
            canvas=MakeCanvas("LingChat world dialogue",null,new Vector2(760,500),.001f);
            var rect=(RectTransform)canvas.transform;Backdrop(rect,new Color(.018f,.043f,.09f,.88f));
            header=Group(rect,"Navigation",0,0,760,150);var h=(RectTransform)header.transform;
            Label(h,"小肥鱼",28,20,170,42,29,Ink).fontStyle=FontStyle.Bold;
            Label(h,"LINGCHAT  /  MR",170,30,230,32,14,Accent);
            Button(h,"返回掌边",608,22,124,40,Collapse,false);Rule(h,28,78,704);
            dialogTab=Button(h,"对话",28,92,104,42,ShowDialogue,false);
            historyTab=Button(h,"历史",140,92,96,42,ShowHistory,false);
            spaceTab=Button(h,"放置与大小",244,92,160,42,ShowSpace,false);
            pairTab=Button(h,"连接 PC",412,92,120,42,ShowPairing,false);
            status=Label(h,"正在准备…",540,99,192,38,16,Muted);
            dialogPage=Group(rect,"Dialogue",0,150,760,340);var d=(RectTransform)dialogPage.transform;
            subtitle=Label(d,"请先选择现实中的平台，让小肥鱼站稳。",28,8,704,130,25,Ink);
            service=Label(d,"",28,148,704,36,17,Muted);Rule(d,28,190,704);
            messageInput=Input(d,"输入想说的话…",28,204,540,60);messageInput.characterLimit=2000;messageInput.readOnly=true;messageInput.onValueChanged.AddListener(value=>draftText=value);
            messageInput.gameObject.AddComponent<XrFieldClick>().Click=OpenComposer;
            sendButton=Button(d,"发送",584,204,148,60,SubmitMessage);
            VoiceInput=gameObject.AddComponent<XrPushToTalk>();VoiceInput.Configure(chat);VoiceInput.Recognized+=Recognized;
            Button(d,"停止",28,282,96,42,()=>{VoiceInput.Cancel();chat.Stop();},false);
            talkButton=Button(d,"按住说话",140,282,316,42,()=>{},false);talkLabel=talkButton.GetComponentInChildren<Text>();
            talkButton.gameObject.AddComponent<XrPushToTalkButton>().VoiceInput=VoiceInput;
            Button(d,"继续 / 补全文字",476,282,256,42,()=>chat.ContinueDialogue(),false);
            historyPage=Group(rect,"History",28,150,704,326);
            earlierHistory=Button((RectTransform)historyPage.transform,"查看更早的对话",0,0,230,36,chat.LoadEarlierHistory,false);
            var viewport=Group((RectTransform)historyPage.transform,"Viewport",0,40,704,286);viewport.AddComponent<RectMask2D>();var scrollHit=viewport.AddComponent<Image>();scrollHit.color=Color.clear;
            historyContent=(RectTransform)Group((RectTransform)viewport.transform,"Rows",0,0,676,0).transform;
            var layout=historyContent.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=18;layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandHeight=false;
            historyContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            historyScroll=historyPage.AddComponent<ScrollRect>();historyScroll.viewport=(RectTransform)viewport.transform;historyScroll.content=historyContent;historyScroll.horizontal=false;historyScroll.movementType=ScrollRect.MovementType.Clamped;
            pairPage=Group(rect,"PC pairing",0,150,760,340);var p=(RectTransform)pairPage.transform;
            Label(p,"连接运行小肥鱼服务的电脑",28,0,704,38,24,Ink);
            Label(p,"Quest 与电脑连接同一 Wi-Fi · 聊天和语音模型留在电脑",28,38,704,20,15,Muted);
            addressInput=Input(p,"PC 显示的地址，例如 http://192.168.1.2:17862",28,58,704,52);
            codeInput=Input(p,"8 位配对码",28,124,330,52);codeInput.contentType=InputField.ContentType.IntegerNumber;codeInput.characterLimit=8;
            pairButton=Button(p,"配对并连接",380,124,352,52,()=>chat.Pair(addressInput.text,codeInput.text));
            pairStatus=Label(p,"",28,192,704,68,18,Muted);
            Button(p,"重新连接",28,280,160,44,()=>chat.Connect(),false);Button(p,"清除本机配对",204,280,204,44,()=>chat.ClearPairing(),false);
            spacePage=Group(rect,"Space",0,150,760,340);var s=(RectTransform)spacePage.transform;
            spaceTitle=Label(s,"选择现实桌面",28,8,704,44,24,Ink);
            spaceHint=Label(s,"",28,48,704,50,18,Muted);
            Button(s,"选择平台",28,116,220,48,()=>entry.EnvironmentSurfaces.BeginSelection());
            ConfirmButton=Button(s,"手动标定",270,116,220,48,()=>entry.Placement.Begin());
            Button(s,"扫描房间",512,116,220,48,()=>entry.EnvironmentSurfaces.RequestScan());
            sizeButton=Button(s,"调整大小",28,184,220,48,()=>entry.SizeAdjustment.Begin());
            Button(s,"恢复手部追踪",270,184,220,48,()=>entry.RecoverHands());
            retry=Button(s,"重试加载角色",512,184,220,48,()=>{_ = entry.Preview.RetryCharacterLoadAsync();});
            Button(s,"选择行走目标",28,252,340,48,()=>entry.Bridge.BeginWalkSelection());
            Button(s,"停止行走",392,252,340,48,()=>{entry.Bridge.CancelWalkSelection();entry.Preview.Locomotion?.Cancel();},false);
            movementSpeedLabel=Label(s,"速度 1×",28,309,120,30,17,Muted);
            MoveLightButton=Button(s,"轻快 1×",152,306,180,32,()=>movementError=entry.Preview.SaveMovementSpeed(1),false);
            MoveJogButton=Button(s,"慢跑 2×",352,306,180,32,()=>movementError=entry.Preview.SaveMovementSpeed(2),false);
            MoveFastButton=Button(s,"快跑 3×",552,306,180,32,()=>movementError=entry.Preview.SaveMovementSpeed(3),false);
            BuildScaleFrame();
            canvas.gameObject.SetActive(false);
            // 三点标定时只留不接收射线的提示条，避免大菜单挡住桌面确认。
            placementHud=MakeCanvas("Placement instruction",null,new Vector2(720,92),.001f);placementHud.gameObject.SetActive(false);
            Backdrop((RectTransform)placementHud.transform,new Color(.018f,.043f,.09f,.8f));
            placementInstruction=Label((RectTransform)placementHud.transform,"",20,14,680,66,21,Ink);
            placementHud.GetComponent<TrackedDeviceGraphicRaycaster>().enabled=false;
            chat.Changed+=Refresh;chat.MessageAccepted+=Accepted;SetPage(false);Refresh();canvas.gameObject.SetActive(false);BuildComposer();
        }
        public void ConfigureHandMenu()
        {
            // 保留官方 HandMenu 的翻掌判定、跟随和显隐；只改输入绑定与视图。
            menuHost=new GameObject("Palm menu host");menuHost.SetActive(false);menuHost.transform.SetParent(entry.TrackingSpace,false);
            var prefab=Resources.Load<GameObject>("QuestHandMenu/HandMenuRig");if(prefab==null)throw new InvalidOperationException("缺少官方 XRI HandMenuRig");
            var rig=Instantiate(prefab,menuHost.transform,false);PalmMenu=rig.GetComponent<HandMenu>();
            entry.BindPalm(PalmMenu.leftPalmAnchor,true);entry.BindPalm(PalmMenu.rightPalmAnchor,false);
            if(PalmMenu.handMenuUIGameObject!=null)Destroy(PalmMenu.handMenuUIGameObject);
            PalmCanvas=MakeCanvas("Palm shortcuts",rig.transform,new Vector2(340,196),.00065f);
            shortcuts=Group((RectTransform)PalmCanvas.transform,"掌上快捷操作",0,0,340,196);var rect=(RectTransform)shortcuts.transform;Backdrop(rect,new Color(.018f,.043f,.09f,.96f));
            Glyph(rect,QuestPanelGlyph.Shape.Fish,15,13,35);
            var title=Label(rect,"小肥鱼",57,10,133,44,23,Ink);
            title.alignment=TextAnchor.MiddleLeft;
            palmHint=Label(rect,"等待选桌面",190,10,134,44,17,Accent);
            palmHint.alignment=TextAnchor.MiddleRight;if(displayFont!=null)palmHint.font=displayFont;
            Rule(rect,16,56,308);
            palmSize=Button(rect,"调大小",16,68,96,60,()=>entry.SizeAdjustment.Begin(),false);
            palmPlace=Button(rect,"选桌面",122,68,96,60,()=>entry.EnvironmentSurfaces.BeginSelection(),false);
            palmWrite=Button(rect,"写消息",228,68,96,60,OpenComposer,false);
            Button(rect,"对话记录",16,140,145,44,ShowHistory,false);Button(rect,"桌面工具",170,140,154,44,ShowSpace,false);
            canvas.transform.SetParent(PalmCanvas.transform,false);canvas.transform.localPosition=Vector3.zero;canvas.transform.localRotation=Quaternion.identity;canvas.transform.localScale=Vector3.one*.60f;
            placementHud.transform.SetParent(PalmCanvas.transform,false);placementHud.transform.localPosition=new Vector3(0,-134,0);placementHud.transform.localScale=Vector3.one*.56f;
            PalmMenu.handMenuUIGameObject=PalmCanvas.gameObject;PalmMenu.menuHandedness=HandMenu.MenuHandedness.Left;PalmMenu.interactionManager=entry.Interactions;
        }
        void SetPage(bool showSpace)
        {
            space=showSpace;page=showSpace?2:0;RefreshPages();
        }
        void RefreshPages()
        {
            header.SetActive(true);dialogPage.SetActive(page==0);spacePage.SetActive(page==2);historyPage.SetActive(page==1);pairPage.SetActive(page==3);
            Highlight(dialogTab,page==0);Highlight(historyTab,page==1);Highlight(spaceTab,page==2);Highlight(pairTab,page==3);
        }
        static void Highlight(Button tab,bool selected){tab.GetComponent<Image>().color=selected?new Color(.18f,.39f,.56f,.62f):Color.clear;}
        public void ShowDialogue(){SetPage(false);Expand();messageInput.SetTextWithoutNotify(draftText);}
        public void ShowSpace(){SetPage(true);Expand();}
        public void ShowHistory(){page=1;RefreshPages();RefreshHistory();Expand();}
        public void ShowPairing(){page=3;RefreshPages();Expand();}
        public void Expand(){if(canvas==null)return;RefreshPages();canvas.gameObject.SetActive(true);if(shortcuts!=null)shortcuts.SetActive(false);}
        public void Collapse(){if(entry.SizeAdjustment!=null&&entry.SizeAdjustment.Active){entry.SizeAdjustment.Cancel();return;}if(canvas!=null)canvas.gameObject.SetActive(false);if(shortcuts!=null)shortcuts.SetActive(true);}
        void Update()
        {
            if(chat==null)return;
            if(composer!=null&&composer.gameObject.activeSelf)
            {
                composeStatus.text=chat.Connected?chat.Status:"等待连接 PC · 草稿已保留";
                composeReply.text=chat.Connected?(string.IsNullOrWhiteSpace(chat.Subtitle)?"小肥鱼正在听。":chat.Subtitle):"连接后可以在这里阅读回复。";
                composeSend.interactable=chat.CanSend&&!string.IsNullOrWhiteSpace(draft.text);
                if(systemKeyboard!=null)
                {
                    if(systemKeyboard.status!=TouchScreenKeyboard.Status.Canceled&&draft.text!=systemKeyboard.text)
                        draft.text=systemKeyboard.text;
                    if(systemKeyboard.status!=TouchScreenKeyboard.Status.Visible){systemKeyboard=null;if(chestKeyboard!=null)chestKeyboard.Open(draftText);}
                }
            }
            bool failed=entry.Preview.LoadingPhase==FeiyuPreview.LoadPhase.Failed||entry.Preview.LoadingPhase==FeiyuPreview.LoadPhase.Cancelled;
            if(failed&&!wasFailed)ShowSpace();wasFailed=failed;
            if(menuHost!=null)
            {
                var head=entry.HeadCamera.transform;
                var chest=head.position-Vector3.up*.27f;
                var hand=entry.LeftHand.position-chest;
                var state=entry.Preview.Interaction?.State;
                bool interacting=state==LingChatXR.Core.PetState.Held||state==LingChatXR.Core.PetState.Touched||state==LingChatXR.Core.PetState.Descending;
                bool nearChest=Vector3.ProjectOnPlane(hand,Vector3.up).magnitude<.39f && Mathf.Abs(hand.y)<.25f;
                menuHost.SetActive(entry.InputAvailable&&entry.LeftBareHand.Tracked&&nearChest&&!interacting&&!(composer!=null&&composer.gameObject.activeSelf)&&!(entry.SizeAdjustment?.Active??false));
            }
            if(IsExpanded&&!canvas.gameObject.activeInHierarchy&&!(entry.SizeAdjustment?.Active??false))
            {
                if(menuHiddenAt<0)menuHiddenAt=Time.unscaledTime;
                if(Time.unscaledTime-menuHiddenAt>.45f)Collapse();
            }
            else menuHiddenAt=-1;
            retry.interactable=failed;sizeButton.interactable=entry.TableLocked;
            if(palmSize!=null)palmSize.interactable=entry.TableLocked;
            if(palmHint!=null&&palmWasPlaced!=entry.TableLocked)
            {
                palmWasPlaced=entry.TableLocked;palmHint.text=entry.TableLocked?"已放置":"等待选桌面";
                palmPlace.GetComponentInChildren<Text>().text=entry.TableLocked?"换桌面":"选桌面";
                Highlight(palmPlace,!entry.TableLocked);Highlight(palmWrite,entry.TableLocked);
            }
            RefreshPages();
            status.text=entry.TableLocked?"桌面已锁定":"等待放置";
            bool selecting=(entry.EnvironmentSurfaces!=null&&entry.EnvironmentSurfaces.Selecting)||(entry.Placement!=null&&entry.Placement.Step>0);
            bool walkingTarget=entry.Bridge.SelectingWalk;
            bool manual=entry.Placement!=null&&entry.Placement.Step>0&&!IsExpanded;bool scanning=entry.EnvironmentSurfaces!=null&&entry.EnvironmentSurfaces.Selecting;
            placementHud.gameObject.SetActive(manual||walkingTarget||scanning);if(placementHud.gameObject.activeSelf)placementInstruction.text=walkingTarget?entry.Bridge.WalkHint:scanning?entry.EnvironmentSurfaces.Hint:entry.Placement.Hint;
            spaceTitle.text=failed?"角色载入失败":entry.TableLocked?"桌面保持锁定":"选择现实桌面";
            spaceHint.text=!string.IsNullOrEmpty(movementError)?movementError:failed?entry.Preview.Status:selecting?(entry.EnvironmentSurfaces.Selecting?entry.EnvironmentSurfaces.Hint:entry.Placement.Hint):"食指指桌面，重复指向可催促加速；手掌挡停。下方速度即时保存，桌面保持锁定。";
            movementSpeedLabel.text="速度 "+entry.Preview.MovementSpeed.ToString("0.##")+"×";
            MoveLightButton.interactable=MoveJogButton.interactable=MoveFastButton.interactable=entry.Preview.Ready;
            subtitle.text=string.IsNullOrEmpty(chat.Subtitle)?(chat.Busy?"小肥鱼正在准备回复…":"想聊点什么？在下方输入。 "):chat.Subtitle;
            sizeHint.text=entry.SizeAdjustment?.Hint??"";service.text=chat.Status;
            sendButton.interactable=chat.CanSend&&!string.IsNullOrWhiteSpace(draftText);pairButton.interactable=!chat.Pairing;
            bool voicePage=canvas.gameObject.activeInHierarchy&&page==0&&!(entry.SizeAdjustment?.Active??false)&&entry.InputAvailable;
            if(!voicePage&&(VoiceInput.Recording||chat.Recognizing))VoiceInput.Cancel();
            talkButton.interactable=voicePage&&(VoiceInput.Recording||chat.CanRecord);messageInput.interactable=!VoiceInput.Recording&&!chat.Recognizing;
            talkLabel.text=VoiceInput.Recording?"正在听 · 松开识别":chat.Recognizing?"正在识别…":chat.CloudRecognition?"按住说话 · 联网识别":"按住说话 · 本地识别";
            if(page==0&&chat.Connected&&!chat.Busy)service.text=!string.IsNullOrEmpty(VoiceInput.Status)?VoiceInput.Status:chat.RecognitionStatus;
            pairStatus.text=chat.Status+(string.IsNullOrEmpty(chat.PairedEndpoint)?"\n配对码在 PC 开启配对时显示，5 分钟内有效。":"\n"+chat.PairedEndpoint);
            if(entry.SizeAdjustment!=null&&entry.SizeAdjustment.Active)UpdateScaleFrame();
        }

        void BuildScaleFrame()
        {
            scaleFrame=MakeCanvas("角色尺寸虚线框",null,new Vector2(420,500),.0005f);
            var rect=(RectTransform)scaleFrame.transform;
            var outline=scaleFrame.gameObject.AddComponent<QuestPanelGlyph>();
            outline.kind=QuestPanelGlyph.Shape.Frame;outline.color=new Color(.55f,.82f,1,.95f);outline.raycastTarget=false;
            scaleHandle=(RectTransform)Group(rect,"右手拉伸",0,0,64,64).transform;
            scaleHandle.anchorMin=scaleHandle.anchorMax=Vector2.one;scaleHandle.pivot=Vector2.one*.5f;scaleHandle.anchoredPosition=Vector2.zero;
            Backdrop(scaleHandle,new Color(.08f,.23f,.36f,.98f));scaleHandle.GetComponent<Image>().raycastTarget=true;
            Glyph(scaleHandle,QuestPanelGlyph.Shape.Resize,9,9,46);
            var bar=(RectTransform)Group(rect,"尺寸工具",0,0,336,60).transform;
            bar.anchorMin=bar.anchorMax=new Vector2(.5f,1);bar.pivot=Vector2.one*.5f;bar.anchoredPosition=new Vector2(0,86);
            Backdrop(bar,new Color(.045f,.085f,.13f,.95f));
            heightValue=Label(bar,"",10,7,116,46,21,Ink);
            CancelSizeButton=Button(bar,"取消",128,7,74,46,()=>entry.SizeAdjustment.Cancel(),false);
            SaveSizeButton=Button(bar,"确认",212,7,116,46,()=>entry.SizeAdjustment.Commit());
            sizeHint=Label(bar,"",0,-56,400,50,18,Ink);
            scaleFrame.gameObject.SetActive(false);
        }
        public void OpenScaleFrame()
        {
            canvas.gameObject.SetActive(false);shortcuts.SetActive(true);if(menuHost!=null)menuHost.SetActive(false);
            scaleFrame.gameObject.SetActive(true);UpdateScaleFrame();
        }
        public void HideScaleFrame(){if(scaleFrame!=null)scaleFrame.gameObject.SetActive(false);}
        public bool RightRayHitsScaleHandle()=>entry.RightRay.TryGetCurrentUIRaycastResult(out var hit)&&hit.gameObject!=null&&hit.gameObject.transform.IsChildOf(scaleHandle);
        void UpdateScaleFrame()
        {
            var avatar=entry.Preview.Avatar;if(avatar==null)return;
            float height=avatar.HeightMeters;var rect=(RectTransform)scaleFrame.transform;
            Vector3 forward=Vector3.ProjectOnPlane(avatar.transform.position-entry.HeadCamera.transform.position,Vector3.up);
            if(!entry.SizeAdjustment.Dragging&&forward.sqrMagnitude>.0001f)rect.rotation=Quaternion.LookRotation(forward,Vector3.up);
            float top=height+.008f;
            rect.position=avatar.transform.position+Vector3.up*(top*.5f);
            rect.sizeDelta=new Vector2(height*.88f+.012f,top)/.0005f;
            heightValue.text=(height*100).ToString("0.0")+" cm";
            sizeHint.text=entry.SizeAdjustment.Hint;
        }
        void Glyph(RectTransform parent,QuestPanelGlyph.Shape kind,float x,float y,float size)
        {
            var glyph=Group(parent,"图标",x,y,size,size).AddComponent<QuestPanelGlyph>();
            glyph.kind=kind;glyph.color=Accent;glyph.raycastTarget=false;
        }
        void BuildComposer()
        {
            composer=MakeCanvas("胸前输入",null,new Vector2(680,300),.00065f);
            var panel=(RectTransform)composer.transform;Backdrop(panel,new Color(.018f,.043f,.09f,.96f));
            Glyph(panel,QuestPanelGlyph.Shape.Fish,20,12,44);Label(panel,"小肥鱼",74,10,142,44,27,Ink).alignment=TextAnchor.MiddleLeft;
            composeStatus=Label(panel,"",224,10,430,44,17,Muted);composeStatus.alignment=TextAnchor.MiddleRight;
            Rule(panel,22,58,636);
            composeReply=Label(panel,"",22,67,632,60,21,Ink);
            draft=Input(panel,"轻点这里，输入想对小肥鱼说的话",22,139,636,64);draft.characterLimit=2000;
            draft.onValueChanged.AddListener(text=>draftText=text);
#if UNITY_ANDROID && !UNITY_EDITOR
            draft.gameObject.AddComponent<XrFieldClick>().Click=()=>{if(systemKeyboard==null&&chestKeyboard!=null)chestKeyboard.Open(draftText);};
#endif
            composeSend=Button(panel,"发送消息",472,225,186,52,SubmitMessage);
            Button(panel,"连接 PC",22,225,132,52,()=>{CloseComposer();ShowPairing();},false);
            Button(panel,"收起",166,225,96,52,CloseComposer,false);
            Button(panel,"系统输入法",274,225,178,52,OpenSystemKeyboard,false);
            var prefab=Resources.Load<GameObject>("QuestKeyboard/XRI Keyboard");
            if(prefab!=null)
            {
                var obj=Instantiate(prefab,panel,false);
                obj.transform.localPosition=new Vector3(0,-409,-45);
                obj.transform.localRotation=Quaternion.Euler(35,0,0)*Quaternion.Euler(0,180,0);
                obj.transform.localScale=Vector3.one*(.057f/.00065f);
                foreach(var keys in obj.GetComponentsInChildren<Canvas>(true))
                {
                    keys.worldCamera=entry.HeadCamera;
                    if(keys.GetComponent<TrackedDeviceGraphicRaycaster>()==null)keys.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                }
                foreach(var key in obj.GetComponentsInChildren<XRKeyboardKey>(true))
                {
                    if(key.targetGraphic==null)continue;
                    bool enter=key.keyCode==KeyCode.Return||key.keyCode==KeyCode.KeypadEnter;
                    key.targetGraphic.color=enter?new Color(.18f,.48f,.69f):new Color(.14f,.23f,.32f);
                    var colors=key.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.73f,.91f,1);
                    colors.pressedColor=new Color(.52f,.79f,1);key.colors=colors;
                }
                chestKeyboard=obj.GetComponentInChildren<XRKeyboard>(true);
                chestKeyboard.closeOnSubmit=false;chestKeyboard.submitOnEnter=true;
                chestKeyboard.onTextUpdated.AddListener(evt=>draft.text=evt.keyboardText);
                chestKeyboard.onTextSubmitted.AddListener(evt=>{draft.text=evt.keyboardText;SubmitMessage();});
            }
            composer.gameObject.SetActive(false);
        }
        void OpenComposer()
        {
            entry.Bridge.CancelInputs();Collapse();menuHost.SetActive(false);var head=entry.HeadCamera.transform;
            var forward=Vector3.ProjectOnPlane(head.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
            composer.transform.SetPositionAndRotation(head.position+forward*.46f-Vector3.up*.19f,Quaternion.LookRotation(forward,Vector3.up));
            composer.gameObject.SetActive(true);draft.SetTextWithoutNotify(draftText);
            if(chestKeyboard!=null)chestKeyboard.Open(draftText);else OpenSystemKeyboard();
        }
        void OpenSystemKeyboard()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(chestKeyboard!=null)chestKeyboard.Close(false);
            draft.readOnly=true;
            systemKeyboard=TouchScreenKeyboard.Open(draftText,TouchScreenKeyboardType.Default,true,false,false,false,"输入想说的话",2000);
#else
            EventSystem.current.SetSelectedGameObject(draft.gameObject);draft.ActivateInputField();
#endif
        }
        void CloseComposer(){if(chestKeyboard!=null)chestKeyboard.Close(false);if(systemKeyboard!=null){systemKeyboard.active=false;systemKeyboard=null;}draft.DeactivateInputField();composer.gameObject.SetActive(false);}
        void Refresh(){if(service!=null)service.text=chat.Status;if(historyPage!=null&&page==1)RefreshHistory();}
        void SubmitMessage(){if(chat.CanSend&&!string.IsNullOrWhiteSpace(draftText))chat.Send(draftText);}
        void Recognized(string text)
        {
            // 输入框为单行；换行会被 uGUI 丢掉，明确用空格隔开连续录入。
            string next=string.IsNullOrWhiteSpace(messageInput.text)?text:messageInput.text+" "+text;
            if(next.Length<=messageInput.characterLimit)messageInput.text=next;
            else VoiceInput.SetStatus("输入框内容已满，原草稿已保留；请先发送或精简后重试");
        }
        void Accepted(string value){if(draftText==value){draftText="";if(messageInput.gameObject.activeInHierarchy)messageInput.SetTextWithoutNotify("");if(draft!=null&&draft.gameObject.activeInHierarchy)draft.SetTextWithoutNotify("");if(systemKeyboard!=null)systemKeyboard.text="";if(chestKeyboard!=null&&chestKeyboard.isOpen)chestKeyboard.Open("");}RefreshHistory();}
        void RefreshHistory()
        {
            earlierHistory.gameObject.SetActive(chat.HasEarlierHistory);
            string snapshot=string.Join("\n",System.Linq.Enumerable.Select(chat.History,row=>row.speaker+":"+row.text));if(snapshot==historySnapshot&&historyContent.childCount>0)return;historySnapshot=snapshot;
            foreach(Transform child in historyContent){child.gameObject.SetActive(false);Destroy(child.gameObject);}
            if(chat.History.Count==0)AddHistoryRow("还没有对话记录。",Muted);
            foreach(var row in chat.History)AddHistoryRow((row.speaker=="user"?"你":"小肥鱼")+"\n"+row.text,row.speaker=="user"?Accent:Ink);
            Canvas.ForceUpdateCanvases();historyScroll.verticalNormalizedPosition=historyPageRevision!=chat.HistoryPageRevision?1:0;
            historyPageRevision=chat.HistoryPageRevision;
        }
        void AddHistoryRow(string value,Color color){var label=Label(historyContent,value,0,0,676,60,21,color);label.verticalOverflow=VerticalWrapMode.Overflow;var layout=label.gameObject.AddComponent<LayoutElement>();layout.preferredHeight=Mathf.Max(50,label.preferredHeight+12);}
        InputField Input(RectTransform parent,string hint,float x,float y,float w,float h)
        {
            var obj=Group(parent,hint,x,y,w,h);Backdrop((RectTransform)obj.transform,new Color(.18f,.30f,.42f,.3f));var bg=obj.GetComponent<Image>();bg.raycastTarget=true;
            var value=Label((RectTransform)obj.transform,"",16,8,w-32,h-12,21,Ink);var placeholder=Label((RectTransform)obj.transform,hint,16,8,w-32,h-12,19,Muted);
            var input=obj.AddComponent<InputField>();input.textComponent=value;input.placeholder=placeholder;input.targetGraphic=bg;return input;
        }
        Canvas MakeCanvas(string name,Transform parent,Vector2 size,float scale)
        {
            var root=new GameObject(name,typeof(RectTransform));root.transform.SetParent(parent,false);
            var result=root.AddComponent<Canvas>();result.renderMode=RenderMode.WorldSpace;result.worldCamera=entry.HeadCamera;
            root.AddComponent<TrackedDeviceGraphicRaycaster>();root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit=10;
            var rect=(RectTransform)root.transform;rect.sizeDelta=size;rect.localScale=Vector3.one*scale;return result;
        }
        GameObject Group(RectTransform parent,string name,float x,float y,float w,float h){var obj=new GameObject(name,typeof(RectTransform));Place((RectTransform)obj.transform,parent,x,y,w,h);return obj;}
        void Backdrop(RectTransform rect,Color color)
        {
            bool panel=color.a>=.8f;
            if(panel)color.a=1f;
            var image=rect.gameObject.AddComponent<Image>();image.sprite=entry.PanelSprite;image.type=Image.Type.Sliced;image.color=color;image.raycastTarget=false;rect.gameObject.AddComponent<QuestPanelGradient>();
            if(panel)
            {
                var border=(RectTransform)new GameObject("浅蓝边线",typeof(RectTransform)).transform;
                border.SetParent(rect,false);border.anchorMin=Vector2.zero;border.anchorMax=Vector2.one;
                border.offsetMin=border.offsetMax=Vector2.zero;
                var graphic=border.gameObject.AddComponent<QuestPanelGlyph>();graphic.kind=QuestPanelGlyph.Shape.Border;graphic.raycastTarget=false;
            }
        }
        void Rule(RectTransform parent,float x,float y,float width){var obj=Group(parent,"Divider",x,y,width,1);var line=obj.AddComponent<Image>();line.color=new Color(.43f,.71f,1,.25f);line.raycastTarget=false;}
        static void Place(RectTransform rect,RectTransform parent,float x,float y,float w,float h){rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);}
        Text Label(RectTransform parent,string content,float x,float y,float w,float h,int fontSize,Color color)
        {
            var obj=Group(parent,"Text",x,y,w,h);var text=obj.AddComponent<Text>();text.font=fontSize>=23&&displayFont!=null?displayFont:font;text.fontSize=fontSize;text.color=color;text.text=content;text.supportRichText=false;text.raycastTarget=false;text.verticalOverflow=VerticalWrapMode.Truncate;return text;
        }
        Button Button(RectTransform parent,string title,float x,float y,float w,float h,Action click,bool filled=true)
        {
            var obj=Group(parent,title,x,y,w,h);var image=obj.AddComponent<Image>();image.sprite=entry.PanelSprite;image.type=Image.Type.Sliced;image.color=filled?new Color(.18f,.39f,.56f,.75f):new Color(.18f,.32f,.46f,.16f);
            var button=obj.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>click());
            var label=Label((RectTransform)obj.transform,title,0,0,w,h,21,Ink);label.alignment=TextAnchor.MiddleCenter;if(displayFont!=null)label.font=displayFont;
            QuestPanelGlyph.Shape? icon=title=="确认"||title=="发送消息"?QuestPanelGlyph.Shape.Check:title=="写消息"?QuestPanelGlyph.Shape.Chat:title=="选桌面"?QuestPanelGlyph.Shape.Table:title=="调大小"?QuestPanelGlyph.Shape.Resize:null;
            if(icon.HasValue)
            {
                float size=Mathf.Min(25,h*.45f);Glyph((RectTransform)obj.transform,icon.Value,7,(h-size)/2,size);
                label.rectTransform.anchoredPosition+=Vector2.right*(size*.46f);if(w<=100)label.fontSize=17;
            }
            if(filled)obj.AddComponent<QuestPanelGradient>();
            return button;
        }
        void OnDestroy(){if(VoiceInput!=null)VoiceInput.Recognized-=Recognized;if(chat!=null){chat.Changed-=Refresh;chat.MessageAccepted-=Accepted;}if(canvas!=null)Destroy(canvas.gameObject);if(menuHost!=null)Destroy(menuHost);if(scaleFrame!=null)Destroy(scaleFrame.gameObject);if(composer!=null)Destroy(composer.gameObject);if(placementHud!=null)Destroy(placementHud.gameObject);}
    }
}
