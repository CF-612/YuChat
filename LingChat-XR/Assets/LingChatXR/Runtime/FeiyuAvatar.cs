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
        public float HeightMeters { get; private set; }
        public float FootprintRadius { get; private set; }
        public float MeasuredHeightMeters { get; private set; }
        public SphereCollider TouchProxy { get; private set; }
        public CapsuleCollider GrabProxy { get; private set; }
        private Transform head;
        private Transform gaze;
        private Quaternion headRest;
        private float happyWeight;
        private float happyBase;
        private float blinkBase;
        private float nextBlink;
        private float blinkStart = -100;
        private bool touching;
        private bool suspended;

        public async Task LoadAsync(string folder, float heightMeters, CancellationToken token)
        {
            if (Instance != null) throw new InvalidOperationException("Replace an avatar by creating a new adapter instance.");
            var manifest = await PackageFiles.ReadAsync(folder, "manifest.json", token);
            Package = JsonUtility.FromJson<CharacterPackage>(Encoding.UTF8.GetString(manifest));
            if (Package == null || Package.schemaVersion != 1 || Package.model == null)
                throw new InvalidDataException("Unsupported character package.");
            var bytes = await PackageFiles.ReadAsync(folder, Package.model.path, token);
            if (!string.Equals(PackageFiles.Hash(bytes), Package.model.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Model SHA256 does not match manifest.");
            Instance = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: true,
                controlRigGenerationOption: ControlRigGenerationOption.None, ct: token);
            if (token.IsCancellationRequested || this == null)
            {
                if (Instance != null) Destroy(Instance.gameObject);
                token.ThrowIfCancellationRequested();
                return;
            }
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
            nextBlink = Time.time + 2.5f;
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

        public void SetFeedback(bool isTouching, bool isSuspended)
        {
            touching = isTouching;
            suspended = isSuspended;
        }

        public void Present(Vector3 viewerPosition)
        {
            if (Instance == null || gaze == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            happyWeight = Mathf.MoveTowards(happyWeight, touching && !suspended ? 0.65f : happyBase, dt * 4f);
            Instance.Runtime.Expression.SetWeight(ExpressionKey.Happy, happyWeight);
            if (Time.time >= nextBlink) { blinkStart = Time.time; nextBlink = Time.time + 3.4f; }
            float blink = Mathf.Max(0, 1 - Mathf.Abs((Time.time - blinkStart) / 0.09f - 1));
            Instance.Runtime.Expression.SetWeight(ExpressionKey.Blink, Mathf.Max(blinkBase, blink));
            if (head == null) return;
            Vector3 local = transform.InverseTransformDirection(viewerPosition - head.position);
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -35, 35);
            float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -18, 18);
            if (suspended) { yaw = 0; pitch = 0; }
            head.localRotation = Quaternion.Slerp(head.localRotation, headRest * Quaternion.Euler(pitch * 0.45f, yaw * 0.45f, 0), 1 - Mathf.Exp(-6f * dt));
            gaze.position = head.position + transform.TransformDirection(Quaternion.Euler(pitch, yaw, 0) * Vector3.forward);
        }

        private void OnDestroy()
        {
            if (Instance != null) Destroy(Instance.gameObject);
        }
    }
}
