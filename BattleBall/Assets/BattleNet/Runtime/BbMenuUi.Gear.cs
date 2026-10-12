using System;
using System.Collections.Generic;
using System.Text;
using BattleBall.Proto;
using NroRig;
using UnityEngine;
using UnityEngine.UI;
using ProtoCharacter = BattleBall.Proto.CharacterInfo;

namespace BattleBall.Net
{
    /// <summary>
    /// Gear on the characters (docs/architecture/BattleBallServer.md 15.19, stage 1): the five equipment slots show the piece worn there, the shared bag can show the pieces instead of the skins
    /// (a button on the bag line switches), a tap on a piece opens its popup with the buttons that wear it, take it off, raise it with an upgrade stone and sell it. Everything is decided by the
    /// server: a piece is worn only by a character of its own planet and level, the stats come back in the next snapshot (CharacterInfo.stats and equipped_items); this file only asks and draws.
    /// The pieces of the bag are fetched with InventoryRequest (the snapshot has only their number); the worn ones are in the snapshot.
    /// </summary>
    public sealed partial class BbMenuUi
    {
        [Header("Gear: the picture and the button of each of the five equipment slots, and the switch of the bag")]
        public Image[] gearIcons;
        public Button[] gearButtons;
        public Button bagModeButton;
        public Text bagModeLabel;

        const uint UpgradeStoneId = 201;                 // material_template: the upgrade stone
        const double UpgradeMaxPct = 75.0;               // balance_config upgrade_stone_max_pct
        const int GearCell = -3;                         // _selectedCell value while the popup of a worn piece is open
        static readonly string[] SetNames = { "", "Thần Linh", "Hủy Diệt", "Thiên Sứ" };
        static readonly string[] SlotNames = { "Áo", "Quần", "Găng", "Giày", "Nhẫn" };

        bool _gearMode;                                  // the bag shows pieces of gear instead of skins
        bool _bagItemsLoaded, _loadingBag, _reloadBag;
        int _selectedGearSlot = -1;
        readonly List<ItemInfo> _bagItems = new List<ItemInfo>();       // every piece in the bag, as the server sent them
        readonly List<ItemInfo> _gearView = new List<ItemInfo>();       // the same, in the order of the bag on screen
        readonly List<MaterialAmount> _stoneView = new List<MaterialAmount>();      // the stones of the account, after the pieces
        readonly bool[] _gearFilled = new bool[5];

        void WireGear()
        {
            if (bagModeButton != null) bagModeButton.onClick.AddListener(ToggleGearMode);
            if (gearButtons != null)
                for (int i = 0; i < gearButtons.Length; i++)
                {
                    int slot = i;
                    gearButtons[i].onClick.AddListener(() => OnGearSlot(slot));
                }
        }

        // ---------------------------------------------------------------------------------------------------------------- the bag of gear

        /// <summary>The picture of a material in the bag (a stone): the server sends its Nro icon id.</summary>
        public static Sprite MaterialIcon(MaterialAmount m)
        {
            var resolver = NroSpriteResolver.Instance;
            return m != null && m.IconId > 0 && resolver != null ? resolver.ResolveSprite((int)m.IconId, BbAssetCache.Scale) : null;
        }

        /// <summary>A piece of gear of the account by id: worn by one of the characters, or in the bag.</summary>
        public ItemInfo ItemById(ulong id)
        {
            if (id == 0) return null;
            if (_snapshot != null) foreach (var ch in _snapshot.Characters) foreach (var it in ch.EquippedItems) if (it.ItemId == id) return it;
            foreach (var it in _bagItems) if (it.ItemId == id) return it;
            return null;
        }

        public ulong AnyCharacterIdPublic { get { return AnyCharacterId(); } }

        public string GearErrorText(ActionResult r) { return GearActionText(r); }

        public static Sprite ItemIcon(ItemInfo it)
        {
            var resolver = NroSpriteResolver.Instance;
            return it != null && it.IconId > 0 && resolver != null ? resolver.ResolveSprite((int)it.IconId, BbAssetCache.Scale) : null;
        }

