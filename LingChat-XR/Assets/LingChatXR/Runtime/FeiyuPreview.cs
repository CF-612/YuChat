using System;
using System.IO;
using System.Threading;
using LingChatXR.Core;
using UnityEngine;

namespace LingChatXR
{
    [Serializable] public sealed class XrPreferences
    {
        public int version = 2;
        public string modelSha256;
        public float heightMeters = 0.24f;
        public float halfWidth = 0.60f;
        public float halfDepth = 0.40f;
    }

    public sealed class FeiyuPreview : MonoBehaviour
    {
        [SerializeField] private Camera previewCamera;
        [SerializeField] private string characterFolder = "blue-placeholder";
        private const float TableY = 0.74f;
        private const string MouseOwner = "editor-mouse";
        private readonly Rect panel = new Rect(18, 18, 325, 450);
        private CancellationTokenSource lifetime;
        private FeiyuAvatar avatar;
        private GameObject avatarRoot;
        private LineRenderer boundary;
        private LineRenderer dropMarker;
        private bool dropAvailable;
        private Vector3 initialCameraPosition;
        private Quaternion initialCameraRotation;
        private readonly Vector3 viewTarget = new Vector3(-.08f, .84f, 0);
        private Vector3 previousMousePosition;
        private bool orbiting;
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
        public string Status => message;
        public bool DropPreviewValid { get; private set; }

        public void SetCamera(Camera value) => previewCamera = value;

        private async void Start()
        {
            lifetime = new CancellationTokenSource();
            try
            {
                speech = GetComponent<Speech.XrSpeechController>();
                if (speech == null) speech = gameObject.AddComponent<Speech.XrSpeechController>();
                if (previewCamera == null) throw new InvalidOperationException("Preview camera is not wired.");
                initialCameraPosition = previewCamera.transform.position;
                initialCameraRotation = previewCamera.transform.rotation;
                var folder = Application.streamingAssetsPath + "/Characters/" + characterFolder;
                var manifestBytes = await PackageFiles.ReadAsync(folder, "manifest.json", lifetime.Token);
                var package = JsonUtility.FromJson<CharacterPackage>(System.Text.Encoding.UTF8.GetString(manifestBytes));
                if (package?.model?.sha256 == null || package.model.sha256.Length != 64 ||
                    Array.Exists(package.model.sha256.ToCharArray(), value => !Uri.IsHexDigit(value)))
                    throw new InvalidDataException("Missing model fingerprint.");
                preferences.modelSha256 = package.model.sha256;
                string saved = PreferencePath();
                if (File.Exists(saved))
                {
                    var candidate = JsonUtility.FromJson<XrPreferences>(File.ReadAllText(saved));
                    if (candidate != null && (candidate.version == 1 || candidate.version == 2) && candidate.modelSha256 == preferences.modelSha256 &&
                        IsRange(candidate.heightMeters, 0.12f, 0.45f) && IsRange(candidate.halfWidth, 0.2f, 0.6f) && IsRange(candidate.halfDepth, 0.2f, 0.4f))
                    {
                        // Upgrade only the previous preview defaults; retain custom calibration and height.
                        if (candidate.version == 1 && Mathf.Approximately(candidate.halfWidth, .3f) && Mathf.Approximately(candidate.halfDepth, .22f))
                        { candidate.halfWidth = .6f; candidate.halfDepth = .4f; }
                        candidate.version = 2;
                        preferences = candidate;
                    }
                }
                avatarRoot = new GameObject("Pet spatial root");
                avatar = avatarRoot.AddComponent<FeiyuAvatar>();
                await avatar.LoadAsync(folder, preferences.heightMeters, lifetime.Token);
                Interaction = new PetInteraction(avatar.FootprintRadius);
                avatarRoot.SetActive(false);
                CreateBoundary();
                message = "点击桌面放置，或按下方按钮确认中心位置。";
                Debug.Log("XR_PREVIEW_READY model=" + package.model.sha256 + " heightMeters=" + avatar.HeightMeters + " measuredHeight=" + avatar.MeasuredHeightMeters + " footprint=" + avatar.FootprintRadius);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (avatarRoot != null) Destroy(avatarRoot);
                message = "载入失败：" + error.Message;
                Debug.LogException(error);
            }
        }

