using System;
using UnityEngine;
using UnityEngine.UI;

namespace NroRig.UI
{
    public class CostumeListItemUI : MonoBehaviour
    {
        [Header("UI Bindings")]
        public Button button;
        public Image backgroundImage;
        public Text labelTitle;
        public Text labelParts;
        public Text labelBadge;
        public Image badgeBackground;
        public GameObject selectionHighlight;

        private NroCostumeEntry _entry;
        private Action<int> _onSelectCallback;

        public NroCostumeEntry Entry => _entry;

        public void Setup(NroCostumeEntry entry, Action<int> onSelectCallback, bool isSelected)
        {
            _entry = entry;
            _onSelectCallback = onSelectCallback;

            if (labelTitle != null)
                labelTitle.text = $"[#{entry.Id}] {entry.Name}";

            if (labelParts != null)
            {
                string headStr = entry.HeadCyclePartIds != null && entry.HeadCyclePartIds.Length > 0
                    ? $"H:{entry.HeadPartId}*"
                    : $"H:{entry.HeadPartId}";
                labelParts.text = $"{headStr}  B:{(entry.BodyPartId >= 0 ? entry.BodyPartId.ToString() : "-")}  L:{(entry.LegPartId >= 0 ? entry.LegPartId.ToString() : "-")}";
            }

            if (labelBadge != null)
                labelBadge.text = entry.CategoryKey;

            if (badgeBackground != null)
                badgeBackground.color = GetCategoryColor(entry.CategoryId);

            SetSelected(isSelected);

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => _onSelectCallback?.Invoke(_entry.Id));
            }
        }

        public void SetSelected(bool isSelected)
        {
            if (selectionHighlight != null)
                selectionHighlight.SetActive(isSelected);

            if (backgroundImage != null)
            {
                backgroundImage.color = isSelected 
                    ? new Color(0.24f, 0.36f, 0.52f, 1f) 
                    : new Color(0.14f, 0.16f, 0.20f, 0.95f);
            }
        }

        public static Color GetCategoryColor(int categoryId)
        {
            switch (categoryId)
            {
                case 1: return new Color(0.20f, 0.60f, 0.86f, 1f); // Standard: Blue
                case 2: return new Color(0.95f, 0.61f, 0.07f, 1f); // Fusion: Amber
                case 3: return new Color(0.91f, 0.30f, 0.24f, 1f); // Broly: Crimson
                case 4: return new Color(0.61f, 0.35f, 0.71f, 1f); // GreatApe: Purple
                case 5: return new Color(0.18f, 0.80f, 0.44f, 1f); // Chibi: Emerald
                case 6: return new Color(0.58f, 0.65f, 0.65f, 1f); // Component: Slate
                default: return Color.gray;
            }
        }
    }
}
