using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniVRM10;
using UnityEngine;

namespace LingChatXR
{
    // Only this adapter writes the model's bones/expressions. The controller owns world translation.
    public sealed class FeiyuAvatar : MonoBehaviour
    {
        public Vrm10Instance Instance { get; private set; }
        public CharacterPackage Package { get; private set; }
        public PresentationProfile Presentation { get; private set; }
        public float HeightMeters { get; private set; }
        public float FootprintRadius { get; private set; }
        public float MeasuredHeightMeters { get; private set; }
        public SphereCollider TouchProxy { get; private set; }
        public CapsuleCollider GrabProxy { get; private set; }
        public Transform HeadGripBone=>head;
        public SphereCollider TailGrabProxy { get; private set; }
        public Transform HeldGripBone => Motions != null && Motions.TailGrabbed ? TailGrabProxy?.transform : head;
        public float HeldGripPadding => Motions != null && Motions.TailGrabbed ? 0 : HeightMeters * .08f;
        public bool SelectGrabPoint(bool tail) => Motions != null && (!tail || TailGrabProxy != null) && Motions.SelectTailGrab(tail);
        private Transform head;
        private Transform gaze;
        private Quaternion headRest;
        private float happyWeight;
        ExpressionKey EyeSmile=ExpressionKey.CreateCustom("eyeSmile"), BrowHappy=ExpressionKey.CreateCustom("browHappy"),
            CheekSlashes=ExpressionKey.CreateCustom("cheekSlashes"), MouthCircle=ExpressionKey.CreateCustom("mouthOpenCircle");
        ExpressionKey ShockEye=ExpressionKey.CreateCustom("eyeCircleShock");
                ExpressionKey SpiralEye=ExpressionKey.CreateCustom("eyeSpiral");
        bool squeezePose,heldReaction;
        int voiceReactionSerial;
        const float DizzySeconds=3f, ScrunchSeconds=.5f;
        float dizzyUntil, landingScrunchUntil;
        bool fallingReaction, fallingDizzy, fallingScrunch;
        public void ShakeWhileHeld()
        {
            if(spiralAvailable&&heldReaction)dizzyUntil=Time.unscaledTime+DizzySeconds;
            if(heldReaction)chat?.ShortVoice?.Play("surprised",GetInstanceID()+"-shake-"+(++voiceReactionSerial),20,true);
        }
        private float happyBase;
        ExpressionKey MouthSmile=ExpressionKey.CreateCustom("mouthSmile");
        bool hasMouthSmile;float smileBase;
        private float blinkBase;
        private float nextBlink;
        private float blinkStart = -100;
        private bool touching;
        private bool suspended;
        private Transform chest;
        private Quaternion chestRest;
        private float settleAmount;

        private float fallTarget, fallPose, impact=1;
        private Transform leftArm,rightArm;
        private Quaternion leftArmRest,rightArmRest;
        private Chat.XrChatController chat;
        private Speech.XrSpeechController speech;
        private readonly float[] voiceSamples = new float[256];
        private float mouth;


        public bool UsesMouth24Face {get;private set;}
        bool spiralAvailable;


        MoodSticker sticker;
        readonly System.Collections.Generic.HashSet<ExpressionKey> faceKeys=new System.Collections.Generic.HashSet<ExpressionKey>();
        float surprisedMouthUntil;
        public string PreviewEmotion {get;set;}
        public string EyeOwner {get;private set;}
        public string MouthOwner {get;private set;}
        // 兼容旧检查入口；运行时能力来自显式配置，不再由模型哈希开启。
        public bool IsAlpha2 => UsesMouth24Face || Presentation?.animeMaterials!=null || Presentation?.secondaryBodyFeedback==true;
        public bool FreezePresentation { get; set; }
        public bool GazeEnabled { get; set; } = true;
        public Chat.XrMotionPlayer Motions {get;private set;}
        public bool MotionPreviewMode {get;set;}
        SharedVrmResources sharedResources;
        SharedVrmResources.Lease previewResources;
        public SharedVrmResources ResourcesForPreview()=>sharedResources??=new SharedVrmResources(Instance.GetComponent<UniGLTF.RuntimeGltfInstance>(),Package.model.sha256);
        private Transform presentationNeck,presentationHead,presentationChest,presentationLeftArm,presentationRightArm;
        Vector3? attentionLeft,attentionRight;
        int gazeHand;
        Quaternion eyeAim=Quaternion.identity;
        float smoothEyesUntil;
        public bool FollowingHand=>gazeHand!=0;
        public void SetNearbyHands(Vector3? left,Vector3? right)
        {
            attentionLeft=left;attentionRight=right;
        }
        Vector3 AttentionPoint(Vector3 viewer,bool petting)
        {
            if(petting||!idleAllowed||!GazeEnabled||MotionPreviewMode||Motions.Busy)
            {gazeHand=0;return viewer;}
            const float enter=.26f,leave=.34f,margin=.035f;
            float left=attentionLeft.HasValue?Vector3.Distance(head.position,attentionLeft.Value):float.PositiveInfinity;
            float right=attentionRight.HasValue?Vector3.Distance(head.position,attentionRight.Value):float.PositiveInfinity;
            if(gazeHand==1&&left<=leave && !(right<=enter&&right+margin<left))return attentionLeft.Value;
            if(gazeHand==2&&right<=leave && !(left<=enter&&left+margin<right))return attentionRight.Value;
            gazeHand=Mathf.Min(left,right)>enter?0:left<=right?1:2;
            return gazeHand==1?attentionLeft.Value:gazeHand==2?attentionRight.Value:viewer;
        }
        private Quaternion gazeOffset=Quaternion.identity;
        public float FootprintAtHeight(float height)=>(FootprintRadius-.005f)*(height/HeightMeters)+.005f;
        public void ResizeUniform(float height)
        {
            CancelTouch();
            if(Instance==null||!float.IsFinite(height)||height<.12f||height>.45f)throw new ArgumentOutOfRangeException(nameof(height));
            if(Mathf.Approximately(height,HeightMeters))return;
            float ratio=height/HeightMeters;
            // 在同一实例上改变统一尺度，保留当前骨骼、表情和音频；不重新载入模型。
            Instance.transform.localScale*=ratio;Instance.transform.localPosition*=ratio;
            MeasuredHeightMeters*=ratio;FootprintRadius=FootprintAtHeight(height);HeightMeters=height;
            if(TailGrabProxy!=null)TailGrabProxy.radius=height*.10f/Mathf.Abs(TailGrabProxy.transform.lossyScale.x);
            GrabProxy.center=Vector3.up*height*.45f;GrabProxy.height=height*.65f;GrabProxy.radius=height*.15f;
            Physics?.CaptureStructuralScale();
            Physics?.RequestReset();
        }
        private Transform[] originalCenters;
        public PortableSpringPhysics Physics { get; private set; }
        public bool UsesAuthorPhysics { get; private set; }

