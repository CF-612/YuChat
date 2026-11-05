using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace LingChatXR
{
    [Serializable] public sealed class CharacterPackage
    {
        public int schemaVersion;
        public string characterId;
        public string displayName;
        public ModelReference model;
        public InteractionBindings interactionBindings;
        public SpringPhysicsConfiguration springPhysics;
        public string presentationProfile;
    }

    [Serializable] public sealed class SpringPhysicsConfiguration
    {
        public SpringPhysicsMaterial[] materials;
        public SpringPhysicsBinding[] bindings;
        public SpringColliderAdjustment[] colliders;
    }
    [Serializable] public sealed class SpringColliderAdjustment
    {
        public int group, index;
        public string node;
        public string capsuleEndBone;
        public Vector3 offset;
        public float radius;
    }
    [Serializable] public sealed class SpringPhysicsBinding
    {
        public int spring;
        public string root, material;
        public int[] extraColliderGroups;
    }
    [Serializable] public sealed class SpringPhysicsMaterial
    {
        public string id;
        public float rootStiffness=1, tipStiffness=1, gravity=1, drag=.65f;
        public float rootAngle=90, tipAngle=90;
        public bool Valid => !string.IsNullOrEmpty(id) &&
            float.IsFinite(rootStiffness)&&rootStiffness>=0&&rootStiffness<=20 &&
            float.IsFinite(tipStiffness)&&tipStiffness>=0&&tipStiffness<=20 &&
            float.IsFinite(gravity)&&gravity>=0&&gravity<=5 &&
            float.IsFinite(drag)&&drag>=0&&drag<=1 &&
            float.IsFinite(rootAngle)&&rootAngle>0&&rootAngle<=180 &&
            float.IsFinite(tipAngle)&&tipAngle>0&&tipAngle<=180;
    }

    [Serializable] public sealed class InteractionBindings
    {
        public string tailGripPath;
    }

    [Serializable] public sealed class ModelReference
    {
        public string path;
        public string sha256;
        public string format;
        public bool isPlaceholder;
    }

    public static class PackageFiles
    {
        // UnityWebRequest also handles Android's jar:file StreamingAssets location.
        public static async Task<byte[]> ReadAsync(string folder, string relativePath, CancellationToken token)
        {
            if (string.IsNullOrEmpty(relativePath) || relativePath.Contains(":") ||
                relativePath.Contains("\\") || relativePath.StartsWith("/") ||
                Array.Exists(relativePath.Split('/'), part => part == ".." || part == "." || part == ""))
                throw new InvalidDataException("Package resource must be a safe relative path.");
            string path = folder.TrimEnd('/', '\\') + "/" + relativePath;
            string uri = path.Contains("://") || path.StartsWith("jar:") ? path : new Uri(path).AbsoluteUri;
            using var request = UnityWebRequest.Get(uri);
            var operation = request.SendWebRequest();
            using var registration = token.Register(request.Abort);
            while (!operation.isDone)
            {
                token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            token.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException("Package read failed: " + request.error);
            return request.downloadHandler.data;
        }

        public static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