        static int SlotIndex(string slot) { return Array.IndexOf(GearKeys, slot); }

        static string PlanetOf(string race)
        {
            int i = RaceIndex(race);
            return i >= 0 ? Titles[i] : race;
        }

        static string SetOf(ItemInfo it) { return it.Tier < SetNames.Length ? SetNames[it.Tier] : "bậc " + it.Tier; }

        void ToggleGearMode()
        {
            _gearMode = !_gearMode;
            _bagPage = 0;
            if (popup.IsOpen) popup.Hide();
            if (_gearMode && !_bagItemsLoaded) LoadBagItems();
            ShowPage(_page, false);
        }

        void OnSnapshotGear()
        {
            if (_gearMode) LoadBagItems();
        }

        /// <summary>Fetches the pieces of the bag (all pages), then draws the bag again. A snapshot that arrives meanwhile asks for one more fetch afterwards.</summary>
        async void LoadBagItems()
        {
            if (_demo || !BbSession.IsActive || _snapshot == null || _snapshot.Characters.Count == 0) return;
            if (_loadingBag) { _reloadBag = true; return; }
            _loadingBag = true;
            try
            {
                var items = await BbSession.Client.InventoryAsync(_snapshot.Characters[0].CharacterId);
                _bagItems.Clear();
                _bagItems.AddRange(items);
                _bagItemsLoaded = true;
            }
            catch (Exception e)
            {
                Toast("Không tải được đồ: " + e.Message);
            }
            _loadingBag = false;
            if (_reloadBag) { _reloadBag = false; LoadBagItems(); return; }
            if (_gearMode) ShowPage(_page, false);
            if (smith != null && smith.IsOpen) smith.OnSnapshot(_snapshot);              // the pieces of the bag are new now
        }

        /// <summary>The pieces of the character's planet first, then the strongest set first, then by slot (Áo, Quần, Găng, Giày, Nhẫn).</summary>
        int CompareGear(ItemInfo a, ItemInfo b, string race)
        {
            bool ma = a.Race == race, mb = b.Race == race;
            if (ma != mb) return ma ? -1 : 1;
            if (a.Tier != b.Tier) return b.Tier.CompareTo(a.Tier);
            int sa = SlotIndex(a.Slot), sb = SlotIndex(b.Slot);
            return sa != sb ? sa.CompareTo(sb) : a.ItemId.CompareTo(b.ItemId);
        }

        void FillGearBag(ProtoCharacter ch)
        {
            _gearView.Clear();
            _gearView.AddRange(_bagItems);
            _gearView.Sort((a, b) => CompareGear(a, b, ch.Race));
            _stoneView.Clear();                                                  // the stones follow the pieces (a bag of Nro holds them as items)
            if (_snapshot != null)
                foreach (var m in _snapshot.Materials) if (m.Quantity > 0 && m.Kind.EndsWith("STONE")) _stoneView.Add(m);
            _stoneView.Sort((a, b) => a.TemplateId.CompareTo(b.TemplateId));
            int perPage = BagPerPage();
            int total = _gearView.Count + _stoneView.Count;
            int pages = Mathf.Max(1, (total + perPage - 1) / perPage);
            _bagPage = Mathf.Clamp(_bagPage, 0, pages - 1);
            bagPrev.gameObject.SetActive(pages > 1);
            bagNext.gameObject.SetActive(pages > 1);
            bagPageText.text = pages > 1 ? (_bagPage + 1) + "/" + pages : "";
            if (bagModeLabel != null) bagModeLabel.text = "Xem skin";
            for (int i = 0; i < perPage; i++)
            {
                int index = _bagPage * perPage + i;
                bool has = index < _gearView.Count;
                bool isStone = !has && index < total;
                var it = has ? _gearView[index] : null;
                var stone = isStone ? _stoneView[index - _gearView.Count] : null;
                Sprite sp = isStone ? MaterialIcon(stone) : ItemIcon(it);
                bagIcons[i].sprite = sp;
                bagIcons[i].enabled = sp != null;
                bagIcons[i].color = has && it.Race != ch.Race ? new Color(1f, 1f, 1f, 0.4f) : Color.white;        // another planet's piece is dimmed
                bagSlotImages[i].sprite = i == _selectedCell && cellSelected != null ? cellSelected : BagCell;
                bagCaptions[i].text = isStone ? stone.Quantity.ToString() : has && it.UpgradePercent > 0 ? "+" + Percent(it.UpgradePercent) + "%" : "";
            }
        }