        public async Task LoadAsync(string folder, float heightMeters, CancellationToken token, bool useAuthorPhysics = false, Action<string> progress = null, FeiyuAvatar sharedSource = null)
        {
            if (Instance != null) throw new InvalidOperationException("Replace an avatar by creating a new adapter instance.");
            using var importLease=sharedSource!=null?sharedSource.ResourcesForPreview().Acquire():null;
            UsesAuthorPhysics = useAuthorPhysics;
            progress?.Invoke("读取角色资料");
            var manifest = await PackageFiles.ReadAsync(folder, "manifest.json", token);
            Package = JsonUtility.FromJson<CharacterPackage>(Encoding.UTF8.GetString(manifest));
            if (Package == null || Package.schemaVersion != 1 || Package.model == null)
                throw new InvalidDataException("Unsupported character package.");
            if(!string.IsNullOrEmpty(Package.presentationProfile))
            {
                try
                {
                    var profileBytes=await PackageFiles.ReadAsync(folder,Package.presentationProfile,token);
                    if(profileBytes.Length>65536)throw new InvalidDataException("表现配置过大");
                    Presentation=JsonUtility.FromJson<PresentationProfile>(Encoding.UTF8.GetString(profileBytes));
                    if(Presentation==null||!Presentation.Valid)throw new InvalidDataException("表现配置格式无效");
                }
                catch(Exception error) when(error is IOException || error is ArgumentException)
                {Presentation=null;Debug.LogWarning("表现配置未启用，使用通用 VRM 行为："+error.Message);}
            }
            ExpressionKey Binding(string semantic)=>ExpressionKey.CreateCustom(Presentation?.Key(semantic)??"__unbound_"+semantic);
            EyeSmile=Binding("eyeSmile");BrowHappy=Binding("browHappy");CheekSlashes=Binding("cheekSlashes");MouthCircle=Binding("mouthOpenCircle");
            ShockEye=Binding("eyeCircleShock");SpiralEye=Binding("eyeSpiral");MouthSmile=Binding("mouthSmile");Blush=Binding("blush");SqueezedEyes=Binding("eyeTroubled");SqueezeClosure=Binding("troubled");
            var bytes = await PackageFiles.ReadAsync(folder, Package.model.path, token);
            if (!string.Equals(PackageFiles.Hash(bytes), Package.model.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Model SHA256 does not match manifest.");
            if(importLease!=null&&!string.Equals(importLease.Resources.ModelSha,Package.model.sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("预览资源与当前模型不一致");
            progress?.Invoke(importLease==null?"导入模型与贴图":"复用贴图 · 准备独立骨骼");
            VrmRuntimeShaders.Validate();
            Instance = importLease!=null?await importLease.Resources.ImportPreview(bytes,token):await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: true,
                controlRigGenerationOption: ControlRigGenerationOption.Generate,
                awaitCaller: new UniGLTF.RuntimeOnlyAwaitCaller(MotionPreviewMode ? .004f : .006f), ct: token);
            if (token.IsCancellationRequested || this == null)
            {
                if (Instance != null) Destroy(Instance.gameObject);
                token.ThrowIfCancellationRequested();
                return;
            }
            if(importLease!=null)previewResources=importLease.Resources.Acquire();
            progress?.Invoke("准备骨骼与物理");
            // UniVRM 控制骨必须在导入时的 T 姿势、原始根变换下建立。
            // PoseArm 之后才访问 Runtime，会把放松手臂误当成 T 姿势，再叠加一次动作旋转。
            _ = Instance.Runtime;
            Instance.transform.SetParent(transform, false);
            Instance.transform.localPosition = Vector3.zero;
            Instance.transform.localRotation = Quaternion.identity;
            // Bake actual geometry once. Converted sparse POSITION accessor bounds may be degenerate.
            Bounds bounds = MeasureGeometry(Instance.gameObject, transform);
            if (bounds.size.y < 0.01f) throw new InvalidDataException("Model has no measurable height.");
            HeightMeters = Mathf.Clamp(heightMeters, 0.12f, 0.45f);
            float scale = HeightMeters / bounds.size.y;
            Instance.transform.localScale = Vector3.one * scale;
            Instance.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            PoseArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, -1);
            PoseArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 1);
            Instance.TryGetBoneTransform(HumanBodyBones.LeftUpperArm,out leftArm);if(leftArm!=null)leftArmRest=leftArm.localRotation;
            Instance.TryGetBoneTransform(HumanBodyBones.RightUpperArm,out rightArm);if(rightArm!=null)rightArmRest=rightArm.localRotation;
            // Centre the placement root between the feet instead of the full mesh (hair can be asymmetric).
            if (Instance.TryGetBoneTransform(HumanBodyBones.LeftFoot, out var leftFoot) &&
                Instance.TryGetBoneTransform(HumanBodyBones.RightFoot, out var rightFoot))
            {
                Vector3 feetCentre = transform.InverseTransformPoint((leftFoot.position + rightFoot.position) * .5f);
                Instance.transform.localPosition -= new Vector3(feetCentre.x, 0, feetCentre.z);
            }
            // Placement uses ground support geometry, not hair, skirt or a source T-pose.
            bounds = MeasureGeometry(Instance.gameObject, transform);
            MeasuredHeightMeters = bounds.size.y;
            FootprintRadius = MeasureSupportRadius();
            if (Instance.TryGetBoneTransform(HumanBodyBones.Head, out head)) headRest = head.localRotation;
            if (Instance.TryGetBoneTransform(HumanBodyBones.Chest, out chest)) chestRest = chest.localRotation;
            Motions=new Chat.XrMotionPlayer(Instance,MotionPreviewMode,Package.model.sha256);
            var tailPath = Package.interactionBindings?.tailGripPath;
            if (!string.IsNullOrEmpty(tailPath))
            {
                var tail = Instance.transform.Find(tailPath);
                if (tail != null)
                {
                    TailGrabProxy = tail.gameObject.AddComponent<SphereCollider>();
                    TailGrabProxy.isTrigger = true;
                    TailGrabProxy.radius = HeightMeters * .10f / Mathf.Abs(tail.lossyScale.x);
                }
                else Debug.LogWarning("角色包的尾巴抓点路径不存在：" + tailPath);
            }
            if(!MotionPreviewMode)_=Motions.PrepareGrabbed();
            var rig=Instance.Runtime.ControlRig;
            presentationNeck=rig.GetBoneTransform(HumanBodyBones.Neck);presentationHead=rig.GetBoneTransform(HumanBodyBones.Head);presentationChest=rig.GetBoneTransform(HumanBodyBones.Chest);
            presentationLeftArm=rig.GetBoneTransform(HumanBodyBones.LeftUpperArm);presentationRightArm=rig.GetBoneTransform(HumanBodyBones.RightUpperArm);
            chat = FindAnyObjectByType<Chat.XrChatController>();speech=chat?.Voice;
            if(!MotionPreviewMode)TouchSound=gameObject.AddComponent<PetTouchSound>();
            var touch = new GameObject("Head touch proxy");
            touch.transform.SetParent(head != null ? head : transform, false);
            TouchProxy = touch.AddComponent<SphereCollider>();
            TouchProxy.isTrigger = true;
            // World-sized proxies; head's parent may include model scale.
            TouchProxy.radius = HeightMeters * 0.18f / touch.transform.lossyScale.x;
            touch.transform.position = head != null ? head.position + Vector3.up * HeightMeters * 0.07f : transform.position + Vector3.up * HeightMeters * 0.85f;
            var grab = new GameObject("Body grab proxy");
            grab.transform.SetParent(transform, false);
            GrabProxy = grab.AddComponent<CapsuleCollider>();
            GrabProxy.isTrigger = true;
            GrabProxy.center = Vector3.up * HeightMeters * 0.45f;
            GrabProxy.height = HeightMeters * 0.65f;
            GrabProxy.radius = HeightMeters * 0.15f;
            gaze = new GameObject("Bounded gaze target").transform;
            gaze.SetParent(transform, false);
            Instance.LookAtTarget = gaze;
            Instance.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.SpecifiedTransform;
            happyBase = Instance.Runtime.Expression.GetWeight(ExpressionKey.Happy);
            blinkBase = Instance.Runtime.Expression.GetWeight(ExpressionKey.Blink);
            foreach(var key in Instance.Runtime.Expression.ExpressionKeys)if(key.Equals(MouthSmile)){hasMouthSmile=true;smileBase=Instance.Runtime.Expression.GetWeight(key);break;}
            foreach(var key in Instance.Runtime.Expression.ExpressionKeys){if(key.Equals(ExpressionKey.CreateCustom("eyeSqueeze"))||key.Equals(ExpressionKey.CreateCustom("eyeChevron"))||key.Equals(ExpressionKey.CreateCustom("><"))){authoredSqueeze=key;hasAuthoredSqueeze=true;}hasClosure|=key.Equals(SqueezeClosure);hasBlush|=key.Equals(Blush);hasSqueezedEyes|=key.Equals(SqueezedEyes);}
            faceKeys.UnionWith(Instance.Runtime.Expression.ExpressionKeys);
            UsesMouth24Face=Presentation?.interactionFace==true&&faceKeys.Contains(ShockEye)&&faceKeys.Contains(MouthCircle)&&faceKeys.Contains(EyeSmile)&&hasSqueezedEyes;
            spiralAvailable=UsesMouth24Face&&faceKeys.Contains(SpiralEye);
            if(Presentation!=null)
            {
                foreach(var binding in Presentation.expressions??Array.Empty<ExpressionBinding>())
                    if(!faceKeys.Contains(ExpressionKey.CreateCustom(binding.key)))Debug.LogWarning("表现配置缺键："+Presentation.profileId+" / "+binding.semantic+" -> "+binding.key);
            }
            nextBlink = Time.time + 2.5f;
            originalCenters = Instance.SpringBone.Springs.ConvertAll(s => s.Center).ToArray();
            // 默认通用接线；true 仅用于保留未修正的作者基线对照，不按角色选择算法。
            Instance.Runtime.SpringBone.ReconstructSpringBone();
            if (!UsesAuthorPhysics)
            {
                Physics = gameObject.AddComponent<PortableSpringPhysics>();
                Physics.Initialize(Instance,Package.springPhysics);
            }
            ResetSecondaryMotion();
        }

        private float MeasureSupportRadius()
        {
            float radius = 0;
            int count = 0;
            foreach (var renderer in Instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                renderer.BakeMesh(mesh, true);
                var weights = renderer.sharedMesh.boneWeights;
                var bones = renderer.bones;
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (i >= weights.Length) continue;
                    var weight = weights[i];
                    float footWeight = SupportWeight(bones, weight.boneIndex0, weight.weight0) +
                        SupportWeight(bones, weight.boneIndex1, weight.weight1) +
                        SupportWeight(bones, weight.boneIndex2, weight.weight2) +
                        SupportWeight(bones, weight.boneIndex3, weight.weight3);
                    if (footWeight < .5f) continue;
                    var point = transform.InverseTransformPoint(renderer.transform.TransformPoint(vertices[i]));
                    if (point.y > HeightMeters * .15f) continue;
                    radius = Mathf.Max(radius, new Vector2(point.x, point.z).magnitude);
                    count++;
                }
                Destroy(mesh);
            }
            if (count == 0) throw new InvalidDataException("No foot support geometry for placement calibration.");
            return Mathf.Max(HeightMeters * .06f, radius) + .005f;
        }

