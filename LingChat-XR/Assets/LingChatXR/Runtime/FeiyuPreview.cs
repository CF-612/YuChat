using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LingChatXR.Core;
using UnityEngine;

namespace LingChatXR
{
    [DefaultExecutionOrder(-50)]
    public sealed class FeiyuPreview : MonoBehaviour
    {
        public enum LoadPhase { None, Queued, ReadingPackage, Importing, Ready, Cancelled, Failed }
        [SerializeField] private Camera previewCamera;
        public const string ActiveCharacterFolder = "local-smallbluefish-alpha2";
        [SerializeField] private string characterFolder = ActiveCharacterFolder;
        [SerializeField] private bool useAuthorPhysics;
        private const float TableY = 0.74f;
        private const string MouseOwner = "editor-mouse";
        private readonly Rect panel = new Rect(18, 62, 325, 570);
        private readonly Rect toolsButton = new Rect(18, 18, 148, 34);
        private bool showTools;
        private CancellationTokenSource lifetime;
        private CancellationTokenSource loadCancellation;
        private readonly SemaphoreSlim loadGate = new SemaphoreSlim(1, 1);
        private int loadRevision;
        private bool welcomeConsumed;
        private ContactShadow contactShadow;
        private string preferenceDirectory;
        private bool explicitCharacter;
        public bool TouchEnabled=>preferences!=null&&preferences.touchEnabled;
        public bool SizeEditing { get; private set; }
        public int SizeEditGeneration { get; private set; }
        private float sizeBeforeEdit;
        private string[] packageFolders=Array.Empty<string>(),packageLabels=Array.Empty<string>();
        private int selectedPackage;
        public LoadPhase LoadingPhase { get; private set; }
        public bool Loading => LoadingPhase==LoadPhase.Queued || LoadingPhase==LoadPhase.ReadingPackage || LoadingPhase==LoadPhase.Importing;
        public string ActivePackageFolder => Ready ? characterFolder : null;
        public float ConfiguredHeight => preferences.heightMeters;
        public string[] PackageFolders => (string[])packageFolders.Clone();
        public string[] PackageLabels => (string[])packageLabels.Clone();
        public XrPreferences ReadPreferences()=>Newtonsoft.Json.Linq.JObject.FromObject(preferences).ToObject<XrPreferences>();
        private FeiyuAvatar avatar;
        private GameObject avatarRoot;
        private LineRenderer boundary;
        private LineRenderer dropMarker;
        TableWaterRings dropRipple;
        private bool dropAvailable;
        private Vector3 initialCameraPosition;
        private Quaternion initialCameraRotation;
        private readonly Vector3 viewTarget = new Vector3(-.08f, .84f, 0);
        private Vector3 previousMousePosition;
        private bool orbiting;
        public bool InspectingFromDesktop { get; private set; }
        private XrPreferences preferences = new XrPreferences();
        private string message = "正在载入小肥鱼…";
        private int grabGeneration;
        private Vector3 grabOffset;
        private float lift;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private Font uiFont;
        private Speech.XrSpeechController speech;
        public PetInteraction Interaction { get; private set; }
        public bool Ready => Interaction != null;
        public FeiyuAvatar Avatar => avatar;
        public string Status => Locomotion!=null&&Locomotion.Active?Locomotion.Status:message;
        public bool ManualInput { get; set; }
        public bool ExternalInput { get; set; }
        public Camera ViewCamera => previewCamera;
        public bool DropPreviewValid { get; private set; }
        public PetLocomotion Locomotion {get;private set;}
        public Quaternion SpatialRotation=>avatarRoot!=null?avatarRoot.transform.rotation:Quaternion.identity;
        public float MovementSpeed=>preferences.movementSpeed;
        public string SaveMovementSpeed(float speed)
        {
            if(!Ready||!IsRange(speed,.75f,3f))return "移动速度应在 0.75–3 倍之间";
            try{PreferenceFiles.Save(PreferencePath(),new Newtonsoft.Json.Linq.JObject{{"version",2},{"modelSha256",preferences.modelSha256},{"movementSpeed",speed}});}
            catch(Exception e)when(PreferenceFiles.IsStorageError(e)){return "速度保存失败，原设置已保留";}
            preferences.movementSpeed=speed;return null;
        }
        public bool TryAutonomousPose(Vector3 position,Quaternion rotation,int generation)
        {
            if(!Ready||SizeEditing||!float.IsFinite(rotation.x)||!float.IsFinite(rotation.y)||!float.IsFinite(rotation.z)||!float.IsFinite(rotation.w)||!Interaction.TryMoveAutonomous(ToCore(position),generation))return false;
            avatarRoot.transform.rotation=rotation;ApplyState();return true;
        }
        public bool TryHeldYaw(string owner,int generation,float yaw)
        {
            if(!Ready||SizeEditing||!float.IsFinite(yaw)||Interaction.State!=PetState.Held||Interaction.Holder!=owner||Interaction.Generation!=generation)return false;
            avatarRoot.transform.rotation=Quaternion.Euler(0,yaw,0);Locomotion?.PreserveManualFacing();return true;
        }

