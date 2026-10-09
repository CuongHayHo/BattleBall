using System;
using System.IO;
using System.Linq;
using System.Text;
using BattleBall.Net;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BattleBall.EditorTools
{
    /// <summary>
    /// Project configuration helpers: the BbConfig asset and the Android build (IL2CPP with ARM64 and ARMv7, as the Nro client; ARM64 is required by Google Play).
    /// Menu: BattleBall/Create Config, BattleBall/Apply Android Settings, BattleBall/Build Android (development APK).
    /// Batch: -executeMethod BattleBall.EditorTools.BbBuild.BuildAndroidBatch (writes Logs/android_build.txt, exit code 0 on success).
    /// </summary>
    public static class BbBuild
    {
        const string ConfigPath = "Assets/BattleNet/Resources/BbConfig.asset";
        const string ApkPath = "Builds/Android/BattleBall-dev.apk";

        [MenuItem("BattleBall/Create Config")]
        public static void CreateConfig()
        {
            if (AssetDatabase.LoadAssetAtPath<BbConfig>(ConfigPath) != null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<BbConfig>(), ConfigPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Created " + ConfigPath);
        }

        [MenuItem("BattleBall/Apply Android Settings")]
        public static void ApplyAndroidSettings()
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            AssetDatabase.SaveAssets();
        }

        [MenuItem("BattleBall/Build Android (development APK)")]
        public static void BuildAndroid()
        {
            string report = Build();
            Debug.Log(report);
        }

        public static void BuildAndroidBatch()
        {
            string report;
            int code = 0;
            try
            {
                report = Build();
                if (!report.StartsWith("Succeeded", StringComparison.OrdinalIgnoreCase)) code = 1;
            }
            catch (Exception e) { report = "FAILED\n" + e; code = 1; }
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "android_build.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report);
            EditorApplication.Exit(code);
        }

        static string Build()
        {
            CreateConfig();
            ApplyAndroidSettings();
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development      // development build: Debug.isDebugBuild is true, so BbConfig may accept the self-signed dev certificate
            };
            BuildReport r = BuildPipeline.BuildPlayer(options);
            var sb = new StringBuilder();
            sb.AppendLine(r.summary.result + " in " + r.summary.totalTime.TotalMinutes.ToString("F1") + " min");
            sb.AppendLine("scenes: " + string.Join(", ", scenes));
            sb.AppendLine("backend: " + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) + ", architectures: " + PlayerSettings.Android.targetArchitectures);
            if (r.summary.result == BuildResult.Succeeded) sb.AppendLine("output: " + r.summary.outputPath + " (" + (r.summary.totalSize / 1048576f).ToString("F1") + " MB)");
            sb.AppendLine("errors: " + r.summary.totalErrors + ", warnings: " + r.summary.totalWarnings);
            foreach (var step in r.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception) sb.AppendLine("  " + m.content);
            return sb.ToString();
        }
    }
}