        private float SupportWeight(Transform[] bones, int index, float weight)
        {
            if (weight <= 0 || index < 0 || index >= bones.Length) return 0;
            foreach (var name in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg })
                if (Instance.TryGetBoneTransform(name, out var bone) && bone == bones[index]) return weight;
            return 0;
        }

        private static Bounds MeasureGeometry(GameObject model, Transform spatialRoot)
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                // Unity 6: compensate renderer scale before applying its Transform once below.
                renderer.BakeMesh(mesh, true);
                foreach (var vertex in mesh.vertices)
                {
                    var point = spatialRoot.InverseTransformPoint(renderer.transform.TransformPoint(vertex));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
                Destroy(mesh);
            }
            if (first) throw new InvalidDataException("No skinned model geometry.");
            return bounds;
        }

        private void PoseArm(HumanBodyBones upperBone, HumanBodyBones lowerBone, float side)
        {
            if (!Instance.TryGetBoneTransform(upperBone, out var upper) || !Instance.TryGetBoneTransform(lowerBone, out var lower)) return;
            var direction = (lower.position - upper.position).normalized;
            var relaxed = new Vector3(side * 0.25f, -1, 0.03f).normalized;
            upper.rotation = Quaternion.FromToRotation(direction, relaxed) * upper.rotation;
        }

        public PetTouchSession TouchSession {get;}=new PetTouchSession();
        public PetTouchSound TouchSound {get;private set;}
        ExpressionKey Blush=ExpressionKey.CreateCustom("blush"), SqueezedEyes=ExpressionKey.CreateCustom("eyeTroubled");
                static readonly Vector2[] IdleLooks={new Vector2(0,0),new Vector2(.16f,.05f),new Vector2(-.2f,-.04f),new Vector2(.07f,.09f),new Vector2(-.1f,-.08f),new Vector2(.24f,.02f),new Vector2(-.05f,.07f),new Vector2(.12f,-.06f)};
        static Vector2 PickIdleLook(int segment)
        {
            double hash=Math.Sin(segment*127.1+311.7)*43758.5453;
            return IdleLooks[(int)((hash-Math.Floor(hash))*IdleLooks.Length)];
        }
        static Vector2 IdleLook(float time)
        {
            float segment=time/5.2f;int index=Mathf.FloorToInt(segment);
            return Vector2.Lerp(PickIdleLook(Mathf.Max(0,index-1)),PickIdleLook(index),Mathf.SmoothStep(0,1,(segment-index)/.48f))*Mathf.Rad2Deg;
        }
        ExpressionKey SqueezeClosure=ExpressionKey.CreateCustom("troubled");
        ExpressionKey authoredSqueeze;bool hasAuthoredSqueeze;
        bool hasClosure,hasBlush,hasSqueezedEyes,idleAllowed,allowBlink=true;float previousBlink,holdLidsUntil=-1,pressEyes,idleAmount;
        public int ForeheadTapCount {get;private set;}
        float tapAge=1,lastTap=-10;
        public bool TryForeheadTap()
        {
            if(MotionPreviewMode||Instance==null||suspended||Motions.Busy||pressTarget>.015f||Time.unscaledTime-lastTap<.55f)return false;
            tapAge=0;lastTap=Time.unscaledTime;ForeheadTapCount++;
            chat?.ShortVoice?.Play("protest",GetInstanceID()+"-tap-"+(++voiceReactionSerial),5);
            return true;
        }
        float touchExpression,pressTarget,jelly,jellyVelocity;
        Vector3 visualScale,visualPosition,headScale;
        bool jellyApplied, petVoiceAttempted;
        public float TouchCompression=>jelly;
        public bool PointerContactHeld {get;set;}
        public void CancelTouch()
        {
            TouchSession.End(Time.unscaledTime,speech!=null&&speech.IsPlaying,true);
            tapAge=1;holdLidsUntil=-1;pressEyes=0;PointerContactHeld=false;TouchSound?.Cancel();touching=false;touchSample=false;touchSway=Vector3.zero;
            pressTarget=jelly=jellyVelocity=touchExpression=0;RestoreJelly();
        }
        void RestoreJelly()
        {
            if(!jellyApplied)return;
            if(Instance!=null){Instance.transform.localScale=visualScale;Instance.transform.localPosition=visualPosition;}
            if(head!=null)head.localScale=headScale;jellyApplied=false;
        }
        void ApplyJelly(float dt)
        {
            RestoreJelly();
            // 有界阻尼弹簧仅驱动视觉压缩，不改变身高设置和空间根。
            for(float remaining=dt;remaining>0;)
            {float h=Mathf.Min(remaining,1f/120);remaining-=h;jellyVelocity+=(170*(pressTarget-jelly)-15*jellyVelocity)*h;jelly=Mathf.Clamp(jelly+jellyVelocity*h,-.018f,.065f);}
            if(Instance==null||Mathf.Abs(jelly)<.00001f)return;
            visualScale=Instance.transform.localScale;visualPosition=Instance.transform.localPosition;
            float vertical=1-jelly,lateral=1+jelly*.35f;
            Instance.transform.localScale=Vector3.Scale(visualScale,new Vector3(lateral,vertical,lateral));
            Instance.transform.localPosition=Vector3.Scale(visualPosition,new Vector3(lateral,vertical,lateral));
            if(head!=null){headScale=head.localScale;head.localScale=Vector3.Scale(headScale,new Vector3(1+jelly*.15f,1-jelly*.35f,1+jelly*.15f));}
            jellyApplied=true;
        }
        Vector3 touchPoint,touchSway;
        bool touchSample;
        float touchAt;
        // 复用 LingChat touch-sway 的有限角度和释放尾迹；世界距离按角色身高归一化。
        public void SampleTouch(Vector3 worldPoint)
        {
            if(!float.IsFinite(worldPoint.x)||!float.IsFinite(worldPoint.y)||!float.IsFinite(worldPoint.z))return;
            if(touchSample&&Time.unscaledTime-touchAt<.15f)
            {
                var delta=transform.InverseTransformDirection(worldPoint-touchPoint)/Mathf.Max(.12f,HeightMeters);
                if(delta.magnitude<.5f)
                {
                    float lateral=new Vector2(delta.x,delta.z).magnitude;
                    float push=delta.y<0?Mathf.Max(0,-delta.y-lateral): -delta.y;
                    pressTarget=Mathf.Clamp(pressTarget+push*.65f,0,.06f);
                }
                touchSway=Vector3.ClampMagnitude(touchSway+new Vector3(-delta.z,0,-delta.x)*24,2);
            }
            TouchSession.Move(worldPoint,HeightMeters);
            touchPoint=worldPoint;touchAt=Time.unscaledTime;touchSample=true;
        }
        public void SetFeedback(bool isTouching, bool isSuspended, bool cancelTouch=false)
        {
            if(cancelTouch)CancelTouch();
            if(isTouching){if(!TouchSession.Active)petVoiceAttempted=false;TouchSession.Begin(Time.unscaledTime,speech!=null&&speech.IsPlaying);TouchSession.Contact(true,Time.unscaledTime,speech!=null&&speech.IsPlaying);}
            if(!isTouching&&TouchSession.Active){if(PointerContactHeld&&!isSuspended)TouchSession.Contact(false,Time.unscaledTime,speech!=null&&speech.IsPlaying);else TouchSession.End(Time.unscaledTime,speech!=null&&speech.IsPlaying,isSuspended);}
            if(touching&&!isTouching&&!cancelTouch&&!isSuspended)holdLidsUntil=Time.unscaledTime+.45f;
            touching = isTouching;
            if(!isTouching)pressTarget=0;
            if(!isTouching||isSuspended)touchSample=false;
            if(isSuspended)touchSway=Vector3.zero;
            suspended = isSuspended;
            Physics?.SetPaused(isSuspended);
        }
        public void ResetSecondaryMotion()
        {
            if (Physics != null) Physics.RequestReset();
            else if (Instance != null) Instance.Runtime.SpringBone.RestoreInitialTransform();
        }
        public void SetWorldSpaceHair(bool enabled)
        {
            if (Physics != null) throw new InvalidOperationException("通用物理保留作者 center；此入口仅供原始基线诊断。");
            if (originalCenters == null) return;
            for (int i = 0; i < originalCenters.Length; i++) Instance.SpringBone.Springs[i].Center = enabled ? null : originalCenters[i];
            Instance.Runtime.SpringBone.ReconstructSpringBone(); ResetSecondaryMotion();
        }
        public void SetLandingFeedback(float amount) => settleAmount = amount;
        public void SetSpatialFeedback(Core.PetInteraction state) => SetSpatialFeedback(state, Time.deltaTime);
        public void SetSpatialFeedback(Core.PetInteraction state, float deltaTime)
        {
            if(state.State!=Core.PetState.Touched&&state.State!=Core.PetState.Idle&&(TouchSession.Active||touchExpression>0||jellyApplied||(TouchSound!=null&&TouchSound.Playing)))CancelTouch();
            bool nowHeld = state.State==Core.PetState.Held;
            chat?.ShortVoice?.SetHeld(nowHeld);
            if(nowHeld && !heldReaction && !MotionPreviewMode)
            {
                chat?.InterruptCharacterVoice();
                chat?.ShortVoice?.Play("curious",GetInstanceID()+"-grab-"+(++voiceReactionSerial),10);
            }
            Motions?.SetGrabbed(state.State==Core.PetState.Held,state.State==Core.PetState.Suspended||state.State==Core.PetState.Unplaced);
            float now=Time.unscaledTime;
            if(state.State==Core.PetState.Descending && heldReaction)
            {
                fallingReaction=true;
                fallingDizzy=now<dizzyUntil;
                fallingScrunch=dizzyUntil>0 && !fallingDizzy && now<dizzyUntil+ScrunchSeconds;
            }
            if(state.State!=Core.PetState.Descending)
            {
                if(fallingReaction && state.State==Core.PetState.Settling)
                    landingScrunchUntil=now+ScrunchSeconds;
                fallingReaction=false;
            }
            if(!heldReaction&&state.State==Core.PetState.Held)surprisedMouthUntil=now+.38f;
            heldReaction=state.State==Core.PetState.Held;
            if(heldReaction||state.State==Core.PetState.Suspended||state.State==Core.PetState.Unplaced)
                landingScrunchUntil=0;
            if(!heldReaction)dizzyUntil=0;
            idleAllowed=state.State==Core.PetState.Idle;
            settleAmount=state.SettleAmount;impact=Mathf.Clamp01(state.LandingSpeed/4.5f);
            fallTarget=state.State==Core.PetState.Descending?Mathf.Clamp01(-state.VerticalVelocity/3):0;
            Physics?.SetPaused(state.State == Core.PetState.Suspended || state.State == Core.PetState.Unplaced);
            Physics?.SetSpatialMotion(state,deltaTime);
        }

        public void Present(Vector3 viewerPosition)
        {
            if (Instance == null || gaze == null || FreezePresentation) return;
            RestoreJelly();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            TouchSession.Tick(Time.unscaledTime,speech!=null&&speech.IsPlaying);
            if(!MotionPreviewMode && TouchSession.Active && TouchSession.Duration>=.45f && !petVoiceAttempted)
            {
                petVoiceAttempted=true;
                chat?.ShortVoice?.Play("happy",GetInstanceID()+"-pet-"+(++voiceReactionSerial),0);
            }
            TouchSound?.Tick(touching&&!suspended,Time.unscaledTime,chat?.Effects?.Volume??.8f,(chat?.Effects?.Enabled??true)&&!(chat?.VoiceInputActive??false),pressTarget>(squeezePose?.032f:.046f));
            bool holdFace=!suspended&&(touching||Time.unscaledTime<holdLidsUntil);
            touchExpression=Mathf.Lerp(touchExpression,holdFace?1:0,1-Mathf.Exp(-dt/(holdFace?.1f:.32f)));
            if(!holdFace&&touchExpression<.001f)touchExpression=0;
            float squeeze=(hasAuthoredSqueeze||hasSqueezedEyes)&&touching?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.035f,.055f,pressTarget)):0;
            pressEyes=Mathf.Lerp(pressEyes,squeeze,1-Mathf.Exp(-dt/.075f));
            ComposeFace(dt,holdFace);
            UpdateSticker(viewerPosition);
            if(!MotionPreviewMode)Motions.UpdateConversation(speech!=null&&speech.IsPlaying,chat!=null&&chat.Thinking,
                speech?.Generation??0,speech?.PlaybackSeconds??0,speech?.Rhythm,chat?.Emotion??"");
            Motions.Step(Time.unscaledDeltaTime);
            touchSway*=Mathf.Exp(-dt/.13f);
            if(!MotionPreviewMode&&!suspended&&!Motions.HeadOccupied&&presentationHead!=null)
                { presentationHead.localRotation*=Quaternion.Euler(touchSway*(presentationNeck==null?.85f:.65f));
                if(presentationNeck!=null)presentationNeck.localRotation*=Quaternion.Euler(touchSway*.20f);
                if(presentationChest!=null)presentationChest.localRotation*=Quaternion.Euler(touchSway*.15f); }
            if(tapAge<.42f)
            {
                tapAge+=dt;
                float recoil=Mathf.Sin(Mathf.PI*Mathf.Clamp01(tapAge/.42f))*Mathf.Exp(-tapAge*5);
                if(presentationChest!=null)presentationChest.localRotation*=Quaternion.Euler(-15*recoil,0,0);
                if(presentationHead!=null)presentationHead.localRotation*=Quaternion.Euler(-8*recoil,0,0);
            }
            if (Presentation?.secondaryBodyFeedback==true)
            {
                // 脚底保持贴地，仅胸部轻微前倾后收稳；口型订阅现有人声，不另建播放源。
                fallPose=Mathf.MoveTowards(fallPose,fallTarget,dt*6);
                if (presentationChest != null) presentationChest.localRotation *= Quaternion.Euler(10f * impact * settleAmount - 3f*fallPose, 0, 0);
                if(presentationLeftArm!=null)presentationLeftArm.localRotation*=Quaternion.Euler(0,0,-18f*fallPose);
                if(presentationRightArm!=null)presentationRightArm.localRotation*=Quaternion.Euler(0,0,18f*fallPose);
            }
            ApplyJelly(dt);
            if (head == null) return;
            Vector3 focus = AttentionPoint(viewerPosition,holdFace);
            Vector3 local = transform.InverseTransformDirection(focus - head.position);
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -85, 85);
            float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -30, 30);
            // 行走/转身拥有身体时看向自身前方；结束后沿用用户的视线偏好，不改设置。
            if (suspended || !GazeEnabled || Motions.LocomotionOwned) { yaw = 0; pitch = 0; }
            bool quiet=idleAllowed&&!MotionPreviewMode&&!Motions.Busy&&!holdFace&&!(speech!=null&&speech.IsPlaying);
            idleAmount=Mathf.Lerp(idleAmount,quiet?1:0,1-Mathf.Exp(-dt/.35f));
            float t=Time.unscaledTime;
            if(quiet&&presentationChest!=null)presentationChest.localRotation*=Quaternion.Euler(Mathf.Sin(t*1.15f)*1.03f*idleAmount,0,Mathf.Sin(t*.17f+.6f)*.63f*idleAmount);
            if(quiet&&GazeEnabled&&!FollowingHand){var look=IdleLook(t)*idleAmount;yaw+=look.x;pitch+=look.y;}
            if(PreviewEmotion=="害羞"){yaw+=12;pitch+=8;}
            if(holdFace){yaw=0;pitch=23;}
            else if(FollowingHand)
                pitch=Mathf.Clamp(-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg,-55,38);
            if(holdFace||FollowingHand)smoothEyesUntil=Time.unscaledTime+.5f;
            var targetAim=Quaternion.Euler(pitch,yaw,0);
            eyeAim=Time.unscaledTime<smoothEyesUntil?Quaternion.Slerp(eyeAim,targetAim,1-Mathf.Exp(-dt*9)):targetAim;
            gazeOffset=Quaternion.Slerp(gazeOffset,Quaternion.Euler(pitch*.6f,yaw*.75f,0),1-Mathf.Exp(-6f*dt));
            if(presentationHead!=null&&!Motions.HeadOccupied)presentationHead.localRotation*=gazeOffset;
            gaze.position = head.position + transform.TransformDirection(eyeAim * Vector3.forward);
        }
        void UpdateSticker(Vector3 viewer)
        {
            if(sticker==null){
                var obj=new GameObject("心情贴纸");obj.transform.SetParent(transform,false);
                sticker=obj.AddComponent<MoodSticker>();
            }
            string mood=heldReaction?"惊讶":PreviewEmotion??(chat!=null&&!chat.Thinking?chat.Emotion:"");
            sticker.Present(mood,head!=null?head.position:transform.position,viewer,HeightMeters);
        }
        void ComposeFace(float dt,bool holdFace)
        {
            bool speaking=speech!=null&&speech.IsPlaying;
            string emotion=PreviewEmotion??(chat!=null&&!chat.Thinking?chat.Emotion:"");
            bool dizzy=heldReaction && Time.unscaledTime<dizzyUntil || fallingReaction && fallingDizzy;
            bool scrunch=Time.unscaledTime<landingScrunchUntil || fallingReaction && fallingScrunch ||
                heldReaction && dizzyUntil>0 && !dizzy && Time.unscaledTime<dizzyUntil+ScrunchSeconds;
            bool carryFace=heldReaction||fallingReaction;
            bool mouthLocked=UsesMouth24Face&&(tapAge<.42f||carryFace&&(!speaking||Time.unscaledTime<surprisedMouthUntil)||holdFace&&squeezePose);
            EyeOwner=carryFace||scrunch||holdFace?"互动":"心情 / 眨眼";
            MouthOwner=mouthLocked?"互动":speaking?"角色语音":"当前心情";
            bool authoredTouch=UsesMouth24Face&&(holdFace||carryFace||scrunch||tapAge<.42f);
            if(UsesMouth24Face)
            {
                if(touching)squeezePose=pressTarget>(squeezePose?.032f:.046f);
                if(!holdFace)squeezePose=false;
                bool shocked=carryFace||tapAge<.42f;
                float pet=holdFace&&!squeezePose&&!shocked&&!scrunch?1:0;
                float press=holdFace&&squeezePose&&!shocked&&!scrunch?1:0;
                var face=Instance.Runtime.Expression;
                face.SetWeight(EyeSmile,pet);face.SetWeight(BrowHappy,pet);
                face.SetWeight(MouthSmile,authoredTouch?pet*.5f:smileBase);face.SetWeight(CheekSlashes,0);
                if(hasBlush)face.SetWeight(Blush,!authoredTouch&&(emotion=="害羞"||emotion=="心动")?.4f:0);
                if(!authoredTouch&&emotion=="心动"){face.SetWeight(BrowHappy,.5f);face.SetWeight(MouthSmile,.35f);}
                face.SetWeight(SqueezedEyes,scrunch?1:press);face.SetWeight(MouthCircle,shocked&&mouthLocked?1:press);
                face.SetWeight(ShockEye,carryFace&&!dizzy&&!scrunch?1:0);face.SetWeight(SpiralEye,dizzy?1:0);
            }
            else
            {
            if(hasBlush)Instance.Runtime.Expression.SetWeight(Blush,.18f*touchExpression);
            if(hasAuthoredSqueeze)Instance.Runtime.Expression.SetWeight(authoredSqueeze,pressEyes);
            else if(hasSqueezedEyes)Instance.Runtime.Expression.SetWeight(SqueezedEyes,pressEyes);
            if(hasClosure&&!hasAuthoredSqueeze)Instance.Runtime.Expression.SetWeight(SqueezeClosure,pressEyes);
            }

            bool happy=emotion=="开心"||emotion=="喜悦"||emotion=="愉快"||emotion=="害羞";
            happyWeight = Mathf.MoveTowards(happyWeight, Mathf.Lerp(happy ? .55f : happyBase,hasMouthSmile?0:.28f,touchExpression), dt * 4f);
            Instance.Runtime.Expression.SetWeight(ExpressionKey.Happy, authoredTouch?0:happyWeight);
            if(hasMouthSmile&&!UsesMouth24Face)Instance.Runtime.Expression.SetWeight(MouthSmile,Mathf.Lerp(smileBase,.7f,touchExpression));
            if(faceKeys.Contains(ExpressionKey.Sad))
            {
                Blend(ExpressionKey.Sad,touchExpression>.01f?0:emotion=="悲伤"||emotion=="伤心"?.55f:0,dt);
                Blend(ExpressionKey.Angry,touchExpression>.01f?0:emotion=="生气"||emotion=="愤怒"?.45f:0,dt);
                Blend(ExpressionKey.Surprised,touchExpression>.01f?0:emotion=="惊讶"||emotion=="震惊"?.55f:0,dt);
                Blend(ExpressionKey.Relaxed,touchExpression>.01f?0:emotion=="安心"||emotion=="放松"?.4f:0,dt);
            }
            if(authoredTouch){nextBlink=Time.time+2.5f;blinkStart=-100;}
            if (Time.time >= nextBlink) { blinkStart = Time.time; nextBlink = Time.time + 3.4f; }
            float blink = Mathf.Max(0, 1 - Mathf.Abs((Time.time - blinkStart) / 0.09f - 1));
            if(blink>0&&previousBlink<=0)allowBlink=!holdFace&&touchExpression==0;
            previousBlink=blink;
            Instance.Runtime.Expression.SetWeight(ExpressionKey.Blink,UsesMouth24Face?(authoredTouch?0:Mathf.Max(blinkBase,blink)):Mathf.Max(blinkBase,Mathf.Max(allowBlink?blink:0,touchExpression)*(1-pressEyes)));
            // 标准 VRM 口型与动作共存，继续复用唯一语音源，不按角色名字接分支。
            float target = !mouthLocked && speech != null && speech.IsPlaying ? Mathf.Clamp01(speech.SampleAmplitude(voiceSamples) * 8) : 0;
            mouth = mouthLocked?0:Mathf.MoveTowards(mouth, target, dt * 8);
            Instance.Runtime.Expression.SetWeight(ExpressionKey.Aa, mouth * .75f);
        }
        private void Blend(ExpressionKey key,float target,float dt) => Instance.Runtime.Expression.SetWeight(key,Mathf.MoveTowards(Instance.Runtime.Expression.GetWeight(key),target,dt*3));

        void OnDisable()=>CancelTouch();
        void OnApplicationFocus(bool focused){if(!focused)CancelTouch();}
        void OnApplicationPause(bool paused){if(paused)CancelTouch();}
        private void OnDestroy()
        {
            CancelTouch();
            Motions?.Dispose();
            if (Instance != null) Destroy(Instance.gameObject);
            previewResources?.Dispose();sharedResources?.Dispose();
        }
    }
}