        void OnBagGear(int slot)
        {
            int index = _bagPage * BagPerPage() + slot;
            if (_snapshot == null || index >= _gearView.Count + _stoneView.Count) return;
            if (index >= _gearView.Count)
            {
                var stone = _stoneView[index - _gearView.Count];
                if (smith != null && smith.IsOpen) { smith.PutStone(stone); return; }
                popup.Show(MaterialIcon(stone), stone.Name, "", (stone.Note.Length > 0 ? stone.Note + "\n" : "") + "Có " + stone.Quantity + " viên. Dùng ở Bà Hạt Mít (nâng cấp, ép sao, pha lê hóa, đổi GEN, mở ô GEN).",
                    null, null, new List<BbPopup.ButtonSpec>(), null);
                _selectedCell = slot;
                RefreshSelection();
                return;
            }
            ShowGearPopup(_gearView[index], null);                                                     // beside Bà Hạt Mít the popup offers the function of her panel instead of wear and sell
            _selectedCell = slot;
            RefreshSelection();
        }

        // ---------------------------------------------------------------------------------------------------------------- the five slots

        static ItemInfo WornIn(ProtoCharacter ch, string slot)
        {
            foreach (var it in ch.EquippedItems) if (it.Slot == slot) return it;
            return null;
        }

        void FillGearSlots(ProtoCharacter ch)
        {
            if (gearIcons == null) return;
            for (int i = 0; i < gearSlots.Length && i < gearIcons.Length; i++)
            {
                var it = WornIn(ch, GearKeys[i]);
                Sprite sp = ItemIcon(it);
                gearIcons[i].sprite = sp;
                gearIcons[i].enabled = sp != null;
                _gearFilled[i] = it != null;
            }
            ApplyGearSelection();
        }

        /// <summary>The slot of the worn piece a popup shows is yellow while it is open; a slot with a piece is the lighter one.</summary>
        void ApplyGearSelection()
        {
            if (gearSlots == null) return;
            if (_selectedCell != GearCell) _selectedGearSlot = -1;
            for (int i = 0; i < gearSlots.Length; i++)
                gearSlots[i].sprite = i == _selectedGearSlot && cellSelected != null ? cellSelected : _gearFilled[i] ? slotFilled : slotEmpty;
        }

        /// <summary>Tap on a gear slot: the popup of the piece worn there; an empty slot sends the player to the bag of gear.</summary>
        void OnGearSlot(int slot)
        {
            if (_snapshot == null || _page < 0 || _page >= _pages.Count || _pages[_page].main < 0) return;
            var ch = CharacterOf(Races[_pages[_page].main]);
            if (ch == null) return;
            var it = WornIn(ch, GearKeys[slot]);
            if (smith != null && smith.IsOpen && it == null) return;
            if (it == null)
            {
                Toast("Ô " + SlotNames[slot] + " trống: chọn món trong túi (nút Xem đồ)");
                if (!_gearMode) ToggleGearMode();
                return;
            }
            ShowGearPopup(it, ch);
            _selectedCell = GearCell;
            _selectedGearSlot = slot;
            RefreshSelection();
        }

        // ---------------------------------------------------------------------------------------------------------------- the popup of a piece

        /// <summary>The popup of a piece: its main stat and lines, the upgrade, who may wear it, and the buttons. <paramref name="wearer"/>: the character that wears it (a worn piece), or null (the bag).</summary>
        public void DebugShowGearPopup(ItemInfo it) { ShowGearPopup(it, null); }

