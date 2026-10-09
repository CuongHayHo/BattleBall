using System.Collections.Generic;
using System.IO;
using BattleBall.Net;
using NroRig;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattleBall.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/Login.unity: the start screens in the layout of the Nro client (loading with progress bar, saved-account menu, login, register)
    /// with the BattleBall logo, and makes it the first scene of the build. Sprites live in Assets/BattleNet/UI/Sprites (buttons and frames derived from
    /// the Nro sprites in SourceData/ui_audio/ui_menu; logo from Desktop/data/logogame.png). Re-running replaces the scene.
    /// Menu: BattleBall/Build Login Scene. Batch: -executeMethod BattleBall.EditorTools.BbLoginSceneBuilder.BuildBatch
    /// </summary>
    public static class BbLoginSceneBuilder
    {
        const string SpriteDir = "Assets/BattleNet/UI/Sprites/";
        public const string ScenePath = "Assets/Scenes/Login.unity";
        public const string MenuScenePath = "Assets/Scenes/Menu.unity";

        static readonly Color Ink = new Color(0.45f, 0.12f, 0.05f);
        static readonly Color ButtonText_ = new Color(0.91f, 0.75f, 0.54f);   // light brown text of the buttons
        static readonly Color OutlineColor = new Color(0.25f, 0.08f, 0.02f, 1f);

        [MenuItem("BattleBall/Build Login Scene")]
        public static void Build()
        {
            AssetDatabase.ImportAsset(FontPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureSprites();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            var camera = cam.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            var ui = canvasGo.AddComponent<BbLoginUi>();

            // background: scrolling star picture with drifting planets (BbSpaceBackground builds its own children)
            var bg = Stretched("Background", root);
            var space = bg.gameObject.AddComponent<BbSpaceBackground>();
            space.background = Sprite("space_bg", "jpg");
            space.planetA = Sprite("planet_a");
            space.planetB = Sprite("planet_b");
            space.Build();

            // ---- menu (saved account)
            var menu = Group("Menu", root);
            Logo(menu, new Vector2(0, 215), new Vector2(440, 220));
            ui.resumeButton = MenuButton("ResumeButton", menu, 50, "Chơi TK", out ui.resumeLabel);
            ui.newPlayButton = MenuButton("NewPlayButton", menu, -20, "Chơi mới", out _);
            ui.switchButton = MenuButton("SwitchButton", menu, -90, "Đổi tài khoản", out _);
            var serverButton = MenuButton("ServerButton", menu, -160, "Máy chủ", out ui.serverLabel);
            serverButton.interactable = false;
            serverButton.transition = Selectable.Transition.None;
            var dot = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            dot.transform.SetParent(serverButton.transform, false);
            Center(Rect(dot), new Vector2(-140, 0), new Vector2(18, 18));
            ui.serverDot = dot.GetComponent<Image>();
            ui.serverDot.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            ui.serverDot.color = new Color(0.2f, 0.9f, 0.2f);
            ui.serverDot.raycastTarget = false;

            // ---- form (login / register)
            var form = Group("Form", root);
            Logo(form, new Vector2(0, 225), new Vector2(440, 220));
            var panel = Image("Panel", form, "panel_login", true);
            panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            panel.rectTransform.pivot = new Vector2(0.5f, 1f);
            panel.rectTransform.anchoredPosition = new Vector2(0, 95);
            panel.rectTransform.sizeDelta = new Vector2(620, 230);
            ui.panel = panel.rectTransform;
            ui.userField = Field("UserField", panel.transform, -24, "Tài khoản", 20, false);
            ui.passField = Field("PassField", panel.transform, -96, "Mật khẩu", 64, true);
            var confirm = Group("ConfirmRow", panel.transform);
            ui.confirmRow = confirm.gameObject;
            ui.confirmField = Field("ConfirmField", confirm.transform, -168, "Nhập lại mật khẩu", 64, true);
            ui.status = Label("Status", panel.transform, "", 24, Ink, new Vector2(0, -174), new Vector2(560, 40), false);
            var row = new GameObject("ButtonRow", typeof(RectTransform));
            row.transform.SetParent(form, false);
            var rowRect = Rect(row);
            rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 0.5f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.sizeDelta = new Vector2(620, 64);
            rowRect.anchoredPosition = new Vector2(0, 95 - 230 - 20);
            ui.buttonRow = rowRect;
            ui.okButton = OrangeButton("OkButton", row.transform, new Vector2(-120, -32), new Vector2(200, 60), "OK", 30, out _);
            ui.secondButton = OrangeButton("SecondButton", row.transform, new Vector2(120, -32), new Vector2(200, 60), "Đăng ký", 30, out ui.secondLabel);

            // ---- loading (black, on top of the others)
            var loading = Group("Loading", root);
            var black = Stretched("Black", loading).gameObject.AddComponent<Image>();
            black.color = Color.black;
            black.raycastTarget = false;
            Logo(loading, new Vector2(0, 80), new Vector2(520, 260));
            var track = Image("BarTrack", loading, "bar_capsule", true);
            Center(track.rectTransform, new Vector2(0, -95), new Vector2(400, 24));
            track.color = new Color(0.04f, 0.38f, 0.1f);
            track.raycastTarget = false;
            var fillGo = new GameObject("BarFill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(track.transform, false);
            Stretch(Rect(fillGo), 0, 0);
            var fill = fillGo.GetComponent<Image>();
            fill.sprite = Sprite("bar_capsule");
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0.55f;
            fill.color = new Color(1f, 0.88f, 0.1f);
            fill.raycastTarget = false;
            ui.barFill = fill;
            ui.loadingStatus = Label("LoadingStatus", loading, "Đang kết nối máy chủ...", 24, Color.white, new Vector2(0, -135), new Vector2(700, 36), false);
            AddOutline(ui.loadingStatus.gameObject);
            ui.cancelButton = OrangeButton("CancelButton", loading, new Vector2(0, -205), new Vector2(170, 56), "Hủy", 28, out ui.cancelLabel);

            // ---- corner texts (version top right, clear data bottom right)
            ui.versionLabel = Label("Version", root, "v1.0", 24, Color.white, Vector2.zero, new Vector2(300, 44), true);
            var vr = ui.versionLabel.rectTransform;
            vr.anchorMin = vr.anchorMax = vr.pivot = new Vector2(1, 1);
            vr.anchoredPosition = new Vector2(-20, -14);
            ui.versionLabel.alignment = TextAnchor.UpperRight;
            AddOutline(ui.versionLabel.gameObject);
            var clear = Label("ClearData", root, "Xóa dữ liệu", 25, ButtonText_, Vector2.zero, new Vector2(260, 44), true);
            var cr = clear.rectTransform;
            cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1, 0);
            cr.anchoredPosition = new Vector2(-20, 14);
            clear.alignment = TextAnchor.LowerRight;
            clear.raycastTarget = true;
            AddOutline(clear.gameObject);
            ui.clearDataButton = clear.gameObject.AddComponent<Button>();
            ui.clearDataButton.targetGraphic = clear;

            ui.loadingRoot = loading.gameObject;
            ui.menuRoot = menu.gameObject;
            ui.formRoot = form.gameObject;
            // saved state = the loading screen (BbLoginUi shows the right screen at run time)
            menu.gameObject.SetActive(false);
            form.gameObject.SetActive(false);
            confirm.gameObject.SetActive(false);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("Login scene built: " + ScenePath);
            BuildMenu();
        }

        public static void BuildBatch()
        {
            try { Build(); }
            catch (System.Exception e) { Debug.LogError(e); EditorApplication.Exit(1); return; }
            EditorApplication.Exit(0);
        }

        // ---------------------------------------------------------------------------------------------------------------- menu scene

        static readonly Color PanelCream = new Color(0.84f, 0.77f, 0.66f);
        static readonly Color PanelTan = new Color(0.69f, 0.56f, 0.38f);
        static readonly Color StatYellow = new Color(1f, 0.93f, 0.25f);

        /// <summary>
        /// Builds Assets/Scenes/Menu.unity in the layout of the Nro inventory panel: a panel on the left (portrait and numbers, one tab per character
        /// Trái Đất / Xayda / Namếc, the character between its equipment slots, the bag, the money) and the buttons into the game on the right.
        /// </summary>
        public static void BuildMenu()
        {
            ConfigureSprites();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            var camera = cam.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;
            var ui = canvasGo.AddComponent<BbMenuUi>();

            // Nro data and sprite resolver used by the character picture (their Awake loads them at run time)
            var catalogGo = new GameObject("NroCostumeCatalog");
            var catalog = catalogGo.AddComponent<NroCostumeCatalog>();
            var so = new SerializedObject(catalog);
            so.FindProperty("costumeCatalogAsset").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/NroMannequin/Data/CostumeCatalog.json");
            so.FindProperty("partDatabaseAsset").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/NroMannequin/Data/PartDatabase.json");
            so.ApplyModifiedPropertiesWithoutUndo();
            new GameObject("NroSpriteResolver").AddComponent<NroSpriteResolver>();

            // the village behind the panel (BbVillageView draws the map, the NPCs and the characters), then the joystick
            var villageRect = Stretched("Village", root);
            var village = villageRect.gameObject.AddComponent<BbVillageView>();
            village.viewport = villageRect;
            village.font = BuiltinFont;
            village.whiteSprite = Sprite("white");
            village.dotSprite = Sprite("dot");
            ui.village = village;

            var joyBase = Image("Joystick", root, "joy_base", false);
            var jr = joyBase.rectTransform;
            jr.anchorMin = jr.anchorMax = Vector2.zero;
            jr.pivot = Vector2.zero;
            jr.sizeDelta = new Vector2(200f, 247f);
            jr.anchoredPosition = new Vector2(40f, 24f);                  // fixed at the bottom left: the panel hides it while it is open
            joyBase.preserveAspect = true;
            var joystick = joyBase.gameObject.AddComponent<BbJoystick>();
            var knob = Image("Knob", joyBase.transform, "joy_knob", false);
            Center(knob.rectTransform, Vector2.zero, new Vector2(78f, 78f));
            knob.raycastTarget = false;
            joystick.knob = knob.rectTransform;
            joystick.radius = 40f;
            village.joystick = joystick;
            ui.joystickRect = jr;

            // training with the dummies of the village (docs/architecture/BattleBallServer.md 15.12): over the village, under the panel and the popups
            var trainingRect = Stretched("Training", root);
            var training = trainingRect.gameObject.AddComponent<BbTraining>();
            training.village = village;
            training.menu = ui;
            training.font = BuiltinFont;
            training.whiteSprite = Sprite("white");
            training.glowSprite = Sprite("glow");
            training.arrowSheet = Sprite("nro_target_arrows");
            training.hpBar = Sprite("nro_hp_red");
            training.hpLost = Sprite("nro_hp_lost");
            training.fireIdle = Sprite("nro_fire");
            training.firePressed = Sprite("nro_fire_press");
            training.focusIdle = Sprite("nro_focus");
            training.focusPressed = Sprite("nro_focus_press");
            training.bossFrame = Sprite("nro_boss_frame");
            training.hudPanel = Sprite("nro_hud_panel");
            training.hudHp = Sprite("nro_hud_hp");
            training.hudHpLost = Sprite("nro_hud_hp_lost");
            training.hudKi = Sprite("nro_hud_ki");
            training.hudKiLost = Sprite("nro_hud_ki_lost");
            training.skillFrame = Sprite("nro_skill");
            training.skillFrameOn = Sprite("nro_skill_on");
            ui.training = training;

            // ---- the panel: the Nro inventory panel (owner request 2026-10-08 "mô phỏng lại menu từ NroUnity"): Nro draws it 176 px wide, here it is 320 (25% of 1280),
            // so every size below is the Nro size times G (320 / 176); full height, anchored left, every child placed from its top-left corner
            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(root, false);
            var panelRect = Rect(panelGo);
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 0.5f);
            panelRect.sizeDelta = new Vector2(PW, 0f);
            panelRect.anchoredPosition = Vector2.zero;
            var panelBg = panelGo.GetComponent<Image>();
            panelBg.sprite = Sprite("white");
            panelBg.color = NroPanel;
            var panel = panelGo.transform;
            ui.panelRect = panelRect;
            var edge = Image("Edge", panel, "white", false);                  // the dark frame of paintFrameSimple, on the side of the village
            edge.rectTransform.anchorMin = new Vector2(1f, 0f);
            edge.rectTransform.anchorMax = new Vector2(1f, 1f);
            edge.rectTransform.pivot = new Vector2(1f, 0.5f);
            edge.rectTransform.sizeDelta = new Vector2(2f, 0f);
            edge.rectTransform.anchoredPosition = Vector2.zero;
            edge.color = NroEdge;
            edge.raycastTarget = false;

            // the orange triangle on the left edge of the screen opens the folded panel (the menu button of Nro, a quarter of the way down; hidden while the panel is open)
            var toggle = Image("PanelToggle", root, "nro_menu_arrow", false);
            var tr0 = toggle.rectTransform;
            tr0.anchorMin = tr0.anchorMax = new Vector2(0f, 0.75f);
            tr0.pivot = new Vector2(0f, 0.5f);
            tr0.sizeDelta = new Vector2(G(14), G(30));
            tr0.anchoredPosition = Vector2.zero;
            ui.panelToggle = toggle.gameObject.AddComponent<Button>();
            ui.panelToggle.targetGraphic = toggle;
            ui.panelToggleRect = tr0;

            Flat("HeaderBg", panel, 0, 0, PW, G(50), NroHeader);
            // the money strip of Nro (paintBottomMoneyInfo): 15 px high at the bottom edge, an orange line on top
            var footerGo = new GameObject("Footer", typeof(RectTransform));
            footerGo.transform.SetParent(panel, false);
            var footer = footerGo.transform;
            var footerRect = Rect(footerGo);
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.sizeDelta = new Vector2(0f, G(15));
            footerRect.anchoredPosition = Vector2.zero;
            Stretch(Flat("FooterBg", footer, 0, 0, PW, G(15), NroBag).rectTransform, 0, 0);
            Flat("FooterLine", footer, 0, 0, PW, 2, NroLine);

            // header: the head of the costume on the left (always shown, also on the Aura and Chức năng tabs), the close button on the right
            ui.portrait = Image("Portrait", panel, "white", false);
            TopLeft(ui.portrait.rectTransform, 2, 2, G(50) - 4, G(50) - 4);
            ui.portrait.preserveAspect = true;
            ui.portrait.raycastTarget = false;
            var close = Image("Close", panel, "nro_close", false);
            TopLeft(close.rectTransform, PW - G(17) - 4, 4, G(17), G(17));
            ui.closeButton = close.gameObject.AddComponent<Button>();
            ui.closeButton.targetGraphic = close;

            var body = Group("Body", panel);                       // everything that fades when the tab changes
            ui.bodyGroup = body.gameObject.AddComponent<CanvasGroup>();

            // the four lines of numbers of the header (x 60 of Nro), yellow with a dark edge
            ui.headerText = Label("HeaderText", body, "", 16, StatYellow, Vector2.zero, Vector2.zero, false);
            TopLeft(ui.headerText.rectTransform, G(60), 3, PW - G(60) - 4, G(50) - 5);
            ui.headerText.alignment = TextAnchor.MiddleLeft;
            ui.headerText.lineSpacing = 1.1f;
            ui.headerText.resizeTextForBestFit = true;
            ui.headerText.resizeTextMinSize = 11;
            ui.headerText.resizeTextMaxSize = 16;
            ui.headerText.verticalOverflow = VerticalWrapMode.Truncate;          // best fit only shrinks text that may not overflow
            AddOutline(ui.headerText.gameObject, 1f);

            // the experience bar (kept by the owner): between the header and the tabs
            var track = Image("ExpTrack", panel, "bar_capsule", true);
            ui.expTrack = track.gameObject;
            TopLeft(track.rectTransform, 4, G(50) + 2, PW - 8, 14);
            track.color = new Color(0.30f, 0.20f, 0.12f);
            track.raycastTarget = false;
            var fillGo = new GameObject("ExpFill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(track.transform, false);
            Stretch(Rect(fillGo), 2, 2);
            var fill = fillGo.GetComponent<Image>();
            fill.sprite = Sprite("white");                          // a plain rectangle: the bar has square-ish corners like the buttons
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fill.color = new Color(0.45f, 0.78f, 0.35f);              // light-medium green (was yellow)
            fill.raycastTarget = false;
            ui.expFill = fill;
            ui.expText = Label("ExpText", track.transform, "", 11, Color.white, Vector2.zero, Vector2.zero, true);
            Stretch(Rect(ui.expText.gameObject), 0, 0);
            AddOutline(ui.expText.gameObject, 1f);

            // tabs: the button frames of Nro (orange, green when selected), 25 px high, side by side over the whole width; the run time spreads them (BbMenuUi.LayoutTabs)
            ui.tabIdle = Sprite("nro_tab_idle");
            ui.tabOn = Sprite("nro_tab_on");
            ui.tabs = new Button[7];                                  // up to 7: three planets, Hợp thể, Kỹ năng, Aura, Chức năng
            ui.tabImages = new UnityEngine.UI.Image[7];
            ui.tabLabels = new Text[7];
            for (int i = 0; i < 7; i++)
            {
                var tab = Image("Tab" + i, panel, "nro_tab_idle", true);
                TopLeft(tab.rectTransform, 4 + i * 52, TabY, 50, G(25));
                var b = tab.gameObject.AddComponent<Button>();
                b.targetGraphic = tab;
                var label = Label("Text", tab.transform, "", 15, new Color(0.45f, 0.18f, 0.05f), Vector2.zero, Vector2.zero, false);
                Stretch(Rect(label.gameObject), 1, 1);
                label.lineSpacing = 0.85f;
                ui.tabs[i] = b;
                ui.tabImages[i] = tab;
                ui.tabLabels[i] = label;
            }
            Flat("TabLine", panel, 2, TabY + G(25) + 1, PW - 4, 2, NroLine);

            // body: the equipment slots in two columns of five (gear on the left, supports on the right), the character between them standing on the box of bonuses
            ui.slotEmpty = Sprite("nro_cell_body");
            ui.slotFilled = Sprite("nro_cell_bag");                 // something equipped (the client has no item pictures yet): the lighter slot
            ui.bagCell = Sprite("nro_cell_bag");
            ui.cellSelected = Sprite("nro_cell_sel");
            ui.gearSlots = new UnityEngine.UI.Image[5];
            ui.gearLabels = new Text[5];
            for (int i = 0; i < 5; i++)
                ui.gearSlots[i] = NroCell("Gear" + i, body, G(2), CT + i * G(24), "nro_cell_body", out ui.gearLabels[i]);
            ui.supportSlots = new UnityEngine.UI.Image[4];
            ui.supportLabels = new Text[4];
            for (int i = 0; i < 4; i++)
                ui.supportSlots[i] = NroCell("Support" + i, body, G(139), CT + i * G(24), "nro_cell_body", out ui.supportLabels[i]);
            // Hỗ trợ 5: the skin the open tab wears (it leaves the bag and sits here; tap it to take it off)
            ui.skinSlotImage = NroCell("Support4", body, G(139), CT + 4 * G(24), "nro_cell_body", out ui.skinSlotLabel);
            ui.skinSlotImage.raycastTarget = true;
            ui.skinSlotButton = ui.skinSlotImage.gameObject.AddComponent<Button>();
            ui.skinSlotButton.targetGraphic = ui.skinSlotImage;
            ui.skinSlotIcon = Image("SkinIcon", ui.skinSlotImage.transform, "white", false);
            Stretch(ui.skinSlotIcon.rectTransform, 8, 5);
            ui.skinSlotIcon.preserveAspect = true;
            ui.skinSlotIcon.raycastTarget = false;
            ui.skinSlotIcon.enabled = false;
            ui.skinSlotLabel.transform.SetAsLastSibling();

            var viewGo = new GameObject("CharacterView", typeof(RectTransform), typeof(BbCharacterView));
            viewGo.transform.SetParent(body, false);
            var viewRect = Rect(viewGo);
            viewRect.anchorMin = viewRect.anchorMax = new Vector2(0f, 1f);
            viewRect.pivot = new Vector2(0.5f, 0f);
            viewRect.anchoredPosition = new Vector2(PW * 0.5f, -(CT + G(70)));    // the feet on the top of the box of bonuses (paintCharBody at yScroll + 70)
            viewRect.sizeDelta = new Vector2(10f, 10f);
            ui.view = viewGo.GetComponent<BbCharacterView>();
            ui.view.uiScale = G(1);                                             // one Nro pixel is one G

            ui.noSkinText = Label("NoSkin", body, "", 18, Color.white, Vector2.zero, Vector2.zero, false);
            TopLeft(ui.noSkinText.rectTransform, G(38), CT + 40, G(100), 60);
            AddOutline(ui.noSkinText.gameObject, 1f);

            // the box of bonuses of Nro: 100 x 50 at x 36, y 70 of the list, brown with yellow lines
            Flat("BonusBox", body, G(38), CT + G(70), G(100), G(50), NroHeader);
            ui.bonusText = Label("BonusText", body, "", 15, StatYellow, Vector2.zero, Vector2.zero, false);
            TopLeft(ui.bonusText.rectTransform, G(38) + 5, CT + G(70) + 2, G(100) - 8, G(50) - 4);
            ui.bonusText.alignment = TextAnchor.MiddleLeft;
            ui.bonusText.lineSpacing = 1.05f;
            ui.bonusText.resizeTextForBestFit = true;
            ui.bonusText.resizeTextMinSize = 10;
            ui.bonusText.resizeTextMaxSize = 15;
            ui.bonusText.verticalOverflow = VerticalWrapMode.Truncate;
            AddOutline(ui.bonusText.gameObject, 1f);

            // the line of the bag: count on the left, page and the scroll arrows of Nro on the right
            float bagLine = CT + 5 * G(24) + 1;
            ui.bagText = Label("BagText", body, "", 15, new Color(0.35f, 0.20f, 0.08f), Vector2.zero, Vector2.zero, true);
            TopLeft(ui.bagText.rectTransform, 6, bagLine, 190, 24);
            ui.bagText.alignment = TextAnchor.MiddleLeft;
            ui.bagPageText = Label("BagPage", body, "", 15, new Color(0.35f, 0.20f, 0.08f), Vector2.zero, Vector2.zero, true);
            TopLeft(ui.bagPageText.rectTransform, 196, bagLine, 54, 24);
            ui.bagPageText.alignment = TextAnchor.MiddleRight;
            ui.bagPrev = ScrollArrow("BagPrev", body, 254, bagLine, true);
            ui.bagNext = ScrollArrow("BagNext", body, 286, bagLine, false);

            // bag: rows of 5 slots down to the money strip (the run time hides the rows a short screen has no room for); the first slot is always the Potara earring
            var bagImages = new List<Image>();
            var bagIcons = new List<Image>();
            var bagLabels = new List<Text>();
            var bagButtons = new List<Button>();
            for (int j = 0; j < BagRows; j++)
                for (int i = 0; i < 5; i++)
                {
                    var bagSlot = NroCell("Bag" + j + i, body, G(2) + i * G(34), bagLine + 26 + j * G(24), "nro_cell_bag", out Text bagLabel);
                    bagSlot.raycastTarget = true;
                    var button = bagSlot.gameObject.AddComponent<Button>();
                    button.targetGraphic = bagSlot;
                    var icon = Image("BagIcon", bagSlot.transform, "white", false);
                    Stretch(icon.rectTransform, 8, 5);
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    icon.enabled = false;
                    bagLabel.transform.SetAsLastSibling();
                    bagLabel.alignment = TextAnchor.LowerRight;
                    bagLabel.fontSize = 13;
                    bagLabel.color = StatYellow;
                    AddOutline(bagLabel.gameObject, 1f);
                    if (i == 0 && j == 0)
                    {
                        ui.earringSlot = button;
                        ui.earringSlotImage = bagSlot;
                        ui.earringIcon = icon;
                        ui.earringCaption = bagLabel;
                        continue;
                    }
                    bagImages.Add(bagSlot);
                    bagIcons.Add(icon);
                    bagLabels.Add(bagLabel);
                    bagButtons.Add(button);
                }
            ui.bagSlotImages = bagImages.ToArray();
            ui.bagIcons = bagIcons.ToArray();
            ui.bagCaptions = bagLabels.ToArray();
            ui.bagButtons = bagButtons.ToArray();

            // money: gold at x 11 and gems at x 75 of Nro, yellow numbers
            var goldIcon = Image("GoldIcon", footer, "nro_gold", false);
            TopLeft(goldIcon.rectTransform, G(11) - 12, 6, 24, 18);
            goldIcon.preserveAspect = true;
            goldIcon.raycastTarget = false;
            ui.goldText = Label("GoldText", footer, "0", 15, StatYellow, Vector2.zero, Vector2.zero, true);
            TopLeft(ui.goldText.rectTransform, G(24), 2, 100, G(15) - 2);
            ui.goldText.alignment = TextAnchor.MiddleLeft;
            AddOutline(ui.goldText.gameObject, 1f);
            var gemIcon = Image("GemIcon", footer, "nro_gem", false);
            TopLeft(gemIcon.rectTransform, G(75) - 11, 6, 22, 18);
            gemIcon.preserveAspect = true;
            gemIcon.raycastTarget = false;
            ui.gemText = Label("GemText", footer, "0", 15, StatYellow, Vector2.zero, Vector2.zero, true);
            TopLeft(ui.gemText.rectTransform, G(85), 2, 140, G(15) - 2);
            ui.gemText.alignment = TextAnchor.MiddleLeft;
            AddOutline(ui.gemText.gameObject, 1f);

            // ---- the free area belongs to the village: only the fusion note (top right) and the toast (bottom middle) stay
            ui.potaraText = Label("PotaraText", root, "", 22, Color.white, Vector2.zero, new Vector2(520, 32), false);
            AnchorTopRight(ui.potaraText.rectTransform, new Vector2(-24, -20));
            ui.potaraText.alignment = TextAnchor.MiddleRight;
            AddOutline(ui.potaraText.gameObject, 1f);
            ui.toastText = Label("Toast", root, "", 26, Color.white, Vector2.zero, new Vector2(700, 36), true);
            var tr = ui.toastText.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0f);
            tr.pivot = new Vector2(0.5f, 0f);
            tr.anchoredPosition = new Vector2(PW * 0.5f, 260f);
            AddOutline(ui.toastText.gameObject, 1f);

            // ---- the shop of an NPC (Urôn): a second panel on the left, under the popup (the menu panel moves to the right while it is open)
            ui.shop = BuildShop(root, ui);

            // ---- the popup (skin, mail, notice), the Chức năng tab and its password form
            ui.popup = BuildPopup(root);
            ui.shop.popup = ui.popup;
            ui.functions = BuildFunctions(panel, root, ui);
            ui.functions.popup = ui.popup;
            ui.auras = BuildAuras(panel, ui);
            ui.skills = BuildSkills(panel, ui);

            // ---- chooser of the fusion partner (hidden until the earring is used with more than one possible partner)
            var chooser = Stretched("Chooser", root);
            var dim = chooser.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            ui.chooserRoot = chooser.gameObject;
            var box = Image("Box", chooser, "panel_login", true);
            Center(box.rectTransform, Vector2.zero, new Vector2(560, 300));
            ui.chooserTitle = Label("Title", box.transform, "", 30, new Color(0.45f, 0.12f, 0.05f), Vector2.zero, Vector2.zero, true);
            TopLeft(ui.chooserTitle.rectTransform, 20, 24, 520, 50);
            ui.chooserButtons = new Button[2];
            ui.chooserLabels = new Text[2];
            for (int i = 0; i < 2; i++)
            {
                ui.chooserButtons[i] = OrangeButton("Choice" + i, box.transform, new Vector2(0, 0), new Vector2(240, 60), "", 26, out ui.chooserLabels[i]);
                var cr = ui.chooserButtons[i].GetComponent<RectTransform>();
                cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 1f);
                cr.pivot = new Vector2(0.5f, 1f);
                cr.anchoredPosition = new Vector2(i == 0 ? -135f : 135f, -100f);
            }
            ui.chooserCancel = OrangeButton("Cancel", box.transform, new Vector2(0, 0), new Vector2(200, 54), "Hủy", 24, out _);
            var cc = ui.chooserCancel.GetComponent<RectTransform>();
            cc.anchorMin = cc.anchorMax = new Vector2(0.5f, 0f);
            cc.pivot = new Vector2(0.5f, 0f);
            cc.anchoredPosition = new Vector2(0f, 30f);
            chooser.gameObject.SetActive(false);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, MenuScenePath);
            PutFirstInBuild();
            Debug.Log("Menu scene built: " + MenuScenePath);
        }

        static readonly Color Dark = new Color(0.30f, 0.14f, 0.05f);
        static readonly Color Brown = new Color(0.45f, 0.30f, 0.15f);

        // ---- the Nro panel (Panel.cs, Paint.paintFrameSimple, PopUp.paintPopUp): sizes in Nro pixels times G, colours as Nro draws them
        const float PW = 320f;                                             // panel width: Nro's 176 px at 25% of the 1280 reference width
        static float G(float nroPx) { return nroPx * PW / 176f; }
        static readonly float TabY = G(50) + 19f;                          // under the header (50) and the experience bar
        static readonly float CT = TabY + G(25) + 5f;                      // top of the list (yScroll of Nro): under the tabs and their orange line
        const int BagRows = 6;
        static readonly float PopupW = G(180), PopupH = 300f, PopupButtonW = 102f, PopupButtonH = 92f;
        static readonly Color NroPanel = Hex(0xDAC9B4), NroEdge = Hex(0x664400), NroHeader = Hex(0x987B55), NroBag = Hex(0xB49F84),
                              NroLine = Hex(0xCE5E0C), NroRow = Hex(0xE7DFD2), NroRowPressed = Hex(0xF9FF4A);

        static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        }

        /// <summary>A slot of the Nro inventory (fill, gap, white outline at 50%) placed from the top-left corner: 35 x 24 Nro px with the outline, a faint caption.</summary>
        static Image NroCell(string name, Transform parent, float x, float y, string sprite, out Text text)
        {
            var img = Image(name, parent, sprite, true);
            TopLeft(img.rectTransform, x, y, G(35), G(24));
            img.raycastTarget = false;
            text = Label("Label", img.transform, "", 12, new Color(1f, 1f, 1f, 0.45f), Vector2.zero, Vector2.zero, false);
            Stretch(Rect(text.gameObject), 6, 4);
            return img;
        }

        /// <summary>The blue scroll arrow of the Nro list (a 9 x 6 px triangle) with a larger area to tap.</summary>
        static Button ScrollArrow(string name, Transform parent, float x, float y, bool up)
        {
            var hit = Image(name, parent, "white", false);
            TopLeft(hit.rectTransform, x, y, 30, 24);
            hit.color = new Color(1f, 1f, 1f, 0f);
            var b = hit.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            var arrow = Image("Arrow", hit.transform, "nro_scroll_down", false);
            Center(arrow.rectTransform, Vector2.zero, new Vector2(G(9) + 2, G(6) + 2));
            arrow.raycastTarget = false;
            if (up) arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);
            return b;
        }

        /// <summary>A row of the Nro list (tools, account): light background, bold dark name in the middle, yellow while pressed.</summary>
        static Button RowButton(string name, Transform parent, float x, float y, float w, float h, string text, int fontSize)
        {
            var img = Image(name, parent, "white", false);
            TopLeft(img.rectTransform, x, y, w, h);
            img.color = NroRow;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.pressedColor = NroRowPressed;
            colors.disabledColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            b.colors = colors;
            var label = Label("Text", img.transform, text, fontSize, Dark, Vector2.zero, Vector2.zero, true);
            Stretch(Rect(label.gameObject), 4, 2);
            return b;
        }

        /// <summary>A command button of Nro (rounded square, green or orange) placed from the top-left corner.</summary>
        static Button NroButton(string name, Transform parent, float x, float y, float w, float h, string text, int fontSize, bool green)
        {
            var img = Image(name, parent, green ? "nro_btn_green" : "nro_btn_orange", true);
            TopLeft(img.rectTransform, x, y, w, h);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var label = Label("Text", img.transform, text, fontSize, Dark, Vector2.zero, Vector2.zero, true);
            Stretch(Rect(label.gameObject), 6, 4);
            return b;
        }

        static Button ArrowButton(string name, Transform parent, float x, float y, bool left)
        {
            var img = Image(name, parent, "arrow_orange", false);
            TopLeft(img.rectTransform, x, y, 30, 24);
            img.rectTransform.pivot = new Vector2(0.5f, 0.5f);                  // turn around the centre
            img.rectTransform.anchoredPosition = new Vector2(x + 15f, -(y + 12f));
            img.preserveAspect = true;
            if (left) img.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            return b;
        }

        /// <summary>An orange button placed from the top-left corner of its parent.</summary>
        static Button PlacedButton(string name, Transform parent, float x, float y, float w, float h, string text, int fontSize, out Text label)
        {
            var b = OrangeButton(name, parent, Vector2.zero, new Vector2(w, h), text, fontSize, out label);
            TopLeft(b.GetComponent<RectTransform>(), x, y, w, h);
            return b;
        }

        static Text PlacedText(string name, Transform parent, string text, int size, Color color, float x, float y, float w, float h, TextAnchor align, bool bold = true)
        {
            var t = Label(name, parent, text, size, color, Vector2.zero, Vector2.zero, bold);
            TopLeft(t.rectTransform, x, y, w, h);
            t.alignment = align;
            return t;
        }

        /// <summary>A text field placed from the top-left corner of its parent.</summary>
        static InputField FieldAt(string name, Transform parent, float x, float y, float w, float h, string hint, int limit, bool password)
        {
            var bg = Image(name, parent, "field_light", true);
            TopLeft(bg.rectTransform, x, y, w, h);
            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(bg.transform, false);
            Stretch(Rect(textGo), 18, 4);
            var text = textGo.GetComponent<Text>();
            text.font = BuiltinFont;
            text.fontSize = 24;
            text.fontStyle = FontStyle.Bold;
            text.color = Ink;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(bg.transform, false);
            Stretch(Rect(phGo), 18, 4);
            var ph = phGo.GetComponent<Text>();
            ph.font = BuiltinFont;
            ph.fontSize = 24;
            ph.fontStyle = FontStyle.Bold;
            ph.color = new Color(1f, 0.97f, 0.88f, 0.95f);
            ph.alignment = TextAnchor.MiddleLeft;
            ph.text = hint;
            var input = bg.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = text;
            input.placeholder = ph;
            input.characterLimit = limit;
            input.lineType = InputField.LineType.SingleLine;
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            var colors = input.colors;
            colors.selectedColor = new Color(0.82f, 1f, 0.72f, 1f);
            input.colors = colors;
            return input;
        }

        /// <summary>
        /// The item popup of Nro: a white box with a black edge and red corners above the row of command buttons (Menu.cs: 60 x 60 rounded squares at the bottom of the
        /// screen, the first one green), the picture and the name at the top, the bonus lines on the right, a line and the description. No dimming and no close button:
        /// a tap anywhere else closes it, as in Nro. BbPopup places the box and the buttons at run time.
        /// </summary>
        static BbPopup BuildPopup(Transform canvasRoot)
        {
            var dimRect = Stretched("Popup", canvasRoot);
            var dim = dimRect.gameObject.AddComponent<Image>();
            dim.sprite = Sprite("white");
            dim.color = new Color(0f, 0f, 0f, 0f);                             // invisible: it only catches the tap outside the box
            var popup = dimRect.gameObject.AddComponent<BbPopup>();
            popup.root = dimRect.gameObject;
            popup.close = dimRect.gameObject.AddComponent<Button>();
            popup.close.targetGraphic = dim;
            popup.close.transition = Selectable.Transition.None;

            var box = Image("Box", dimRect, "nro_popup", true);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = Vector2.zero;
            box.rectTransform.pivot = Vector2.zero;
            box.rectTransform.sizeDelta = new Vector2(PopupW, PopupH);           // 180 Nro px wide, a little wider than the panel as in Nro
            box.rectTransform.anchoredPosition = new Vector2(G(3), 100f);
            box.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;      // a tap on the box itself does not close it
            popup.box = box.rectTransform;
            popup.icon = Image("Icon", box.transform, "white", false);
            TopLeft(popup.icon.rectTransform, 10, 8, 40, 40);
            popup.icon.preserveAspect = true;
            popup.icon.raycastTarget = false;
            popup.title = PlacedText("Title", box.transform, "", 19, Dark, 56, 8, PopupW - 66, 40, TextAnchor.MiddleRight);
            popup.title.horizontalOverflow = HorizontalWrapMode.Wrap;
            popup.title.resizeTextForBestFit = true;
            popup.title.resizeTextMinSize = 13;
            popup.title.resizeTextMaxSize = 19;
            popup.title.verticalOverflow = VerticalWrapMode.Truncate;

            popup.gifts = new UnityEngine.UI.Image[5];
            popup.giftCaptions = new Text[5];
            for (int i = 0; i < 5; i++)
            {
                var cell = new GameObject("Gift" + i, typeof(RectTransform));
                cell.transform.SetParent(box.transform, false);
                TopLeft(cell.GetComponent<RectTransform>(), 8 + i * 62, 52, 60, 70);
                popup.gifts[i] = Image("Icon", cell.transform, "white", false);
                TopLeft(popup.gifts[i].rectTransform, 12, 0, 36, 36);
                popup.gifts[i].preserveAspect = true;
                popup.gifts[i].raycastTarget = false;
                popup.giftCaptions[i] = PlacedText("Caption", cell.transform, "", 13, Dark, 0, 38, 60, 30, TextAnchor.UpperCenter);
            }

            var back = Flat("PreviewBack", box.transform, 8, 52, 112, 152, new Color(0.97f, 0.94f, 0.88f));
            popup.previewRoot = back.gameObject;
            back.gameObject.AddComponent<RectMask2D>();                         // a big aura does not spill over the text of the popup
            var previewGo = new GameObject("Preview", typeof(RectTransform), typeof(BbCharacterView));
            previewGo.transform.SetParent(back.transform, false);
            var previewRect = Rect(previewGo);
            previewRect.anchorMin = previewRect.anchorMax = new Vector2(0f, 1f);
            previewRect.pivot = new Vector2(0.5f, 0f);
            previewRect.anchoredPosition = new Vector2(56f, -142f);           // the feet, in the lower part of the backdrop
            previewRect.sizeDelta = new Vector2(10f, 10f);
            popup.preview = previewGo.GetComponent<BbCharacterView>();
            popup.preview.uiScale = 2f;
            back.gameObject.SetActive(false);

            popup.lines = PlacedText("Lines", box.transform, "", 17, Dark, 8, 52, PopupW - 16, 152, TextAnchor.UpperRight);
            popup.lines.resizeTextForBestFit = true;
            popup.lines.resizeTextMinSize = 12;
            popup.lines.resizeTextMaxSize = 17;
            popup.lines.supportRichText = true;
            popup.lines.lineSpacing = 1.05f;
            popup.lines.verticalOverflow = VerticalWrapMode.Truncate;
            Flat("Separator", box.transform, 14, 210, PopupW - 28, 2, new Color(0f, 0f, 0f, 1f));
            popup.description = PlacedText("Description", box.transform, "", 15, Dark, 12, 216, PopupW - 24, PopupH - 222, TextAnchor.UpperCenter, false);
            popup.description.verticalOverflow = VerticalWrapMode.Truncate;
            popup.description.supportRichText = false;

            // the command buttons: Nro's rounded squares, three to a row at the bottom of the panel (BbPopup.Show places them)
            popup.buttons = new Button[6];
            popup.buttonImages = new UnityEngine.UI.Image[6];
            popup.buttonLabels = new Text[6];
            for (int i = 0; i < 6; i++)
            {
                var img = Image("Button" + i, dimRect, "nro_btn_orange", true);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                img.rectTransform.pivot = Vector2.zero;
                img.rectTransform.sizeDelta = new Vector2(PopupButtonW, PopupButtonH);
                var b = img.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                var colors = b.colors;
                colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 1f);            // grey, not see-through: the money strip lies under the buttons
                b.colors = colors;
                var label = Label("Text", img.transform, "", 17, Dark, Vector2.zero, Vector2.zero, true);
                Stretch(Rect(label.gameObject), 8, 6);
                label.lineSpacing = 0.95f;
                popup.buttons[i] = b;
                popup.buttonImages[i] = img;
                popup.buttonLabels[i] = label;
            }
            popup.normalSprite = Sprite("nro_btn_orange");
            popup.primarySprite = Sprite("nro_btn_green");
            popup.areaWidth = PW;
            dimRect.gameObject.SetActive(false);
            return popup;
        }

        /// <summary>The Aura tab: a header right of the portrait, 4 x 5 boxes (bag slots of Nro, a still picture of the aura in each), the page arrows under them.</summary>
        static BbMenuAuras BuildAuras(Transform panel, BbMenuUi ui)
        {
            var go = new GameObject("Auras", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var stretch = Rect(go);
            stretch.anchorMin = Vector2.zero;
            stretch.anchorMax = Vector2.one;
            stretch.offsetMin = stretch.offsetMax = Vector2.zero;
            var auras = go.AddComponent<BbMenuAuras>();
            auras.root = go;
            auras.slotEmpty = ui.bagCell;
            auras.slotWorn = ui.cellSelected;

            auras.headerText = Label("AuraHeader", go.transform, "", 15, StatYellow, Vector2.zero, Vector2.zero, false);
            TopLeft(auras.headerText.rectTransform, G(60), 3, PW - G(60) - 4, G(50) - 5);
            auras.headerText.alignment = TextAnchor.MiddleLeft;
            auras.headerText.lineSpacing = 1.05f;
            auras.headerText.resizeTextForBestFit = true;
            auras.headerText.resizeTextMinSize = 10;
            auras.headerText.resizeTextMaxSize = 15;
            auras.headerText.verticalOverflow = VerticalWrapMode.Truncate;
            AddOutline(auras.headerText.gameObject, 1f);

            const int cols = 4, rows = 5, count = cols * rows;
            const float cell = 74f, pitch = 78f;
            auras.slots = new Button[count];
            auras.slotImages = new UnityEngine.UI.Image[count];
            auras.thumbs = new UnityEngine.UI.Image[count];
            auras.thumbGroups = new CanvasGroup[count];
            auras.slotLabels = new Text[count];
            for (int i = 0; i < count; i++)
            {
                int col = i % cols, row = i / cols;
                var box = Image("Aura" + i, go.transform, "nro_cell_bag", true);
                TopLeft(box.rectTransform, 4 + col * pitch, CT + 2 + row * pitch, cell, cell);
                var button = box.gameObject.AddComponent<Button>();
                button.targetGraphic = box;
                var thumbGo = new GameObject("Thumb", typeof(RectTransform), typeof(CanvasGroup), typeof(UnityEngine.UI.Image));
                thumbGo.transform.SetParent(box.transform, false);
                var tr = Rect(thumbGo);
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0f);
                tr.pivot = new Vector2(0.5f, 0f);
                tr.anchoredPosition = new Vector2(0f, 13f);                      // the aura stands above the label
                tr.sizeDelta = new Vector2(62f, 56f);
                thumbGo.GetComponent<CanvasGroup>().blocksRaycasts = false;
                var thumbImage = thumbGo.GetComponent<UnityEngine.UI.Image>();
                thumbImage.preserveAspect = true;
                thumbImage.raycastTarget = false;
                thumbImage.enabled = false;
                var label = Label("Label", box.transform, "", 12, Color.white, Vector2.zero, Vector2.zero, true);
                label.rectTransform.anchorMin = new Vector2(0f, 0f);
                label.rectTransform.anchorMax = new Vector2(1f, 0f);
                label.rectTransform.pivot = new Vector2(0.5f, 0f);
                label.rectTransform.offsetMin = new Vector2(3f, 3f);
                label.rectTransform.offsetMax = new Vector2(-3f, 17f);
                label.raycastTarget = false;
                AddOutline(label.gameObject, 1f);
                auras.slots[i] = button;
                auras.slotImages[i] = box;
                auras.thumbs[i] = thumbImage;
                auras.thumbGroups[i] = thumbGo.GetComponent<CanvasGroup>();
                auras.slotLabels[i] = label;
            }
            float pageY = CT + 2 + rows * pitch + 4;
            auras.pageText = Label("AuraPage", go.transform, "", 15, new Color(0.35f, 0.20f, 0.08f), Vector2.zero, Vector2.zero, true);
            TopLeft(auras.pageText.rectTransform, PW * 0.5f - 40, pageY, 80, 26);
            auras.prev = ArrowButton("AuraPrev", go.transform, PW * 0.5f - 76, pageY + 1, true);
            auras.next = ArrowButton("AuraNext", go.transform, PW * 0.5f + 46, pageY + 1, false);
            go.SetActive(false);
            return auras;
        }

        /// <summary>The Kỹ năng tab: a header right of the portrait and a scrolling list down to the money strip (BbMenuSkills fills it).</summary>
        static BbMenuSkills BuildSkills(Transform panel, BbMenuUi ui)
        {
            var rootRect = Group("Skills", panel);
            var k = rootRect.gameObject.AddComponent<BbMenuSkills>();
            k.root = rootRect.gameObject;
            k.font = BuiltinFont;
            k.rowSprite = Sprite("white");
            k.iconFrame = Sprite("nro_skill");
            k.buttonSprite = Sprite("nro_btn_orange");
            k.popup = ui.popup;
            k.headerText = PlacedText("Header", rootRect, "", 15, StatYellow, G(60), 3, PW - G(60) - 4, G(50) - 5, TextAnchor.MiddleLeft);
            k.headerText.resizeTextForBestFit = true;
            k.headerText.resizeTextMinSize = 10;
            k.headerText.resizeTextMaxSize = 15;
            k.headerText.verticalOverflow = VerticalWrapMode.Truncate;
            AddOutline(k.headerText.gameObject, 1f);
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(rootRect, false);
            var sr = scrollGo.GetComponent<RectTransform>();
            sr.anchorMin = Vector2.zero;
            sr.anchorMax = Vector2.one;
            sr.offsetMin = new Vector2(2f, G(15) + 2f);
            sr.offsetMax = new Vector2(-2f, -CT);
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scrollGo.transform, false);
            Stretch(Rect(viewport), 0, 0);
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var cr = Rect(content);
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(0.5f, 1f);
            cr.anchoredPosition = Vector2.zero;
            cr.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 2;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = Rect(viewport);
            scroll.content = cr;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            k.scroll = scroll;
            k.content = cr;
            rootRect.gameObject.SetActive(false);
            return k;
        }

        /// <summary>
        /// The shop panel of an NPC (Nro Panel.cs, typeShop): the same frame as the menu panel on the left of the screen (hidden away until a shop opens): the portrait of the NPC and its
        /// name at the top, up to four tabs, the list of items (made at run time, BbShopPanel) and the money strip.
        /// </summary>
        static BbShopPanel BuildShop(Transform canvasRoot, BbMenuUi ui)
        {
            var panelGo = new GameObject("ShopPanel", typeof(RectTransform), typeof(Image), typeof(BbShopPanel));
            panelGo.transform.SetParent(canvasRoot, false);
            var rect = Rect(panelGo);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(PW, 0f);
            rect.anchoredPosition = new Vector2(-PW, 0f);
            var bg = panelGo.GetComponent<Image>();
            bg.sprite = Sprite("white");
            bg.color = NroPanel;
            var panel = panelGo.transform;
            var shop = panelGo.GetComponent<BbShopPanel>();
            shop.root = panelGo;
            shop.panelRect = rect;
            shop.font = BuiltinFont;
            shop.menu = ui;
            var edge = Image("Edge", panel, "white", false);
            edge.rectTransform.anchorMin = new Vector2(1f, 0f);
            edge.rectTransform.anchorMax = new Vector2(1f, 1f);
            edge.rectTransform.pivot = new Vector2(1f, 0.5f);
            edge.rectTransform.sizeDelta = new Vector2(2f, 0f);
            edge.rectTransform.anchoredPosition = Vector2.zero;
            edge.color = NroEdge;
            edge.raycastTarget = false;

            Flat("HeaderBg", panel, 0, 0, PW, G(50), NroHeader);
            var footerGo = new GameObject("Footer", typeof(RectTransform));
            footerGo.transform.SetParent(panel, false);
            var footerRect = Rect(footerGo);
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.sizeDelta = new Vector2(0f, G(15));
            footerRect.anchoredPosition = Vector2.zero;
            Stretch(Flat("FooterBg", footerGo.transform, 0, 0, PW, G(15), NroBag).rectTransform, 0, 0);
            Flat("FooterLine", footerGo.transform, 0, 0, PW, 2, NroLine);
            var goldIcon = Image("GoldIcon", footerGo.transform, "nro_gold", false);
            TopLeft(goldIcon.rectTransform, G(11) - 12, 6, 24, 18);
            goldIcon.preserveAspect = true;
            goldIcon.raycastTarget = false;
            shop.goldText = Label("GoldText", footerGo.transform, "0", 15, StatYellow, Vector2.zero, Vector2.zero, true);
            TopLeft(shop.goldText.rectTransform, G(24), 2, 100, G(15) - 2);
            shop.goldText.alignment = TextAnchor.MiddleLeft;
            AddOutline(shop.goldText.gameObject, 1f);
            var gemIcon = Image("GemIcon", footerGo.transform, "nro_gem", false);
            TopLeft(gemIcon.rectTransform, G(75) - 11, 6, 22, 18);
            gemIcon.preserveAspect = true;
            gemIcon.raycastTarget = false;
            shop.gemText = Label("GemText", footerGo.transform, "0", 15, StatYellow, Vector2.zero, Vector2.zero, true);
            TopLeft(shop.gemText.rectTransform, G(85), 2, 140, G(15) - 2);
            shop.gemText.alignment = TextAnchor.MiddleLeft;
            AddOutline(shop.gemText.gameObject, 1f);
            shop.gemSprite = Sprite("nro_gem");

            // header: the portrait of the NPC on the left, its name and what it sells to the right, the close button
            shop.portrait = Image("Portrait", panel, "white", false);
            TopLeft(shop.portrait.rectTransform, 2, 2, G(50) - 4, G(50) - 4);
            shop.portrait.preserveAspect = true;
            shop.portrait.raycastTarget = false;
            shop.headerText = Label("HeaderText", panel, "", 16, StatYellow, Vector2.zero, Vector2.zero, false);
            TopLeft(shop.headerText.rectTransform, G(60), 3, PW - G(60) - G(17) - 8, G(50) - 5);
            shop.headerText.alignment = TextAnchor.MiddleLeft;
            shop.headerText.lineSpacing = 1.1f;
            shop.headerText.resizeTextForBestFit = true;
            shop.headerText.resizeTextMinSize = 11;
            shop.headerText.resizeTextMaxSize = 16;
            shop.headerText.verticalOverflow = VerticalWrapMode.Truncate;
            AddOutline(shop.headerText.gameObject, 1f);
            var close = Image("Close", panel, "nro_close", false);
            TopLeft(close.rectTransform, PW - G(17) - 4, 4, G(17), G(17));
            shop.closeButton = close.gameObject.AddComponent<Button>();
            shop.closeButton.targetGraphic = close;

            // tabs (up to four, spread at run time) and the orange line under them
            shop.tabIdle = Sprite("nro_tab_idle");
            shop.tabOn = Sprite("nro_tab_on");
            float tabY = G(50) + 4f;
            shop.tabs = new Button[4];
            shop.tabImages = new UnityEngine.UI.Image[4];
            shop.tabLabels = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                var tab = Image("Tab" + i, panel, "nro_tab_idle", true);
                TopLeft(tab.rectTransform, 4 + i * 78, tabY, 76, G(25));
                var b = tab.gameObject.AddComponent<Button>();
                b.targetGraphic = tab;
                var label = Label("Text", tab.transform, "", 15, new Color(0.45f, 0.18f, 0.05f), Vector2.zero, Vector2.zero, false);
                Stretch(Rect(label.gameObject), 1, 1);
                label.lineSpacing = 0.85f;
                shop.tabs[i] = b;
                shop.tabImages[i] = tab;
                shop.tabLabels[i] = label;
            }
            Flat("TabLine", panel, 2, tabY + G(25) + 1, PW - 4, 2, NroLine);

            // the list of items
            shop.rowSprite = Sprite("white");
            shop.cellSprite = Sprite("nro_cell_body");
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(panel, false);
            var sr = scrollGo.GetComponent<RectTransform>();
            sr.anchorMin = Vector2.zero;
            sr.anchorMax = Vector2.one;
            sr.offsetMin = new Vector2(2f, G(15) + 2f);
            sr.offsetMax = new Vector2(-2f, -(tabY + G(25) + 5f));
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scrollGo.transform, false);
            Stretch(Rect(viewport), 0, 0);
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var cr = Rect(content);
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(0.5f, 1f);
            cr.anchoredPosition = Vector2.zero;
            cr.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 2;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = Rect(viewport);
            scroll.content = cr;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            shop.scroll = scroll;
            shop.content = cr;
            panelGo.SetActive(false);
            return shop;
        }

        /// <summary>The Chức năng tab as the tool list of Nro (paintTools): full-width light rows with the name centred; the mail and notice lists, the account and the sprite scale.</summary>
        static BbMenuFunctions BuildFunctions(Transform panel, Transform canvasRoot, BbMenuUi ui)
        {
            var rootRect = Group("Functions", panel);
            var f = rootRect.gameObject.AddComponent<BbMenuFunctions>();
            f.root = rootRect.gameObject;
            f.menu = ui;
            f.goldSprite = Sprite("nro_gold");
            f.gemSprite = Sprite("nro_gem");

            f.headerText = PlacedText("Header", rootRect, "", 16, StatYellow, G(60), 3, PW - G(60) - 4, G(50) - 5, TextAnchor.MiddleLeft);
            f.headerText.resizeTextForBestFit = true;
            f.headerText.resizeTextMinSize = 11;
            f.headerText.resizeTextMaxSize = 16;
            f.headerText.verticalOverflow = VerticalWrapMode.Truncate;
            AddOutline(f.headerText.gameObject, 1f);
            f.backButton = RowButton("Back", rootRect, 2, CT, 92, G(24) - 2, "‹ Quay lại", 15);

            // menu: one row per function (Nro: ITEM_HEIGHT 25, colour #E7DFD2, bold dark name in the middle)
            var menuView = Group("MenuView", rootRect);
            f.menuView = menuView.gameObject;
            f.mailButton = RowButton("MailButton", menuView, 2, CT + 0 * G(25), PW - 4, G(24), "Hòm thư", 18);
            f.noticeButton = RowButton("NoticeButton", menuView, 2, CT + 1 * G(25), PW - 4, G(24), "Thông báo", 18);
            f.accountButton = RowButton("AccountButton", menuView, 2, CT + 2 * G(25), PW - 4, G(24), "Tài khoản", 18);
            f.configButton = RowButton("ConfigButton", menuView, 2, CT + 3 * G(25), PW - 4, G(24), "Cấu hình", 18);
            var badge = Image("Badge", f.mailButton.transform, "dot", false);
            TopLeft(badge.rectTransform, PW - 4 - 42, 4, 34, 34);
            badge.color = new Color(0.85f, 0.10f, 0.05f);
            badge.raycastTarget = false;
            f.mailBadge = badge.gameObject;
            f.mailBadgeText = PlacedText("Count", badge.transform, "", 17, Color.white, 0, 0, 34, 34, TextAnchor.MiddleCenter);
            AddOutline(f.mailBadgeText.gameObject, 1f);

            // list: title, "take all", scroll area with one row per mail or notice, down to the money strip
            var listView = Group("ListView", rootRect);
            f.listView = listView.gameObject;
            f.listTitle = PlacedText("Title", listView, "", 19, Dark, 96, CT, 120, G(24) - 2, TextAnchor.MiddleCenter);
            f.actionButton = RowButton("Action", listView, 218, CT, PW - 220, G(24) - 2, "Nhận tất cả", 14);
            f.actionLabel = f.actionButton.GetComponentInChildren<Text>();
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(listView, false);
            var sr = scrollGo.GetComponent<RectTransform>();
            sr.anchorMin = Vector2.zero;
            sr.anchorMax = Vector2.one;
            sr.offsetMin = new Vector2(2f, G(15) + 2f);
            sr.offsetMax = new Vector2(-2f, -(CT + G(24) + 2f));
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scrollGo.transform, false);
            Stretch(Rect(viewport), 0, 0);
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var cr = Rect(content);
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(0.5f, 1f);
            cr.anchoredPosition = Vector2.zero;
            cr.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 2;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = Rect(viewport);
            scroll.content = cr;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            f.scroll = scroll;
            f.content = cr;
            f.emptyText = PlacedText("Empty", listView, "", 19, Brown, 4, CT + 140, PW - 8, 60, TextAnchor.MiddleCenter);

            var row = Image("RowTemplate", content.transform, "white", false);
            row.color = NroRow;
            var rowButton = row.gameObject.AddComponent<Button>();
            rowButton.targetGraphic = row;
            var rowColors = rowButton.colors;
            rowColors.pressedColor = NroRowPressed;
            rowButton.colors = rowColors;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
            var rowTitle = PlacedText("Title", row.transform, "", 17, Dark, 10, 5, 236, 26, TextAnchor.MiddleLeft);
            rowTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            PlacedText("Sub", row.transform, "", 14, Brown, 10, 32, 236, 22, TextAnchor.MiddleLeft, false);
            var dot = Image("Dot", row.transform, "dot", false);
            TopLeft(dot.rectTransform, PW - 4 - 24, 8, 14, 14);
            dot.color = new Color(0.85f, 0.10f, 0.05f);
            dot.raycastTarget = false;
            var gift = Image("Gift", row.transform, "btn_orange", true);
            TopLeft(gift.rectTransform, PW - 4 - 66, 30, 58, 24);
            gift.raycastTarget = false;
            ButtonText(gift.transform, "QUÀ", 13);
            f.rowTemplate = row.gameObject;
            row.gameObject.SetActive(false);                     // copies are made from it at run time

            // account
            var accountView = Group("AccountView", rootRect);
            f.accountView = accountView.gameObject;
            f.accountInfo = PlacedText("Info", accountView, "", 19, Dark, 4, CT + G(25), PW - 8, 46, TextAnchor.MiddleCenter);
            f.changePasswordButton = RowButton("ChangePassword", accountView, 2, CT + 2 * G(25) + 8, PW - 4, G(24), "Đổi mật khẩu", 18);
            f.logoutButton = RowButton("Logout", accountView, 2, CT + 3 * G(25) + 8, PW - 4, G(24), "Đăng xuất", 18);

            // config: the sprite scale (the Nro command buttons, green for the scale in use)
            var configView = Group("ConfigView", rootRect);
            f.configView = configView.gameObject;
            PlacedText("Title", configView, "Độ phân giải hình ảnh", 19, Dark, 4, CT + G(25), PW - 8, 34, TextAnchor.MiddleCenter);
            string[] scaleNames = { "x1" + "\n" + "Nhẹ nhất", "x2", "x3", "x4" + "\n" + "Nét nhất" };
            f.scaleButtons = new Button[4];
            f.scaleImages = new UnityEngine.UI.Image[4];
            f.scaleLabels = new Text[4];
            float scaleTop = CT + G(25) + 40;
            for (int i = 0; i < 4; i++)
            {
                var img = Image("Scale" + (i + 1), configView, "nro_btn_orange", true);
                TopLeft(img.rectTransform, 10 + (i % 2) * 154, scaleTop + (i / 2) * 92, 146, 86);
                var b = img.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                f.scaleButtons[i] = b;
                f.scaleImages[i] = img;
                f.scaleLabels[i] = Label("Text", img.transform, scaleNames[i], 18, Dark, Vector2.zero, Vector2.zero, true);
                Stretch(Rect(f.scaleLabels[i].gameObject), 6, 4);
            }
            f.configStatus = PlacedText("Status", configView, "", 16, Dark, 4, scaleTop + 188, PW - 8, 32, TextAnchor.MiddleCenter);
            f.soundButton = RowButton("SoundButton", configView, 2, scaleTop + 292, PW - 4, G(24), "Âm thanh: Bật", 18);
            f.weatherButton = RowButton("WeatherButton", configView, 2, scaleTop + 342, PW - 4, G(24), "Thời tiết: Bật", 18);
            PlacedText("Note", configView, "x1 nhẹ nhất cho máy yếu, x4 nét nhất. Đổi sang độ phân giải chưa có sẽ tải thêm từ máy chủ.", 14, Brown, 8, scaleTop + 222, PW - 16, 60, TextAnchor.UpperCenter, false);

            // password form (on top of the panel)
            var formRect = Stretched("PasswordForm", canvasRoot);
            var dim = formRect.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.45f);
            f.passwordRoot = formRect.gameObject;
            var box = Image("Box", formRect, "nro_popup", true);
            TopLeft(box.rectTransform, G(3), 170, PopupW, 380);
            PlacedText("Title", box.transform, "Đổi mật khẩu", 22, Dark, 14, 12, PopupW - 28, 40, TextAnchor.MiddleCenter);
            f.oldField = FieldAt("OldPassword", box.transform, 16, 60, PopupW - 32, 54, "Mật khẩu hiện tại", 64, true);
            f.newField = FieldAt("NewPassword", box.transform, 16, 122, PopupW - 32, 54, "Mật khẩu mới (8-64 ký tự)", 64, true);
            f.confirmField = FieldAt("ConfirmPassword", box.transform, 16, 184, PopupW - 32, 54, "Nhập lại mật khẩu mới", 64, true);
            f.passwordStatus = PlacedText("Status", box.transform, "", 16, Dark, 16, 244, PopupW - 32, 40, TextAnchor.MiddleCenter);
            f.passwordOk = NroButton("Submit", box.transform, 16, 292, (PopupW - 40) * 0.5f, 72, "Đổi", 20, true);
            f.passwordCancel = NroButton("Cancel", box.transform, 24 + (PopupW - 40) * 0.5f, 292, (PopupW - 40) * 0.5f, 72, "Hủy", 20, false);
            formRect.gameObject.SetActive(false);
            rootRect.gameObject.SetActive(false);
            return f;
        }

        /// <summary>Top-left anchored placement inside the parent: x to the right, y downward.</summary>
        static void TopLeft(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        static void AnchorTopRight(RectTransform r, Vector2 pos)
        {
            r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 1f);
            r.anchoredPosition = pos;
        }

        /// <summary>A flat coloured rectangle (a tinted 8x8 white sprite).</summary>
        static Image Flat(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var img = Image(name, parent, "white", false);
            TopLeft(img.rectTransform, x, y, w, h);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A button in the free area on the right of the panel; y is measured up from the vertical centre.</summary>
        static Button RightButton(string name, Transform parent, float y, Vector2 size, string text, int fontSize)
        {
            var b = OrangeButton(name, parent, Vector2.zero, size, text, fontSize, out _);
            var r = b.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(-420f, y);            // centre of the area right of the 440 px panel on a 1280 px wide screen
            return b;
        }

        // ---------------------------------------------------------------------------------------------------------------- assets

        static void ConfigureSprites()
        {
            Border("btn_orange", new Vector4(12, 12, 12, 12), 2048);
            Border("panel_login", new Vector4(26, 26, 26, 26), 2048);
            Border("field_light", new Vector4(26, 20, 26, 20), 2048);
            Border("bar_capsule", new Vector4(6, 6, 6, 6), 2048);
            Border("logo", Vector4.zero, 1024);
            Border("tab_idle", new Vector4(10, 10, 10, 10), 256);
            Border("tab_on", new Vector4(10, 10, 10, 10), 256);
            Border("slot", new Vector4(8, 8, 8, 8), 256);
            Border("slot_filled", new Vector4(8, 8, 8, 8), 256);
            Border("portrait_frame", new Vector4(12, 12, 12, 12), 256);
            Border("white", Vector4.zero, 64);
            Border("icon_gold", Vector4.zero, 256);
            Border("icon_gem", Vector4.zero, 256);
            Border("arrow_orange", Vector4.zero, 256);
            Border("joy_base", Vector4.zero, 512);
            Border("joy_knob", Vector4.zero, 256);
            Border("glow", Vector4.zero, 512);
            Border("dot", Vector4.zero, 256);
            Border("space_bg", Vector4.zero, 2048, "jpg");
            Border("planet_a", Vector4.zero, 1024);
            Border("planet_b", Vector4.zero, 1024);
            const float nroPpu = 100f * 4f * 176f / PW;
            Border("nro_tab_idle", new Vector4(40, 40, 40, 40), 256, "png", nroPpu);
            Border("nro_tab_on", new Vector4(40, 40, 40, 40), 256, "png", nroPpu);
            Border("nro_cell_body", new Vector4(8, 8, 8, 8), 64, "png", nroPpu);
            Border("nro_cell_bag", new Vector4(8, 8, 8, 8), 64, "png", nroPpu);
            Border("nro_cell_sel", new Vector4(8, 8, 8, 8), 64, "png", nroPpu);
            Border("nro_popup", new Vector4(28, 24, 28, 24), 128, "png", nroPpu);
            Border("nro_btn_orange", new Vector4(72, 72, 72, 72), 256, "png", nroPpu);
            Border("nro_btn_green", new Vector4(72, 72, 72, 72), 256, "png", nroPpu);
            Border("nro_close", Vector4.zero, 128);
            Border("nro_gold", Vector4.zero, 64);
            Border("nro_gem", Vector4.zero, 64);
            Border("nro_menu_arrow", Vector4.zero, 128);
            Border("nro_scroll_down", Vector4.zero, 64);
            Border("nro_target_arrows", Vector4.zero, 256);
            Border("nro_hp_red", new Vector4(16, 0, 16, 0), 512, "png", 154f);      // 40 px high shown 26 high
            Border("nro_hp_lost", new Vector4(16, 0, 16, 0), 512, "png", 154f);
            foreach (string hud in new[] { "nro_hud_panel", "nro_hud_hp", "nro_hud_hp_lost", "nro_hud_ki", "nro_hud_ki_lost" }) Border(hud, Vector4.zero, 1024);
            foreach (string btn in new[] { "nro_fire", "nro_fire_press", "nro_focus", "nro_focus_press" }) Border(btn, Vector4.zero, 256);
            Border("nro_boss_frame", new Vector4(24, 24, 24, 24), 256);
            Border("nro_skill", Vector4.zero, 128);
            Border("nro_skill_on", Vector4.zero, 128);
            AssetDatabase.Refresh();
        }

        static void Border(string name, Vector4 border, int maxSize, string ext = "png", float pixelsPerUnit = 100f)
        {
            string path = SpriteDir + name + "." + ext;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spriteBorder = border;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.maxTextureSize = maxSize;
            imp.spritePixelsPerUnit = pixelsPerUnit;
            imp.SaveAndReimport();
        }

        static Sprite Sprite(string name, string ext = "png")
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + name + "." + ext);
        }

        /// <summary>Login first, Menu second, then the other scenes that were in the build before.</summary>
        static void PutFirstInBuild()
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(MenuScenePath, true)
            };
            foreach (var s in EditorBuildSettings.scenes) if (s.path != ScenePath && s.path != MenuScenePath) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ---------------------------------------------------------------------------------------------------------------- widgets

        const string FontPath = "Assets/BattleNet/UI/Fonts/barmeneb.ttf";   // alternatives in the same folder: Baloo2.ttf; Desktop/data/damage_font/fonts has chelthm.ttf and staccato.ttf

        /// <summary>Font of every text (barmeneb.ttf from the damage_font set, covers Vietnamese); falls back to Unity's built-in font when the file is missing.</summary>
        static Font BuiltinFont
        {
            get
            {
                var f = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
                return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        static RectTransform Rect(GameObject go) { return go.GetComponent<RectTransform>(); }

        static void Center(RectTransform r, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        /// <summary>Top centre of the parent, pivot at the top centre: pos.y is measured down from the parent's top edge.</summary>
        static void TopCenter(RectTransform r, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        static void Stretch(RectTransform r, float padX, float padY)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(padX, padY);
            r.offsetMax = new Vector2(-padX, -padY);
        }

        static RectTransform Stretched(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch(Rect(go), 0, 0);
            return Rect(go);
        }

        static RectTransform Group(string name, Transform parent) { return Stretched(name, parent); }

        static Image Image(string name, Transform parent, string sprite, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = Sprite(sprite);
            img.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            return img;
        }

        static void Logo(Transform parent, Vector2 pos, Vector2 size)
        {
            var logo = Image("Logo", parent, "logo", false);
            Center(logo.rectTransform, pos, size);
            logo.preserveAspect = true;
            logo.raycastTarget = false;
        }

        static void AddOutline(GameObject go, float distance = 2f)
        {
            var o = go.AddComponent<Outline>();
            o.effectColor = OutlineColor;
            o.effectDistance = new Vector2(distance, -distance);
        }

        /// <summary>Text: placed from the centre of its parent, or from the top edge when the parent is the form panel.</summary>
        static Text Label(string name, Transform parent, string text, int size, Color color, Vector2 pos, Vector2 boxSize, bool bold)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            if (parent.name == "Panel") TopCenter(Rect(go), pos, boxSize);
            else Center(Rect(go), pos, boxSize);
            var t = go.GetComponent<Text>();
            t.font = BuiltinFont;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static Button OrangeButton(string name, Transform parent, Vector2 pos, Vector2 size, string text, int fontSize, out Text label)
        {
            var image = Image(name, parent, "btn_orange", true);
            Center(image.rectTransform, pos, size);
            var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            var colors = b.colors;
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.9f);
            b.colors = colors;
            label = ButtonText(image.transform, text, fontSize);
            return b;
        }

        static Button MenuButton(string name, Transform parent, float y, string text, out Text label)
        {
            return OrangeButton(name, parent, new Vector2(0, y), new Vector2(400, 58), text, 27, out label);
        }

        static Text ButtonText(Transform parent, string text, int size)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Stretch(Rect(go), 0, 0);
            var t = go.GetComponent<Text>();
            t.font = BuiltinFont;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.color = ButtonText_;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            AddOutline(go, 1f);       // thin outline: the diacritics stay readable at this size
            return t;
        }

        static InputField Field(string name, Transform parent, float y, string hint, int limit, bool password)
        {
            var bg = Image(name, parent, "field_light", true);
            TopCenter(bg.rectTransform, new Vector2(0, y), new Vector2(520, 62));

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(bg.transform, false);
            Stretch(Rect(textGo), 22, 6);
            var text = textGo.GetComponent<Text>();
            text.font = BuiltinFont;
            text.fontSize = 28;
            text.fontStyle = FontStyle.Bold;
            text.color = Ink;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(bg.transform, false);
            Stretch(Rect(phGo), 22, 6);
            var ph = phGo.GetComponent<Text>();
            ph.font = BuiltinFont;
            ph.fontSize = 28;
            ph.fontStyle = FontStyle.Bold;
            ph.color = new Color(1f, 0.97f, 0.88f, 0.95f);
            ph.alignment = TextAnchor.MiddleLeft;
            ph.text = hint;

            var input = bg.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = text;
            input.placeholder = ph;
            input.characterLimit = limit;
            input.lineType = InputField.LineType.SingleLine;
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            var colors = input.colors;
            colors.selectedColor = new Color(0.82f, 1f, 0.72f, 1f);      // like the Nro field turning green while it is edited
            input.colors = colors;
            return input;
        }
    }
}