        private static bool IsRange(float value, float min, float max) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        private string PreferencePath() => Path.Combine(Application.persistentDataPath, "xr-" + preferences.modelSha256 + ".json");

        public bool Place(Vector3 position)
        {
            if (!Ready) return false;
            position.y = TableY;
            bool placed = Interaction.ConfirmPlane(new Vec3(0, TableY, 0), preferences.halfWidth, preferences.halfDepth, ToCore(position));
            if (placed)
            {
                avatarRoot.SetActive(true);
                message = "左键按住头部抚摸；右键按住身体提起。";
                ApplyState();
            }
            else message = "放置点太靠近边缘，请往活动区内移。";
            return placed;
        }

        private void Update()
        {
            if (!Ready) return;
            bool overUi = panel.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            if (Input.GetMouseButtonDown(2)) orbiting = !overUi;
            if (!Input.GetMouseButton(2)) orbiting = false;
            Vector2 drag = orbiting && !overUi ? (Vector2)(Input.mousePosition - previousMousePosition) : Vector2.zero;
            if (Input.GetMouseButtonDown(2)) drag = Vector2.zero;
            previousMousePosition = Input.mousePosition;
            HandleViewInput(drag, overUi ? 0 : Input.mouseScrollDelta.y, Input.GetKeyDown(KeyCode.F));
            Ray ray = previewCamera.ScreenPointToRay(Input.mousePosition);
            HandlePointer(ray, overUi, Input.GetMouseButtonDown(0), Input.GetMouseButton(0),
                Input.GetMouseButtonDown(1), Input.GetMouseButtonUp(1), Input.mouseScrollDelta.y);
            if (Input.GetKeyDown(KeyCode.Escape)) SimulateTrackingLoss();
        }