        void ShowGearPopup(ItemInfo it, ProtoCharacter wearer)
        {
            var lines = new StringBuilder();
            string main = it.MainStat != null && it.MainStat.Text.Length > 0 ? it.MainStat.Text : it.Slot;
            lines.Append("<color=#1E8F1E>").Append(main).Append("</color>");
            foreach (var l in it.Lines)
            {
                bool red = l.Source == "CRYSTAL_STONE";                                       // the crystal line is red (docs 15.27)
                lines.Append(red ? "\n<color=#D02020>" : "\n<color=#1E8F1E>").Append(l.Text.Length > 0 ? l.Text : l.StatKey + " +" + Percent(l.Value))
                     .Append(l.Cap > 0 ? " / " + Percent(l.Cap) + "%" : "").Append("</color>");
            }
            lines.Append("\n<color=#1E5AB4>Nâng cấp: +").Append(Percent(it.UpgradePercent)).Append("% / ").Append(Percent(UpgradeMaxPct)).Append("%</color>");
            AppendGenLines(lines, it, wearer);
            var desc = new StringBuilder("Bộ ").Append(SetOf(it)).Append(" của ").Append(PlanetOf(it.Race)).Append(".\nCấp yêu cầu: ").Append(it.MinLevel).Append('.');
            if (wearer != null) desc.Append("\nĐang mặc ở ").Append(PlanetOf(wearer.Race)).Append('.');

            var specs = new List<BbPopup.ButtonSpec>();
            var actions = new List<Action>();
            var user = wearer ?? CharacterOf(it.Race);
            bool forSmith = smith != null && smith.IsOpen;
            if (forSmith)
            {
                // beside Bà Hạt Mít (owner request 2026-10-12): the piece is not put in her panel by a tap; the popup shows its figures and asks (the function of her panel, or cancel)
                specs.Add(new BbPopup.ButtonSpec(smith.ModeName, true, true));
                actions.Add(() => { popup.Hide(); smith.PutItem(it); });
                specs.Add(new BbPopup.ButtonSpec("Hủy", true, false));
                actions.Add(() => popup.Hide());
            }
            else if (wearer != null)
            {
                specs.Add(new BbPopup.ButtonSpec("Tháo", true, true));
                actions.Add(() => UnequipGear(wearer, it.Slot));
            }
            else
            {
                int open = _pages[_page].main;
                bool own = open >= 0 && Races[open] == it.Race;
                bool levelOk = user != null && user.Level >= it.MinLevel;
                string label = user == null ? "Chưa có nhân vật" : !levelOk ? "Cần cấp\n" + it.MinLevel : own ? "Mặc" : "Dùng cho\n" + PlanetOf(it.Race);
                specs.Add(new BbPopup.ButtonSpec(label, user != null && levelOk, true));
                actions.Add(() => EquipGear(user, it));
            }
            if (wearer == null && !forSmith)
            {
                specs.Add(new BbPopup.ButtonSpec("Bán", true, false));
                actions.Add(() => ConfirmSellGear(it));
            }
            if (forSmith) desc.Append("\nBấm \"").Append(smith.ModeName).Append("\" để đặt món này vào bảng của Bà Hạt Mít.");
            else desc.Append("\nNâng cấp, ép sao, pha lê hóa, đổi GEN: đến Bà Hạt Mít.");
            popup.Show(ItemIcon(it), it.Name.Length > 0 ? it.Name : "Đồ #" + it.ItemId, lines.ToString(), desc.ToString(), null, null, specs,
                i => { if (i < actions.Count) actions[i](); });
        }

        // ---------------------------------------------------------------------------------------------------------------- GEN

        ulong AnyCharacterId() { return _snapshot != null && _snapshot.Characters.Count > 0 ? _snapshot.Characters[0].CharacterId : 0; }

        ulong Owned(uint templateId)
        {
            if (_snapshot != null && templateId != 0) foreach (var m in _snapshot.Materials) if (m.TemplateId == templateId) return m.Quantity;
            return 0;
        }

