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
    /// The shop of an NPC (owner request 2026-10-09: Urôn sells every skin; 2026-10-10: Bunma sells clothes and stones), drawn as the shop panel of the Nro client (Panel.cs typeShop: the NPC's
    /// portrait and name at the top, a row of tabs, the items as rows with the picture in a dark cell, the name, the price and the bonus lines under them, the money strip at the bottom). As in
    /// Nro the panel stands on the left and the player's own panel (the menu) moves to the right while it is open, so that what was bought can be seen in the bag at once.
    /// What the shop sells comes from the server (ShopRequest, answered by ShopContent: the same flow as the shop of an NPC in Nro, cmd -44). An item is a skin (paid with gems, BuySkinRequest),
    /// a whole piece of gear (gold) or a stone (gold or gems, bought by the piece; ShopBuyRequest); a tap on a row opens the item popup with the buy buttons, the server checks everything again.
    /// The list is virtual: only the rows that are on the screen exist (about fifteen, reused while scrolling), so opening a shop of 165 skins costs no more than a shop of five items.
    /// The hierarchy is built by BattleBall/Build Login Scene.
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
        const float RowH = 50f, RowStep = 52f;
        static readonly Color NameWhite = Color.white, PriceGreen = new Color(0.20f, 0.85f, 0.20f), PriceGold = new Color(1f, 0.93f, 0.25f), DescBlue = new Color(0.10f, 0.35f, 0.85f);
        static readonly Color RowColor = new Color(0.80f, 0.71f, 0.58f), RowPressed = new Color(0.98f, 1f, 0.29f), RowOwned = new Color(0.70f, 0.65f, 0.58f);
        static readonly string[] Races = { "EARTH", "NAMEC", "XAYDA" };          // by SkinInfo.gender_id: 0 Trái Đất, 1 Namếc, 2 Xayda
        static readonly string[] RaceTitles = { "Trái Đất", "Namếc", "Xayda" };

        sealed class Row
        {
            public RectTransform rt;
            public Image bg, icon, gem;
            public Text name, price, own, desc;
            public int index = -1;                  // the item of the open tab this row shows now (-1: none)
            public bool skinPending, iconPending;   // its picture is not loaded yet (Update loads a few a frame, so opening a shop never stalls on 18 pictures at once)
            public uint pendingId;
        }

        ShopContent _content;
        PlayerDataSnapshot _snapshot;
        int _tab;
        object _openKey;                        // the item whose popup is open (its row is yellow): a costume id for a skin, "kind:template" for the rest
        bool _open, _busy, _wired;
        float _x = -PanelWidth;                 // left edge of the panel: 0 open, -PanelWidth away
        Sprite _goldSprite;
        readonly List<Row> _rows = new List<Row>();
        readonly List<ShopItem> _items = new List<ShopItem>();                    // the items of the open tab
        readonly Dictionary<uint, Sprite> _skinIcons = new Dictionary<uint, Sprite>();
        readonly Dictionary<uint, Sprite> _icons = new Dictionary<uint, Sprite>();

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
            // the list is virtual: the rows are put by hand, so the automatic layout of the scene stands aside
            var layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout != null) layout.enabled = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            scroll.onValueChanged.AddListener(_ => UpdateRows(false));
            var goldIcon = goldText != null && goldText.transform.parent != null ? goldText.transform.parent.Find("GoldIcon") : null;
            if (goldIcon != null) _goldSprite = goldIcon.GetComponent<Image>().sprite;
        }

        void Awake() { Wire(); }

        public double DebugOpenMs, DebugIconMs;

        public void Open(ShopContent content, PlayerDataSnapshot snapshot, bool immediate = false)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            OpenInner(content, snapshot, immediate);
            DebugOpenMs = watch.Elapsed.TotalMilliseconds;
        }

        void OpenInner(ShopContent content, PlayerDataSnapshot snapshot, bool immediate)
        {
            Wire();
            _content = content;
            _snapshot = snapshot;
            _tab = 0;
            _open = true;
            _openKey = null;
            root.SetActive(true);
            if (immediate) { _x = 0f; Apply(); }
            LayoutTabs();
            RefreshAll(true);
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
            if (_open) RefreshAll(false);
        }

        /// <summary>Makes the rows ahead of the first opening (the menu calls it at start): creating them is most of what opening a shop costs.</summary>
        public void Warm()
        {
            Wire();
            EnsurePool();
        }

        /// <summary>Loads the pictures the rows are waiting for, a few a frame (about 4 ms), the first rows first.</summary>
        void LoadPendingIcons()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var row in _rows)
            {
                if (!row.skinPending && !row.iconPending) continue;
                if (!row.rt.gameObject.activeSelf || row.index < 0) { row.skinPending = row.iconPending = false; continue; }
                Sprite sp = row.skinPending ? SkinIcon(row.pendingId) : IconOf(row.pendingId);
                row.skinPending = row.iconPending = false;
                row.icon.sprite = sp;
                row.icon.enabled = sp != null;
                if (watch.Elapsed.TotalMilliseconds > 4.0) break;
            }
        }

        void Update()
        {
            LoadPendingIcons();
            float target = _open ? 0f : -PanelWidth;
            if (!Mathf.Approximately(_x, target))
            {
                _x = Mathf.MoveTowards(_x, target, PanelWidth * 5f * Time.unscaledDeltaTime);
                Apply();
                if (!_open && Mathf.Approximately(_x, -PanelWidth)) root.SetActive(false);
            }
            if (_openKey != null && popup != null && !popup.IsOpen) { _openKey = null; UpdateRows(true); }
        }

        void Apply()
        {
            panelRect.anchoredPosition = new Vector2(_x, 0f);
            if (menu != null) menu.ApplyInsets();
        }

        // ---------------------------------------------------------------------------------------------------------------- tabs and rows

        /// <summary>The scene has four tab buttons; a shop with more tabs (Bunma has five) gets copies of the first one.</summary>
        void EnsureTabs(int n)
        {
            if (tabs.Length >= n) return;
            var b = new List<Button>(tabs);
            var im = new List<Image>(tabImages);
            var lb = new List<Text>(tabLabels);
            while (b.Count < n)
            {
                var copy = Instantiate(tabs[0].gameObject, tabs[0].transform.parent);
                copy.name = "Tab" + b.Count;
                var button = copy.GetComponent<Button>();
                button.onClick.RemoveAllListeners();
                int index = b.Count;
                button.onClick.AddListener(() => SelectTab(index));
                b.Add(button);
                im.Add(copy.GetComponent<Image>());
                lb.Add(copy.GetComponentInChildren<Text>());
            }
            tabs = b.ToArray();
            tabImages = im.ToArray();
            tabLabels = lb.ToArray();
        }

        void LayoutTabs()
        {
            int n = _content != null ? _content.Tabs.Count : 0;
            EnsureTabs(n);
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
            RefreshAll(true);
        }

        bool Owns(uint costumeId)
        {
            if (_snapshot != null) foreach (var s in _snapshot.Skins) if (s.CostumeId == costumeId) return true;
            return false;
        }

        static bool IsSkin(ShopItem item) { return item.Kind.Length == 0 || item.Kind == "SKIN"; }

        /// <summary>How many of this material (a fragment) the account has.</summary>
        ulong Have(uint materialId)
        {
            if (_snapshot != null) foreach (var m in _snapshot.Materials) if (m.TemplateId == materialId) return m.Quantity;
            return 0;
        }

        static object KeyOf(ShopItem item)
        {
            return IsSkin(item) ? (object)item.Skin.CostumeId : item.Kind + ":" + item.TemplateId;
        }

        /// <summary>The header, the tabs, the money and the rows of the open tab. <paramref name="toTop"/>: the list goes back to its first item (a new tab).</summary>
        void RefreshAll(bool toTop)
        {
            if (_content == null) return;
            for (int i = 0; i < tabs.Length; i++)
            {
                if (!tabs[i].gameObject.activeSelf) continue;
                tabImages[i].sprite = i == _tab ? tabOn : tabIdle;
                tabLabels[i].color = i == _tab ? new Color(0.10f, 0.35f, 0.12f) : new Color(0.45f, 0.18f, 0.05f);
            }
            int total = 0, owned = 0, skins = 0;
            bool gear = false, stones = false;
            foreach (var t in _content.Tabs)
            {
                foreach (var it in t.Items)
                {
                    total++;
                    if (IsSkin(it)) { skins++; if (Owns(it.Skin.CostumeId)) owned++; }
                    else if (it.Kind == "MATERIAL") stones = true;
                    else gear = true;
                }
            }
            var sells = new List<string>();
            if (skins > 0) sells.Add("cải trang");
            if (gear) sells.Add("quần áo");
            if (stones) sells.Add("đá");
            string line2 = "Bán " + string.Join(" và ", sells) + (skins > 0 && !gear && !stones ? " bằng ngọc" : "");
            string line3 = skins > 0 ? "Đã có " + owned + " / " + skins : total + " mặt hàng";
            headerText.text = _content.NpcName + "\n" + line2 + "\n" + line3;
            var npcIcon = IconOf(_content.PortraitIcon);
            portrait.sprite = npcIcon;
            portrait.enabled = npcIcon != null;
            if (_snapshot != null)
            {
                goldText.text = Money(_snapshot.Gold);
                gemText.text = Money(_snapshot.Gems);
            }
            _items.Clear();
            if (_tab < _content.Tabs.Count) _items.AddRange(_content.Tabs[_tab].Items);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, Mathf.Max(0f, _items.Count * RowStep));
            if (toTop) { content.anchoredPosition = Vector2.zero; scroll.StopMovement(); }
            UpdateRows(true);
        }

        /// <summary>Puts the rows that are in view on their items (and hides the others). <paramref name="rebind"/>: draw them again even if they show the same item.</summary>
        void UpdateRows(bool rebind)
        {
            if (_content == null) return;
            EnsurePool();
            int first = Mathf.Max(0, Mathf.FloorToInt(content.anchoredPosition.y / RowStep));
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                int index = first + i;
                bool has = index < _items.Count;
                row.rt.gameObject.SetActive(has);
                if (!has) { row.index = -1; continue; }
                if (row.index == index && !rebind) continue;
                row.index = index;
                row.rt.anchoredPosition = new Vector2(0f, -index * RowStep);
                Bind(row, _items[index], index);
            }
        }

        void EnsurePool()
        {
            float height = ((RectTransform)transform).rect.height;
            int want = Mathf.Max(14, Mathf.CeilToInt((height > 10f ? height : 720f) / RowStep) + 2);
            while (_rows.Count < want) _rows.Add(NewRow());
        }

        Row NewRow()
        {
            var row = new Row();
            var go = NewRect("Row", content);
            row.rt = go;
            go.anchorMin = new Vector2(0f, 1f);
            go.anchorMax = new Vector2(1f, 1f);
            go.pivot = new Vector2(0.5f, 1f);
            go.sizeDelta = new Vector2(0f, RowH);
            row.bg = go.gameObject.AddComponent<Image>();
            row.bg.sprite = rowSprite;
            var button = go.gameObject.AddComponent<Button>();
            button.targetGraphic = row.bg;
            var colors = button.colors;
            colors.pressedColor = RowPressed;
            button.colors = colors;
            var captured = row;
            button.onClick.AddListener(() => { if (captured.index >= 0 && captured.index < _items.Count) OnItem(_items[captured.index]); });

            // the picture in its dark cell (Nro: the item icon in a 24 px cell on the left of the row)
            var cell = NewRect("Cell", go);
            Place(cell, 2f, 2f, 46f, 46f);
            var ci = cell.gameObject.AddComponent<Image>();
            ci.sprite = cellSprite;
            ci.type = Image.Type.Sliced;
            ci.raycastTarget = false;
            var ir = NewRect("Icon", cell);
            Stretch(ir, 6f, 5f);
            row.icon = ir.gameObject.AddComponent<Image>();
            row.icon.preserveAspect = true;
            row.icon.raycastTarget = false;

            row.name = NewText(go, "", 16, NameWhite, TextAnchor.MiddleLeft, true);
            Place(row.name.rectTransform, 54f, 2f, 170f, 24f);
            row.name.horizontalOverflow = HorizontalWrapMode.Overflow;
            Outline(row.name.gameObject);
            row.own = NewText(go, "Đã có", 15, new Color(0.30f, 0.14f, 0.05f), TextAnchor.MiddleRight, true);
            Place(row.own.rectTransform, 224f, 2f, 90f, 24f);
            row.price = NewText(go, "", 16, PriceGreen, TextAnchor.MiddleRight, true);
            Place(row.price.rectTransform, 190f, 2f, 96f, 24f);
            Outline(row.price.gameObject);
            var gem = NewRect("Money", go);
            Place(gem, 289f, 5f, 22f, 18f);
            row.gem = gem.gameObject.AddComponent<Image>();
            row.gem.preserveAspect = true;
            row.gem.raycastTarget = false;
            row.desc = NewText(go, "", 13, DescBlue, TextAnchor.UpperLeft, false);
            Place(row.desc.rectTransform, 54f, 26f, 258f, 22f);
            row.desc.horizontalOverflow = HorizontalWrapMode.Overflow;
            return row;
        }

        void Bind(Row row, ShopItem item, int index)
        {
            bool skin = IsSkin(item);
            bool owned = skin && Owns(item.Skin.CostumeId);
            bool open = _openKey != null && _openKey.Equals(KeyOf(item));
            row.bg.color = open ? RowPressed : owned ? RowOwned : RowColor;
            // the picture: at once when it was loaded before, otherwise a moment later (LoadPendingIcons)
            uint iconKey = skin ? item.Skin.CostumeId : item.IconId;
            Sprite icon = null;
            bool known = skin ? _skinIcons.TryGetValue(iconKey, out icon) && icon != null : _icons.TryGetValue(iconKey, out icon) && icon != null;
            row.icon.sprite = known ? icon : null;
            row.icon.enabled = known;
            row.skinPending = !known && skin;
            row.iconPending = !known && !skin && iconKey != 0;
            row.pendingId = iconKey;
            string name;
            if (skin)
            {
                name = item.Skin.Name.Length > 0 ? item.Skin.Name : "Cải trang";
                if (item.Skin.FusionMainOnly) name += " (hợp thể)";
            }
            else name = item.Name;
            row.name.text = name;
            bool exchange = item.Kind == "EXCHANGE";
            row.own.gameObject.SetActive(owned);
            row.price.gameObject.SetActive(!owned);
            row.gem.gameObject.SetActive(!owned);
            if (exchange)
            {
                ulong have = Have(item.FragmentTemplateId);
                row.price.text = Math.Min(have, 9999UL) + "/" + item.FragmentCost;
                row.price.color = have >= item.FragmentCost ? PriceGreen : new Color(1f, 0.55f, 0.45f);
                row.gem.sprite = IconOf(item.FragmentIconId);                    // the fragment (the Nro "Mảnh áo", "Mảnh quần"...) the piece is paid with
                row.gem.enabled = row.gem.sprite != null;
            }
            else if (!owned)
            {
                bool gems = item.GemPrice > 0;
                row.price.text = Money(gems ? item.GemPrice : item.GoldPrice);
                row.price.color = gems ? PriceGreen : PriceGold;
                row.gem.sprite = gems ? gemSprite : _goldSprite;
                row.gem.enabled = row.gem.sprite != null;
            }
            row.desc.text = skin ? FirstLines(item.Skin) : FirstLine(item.Description);
        }

        Sprite SkinIcon(uint costumeId)
        {
            Sprite sp;
            if (!_skinIcons.TryGetValue(costumeId, out sp) || sp == null)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                sp = BbCostumeAvatar.Of((int)costumeId);
                _skinIcons[costumeId] = sp;
                DebugIconMs += watch.Elapsed.TotalMilliseconds;
            }
            return sp;
        }

        Sprite IconOf(uint iconId)
        {
            if (iconId == 0 || NroSpriteResolver.Instance == null) return null;
            Sprite sp;
            if (!_icons.TryGetValue(iconId, out sp) || sp == null) { sp = NroSpriteResolver.Instance.ResolveSprite((int)iconId, BbAssetCache.Scale); _icons[iconId] = sp; }
            return sp;
        }

        /// <summary>The first line of a description (the bonus of a piece of gear, what a stone does).</summary>
        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int nl = text.IndexOf('\n');
            return nl < 0 ? text : text.Substring(0, nl);
        }

        /// <summary>The bonus lines of a skin on one line of the row (the first two), or who it is for when it has none.</summary>
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
                    if (IsSkin(item) && item.Skin.CostumeId == costumeId) { SelectTab(t); OnItem(item); return; }
        }

        /// <summary>For the screenshot tool: open this tab, then tap the item at this index of it.</summary>
        public void DemoTapIndex(int tab, int index)
        {
            if (_content == null || tab >= _content.Tabs.Count) return;
            SelectTab(tab);
            if (index < _items.Count) OnItem(_items[index]);
        }

        /// <summary>For the scripted run in Play mode: the first skin the account does not own yet in the open tab (0 when there is none).</summary>
        public uint DebugFirstUnowned()
        {
            foreach (var item in _items) if (IsSkin(item) && !Owns(item.Skin.CostumeId)) return item.Skin.CostumeId;
            return 0;
        }

        /// <summary>For the scripted run in Play mode: buy the skin of this costume.</summary>
        public void DebugBuy(uint costumeId)
        {
            if (_content == null) return;
            foreach (var t in _content.Tabs)
                foreach (var item in t.Items)
                    if (IsSkin(item) && item.Skin.CostumeId == costumeId) { BuySkin(item); return; }
        }

        /// <summary>For the scripted run in Play mode: buy this much of the item at this index of the open tab (gear or a stone).</summary>
        public void DebugBuyAt(int index, uint quantity)
        {
            if (index >= 0 && index < _items.Count) BuyGoods(_items[index], quantity);
        }

        public int DebugRowCount { get { return _items.Count; } }
        public int DebugLiveRows { get { int n = 0; foreach (var r in _rows) if (r.rt.gameObject.activeSelf) n++; return n; } }

        // ---------------------------------------------------------------------------------------------------------------- popup and purchase

        void OnItem(ShopItem item)
        {
            _openKey = KeyOf(item);
            popup.areaLeftOverride = 0f;                            // the shop stands on the left; the popup stands under it
            UpdateRows(true);
            if (IsSkin(item)) { ShowSkinPopup(item); return; }
            var lines = new StringBuilder();
            string[] parts = item.Description.Split('\n');
            lines.Append("<color=#1E8F1E>").Append(parts[0]).Append("</color>");
            var desc = new StringBuilder();
            for (int i = 1; i < parts.Length; i++) { if (desc.Length > 0) desc.Append('\n'); desc.Append(parts[i]); }
            var specs = new List<BbPopup.ButtonSpec>();
            var amounts = new List<uint>();
            if (item.Kind == "GEAR")
            {
                specs.Add(new BbPopup.ButtonSpec("Mua bằng\n" + Money(item.GoldPrice) + " vàng", true, true));
                amounts.Add(1);
            }
            else if (item.Kind == "EXCHANGE")
            {
                specs.Add(new BbPopup.ButtonSpec("Đổi\n" + item.FragmentCost + " mảnh", true, true));
                amounts.Add(1);
                lines.Append("\n<color=#7A5A3A>Đang có ").Append(Have(item.FragmentTemplateId)).Append(" / ").Append(item.FragmentCost).Append(" mảnh</color>");
            }
            else
            {
                foreach (uint q in new uint[] { 1, 10, 100 })
                {
                    specs.Add(new BbPopup.ButtonSpec("Mua " + q + "\n" + Money((item.GemPrice > 0 ? item.GemPrice : item.GoldPrice) * q) + (item.GemPrice > 0 ? " ngọc" : " vàng"), true, q == 1));
                    amounts.Add(q);
                }
            }
            popup.Show(IconOf(item.IconId), item.Name, lines.ToString(), desc.ToString(), null, null, specs, i => { if (i < amounts.Count) BuyGoods(item, amounts[i]); }, null, 0);
        }

        void ShowSkinPopup(ShopItem item)
        {
            var skin = item.Skin;
            bool owned = Owns(skin.CostumeId);
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
                null, null, specs, i => { if (i == 0) BuySkin(item); }, null, (int)skin.CostumeId);
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

        async void BuySkin(ShopItem item)
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
                RefreshAll(false);
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

        /// <summary>Buys a piece of gear or some stones: the server prices them for the account's highest character, checks the shop sells them, takes the money and pushes a new snapshot.</summary>
        async void BuyGoods(ShopItem item, uint quantity)
        {
            if (_busy) return;
            popup.Hide();
            if (menu.IsDemo || !BbSession.IsActive) { menu.Toast("Cần kết nối máy chủ để mua"); return; }
            _busy = true;
            try
            {
                var result = await BbSession.Client.ShopBuyAsync(_content.NpcId, item.Kind, item.TemplateId, quantity);
                string done = item.Kind == "EXCHANGE" ? "Đã đổi lấy " + item.Name : "Đã mua " + (item.Kind == "GEAR" ? item.Name : quantity + " " + item.Name);
                menu.Toast(result.Ok ? done : result.Error == ActionError.NotEnoughMaterial ? "Không đủ mảnh" : BbMenuUi.ActionText(result));
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
    }
}
