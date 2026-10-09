using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BattleBall.Proto;
using NroRig;
using UnityEngine;
using UnityEngine.UI;
using CharacterInfo = BattleBall.Proto.CharacterInfo;

namespace BattleBall.Net
{
    /// <summary>
    /// The shop of an NPC (owner request 2026-10-09: Urôn sells every skin), drawn as the shop panel of the Nro client (Panel.cs typeShop: the NPC's portrait and name at the top, a row
    /// of tabs, the items as rows with the picture in a dark cell, the name, the price with the gem and the bonus lines under them, the money strip at the bottom). As in Nro the
    /// panel stands on the left and the player's own panel (the menu) moves to the right while it is open, so that what was bought can be seen in the bag at once.
    /// What the shop sells comes from the server (ShopRequest, answered by ShopContent: the same flow as the shop of an NPC in Nro, cmd -44); a tap on a row opens the item popup with
    /// the button that buys it (BuySkinRequest, which the server checks again). The rows are made at run time under <see cref="content"/>; the hierarchy is built by BattleBall/Build Login Scene.
    /// </summary>
    public sealed class BbShopPanel : MonoBehaviour
    {
        public GameObject root;
        public RectTransform panelRect;
        public Image portrait;
        public Text headerText;
        public Button closeButton;
        public Button[] tabs;
        public Image[] tabImages;
        public Text[] tabLabels;
        public Sprite tabIdle, tabOn;
        public ScrollRect scroll;
        public RectTransform content;
        public Font font;
        public Sprite rowSprite, cellSprite, gemSprite;
        public Text goldText, gemText;
        public BbPopup popup;
        public BbMenuUi menu;

        /// <summary>Raised when the player closes the shop (the X of the panel).</summary>
        public Action Closed;

        const float PanelWidth = 320f;
        static readonly Color NameWhite = Color.white, PriceGreen = new Color(0.20f, 0.85f, 0.20f), DescBlue = new Color(0.10f, 0.35f, 0.85f);
        static readonly Color RowColor = new Color(0.80f, 0.71f, 0.58f), RowPressed = new Color(0.98f, 1f, 0.29f), RowOwned = new Color(0.70f, 0.65f, 0.58f);
        static readonly string[] Races = { "EARTH", "NAMEC", "XAYDA" };          // by SkinInfo.gender_id: 0 Trái Đất, 1 Namếc, 2 Xayda
        static readonly string[] RaceTitles = { "Trái Đất", "Namếc", "Xayda" };

        ShopContent _content;
        PlayerDataSnapshot _snapshot;
        int _tab;
        uint _openItem;                         // the costume whose popup is open (its row is yellow)
        bool _open, _busy;
        float _x = -PanelWidth;                 // left edge of the panel: 0 open, -PanelWidth away
        readonly List<GameObject> _rows = new List<GameObject>();
        bool _wired;

        public bool IsOpen { get { return _open; } }

