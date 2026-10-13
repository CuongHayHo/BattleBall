using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NroRig.EditorTools
{
    /// <summary>
    /// Renders the mannequin of the CostumeTester scene in each of the 33 poses of NroPoseTable, one PNG per pose (Logs/pose_sheet/pose_NN.png),
    /// wearing the costume CostumeId (its first head frame). Run in batch mode:
    /// Unity -batchmode -quit -executeMethod NroRig.EditorTools.NroPoseSheetCapture.CaptureBatch
    /// </summary>
    public static class NroPoseSheetCapture
    {
        const string ScenePath = "Assets/NroMannequin/Scenes/CostumeTester.unity";
        const int Size = 320;
        const int Layer = 31;
        const int CostumeId = 1815;               // "ct dep vcl" (owner choice 2026-10-13)

        [MenuItem("NRO/Capture 33 Poses", priority = 3)]
        public static void Capture()
        {
            Render("pose_sheet", Size, false, null);
        }

        /// <summary>
        /// The five running frames (Run_1..Run_5) of the same costume on a transparent background, 640 px (Logs/logo_run/run_N.png), for the animated
        /// logo of the web page (BattleBallServer/web, tools/build_logo.py). Unity -batchmode -quit -executeMethod NroRig.EditorTools.NroPoseSheetCapture.CaptureRunLogoBatch
        /// </summary>
        public static void CaptureRunLogoBatch()
        {
            Render("logo_run", 640, true, new[] { (int)NroPose.Run_1, (int)NroPose.Run_2, (int)NroPose.Run_3, (int)NroPose.Run_4, (int)NroPose.Run_5 });
            EditorApplication.Exit(0);
        }

        static void Render(string folder, int size, bool transparent, int[] poses)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var mannequin = Object.FindObjectOfType<NroMannequin>();
            var catalog = Object.FindObjectOfType<NroCostumeCatalog>();
            var resolver = Object.FindObjectOfType<NroSpriteResolver>();
            if (mannequin == null || catalog == null || resolver == null)
            {
                Debug.LogError("[PoseSheet] the CostumeTester scene has no mannequin, catalog or resolver");
                return;
            }
            resolver.Initialize();
            catalog.Initialize();
            const int zoom = 4;
            var entry = catalog.GetCostume(CostumeId);
            if (entry == null)
            {
                Debug.LogError("[PoseSheet] no costume " + CostumeId);
                return;
            }
            int headId = entry.headCyclePartIds != null && entry.headCyclePartIds.Length > 0 ? entry.headCyclePartIds[0] : entry.headPartId;
            mannequin.SetParts(catalog.GetPart(headId, zoom), catalog.GetPart(entry.bodyPartId, zoom), catalog.GetPart(entry.legPartId, zoom));
            mannequin.aura = null;
            mannequin.isMonkey = mannequin.isFusion = false;
            mannequin.zoom = 1f;
            mannequin.SetFacing(1);
            // the scene's sprites are lit by its 2D light; this camera sees only the mannequin, so draw it unlit
            var unlit = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            foreach (var r in mannequin.GetComponentsInChildren<SpriteRenderer>(true)) r.sharedMaterial = unlit;
            foreach (var t in mannequin.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;

            var camGo = new GameObject("PoseSheetCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 0.5f;
            cam.transform.position = mannequin.transform.position + new Vector3(0f, 0.38f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = transparent ? new Color(0f, 0f, 0f, 0f) : Color.white;
            cam.cullingMask = 1 << Layer;

            string dir = Path.Combine(Application.dataPath, "..", "Logs", folder);
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(size, size, transparent ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
            foreach (int cf in poses ?? System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Range(0, NroPoseTable.PoseCount)))
            {
                mannequin.SetPose((NroPose)cf);
                mannequin.Apply();
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, transparent ? "run_" + cf + ".png" : "pose_" + cf.ToString("00") + "_" + (NroPose)cf + ".png"), tex.EncodeToPNG());
                RenderTexture.active = null;
                cam.targetTexture = null;
            }
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
            Debug.Log("[PoseSheet] wrote " + (poses ?? new int[NroPoseTable.PoseCount]).Length + " poses to " + Path.GetFullPath(dir));
        }

        public static void CaptureBatch()
        {
            Capture();
            EditorApplication.Exit(0);
        }
    }
}
