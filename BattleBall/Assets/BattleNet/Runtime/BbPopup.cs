using System;
using BattleBall.Proto;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The detail popup of the menu, as the Nro item popup: a white box (picture and name at the top, the bonus lines right aligned, a line, the description) standing
    /// on the row of command buttons at the bottom of the panel (Nro's rounded squares, three to a row, the first one green). Nothing is dimmed and a tap outside the
    /// box closes it. One popup serves a skin, an aura, a mail, a notice and an NPC; the hierarchy is built by BattleBall/Build Login Scene.
    /// </summary>
    public sealed class BbPopup : MonoBehaviour
    {
        public GameObject root;
        public RectTransform box;
        public Image icon;
        public GameObject previewRoot;                           // the backdrop of the full-body preview of a costume (the skin popups)
        public BbCharacterView preview;
        public Text title, lines, description;
        public Image[] gifts;
        public Text[] giftCaptions;
        public Button[] buttons;
        public Image[] buttonImages;
        public Text[] buttonLabels;
        public Button close;
        public Sprite primarySprite, normalSprite;
        public float areaWidth = 320f;                           // the buttons are centred in the width of the panel
        public Func<float> areaLeftProvider;                     // where the panel that owns the popup starts: 0 on the left, the menu panel docked on the right (asked at every Show)
        public float? areaLeftOverride;                          // the shop on the left sets 0 for the popup of its items; Hide clears it
        public bool centerOnScreen;                              // the dialog of an NPC stands in the middle of the screen (owner request 2026-10-10); Hide clears it
        float areaLeft;

        public struct ButtonSpec
        {
            public string label;
            public bool enabled;
            public bool primary;
            public ButtonSpec(string label, bool enabled, bool primary) { this.label = label; this.enabled = enabled; this.primary = primary; }
        }

        RectTransform _separator;                                 // the line above the description, and the layout of the full box, kept for the compact dialog of an NPC
        float _fullHeight, _sepY, _descY, _descH;
        Action<int> _onButton;
        Action _onClose;
        bool _wired;
        float _previewScale;

        public bool IsOpen { get { return root != null && root.activeSelf; } }

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            _separator = box.Find("Separator") as RectTransform;
            _fullHeight = box.sizeDelta.y;
            if (_separator != null) _sepY = _separator.anchoredPosition.y;
            _descY = description.rectTransform.anchoredPosition.y;
            _descH = description.rectTransform.sizeDelta.y;
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                buttons[i].onClick.AddListener(() => { if (_onButton != null) _onButton(index); });
            }
            close.onClick.AddListener(Hide);
        }

        void Awake() { Wire(); }

        /// <summary>
        /// Opens the popup. <paramref name="giftIcons"/> and <paramref name="giftTexts"/> fill the row of small pictures under the title (a mail's gifts); a null icon
        /// shows only the caption. <paramref name="onButton"/> gets the index of the pressed button; the popup stays open until the handler calls <see cref="Hide"/>.
        /// </summary>
        public void Show(Sprite iconSprite, string titleText, string linesRich, string descText, IList<Sprite> giftIcons, IList<string> giftTexts,
                         IList<ButtonSpec> specs, Action<int> onButton, Action onClose = null, int previewCostume = 0, AuraInfo previewAura = null)
        {
            Wire();
            areaLeft = areaLeftOverride ?? (areaLeftProvider != null ? areaLeftProvider() : 0f);
            if (centerOnScreen) areaLeft = Mathf.Max(0f, (((RectTransform)root.transform).rect.width - areaWidth) * 0.5f);
            _onButton = onButton;
            _onClose = onClose;
            icon.sprite = iconSprite;
            icon.enabled = iconSprite != null;
            title.text = titleText ?? "";
            lines.text = linesRich ?? "";
            description.text = descText ?? "";

            int g = giftTexts != null ? Mathf.Min(giftTexts.Count, gifts.Length) : 0;
            for (int i = 0; i < gifts.Length; i++)
            {
                bool used = i < g;
                gifts[i].transform.parent.gameObject.SetActive(used);
                if (!used) continue;
                var sp = giftIcons != null && i < giftIcons.Count ? giftIcons[i] : null;
                gifts[i].sprite = sp;
                gifts[i].enabled = sp != null;
                giftCaptions[i].text = giftTexts[i];
            }

            // the whole body of the costume on the left, standing (the head picture above is only the icon); the bonus lines take the room on its right
            // an aura is shown on a character in a backdrop as wide as the popup, the character as small as the tallest frame of the aura needs
            bool aura = previewAura != null && previewCostume > 0;
            bool body = false;
            if (preview != null)
            {
                if (_previewScale <= 0f) _previewScale = preview.uiScale;
                preview.SetAura(aura ? (int)previewAura.AuraId : 0, aura ? previewAura.Layers : null);
                preview.uiScale = aura ? Mathf.Clamp(130f / Mathf.Max(preview.AuraNroHeight, 50f), 0.8f, _previewScale) : _previewScale;
                body = previewCostume > 0 && g == 0 && preview.Show(previewCostume);
                if (previewRoot != null)
                {
                    var back = (RectTransform)previewRoot.transform;
                    back.sizeDelta = new Vector2(aura ? box.sizeDelta.x - 16f : 112f, back.sizeDelta.y);
                    ((RectTransform)preview.transform).anchoredPosition = new Vector2(back.sizeDelta.x * 0.5f, ((RectTransform)preview.transform).anchoredPosition.y);
                }
            }
            if (previewRoot != null) previewRoot.SetActive(body);
            var linesRect = lines.rectTransform;                    // under the row of gifts when there is one
            bool narrow = body && !aura;
            float boxW = box.sizeDelta.x;
            linesRect.anchoredPosition = new Vector2(narrow ? 126f : 8f, g > 0 ? -128f : -52f);
            linesRect.sizeDelta = new Vector2(narrow ? boxW - 134f : boxW - 16f, g > 0 ? 78f : 152f);

            // the dialog of an NPC (a few words and the choices) needs no room for an icon and bonus lines: a short box (owner request 2026-10-10: in the middle of the screen)
            bool compact = centerOnScreen && string.IsNullOrEmpty(linesRich) && g == 0 && !body && iconSprite == null;
            box.sizeDelta = new Vector2(box.sizeDelta.x, compact ? 116f : _fullHeight);
            if (_separator != null) _separator.anchoredPosition = new Vector2(_separator.anchoredPosition.x, compact ? -48f : _sepY);
            var descRect = description.rectTransform;
            descRect.anchoredPosition = new Vector2(descRect.anchoredPosition.x, compact ? -54f : _descY);
            descRect.sizeDelta = new Vector2(descRect.sizeDelta.x, compact ? 56f : _descH);

            // the command buttons: rows of three at the bottom (the first row on top), centred in the panel; the box stands on them
            int n = specs != null ? Mathf.Min(specs.Count, buttons.Length) : 0;
            const int perRow = 3;
            const float gap = 4f, bottom = 2f;
            int rows = (n + perRow - 1) / perRow;
            var size = buttons[0].GetComponent<RectTransform>().sizeDelta;
            for (int i = 0; i < buttons.Length; i++)
            {
                bool used = i < n;
                buttons[i].gameObject.SetActive(used);
                if (!used) continue;
                var r = buttons[i].GetComponent<RectTransform>();
                int row = i / perRow;
                int col = i - row * perRow;
                int inRow = row == rows - 1 ? n - row * perRow : perRow;
                float left = areaLeft + (areaWidth - (inRow * size.x + gap * (inRow - 1))) * 0.5f;
                r.anchoredPosition = new Vector2(left + col * (size.x + gap), bottom + (rows - 1 - row) * (size.y + gap));
                buttons[i].interactable = specs[i].enabled;
                buttonImages[i].sprite = specs[i].primary ? primarySprite : normalSprite;
                buttonLabels[i].text = specs[i].label;
                buttonLabels[i].color = new Color(0.30f, 0.14f, 0.05f);          // Nro writes every command in dark bold
                buttonLabels[i].fontSize = specs[i].label.Length > 14 ? 15 : 17;
            }
            // the box is a little wider than the panel (as in Nro): it hangs out on the right of a panel on the left, and on the left of a panel docked on the right
            float boxX = centerOnScreen ? areaLeft + (areaWidth - box.sizeDelta.x) * 0.5f : areaLeft > 0f ? areaLeft + areaWidth - box.sizeDelta.x - 5.5f : 5.5f;
            box.anchoredPosition = new Vector2(boxX, bottom + rows * (size.y + gap));
            root.SetActive(true);
        }

        public void Hide()
        {
            if (!IsOpen) return;
            root.SetActive(false);
            areaLeftOverride = null;
            centerOnScreen = false;
            var closed = _onClose;
            _onClose = null;
            if (closed != null) closed();
        }
    }
}
