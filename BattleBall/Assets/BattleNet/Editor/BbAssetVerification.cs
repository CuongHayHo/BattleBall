using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BattleBall.Net;
using NroRig;
using UnityEditor;
using UnityEngine;

namespace BattleBall.EditorTools
{
    /// <summary>
    /// Checks the asset download against the local web server (start it with BattleBallServer/tools/run_web.ps1, after tools/build_assets.py):
    /// first download into an empty cache, "already up to date" without any request, an incremental update of one damaged pack, the zip-slip guard,
    /// and that NroSpriteResolver resolves every sprite the costumes need from the cache alone.
    /// Menu: BattleBall/Verify Asset Download. Batch (no -quit; it exits by itself):
    /// Unity -batchmode -nographics -projectPath ... -executeMethod BattleBall.EditorTools.BbAssetVerification.RunBatch [-bbAssetUrl http://127.0.0.1:8081/assets/]
    /// Writes Logs/asset_verification.txt and exits 0 (passed) or 1.
    /// </summary>
    public static class BbAssetVerification
    {
        static Task<string> _task;
        static DateTime _deadline;

        [MenuItem("BattleBall/Verify Asset Download")]
        public static void RunFromMenu()
        {
            RunAsync(DefaultUrl()).ContinueWith(t => Debug.Log(t.IsFaulted ? "Asset verification FAILED: " + t.Exception : "Asset verification passed:\n" + t.Result), TaskScheduler.FromCurrentSynchronizationContext());
        }

        public static void RunBatch()
        {
            string url = Arg("-bbAssetUrl", DefaultUrl());
            _deadline = DateTime.UtcNow.AddMinutes(8);
            _task = RunAsync(url);
            EditorApplication.update += Poll;           // the editor keeps ticking, so UnityWebRequest and the awaits below make progress
        }

        static void Poll()
        {
            if (!_task.IsCompleted && DateTime.UtcNow < _deadline) return;
            EditorApplication.update -= Poll;
            string text;
            int code = 0;
            if (!_task.IsCompleted) { text = "FAILED\ntimed out"; code = 1; }
            else if (_task.IsFaulted) { text = "FAILED\n" + _task.Exception.GetBaseException(); code = 1; }
            else text = "PASSED\n" + _task.Result;
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "asset_verification.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            Debug.Log(text);
            EditorApplication.Exit(code);
        }

        static string DefaultUrl() { return "http://127.0.0.1:8081/assets/"; }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return fallback;
        }

