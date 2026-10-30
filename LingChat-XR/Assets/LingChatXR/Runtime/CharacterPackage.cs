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
