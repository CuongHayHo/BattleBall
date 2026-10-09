#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using NroRig.UI;

namespace NroRig.EditorTools
{
    public static class CostumeTesterSceneBuilder
    {
        private const string SceneDir = "Assets/NroMannequin/Scenes";
        private const string ScenePath = SceneDir + "/CostumeTester.unity";
        private const string MannequinPrefabPath = "Assets/NroMannequin/Prefabs/NroMannequin.prefab";

        [MenuItem("NRO/Build Costume Tester Scene", priority = 1)]
        public static void BuildScene()
        {
            if (!Directory.Exists(SceneDir)) Directory.CreateDirectory(SceneDir);

            // Ensure Mannequin Prefab exists
            if (!File.Exists(MannequinPrefabPath))
            {
                NroMannequinPrefabBuilder.Build();
            }

            // Create new Empty Scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Camera Setup
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0, 0.5f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 2.5f;
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>();
            var camController = camGo.AddComponent<Camera2DController>();
            camController.defaultSize = 2.5f;
            camController.defaultPosition = new Vector3(0, 0.5f, -10f);

            // 2. Global Light 2D Setup
            var lightGo = new GameObject("Global Light 2D");
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = Color.white;
            light.intensity = 1.0f;

            // 3. Ground Guide Line
            var groundGo = new GameObject("Ground_Guide_Line");
            var line = groundGo.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, new Vector3(-10f, 0, 0));
            line.SetPosition(1, new Vector3(10f, 0, 0));
            line.startWidth = 0.02f;
            line.endWidth = 0.02f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = new Color(0.3f, 0.35f, 0.45f, 0.5f);
            line.endColor = new Color(0.3f, 0.35f, 0.45f, 0.5f);

            // 4. Mannequin Instance from Prefab
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MannequinPrefabPath);
            GameObject mannequinGo;
            if (prefab != null)
            {
                mannequinGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            }
            else
            {
                mannequinGo = new GameObject("NroMannequin");
                mannequinGo.AddComponent<NroMannequin>();
            }
            mannequinGo.name = "NroMannequin";
            mannequinGo.transform.position = Vector3.zero;
            var mannequin = mannequinGo.GetComponent<NroMannequin>();

            // 5. Overhead Mock HP Bar
            var hpBarGo = new GameObject("World_MockHPBar");
            hpBarGo.transform.position = new Vector3(0, 0.6f, 0);

            var bgSprite = CreateBoxSprite(80, 8, new Color(0.4f, 0.1f, 0.1f, 0.9f));
            var fillSprite = CreateBoxSprite(80, 8, new Color(0.1f, 0.85f, 0.2f, 1f));

            var hpBg = new GameObject("Bar_BG").AddComponent<SpriteRenderer>();
            hpBg.transform.SetParent(hpBarGo.transform, false);
            hpBg.sprite = bgSprite;
            hpBg.sortingOrder = 10;

            var hpFill = new GameObject("Bar_Fill").AddComponent<SpriteRenderer>();
            hpFill.transform.SetParent(hpBarGo.transform, false);
            hpFill.sprite = fillSprite;
            hpFill.sortingOrder = 11;

            // 6. Data & Resolver Managers
            var managerGo = new GameObject("NroManagers");
            var catalog = managerGo.AddComponent<NroCostumeCatalog>();
            var resolver = managerGo.AddComponent<NroSpriteResolver>();

            // 7. EventSystem
            var eventGo = new GameObject("EventSystem");
            eventGo.AddComponent<EventSystem>();
            eventGo.AddComponent<StandaloneInputModule>();

            // 8. UGUI Canvas Hierarchy
            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // UI Controller Script
            var uiController = canvasGo.AddComponent<CostumeTesterUI>();
            uiController.mannequin = mannequin;
            uiController.catalog = catalog;
            uiController.resolver = resolver;
            uiController.mockHpBar = hpBarGo.transform;
            uiController.cameraController = camController;