        static async Task<string> RunAsync(string baseUrl)
        {
            var log = new StringBuilder();
            void Ok(string what) { log.AppendLine("OK   " + what); }
            void Expect(bool cond, string what) { if (!cond) throw new Exception("FAILED: " + what); Ok(what); }

            string projectRoot = Directory.GetCurrentDirectory();
            string root = Path.Combine(projectRoot, "Logs", "asset_test_cache");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            string manifestFile = Path.GetFullPath(Path.Combine(projectRoot, "..", "BattleBallServer", "web", "assets", "manifest.json"));
            var manifest = JsonUtility.FromJson<BbAssetManifest>(File.ReadAllText(manifestFile));
            const int scale = 2;
            int expectedPacks = 0;
            long expectedBytes = 0;
            foreach (var p in manifest.packs) if (p.scale == 0 || p.scale == scale) { expectedPacks++; expectedBytes += p.size; }
            var ct = CancellationToken.None;

            // 1. empty cache: everything of scale x2 (and the common pack) is downloaded, verified and unzipped
            long lastDone = 0, lastTotal = 0;
            var updater = new BbAssetUpdater(root);
            var r1 = await updater.UpdateAsync(baseUrl, manifest.version, scale, (d, t) => { lastDone = d; lastTotal = t; }, ct);
            Expect(!r1.wasUpToDate && r1.packsDownloaded == expectedPacks && r1.bytesDownloaded == expectedBytes,
                "first download: " + r1.packsDownloaded + " packs, " + (r1.bytesDownloaded / 1048576f).ToString("F1") + " MB");
            Expect(lastTotal == expectedBytes && lastDone == expectedBytes, "progress reached 100% (" + lastDone + " of " + lastTotal + " bytes)");
            Expect(File.Exists(Path.Combine(root, "data", "res", "x2", "Big0")) && File.Exists(Path.Combine(root, "data", "update_data", "image")), "atlas and image table were unzipped");
            Expect(!Directory.Exists(Path.Combine(root, "data", "res", "x3")), "no pack of another scale was downloaded");

            // 2. same version again: nothing is contacted
            var r2 = await updater.UpdateAsync("http://127.0.0.1:1/unreachable/", manifest.version, scale, null, ct);
            Expect(r2.wasUpToDate && r2.packsDownloaded == 0, "same version: nothing downloaded and no request made");

            // 3. one damaged pack record: only that pack is downloaded again
            string indexFile = Path.Combine(root, "installed.json");
            string index = File.ReadAllText(indexFile);
            string victim = "x2_icons_003";
            string marker = "\"" + victim + "\"";
            Expect(index.Contains(marker), "installed.json lists " + victim);
            var idx = JsonUtility.FromJson<IndexView>(index);
            int at = idx.ids.IndexOf(victim);
            idx.hashes[at] = "0000";
            idx.version = "old";
            File.WriteAllText(indexFile, JsonUtility.ToJson(idx));
            var r3 = await updater.UpdateAsync(baseUrl, manifest.version, scale, null, ct);
            Expect(r3.packsDownloaded == 1 && !r3.wasUpToDate, "incremental update: only the damaged pack was downloaded again (" + r3.packsDownloaded + ")");

            // 4. a wrong server address fails cleanly
            bool failed = false;
            try { await new BbAssetUpdater(Path.Combine(root, "other")).UpdateAsync("http://127.0.0.1:1/none/", "v", scale, null, ct); }
            catch (Exception) { failed = true; }
            Expect(failed, "an unreachable server is reported as an error");

            // 5. zip-slip guard
            Expect(BbAssetUpdater.SafeEntryPath(root, "data/icon/x2/1.png") != null, "normal entry name accepted");
            Expect(BbAssetUpdater.SafeEntryPath(root, "../evil.txt") == null, "'..' entry refused");
            Expect(BbAssetUpdater.SafeEntryPath(root, "data/../../evil.txt") == null, "nested '..' entry refused");
            Expect(BbAssetUpdater.SafeEntryPath(root, "/etc/passwd") == null, "absolute entry refused");
            Expect(BbAssetUpdater.SafeEntryPath(root, "C:/Windows/x") == null, "drive entry refused");
            Expect(BbAssetUpdater.SafeEntryPath(root, "a\\b") == null, "backslash entry refused");

            // 6. the sprite resolver finds every icon the costumes need in the cache (the cache holds x2 only)
            var ids = new HashSet<int>();
            foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(projectRoot, "Assets", "NroMannequin", "Data", "PartDatabase.json")), "\"iconId\":\\s*(\\d+)")) ids.Add(int.Parse(m.Groups[1].Value));
            foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(projectRoot, "Assets", "NroMannequin", "Data", "CostumeCatalog.json")), "\"iconId\":\\s*(\\d+)")) ids.Add(int.Parse(m.Groups[1].Value));
            var go = new GameObject("verify-resolver");
            try
            {
                var resolver = go.AddComponent<NroSpriteResolver>();
                resolver.Initialize(root);
                int atlas = 0, loose = 0, dummy = 0;
                foreach (int id in ids)
                {
                    if (id <= 0) continue;
                    var sp = resolver.ResolveSprite(id, scale);
                    if (sp.name.StartsWith("Atlas_")) atlas++;
                    else if (sp.name.StartsWith("Loose_")) loose++;
                    else dummy++;
                }
                Expect(dummy == 0 && atlas > 0 && loose > 0, "resolver: " + atlas + " sprites from atlases, " + loose + " loose icons, " + dummy + " missing");
                resolver.ClearAllCache();
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }

            if (Directory.Exists(root)) Directory.Delete(root, true);

            // 7. scale x1 (settings, owner request 2026-10-08): its own packs (the x2 atlases that the resolver shrinks, the loose x1 icons and the x2 fallback of the
            //    icons that have no x1 file) are enough for every sprite the costumes need
            string root1 = Path.Combine(projectRoot, "Logs", "asset_test_cache_x1");
            if (Directory.Exists(root1)) Directory.Delete(root1, true);
            int expected1 = 0;
            foreach (var p in manifest.packs) if (p.scale == 0 || p.scale == 1) expected1++;
            var r7 = await new BbAssetUpdater(root1).UpdateAsync(baseUrl, manifest.version, 1, null, ct);
            Expect(r7.packsDownloaded == expected1 && Directory.Exists(Path.Combine(root1, "data", "icon", "x1")) && !Directory.Exists(Path.Combine(root1, "data", "res", "x3")),
                "x1 download: " + r7.packsDownloaded + " packs, " + (r7.bytesDownloaded / 1048576f).ToString("F1") + " MB");
            var go1 = new GameObject("verify-resolver-x1");
            try
            {
                var resolver = go1.AddComponent<NroSpriteResolver>();
                resolver.Initialize(root1);
                int atlas = 0, loose = 0, dummy = 0;
                foreach (int id in ids)
                {
                    if (id <= 0) continue;
                    var sp = resolver.ResolveSprite(id, 1);
                    if (sp.name.StartsWith("Atlas_")) atlas++;
                    else if (sp.name.StartsWith("Loose_")) loose++;
                    else dummy++;
                }
                Expect(dummy == 0 && atlas > 0 && loose > 0, "resolver at x1: " + atlas + " sprites from the shrunk atlases, " + loose + " loose icons, " + dummy + " missing");
                resolver.ClearAllCache();
            }
            finally { UnityEngine.Object.DestroyImmediate(go1); }
            if (Directory.Exists(root1)) Directory.Delete(root1, true);
            return log.ToString();
        }

        [Serializable]
        class IndexView
        {
            public string version;
            public int scale;
            public List<string> ids;
            public List<string> hashes;
        }
    }
}
