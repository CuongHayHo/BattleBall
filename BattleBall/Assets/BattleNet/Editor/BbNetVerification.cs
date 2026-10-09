using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using BattleBall.Net;

namespace BattleBall.EditorTools
{
    /// <summary>
    /// Runs <see cref="BbNetSelfTest"/> against the local server (start it with BattleBallServer/tools/run_server.ps1).
    /// Menu: BattleBall/Verify Network. Batch mode: Unity -batchmode -nographics -projectPath ... -executeMethod BattleBall.EditorTools.BbNetVerification.RunBatch
    /// [-bbHost 127.0.0.1] [-bbPort 7443]; writes Logs/network_verification.txt and exits 0 (all passed) or 1.
    /// </summary>
    public static class BbNetVerification
    {
        [MenuItem("BattleBall/Verify Network")]
        public static void RunFromMenu()
        {
            string report;
            try { report = System.Threading.Tasks.Task.Run(() => BbNetSelfTest.RunAsync("127.0.0.1", 7443)).GetAwaiter().GetResult(); Debug.Log("Network verification passed:\n" + report); }
            catch (Exception e) { Debug.LogError("Network verification failed: " + e); }
        }

        public static void RunBatch()
        {
            string host = Arg("-bbHost", "127.0.0.1");
            int port = int.Parse(Arg("-bbPort", "7443"));
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "network_verification.txt");
            int code = 0;
            string text;
            try { text = "PASSED\n" + System.Threading.Tasks.Task.Run(() => BbNetSelfTest.RunAsync(host, port)).GetAwaiter().GetResult(); }
            catch (Exception e) { text = "FAILED\n" + e; code = 1; }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            Debug.Log(text);
            EditorApplication.Exit(code);
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return fallback;
        }
    }
}