            // Build Panels
            BuildLeftPanel(canvasGo.transform, uiController, defaultFont);
            BuildBottomPanel(canvasGo.transform, uiController, defaultFont);
            BuildRightPanel(canvasGo.transform, uiController, defaultFont);

            // Save Scene
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Add Scene to Build Settings if not already present
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Any(s => s.path == ScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            Debug.Log($"[NRO] Đã tạo thành công Scene CostumeTester tại: {ScenePath}");
        }

        private static void BuildLeftPanel(Transform canvas, CostumeTesterUI ui, Font font)
        {
            var panelGo = CreatePanel(canvas, "LeftPanel", new Vector2(0, 0), new Vector2(0, 1), 
                new Vector2(0, 0.5f), new Vector2(210, 0), new Vector2(420, 0), new Color(0.12f, 0.14f, 0.18f, 0.95f));
            var panel = panelGo.transform;

            // Title
            CreateText(panel, "Title", "NRO COSTUME TESTER", font, 20, FontStyle.Bold, Color.white,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -35), new Vector2(-20, 40));

            // Search Bar InputField
            var searchGo = CreatePanel(panel, "SearchBar", new Vector2(0, 1), new Vector2(1, 1), 
                new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(-20, 36), new Color(0.18f, 0.22f, 0.28f, 1f));
            var searchInput = searchGo.AddComponent<InputField>();
            var searchText = CreateText(searchGo.transform, "Text", "", font, 14, FontStyle.Normal, Color.white,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-60, -10));
            var placeholder = CreateText(searchGo.transform, "Placeholder", "Search costume name or ID...", font, 14, FontStyle.Italic, Color.gray,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-60, -10));
            searchInput.textComponent = searchText;
            searchInput.placeholder = placeholder;
            ui.searchInput = searchInput;

            var clearBtn = CreateButton(searchGo.transform, "ClearBtn", "✕", font, 14, 
                new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(28, 28));
            ui.clearSearchButton = clearBtn;

            // 7 Category Filter Buttons
            var filterContainer = CreatePanel(panel, "FilterContainer", new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0.5f, 1), new Vector2(0, -145), new Vector2(-20, 60), Color.clear);
            var grid = filterContainer.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(95, 26);
            grid.spacing = new Vector2(5, 5);

            string[] catKeys = { "All", "Standard", "Fusion", "Broly", "GreatApe", "Chibi", "Component" };
            string[] catLabels = { "All (221)", "Standard", "Fusion", "Broly", "Ape", "Chibi", "Parts" };
            ui.categoryFilterButtons = new Button[catKeys.Length];
            for (int i = 0; i < catKeys.Length; i++)
            {
                var btn = CreateButton(filterContainer.transform, $"Filter_{catKeys[i]}", catLabels[i], font, 11,
                    Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                ui.categoryFilterButtons[i] = btn;
            }

            // ScrollView
            var scrollGo = CreatePanel(panel, "CostumeScrollView", new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(0.5f, 0.5f), new Vector2(0, -120), new Vector2(-20, -260), new Color(0.08f, 0.10f, 0.12f, 1f));
            var scrollRect = scrollGo.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            ui.costumeScrollRect = scrollRect;

            var viewport = CreatePanel(scrollGo.transform, "Viewport", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.clear);
            viewport.AddComponent<RectMask2D>();
            scrollRect.viewport = viewport.GetComponent<RectTransform>();

            var content = CreatePanel(viewport.transform, "Content", new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 0), Color.clear);
            var contentVlg = content.AddComponent<VerticalLayoutGroup>();
            contentVlg.childControlWidth = true;
            contentVlg.childControlHeight = false;
            contentVlg.childForceExpandWidth = true;
            contentVlg.spacing = 3;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = content.GetComponent<RectTransform>();
            ui.costumeContent = content.GetComponent<RectTransform>();

            // Create Item Template inside Content
            var itemTemplate = BuildCostumeItemTemplate(content.transform, font);
            ui.costumeItemPrefab = itemTemplate;
        }

        private static GameObject BuildCostumeItemTemplate(Transform parent, Font font)
        {
            var item = CreatePanel(parent, "CostumeItemTemplate", new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 48), new Color(0.14f, 0.16f, 0.20f, 0.95f));

            var itemUI = item.AddComponent<CostumeListItemUI>();
            itemUI.button = item.AddComponent<Button>();
            itemUI.backgroundImage = item.GetComponent<Image>();

            // Title
            itemUI.labelTitle = CreateText(item.transform, "Title", "[#451] Costume Name", font, 13, FontStyle.Bold, Color.white,
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 10), new Vector2(250, 20));

            // Parts
            itemUI.labelParts = CreateText(item.transform, "Parts", "H:344 B:345 L:346", font, 11, FontStyle.Normal, new Color(0.7f, 0.7f, 0.7f),
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, -10), new Vector2(250, 18));

            // Badge
            var badgeGo = CreatePanel(item.transform, "Badge", new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(80, 24), new Color(0.2f, 0.6f, 0.86f, 1f));
            itemUI.badgeBackground = badgeGo.GetComponent<Image>();
            itemUI.labelBadge = CreateText(badgeGo.transform, "Text", "Standard", font, 11, FontStyle.Bold, Color.white,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            itemUI.labelBadge.alignment = TextAnchor.MiddleCenter;

            // Highlight border
            var hlGo = CreatePanel(item.transform, "SelectionHighlight", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.95f, 0.75f, 0.10f, 0.35f));
            hlGo.SetActive(false);
            itemUI.selectionHighlight = hlGo;

            return item;
        }

        private static void BuildBottomPanel(Transform canvas, CostumeTesterUI ui, Font font)
        {
            var panelGo = CreatePanel(canvas, "BottomPanel", new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(1100, 155), new Color(0.12f, 0.14f, 0.18f, 0.95f));
            var panel = panelGo.transform;

            // Sub-Bar
            CreateText(panel, "Title", "POSE CONTROLLER (33 POSES)", font, 14, FontStyle.Bold, Color.white,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -18), new Vector2(300, 25));

            var poseLabel = CreateText(panel, "PoseLabel", "[cf = 0] Stand_1", font, 13, FontStyle.Bold, new Color(0.95f, 0.75f, 0.1f),
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(300, 25));
            poseLabel.alignment = TextAnchor.MiddleCenter;
            ui.currentPoseLabel = poseLabel;

            ui.prevPoseButton = CreateButton(panel, "PrevPose", "◀ Prev", font, 11,
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-220, -18), new Vector2(65, 24));

            ui.nextPoseButton = CreateButton(panel, "NextPose", "Next ▶", font, 11,
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(220, -18), new Vector2(65, 24));

            ui.autoPlayButton = CreateButton(panel, "AutoPlay", "▶ Run Loop", font, 11,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-80, -18), new Vector2(110, 24));

            // Grid for 33 buttons
            var gridGo = CreatePanel(panel, "PoseGrid", new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(-20, 110), Color.clear);
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(92, 32);
            grid.spacing = new Vector2(6, 4);

            ui.poseButtons = new Button[NroPoseTable.PoseCount];
            string[] shortNames = {
                "00. Stand 1", "01. Stand 2", "02. Run 1", "03. Run 2", "04. Run 3",
                "05. Run 4", "06. Run 5", "07. Jump", "08. Fly Horiz", "09. Kick 1",
                "10. Kick 2", "11. Kick High", "12. Fall", "13. Punch 1", "14. Punch 2",
                "15. P-Ready", "16. Strike", "17. Charge Ki", "18. Blast", "19. Kame Chg",
                "20. Kame Fire", "21. Kame Rel", "22. Special", "23. Hurt", "24. Knockdown",
                "25. Dash", "26. Air P1", "27. Air P2", "28. Air Rdy", "29. Air Strike",
                "30. Air K-Chg", "31. Air K-Fire", "32. Air K-Rel"
            };

            for (int i = 0; i < NroPoseTable.PoseCount; i++)
            {
                var btn = CreateButton(gridGo.transform, $"Pose_{i}", shortNames[i], font, 10,
                    Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                ui.poseButtons[i] = btn;
            }
        }

        private static void BuildRightPanel(Transform canvas, CostumeTesterUI ui, Font font)
        {
            var panelGo = CreatePanel(canvas, "RightPanel", new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(1, 0.5f), new Vector2(-190, 0), new Vector2(380, 0), new Color(0.12f, 0.14f, 0.18f, 0.95f));
            var panel = panelGo.transform;

            // Resolution Group
            CreateText(panel, "ResTitle", "TEXTURE RESOLUTION", font, 13, FontStyle.Bold, Color.white,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(-20, 25));

            var resRow = CreatePanel(panel, "ResRow", new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0.5f, 1), new Vector2(0, -65), new Vector2(-20, 36), Color.clear);
            var resGrid = resRow.AddComponent<GridLayoutGroup>();
            resGrid.cellSize = new Vector2(80, 32);
            resGrid.spacing = new Vector2(10, 0);

            ui.resolutionButtons = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                ui.resolutionButtons[i] = CreateButton(resRow.transform, $"Res_x{i+1}", $"x{i+1}", font, 13,
                    Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            }

            // Facing Group
            CreateText(panel, "FacingTitle", "FACING DIRECTION", font, 13, FontStyle.Bold, Color.white,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -115), new Vector2(-20, 25));

            var flipBtn = CreateButton(panel, "FlipBtn", "Facing: Right (+1)", font, 13,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(-20, 34));
            ui.flipFacingButton = flipBtn;
            ui.flipFacingLabel = flipBtn.GetComponentInChildren<Text>();

            // Reset View Button
            ui.resetCameraViewButton = CreateButton(panel, "ResetCamBtn", "Reset Camera View", font, 12,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -195), new Vector2(-20, 30));

            // Debug HUD Card
            var hudCard = CreatePanel(panel, "DebugHUDCard", new Vector2(0, 0), new Vector2(1, 1),
                new Vector2(0.5f, 0.5f), new Vector2(0, -235), new Vector2(-20, -250), new Color(0.08f, 0.10f, 0.12f, 1f));

            var hudText = CreateText(hudCard.transform, "HUDText", "Loading Debug Info...", font, 12, FontStyle.Normal, Color.white,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20, -20));
            hudText.alignment = TextAnchor.UpperLeft;
            ui.debugHudText = hudText;
        }

        #region Helper Creators

        private static GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private static GameObject CreatePanel(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta, Color color)
        {
            return CreatePanel(parent.transform, name, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta, color);
        }

        private static Text CreateText(Transform parent, string name, string text, Font font, int fontSize,
            FontStyle style, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = font;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = color;
            t.raycastTarget = false;
            return t;
        }

        private static Text CreateText(GameObject parent, string name, string text, Font font, int fontSize,
            FontStyle style, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            return CreateText(parent.transform, name, text, font, fontSize, style, color, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta);
        }

        private static Button CreateButton(Transform parent, string name, string label, Font font, int fontSize,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = CreatePanel(parent, name, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta, new Color(0.20f, 0.24f, 0.30f, 0.95f));
            var btn = go.AddComponent<Button>();

            var text = CreateText(go.transform, "Label", label, font, fontSize, FontStyle.Normal, Color.white,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            text.alignment = TextAnchor.MiddleCenter;
            return btn;
        }

        private static Button CreateButton(GameObject parent, string name, string label, Font font, int fontSize,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            return CreateButton(parent.transform, name, label, font, fontSize, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta);
        }

        private static Sprite CreateBoxSprite(int width, int height, Color color)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var colors = new Color[width * height];
            for (int i = 0; i < colors.Length; i++) colors[i] = color;
            tex.SetPixels(colors);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        #endregion
    }
}
#endif