        /// <summary>The width of the panel that is on the screen now (the village centres on what is left).</summary>
        public float VisibleWidth { get { return Mathf.Clamp(PanelWidth + _x, 0f, PanelWidth); } }

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            closeButton.onClick.AddListener(Close);
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                tabs[i].onClick.AddListener(() => SelectTab(index));
            }
        }

        void Awake() { Wire(); }

        public void Open(ShopContent content, PlayerDataSnapshot snapshot, bool immediate = false)
        {
            Wire();
            _content = content;
            _snapshot = snapshot;
            _tab = 0;
            _open = true;
            _openItem = 0;
            root.SetActive(true);
            if (immediate) { _x = 0f; Apply(); }
            LayoutTabs();
            Refresh();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (popup != null && popup.IsOpen) popup.Hide();
            if (Closed != null) Closed();
        }

        /// <summary>The player data changed (a purchase went through): owned skins and the money.</summary>
        public void OnSnapshot(PlayerDataSnapshot snapshot)
        {
            _snapshot = snapshot;
            if (_open) Refresh();
        }

        void Update()
        {
            float target = _open ? 0f : -PanelWidth;
            if (!Mathf.Approximately(_x, target))
            {
                _x = Mathf.MoveTowards(_x, target, PanelWidth * 5f * Time.unscaledDeltaTime);
                Apply();
                if (!_open && Mathf.Approximately(_x, -PanelWidth)) root.SetActive(false);
            }
            if (_openItem != 0 && popup != null && !popup.IsOpen) { _openItem = 0; Refresh(); }
        }

        void Apply()
        {
            panelRect.anchoredPosition = new Vector2(_x, 0f);
            if (menu != null) menu.ApplyInsets();
        }

        // ---------------------------------------------------------------------------------------------------------------- tabs and rows

        void LayoutTabs()
        {
            int n = _content != null ? _content.Tabs.Count : 0;
            const float gap = 2f, total = PanelWidth - 8f;
            float w = n > 0 ? (total - gap * (n - 1)) / n : total;
            for (int i = 0; i < tabs.Length; i++)
            {
                bool used = i < n;
                tabs[i].gameObject.SetActive(used);
                if (!used) continue;
                var r = tabs[i].GetComponent<RectTransform>();
                r.sizeDelta = new Vector2(w, r.sizeDelta.y);
                r.anchoredPosition = new Vector2(4f + i * (w + gap), r.anchoredPosition.y);
                tabLabels[i].text = TwoLines(_content.Tabs[i].Name);
            }
        }

        /// <summary>A tab name on two lines as the Nro tabs ("Trái / Đất"): the first space becomes a line break.</summary>
        static string TwoLines(string name)
        {
            int space = name.IndexOf(' ');
            return space < 0 ? name : name.Substring(0, space) + "\n" + name.Substring(space + 1);
        }

        void SelectTab(int tab)
        {
            if (_content == null || tab < 0 || tab >= _content.Tabs.Count || tab == _tab) return;
            _tab = tab;
            scroll.verticalNormalizedPosition = 1f;
            Refresh();
        }

        bool Owns(uint costumeId)
        {
            if (_snapshot != null) foreach (var s in _snapshot.Skins) if (s.CostumeId == costumeId) return true;
            return false;
        }

        void Refresh()
        {
            if (_content == null) return;
            for (int i = 0; i < tabs.Length; i++)
            {
                if (!tabs[i].gameObject.activeSelf) continue;
                tabImages[i].sprite = i == _tab ? tabOn : tabIdle;
                tabLabels[i].color = i == _tab ? new Color(0.10f, 0.35f, 0.12f) : new Color(0.45f, 0.18f, 0.05f);
            }
            foreach (var r in _rows) Kill(r);
            _rows.Clear();
            int total = 0, owned = 0;
            foreach (var t in _content.Tabs) foreach (var it in t.Items) { total++; if (Owns(it.Skin.CostumeId)) owned++; }
            headerText.text = _content.NpcName + "\nBán cải trang bằng ngọc\nĐã có " + owned + " / " + total;
            var npcIcon = NroSpriteResolver.Instance != null ? NroSpriteResolver.Instance.ResolveSprite(728, BbAssetCache.Scale) : null;
            portrait.sprite = npcIcon;
            portrait.enabled = npcIcon != null;
            if (_snapshot != null)
            {
                goldText.text = Money(_snapshot.Gold);
                gemText.text = Money(_snapshot.Gems);
            }
            if (_tab < _content.Tabs.Count)
                foreach (var item in _content.Tabs[_tab].Items) AddRow(item);
        }

        void AddRow(ShopItem item)
        {
            var skin = item.Skin;
            bool owned = Owns(skin.CostumeId);
            var go = NewRect("Item" + skin.CostumeId, content);
            go.gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;
            var bg = go.gameObject.AddComponent<Image>();
            bg.sprite = rowSprite;
            bg.color = skin.CostumeId == _openItem ? RowPressed : owned ? RowOwned : RowColor;
            var button = go.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            var colors = button.colors;
            colors.pressedColor = RowPressed;
            button.colors = colors;
            var captured = item;
            button.onClick.AddListener(() => OnItem(captured));

            // the picture in its dark cell (Nro: the item icon in a 24 px cell on the left of the row)
            var cell = NewRect("Cell", go);
            Place(cell, 2f, 2f, 46f, 46f);
            var ci = cell.gameObject.AddComponent<Image>();
            ci.sprite = cellSprite;
            ci.type = Image.Type.Sliced;
            ci.raycastTarget = false;
            Sprite icon = BbCostumeAvatar.Of((int)skin.CostumeId);
            var ir = NewRect("Icon", cell);
            Stretch(ir, 6f, 5f);
            var ii = ir.gameObject.AddComponent<Image>();
            ii.sprite = icon;
            ii.enabled = icon != null;
            ii.preserveAspect = true;
            ii.raycastTarget = false;

            string name = skin.Name.Length > 0 ? skin.Name : "Cải trang";
            if (skin.FusionMainOnly) name += " (hợp thể)";
            var nameText = NewText(go, name, 16, NameWhite, TextAnchor.MiddleLeft, true);
            Place(nameText.rectTransform, 54f, 2f, 170f, 24f);
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Outline(nameText.gameObject);

            // the price on the right with the gem (green), or that it is owned
            if (owned)
            {
                var own = NewText(go, "Đã có", 15, new Color(0.30f, 0.14f, 0.05f), TextAnchor.MiddleRight, true);
                Place(own.rectTransform, 224f, 2f, 90f, 24f);
            }
            else
            {
                var price = NewText(go, Money(item.GemPrice), 16, PriceGreen, TextAnchor.MiddleRight, true);
                Place(price.rectTransform, 210f, 2f, 76f, 24f);
                Outline(price.gameObject);
                var gem = NewRect("Gem", go);
                Place(gem, 289f, 5f, 22f, 18f);
                var gi = gem.gameObject.AddComponent<Image>();
                gi.sprite = gemSprite;
                gi.preserveAspect = true;
                gi.raycastTarget = false;
            }

            var desc = NewText(go, FirstLines(skin), 13, DescBlue, TextAnchor.UpperLeft, false);
            Place(desc.rectTransform, 54f, 26f, 258f, 22f);
            desc.horizontalOverflow = HorizontalWrapMode.Overflow;
            _rows.Add(go.gameObject);
        }

        /// <summary>The bonus lines on one line of the row (the first two), or who it is for when it has none.</summary>
        static string FirstLines(SkinInfo skin)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var s in skin.Stats)
            {
                if (n == 2) break;
                if (n > 0) sb.Append(", ");
                sb.Append(s.Text.Length > 0 ? s.Text : s.StatKey + " +" + s.Value.ToString("0.#", CultureInfo.InvariantCulture));
                n++;
            }
            return n > 0 ? sb.ToString() : ForWhom(skin);
        }

        static string ForWhom(SkinInfo skin)
        {
            return skin.GenderId == 3 ? "Dùng cho mọi hành tinh" : "Dùng cho " + RaceTitles[Mathf.Clamp((int)skin.GenderId, 0, 2)];
        }

        /// <summary>For the screenshot tool: tap the item of this costume (the tab that holds it opens first).</summary>
        public void DemoTap(uint costumeId)
        {
            if (_content == null) return;
            for (int t = 0; t < _content.Tabs.Count; t++)
                foreach (var item in _content.Tabs[t].Items)
                    if (item.Skin.CostumeId == costumeId) { SelectTab(t); OnItem(item); return; }
        }

        /// <summary>For the scripted run in Play mode: the first item the account does not own yet in the open tab (0 when there is none).</summary>
        public uint DebugFirstUnowned()
        {
            if (_content == null || _tab >= _content.Tabs.Count) return 0;
            foreach (var item in _content.Tabs[_tab].Items) if (!Owns(item.Skin.CostumeId)) return item.Skin.CostumeId;
            return 0;
        }

        /// <summary>For the scripted run in Play mode: tap the item of this costume and press its buy button.</summary>
        public void DebugBuy(uint costumeId)
        {
            if (_content == null) return;
            foreach (var t in _content.Tabs)
                foreach (var item in t.Items)
                    if (item.Skin.CostumeId == costumeId) { Buy(item); return; }
        }

        public int DebugRowCount { get { return _rows.Count; } }

        // ---------------------------------------------------------------------------------------------------------------- popup and purchase

        void OnItem(ShopItem item)
        {
            var skin = item.Skin;
            bool owned = Owns(skin.CostumeId);
            _openItem = skin.CostumeId;
            popup.areaLeftOverride = 0f;                            // the shop stands on the left; the popup stands under it
            Refresh();
            var lines = new StringBuilder();
            foreach (var stat in skin.Stats)
            {
                if (lines.Length > 0) lines.Append('\n');
                lines.Append("<color=#1E8F1E>").Append(stat.Text.Length > 0 ? stat.Text : stat.StatKey + " +" + stat.Value.ToString("0.#", CultureInfo.InvariantCulture)).Append("</color>");
            }
            if (skin.Stats.Count == 0) lines.Append("<color=#7A5A3A>Không có chỉ số cộng thêm</color>");
            var desc = new StringBuilder(ForWhom(skin)).Append('.');
            if (skin.FusionMainOnly) desc.Append("\nCải trang hợp thể: chỉ chủ hợp thể mặc được (dùng Bông tai để hợp thể).");
            var specs = new List<BbPopup.ButtonSpec>
            {
                new BbPopup.ButtonSpec(owned ? "Đã sở hữu" : "Mua bằng\n" + Money(item.GemPrice) + " ngọc", !owned, true)
            };
            popup.Show(BbCostumeAvatar.Of((int)skin.CostumeId), skin.Name.Length > 0 ? skin.Name : "Cải trang #" + skin.CostumeId, lines.ToString(), desc.ToString(),
                null, null, specs, i => { if (i == 0) Buy(item); }, null, (int)skin.CostumeId);
        }

        CharacterInfo CharacterFor(SkinInfo skin)
        {
            if (_snapshot == null) return null;
            string race = null;
            switch (skin.FusionRace)
            {
                case "td": race = "EARTH"; break;
                case "nm": race = "NAMEC"; break;
                case "xd": race = "XAYDA"; break;
                default: if (skin.GenderId < 3) race = Races[(int)skin.GenderId]; break;
            }
            foreach (var c in _snapshot.Characters) if (race == null || c.Race == race) return c;
            return null;
        }

        async void Buy(ShopItem item)
        {
            if (_busy) return;
            var skin = item.Skin;
            popup.Hide();
            if (Owns(skin.CostumeId)) { menu.Toast("Đã sở hữu"); return; }
            if (menu.IsDemo || !BbSession.IsActive)
            {
                if (_snapshot == null || _snapshot.Gems < item.GemPrice) { menu.Toast("Không đủ ngọc"); return; }     // no server: only change what is shown
                _snapshot.Gems -= item.GemPrice;
                _snapshot.Skins.Add(skin);
                menu.OnShopPurchase(_snapshot);
                Refresh();
                menu.Toast("Đã mua " + (skin.Name.Length > 0 ? skin.Name : "cải trang") + " (thử nghiệm)");
                return;
            }
            var ch = CharacterFor(skin);
            if (ch == null) { menu.Toast("Không có nhân vật nào mặc được cải trang này"); return; }
            _busy = true;
            try
            {
                var result = await BbSession.Client.BuySkinAsync(ch.CharacterId, skin.CostumeId);
                menu.Toast(result.Ok ? "Đã mua " + (skin.Name.Length > 0 ? skin.Name : "cải trang") : BbMenuUi.ActionText(result));
            }
            catch (Exception e)
            {
                menu.Toast("Không gửi được lệnh: " + e.Message);
            }
            _busy = false;
        }

        // ---------------------------------------------------------------------------------------------------------------- helpers

        static string Money(ulong v)
        {
            return v.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        static void Stretch(RectTransform r, float padX, float padY)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(padX, padY);
            r.offsetMax = new Vector2(-padX, -padY);
        }

        Text NewText(RectTransform parent, string text, int size, Color color, TextAnchor align, bool bold)
        {
            var go = NewRect("Text", parent);
            var t = go.gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.raycastTarget = false;
            return t;
        }

        static void Outline(GameObject go)
        {
            var o = go.AddComponent<UnityEngine.UI.Outline>();
            o.effectColor = new Color(0.15f, 0.08f, 0.02f, 0.9f);
            o.effectDistance = new Vector2(1f, -1f);
        }

        static void Kill(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
