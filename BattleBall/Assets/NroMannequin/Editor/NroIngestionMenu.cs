#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NroRig.EditorTools
{
    public static class NroIngestionMenu
    {
        [MenuItem("NRO/Database/Ingest Costume Database", priority = 10)]
        public static void RunDatabaseIngestion()
        {
            string scriptPath = @"c:\Users\cuonghayho\Documents\HocTap\AdrGame\phan_tich_game_2d\Ball\Report\scripts\ingest_nro_costumes.py";

            if (!File.Exists(scriptPath))
            {
                Debug.LogError($"[NroIngestion] Không tìm thấy file script Python tại: {scriptPath}");
                return;
            }

            string outDir = Path.Combine(Application.dataPath, "NroMannequin", "Data");
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\" --out-dir \"{outDir}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Debug.Log("[NroIngestion] Đang chạy ingestion script Python...");
            using (Process process = Process.Start(psi))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    Debug.Log($"[NroIngestion] Hoàn tất thành công!\n{stdout}");
                    AssetDatabase.Refresh();
                }
                else
                {
                    Debug.LogError($"[NroIngestion] Lỗi khi chạy script (Code {process.ExitCode}):\n{stderr}\n{stdout}");
                }
            }
        }
    }
}
#endif
