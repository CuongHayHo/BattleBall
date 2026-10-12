using System;
using System.Collections.Generic;
using System.Text;
using BattleBall.Proto;
using NroRig;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// What Bà Hạt Mít does for the gear (owner request 2026-10-10: "đến NPC bà hạt mít bấm vào ra lựa chọn ... người dùng bỏ đá và đồ tương ứng dùng cho chức năng muốn dùng sau đó nâng cấp",
    /// the combine panel of the original game, docs 15.22): the piece is raised with upgrade stones, one of its GEN lines is changed to another random set with a GEN stone, or one more
    /// GEN line is opened with GEN slot stones. The player picks the piece (from the bag or worn) and the stone in his own panel, which stands on the right beside this one, they sit in
    /// the two rows here, and the button does it. Everything is decided by the server (the same requests as before); this panel only collects the choice and tells the result.
    /// The hierarchy is built by BattleBall/Build Login Scene.
    /// </summary>
    public sealed class BbSmithPanel : MonoBehaviour
    {
        public enum Mode { Upgrade, Reroll, Unlock, Star, Crystal }

        public GameObject root;
        public RectTransform panelRect;
        public Image portrait;
        public Text headerText;
        public Button closeButton;
        public Text goldText, gemText;
        public Text titleText;
        public Button itemRow, stoneRow;
        public Image itemIcon, stoneIcon;
        public Text itemName, itemDesc, stoneName, stoneDesc;
        public Button[] lineButtons;
        public Text[] lineLabels;
        public RectTransform infoRect;
        public Text infoText;
        public Button actionButton;
        public Text actionLabel, resultText;
        public Button altButton;                                           // "Đổi loại" beside the main button (Ép sao on a filled stat line)
        public Text altLabel;
        public Sprite portraitSprite;
        public BbMenuUi menu;

        /// <summary>Raised when the player closes the panel (the X).</summary>
        public Action Closed;

        const float PanelWidth = 320f;
        const float LineH = 40f;
        const uint PortraitIcon = 1410;                                    // Nro avatar of Bà Hạt Mít (npc_template 21)
        static readonly Color Good = new Color(0.12f, 0.56f, 0.12f), Bad = new Color(0.72f, 0.12f, 0.12f), Blue = new Color(0.10f, 0.35f, 0.85f), Ink = new Color(0.18f, 0.10f, 0.04f);
        static readonly Color RowOn = new Color(0.98f, 1f, 0.29f), RowOff = new Color(0.906f, 0.875f, 0.824f);

        Mode _mode;
        PlayerDataSnapshot _snapshot;
        bool _open, _wired, _busy;
        float _x = -PanelWidth;
        ulong _itemId;
        uint _stoneTemplate;                                               // 0 = no stone in the row
        int _line;
        bool _sticky;                                                      // the text under the rows is a result: it stays until the player changes the choice
        string _watchKey;                                                  // what the piece looked like when the request left: the result is told when it changes
        float _watchUntil;

        public bool IsOpen { get { return _open; } }
        public float VisibleWidth { get { return Mathf.Clamp(PanelWidth + _x, 0f, PanelWidth); } }
        public Mode CurrentMode { get { return _mode; } }
        public bool DebugHasItem { get { return _itemId != 0; } }

        /// <summary>The name of what the panel does now ("Nâng cấp", "Đổi GEN", ...): the button of the popup of a piece.</summary>
        public string ModeName { get { return ModeTitle(_mode); } }

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            closeButton.onClick.AddListener(Close);
            itemRow.onClick.AddListener(() => { _itemId = 0; _line = 0; _sticky = false; Refresh(); });
            stoneRow.onClick.AddListener(() => { _stoneTemplate = 0; _sticky = false; Refresh(); });
            actionButton.onClick.AddListener(Run);
            if (altButton != null) altButton.onClick.AddListener(RunAlt);
            for (int i = 0; i < lineButtons.Length; i++)
            {
                int line = i;
                lineButtons[i].onClick.AddListener(() => { _line = line; _sticky = false; Refresh(); });
            }
        }

        void Awake() { Wire(); }

        public void Open(Mode mode, PlayerDataSnapshot snapshot, bool immediate = false)
        {
            Wire();
            _mode = mode;
            _snapshot = snapshot;
            _itemId = 0;
            _stoneTemplate = 0;
            _line = 0;
            _watchKey = null;
            _sticky = false;
            resultText.text = "";
            _open = true;
            root.SetActive(true);
            if (immediate) { _x = 0f; Apply(); }
            if (portrait != null)
            {
                var resolver = NroSpriteResolver.Instance;
                portrait.sprite = resolver != null ? resolver.ResolveSprite((int)PortraitIcon, BbAssetCache.Scale) : portraitSprite;
                portrait.enabled = portrait.sprite != null;
            }
            titleText.text = ModeTitle(mode);
            headerText.text = "Bà Hạt Mít\n" + (mode == Mode.Upgrade ? "Ta sẽ phù phép\ncho trang bị của ngươi\ntrở lên mạnh mẽ"
                                              : mode == Mode.Reroll ? "Ta sẽ đổi một dòng GEN\ncủa trang bị ngươi\nsang bộ khác"
                                              : mode == Mode.Star ? "Ta sẽ ép đá chỉ số\nvào trang bị của ngươi\nđể thêm sức mạnh"
                                              : mode == Mode.Crystal ? "Ta sẽ pha lê hóa\ntrang bị của ngươi\nthành dòng đỏ"
                                              : "Ta sẽ mở thêm\nmột ô GEN cho\ntrang bị của ngươi");
            Refresh();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (Closed != null) Closed();
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
        }

        void Apply()
        {
            panelRect.anchoredPosition = new Vector2(_x, 0f);
            if (menu != null) menu.ApplyInsets();
        }

        // ---------------------------------------------------------------------------------------------------------------- what the player puts in

        /// <summary>The player taps a piece in his panel (bag or worn): it goes into the first row.</summary>
        public void PutItem(ItemInfo it)
        {
            if (!_open || it == null) return;
            _itemId = it.ItemId;
            _line = 0;
            _sticky = false;
            Refresh();
        }

        /// <summary>The player taps a stone in his bag: it goes into the second row when it is the stone this function uses.</summary>
        public void PutStone(MaterialAmount stone)
        {
            if (!_open || stone == null) return;
            if (stone.Kind != StoneKind)
            {
                resultText.color = Bad;
                resultText.text = "Đá này không dùng cho chức năng " + titleText.text.ToLower();
                _sticky = true;
                return;
            }
            _stoneTemplate = stone.TemplateId;
            _sticky = false;
            Refresh();
        }

        static string ModeTitle(Mode mode) { return mode == Mode.Upgrade ? "Nâng cấp" : mode == Mode.Reroll ? "Đổi GEN" : mode == Mode.Star ? "Ép sao" : mode == Mode.Crystal ? "Pha lê hóa" : "Mở ô GEN"; }

        string StoneKind { get { return _mode == Mode.Upgrade ? "UPGRADE_STONE" : _mode == Mode.Reroll ? "GEN_STONE" : _mode == Mode.Star ? "STAT_STONE" : _mode == Mode.Crystal ? "CRYSTAL_STONE" : "GEN_SLOT_STONE"; } }

        MaterialAmount StoneOwned()
        {
            if (_snapshot != null) foreach (var m in _snapshot.Materials) if (m.Kind == StoneKind && m.Quantity > 0) return m;
            return null;
        }

        MaterialAmount StoneRow()
        {
            if (_stoneTemplate == 0 || _snapshot == null) return null;
            foreach (var m in _snapshot.Materials) if (m.TemplateId == _stoneTemplate) return m;
            return null;
        }

        public void OnSnapshot(PlayerDataSnapshot snapshot)
        {
            _snapshot = snapshot;
            if (_open) Refresh();
        }

        // ---------------------------------------------------------------------------------------------------------------- the rows and the button

        const double UpgradeStep = 5.0, UpgradeMax = 75.0;

        void Refresh()
        {
            if (!_open) return;
            var it = menu.ItemById(_itemId);
            if (_itemId != 0 && it == null && Time.unscaledTime > _watchUntil) _itemId = 0;      // sold or gone
            Sprite sp = it != null ? BbMenuUi.ItemIcon(it) : null;
            itemIcon.sprite = sp;
            itemIcon.enabled = sp != null;
            itemName.text = it != null ? it.Name : "Chọn đồ";
            itemDesc.text = it != null ? ItemLine(it) : "Chạm một món trong hành trang hoặc đang mặc (bên phải)";
            itemRow.GetComponent<Image>().color = it != null ? RowOn : RowOff;

            var stone = StoneRow();
            if (stone != null && stone.Quantity == 0) { stone = null; _stoneTemplate = 0; }
            Sprite ss = stone != null ? BbMenuUi.MaterialIcon(stone) : null;
            stoneIcon.sprite = ss;
            stoneIcon.enabled = ss != null;
            stoneName.text = stone != null ? stone.Name : StoneHint();
            stoneDesc.text = stone != null ? "Có " + stone.Quantity + " viên" : "Chạm viên đá trong hành trang (bên phải)";
            stoneRow.GetComponent<Image>().color = stone != null ? RowOn : RowOff;

            // the rows to choose from: the GEN lines (Đổi GEN) or the four extra lines of the piece (Ép sao)
            int rowCount = it == null ? 0 : _mode == Mode.Reroll ? it.Gens.Count : _mode == Mode.Star ? 4 : 0;
            rowCount = Mathf.Min(rowCount, lineButtons.Length);
            if (_line >= rowCount) _line = 0;
            var slotLine = _mode == Mode.Star && it != null ? LineOf(it, _line + 1) : null;
            int cost = Cost(it, slotLine);
            bool haveStone = stone != null && stone.Quantity >= (ulong)Mathf.Max(1, cost);
            string why = null;
            var info = new StringBuilder();
            for (int i = 0; i < lineButtons.Length; i++)
            {
                bool shown = i < rowCount;
                lineButtons[i].gameObject.SetActive(shown);
                if (!shown) continue;
                if (_mode == Mode.Star) lineLabels[i].text = StarLabel(it, i);
                else { var g = it.Gens[i]; lineLabels[i].text = "GEN " + (i + 1) + ": " + (g.GenKey.Length == 0 ? "(trống)" : g.Name); }
                lineLabels[i].color = _mode == Mode.Star && i == 3 ? new Color(0.72f, 0.12f, 0.12f) : Ink;
                lineButtons[i].GetComponent<Image>().color = i == _line ? RowOn : RowOff;
            }
            infoRect.anchoredPosition = new Vector2(infoRect.anchoredPosition.x, -(InfoTop + (rowCount > 0 ? rowCount * LineH + 6f : 0f)));

            if (it == null) why = "Chọn món đồ muốn " + (_mode == Mode.Upgrade ? "nâng cấp" : _mode == Mode.Reroll ? "đổi GEN" : _mode == Mode.Star ? "ép sao" : _mode == Mode.Crystal ? "pha lê hóa" : "mở thêm ô GEN") + ".";
            else if (_mode == Mode.Star) why = StarInfo(it, slotLine, cost, info);
            else if (_mode == Mode.Crystal) why = CrystalInfo(it, cost, info);
            else if (_mode == Mode.Upgrade)
            {
                bool maxed = it.UpgradePercent + 1e-9 >= UpgradeMax;
                double main = it.MainStat != null ? it.MainStat.Value : 0, now = it.UpgradePercent;
                double next = main / (1 + now / 100.0) * (1 + (now + UpgradeStep) / 100.0);
                info.Append("Nâng cấp: +").Append(Pct(now)).Append("% → +").Append(maxed ? Pct(now) : Pct(now + UpgradeStep)).Append("% (tối đa ").Append(Pct(UpgradeMax)).Append("%)");
                if (it.MainStat != null && !maxed) info.Append("\n").Append(StatName(it.MainStat.Text)).Append(": ").Append(Num(main)).Append(" → ").Append(Num(next));
                info.Append("\nTốn 1 đá nâng cấp.");
                if (maxed) why = "Món này đã nâng cấp tối đa.";
            }
            else if (_mode == Mode.Reroll)
            {
                var g = _line < it.Gens.Count ? it.Gens[_line] : null;
                info.Append("Đổi dòng GEN ").Append(_line + 1).Append(g != null && g.GenKey.Length > 0 ? " (" + g.Name + ")" : " (trống)")
                    .Append(" sang một bộ ngẫu nhiên của ").Append(Planet(it.Race)).Append(", không trùng dòng khác của món.\nTốn ").Append(cost).Append(" đá GEN.");
            }
            else
            {
                if (it.GenUnlockCost == 0) { why = "Món này đã đủ " + it.GenSlots + " ô GEN."; info.Append("Món có ").Append(it.GenSlots).Append(" ô GEN, nhiều nhất rồi."); }
                else info.Append("Món có ").Append(it.GenSlots).Append(" ô GEN. Mở ô thứ ").Append(it.GenSlots + 1).Append(" tốn ").Append(it.GenUnlockCost).Append(" đá mở ô.");
            }
            if (why == null && !haveStone) why = stone == null ? "Chạm viên đá trong hành trang để bỏ vào." : "Không đủ đá (cần " + cost + ").";
            infoText.text = info.ToString();
            actionButton.interactable = why == null && !_busy;
            actionLabel.text = _mode == Mode.Upgrade ? "Nâng cấp" : _mode == Mode.Reroll ? "Đổi GEN"
                             : _mode == Mode.Star ? (slotLine == null && _line < 3 ? "Thêm dòng" : "Ép sao")
                             : _mode == Mode.Crystal ? (it != null && LineOf(it, 4) != null ? "Pha lê hóa lại" : "Pha lê hóa") : "Mở ô";
            if (altButton != null)
            {
                bool canRetype = _mode == Mode.Star && it != null && slotLine != null && _line < 3 && stone != null && stone.Quantity >= (ulong)RetypeCost();
                altButton.gameObject.SetActive(_mode == Mode.Star && it != null && slotLine != null && _line < 3);
                altButton.interactable = canRetype && !_busy;
                altLabel.text = "Đổi loại";
                bool two = altButton.gameObject.activeSelf;                            // two buttons side by side while the second one shows
                var ar = (RectTransform)actionButton.transform;
                ar.anchoredPosition = new Vector2(two ? -80f : 0f, ar.anchoredPosition.y);
                ar.sizeDelta = new Vector2(two ? 140f : 150f, ar.sizeDelta.y);
            }
            if (_watchKey == null && !_sticky) { resultText.color = Ink; resultText.text = why ?? ""; }
            if (snapshotMoney()) { goldText.text = Money(_snapshot.Gold); gemText.text = Money(_snapshot.Gems); }
            TellResult(it);
        }

        const float InfoTop = 290f;

        bool snapshotMoney() { return _snapshot != null; }

        int RetypeCost() { return _snapshot != null && _snapshot.StatStoneRules != null ? (int)_snapshot.StatStoneRules.RetypeStones : 1; }

        static StatLine LineOf(ItemInfo it, int lineNo)
        {
            foreach (var l in it.Lines) if (l.LineNo == lineNo) return l;
            return null;
        }

        static string StarLabel(ItemInfo it, int i)
        {
            var l = LineOf(it, i + 1);
            if (i < 3) return l == null ? "Dòng " + (i + 1) + ": (trống)" : l.Text + " / " + Pct(l.Cap) + "%";
            return l == null ? "Dòng đỏ: (chưa pha lê hóa)" : "Đỏ: " + l.Text + " / " + Pct(l.Cap) + "%";
        }

        /// <summary>The text of the Ép sao panel for the chosen row; the return value is why the button cannot be pressed (null = it can).</summary>
        string StarInfo(ItemInfo it, StatLine l, int cost, StringBuilder info)
        {
            if (_line == 3 && l == null)
            {
                info.Append("Món chưa có dòng đỏ. Dùng chức năng Pha lê hóa (đá pha lê) để tạo, rồi quay lại đây nâng nó bằng đá chỉ số.");
                return "Chưa có dòng đỏ.";
            }
            if (l == null)
            {
                info.Append("Thêm một dòng chỉ số ngẫu nhiên (Sức đánh, HP, Giáp, Ki, Chí mạng...), không trùng dòng khác của món, khởi đầu ").Append("+1,5 đến 2%.\nTốn ").Append(cost).Append(" đá chỉ số.");
                return null;
            }
            bool red = _line == 3;
            if (l.SuccessPct <= 0)
            {
                info.Append(l.Text).Append(" đã đạt tối đa ").Append(Pct(l.Cap)).Append("%.");
                return "Dòng này đã đạt tối đa.";
            }
            info.Append(red ? "Dòng đỏ" : "Dòng " + (_line + 1)).Append(": ").Append(l.Text).Append(" (tối đa ").Append(Pct(l.Cap)).Append("%)\nÉp sao: +").Append(Pct(l.StepMin)).Append(" đến ").Append(Pct(l.StepMax))
                .Append("%, thành công ").Append(Pct(l.SuccessPct)).Append("% (đá mất dù thành hay bại). Tốn ").Append(cost).Append(" đá chỉ số.");
            AppendPreview(it, l, info);
            if (!red) info.Append("\nĐổi loại: sang một chỉ số khác chưa có trên món, giữ nguyên số. Tốn ").Append(RetypeCost()).Append(" đá.");
            return null;
        }

        // ---- the preview: what a success would make of the line and, for a worn piece, of the stat of the character (owner request 2026-10-11)

        static string ShortStat(string key)
        {
            switch (key)
            {
                case "ATK_PCT": return "Sức đánh";
                case "HP_PCT": return "HP";
                case "KI_PCT": return "Ki";
                case "ARMOR_PCT": return "Giáp";
                case "CRIT_RATE_PCT": return "Tỷ lệ chí mạng";
                case "CRIT_DMG_PCT": return "Sát thương chí mạng";
                default: return key;
            }
        }

        BattleBall.Proto.CharacterInfo WearerOf(ItemInfo it)
        {
            if (_snapshot == null) return null;
            foreach (var c in _snapshot.Characters) foreach (var e in c.EquippedItems) if (e.ItemId == it.ItemId) return c;
            return null;
        }

        static double TotalOf(BattleBall.Proto.CharacterInfo c, string key)
        {
            foreach (var b in c.Bonuses) if (b.StatKey == key) return b.Value;
            return 0;
        }

        /// <summary>The stat of the character the line feeds, now and after a raise of {@code delta} percent (the % bonuses add up, so the factor is (1 + (total + delta)) / (1 + total)); null when it has none.</summary>
        static double? StatAfter(BattleBall.Proto.CharacterInfo c, string key, double delta, out double now)
        {
            now = 0;
            if (c.Stats == null) return null;
            double total = TotalOf(c, key), factor = (1 + (total + delta) / 100.0) / (1 + total / 100.0);
            switch (key)
            {
                case "ATK_PCT": now = c.Stats.Atk; return now * factor;
                case "HP_PCT": now = c.Stats.HpMax; return now * factor;
                case "KI_PCT": now = c.Stats.KiMax; return now * factor;
                case "ARMOR_PCT": now = c.Stats.Armor; return now * factor;
                case "CRIT_RATE_PCT": now = c.Stats.CritRatePct; return Math.Min(100.0, now + delta);
                case "CRIT_DMG_PCT": now = c.Stats.CritDmgPct; return now + delta;
                default: return null;
            }
        }

        void AppendPreview(ItemInfo it, StatLine l, StringBuilder info)
        {
            double lo = Math.Min(l.Cap, Math.Round((l.Value + l.StepMin) * 10.0) / 10.0), hi = Math.Min(l.Cap, Math.Round((l.Value + l.StepMax) * 10.0) / 10.0);
            string name = ShortStat(l.StatKey);
            info.Append("\nNếu thành công: ").Append(name).Append(" +").Append(Pct(l.Value)).Append("% → +").Append(Pct(lo)).Append(hi > lo ? " đến " + Pct(hi) : "").Append("%");
            var wearer = WearerOf(it);
            if (wearer == null) return;
            double now;
            var a = StatAfter(wearer, l.StatKey, lo - l.Value, out now);
            var b = StatAfter(wearer, l.StatKey, hi - l.Value, out now);
            if (a == null || b == null) return;
            bool plain = l.StatKey.StartsWith("CRIT");
            string from = plain ? Pct(now) + "%" : Num(now), to = plain ? Pct(a.Value) + (b.Value > a.Value + 1e-9 ? " đến " + Pct(b.Value) : "") + "%" : Num(a.Value) + (Math.Round(b.Value) > Math.Round(a.Value) ? " đến " + Num(b.Value) : "");
            info.Append("\n").Append(name).Append(" của ").Append(Planet(wearer.Race)).Append(": ").Append(from).Append(" → ").Append(to);
        }

        string CrystalInfo(ItemInfo it, int cost, StringBuilder info)
        {
            var red = LineOf(it, 4);
            double start = _snapshot != null && _snapshot.StatStoneRules != null ? _snapshot.StatStoneRules.CrystalStart : 10;
            info.Append("Dòng đỏ: ").Append(red == null ? "(chưa có)" : red.Text + " / " + Pct(red.Cap) + "%")
                .Append("\nTạo một dòng đỏ ngẫu nhiên (Sức đánh, HP, Giáp, Ki hoặc Sát thương chí mạng) khởi đầu ").Append(Pct(start)).Append("%, tối đa 50%; nâng dần bằng đá chỉ số ở chức năng Ép sao.");
            if (red != null) info.Append("\nPha lê hóa lại: đổi sang loại khác và về lại ").Append(Pct(start)).Append("%.");
            info.Append("\nTốn ").Append(cost).Append(" đá pha lê.");
            return null;
        }

        int Cost(ItemInfo it, StatLine slotLine = null)
        {
            if (_mode == Mode.Crystal) return _snapshot != null && _snapshot.StatStoneRules != null ? (int)_snapshot.StatStoneRules.CrystalStones : 1;
            if (_mode == Mode.Star)
            {
                var r = _snapshot != null ? _snapshot.StatStoneRules : null;
                return r == null ? 1 : slotLine == null ? (int)r.AddStones : (int)r.RaiseStones;
            }
            if (it == null) return 1;
            if (_mode == Mode.Reroll) return _snapshot != null && _snapshot.GenRules != null ? (int)_snapshot.GenRules.RerollStones : 1;
            if (_mode == Mode.Unlock) return (int)it.GenUnlockCost;
            return 1;
        }

        string StoneHint() { return _mode == Mode.Upgrade ? "Đá nâng cấp" : _mode == Mode.Reroll ? "Đá GEN" : _mode == Mode.Star ? "Đá chỉ số" : _mode == Mode.Crystal ? "Đá pha lê" : "Đá mở ô GEN"; }

        string ItemLine(ItemInfo it)
        {
            var sb = new StringBuilder();
            sb.Append(it.MainStat != null ? it.MainStat.Text : it.Slot);
            if (it.UpgradePercent > 0) sb.Append("  [+").Append(Pct(it.UpgradePercent)).Append("%]");
            int n = 0;
            foreach (var g in it.Gens) if (g.GenKey.Length > 0) n++;
            if (it.Gens.Count > 0) sb.Append("\nGEN: ").Append(n).Append("/").Append(it.GenSlots).Append(it.Lines.Count > 0 ? "  Dòng: " + it.Lines.Count + "/" + (it.StatLinesMax + 1) : "");
            else if (it.Lines.Count > 0) sb.Append("\nDòng: ").Append(it.Lines.Count).Append("/").Append(it.StatLinesMax + 1);
            return sb.ToString();
        }

        static string StatName(string text)
        {
            int plus = text.LastIndexOf(" +");
            return plus > 0 ? text.Substring(0, plus) : "Chỉ số";
        }

        static string Planet(string race) { return race == "EARTH" ? "Trái Đất" : race == "NAMEC" ? "Namếc" : race == "XAYDA" ? "Xayda" : race; }
        static string Pct(double v) { return v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ','); }
        static string Num(double v) { return Math.Round(v).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.'); }
        static string Money(ulong v) { return v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.'); }

        /// <summary>After a request went through, tells what changed on the piece (the new snapshot and bag come a moment later).</summary>
        void TellResult(ItemInfo it)
        {
            if (_watchKey == null || it == null) return;
            string now = Key(it);
            if (now == _watchKey)
            {
                if (Time.unscaledTime > _watchUntil) _watchKey = null;
                return;
            }
            resultText.color = Good;
            resultText.text = ResultText(_watchKey, it);
            _watchKey = null;
            _sticky = true;
        }

        string Key(ItemInfo it)
        {
            var sb = new StringBuilder();
            sb.Append(Pct(it.UpgradePercent)).Append('|').Append(it.GenSlots).Append('|');
            foreach (var g in it.Gens) sb.Append(g.GenKey).Append(',');
            sb.Append('|');
            foreach (var l in it.Lines) sb.Append(l.LineNo).Append(':').Append(l.StatKey).Append(':').Append(Pct(l.Value)).Append(',');
            return sb.ToString();
        }

        string _watchLineBefore = "";

        string ResultText(string before, ItemInfo it)
        {
            if (_mode == Mode.Upgrade) return "Thành công! " + it.Name + " đã lên +" + Pct(it.UpgradePercent) + "%";
            if (_mode == Mode.Unlock) return "Đã mở ô GEN thứ " + it.GenSlots + " cho " + it.Name;
            if (_mode == Mode.Star || _mode == Mode.Crystal)
            {
                var l = LineOf(it, _mode == Mode.Crystal ? 4 : _starLine + 1);
                return (_mode == Mode.Crystal ? "Dòng đỏ: " : "Dòng " + (_starLine + 1) + ": ") + (_watchLineBefore.Length > 0 ? _watchLineBefore : "(trống)") + " → " + (l != null ? l.Text : "(trống)");
            }
            string now = _line < it.Gens.Count && it.Gens[_line].GenKey.Length > 0 ? it.Gens[_line].Name : "(trống)";
            return "GEN " + (_line + 1) + ": " + (_watchLineBefore.Length > 0 ? _watchLineBefore : "(trống)") + " → " + now;
        }

        int _starLine;                                                     // the row the last Ép sao request named (0 to 3)

        void Run() { Go(null); }
        void RunAlt() { Go("RETYPE"); }

        /// <summary>The button: the action of the mode; {@code starAction} is "RETYPE" for the second button of Ép sao.</summary>
        async void Go(string starAction)
        {
            var it = menu.ItemById(_itemId);
            if (it == null || _busy || !BbSession.IsActive) return;
            _busy = true;
            actionButton.interactable = false;
            _watchKey = Key(it);
            _starLine = _line;
            StatLine before = _mode == Mode.Star ? LineOf(it, _line + 1) : _mode == Mode.Crystal ? LineOf(it, 4) : null;
            _watchLineBefore = _mode == Mode.Star || _mode == Mode.Crystal ? (before != null ? before.Text : "")
                             : _line < it.Gens.Count && it.Gens[_line].GenKey.Length > 0 ? it.Gens[_line].Name : "";
            _watchUntil = Time.unscaledTime + 6f;
            ActionResult result = null;
            try
            {
                ulong who = menu.AnyCharacterIdPublic;
                if (_mode == Mode.Upgrade) result = await BbSession.Client.UpgradeItemAsync(who, it.ItemId);
                else if (_mode == Mode.Reroll) result = await BbSession.Client.RerollGenAsync(who, it.ItemId, (uint)_line);
                else if (_mode == Mode.Star) result = await BbSession.Client.StatStoneAsync(who, it.ItemId, starAction ?? (before == null ? "ADD" : "RAISE"), (uint)(_line + 1));
                else if (_mode == Mode.Crystal) result = await BbSession.Client.CrystalizeAsync(who, it.ItemId);
                else result = await BbSession.Client.UnlockGenSlotAsync(who, it.ItemId);
            }
            catch (Exception e)
            {
                resultText.color = Bad;
                resultText.text = "Không gửi được lệnh: " + e.Message;
                _watchKey = null;
            }
            _busy = false;
            if (result != null && !result.Ok)
            {
                _watchKey = null;
                resultText.color = Bad;
                resultText.text = _mode == Mode.Star && result.Error == ActionError.Maxed ? "Dòng này đã đạt tối đa, hoặc món đã đủ dòng." : menu.GearErrorText(result);
                _sticky = true;
            }
            else if (result != null && _mode == Mode.Star && !result.Success)
            {
                _watchKey = null;                                          // a raise that missed: the stone is gone and nothing else changed
                resultText.color = Bad;
                resultText.text = "Chưa thành công, đá đã mất. Thử lại!";
                _sticky = true;
            }
            Refresh();
        }

        // ---------------------------------------------------------------------------------------------------------------- for the screenshot tool

        /// <summary>For the scripted run in Play mode.</summary>
        public string DebugResult { get { return resultText.text; } }
        public bool DebugCanPress { get { return actionButton.interactable; } }
        public void DebugLine(int line) { _line = line; _sticky = false; Refresh(); }
        public void DebugPress() { Run(); }
        public void DebugAlt() { RunAlt(); }
        public bool DebugAltVisible { get { return altButton != null && altButton.gameObject.activeSelf; } }
        public string DebugInfo { get { return infoText.text; } }
        public string DebugRows { get { var sb = new StringBuilder(); foreach (var t in lineLabels) if (t.transform.parent.gameObject.activeSelf) sb.Append(t.text).Append(" | "); return sb.ToString(); } }

        public void DemoSet(ItemInfo it, MaterialAmount stone, int line)
        {
            _itemId = it != null ? it.ItemId : 0;
            _stoneTemplate = stone != null ? stone.TemplateId : 0;
            _line = line;
            Refresh();
        }
    }
}
