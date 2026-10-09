#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NroRig.EditorTools
{
    /// <summary>
    /// Tao san prefab Mannequin duy nhat: Assets/NroMannequin/Prefabs/NroMannequin.prefab.
    /// Tu dong tao neu chua ton tai khi Unity nap script; co the tao lai bang menu NRO/Create Mannequin Prefab.
    /// </summary>
    [InitializeOnLoad]
    public static class NroMannequinPrefabBuilder
    {
        private const string PrefabDir = "Assets/NroMannequin/Prefabs";
        private const string PrefabPath = PrefabDir + "/NroMannequin.prefab";

        static NroMannequinPrefabBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(PrefabPath)) Build();
            };
        }

        [MenuItem("NRO/Create Mannequin Prefab")]
        public static void Build()
        {
            if (!Directory.Exists(PrefabDir)) Directory.CreateDirectory(PrefabDir);

            var root = new GameObject("NroMannequin");
            var m = root.AddComponent<NroMannequin>();

            // Thu tu tao khop Z-order: Aura(0) -> Head(2) -> Leg(3) -> Body(4)
            m.auraRenderer = CreateSlot(root.transform, "Aura_Slot");
            m.headRenderer = CreateSlot(root.transform, "Head_Slot");
            m.legRenderer = CreateSlot(root.transform, "Leg_Slot");
            m.bodyRenderer = CreateSlot(root.transform, "Body_Slot");

            m.pose = NroPose.Stand_1;
            m.facing = 1;
            m.Apply();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[NRO] Da tao Mannequin prefab: " + PrefabPath);
        }

        private static SpriteRenderer CreateSlot(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<SpriteRenderer>();
        }
    }
}
#endif