        // Editor view input is independent of the future physical XR rig.
        public void HandleViewInput(Vector2 dragPixels, float scroll, bool reset)
        {
            if (!Ready || Interaction.State == PetState.Held) return;
            if (reset)
            {
                previewCamera.transform.SetPositionAndRotation(initialCameraPosition, initialCameraRotation);
                return;
            }
            if (dragPixels == Vector2.zero && scroll == 0) return;
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
                    Interaction.ReleaseGrab(MouseOwner, grabGeneration, release);
                    message = Interaction.LastReleaseWasValid ? "已放回，从新位置继续。" : "落点无效，已回到最近的安全位置。";
                }
                else if (dropAvailable)
                {
                    lift = Mathf.Clamp(lift + scroll * 0.025f, 0, 0.65f);
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
                    bool onHead = avatar.TouchProxy.Raycast(ray, out _, 4);
                    if (leftHeld && onHead) Interaction.BeginTouch(MouseOwner);
                    else Interaction.EndTouch(MouseOwner);
                    if (rightDown && avatar.GrabProxy.Raycast(ray, out _, 4) && Interaction.BeginGrab(MouseOwner, out grabGeneration))
                    {
                        lift = 0.12f;
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
            if (Ready && avatarRoot.activeSelf) avatar.Present(previewCamera.transform.position);
        }

        private void ApplyState()
        {
            avatarRoot.transform.position = ToUnity(Interaction.Position);
            avatar.SetFeedback(Interaction.State == PetState.Touched, Interaction.State == PetState.Suspended);
            if (boundary != null) boundary.startColor = boundary.endColor = Interaction.PlaneConfirmed ? new Color(0.25f, 0.85f, 0.68f) : new Color(0.7f, 0.7f, 0.7f);
            bool held = Interaction.State == PetState.Held;
            DropPreviewValid = held && dropAvailable &&
                Math.Abs((double)Interaction.Position.X) <= (double)preferences.halfWidth - Interaction.RadiusMeters &&
                Math.Abs((double)Interaction.Position.Z) <= (double)preferences.halfDepth - Interaction.RadiusMeters;
            if (dropMarker != null)
            {
                dropMarker.enabled = held && dropAvailable;
                dropMarker.transform.position = new Vector3(Interaction.Position.X, TableY + .005f, Interaction.Position.Z);
                dropMarker.startColor = dropMarker.endColor = DropPreviewValid ? Color.green : new Color(1, .2f, .15f);
            }
            if (held) message = DropPreviewValid ? "绿色落点：松开右键即可放回桌面。滚轮可调整提起高度。" : "落点无效：请把整个圆圈移进活动区；在界面上松手会回安全点。";
        }

        public void SimulateTrackingLoss()
        {
            if (!Ready) return;
            Interaction.Suspend();
            if (speech != null) speech.Stop();
            ApplyState();
            message = "空间已失效；请重新确认桌面。";
        }

        private void OnApplicationFocus(bool focused) { if (!focused) SimulateTrackingLoss(); }
        private void OnApplicationPause(bool paused) { if (paused) SimulateTrackingLoss(); }
        private void OnDisable() { if (Ready && avatarRoot != null) SimulateTrackingLoss(); if (speech != null) speech.Stop(); }

        private void CreateBoundary()
        {
            boundary = new GameObject("Confirmed activity boundary").AddComponent<LineRenderer>();
            boundary.transform.SetParent(transform, false);
            boundary.useWorldSpace = true;
            boundary.loop = true;
            boundary.positionCount = 4;
            boundary.widthMultiplier = 0.003f;
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
            dropMarker.enabled = false;
        }

        private void OnGUI()
        {
            if (titleStyle == null)
            {
                uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 17);
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 23, font = uiFont, fontStyle = FontStyle.Bold };
                bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, font = uiFont, wordWrap = true };
            }
            GUI.skin.font = uiFont;
            GUILayout.BeginArea(panel, GUI.skin.box);
            GUILayout.Label("小肥鱼 · 互动小样", titleStyle);
            GUILayout.Label("Editor 模拟桌面", bodyStyle);
            GUILayout.Label("中键拖动旋转 · 滚轮缩放 · F 重置视角\n左键摸头 · 右键提起，松开放回", bodyStyle);
            GUILayout.Space(10);
            GUILayout.Label(message, bodyStyle, GUILayout.Height(70));
            GUI.enabled = Ready;
            if (GUILayout.Button("确认桌面并放在中心", GUILayout.Height(32))) Place(new Vector3(0, TableY, 0));
            if (GUILayout.Button("重置视角 / F（放回后可用）", GUILayout.Height(32))) HandleViewInput(Vector2.zero, 0, true);
            if (GUILayout.Button("模拟追踪丢失 / Esc", GUILayout.Height(32))) SimulateTrackingLoss();
            if (GUILayout.Button("重置放置", GUILayout.Height(32)))
            {
                Interaction.Reset(); avatarRoot.SetActive(false); ApplyState(); message = "请重新确认桌面并放置。";
            }
            if (GUILayout.Button("保存身高与活动范围", GUILayout.Height(32)))
            {
                try { Directory.CreateDirectory(Application.persistentDataPath); File.WriteAllText(PreferencePath(), JsonUtility.ToJson(preferences, true)); message = "已保存；下次启动仍需重新确认桌面。"; }
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
            PetState.Suspended => "等待重新确认", _ => "等待放置"
        };
        public static Vec3 ToCore(Vector3 value) => new Vec3(value.x, value.y, value.z);
        public static Vector3 ToUnity(Vec3 value) => new Vector3(value.X, value.Y, value.Z);

        private void OnDestroy()
        {
            lifetime?.Cancel(); lifetime?.Dispose();
            if (avatarRoot != null) Destroy(avatarRoot);
            if (boundary != null && boundary.sharedMaterial != null) Destroy(boundary.sharedMaterial);
            if (uiFont != null) Destroy(uiFont);
        }
    }
}