        uint GenRerollStones() { return _snapshot != null && _snapshot.GenRules != null && _snapshot.GenRules.RerollStones > 0 ? _snapshot.GenRules.RerollStones : 1; }

        GenSetInfo GenSetOf(string key)
        {
            if (_snapshot != null) foreach (var g in _snapshot.GenSets) if (g.GenKey == key) return g;
            return null;
        }

        static string ShortGen(string name) { return name.StartsWith("Set ") ? name.Substring(4) : name; }

        /// <summary>The GEN lines of a piece for its popup: the set of each line and what it does at each step; the steps a worn piece reaches now are green, the others grey.</summary>
        void AppendGenLines(StringBuilder sb, ItemInfo it, ProtoCharacter wearer)
        {
            // the box holds about ten lines: with several sets of several steps only the best step of each set is listed (the steps of a set are in docs 15.21)
            int rows = 2;
            foreach (var l in it.Gens) { var s = l.GenKey.Length == 0 ? null : GenSetOf(l.GenKey); rows += 1 + (s == null ? 0 : s.Tiers.Count); }
            bool compact = rows > 10;
            for (int i = 0; i < it.Gens.Count; i++)
            {
                var line = it.Gens[i];
                if (line.GenKey.Length == 0) { sb.Append("\n<color=#7A5A00>GEN ").Append(i + 1).Append(": trống</color>"); continue; }
                int worn = 0;
                if (wearer != null) foreach (var a in wearer.ActiveGens) if (a.GenKey == line.GenKey) worn = (int)a.Pieces;
                var set = GenSetOf(line.GenKey);
                sb.Append("\n<color=#B25A00>GEN ").Append(i + 1).Append(": ").Append(line.Name.Length > 0 ? line.Name : line.GenKey);
                if (wearer != null && set != null) sb.Append(" (").Append(worn).Append("/").Append(set.Tiers.Count > 0 ? (int)set.Tiers[set.Tiers.Count - 1].Pieces : 5).Append(")");
                sb.Append("</color>");
                if (set != null)
                    for (int k = compact ? set.Tiers.Count - 1 : 0; k < set.Tiers.Count; k++)
                    {
                        var t = set.Tiers[k];
                        sb.Append("\n  <color=").Append(wearer != null && worn >= t.Pieces ? "#1E8F1E" : "#6B6B6B").Append(">").Append(t.Pieces).Append(" món: ").Append(t.Text).Append("</color>");
                    }
            }
        }

        /// <summary>The summary of the sets worn by a character, one short line for under the character ("Bộ: Nappa 5/5, Gohan 2/5").</summary>
        string GenSummary(ProtoCharacter ch)
        {
            if (ch == null || ch.ActiveGens.Count == 0) return "";
            var sb = new StringBuilder("\nBộ: ");
            for (int i = 0; i < ch.ActiveGens.Count; i++)
            {
                var g = ch.ActiveGens[i];
                var set = GenSetOf(g.GenKey);
                if (i > 0) sb.Append(", ");
                sb.Append(set != null ? ShortGen(set.Name) : g.GenKey).Append(' ').Append(g.Pieces).Append('/').Append(set != null && set.Tiers.Count > 0 ? (int)set.Tiers[set.Tiers.Count - 1].Pieces : 5);
            }
            return sb.ToString();
        }

        void ConfirmSellGear(ItemInfo it)
        {
            var specs = new List<BbPopup.ButtonSpec> { new BbPopup.ButtonSpec("Bán", true, false), new BbPopup.ButtonSpec("Hủy", true, true) };
            popup.Show(ItemIcon(it), it.Name, "", "Bán món này lấy ít vàng (5% giá mua)? Bán rồi sẽ không lấy lại được.", null, null, specs, i =>
            {
                if (i == 0) SellGear(it); else popup.Hide();
            });
        }

