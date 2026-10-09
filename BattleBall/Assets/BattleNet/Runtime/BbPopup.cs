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

        public struct ButtonSpec
        {
            public string label;
            public bool enabled;
            public bool primary;
            public ButtonSpec(string label, bool enabled, bool primary) { this.label = label; this.enabled = enabled; this.primary = primary; }
        }

        Action<int> _onButton;
        Action _onClose;
        bool _wired;
        float _previewScale;

        public bool IsOpen { get { return root != null && root.activeSelf; } }

        void Wire()
        {
            if (_wired) return;
            _wired = true;
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
                float left = (areaWidth - (inRow * size.x + gap * (inRow - 1))) * 0.5f;
                r.anchoredPosition = new Vector2(left + col * (size.x + gap), bottom + (rows - 1 - row) * (size.y + gap));
                buttons[i].interactable = specs[i].enabled;
                buttonImages[i].sprite = specs[i].primary ? primarySprite : normalSprite;
                buttonLabels[i].text = specs[i].label;
                buttonLabels[i].color = new Color(0.30f, 0.14f, 0.05f);          // Nro writes every command in dark bold
                buttonLabels[i].fontSize = specs[i].label.Length > 14 ? 15 : 17;
            }
            box.anchoredPosition = new Vector2(box.anchoredPosition.x, bottom + rows * (size.y + gap));
            root.SetActive(true);
        }

        public void Hide()
        {
            if (!IsOpen) return;
            root.SetActive(false);
            var closed = _onClose;
            _onClose = null;
            if (closed != null) closed();
        }
    }
}
