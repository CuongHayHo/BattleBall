using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace BattleBall.Net
{
    [Serializable]
    public class BbAssetPack
    {
        public string id;
        public int scale;        // 0 = needed at every scale, otherwise 1, 2, 3 or 4
        public string url;       // relative to the asset base URL
        public long size;
        public string sha256;
    }

    [Serializable]
    public class BbAssetManifest
    {
        public string version;
        public BbAssetPack[] packs;
    }

    /// <summary>What is installed in the cache folder (installed.json).</summary>
    [Serializable]
    class BbInstalledIndex
    {
        public string version = "";
        public int scale;
        public List<string> ids = new List<string>();
        public List<string> hashes = new List<string>();

        public string HashOf(string id)
        {
            int i = ids.IndexOf(id);
            return i >= 0 ? hashes[i] : null;
        }

        public void Set(string id, string hash)
        {
            int i = ids.IndexOf(id);
            if (i >= 0) hashes[i] = hash; else { ids.Add(id); hashes.Add(hash); }
        }
    }

    public static class BbAssetCache
    {
        const string KeyScale = "bb.assetScale";             // the scale picked automatically on the first run (2 to 4)
        const string KeyScaleUser = "bb.assetScaleUser";     // the scale the player chose in the settings (1 to 4); it wins over BbConfig and the automatic choice

        /// <summary>Saves the scale the player chose in the settings (1 to 4).</summary>
        public static void SetUserScale(int scale)
        {
            PlayerPrefs.SetInt(KeyScaleUser, Mathf.Clamp(scale, 1, 4));
            PlayerPrefs.Save();
        }

        public static string DefaultRoot { get { return Path.Combine(Application.persistentDataPath, "bbassets"); } }

        /// <summary>The cache folder when it holds an installed asset set, otherwise null. Its layout is that of SourceData (data/res, data/icon, data/update_data).</summary>
        public static string InstalledRoot
        {
            get { return File.Exists(Path.Combine(DefaultRoot, "installed.json")) ? DefaultRoot : null; }
        }

        /// <summary>
        /// The sprite scale in use (x1 to x4): the player's choice in the settings, else the one set in BbConfig, else the one decided on the first run by
        /// resolution and RAM (x2 to x4).
        /// </summary>
        public static int Scale
        {
            get
            {
                int user = PlayerPrefs.GetInt(KeyScaleUser, 0);
                if (user >= 1 && user <= 4) return user;
                int forced = BbConfig.Current.assetScale;
                if (forced >= 1 && forced <= 4) return forced;      // set in BbConfig
                int saved = PlayerPrefs.GetInt(KeyScale, 0);
                if (saved >= 2 && saved <= 4) return saved;
                saved = BbAssetUpdater.ChooseScale(Mathf.Min(Screen.width, Screen.height), SystemInfo.systemMemorySize);
                PlayerPrefs.SetInt(KeyScale, saved);
                PlayerPrefs.Save();
                return saved;
            }
        }
    }

    public sealed class BbAssetResult
    {
        public string version;
        public int scale;
        public int packsDownloaded;
        public long bytesDownloaded;
        public bool wasUpToDate;      // the installed version already matched: nothing was contacted
    }

    /// <summary>
    /// Downloads the game assets announced by the server (HelloAck.asset_base_url and asset_version): reads manifest.json, downloads the packs of this
    /// device's scale that are missing or changed, checks each pack's SHA-256, unzips it into the cache folder and records it in installed.json.
    /// A pack is one zip; an interrupted run keeps what was already installed and continues with the rest next time.
    /// Unity main thread only (UnityWebRequest); await it from a MonoBehaviour or from an editor update loop.
    /// </summary>
    public sealed class BbAssetUpdater
    {
        readonly string _root;
        public string Root { get { return _root; } }

        public BbAssetUpdater(string cacheRoot) { _root = cacheRoot; }

        /// <summary>x4 for big and strong devices, x3 for medium, x2 otherwise (x1 data is incomplete and is not published).</summary>
        public static int ChooseScale(int minScreenSide, int ramMb)
        {
            if (minScreenSide >= 1080 && ramMb >= 3500) return 4;
            if (minScreenSide >= 720 && ramMb >= 2500) return 3;
            return 2;
        }

        /// <param name="progress">(bytes done, bytes to download in total) over the packs that have to be downloaded.</param>
        public async Task<BbAssetResult> UpdateAsync(string baseUrl, string serverVersion, int scale, Action<long, long> progress, CancellationToken ct)
        {
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            Directory.CreateDirectory(_root);
            var index = LoadIndex();
            var result = new BbAssetResult { version = serverVersion, scale = scale };

            if (!string.IsNullOrEmpty(serverVersion) && index.version == serverVersion && index.scale == scale)
            {
                result.wasUpToDate = true;
                return result;
            }

            string manifestText = await GetText(baseUrl + "manifest.json", ct);
            var manifest = JsonUtility.FromJson<BbAssetManifest>(manifestText);
            if (manifest == null || manifest.packs == null) throw new InvalidDataException("the asset manifest is not valid");

            var todo = new List<BbAssetPack>();
            long total = 0;
            foreach (var p in manifest.packs)
            {
                if (p.scale != 0 && p.scale != scale) continue;
                if (index.HashOf(p.id) == p.sha256) continue;
                todo.Add(p);
                total += p.size;
            }
            progress?.Invoke(0, total);

            long done = 0;
            string tmpDir = Path.Combine(_root, "tmp");
            Directory.CreateDirectory(tmpDir);
            foreach (var p in todo)
            {
                ct.ThrowIfCancellationRequested();
                string zipPath = Path.Combine(tmpDir, SafeName(p.id) + ".zip");
                long before = done;
                await Download(baseUrl + p.url, zipPath, bytes => progress?.Invoke(before + (long)bytes, total), ct);
                done = before + p.size;
                string actual = await Task.Run(() => Sha256Of(zipPath));
                if (!string.Equals(actual, p.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(zipPath);
                    throw new InvalidDataException("pack " + p.id + " is damaged (hash differs)");
                }
                await Task.Run(() => Extract(zipPath, _root));
                File.Delete(zipPath);
                index.Set(p.id, p.sha256);
                SaveIndex(index);                       // progress survives an interruption
                result.packsDownloaded++;
                result.bytesDownloaded += p.size;
                progress?.Invoke(done, total);
            }

            index.version = manifest.version;
            index.scale = scale;
            SaveIndex(index);
            result.version = manifest.version;
            return result;
        }

        // ---------------------------------------------------------------------------------------------------------------- files

        BbInstalledIndex LoadIndex()
        {
            try
            {
                string f = Path.Combine(_root, "installed.json");
                if (File.Exists(f)) return JsonUtility.FromJson<BbInstalledIndex>(File.ReadAllText(f, Encoding.UTF8)) ?? new BbInstalledIndex();
            }
            catch (Exception) { /* a damaged index means "nothing installed": everything is downloaded again */ }
            return new BbInstalledIndex();
        }

        void SaveIndex(BbInstalledIndex index)
        {
            string f = Path.Combine(_root, "installed.json");
            string tmp = f + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(index), Encoding.UTF8);
            if (File.Exists(f)) File.Delete(f);
            File.Move(tmp, f);
        }

        static string SafeName(string id)
        {
            var sb = new StringBuilder();
            foreach (char c in id) sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            return sb.ToString();
        }

        static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                var h = sha.ComputeHash(fs);
                var sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>The target path of a zip entry inside <paramref name="root"/>, or null when the name would escape it (zip-slip) or is not a plain relative path.</summary>
        public static string SafeEntryPath(string root, string entryName)
        {
            if (string.IsNullOrEmpty(entryName) || entryName.Contains("\\") || entryName.StartsWith("/") || entryName.Contains(":")) return null;
            foreach (string part in entryName.Split('/'))
                if (part == ".." || part == ".") return null;
            string full = Path.GetFullPath(Path.Combine(root, entryName));
            string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(rootFull, StringComparison.Ordinal) ? full : null;
        }

        static void Extract(string zipPath, string root)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith("/")) continue;      // folder entry
                    string target = SafeEntryPath(root, entry.FullName);
                    if (target == null) throw new InvalidDataException("unsafe name in the pack: " + entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- web

        static async Task<string> GetText(string url, CancellationToken ct)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 20;
                await Send(req, null, ct);
                return req.downloadHandler.text;
            }
        }

        static async Task Download(string url, string path, Action<ulong> onBytes, CancellationToken ct)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                req.timeout = 60;
                req.downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true };
                await Send(req, onBytes, ct);
            }
        }

        static async Task Send(UnityWebRequest req, Action<ulong> onBytes, CancellationToken ct)
        {
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                if (ct.IsCancellationRequested) { req.Abort(); throw new OperationCanceledException(ct); }
                onBytes?.Invoke(req.downloadedBytes);
                await Task.Yield();
            }
            if (req.result != UnityWebRequest.Result.Success) throw new IOException(req.error + " (" + req.url + ")");
        }
    }
}