        static string GearActionText(ActionResult r)
        {
            switch (r.Error)
            {
                case ActionError.RaceLocked: return "Món đồ này không dành cho hành tinh đó";
                case ActionError.LevelTooLow: return "Chưa đủ cấp để mặc";
                case ActionError.NotEnoughMaterial: return "Không đủ đá";
                case ActionError.Disabled: return "Không còn bộ GEN nào khác cho món này";
                case ActionError.Maxed: return "Đã nâng cấp tối đa";
                case ActionError.BagFull: return "Túi đầy";
                case ActionError.NotFound: return "Không tìm thấy món đồ";
                default: return ActionText(r);
            }
        }

        /// <summary>Runs one request of the gear popups: closes the popup, asks the server, tells the player the result (the new stats come with the next snapshot).</summary>
        async void RunGear(Func<System.Threading.Tasks.Task<ActionResult>> request, string done)
        {
            popup.Hide();
            if (_demo || !BbSession.IsActive) { Toast("Cần kết nối máy chủ"); return; }
            try
            {
                var result = await request();
                Toast(result.Ok ? done : GearActionText(result));
            }
            catch (Exception e)
            {
                Toast("Không gửi được lệnh: " + e.Message);
            }
        }

        void EquipGear(ProtoCharacter user, ItemInfo it)
        {
            if (user == null) return;
            RunGear(() => BbSession.Client.EquipItemAsync(user.CharacterId, it.ItemId), "Đã mặc " + it.Name);
        }

        void UnequipGear(ProtoCharacter wearer, string slot)
        {
            RunGear(() => BbSession.Client.UnequipItemAsync(wearer.CharacterId, slot), "Đã tháo đồ");
        }

        void UpgradeGear(ProtoCharacter user, ItemInfo it)
        {
            if (user == null) return;
            RunGear(() => BbSession.Client.UpgradeItemAsync(user.CharacterId, it.ItemId), "Đã nâng cấp " + it.Name);
        }

        void SellGear(ItemInfo it)
        {
            var any = _snapshot != null && _snapshot.Characters.Count > 0 ? _snapshot.Characters[0] : null;
            if (any == null) return;
            RunGear(() => BbSession.Client.SellItemAsync(any.CharacterId, it.ItemId), "Đã bán " + it.Name);
        }

        // ---------------------------------------------------------------------------------------------------------------- for the screenshot tool and the scripted run

        /// <summary>For the screenshot tool: the bag shows these pieces of gear (no server).</summary>
        public void DemoGearBag(IEnumerable<ItemInfo> items)
        {
            _bagItems.Clear();
            _bagItems.AddRange(items);
            _bagItemsLoaded = true;
            _gearMode = true;
            _bagPage = 0;
            ShowPage(_page, false);
        }

        /// <summary>For the screenshot tool: tap this slot of the bag (gear mode).</summary>
        public void DemoGearBagSlot(int slot) { OnBagGear(slot); }



        /// <summary>For the screenshot tool: tap this equipment slot of the open character.</summary>
        public void DemoGearSlot(int slot) { OnGearSlot(slot); }

        /// <summary>For the scripted run in Play mode: the bag shows pieces of gear now (waits for the first fetch).</summary>
        public bool DebugGearMode { get { return _gearMode; } }
        public int DebugGearCount { get { return _bagItems.Count; } }
        public bool DebugGearLoaded { get { return _bagItemsLoaded; } }
        public void DebugToggleGear() { ToggleGearMode(); }

        /// <summary>For the scripted run in Play mode: press the first button of the popup of the first piece of the bag that fits the open character (wear it).</summary>
        public ItemInfo DebugFirstGearOfOpenPlanet()
        {
            int open = _pages[_page].main;
            foreach (var it in _gearView) if (open >= 0 && it.Race == Races[open]) return it;
            return null;
        }

        public void DebugEquip(ItemInfo it) { EquipGear(CharacterOf(it.Race), it); }
        public void DebugUnequip(string slot) { var ch = CharacterOf(Races[_pages[_page].main]); if (ch != null) UnequipGear(ch, slot); }
        public void DebugUpgrade(ItemInfo it) { UpgradeGear(CharacterOf(it.Race), it); }
        public void DebugOpenGearPopup(ItemInfo it) { ShowGearPopup(it, null); }
    }
}
