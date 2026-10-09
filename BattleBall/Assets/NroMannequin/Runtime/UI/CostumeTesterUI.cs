using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NroRig.UI
{
    [DisallowMultipleComponent]
    public class CostumeTesterUI : MonoBehaviour
    {
        [Header("Target Systems")]
        public NroMannequin mannequin;
        public NroCostumeCatalog catalog;
        public NroSpriteResolver resolver;
        public Transform mockHpBar;

        [Header("Costume List & Search")]
        public ScrollRect costumeScrollRect;
        public RectTransform costumeContent;
        public GameObject costumeItemPrefab;
        public InputField searchInput;
        public Button clearSearchButton;
        public Button[] categoryFilterButtons;

        [Header("Pose Controller")]
        public Transform poseButtonsContainer;
        public Button[] poseButtons;
        public Button prevPoseButton;
        public Button nextPoseButton;
        public Button autoPlayButton;
        public Text currentPoseLabel;

        [Header("Resolution & Facing")]
        public Button[] resolutionButtons; // Index 0: x1, 1: x2, 2: x3, 3: x4
        public Button flipFacingButton;
        public Text flipFacingLabel;

        [Header("Debug HUD")]
        public Text debugHudText;
        public Button resetCameraViewButton;
        public Camera2DController cameraController;

        // Runtime State
        private int _currentCostumeId = -1;
        private int _currentPose = 0;
        private int _currentZoom = 2; // Default x2
        private int _currentFacing = 1;
        private string _activeCategoryFilter = "All";
        private string _activeSearchQuery = string.Empty;

        private readonly List<CostumeListItemUI> _instantiatedItems = new List<CostumeListItemUI>();
        private bool _isAutoPlaying = false;
        private Coroutine _autoPlayCoroutine;

        private void Awake()
        {
            // Defensive auto-discovery
            if (mannequin == null) mannequin = FindObjectOfType<NroMannequin>();
            if (catalog == null) catalog = FindObjectOfType<NroCostumeCatalog>();
            if (resolver == null) resolver = FindObjectOfType<NroSpriteResolver>();
            if (cameraController == null) cameraController = FindObjectOfType<Camera2DController>();
        }

        private IEnumerator Start()
        {
            // Ensure catalog is initialized
            if (catalog != null && !catalog.IsLoaded)
            {
                catalog.Initialize();
            }

            yield return null;

            SetupSearchAndFilters();
            SetupResolutionButtons();
            SetupFacingButton();
            SetupPoseButtons();

            if (resetCameraViewButton != null && cameraController != null)
                resetCameraViewButton.onClick.AddListener(cameraController.ResetView);

            PopulateCostumeList();

            // Initial state
            SetResolution(_currentZoom);
            SetPose(0);
            SetFacing(1);

            // Select default costume (first costume in list)
            if (_instantiatedItems.Count > 0)
            {
                SelectCostume(_instantiatedItems[0].Entry.Id);
            }
        }

        private void Update()
        {
            HandleKeyboardShortcuts();
        }

        private void LateUpdate()
        {
            UpdateMockHPBarPosition();
        }

        #region Costume List & Search

        private void PopulateCostumeList()
        {
            if (catalog == null || costumeContent == null || costumeItemPrefab == null) return;

            var allCostumes = catalog.GetAllCostumes();
            if (allCostumes == null) return;

            // Clear existing instantiated items (preserving template)
            foreach (Transform child in costumeContent)
            {
                if (child.gameObject != costumeItemPrefab)
                    Destroy(child.gameObject);
            }
            _instantiatedItems.Clear();

            costumeItemPrefab.SetActive(false);

            for (int i = 0; i < allCostumes.Count; i++)
            {
                var entry = allCostumes[i];
                GameObject go = Instantiate(costumeItemPrefab, costumeContent);
                go.name = $"CostumeItem_{entry.Id}";
                go.SetActive(true);

                var itemUI = go.GetComponent<CostumeListItemUI>();
                if (itemUI == null) itemUI = go.AddComponent<CostumeListItemUI>();

                itemUI.Setup(entry, SelectCostume, entry.Id == _currentCostumeId);
                _instantiatedItems.Add(itemUI);
            }

            ApplyCostumeFilters();
        }

        private void SetupSearchAndFilters()
        {
            if (searchInput != null)
            {
                searchInput.onValueChanged.AddListener(query =>
                {
                    _activeSearchQuery = query;
                    ApplyCostumeFilters();
                });
            }

            if (clearSearchButton != null && searchInput != null)
            {
                clearSearchButton.onClick.AddListener(() =>
                {
                    searchInput.text = string.Empty;
                });
            }

            // Category filter buttons setup
            string[] categories = { "All", "Standard", "Fusion", "Broly", "GreatApe", "Chibi", "Component" };
            if (categoryFilterButtons != null)
            {
                for (int i = 0; i < categoryFilterButtons.Length && i < categories.Length; i++)
                {
                    string catKey = categories[i];
                    categoryFilterButtons[i].onClick.AddListener(() =>
                    {
                        _activeCategoryFilter = catKey;
                        HighlightActiveFilterButton(catKey);
                        ApplyCostumeFilters();
                    });
                }
                HighlightActiveFilterButton("All");
            }
        }

        private void HighlightActiveFilterButton(string activeKey)
        {
            string[] categories = { "All", "Standard", "Fusion", "Broly", "GreatApe", "Chibi", "Component" };
            if (categoryFilterButtons == null) return;

            for (int i = 0; i < categoryFilterButtons.Length && i < categories.Length; i++)
            {
                bool isActive = string.Equals(categories[i], activeKey, StringComparison.OrdinalIgnoreCase);
                var img = categoryFilterButtons[i].GetComponent<Image>();
                if (img != null)
                {
                    img.color = isActive 
                        ? new Color(0.20f, 0.60f, 0.86f, 1f) 
                        : new Color(0.18f, 0.20f, 0.26f, 0.95f);
                }
            }
        }

        private void ApplyCostumeFilters()
        {
            for (int i = 0; i < _instantiatedItems.Count; i++)
            {
                var item = _instantiatedItems[i];
                bool matches = MatchesFilter(item.Entry, _activeCategoryFilter, _activeSearchQuery);
                item.gameObject.SetActive(matches);
            }

            // Reset scroll position to top
            if (costumeScrollRect != null)
                costumeScrollRect.verticalNormalizedPosition = 1f;
        }

        private bool MatchesFilter(NroCostumeEntry entry, string catFilter, string query)
        {
            if (catFilter != "All" && !string.Equals(entry.CategoryKey, catFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(query))
                return true;

            string q = query.Trim();
            if (entry.Id.ToString().Contains(q)) return true;
            if (entry.HeadPartId.ToString().Contains(q)) return true;
            if (entry.BodyPartId.ToString().Contains(q)) return true;
            if (entry.LegPartId.ToString().Contains(q)) return true;
            if (entry.Name != null && entry.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        public void SelectCostume(int costumeId)
        {
            _currentCostumeId = costumeId;

            // Update item highlights
            for (int i = 0; i < _instantiatedItems.Count; i++)
            {
                _instantiatedItems[i].SetSelected(_instantiatedItems[i].Entry.Id == _currentCostumeId);
            }

            if (catalog == null || mannequin == null) return;

            var entry = catalog.GetCostume(costumeId);
            if (entry == null) return;

            // Load Parts for current zoom level
            NroPartData headPart = entry.HeadPartId >= 0 ? catalog.GetPart(entry.HeadPartId, _currentZoom) : null;
            // For Head-Only costume: borrow default base body (57) and leg (58) according to Player.java:935,995
            NroPartData bodyPart = entry.BodyPartId >= 0 ? catalog.GetPart(entry.BodyPartId, _currentZoom) : catalog.GetPart(57, _currentZoom);
            NroPartData legPart = entry.LegPartId >= 0 ? catalog.GetPart(entry.LegPartId, _currentZoom) : catalog.GetPart(58, _currentZoom);

            mannequin.SetParts(headPart, bodyPart, legPart);

            // Handle Multi-Frame Head cycle
            if (entry.HeadCyclePartIds != null && entry.HeadCyclePartIds.Length > 0)
            {
                NroPartData[] cycle = new NroPartData[entry.HeadCyclePartIds.Length];
                for (int i = 0; i < cycle.Length; i++)
                {
                    cycle[i] = catalog.GetPart(entry.HeadCyclePartIds[i], _currentZoom);
                }
                mannequin.SetHeadCycle(cycle);
            }

            // Flag updates
            mannequin.isMonkey = entry.CategoryId == 4 || string.Equals(entry.CategoryKey, "GreatApe", StringComparison.OrdinalIgnoreCase);
            mannequin.isFusion = entry.CategoryId == 2 || string.Equals(entry.CategoryKey, "Fusion", StringComparison.OrdinalIgnoreCase);

            mannequin.Apply();
            UpdateDebugHUD();
        }

        #endregion

        #region Pose Controller

        private void SetupPoseButtons()
        {
            if (poseButtons != null)
            {
                for (int i = 0; i < poseButtons.Length && i < NroPoseTable.PoseCount; i++)
                {
                    int poseIndex = i;
                    poseButtons[i].onClick.AddListener(() => SetPose(poseIndex));
                }
            }

            if (prevPoseButton != null)
                prevPoseButton.onClick.AddListener(PreviousPose);

            if (nextPoseButton != null)
                nextPoseButton.onClick.AddListener(NextPose);

            if (autoPlayButton != null)
                autoPlayButton.onClick.AddListener(ToggleAutoPlay);
        }

        public void SetPose(int poseIndex)
        {
            _currentPose = Mathf.Clamp(poseIndex, 0, NroPoseTable.PoseCount - 1);
            if (mannequin != null)
            {
                mannequin.SetPose((NroPose)_currentPose);
            }

            if (currentPoseLabel != null)
                currentPoseLabel.text = $"[cf = {_currentPose}] {((NroPose)_currentPose)}";

            HighlightActivePoseButton(_currentPose);
            UpdateDebugHUD();
        }

        public void NextPose()
        {
            SetPose((_currentPose + 1) % NroPoseTable.PoseCount);
        }

        public void PreviousPose()
        {
            SetPose((_currentPose - 1 + NroPoseTable.PoseCount) % NroPoseTable.PoseCount);
        }

        private void ToggleAutoPlay()
        {
            _isAutoPlaying = !_isAutoPlaying;
            if (_isAutoPlaying)
            {
                _autoPlayCoroutine = StartCoroutine(AutoPlayLoop());
                if (autoPlayButton != null)
                {
                    var txt = autoPlayButton.GetComponentInChildren<Text>();
                    if (txt != null) txt.text = "⏸ Stop Loop";
                    var img = autoPlayButton.GetComponent<Image>();
                    if (img != null) img.color = new Color(0.9f, 0.3f, 0.2f, 1f);
                }
            }
            else
            {
                if (_autoPlayCoroutine != null) StopCoroutine(_autoPlayCoroutine);
                if (autoPlayButton != null)
                {
                    var txt = autoPlayButton.GetComponentInChildren<Text>();
                    if (txt != null) txt.text = "▶ Run Loop";
                    var img = autoPlayButton.GetComponent<Image>();
                    if (img != null) img.color = new Color(0.2f, 0.7f, 0.3f, 1f);
                }
            }
        }

        private IEnumerator AutoPlayLoop()
        {
            // Loops running poses cf = 2, 3, 4, 5, 6 at 10 fps
            int[] runPoses = { 2, 3, 4, 5, 6 };
            int idx = 0;
            while (_isAutoPlaying)
            {
                SetPose(runPoses[idx]);
                idx = (idx + 1) % runPoses.Length;
                yield return new WaitForSeconds(0.1f);
            }
        }

        private void HighlightActivePoseButton(int activePose)
        {
            if (poseButtons == null) return;
            for (int i = 0; i < poseButtons.Length; i++)
            {
                var img = poseButtons[i].GetComponent<Image>();
                if (img != null)
                {
                    img.color = (i == activePose)
                        ? new Color(0.95f, 0.75f, 0.10f, 1f) // Bright Gold
                        : new Color(0.20f, 0.24f, 0.30f, 0.95f);
                }
            }
        }

        #endregion

        #region Resolution & Facing

        private void SetupResolutionButtons()
        {
            if (resolutionButtons != null)
            {
                for (int i = 0; i < resolutionButtons.Length; i++)
                {
                    int zoom = i + 1; // 1, 2, 3, 4
                    resolutionButtons[i].onClick.AddListener(() => SetResolution(zoom));
                }
            }
        }

        public void SetResolution(int zoomLevel)
        {
            _currentZoom = Mathf.Clamp(zoomLevel, 1, 4);

            if (resolver != null)
            {
                resolver.SetZoomLevel(_currentZoom);
            }

            // Reload current costume with new resolution sprites
            if (_currentCostumeId >= 0)
            {
                SelectCostume(_currentCostumeId);
            }

            HighlightActiveResolutionButton(_currentZoom);
            UpdateDebugHUD();
        }

        private void HighlightActiveResolutionButton(int activeZoom)
        {
            if (resolutionButtons == null) return;
            for (int i = 0; i < resolutionButtons.Length; i++)
            {
                int zoom = i + 1;
                var img = resolutionButtons[i].GetComponent<Image>();
                if (img != null)
                {
                    img.color = (zoom == activeZoom)
                        ? new Color(0.18f, 0.80f, 0.44f, 1f) // Emerald Green
                        : new Color(0.20f, 0.24f, 0.30f, 0.95f);
                }
            }
        }

        private void SetupFacingButton()
        {
            if (flipFacingButton != null)
            {
                flipFacingButton.onClick.AddListener(ToggleFacing);
            }
        }

        public void ToggleFacing()
        {
            SetFacing(_currentFacing == 1 ? -1 : 1);
        }

        public void SetFacing(int dir)
        {
            _currentFacing = dir >= 0 ? 1 : -1;
            if (mannequin != null)
            {
                mannequin.SetFacing(_currentFacing);
            }

            if (flipFacingLabel != null)
                flipFacingLabel.text = _currentFacing > 0 ? "Facing: Right (+1)" : "Facing: Left (-1)";

            UpdateDebugHUD();
        }

        #endregion

        #region Debug HUD & Overhead HP Bar

        private void UpdateDebugHUD()
        {
            if (debugHudText == null || mannequin == null) return;

            var entry = catalog != null ? catalog.GetCostume(_currentCostumeId) : null;
            string costumeName = entry != null ? entry.Name : "None";
            string groupName = entry != null ? entry.CategoryName : "None";
            int headId = entry != null ? entry.HeadPartId : -1;
            int bodyId = entry != null ? entry.BodyPartId : -1;
            int legId = entry != null ? entry.LegPartId : -1;
            int ch = mannequin.CharHeight;
            float hpOffset = mannequin.GetOverheadOffsetPixels(true);
            Vector3 hpWorld = mannequin.GetOverheadWorldPosition(true);

            debugHudText.text =
                $"<color=#FFD700><b>=== NRO MANNEQUIN DEBUG HUD ===</b></color>\n" +
                $"<b>Costume:</b> [#{_currentCostumeId}] {costumeName}\n" +
                $"<b>Category:</b> {groupName}\n\n" +
                $"<b>Parts:</b> Head:{headId} | Body:{bodyId} | Leg:{legId}\n" +
                $"<b>Pose:</b> cf={_currentPose} ({(NroPose)_currentPose})\n" +
                $"<b>Facing:</b> {(_currentFacing > 0 ? "Right (+1)" : "Left (-1)")}\n" +
                $"<b>Zoom:</b> x{_currentZoom} (PPU: {100f * _currentZoom})\n\n" +
                $"<color=#00FFFF><b>[Anatomy & Height]</b></color>\n" +
                $"<b>CharHeight (ch):</b> {ch} px\n" +
                $"<b>isMonkey:</b> {mannequin.isMonkey} | <b>isFusion:</b> {mannequin.isFusion}\n" +
                $"<b>HP Offset:</b> {hpOffset:F1} px (Player Me)\n" +
                $"<b>HP World Pos:</b> ({hpWorld.x:F3}, {hpWorld.y:F3}, {hpWorld.z:F3})\n\n" +
                $"<b>Renderers:</b> H:{(mannequin.headRenderer != null && mannequin.headRenderer.enabled ? "ON" : "OFF")} " +
                $"B:{(mannequin.bodyRenderer != null && mannequin.bodyRenderer.enabled ? "ON" : "OFF")} " +
                $"L:{(mannequin.legRenderer != null && mannequin.legRenderer.enabled ? "ON" : "OFF")}";
        }

        private void UpdateMockHPBarPosition()
        {
            if (mockHpBar != null && mannequin != null)
            {
                mockHpBar.position = mannequin.GetOverheadWorldPosition(true);
            }
        }

        #endregion

        private void HandleKeyboardShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                ToggleFacing();

            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
                NextPose();

            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
                PreviousPose();

            if (Input.GetKeyDown(KeyCode.Alpha1)) SetResolution(1);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetResolution(2);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetResolution(3);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetResolution(4);

            if (Input.GetKeyDown(KeyCode.P))
                ToggleAutoPlay();
        }
    }
}