        public void SetCamera(Camera value) => previewCamera = value;
        public void ConfigureCharacter(string folder, bool descent)
        {
            if (Ready || lifetime != null) throw new InvalidOperationException("角色必须在载入前选择。");
            ValidateFolder(folder);
            characterFolder = folder;
            explicitCharacter=true;
        }
        internal static void ValidateFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || folder.Contains("/") || folder.Contains("\\") || folder.Contains(":") || folder=="." || folder=="..")
                throw new ArgumentException("角色目录必须是 Characters 下的单个目录名。", nameof(folder));
        }
        public void ConfigurePreferenceStorage(string directory)
        {
            if(lifetime!=null)throw new InvalidOperationException("存储目录必须在启动前选择。");
            preferenceDirectory=Path.GetFullPath(directory);
        }
        public void ConfigureAuthorPhysics(bool enabled)
        {
            if (Ready || lifetime != null) throw new InvalidOperationException("物理模式必须在载入前选择。");
            useAuthorPhysics = enabled;
        }
        public void RefreshSpatialState() { if (Ready) ApplyState(); }

        private async void Start()
        {
            Locomotion=gameObject.AddComponent<PetLocomotion>();Locomotion.Configure(this);
            lifetime = lifetime ?? new CancellationTokenSource();
            speech = FindAnyObjectByType<Chat.XrChatController>()?.Voice;
            if (speech == null) speech = GetComponent<Speech.XrSpeechController>();
            if (speech == null) speech = gameObject.AddComponent<Speech.XrSpeechController>();
            if (previewCamera == null) { message="相机未配置。";LoadingPhase=LoadPhase.Failed;return; }
            gameObject.AddComponent<PetLoadingIndicator>().Configure(this,previewCamera);
            initialCameraPosition = previewCamera.transform.position;
            initialCameraRotation = previewCamera.transform.rotation;
            LoadingPhase=LoadPhase.ReadingPackage;message="读取角色目录…";
            try{await RefreshPackages();}
            catch(OperationCanceledException){return;}
            catch(Exception error){message="角色索引读取失败："+error.Message;LoadingPhase=LoadPhase.Failed;Debug.LogWarning("XR_CHARACTER_INDEX_FAILED "+error.Message);return;}
            if(!explicitCharacter)
            {
                try{string saved=(string)PreferenceFiles.Read(ApplicationPreferencesPath)["modelFolder"];if(Array.IndexOf(packageFolders,saved)>=0)characterFolder=saved;}
                catch(Exception e)when(PreferenceFiles.IsStorageError(e)){Debug.LogWarning("XR_APP_PREFERENCES_UNREADABLE: 原文件保留，使用默认模型。");}
            }
            await SwitchCharacterAsync(characterFolder);
        }

        public async Task<bool> SwitchCharacterAsync(string selectedFolder, CancellationToken cancellationToken=default)
        {
            ValidateFolder(selectedFolder);
            if(this==null||!isActiveAndEnabled)return false;
            lifetime = lifetime ?? new CancellationTokenSource();
            int revision=++loadRevision;
            loadCancellation?.Cancel();
            var request=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,cancellationToken);
            loadCancellation=request;
            var token=request.Token;
            LoadingPhase=LoadPhase.Queued;message="正在切换模型…";
            ClearCharacter();
            var chat=FindAnyObjectByType<Chat.XrChatController>();
            // 未连接时保留具体故障提示，不能让模型切换把它盖成“已停止”。
            if(chat!=null&&(chat.Connected||chat.Busy))chat.Stop();else speech?.Stop();
            GameObject pendingRoot=null;bool entered=false;
            try
            {
                await loadGate.WaitAsync(token);entered=true;
                token.ThrowIfCancellationRequested();
                LoadingPhase=LoadPhase.ReadingPackage;
                var folder = Application.streamingAssetsPath + "/Characters/" + selectedFolder;
                var manifestBytes = await PackageFiles.ReadAsync(folder, "manifest.json", token);
                var package = JsonUtility.FromJson<CharacterPackage>(System.Text.Encoding.UTF8.GetString(manifestBytes));
                if (package?.model?.sha256 == null || package.model.sha256.Length != 64 ||
                    Array.Exists(package.model.sha256.ToCharArray(), value => !Uri.IsHexDigit(value)))
                    throw new InvalidDataException("Missing model fingerprint.");
                // 每次从默认值或本资产自己的保存记录开始，不带入上一角色的校准。
                var nextPreferences=new XrPreferences {modelSha256=package.model.sha256};
                string saved = PreferencePath(package.model.sha256);
                if (File.Exists(saved))
                {
                    var candidate = PreferenceFiles.Load<XrPreferences>(saved);
                    if (candidate != null && (candidate.version == 1 || candidate.version == 2) && candidate.modelSha256 == package.model.sha256 &&
                        IsRange(candidate.heightMeters, 0.12f, 0.45f) && IsRange(candidate.halfWidth, 0.2f, 0.6f) && IsRange(candidate.halfDepth, 0.2f, 0.4f))
                    {
                        // Upgrade only the previous preview defaults; retain custom calibration and height.
                        if (candidate.version == 1 && Mathf.Approximately(candidate.halfWidth, .3f) && Mathf.Approximately(candidate.halfDepth, .22f))
                        { candidate.halfWidth = .6f; candidate.halfDepth = .4f; }
                        candidate.version = 2;
                        if(!IsRange(candidate.movementSpeed,.75f,3f))candidate.movementSpeed=1;
                        nextPreferences = candidate;
                    }
                }
                LoadingPhase=LoadPhase.Importing;
                pendingRoot = new GameObject("Pet spatial root");pendingRoot.SetActive(false);
                var nextAvatar = pendingRoot.AddComponent<FeiyuAvatar>();
                await nextAvatar.LoadAsync(folder, nextPreferences.heightMeters, token, useAuthorPhysics,
                    progress=>{if(this!=null&&revision==loadRevision){message=progress;Debug.Log("XR_CHARACTER_STAGE "+progress);}});
                token.ThrowIfCancellationRequested();
                if(this==null||revision!=loadRevision||!isActiveAndEnabled)throw new OperationCanceledException();
                if(!ExternalInput)AnimePresentation.ConfigureStage(previewCamera);
                Debug.Log("XR_LILTOON_MATERIALS " + AnimePresentation.Apply(nextAvatar));
                // 所有异步操作成功后一次发布；旧请求不能回写新状态。
                avatarRoot=pendingRoot;pendingRoot=null;avatar=nextAvatar;preferences=nextPreferences;characterFolder=selectedFolder;
                Interaction = new PetInteraction(avatar.FootprintRadius);
                contactShadow=gameObject.AddComponent<ContactShadow>();contactShadow.Configure(avatarRoot.transform, TableY, avatar.FootprintRadius);
                CreateBoundary();
                LoadingPhase=LoadPhase.Ready;
                message = "点击桌面放置，或按下方按钮确认中心位置。";
                Debug.Log("XR_PREVIEW_READY model=" + package.model.sha256 + " heightMeters=" + avatar.HeightMeters + " measuredHeight=" + avatar.MeasuredHeightMeters + " footprint=" + avatar.FootprintRadius);
                return true;
            }
            catch (OperationCanceledException)
            {
                if(this!=null&&revision==loadRevision){LoadingPhase=LoadPhase.Cancelled;message="已取消切换，请重新选择模型。";}
                return false;
            }
            catch (Exception error)
            {
                if(this!=null&&revision==loadRevision){ClearCharacter();LoadingPhase=LoadPhase.Failed;message = "载入失败：" + error.Message;}
                Debug.LogWarning("XR_CHARACTER_LOAD_FAILED: "+error);
                return false;
            }
            finally
            {
                if(pendingRoot!=null)Destroy(pendingRoot);
                if(entered)loadGate.Release();
                if(loadCancellation==request)loadCancellation=null;
                request.Dispose();
            }
        }

        public void CancelCharacterLoad()=>loadCancellation?.Cancel();
        public Task<bool> RetryCharacterLoadAsync()=>SwitchCharacterAsync(characterFolder);
        private void ClearCharacter()
        {
            SizeEditing=false;++SizeEditGeneration;
            Interaction?.Suspend();Interaction=null;
            dropAvailable=false;DropPreviewValid=false;orbiting=false;grabGeneration=0;grabOffset=Vector3.zero;lift=0;
            if(avatarRoot!=null){avatarRoot.SetActive(false);Destroy(avatarRoot);}avatarRoot=null;avatar=null;
            if(contactShadow!=null)Destroy(contactShadow);contactShadow=null;
            if(boundary!=null){if(boundary.sharedMaterial!=null)Destroy(boundary.sharedMaterial);Destroy(boundary.gameObject);}boundary=null;
            if(dropMarker!=null)Destroy(dropMarker.gameObject);dropMarker=null;
        }

        private static bool IsRange(float value, float min, float max) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        private string PreferenceDirectory => preferenceDirectory??Environment.GetEnvironmentVariable("XR_PREFERENCES_DIRECTORY")??Application.persistentDataPath;
        private string ApplicationPreferencesPath => Path.Combine(PreferenceDirectory,"xr-application.json");
        private string PreferencePath(string sha=null) => Path.Combine(PreferenceDirectory, "xr-" + (sha??preferences.modelSha256) + ".json");
        public async Task<string> SaveSpatialPreferencesAsync(XrPreferences draft)
        {
            if(!Ready||draft==null||draft.modelSha256!=preferences.modelSha256)return "模型已变化，请重新打开设置";
            if(!IsRange(draft.heightMeters,.12f,.45f)||!IsRange(draft.halfWidth,.2f,.6f)||!IsRange(draft.halfDepth,.2f,.4f))return "身高需为 12–45 cm；活动区宽 40–120 cm、深 40–80 cm";
            if(Interaction.State==PetState.Held||Interaction.State==PetState.Descending)return "请先将角色放稳，再保存空间设置";
            try
            {
                // 只写本组字段，不覆盖互动选项或未来字段。
                PreferenceFiles.Save(PreferencePath(),new Newtonsoft.Json.Linq.JObject{{"version",2},{"modelSha256",draft.modelSha256},{"heightMeters",draft.heightMeters},{"halfWidth",draft.halfWidth},{"halfDepth",draft.halfDepth}});
            }
            catch(Exception e)when(PreferenceFiles.IsStorageError(e)){return "空间设置保存失败，原文件已保留";}
            return await SwitchCharacterAsync(characterFolder)?null:"设置已保存，但模型重新载入失败；请重新选择模型";
        }
        public string SaveInteractionPreferences(XrPreferences draft)
        {
            if(!Ready||draft==null||draft.modelSha256!=preferences.modelSha256)return "模型已变化，请重新打开设置";
            if(!IsRange(draft.movementSpeed,.75f,3f))return "移动速度应在 0.75–3 倍之间";
            try{PreferenceFiles.Save(PreferencePath(),new Newtonsoft.Json.Linq.JObject{{"version",2},{"modelSha256",draft.modelSha256},{"gazeEnabled",draft.gazeEnabled},{"touchEnabled",draft.touchEnabled},{"movementSpeed",draft.movementSpeed}});}
            catch(Exception e)when(PreferenceFiles.IsStorageError(e)){return "互动设置保存失败，原文件已保留";}
            preferences.gazeEnabled=draft.gazeEnabled;preferences.touchEnabled=draft.touchEnabled;
            preferences.movementSpeed=draft.movementSpeed;
            if(!draft.touchEnabled){avatar.CancelTouch();Interaction.CancelTouch();RefreshSpatialState();}return null;
        }
        public async Task<string> SelectModelAsync(string folder)
        {
            if(!await SwitchCharacterAsync(folder))return "模型切换失败，未保存选择";
            try{PreferenceFiles.Save(ApplicationPreferencesPath,new Newtonsoft.Json.Linq.JObject{{"modelFolder",folder}});return null;}
            catch(Exception e)when(PreferenceFiles.IsStorageError(e)){return "已切换外观，但选择保存失败";}
        }
        public void ResetPlacement()
        {
            if(!Ready)return;
            CancelSizeEdit();
            Interaction.Reset();avatarRoot.SetActive(false);ApplyState();avatar.ResetSecondaryMotion();message="请重新确认桌面并放置。";
            if(ExternalInput&&boundary!=null)boundary.enabled=false;
        }
        public bool BeginSizeEdit(out int generation)
        {
            generation=SizeEditGeneration;
            if(!Ready||!Interaction.PlaneConfirmed||(Interaction.State!=PetState.Idle&&Interaction.State!=PetState.Touched))
            {message="请先确认桌面并把角色放稳，再调整大小。";return false;}
            if(SizeEditing)return false;
            sizeBeforeEdit=avatar.HeightMeters;SizeEditing=true;generation=++SizeEditGeneration;
            avatar.CancelTouch();Interaction.CancelTouch();RefreshSpatialState();message="拖动角色右上方控制点调整大小；确认保存，取消恢复。";return true;
        }
        public bool PreviewSize(int generation,float height)
        {
            if(!SizeEditing||generation!=SizeEditGeneration||!Ready||!float.IsFinite(height))return false;
            height=Mathf.Clamp(height,.12f,.45f);
            if(!Interaction.TryResizeFootprint(avatar.FootprintAtHeight(height))){message="放大后会超过活动区，请缩小或先将角色移到中央。";return false;}
            ResizeVisible(height);message="当前身高 "+(height*100).ToString("0.0")+" cm · 尚未保存";return true;
        }
        private void ResizeVisible(float height)
        {
            avatar.ResizeUniform(height);contactShadow?.SetRadius(avatar.FootprintRadius);
            if(dropMarker!=null)for(int i=0;i<48;i++){float angle=i*Mathf.PI*2/48;dropMarker.SetPosition(i,new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*Interaction.RadiusMeters);}
        }
        public bool CommitSizeEdit(int generation)
        {
            if(!SizeEditing||generation!=SizeEditGeneration||!Ready)return false;
            try{PreferenceFiles.Save(PreferencePath(),new Newtonsoft.Json.Linq.JObject{{"version",2},{"modelSha256",preferences.modelSha256},{"heightMeters",avatar.HeightMeters}});}
            catch(Exception e)when(PreferenceFiles.IsStorageError(e)){message="大小保存失败；预览仍在，可以重试或取消。";return false;}
            preferences.heightMeters=avatar.HeightMeters;SizeEditing=false;++SizeEditGeneration;message="大小已保存。当前桌面继续有效，下次启动仍需重新确认。";return true;
        }
        public void CancelSizeEdit()
        {
            if(!SizeEditing)return;
            if(Ready){Interaction.TryResizeFootprint(avatar.FootprintAtHeight(sizeBeforeEdit));ResizeVisible(sizeBeforeEdit);}
            SizeEditing=false;++SizeEditGeneration;message="已恢复调整前的大小。";
        }

        public bool Place(Vector3 position)
        {
            if(!Ready)return false;
            position.y=TableY;
            return PlaceOnPlane(new Vector3(0,TableY,0),preferences.halfWidth,preferences.halfDepth,position);
        }

        public bool PlaceOnPlane(Vector3 center,float halfWidth,float halfDepth,Vector3 position,float yawDegrees=0)
        {
            if (!Ready) return false;
            CancelSizeEdit();
            position.y = center.y;
            bool placed = Interaction.ConfirmPlane(ToCore(center),halfWidth,halfDepth,ToCore(position),yawDegrees*Mathf.Deg2Rad);
            if (placed)
            {
                contactShadow?.SetPlane(center.y);
                if(boundary!=null){boundary.enabled=true;var rotation=Quaternion.Euler(0,yawDegrees,0);boundary.SetPositions(new[]{center+rotation*new Vector3(-halfWidth,.003f,-halfDepth),center+rotation*new Vector3(halfWidth,.003f,-halfDepth),center+rotation*new Vector3(halfWidth,.003f,halfDepth),center+rotation*new Vector3(-halfWidth,.003f,halfDepth)});}
                avatarRoot.SetActive(true);
                message = "左键点桌面行走；按住头部抚摸；右键按住身体提起。";
                ApplyState();
                if(ExternalInput||!welcomeConsumed)FacePlayerOnce(previewCamera.transform.position);
                if(!welcomeConsumed){welcomeConsumed=true;_=WelcomeWhenVisible(avatar,Interaction.Generation);}
                avatar.ResetSecondaryMotion();
            }
            else message = "放置点太靠近边缘，请往活动区内移。";
            return placed;
        }

        async Task WelcomeWhenVisible(FeiyuAvatar model,int placement)
        {
            await model.Motions.CatalogReady;
            if(this==null||model==null||model!=avatar||!isActiveAndEnabled||SizeEditing||
                Interaction==null||Interaction.Generation!=placement||Interaction.State!=PetState.Idle||
                (Locomotion!=null&&Locomotion.Active))return;
            model.Motions.SetSpatialAllowed(true);
            await model.Motions.Welcome();
        }
        private void FacePlayerOnce(Vector3 viewer)
        {
            Vector3 direction=Vector3.ProjectOnPlane(viewer-avatarRoot.transform.position,Vector3.up);
            if(direction.sqrMagnitude>.0001f)avatarRoot.transform.rotation=Quaternion.LookRotation(direction,Vector3.up);
        }

        private void Update()
        {
            if (!Ready) return;
            if (Interaction.StepDescent(Time.deltaTime, Interaction.Generation)) ApplyState();
            if (ManualInput || ExternalInput) return;
            bool chatUi = Chat.XrChatView.BlocksSceneInput(Input.mousePosition);
            Vector2 guiPointer = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            bool overUi = chatUi || (!Chat.XrChatView.SettingsAvailable && (toolsButton.Contains(guiPointer) || (showTools && panel.Contains(guiPointer))));
            if (Input.GetMouseButtonDown(2)) orbiting = !overUi;
            if (!Input.GetMouseButton(2)) orbiting = false;
            Vector2 drag = orbiting && !overUi ? (Vector2)(Input.mousePosition - previousMousePosition) : Vector2.zero;
            if (Input.GetMouseButtonDown(2)) drag = Vector2.zero;
            previousMousePosition = Input.mousePosition;
            HandleViewInput(drag, overUi ? 0 : Input.mouseScrollDelta.y, !Chat.XrChatView.HasTextFocus && Input.GetKeyDown(KeyCode.F));
            Ray ray = previewCamera.ScreenPointToRay(Input.mousePosition);
            HandlePointer(ray, overUi, Input.GetMouseButtonDown(0), Input.GetMouseButton(0),
                Input.GetMouseButtonDown(1), Input.GetMouseButtonUp(1), Input.mouseScrollDelta.y);
            if (!Chat.XrChatView.HasTextFocus && Input.GetKeyDown(KeyCode.Escape)){if(SizeEditing)CancelSizeEdit();else if(Locomotion.Active)Locomotion.Cancel();else SimulateTrackingLoss();}
        }

        // Editor view input is independent of the future physical XR rig.
        public void HandleViewInput(Vector2 dragPixels, float scroll, bool reset)
        {
            if (!Ready || Interaction.State == PetState.Held) return;
            if (reset)
            {
                InspectingFromDesktop = false;
                previewCamera.transform.SetPositionAndRotation(initialCameraPosition, initialCameraRotation);
                return;
            }
            if (dragPixels == Vector2.zero && scroll == 0) return;
            if (dragPixels != Vector2.zero && !ExternalInput)
            {
                InspectingFromDesktop = true;
                if (Locomotion.State == PetLocomotion.Phase.Facing) Locomotion.Cancel();
            }
            Vector3 offset = previewCamera.transform.position - viewTarget;
            float distance = Mathf.Clamp(offset.magnitude * Mathf.Exp(-scroll * .12f), .45f, 2.5f);
            float yaw = Mathf.Atan2(offset.x, offset.z) + dragPixels.x * .006f;
            float pitch = Mathf.Clamp(Mathf.Asin(offset.y / offset.magnitude) - dragPixels.y * .006f, 10 * Mathf.Deg2Rad, 75 * Mathf.Deg2Rad);
            offset = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * distance;
            previewCamera.transform.position = viewTarget + offset;
            previewCamera.transform.LookAt(viewTarget);
        }

        // A mouse/hand adapter supplies intent; ray hit tests and ownership stay here.
        // Tests can exercise the same path without driving the user's desktop pointer.
        public void HandlePointer(Ray ray, bool overUi, bool leftDown, bool leftHeld, bool rightDown, bool rightUp, float scroll)
        {
            if (!Ready) return;
            if(SizeEditing)return;
            avatar.PointerContactHeld=leftHeld&&!overUi&&preferences.touchEnabled;
            Physics.SyncTransforms();
            if (Interaction.State == PetState.Held)
            {
                // Sample this frame, including button-up; stale held positions must not decide a drop.
                var plane = new Plane(Vector3.up, new Vector3(0, TableY, 0));
                float distance = 0;
                dropAvailable = !overUi && plane.Raycast(ray, out distance);
                var position = dropAvailable ? ray.GetPoint(distance) - grabOffset : Vector3.zero;
                position.y = TableY;
                // Releasing over UI ends the grab safely, but never places through UI.
                if (rightUp)
                {
                    // Mouse release requests placement on the table. Physical MR contact rules remain strict.
                    var release = dropAvailable ? ToCore(position) : new Vec3(float.NaN, 0, 0);
                    Interaction.ReleaseGrabWithDescent(MouseOwner, grabGeneration, release);
                    message = Interaction.LastReleaseWasValid ? "正在下落。" : "落点在活动区外，正在落回安全位置。";
                }
                else if (dropAvailable)
                {
                    lift = Mathf.Clamp(lift + scroll * 0.05f, 0, 2f);
                    position.y = TableY + lift;
                    Interaction.MoveGrab(MouseOwner, grabGeneration, ToCore(position));
                }
            }
            else if (!overUi)
            {
                if (!Interaction.PlaneConfirmed && leftDown)
                {
                    var plane = new Plane(Vector3.up, new Vector3(0, TableY, 0));
                    if (plane.Raycast(ray, out float distance)) Place(ray.GetPoint(distance));
                }
                else if (Interaction.PlaneConfirmed)
                {
                    bool onHead = avatar.TouchProxy.Raycast(ray, out var touchHit, 4);
                    if(leftDown&&!onHead&&!avatar.GrabProxy.Raycast(ray,out _,4))
                    {
                        var plane=new Plane(Vector3.up,ToUnity(Interaction.PlaneCenter));
                        if(plane.Raycast(ray,out float distance))_=Locomotion.GoTo(ray.GetPoint(distance));
                    }
                    if (preferences.touchEnabled && leftHeld && onHead) { if(Interaction.BeginTouch(MouseOwner))avatar.SampleTouch(touchHit.point); }
                    else Interaction.EndTouch(MouseOwner);
                    bool onTail = avatar.TailGrabProxy != null && avatar.TailGrabProxy.Raycast(ray, out _, 4);
                    if (rightDown && (onTail || avatar.GrabProxy.Raycast(ray, out _, 4)) &&
                        avatar.SelectGrabPoint(onTail) && Interaction.BeginGrab(MouseOwner, out grabGeneration))
                    {
                        lift = Mathf.Max(0, Interaction.Position.Y - TableY);
                        var plane = new Plane(Vector3.up, new Vector3(0, TableY, 0));
                        var position = ToUnity(Interaction.Position);
                        position.y = TableY + lift;
                        grabOffset = plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) - position : Vector3.zero;
                        grabOffset.y = 0;
                        Interaction.MoveGrab(MouseOwner, grabGeneration, ToCore(position));
                        dropAvailable = true;
                    }
                }
            }
            else Interaction.EndTouch(MouseOwner);
            ApplyState();
        }

        private void LateUpdate()
        {
            if (Ready)
            {
                avatar.Motions?.SetSpatialAllowed(Interaction.PlaneConfirmed&&Interaction.State==PetState.Idle);
                if(avatarRoot.activeSelf){avatar.GazeEnabled=preferences.gazeEnabled && !InspectingFromDesktop;avatar.SetSpatialFeedback(Interaction);avatar.Present(previewCamera.transform.position);}
            }
        }

        private void ApplyState()
        {
            avatarRoot.transform.position = ToUnity(Interaction.Position);
            avatar.SetFeedback(Interaction.State == PetState.Touched, Interaction.State == PetState.Suspended, Interaction.State!=PetState.Idle&&Interaction.State!=PetState.Touched);
            if (boundary != null) {boundary.startColor=Interaction.PlaneConfirmed?new Color(.36f,.82f,1,.9f):Color.gray;boundary.endColor=Interaction.PlaneConfirmed?new Color(.30f,.61f,.96f,.9f):Color.gray;}
            bool held = Interaction.State == PetState.Held;
            if(ExternalInput&&boundary!=null)boundary.enabled=Interaction.PlaneConfirmed&&(held||SizeEditing);
            if(ExternalInput)dropAvailable=held;
            DropPreviewValid = held && dropAvailable &&
                Interaction.ContainsFootprint(Interaction.Position,Interaction.RadiusMeters);
            if (dropMarker != null)
            {
                dropMarker.enabled = held && dropAvailable;
                dropMarker.transform.position = new Vector3(Interaction.Position.X, Interaction.PlaneCenter.Y + .005f, Interaction.Position.Z);
                if(dropMarker.enabled&&DropPreviewValid)dropRipple.Show(dropMarker.transform.position,Interaction.RadiusMeters*.94f,false,true);else dropRipple.Hide();
                dropMarker.startColor = dropMarker.endColor = DropPreviewValid ? new Color(.42f,.79f,1,.95f) : new Color(1, .2f, .15f);
            }
            if (held) message = DropPreviewValid ? "绿色落点：松开右键即可放回桌面。滚轮可调整提起高度。" : "落点无效：请把整个圆圈移进活动区；在界面上松手会回安全点。";
        }

        public void SimulateTrackingLoss()
        {
            if (!Ready) return;
            CancelSizeEdit();
            Interaction.Suspend();
            var chat = FindAnyObjectByType<Chat.XrChatController>();
            if (chat != null) chat.Stop(); else if (speech != null) speech.Stop();
            ApplyState();
            avatar.ResetSecondaryMotion();
            message = "空间已失效；请重新确认桌面。";
        }

        // XR 的短暂系统遮罩由 QuestEntry 暂停输入，不能让桌面预览再次清掉已确认平台。
        private void OnApplicationFocus(bool focused) { if (!ExternalInput&&!focused) SimulateTrackingLoss(); }
        private void OnApplicationPause(bool paused) { if (!ExternalInput&&paused) SimulateTrackingLoss(); }
        private void OnDisable() { CancelCharacterLoad();if (Ready && avatarRoot != null) SimulateTrackingLoss(); if (speech != null) speech.Stop(); }

        private void CreateBoundary()
        {
            boundary = new GameObject("Confirmed activity boundary").AddComponent<LineRenderer>();
            boundary.transform.SetParent(transform, false);
            boundary.useWorldSpace = true;
            boundary.loop = true;
            boundary.enabled=!ExternalInput;
            boundary.positionCount = 4;
            boundary.widthMultiplier = 0.003f;
            boundary.numCornerVertices=5;
            boundary.material = new Material(Shader.Find("Sprites/Default"));
            float w = preferences.halfWidth, d = preferences.halfDepth;
            boundary.SetPositions(new[] { new Vector3(-w, TableY + .003f, -d), new Vector3(w, TableY + .003f, -d), new Vector3(w, TableY + .003f, d), new Vector3(-w, TableY + .003f, d) });
            dropMarker = new GameObject("Proposed tabletop footprint").AddComponent<LineRenderer>();
            dropMarker.transform.SetParent(transform, false);
            dropMarker.useWorldSpace = false;
            dropMarker.loop = true;
            dropMarker.positionCount = 48;
            dropMarker.widthMultiplier = .004f;
            dropMarker.sharedMaterial = boundary.sharedMaterial;
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI * 2 / 48;
                dropMarker.SetPosition(i, new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * Interaction.RadiusMeters);
            }
            var inner=new GameObject("放回提示水纹");inner.transform.SetParent(dropMarker.transform,false);dropRipple=inner.AddComponent<TableWaterRings>();dropRipple.Hide();
            dropMarker.enabled = false;
        }

        private void OnGUI()
        {
            if(ExternalInput)return;
            if(Chat.XrChatView.SettingsAvailable)return;
            if (titleStyle == null)
            {
                uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 17);
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 23, font = uiFont, fontStyle = FontStyle.Bold };
                bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, font = uiFont, wordWrap = true };
            }
            GUI.skin.font = uiFont;
            if (GUI.Button(toolsButton, showTools ? "收起空间设置" : "空间与放置")) showTools = !showTools;
            if (!showTools) return;
            GUILayout.BeginArea(panel, GUI.skin.box);
            GUILayout.Label("小肥鱼 · 互动小样", titleStyle);
            GUILayout.Label((Ready?avatar.Package.displayName:"角色预览")+" · 模拟桌面", bodyStyle);
            if(packageFolders.Length>0)
            {
                selectedPackage=GUILayout.SelectionGrid(selectedPackage,packageLabels,1);
                if(GUILayout.Button("切换到选中模型",GUILayout.Height(28)))SwitchFromUi(packageFolders[selectedPackage]);
            }
            if(Loading&&GUILayout.Button("取消载入"))CancelCharacterLoad();
            GUILayout.Label("中键拖动旋转 · 滚轮缩放 · F 重置视角\n左键摸头 · 右键提起，松开放回", bodyStyle);
            GUILayout.Space(10);
            GUILayout.Label(message, bodyStyle, GUILayout.Height(70));
            GUI.enabled = Ready;
            if (GUILayout.Button("确认桌面并放在中心", GUILayout.Height(32))) Place(new Vector3(0, TableY, 0));
            if (GUILayout.Button("重置视角 / F（放回后可用）", GUILayout.Height(32))) HandleViewInput(Vector2.zero, 0, true);
            if (GUILayout.Button("模拟追踪丢失 / Esc", GUILayout.Height(32))) SimulateTrackingLoss();
            if (GUILayout.Button("重置放置", GUILayout.Height(32)))
            {
                Interaction.Reset(); avatarRoot.SetActive(false); ApplyState(); avatar.ResetSecondaryMotion(); message = "请重新确认桌面并放置。";
            }
            if (GUILayout.Button("保存身高与活动范围", GUILayout.Height(32)))
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath())); PreferenceFiles.Save(PreferencePath(), Newtonsoft.Json.Linq.JObject.FromObject(preferences)); message = "已保存；下次启动仍需重新确认桌面。"; }
                catch (Exception error) { message = "保存失败：" + error.Message; }
            }
            GUI.enabled = true;
            GUILayout.Label(Ready ? "状态：" + StateLabel(Interaction.State) : "正在准备模型", bodyStyle);
            GUILayout.Label("当前无行走片段，限区自主行走待接入。", bodyStyle);
            GUILayout.EndArea();
        }

        private static string StateLabel(PetState state) => state switch
        {
            PetState.Idle => "已放置", PetState.Touched => "被抚摸", PetState.Held => "被提起",
            PetState.Descending => "下落中", PetState.Settling => "接触收稳",
            PetState.Suspended => "等待重新确认", _ => "等待放置"
        };
        public static Vec3 ToCore(Vector3 value) => new Vec3(value.x, value.y, value.z);
        public static Vector3 ToUnity(Vec3 value) => new Vector3(value.X, value.Y, value.Z);

        private void OnDestroy()
        {
            ++loadRevision;lifetime?.Cancel();loadCancellation?.Cancel();ClearCharacter();lifetime?.Dispose();
            if (uiFont != null) Destroy(uiFont);
        }
        private async void SwitchFromUi(string folder){await SwitchCharacterAsync(folder);}
        private async Task RefreshPackages()
        {
            var catalog=await CharacterCatalog.Load(Application.streamingAssetsPath+"/Characters",lifetime.Token);
            packageFolders=catalog.folders;packageLabels=catalog.labels;selectedPackage=Mathf.Max(0,Array.IndexOf(packageFolders,characterFolder));
        }
    }
}
