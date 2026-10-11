using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BattleBall.Proto;
using NroRig;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ProtoCharacter = BattleBall.Proto.CharacterInfo;

namespace BattleBall.Net
{
    /// <summary>
    /// Main menu after login, drawn as the Nro inventory panel (NroUnity Panel.cs: sizes, colours and pictures; owner request 2026-10-08): a panel on the left with the portrait and numbers of the character, a row of
    /// tabs (one per character: Trái Đất, Xayda, Namếc), the character between its equipment slots with the summary of its bonuses under it, the bag
    /// and the money; the free area on the right holds the buttons into the game. "Bông tai" (Potara) merges two characters: their two tabs become one
    /// tab showing the sum of their stats (rules of the GDD: both from level 50, the character on the current tab is the main one, the pair is kept for
    /// the next match). The three characters share one inventory (wallet, skins, gear): the bag under the character is the same on every tab, a tap on a skin opens
    /// a popup that offers it to the characters who may wear it. The last tab, "Chức năng", holds the mailbox, the notices and the account (BbMenuFunctions).
    /// Behind the panel is the village (BbVillageView): the main character, the two others as AI, the NPCs; the joystick moves the main character and the
    /// panel is closed with its X and opened again with the orange triangle on the left edge of the screen. Everything is read from the PlayerDataSnapshot the server pushed.
    /// The hierarchy is built by BattleBall/Build Login Scene.
    /// </summary>
    public sealed partial class BbMenuUi : MonoBehaviour
    {
        [Header("Panel header")]
        public Image portrait;
        public Text headerText;
        public Image expFill;
        public Text expText;

        [Header("Tabs (the characters: Trái Đất, Xayda, Namếc, two of them become one after a fusion; the last tab is Chức năng)")]
        public Button[] tabs;
        public Image[] tabImages;
        public Text[] tabLabels;
        public Sprite tabIdle, tabOn;
        public GameObject expTrack;

        [Header("Body")]
        public CanvasGroup bodyGroup;
        public BbCharacterView view;
        public Text noSkinText, bonusText;
        public Image[] gearSlots, supportSlots;
        public Text[] gearLabels, supportLabels;
        public Sprite slotEmpty, slotFilled;
        public Sprite bagCell, cellSelected;      // the bag slot of Nro and the yellow slot of the item a popup shows
        public Text bagText;
        [Header("Hỗ trợ 5: the skin the open tab wears")]
        public Button skinSlotButton;
        public Image skinSlotImage, skinSlotIcon;
        public Text skinSlotLabel;

        [Header("Money")]
        public Text goldText, gemText;

        [Header("Village behind the menu")]
        public BbVillageView village;
        public RectTransform panelRect, joystickRect;
        public Button panelToggle;                // the orange triangle on the left edge: opens the folded panel
        public RectTransform panelToggleRect;
        public Button closeButton;                // the X of the panel: folds it away
        public Text toastText, potaraText;

        [Header("Potara earring: the first slot of the bag")]
        public Button earringSlot;
        public Image earringSlotImage, earringIcon;
        public Text earringCaption;

        [Header("Bag slots after the earring: the skins the character owns")]
        public Image[] bagSlotImages, bagIcons;
        public Text[] bagCaptions;
        public Button[] bagButtons;
        public Button bagPrev, bagNext;
        public Text bagPageText;

        [Header("Popup, the Aura tab and the Chức năng tab")]
        public BbPopup popup;
        public BbMenuFunctions functions;
        public BbMenuAuras auras;
        public BbTraining training;              // the training dummies of the village (docs 15.12)
        public BbMenuSkills skills;              // the Kỹ năng tab: the skills of the three characters, raised with gold
        public BbAttendancePanel attendance;     // the table of the daily check-in of the elder (docs 15.25); stands where the shop stands
        public BbSmithPanel smith;               // what Bà Hạt Mít does for the gear: upgrade, change a GEN line, open a GEN line (docs 15.22); stands where the shop stands
        public BbShopPanel shop;                 // the shop of an NPC (Urôn): stands on the left while this panel moves to the right (docs 15.15)

        [Header("Fusion partner chooser")]
        public GameObject chooserRoot;
        public Text chooserTitle;
        public Button[] chooserButtons;
        public Text[] chooserLabels;
        public Button chooserCancel;

        const string KeyPage = "bb.menuPage", KeyPanel = "bb.menuOpen";
        const float PanelWidth = 320f;          // Nro's 176 px panel at 25% of the 1280 reference width
        const int DefaultFusionMinLevel = 50;          // GDD values, used only until the server sends PotaraInfo.min_fusion_level and shards_needed
        const int DefaultPotaraShards = 100;

        // the order of the tabs (the player's own list: Trái Đất, Xayda, Namếc)
        static readonly string[] Races = { "EARTH", "XAYDA", "NAMEC" };
        static readonly string[] Titles = { "Trái Đất", "Xayda", "Namếc" };
        // equipment slot keys of the protocol (EquipSlotInfo.slot) in the order of the left column, then the support slots of the right column
        static readonly string[] GearKeys = { "ARMOR", "PANTS", "GLOVES", "BOOTS", "RING" };
        static readonly string[] SupportKeys = { "SUPPORT_1", "SUPPORT_2", "SUPPORT_3", "SUPPORT_4" };

        struct Page
        {
            public int main;           // index into Races; -1 = the Chức năng tab
            public int sub;            // index into Races of the fused partner, or -1
            public bool fusion;        // the tab of the fusion form (totals of main and partner)
            public bool aura;          // the Aura tab (main = -1 like the Chức năng tab: no character of its own)
            public bool skills;        // the Kỹ năng tab (main = -1 too: it lists the three characters)
        }

        readonly List<Page> _pages = new List<Page>();
        PlayerDataSnapshot _snapshot;
        public PlayerDataSnapshot Snapshot { get { return _snapshot; } }
        int _page;
        int _fusionMain = -1, _fusionSub = -1;
        float _fade = 1f, _toastUntil;
        Vector2 _pressAt;
        bool _pressing, _demo;
        bool _panelOpen = true;
        float _panelX;                          // how far the panel has slid towards its own side: 0 open, -PanelWidth folded
        bool _dockRight, _savedOpen, _shopBusy; // _dockRight: a shop is open, so the panel stands on the right (as the second panel of Nro); _savedOpen: how it was before
        public bool IsDemo { get { return _demo; } }

        /// <summary>The pair chosen with the earring (character ids), or 0: to be sent as the team of the next match.</summary>
        public ulong FusionMainCharacterId { get { var c = _fusionMain >= 0 ? CharacterOf(Races[_fusionMain]) : null; return c != null ? c.CharacterId : 0; } }
        public ulong FusionSubCharacterId { get { var c = _fusionSub >= 0 ? CharacterOf(Races[_fusionSub]) : null; return c != null ? c.CharacterId : 0; } }

        /// <summary>Made-up player data for the screenshot tool; when set, the menu shows it instead of a session.</summary>
        public static PlayerDataSnapshot DemoOverride;

        void Awake()
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                tabs[i].onClick.AddListener(() => SelectPage(index));
            }
            panelToggle.onClick.AddListener(() => SetPanelOpen(true, false));
            if (closeButton != null) closeButton.onClick.AddListener(() => SetPanelOpen(false, false));
            village.NpcTapped = spec => { if (training != null) training.Focus(spec.Id); else OnNpc(spec); };      // a tap on an NPC focuses it; the fist button talks to it (owner request 2026-10-10)
            if (auras != null) auras.Tapped = OnAuraTapped;
            if (shop != null) { shop.menu = this; shop.Closed = OnShopClosed; shop.Warm(); }
            if (smith != null) { smith.menu = this; smith.Closed = OnShopClosed; }
            if (attendance != null) { attendance.menu = this; attendance.Closed = OnShopClosed; }
            if (quest != null) { quest.menu = this; quest.Closed = OnShopClosed; }
            WireDungeon();
            WirePopupSide();
            _panelOpen = PlayerPrefs.GetInt(KeyPanel, 1) != 0;
            SetPanelOpen(_panelOpen, true);
            earringSlot.onClick.AddListener(OnEarring);
            skinSlotButton.onClick.AddListener(OnWornSkin);
            WireGear();
            bagPrev.onClick.AddListener(() => ChangeBagPage(-1));
            bagNext.onClick.AddListener(() => ChangeBagPage(1));
            for (int i = 0; i < bagButtons.Length; i++)
            {
                int slot = i;
                bagButtons[i].onClick.AddListener(() => OnBagSkin(slot));
            }
            chooserCancel.onClick.AddListener(() => chooserRoot.SetActive(false));
            for (int i = 0; i < chooserButtons.Length; i++)
            {
                int slot = i;
                chooserButtons[i].onClick.AddListener(() => OnChoose(slot));
            }
            chooserRoot.SetActive(false);
            toastText.gameObject.SetActive(false);
            RebuildPages();
            _page = PageOfRace(Mathf.Clamp(PlayerPrefs.GetInt(KeyPage, 0), 0, Races.Length - 1));
        }

        void Start()
        {
            if (DemoOverride != null) { ShowDemo(DemoOverride, 0); return; }
            if (_demo) return;
            if (!BbSession.IsActive)                          // opened directly (not through the login): go and log in
            {
                SceneManager.LoadScene(BbSession.LoginScene);
                return;
            }
            BbSession.Client.SnapshotChanged += OnSnapshot;
            SetSnapshot(BbSession.Snapshot);
            if (BbSession.ResumedMatch != null) ResumeDungeonAfterDrop();
        }

        void OnDestroy()
        {
            UnsubscribeDungeon();
            if (BbSession.Client != null) BbSession.Client.SnapshotChanged -= OnSnapshot;
        }

        void OnSnapshot(PlayerDataSnapshot s) { SetSnapshot(s); }

        /// <summary>Shows this player data.</summary>
        public void SetSnapshot(PlayerDataSnapshot snapshot)
        {
            _snapshot = snapshot;
            LoadPins();
            if (_fusionMain >= 0 && (CharacterOf(Races[_fusionMain]) == null || CharacterOf(Races[_fusionSub]) == null)) Unfuse(false);
            ShowPage(_page, false);
            if (functions != null) functions.Refresh(snapshot);
            if (shop != null && shop.IsOpen) shop.OnSnapshot(snapshot);
            if (smith != null && smith.IsOpen) smith.OnSnapshot(snapshot);
            if (attendance != null && attendance.IsOpen) attendance.OnSnapshot(snapshot);
            OnQuestSnapshot(snapshot);
            OnSnapshotGear();
        }

        /// <summary>For the screenshot tool: build the page without a session.</summary>
        public void ShowDemo(PlayerDataSnapshot snapshot, int page)
        {
            _demo = true;
            chooserRoot.SetActive(false);
            popup.Hide();
            SetPanelOpen(_panelOpen, true);
            _snapshot = snapshot;
            _pins.Clear();
            _pinsAccount = 0;
            Unfuse(false);
            _page = Mathf.Clamp(page, 0, _pages.Count - 1);
            ShowPage(_page, false);
            village.ResetFormation();
            if (functions != null) functions.Refresh(snapshot);
        }

        /// <summary>For the screenshot tool: the Chức năng tab (the last one) with made-up mails and notices.</summary>
        public void ShowDemoFunctions(PlayerDataSnapshot snapshot, MailList mails, NoticeList notices, int view)
        {
            ShowDemo(snapshot, 0);
            functions.SetDemo(mails, notices);
            ShowPage(_pages.Count - 1, false);
            functions.Open(view);
        }

        /// <summary>For the screenshot tool: tap the NPC at this index of the map.</summary>
        public void DemoNpc(int index)
        {
            OnNpc(village.Map.Npcs[index]);
        }

        /// <summary>For the screenshot tool: tap this skin in the bag (the bag turns to the page that holds it).</summary>
        public void DemoOpenSkin(uint costumeId)
        {
            int index = _bagSkins.FindIndex(skin => skin.CostumeId == costumeId);
            if (index < 0) return;
            int perPage = BagPerPage();
            _bagPage = index / perPage;
            ShowPage(_page, false);
            OnBagSkin(index % perPage);
        }

        /// <summary>For the screenshot tool: pin this skin (what the "Ghim" button does).</summary>
        public void DemoPin(uint costumeId)
        {
            var skin = SkinOf(costumeId);
            if (skin != null) TogglePin(skin);
        }

        /// <summary>For the screenshot tool: ask whether to throw this skin away (the confirmation after the "Vứt" button).</summary>
        public void DemoDiscardAsk(uint costumeId)
        {
            var skin = SkinOf(costumeId);
            if (skin != null) ConfirmDiscard(skin, null);
        }

        /// <summary>For the screenshot tool: tap Hỗ trợ 5 (the skin the open tab wears).</summary>
        public void DemoWornSkin()
        {
            OnWornSkin();
        }

        /// <summary>For the screenshot tool: take off the skin of the open tab (what the "Tháo" button does).</summary>
        public void DemoUnequip()
        {
            var pg = _pages[_page];
            UnequipSkin(pg.fusion ? Mathf.Max(0, _fusionMain) : pg.main, pg.fusion);
        }

        /// <summary>For the screenshot tool: merge two characters (indexes into Trái Đất, Xayda, Namếc).</summary>
        public void DemoFuse(int main, int sub)
        {
            Fuse(main, sub, false);
        }

        /// <summary>For the play run: the fusion and the split the way the earring does them (with the effects of the Nro client).</summary>
        public void DemoFuseWithEffect(int main, int sub)
        {
            Fuse(main, sub, true);
        }

        /// <summary>For the play run: the effects of the fusion dance (Nro type 4) on the main character; the menu state does not change.</summary>
        public void DemoDance(int main)
        {
            var c = CharacterOf(Races[main]);
            if (c != null) FusionFx.Play(BbFusionFx.Kind.Dance, c.CharacterId);
        }

        public void DemoSplit()
        {
            SplitFusion();
        }

        /// <summary>The sprite scale changed: forget the sprites cut at the old scale and draw everything again.</summary>
        public void ApplyScaleChange()
        {
            if (NroCostumeCatalog.Instance != null) NroCostumeCatalog.Instance.ClearPartCache();
            if (NroSpriteResolver.Instance != null) NroSpriteResolver.Instance.ClearAllCache();
            BbAuraAssets.Clear();                     // the aura pictures of the old scale
            village.ReloadMap();                      // the ground and scenery pictures of the new scale
            ShowPage(_page, false);
            village.RefreshArt();
        }

        /// <summary>For the screenshot tool: open the Kỹ năng tab.</summary>
        public void DemoSelectSkills()
        {
            DemoSelect(_pages.FindIndex(p => p.skills));
        }

        /// <summary>For the screenshot tool: learn or raise this skill of the character of this race (what the button does).</summary>
        public void DemoUpgradeSkill(string race, uint skillId)
        {
            var ch = CharacterOf(race);
            if (ch == null || _snapshot == null) return;
            foreach (var e in _snapshot.SkillCatalog) if (e.SkillId == skillId) OnUpgradeSkill(ch.CharacterId, e);
        }

        /// <summary>For the screenshot tool: open the Aura tab.</summary>
        public void DemoSelectAura()
        {
            DemoSelect(_pages.FindIndex(p => p.aura));
        }

        /// <summary>For the screenshot tool: open this tab.</summary>
        public void DemoSelect(int page)
        {
            SelectPage(page);
            village.ResetFormation();
        }

        /// <summary>For the screenshot tool: turn the bag page.</summary>
        public void ChangeBagPageForDemo(int delta)
        {
            ChangeBagPage(delta);
        }

        /// <summary>For the screenshot tool: press the earring button.</summary>
        public void DemoEarring()
        {
            OnEarring();
        }

        // ---------------------------------------------------------------------------------------------------------------- pages and tabs

        /// <summary>
        /// One page per character, a new page for the fusion form after them while the earring merges two characters (the characters keep their own pages:
        /// their ordinary skins and gear are still worn there), and the Chức năng page last.
        /// </summary>
        void RebuildPages()
        {
            _pages.Clear();
            for (int i = 0; i < Races.Length; i++) _pages.Add(new Page { main = i, sub = -1 });
            if (_fusionMain >= 0) _pages.Add(new Page { main = _fusionMain, sub = _fusionSub, fusion = true });
            _pages.Add(new Page { main = -1, sub = -1, skills = true });
            _pages.Add(new Page { main = -1, sub = -1, aura = true });
            _pages.Add(new Page { main = -1, sub = -1 });
            LayoutTabs();
        }

        int FusionPage()
        {
            return _pages.FindIndex(pg => pg.fusion);
        }

        void LayoutTabs()
        {
            int n = _pages.Count;
            const float gap = 2f, total = PanelWidth - 8f;
            float w = (total - gap * (n - 1)) / n;
            for (int i = 0; i < tabs.Length; i++)
            {
                bool used = i < n;
                tabs[i].gameObject.SetActive(used);
                if (!used) continue;
                var r = tabs[i].GetComponent<RectTransform>();
                r.sizeDelta = new Vector2(w, r.sizeDelta.y);
                r.anchoredPosition = new Vector2(4f + i * (w + gap), r.anchoredPosition.y);
                var p = _pages[i];
                tabLabels[i].text = TwoLines(p.skills ? "Kỹ năng" : p.aura ? "Aura" : p.main < 0 ? "Chức năng" : p.fusion ? "Hợp thể" : Titles[p.main]);
                tabLabels[i].fontSize = n >= 7 ? 13 : n >= 6 ? 14 : 15;
            }
        }

        /// <summary>A tab name on two lines as the Nro tabs ("Nhiệm / Vụ"): the first space becomes a line break.</summary>
        static string TwoLines(string name)
        {
            int space = name.IndexOf(' ');
            return space < 0 ? name : name.Substring(0, space) + "\n" + name.Substring(space + 1);
        }

        int PageOfRace(int race)
        {
            for (int i = 0; i < _pages.Count; i++) if (!_pages[i].fusion && _pages[i].main == race) return i;
            return 0;
        }

        void SelectPage(int page)
        {
            if (page == _page || page < 0 || page >= _pages.Count) return;
            if (!_pages[page].fusion && _pages[page].main >= 0) PlayerPrefs.SetInt(KeyPage, _pages[page].main);
            _page = page;
            ShowPage(page, true);
            if (village != null && village.InArena && training != null && _pages[page].main >= 0)
            {
                // the player takes over that character in the dungeon, nothing is reset; with a fusion the tab of the fusion form and the tabs of its two characters are one fighter
                var chosen = CharacterOf(Races[_pages[page].main]);
                if (_fusionMain >= 0 && !_pages[page].fusion && _pages[page].main == _fusionSub) chosen = CharacterOf(Races[_fusionMain]);
                if (chosen != null) training.SwitchDungeonMain(chosen.CharacterId);
            }
        }

        void Go(int delta)
        {
            SelectPage((_page + delta + _pages.Count) % _pages.Count);
        }

        ProtoCharacter CharacterOf(string race)
        {
            if (_snapshot == null) return null;
            foreach (var c in _snapshot.Characters) if (c.Race == race) return c;
            return null;
        }

        void ShowPage(int page, bool animate)
        {
            if (_pages.Count == 0) RebuildPages();           // Awake has not run when the screenshot tool drives the menu outside play mode
            _page = Mathf.Clamp(page, 0, _pages.Count - 1);
            var pg = _pages[_page];
            for (int i = 0; i < _pages.Count; i++)
            {
                tabImages[i].sprite = i == _page ? tabOn : tabIdle;
                tabLabels[i].color = i == _page ? new Color(0.10f, 0.35f, 0.12f) : new Color(0.45f, 0.18f, 0.05f);
            }
            RefreshEarring();

            bool plainTab = pg.main < 0;                              // the Aura and Chức năng tabs have no character of their own
            bool functionTab = plainTab && !pg.aura && !pg.skills;
            bodyGroup.gameObject.SetActive(!plainTab);
            expTrack.SetActive(!plainTab);
            if (functions != null) { if (functionTab) functions.Show(); else functions.Hide(); }
            if (auras != null) { auras.Tapped = OnAuraTapped; if (pg.aura) auras.Show(_snapshot); else auras.Hide(); }
            if (skills != null) { skills.Upgrade = OnUpgradeSkill; skills.popup = popup; if (pg.skills) skills.Show(_snapshot); else skills.Hide(); }
            ApplyAura();
            if (pg.main >= 0) _lastRace = pg.main;
            if (_snapshot != null)
            {
                goldText.text = Money(_snapshot.Gold);                 // the wallet is the account's, shared by the three characters
                gemText.text = Money(_snapshot.Gems);
            }
            if (plainTab)
            {
                if (animate) { _fade = 0f; }
                return;
            }

            var ch = CharacterOf(Races[pg.main]);
            var partner = pg.fusion ? CharacterOf(Races[pg.sub]) : null;
            if (ch == null)
            {
                headerText.text = "";
                expText.text = "";
                bonusText.text = "";
                expFill.fillAmount = 0f;
                if (_snapshot == null) goldText.text = gemText.text = "0";
                noSkinText.gameObject.SetActive(true);
                noSkinText.text = "Chưa có dữ liệu nhân vật";
                return;
            }

            // header: the four lines of the Nro panel (HP, KI, damage, armor and critical rate); a fusion shows the sum of both characters
            // the fusion form: the main character's stats with the fusion skin counting (FusionStats) plus the partner's own stats
            var mainStats = pg.fusion && ch.FusionStats != null ? ch.FusionStats : ch.Stats;
            double crit = mainStats != null ? mainStats.CritRatePct : 0;
            ulong hp = mainStats != null ? mainStats.HpMax : 0, ki = mainStats != null ? mainStats.KiMax : 0;
            ulong atk = mainStats != null ? mainStats.Atk : 0, armor = mainStats != null ? mainStats.Armor : 0;
            if (partner != null && partner.Stats != null)
            {
                hp += partner.Stats.HpMax;
                ki += partner.Stats.KiMax;
                atk += partner.Stats.Atk;
                armor += partner.Stats.Armor;
                crit = System.Math.Min(100.0, crit + partner.Stats.CritRatePct);
            }
            headerText.text = "HP: " + Money(hp) + "\nKI: " + Money(ki) + "\nSức đánh: " + Money(atk) + "\nGiáp: " + Money(armor) +
                              ", Chí mạng: " + Percent(crit) + "%";
            if (partner != null)
            {
                expFill.fillAmount = ch.ExpToNext > 0 ? Mathf.Clamp01((float)ch.Exp / ch.ExpToNext) : 1f;
                expText.text = "Hợp thể   Chủ cấp " + ch.Level + "  +  Phụ cấp " + partner.Level;
            }
            else if (ch.ExpToNext > 0)
            {
                expFill.fillAmount = Mathf.Clamp01((float)ch.Exp / ch.ExpToNext);
                expText.text = "Cấp " + ch.Level + "   EXP " + Money(ch.Exp) + " / " + Money(ch.ExpToNext);
            }
            else
            {
                expFill.fillAmount = 1f;
                expText.text = "Cấp " + ch.Level + " (tối đa)";
            }

            // equipment: a slot is drawn "filled" when something is equipped there (the item pictures need the item table, which the client does not have yet)
            // the fusion form has no gear and no support slots, only the one slot for the fusion skin (owner request 2026-10-08)
            foreach (var slot in gearSlots) slot.gameObject.SetActive(!pg.fusion);
            foreach (var slot in supportSlots) slot.gameObject.SetActive(!pg.fusion);
            if (!_skinHomeKnown) { _skinHome = skinSlotImage.rectTransform.anchoredPosition; _skinHomeKnown = true; }
            skinSlotImage.rectTransform.anchoredPosition = pg.fusion ? supportSlots[0].rectTransform.anchoredPosition : _skinHome;
            if (!pg.fusion)
            {
                FillSlots(ch, gearSlots, gearLabels, GearKeys);
                FillSlots(ch, supportSlots, supportLabels, SupportKeys);
                FillGearSlots(ch);
            }
            bagText.text = (bagModeButton != null ? "Túi: " : "Hành trang: ") + (_snapshot != null ? _snapshot.BagCount : 0) + " / 2.000";
            bonusText.text = BonusSummary(ch, partner, pg.fusion) + GenSummary(ch);

            // the fusion form looks like the fusion skin; the ordinary skins of the main and the partner do not change its look (owner decision 2026-10-08)
            int worn = 0, wornFusion = 0;
            foreach (var e in ch.Equipment)
            {
                if (e.Slot == "SKIN") worn = (int)e.CostumeId;
                if (e.Slot == "FUSION_SKIN") wornFusion = (int)e.CostumeId;
            }
            int skin = DisplayCostume(ch, pg.fusion);
            RefreshVillage(pg);
            bool shown = skin > 0 && view.Show(skin);
            noSkinText.gameObject.SetActive(!shown);
            if (!shown) noSkinText.text = "Chưa có trang phục";
            SetPortrait(skin);
            _wornHere = pg.fusion ? wornFusion : worn;                      // the skin of the open tab leaves the bag and sits in Hỗ trợ 5
            FillSkinSlot(_wornHere, pg.fusion);
            FillBag(ch);

            if (animate) _fade = 0f;
        }

        /// <summary>The summary lines under the character, as the Nro panel: the totals of the % bonuses of gear, skin and supports (both characters when fused).</summary>
        static string BonusSummary(ProtoCharacter a, ProtoCharacter b, bool fusion)
        {
            var total = new Dictionary<string, double>();
            foreach (var c in new[] { a, b })
            {
                if (c == null) continue;
                var lines = fusion && c == a ? c.FusionBonuses : c.Bonuses;          // the main character of a fusion counts with its fusion skin
                foreach (var line in lines)
                {
                    double v;
                    total.TryGetValue(line.StatKey, out v);
                    total[line.StatKey] = v + line.Value;
                }
            }
            return "HP: " + Pct(total, "HP_PCT") + ", KI: " + Pct(total, "KI_PCT") + "\n" +
                   "SĐ: " + Pct(total, "ATK_PCT") + ", SĐCM: " + Pct(total, "CRIT_DMG_PCT") + "\n" +
                   "Hút HP: " + Pct(total, "LIFESTEAL_PCT") + ", Hút KI: " + Pct(total, "KI_STEAL_PCT") + "\n" +
                   "Giáp: " + Pct(total, "ARMOR_PCT") + ", Né: " + Pct(total, "DODGE_PCT");
        }

        static string Pct(Dictionary<string, double> total, string key)
        {
            double v;
            total.TryGetValue(key, out v);
            return Percent(v) + "%";
        }

        static string Percent(double v)
        {
            return v.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',');
        }

        void FillSlots(ProtoCharacter ch, Image[] slots, Text[] labels, string[] keys)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                bool filled = false;
                foreach (var e in ch.Equipment)
                    if (e.Slot == keys[i] && (e.ItemId != 0 || e.SupportInstanceId != 0)) filled = true;
                slots[i].sprite = filled ? slotFilled : slotEmpty;
                labels[i].color = filled ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.45f);
            }
        }

        void SetPortrait(int costumeId)
        {
            Sprite sp = BbCostumeAvatar.Of(costumeId);
            portrait.sprite = sp;
            portrait.enabled = sp != null;
        }

        static string Money(ulong v)
        {
            return v.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
        }

        // ---------------------------------------------------------------------------------------------------------------- Potara earring (fusion)

        int FusionMinLevel()
        {
            var p = _snapshot != null ? _snapshot.Potara : null;
            return p != null && p.MinFusionLevel > 0 ? (int)p.MinFusionLevel : DefaultFusionMinLevel;
        }

        // ---------------------------------------------------------------------------------------------------------------- the village and the folding panel

        /// <summary>The costume a character shows: its own skin; the fusion form of the main character shows its fusion skin when it wears one.</summary>
        int DisplayCostume(ProtoCharacter ch, bool fusionForm)
        {
            int worn = 0, fusionSkin = 0;
            foreach (var e in ch.Equipment)
            {
                if (e.Slot == "SKIN") worn = (int)e.CostumeId;
                if (e.Slot == "FUSION_SKIN") fusionSkin = (int)e.CostumeId;
            }
            if (fusionForm && fusionSkin > 0) return fusionSkin;
            var info = SkinOf((uint)worn);
            if (info != null && info.FusionMainOnly && ch.DefaultCostumeId > 0) return (int)ch.DefaultCostumeId;      // never a fusion skin on the character itself
            if (worn == 0 && ch.DefaultCostumeId > 0) return (int)ch.DefaultCostumeId;                              // taken off: the race's own look
            return worn;
        }

        /// <summary>
        /// The party in the village. While a fusion exists the fused form stands there (the main character with its fusion skin, the partner merged into it) next to the
        /// remaining character as AI, whichever tab is open: looking at the main or the partner tab does not split it, only the earring does (owner decision 2026-10-08).
        /// Without a fusion the character of the open tab is the main one and the other two stand beside it as AI.
        /// </summary>
        void RefreshVillage(Page pg)
        {
            if (village != null && village.InArena) return;                  // the arena of a dungeon has its own party
            bool fused = _fusionMain >= 0;
            int mainRace = fused ? _fusionMain : pg.main;
            var main = CharacterOf(Races[mainRace]);
            if (main == null) return;
            var cast = new List<BbVillageView.ActorSpec>();
            cast.Add(new BbVillageView.ActorSpec { Id = main.CharacterId, Name = fused ? "Hợp thể" : Titles[mainRace], CostumeId = DisplayCostume(main, fused), Main = true });
            for (int i = 0; i < Races.Length; i++)
            {
                if (i == mainRace || (fused && i == _fusionSub)) continue;      // in the fusion form the partner is merged into the main character
                var ch = CharacterOf(Races[i]);
                if (ch == null) continue;
                cast.Add(new BbVillageView.ActorSpec { Id = ch.CharacterId, Name = Titles[i], CostumeId = DisplayCostume(ch, false), Main = false });
            }
            village.SetCast(cast);
            if (Application.isPlaying) FusionFx.SetFused(fused ? main.CharacterId : 0);          // not in the screenshot tool, which builds the menu outside play mode
        }

        BbFusionFx _fusionFx;

        public BbFusionFx FusionEffects { get { return FusionFx; } }

        /// <summary>The fusion effects (merging, splitting, the aura of the merged form), made when first needed under the training layer so that the buttons stay on top.</summary>
        BbFusionFx FusionFx
        {
            get
            {
                if (_fusionFx != null) return _fusionFx;
                var parent = training != null ? training.transform : transform;
                var go = new GameObject("FusionFx", typeof(RectTransform), typeof(BbFusionFx));
                go.transform.SetParent(parent, false);
                _fusionFx = go.GetComponent<BbFusionFx>();
                _fusionFx.village = village;
                _fusionFx.menu = this;
                return _fusionFx;
            }
        }

        /// <summary>The player talks to the NPC in focus (the fist button): its dialog, its shop or its panel opens.</summary>
        public void InteractNpc(NpcSpec npc) { OnNpc(npc); }

        void OnNpc(NpcSpec npc)
        {
            if (npc.Action == NpcAction.Shop && shop != null) { OpenShop(npc); return; }
            if (npc.Action == NpcAction.Smith && smith != null) { OpenSmithMenu(npc); return; }
            if (npc.Action == NpcAction.Attendance && attendance != null) { OpenAttendanceDialog(npc); return; }
            if (npc.Action == NpcAction.Quest && quest != null) { OpenQuestDialog(npc); return; }
            var specs = new List<BbPopup.ButtonSpec>();
            if (npc.Action == NpcAction.DungeonGate && dungeonPicker != null) specs.Add(new BbPopup.ButtonSpec("Chọn\nphó bản", true, true));
            popup.centerOnScreen = true;
            popup.Show(null, npc.Name, "", npc.Talk, null, null, specs, specs.Count == 0 ? (Action<int>)null : i => { popup.Hide(); OpenDungeonList(npc); });
        }

        /// <summary>True while the panel, a popup (an NPC talking, an item) or the function list is open: the combat buttons stay hidden then (owner request 2026-10-09).</summary>
        public bool BlocksCombatUi
        {
            get { return _panelOpen || popup.IsOpen || chooserRoot.activeSelf || (functions != null && functions.IsOpen) || (shop != null && shop.IsOpen) || (smith != null && smith.IsOpen) || (attendance != null && attendance.IsOpen) || (quest != null && quest.IsOpen) || DungeonBlocksCombatUi || (dungeonPicker != null && dungeonPicker.IsOpen); }
        }

        /// <summary>Folds the panel away to the left (the village fills the screen, the X of the panel) or brings it back (the triangle); the choice is remembered.</summary>
        public bool PanelOpen { get { return _panelOpen; } }

        public void SetPanelOpen(bool open, bool immediate, bool remember = true)
        {
            _panelOpen = open;
            panelToggle.gameObject.SetActive(!open && !DungeonBlocksCombatUi);
            if (remember && !_demo && !_dockRight) PlayerPrefs.SetInt(KeyPanel, open ? 1 : 0);        // beside a shop the panel is open on purpose: keep what the player chose
            if (immediate) { _panelX = open ? 0f : -PanelWidth; ApplyPanelX(); }
        }

        void ApplyPanelX()
        {
            panelRect.anchoredPosition = new Vector2(_dockRight ? -_panelX : _panelX, 0f);
            panelToggle.gameObject.SetActive(!_panelOpen && !DungeonBlocksCombatUi);          // Nro shows its menu triangle only while the panel is closed (and not while the ship flies)
            ApplyInsets();
        }

        /// <summary>Tells the village how much of each side the panels cover (the menu on one side, the shop on the left), so that it centres on what is left.</summary>
        public void ApplyInsets()
        {
            float menuW = Mathf.Clamp(PanelWidth + _panelX, 0f, PanelWidth);
            float shopW = Mathf.Max(Mathf.Max(shop != null ? shop.VisibleWidth : 0f, smith != null ? smith.VisibleWidth : 0f), Mathf.Max(attendance != null ? attendance.VisibleWidth : 0f, quest != null ? quest.VisibleWidth : 0f));
            village.SetInset(_dockRight ? shopW : Mathf.Max(menuW, shopW), _dockRight ? menuW : 0f);
        }

        // ---------------------------------------------------------------------------------------------------------------- the NPC shop

        /// <summary>The panel stands on the right (beside the shop) or back on the left; it slides in from its own side. The dark frame and the orange triangle go to the side of the village.</summary>
        void SetDock(bool right)
        {
            if (_dockRight == right) return;
            _dockRight = right;
            float side = right ? 1f : 0f;
            panelRect.anchorMin = new Vector2(side, 0f);
            panelRect.anchorMax = new Vector2(side, 1f);
            panelRect.pivot = new Vector2(side, 0.5f);
            var edge = panelRect.Find("Edge") as RectTransform;
            if (edge != null)
            {
                edge.anchorMin = new Vector2(1f - side, 0f);
                edge.anchorMax = new Vector2(1f - side, 1f);
                edge.pivot = new Vector2(1f - side, 0.5f);
            }
            panelToggleRect.anchorMin = panelToggleRect.anchorMax = new Vector2(side, 0.75f);
            panelToggleRect.pivot = new Vector2(side, 0.5f);
            panelToggleRect.localScale = new Vector3(right ? -1f : 1f, 1f, 1f);
            _panelX = -PanelWidth;
            ApplyPanelX();
        }

        /// <summary>Taps on an NPC that sells something: the server sends its shop (ShopRequest), then the shop opens on the left and this panel moves to the right.</summary>
        async void OpenShop(NpcSpec npc)
        {
            if (_shopBusy) return;
            if (_demo || !BbSession.IsActive) { Toast("Cửa hàng cần kết nối máy chủ"); return; }
            _shopBusy = true;
            ShopContent content = null;
            try { content = await BbSession.Client.ShopAsync(npc.Id); }
            catch (Exception e) { Toast("Không mở được cửa hàng: " + e.Message); }
            _shopBusy = false;
            if (content != null) ShowShop(content, false);
        }

        /// <summary>The popups of this panel stand under it, on whichever side it is (a shop moves it to the right).</summary>
        void WirePopupSide()
        {
            popup.areaLeftProvider = () => _dockRight ? Mathf.Max(0f, ((RectTransform)popup.root.transform).rect.width - PanelWidth) : 0f;
        }

        /// <summary>Tap on Bà Hạt Mít: she asks what to do (as the menu of the NPC in Nro); the choice opens her panel on the left and moves the player's panel to the right.</summary>
        void OpenSmithMenu(NpcSpec npc)
        {
            var specs = new List<BbPopup.ButtonSpec>
            {
                new BbPopup.ButtonSpec("Nâng cấp\ntrang bị", true, true),
                new BbPopup.ButtonSpec("Ép\nsao", true, false),
                new BbPopup.ButtonSpec("Pha lê\nhóa", true, false),
                new BbPopup.ButtonSpec("Đổi\nGEN", true, false),
                new BbPopup.ButtonSpec("Mở ô\nGEN", true, false),
            };
            WirePopupSide();
            popup.centerOnScreen = true;
            popup.Show(null, npc.Name, "", "Ta có thể giúp gì cho ngươi ?", null, null, specs, i =>
            {
                popup.Hide();
                OpenSmith(i == 0 ? BbSmithPanel.Mode.Upgrade : i == 1 ? BbSmithPanel.Mode.Star : i == 2 ? BbSmithPanel.Mode.Crystal : i == 3 ? BbSmithPanel.Mode.Reroll : BbSmithPanel.Mode.Unlock, false);
            });
        }

        void OpenSmith(BbSmithPanel.Mode mode, bool immediate)
        {
            if (shop != null && shop.IsOpen) shop.Close();
            WirePopupSide();
            popup.Hide();
            if (!_dockRight) _savedOpen = _panelOpen;
            SetDock(true);
            SetPanelOpen(true, immediate);
            if (!_gearMode) ToggleGearMode();                                     // the bag shows the pieces and the stones
            smith.Open(mode, _snapshot, immediate);
        }

        /// <summary>For the screenshot tool: what Bà Hạt Mít asks when she is tapped.</summary>
        public void DemoSmithMenu() { OpenSmithMenu(new NpcSpec { Id = "shop", Name = "Bà Hạt Mít" }); }

        /// <summary>For the screenshot tool: open the panel of Bà Hạt Mít.</summary>
        public void DemoSmith(BbSmithPanel.Mode mode) { OpenSmith(mode, true); }

        void ShowShop(ShopContent content, bool immediate)
        {
            WirePopupSide();
            popup.Hide();
            if (!_dockRight) _savedOpen = _panelOpen;
            SetDock(true);
            SetPanelOpen(true, immediate);                                        // Nro opens the player's panel next to a shop
            shop.Open(content, _snapshot, immediate);
        }

        void OnShopClosed()
        {
            SetDock(false);
            SetPanelOpen(_demo ? _savedOpen : PlayerPrefs.GetInt(KeyPanel, 1) != 0, false);
        }

        /// <summary>For the screenshot tool: open the shop with this (made-up) content.</summary>
        public void DemoShop(ShopContent content)
        {
            ShowShop(content, true);
        }

        /// <summary>Called by the shop after a purchase that was made without a server (the demo): show the new skin in the bag.</summary>
        public void OnShopPurchase(PlayerDataSnapshot snapshot)
        {
            _snapshot = snapshot;
            ShowPage(_page, false);
        }

        // ---------------------------------------------------------------------------------------------------------------- auras

        int _lastRace;                       // the character page open last (the preview of an aura shows this character)

        AuraInfo AuraOf(uint id)
        {
            if (_snapshot == null || id == 0) return null;
            foreach (var a in _snapshot.Auras) if (a.AuraId == id) return a;
            return null;
        }

        /// <summary>The aura of the account is on every character: the one of the tab and the whole party of the village.</summary>
        void ApplyAura()
        {
            var info = AuraOf(_snapshot != null ? _snapshot.EquippedAuraId : 0);
            int id = info != null ? (int)info.AuraId : 0;
            if (village != null) village.SetAura(id, info != null ? info.Layers : null);
            if (view != null) view.SetAura(id, info != null ? info.Layers : null);
        }

        /// <summary>Tap on an aura of the Aura tab: the preview on a character with the button that wears it or takes it off.</summary>
        void OnAuraTapped(AuraInfo aura)
        {
            uint level = BbMenuAuras.HighestLevel(_snapshot);
            bool open = aura.UnlockLevel <= level;
            bool worn = _snapshot != null && _snapshot.EquippedAuraId == aura.AuraId;
            var ch = CharacterOf(Races[_lastRace]) ?? CharacterOf(Races[0]);
            int costume = ch != null ? DisplayCostume(ch, false) : 0;
            var specs = new List<BbPopup.ButtonSpec>();
            if (worn) specs.Add(new BbPopup.ButtonSpec("Tháo", true, true));
            else specs.Add(new BbPopup.ButtonSpec(open ? "Trang bị" : "Chưa đủ cấp " + aura.UnlockLevel, open, true));
            string text = open ? "Hiệu ứng hình ảnh, dùng chung cho cả 3 hành tinh. Chỉ hiện khi nhân vật đứng yên."
                               : "Mở khi nhân vật cao nhất của tài khoản đạt cấp " + aura.UnlockLevel + " (hiện là cấp " + level + ").";
            popup.Show(null, BbMenuAuras.NameOf(aura), "", text, null, null, specs,
                i => { if (worn) EquipAura(0); else EquipAura(aura.AuraId); }, null, costume > 0 ? costume : 1, aura);
        }

        async void EquipAura(uint auraId)
        {
            popup.Hide();
            if (_demo || !BbSession.IsActive)
            {
                if (_snapshot != null) _snapshot.EquippedAuraId = auraId;                // no server: only change what is shown
                ShowPage(_page, false);
                return;
            }
            try
            {
                var result = await BbSession.Client.EquipAuraAsync(auraId);
                Toast(result.Ok ? (auraId == 0 ? "Đã tháo aura" : "Đã trang bị aura") : ActionText(result));
            }
            catch (Exception e)
            {
                Toast("Không gửi được lệnh: " + e.Message);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- the Kỹ năng tab

        /// <summary>Learn or raise a skill with the gold of the account (the server checks the race, the level cap and the gold, then pushes a new snapshot).</summary>
        async void OnUpgradeSkill(ulong characterId, SkillCatalogEntry entry)
        {
            ProtoCharacter ch = null;
            if (_snapshot != null) foreach (var c in _snapshot.Characters) if (c.CharacterId == characterId) ch = c;
            if (ch == null) return;
            uint level = 0;
            SkillState state = null;
            foreach (var s in ch.Skills) if (s.SkillId == entry.SkillId) { level = s.Level; state = s; }
            SkillLevelInfo next = null;
            foreach (var l in entry.Levels) if (l.Level == level + 1) next = l;
            if (next == null) { Toast("Đã đạt cấp tối đa"); return; }
            if (_demo || !BbSession.IsActive)
            {
                if (_snapshot.Gold < next.GoldCost) { Toast("Không đủ vàng"); return; }       // no server: only change what is shown
                if (BbQuestPanel.CardsOf(_snapshot) < next.CardCost) { Toast("Không đủ thẻ nâng cấp"); return; }
                _snapshot.Gold -= next.GoldCost;
                if (state == null) ch.Skills.Add(new SkillState { SkillId = entry.SkillId, Level = 1 }); else state.Level = level + 1;
                ShowPage(_page, false);
                Toast((level == 0 ? "Đã học " : "Đã nâng ") + entry.Name + " lên cấp " + (level + 1));
                return;
            }
            try
            {
                var result = await BbSession.Client.UpgradeSkillAsync(characterId, entry.SkillId);
                Toast(result.Ok ? (level == 0 ? "Đã học " : "Đã nâng ") + entry.Name + " lên cấp " + (level + 1)
                      : result.Error == ActionError.NotEnoughMaterial ? "Không đủ thẻ nâng cấp: " + next.CardCost + " thẻ (làm nhiệm vụ của Ông Gôhan để nhận)" : ActionText(result));
            }
            catch (Exception e)
            {
                Toast("Không gửi được lệnh: " + e.Message);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- the shared bag and the skin popup

        readonly List<SkinInfo> _bagSkins = new List<SkinInfo>();
        int _bagPage;

        static int RaceIndex(string race)
        {
            return Array.IndexOf(Races, race);
        }

        SkinInfo SkinOf(uint costumeId)
        {
            if (_snapshot == null) return null;
            foreach (var skin in _snapshot.Skins) if (skin.CostumeId == costumeId) return skin;
            return null;
        }

        /// <summary>Whether a character of this race may wear the skin: the race of the skin, and a fusion skin only for the race it was made for.</summary>
        static bool Fits(SkinInfo skin, string race)
        {
            int r = race == "EARTH" ? 0 : race == "NAMEC" ? 1 : 2;
            if (skin.GenderId != 3 && skin.GenderId != r) return false;
            switch (skin.FusionRace)
            {
                case "td": return race == "EARTH";
                case "nm": return race == "NAMEC";
                case "xd": return race == "XAYDA";
                default: return true;
            }
        }

        int _wornHere;                       // costume worn in the slot of the open tab (SKIN, or FUSION_SKIN on the fusion tab), 0 = none
        Vector2 _skinHome;                   // where the builder put Hỗ trợ 5 (the last slot of the right column)
        bool _skinHomeKnown;
        int _selectedCell = -1;              // the slot whose popup is open, drawn yellow as in Nro: a bag slot index, SkinSlotCell, or -1

        const int SkinSlotCell = -2;

        Sprite BagCell { get { return bagCell != null ? bagCell : slotEmpty; } }

        /// <summary>The slot of the item a popup shows is yellow while the popup is open (Nro: the selected slot).</summary>
        void RefreshSelection()
        {
            for (int i = 0; i < bagSlotImages.Length; i++)
                bagSlotImages[i].sprite = i == _selectedCell && cellSelected != null ? cellSelected : BagCell;
            skinSlotImage.sprite = _selectedCell == SkinSlotCell && cellSelected != null ? cellSelected : _wornHere > 0 ? slotFilled : slotEmpty;
            ApplyGearSelection();
        }

        /// <summary>
        /// The bag slots that fit above the money strip: the builder makes six rows for the 720 px reference height, a wide phone has a shorter canvas
        /// (the scaler matches width and height halfway), so the rows that would run under the strip are hidden and a page holds fewer skins.
        /// </summary>
        int BagPerPage()
        {
            float height = panelRect.rect.height;
            var footer = panelRect.Find("Footer") as RectTransform;
            float limit = height - (footer != null ? footer.rect.height : 0f) + 1f;
            int visible = 0;
            for (int i = 0; i < bagSlotImages.Length; i++)
            {
                var r = bagSlotImages[i].rectTransform;
                bool fits = height < 100f || -r.anchoredPosition.y + r.sizeDelta.y <= limit;      // not laid out yet: keep them all
                bagSlotImages[i].gameObject.SetActive(fits);
                if (fits) visible = i + 1;
            }
            return Mathf.Max(1, visible);
        }

        /// <summary>Hỗ trợ 5: the picture of the skin the open tab wears, or the empty slot.</summary>
        void FillSkinSlot(int costumeId, bool fusionForm)
        {
            Sprite sp = BbCostumeAvatar.Of(costumeId);
            skinSlotIcon.sprite = sp;
            skinSlotIcon.enabled = sp != null;
            bool filled = costumeId > 0;
            skinSlotImage.sprite = _selectedCell == SkinSlotCell && cellSelected != null ? cellSelected : filled ? slotFilled : slotEmpty;
            skinSlotLabel.text = fusionForm && !filled ? "Hợp\nthể" : "";
            skinSlotLabel.alignment = TextAnchor.MiddleCenter;
            skinSlotLabel.color = filled ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.45f);
        }

        // ---- the order of the shared bag: pinned skins first (the latest pin in front), then by costume id; the same on every tab (owner request 2026-10-08)
        readonly List<uint> _pins = new List<uint>();
        ulong _pinsAccount;

        string PinsKey { get { return "bb.pins." + _pinsAccount; } }

        void LoadPins()
        {
            ulong account = _snapshot != null ? _snapshot.AccountId : 0;
            if (account == _pinsAccount) return;
            _pinsAccount = account;
            _pins.Clear();
            if (_demo || account == 0) return;
            foreach (var part in PlayerPrefs.GetString(PinsKey, "").Split(','))
            {
                uint id;
                if (uint.TryParse(part, out id) && !_pins.Contains(id)) _pins.Add(id);
            }
        }

        void SavePins()
        {
            if (_demo || _pinsAccount == 0) return;
            PlayerPrefs.SetString(PinsKey, string.Join(",", _pins.ConvertAll(p => p.ToString()).ToArray()));
        }

        int CompareBag(SkinInfo a, SkinInfo b)
        {
            int pa = _pins.IndexOf(a.CostumeId), pb = _pins.IndexOf(b.CostumeId);
            if ((pa >= 0) != (pb >= 0)) return pa >= 0 ? -1 : 1;
            return pa >= 0 ? pa.CompareTo(pb) : a.CostumeId.CompareTo(b.CostumeId);
        }

        /// <summary>The costumes any character of the account wears now (its own skin or the fusion skin): they sit in Hỗ trợ 5 of that character's tab, not in the bag.</summary>
        HashSet<uint> WornByAnyone()
        {
            var worn = new HashSet<uint>();
            if (_snapshot == null) return worn;
            foreach (var c in _snapshot.Characters)
                foreach (var e in c.Equipment)
                    if ((e.Slot == "SKIN" || e.Slot == "FUSION_SKIN") && e.CostumeId != 0) worn.Add(e.CostumeId);
            return worn;
        }

        /// <summary>The slots after the earring show the skins of the account that nobody wears (picture of the costume), fourteen to a page, in the same order on every tab.</summary>
        void FillBag(ProtoCharacter ch)
        {
            if (bagModeLabel != null) bagModeLabel.text = _gearMode ? "Xem skin" : "Xem đồ";
            if (_gearMode) { FillGearBag(ch); return; }
            _bagSkins.Clear();
            if (_snapshot != null)
            {
                var worn = WornByAnyone();
                foreach (var skin in _snapshot.Skins) if (!worn.Contains(skin.CostumeId)) _bagSkins.Add(skin);
            }
            _bagSkins.Sort(CompareBag);
            int perPage = BagPerPage();
            int pages = Mathf.Max(1, (_bagSkins.Count + perPage - 1) / perPage);
            _bagPage = Mathf.Clamp(_bagPage, 0, pages - 1);
            bagPrev.gameObject.SetActive(pages > 1);
            bagNext.gameObject.SetActive(pages > 1);
            bagPageText.text = pages > 1 ? (_bagPage + 1) + "/" + pages : "";
            for (int i = 0; i < perPage; i++)
            {
                int index = _bagPage * perPage + i;
                bool has = index < _bagSkins.Count;
                var info = has ? _bagSkins[index] : null;
                Sprite sp = has ? BbCostumeAvatar.Of((int)info.CostumeId) : null;
                bagIcons[i].sprite = sp;
                bagIcons[i].enabled = sp != null;
                bagIcons[i].color = has && !Fits(info, ch.Race) ? new Color(1f, 1f, 1f, 0.4f) : Color.white;        // another race's skin is dimmed
                bagSlotImages[i].sprite = i == _selectedCell && cellSelected != null ? cellSelected : BagCell;
                bagCaptions[i].text = has && _pins.Contains(info.CostumeId) ? "Ghim" : "";
            }
        }

        void ChangeBagPage(int delta)
        {
            _bagPage += delta;
            ShowPage(_page, false);
        }

        /// <summary>Tap on a skin of the bag: the popup with its name, bonuses and description, and one button for every character who may use it.</summary>
        void OnBagSkin(int slot)
        {
            if (_gearMode) { OnBagGear(slot); return; }
            int index = _bagPage * BagPerPage() + slot;
            if (_snapshot == null || index >= _bagSkins.Count) return;
            ShowSkinPopup(_bagSkins[index], false);
            _selectedCell = slot;
            RefreshSelection();
        }

        /// <summary>Tap on Hỗ trợ 5: the popup of the skin the open tab wears, with the button that takes it off.</summary>
        void OnWornSkin()
        {
            if (_wornHere <= 0) return;
            ShowSkinPopup(SkinOf((uint)_wornHere) ?? new SkinInfo { CostumeId = (uint)_wornHere }, true);
            _selectedCell = SkinSlotCell;
            RefreshSelection();
        }

        void ShowSkinPopup(SkinInfo skin, bool wornHere)
        {
            Sprite icon = BbCostumeAvatar.Of((int)skin.CostumeId);

            var lines = new StringBuilder();
            foreach (var stat in skin.Stats)
            {
                if (lines.Length > 0) lines.Append('\n');
                lines.Append("<color=#1E8F1E>").Append(stat.Text.Length > 0 ? stat.Text : stat.StatKey + " +" + Percent(stat.Value)).Append("</color>");
            }
            if (skin.Stats.Count == 0) lines.Append("<color=#7A5A3A>Không có chỉ số cộng thêm</color>");

            var specs = new List<BbPopup.ButtonSpec>();
            var targets = new List<int>();
            if (wornHere)
            {
                bool fusionTab = _pages[_page].fusion;
                int race = fusionTab ? Mathf.Max(0, _fusionMain) : _pages[_page].main;
                specs.Add(new BbPopup.ButtonSpec("Tháo", true, true));
                popup.Show(icon, skin.Name.Length > 0 ? skin.Name : "Cải trang #" + skin.CostumeId, lines.ToString(), SkinDescription(skin), null, null, specs,
                    i => UnequipSkin(race, fusionTab), null, (int)skin.CostumeId);
                return;
            }
            if (skin.FusionMainOnly)
            {
                // a fusion skin is worn by the fusion form: one button, usable while a fusion exists and its main character is of the race of the skin
                var fusionMain = _fusionMain >= 0 ? CharacterOf(Races[_fusionMain]) : null;
                bool usable = fusionMain != null && Fits(skin, fusionMain.Race);
                specs.Add(new BbPopup.ButtonSpec("Dùng cho hợp thể", usable, true));
                targets.Add(Mathf.Max(0, _fusionMain));
            }
            else
            {
                int open = _pages[_page].main;
                var ordered = new List<int>();
                if (open >= 0) ordered.Add(open);
                for (int r = 0; r < Races.Length; r++) if (r != open) ordered.Add(r);
                foreach (int r in ordered)
                {
                    var c = CharacterOf(Races[r]);
                    if (c == null || !Fits(skin, c.Race)) continue;
                    specs.Add(new BbPopup.ButtonSpec(r == open ? "Sử dụng" : "Dùng cho\n" + Titles[r], true, r == open));
                    targets.Add(r);
                }
            }
            int pinButton = specs.Count;
            specs.Add(new BbPopup.ButtonSpec(_pins.Contains(skin.CostumeId) ? "Bỏ ghim" : "Ghim", true, false));
            specs.Add(new BbPopup.ButtonSpec("Vứt", true, false));
            popup.Show(icon, skin.Name.Length > 0 ? skin.Name : "Cải trang #" + skin.CostumeId, lines.ToString(), SkinDescription(skin), null, null, specs,
                i =>
                {
                    if (i < targets.Count) EquipSkin(targets[i], skin, skin.FusionMainOnly);
                    else if (i == pinButton) TogglePin(skin);
                    else ConfirmDiscard(skin, icon);
                }, null, (int)skin.CostumeId);
        }

        /// <summary>Ghim / bỏ ghim: a pinned skin goes to the front of the bag (the latest pin first).</summary>
        void TogglePin(SkinInfo skin)
        {
            popup.Hide();
            if (_pins.Contains(skin.CostumeId)) _pins.Remove(skin.CostumeId);
            else _pins.Insert(0, skin.CostumeId);
            SavePins();
            _bagPage = 0;
            ShowPage(_page, false);
        }

        void ConfirmDiscard(SkinInfo skin, Sprite icon)
        {
            var specs = new List<BbPopup.ButtonSpec> { new BbPopup.ButtonSpec("Vứt", true, false), new BbPopup.ButtonSpec("Hủy", true, true) };
            popup.Show(icon, skin.Name.Length > 0 ? skin.Name : "Cải trang #" + skin.CostumeId, "", "Vứt cải trang này? Vứt rồi sẽ không lấy lại được.", null, null, specs,
                i => { if (i == 0) DiscardSkin(skin); else popup.Hide(); }, null, (int)skin.CostumeId);
        }

        async void DiscardSkin(SkinInfo skin)
        {
            popup.Hide();
            string name = skin.Name.Length > 0 ? skin.Name : "cải trang";
            if (_demo || !BbSession.IsActive)
            {
                if (_snapshot != null) _snapshot.Skins.Remove(skin);                 // no server: only change what is shown
                _pins.Remove(skin.CostumeId);
                ShowPage(_page, false);
                return;
            }
            try
            {
                var result = await BbSession.Client.DiscardSkinAsync(skin.CostumeId);
                if (result.Ok) { _pins.Remove(skin.CostumeId); SavePins(); }
                Toast(result.Ok ? "Đã vứt " + name : ActionText(result));
            }
            catch (Exception e)
            {
                Toast("Không gửi được lệnh: " + e.Message);
            }
        }

        string SkinDescription(SkinInfo skin)
        {
            var sb = new StringBuilder();
            if (skin.FusionMainOnly)
            {
                string planet = skin.FusionRace == "td" ? "Trái Đất" : skin.FusionRace == "nm" ? "Namếc" : skin.FusionRace == "xd" ? "Xayda" : "";
                sb.Append("Đổi ngoại hình hợp thể, tăng chỉ số khi dùng Bông tai");
                if (planet.Length > 0) sb.Append(" (chủ hành tinh ").Append(planet).Append(')');
                sb.Append(". Cải trang thường của chủ/phụ chỉ tăng chỉ số riêng.");
                if (_fusionMain < 0) sb.Append("\nHãy dùng Bông tai để hợp thể trước.");
            }
            else
            {
                sb.Append("Dùng cho: ").Append(skin.GenderId == 3 ? "mọi hành tinh" : Titles[GenderToTab(skin.GenderId)]).Append('.');
            }
            return sb.ToString();
        }

        static int GenderToTab(uint gender)          // gender ids are 0 Trái Đất, 1 Namếc, 2 Xayda; the tabs are Trái Đất, Xayda, Namếc
        {
            return gender == 0 ? 0 : gender == 1 ? 2 : 1;
        }

        async void EquipSkin(int race, SkinInfo skin, bool fusion)
        {
            popup.Hide();
            var ch = CharacterOf(Races[race]);
            if (ch == null) return;
            if (_demo || !BbSession.IsActive)
            {
                string slot = fusion ? "FUSION_SKIN" : "SKIN";                    // no server: only change what is shown
                bool found = false;
                foreach (var e in ch.Equipment) if (e.Slot == slot) { e.CostumeId = skin.CostumeId; found = true; }
                if (!found) ch.Equipment.Add(new EquipSlotInfo { Slot = slot, CostumeId = skin.CostumeId });
                ShowPage(_page, false);
                return;
            }
            try
            {
                var result = await BbSession.Client.EquipSkinAsync(ch.CharacterId, skin.CostumeId, fusion);
                Toast(result.Ok ? "Đã mặc " + (skin.Name.Length > 0 ? skin.Name : "cải trang") + " cho " + Titles[race] : ActionText(result));
            }
            catch (Exception e)
            {
                Toast("Không gửi được lệnh: " + e.Message);
            }
        }

        /// <summary>Takes the skin off the slot of the open tab (the character shows the look of its race again).</summary>
        async void UnequipSkin(int race, bool fusion, bool quiet = false)
        {
            popup.Hide();
            var ch = CharacterOf(Races[race]);
            if (ch == null) return;
            if (_demo || !BbSession.IsActive)
            {
                string slot = fusion ? "FUSION_SKIN" : "SKIN";                    // no server: only change what is shown
                for (int i = ch.Equipment.Count - 1; i >= 0; i--) if (ch.Equipment[i].Slot == slot) ch.Equipment.RemoveAt(i);
                ShowPage(_page, false);
                return;
            }
            try
            {
                var result = await BbSession.Client.EquipSkinAsync(ch.CharacterId, 0, fusion);
                if (!result.Ok || !quiet) Toast(result.Ok ? "Đã tháo cải trang" : ActionText(result));
            }
            catch (Exception e)
            {
                Toast("Không gửi được lệnh: " + e.Message);
            }
        }

        public static string ActionText(ActionResult r)
        {
            switch (r.Error)
            {
                case ActionError.NotFound: return "Không tìm thấy";
                case ActionError.RaceLocked: return "Cải trang này không dành cho hành tinh đó";
                case ActionError.NotOwned: return "Chưa sở hữu";
                case ActionError.BagFull: return "Hành trang đầy";
                case ActionError.Disabled: return r.Message.Length > 0 ? ViMessage(r.Message) : "Không dùng được";
                case ActionError.AlreadyOwned: return "Đã nhận rồi";
                case ActionError.LevelTooLow: return "Chưa đủ cấp";
                case ActionError.NotEnoughGold: return "Không đủ vàng";
                case ActionError.Maxed: return "Đã đạt cấp tối đa";
                case ActionError.NotEnoughGems: return "Không đủ đá quý";
                default: return "Lỗi: " + r.Message;
            }
        }

        static string ViMessage(string english)
        {
            return english == "take the gift first" ? "Hãy nhận quà trước khi xóa" : english == "take the skin off first" ? "Hãy tháo cải trang ra trước" : english;
        }

        /// <summary>The first slot of the bag is always the Potara: the earring when owned (tap to use it), the fragment with its count until then.</summary>
        void RefreshEarring()
        {
            var p = _snapshot != null ? _snapshot.Potara : null;
            bool owned = p != null && p.Owned;
            var resolver = NroSpriteResolver.Instance;
            uint iconId = p == null ? 0 : (owned ? p.IconId : p.ShardIconId);
            Sprite sp = iconId > 0 && resolver != null ? resolver.ResolveSprite((int)iconId, BbAssetCache.Scale) : null;
            earringIcon.sprite = sp;
            earringIcon.enabled = sp != null;
            earringIcon.color = Color.white;
            bool onFusion = _fusionMain >= 0;                                      // the earring is in use for the whole account: "Tách" on every tab while it merges two characters
            earringSlotImage.sprite = BagCell;
            int needed = p != null && p.ShardsNeeded > 0 ? (int)p.ShardsNeeded : DefaultPotaraShards;
            earringCaption.text = onFusion ? "Tách" : owned ? "Dùng" : (p != null ? p.Fragments : 0) + "/" + needed;
            earringCaption.color = onFusion ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, owned ? 0.95f : 0.7f);
            potaraText.text = _fusionMain >= 0 ? "Hợp thể: " + Titles[_fusionMain] + " (chủ) + " + Titles[_fusionSub] : "";
        }

        /// <summary>
        /// The earring is an item like a skin (owner request 2026-10-10): tapping it opens its popup. Not in use: "Sử dụng" for the planet of the open tab as the main character and
        /// "Dùng cho X" for each other planet (the partner is chosen next). In use: "Tách", the same on every tab (it merges the characters of the whole account).
        /// </summary>
        void OnEarring()
        {
            var potara = _snapshot != null ? _snapshot.Potara : null;
            if (potara == null || !potara.Owned) { Toast("Chưa có Bông tai Potara"); return; }
            if (InDungeon) { Toast("Không thể hợp thể hay tách khi đang ở phó bản"); return; }          // the squad of a match is fixed when it starts (docs 15.26)
            var resolver = NroSpriteResolver.Instance;
            Sprite icon = resolver != null && potara.IconId > 0 ? resolver.ResolveSprite((int)potara.IconId, BbAssetCache.Scale) : null;
            var specs = new List<BbPopup.ButtonSpec>();
            var mains = new List<int>();
            string lines, desc = "Hợp thể hai nhân vật: chỉ số cộng lại, nhân vật chủ giữ kỹ năng. Cả hai cần đạt cấp " + FusionMinLevel() + ". Dùng chung cho cả tài khoản: tách ở tab nào cũng được.";
            bool fused = _fusionMain >= 0;
            if (fused)
            {
                lines = "<color=#2E7D32>Đang dùng</color>\nHợp thể: " + Titles[_fusionMain] + " (chủ) + " + Titles[_fusionSub];
                specs.Add(new BbPopup.ButtonSpec("Tách", true, true));
            }
            else
            {
                lines = "Chưa dùng";
                int open = _pages[_page].main >= 0 ? _pages[_page].main : Mathf.Max(0, _lastRace);
                var ordered = new List<int> { open };
                for (int r = 0; r < Races.Length; r++) if (r != open) ordered.Add(r);
                foreach (int r in ordered)
                {
                    var c = CharacterOf(Races[r]);
                    bool ready = c != null && c.Level >= FusionMinLevel();
                    specs.Add(new BbPopup.ButtonSpec(r == open ? "Sử dụng" : "Dùng cho\n" + Titles[r], ready, r == open));
                    mains.Add(r);
                }
            }
            popup.Show(icon, "Bông tai Potara", lines, desc, null, null, specs, i =>
            {
                popup.Hide();
                if (fused) { SplitFusion(); return; }
                int main = mains[i];
                var others = new List<int>();
                for (int r = 0; r < Races.Length; r++) if (r != main) others.Add(r);
                OpenChooser(main, others);
            });
        }

        readonly List<int> _choices = new List<int>();
        int _chooserMain;

        /// <summary>The second step of "Sử dụng" is a popup of the same kind as the first (owner remark 2026-10-10: the old chooser dialog looked out of place): one button per other planet.</summary>
        void OpenChooser(int main, List<int> candidates)  // all the other planets, so the player always sees the choice
        {
            _chooserMain = main;
            _choices.Clear();
            _choices.AddRange(candidates);
            var potara = _snapshot != null ? _snapshot.Potara : null;
            var resolver = NroSpriteResolver.Instance;
            Sprite icon = resolver != null && potara != null && potara.IconId > 0 ? resolver.ResolveSprite((int)potara.IconId, BbAssetCache.Scale) : null;
            var specs = new List<BbPopup.ButtonSpec>();
            bool first = true;
            foreach (int r in candidates)
            {
                var partner = CharacterOf(Races[r]);
                bool ready = partner != null && partner.Level >= FusionMinLevel();      // a planet whose character is below the level stays visible but cannot be chosen
                specs.Add(new BbPopup.ButtonSpec(ready ? Titles[r] : Titles[r] + "\n(cần cấp " + FusionMinLevel() + ")", ready, ready && first));
                if (ready) first = false;
            }
            popup.Show(icon, "Bông tai Potara", "Chủ: " + Titles[main] + "\nChọn hành tinh hợp thể cùng", "Hợp thể " + Titles[main] + " (chủ) với hành tinh bạn chọn: chỉ số cộng lại, nhân vật chủ giữ kỹ năng.",
                       null, null, specs, i =>
                       {
                           popup.Hide();
                           if (i < _choices.Count) Fuse(_chooserMain, _choices[i], true);
                       });
        }

        /// <summary>The buttons of the old chooser dialog (no longer opened: the partner is chosen in a popup, OpenChooser) still call this.</summary>
        void OnChoose(int slot)
        {
            chooserRoot.SetActive(false);
            if (slot < _choices.Count) Fuse(_chooserMain, _choices[slot], true);
        }

        /// <summary>The two characters become one tab; the main one is the character whose tab was open.</summary>
        void Fuse(int main, int sub, bool announce)
        {
            var mainCharacter = CharacterOf(Races[main]);
            if (announce && mainCharacter != null) FusionFx.Play(true, mainCharacter.CharacterId);      // the character vanishes in the white blinking, the Potara effect rises
            _fusionMain = main;
            _fusionSub = sub;
            RebuildPages();
            _page = FusionPage();                                                  // the new tab of the fusion form opens
            ShowPage(_page, true);
            if (announce) Toast("Hợp thể: " + Titles[main] + " (chủ) + " + Titles[sub]);
        }

        /// <summary>The earring splits the fusion; the fusion skin the main character wore comes off and goes back to the bag (owner request 2026-10-08).</summary>
        void SplitFusion()
        {
            int main = _fusionMain;
            var ch = main >= 0 ? CharacterOf(Races[main]) : null;
            if (ch != null) FusionFx.Play(false, ch.CharacterId);                  // splitting blinks the same way, without the effect
            bool wearsFusionSkin = false;
            if (ch != null) foreach (var e in ch.Equipment) if (e.Slot == "FUSION_SKIN" && e.CostumeId != 0) wearsFusionSkin = true;
            Unfuse(true);
            if (wearsFusionSkin) UnequipSkin(main, true, true);
            Toast(wearsFusionSkin ? "Đã tách hợp thể, cải trang hợp thể về hành trang" : "Đã tách hợp thể");
        }

        void Unfuse(bool refresh)
        {
            if (_fusionMain < 0) return;
            int main = _fusionMain;
            _fusionMain = _fusionSub = -1;
            RebuildPages();
            _page = PageOfRace(main);
            if (refresh) ShowPage(_page, true);
        }

        // ---------------------------------------------------------------------------------------------------------------- input and animation

        public void Toast(string text)
        {
            toastText.text = text;
            toastText.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 1.8f;
        }

        bool _liveShown;
        float _liveAt;

        /// <summary>
        /// While a training session runs the four lines under the planet tabs are live (owner request 2026-10-10: "HP: 1000/10000"): the HP and the Ki now over their
        /// maximum, the attack, the armor and the critical rate with the buffs on (the monkey raises the HP, the attack and the critical rate). The server's frame carries them.
        /// </summary>
        void RefreshLiveStats()
        {
            if (Time.unscaledTime < _liveAt) return;
            _liveAt = Time.unscaledTime + 0.15f;
            BbTraining.Live live = new BbTraining.Live();
            bool have = false;
            ProtoCharacter ch = null;
            if (training != null && _pages.Count > 0 && _page < _pages.Count && _pages[_page].main >= 0)
            {
                ch = CharacterOf(Races[_pages[_page].main]);
                have = ch != null && training.TryLive(ch.CharacterId, out live);
                // a fused session has one fighter for the main character and its partner, carrying the sum: only the fusion tab shows it live; the tabs of the two
                // characters keep their own figures (and the other way round when the session is not a fusion)
                if (have && ch.CharacterId == training.SessionMainCharacter && training.FusedSession != _pages[_page].fusion) have = false;
                if (have && _pages[_page].fusion && !training.FusedSession) have = false;
            }
            if (have)
            {
                headerText.text = "HP: " + Money(live.Hp) + " / " + Money(live.HpMax) + "\nKI: " + Money(live.Ki) + " / " + Money(live.KiMax) + "\nSức đánh: " + Money(live.Atk) +
                                  "\nGiáp: " + Money(live.Armor) + ", Chí mạng: " + Percent(live.CritRatePct) + "%";
                _liveShown = true;
            }
            else if (_liveShown)
            {
                _liveShown = false;
                if (_pages.Count > 0) ShowPage(_page, false);                  // the session ended: the figures of the character itself again
            }
        }

        bool _outsideDown;
        Vector2 _outsideAt;
        float _outsideTime;

        /// <summary>
        /// A tap on the village outside the panel folds the panel away (owner request 2026-10-10), the same as its X. It does not count when the touch lands on something that
        /// takes touches itself (the stick, an NPC, a button), when the finger moved (a drag, a swipe) or when a popup, the shop or the functions window is open.
        /// </summary>
        void HandleOutsideTap()
        {
            if (!_panelOpen || _dockRight || popup.IsOpen || chooserRoot.activeSelf || (shop != null && shop.IsOpen) || (smith != null && smith.IsOpen) || (attendance != null && attendance.IsOpen) || (quest != null && quest.IsOpen) || (functions != null && functions.IsOpen)) { _outsideDown = false; return; }
            if (Input.GetMouseButtonDown(0))
            {
                var cam = GetComponent<Canvas>().worldCamera;
                _outsideDown = !RectTransformUtility.RectangleContainsScreenPoint(panelRect, Input.mousePosition, cam) && !OverTouchTarget(Input.mousePosition);
                _outsideAt = Input.mousePosition;
                _outsideTime = Time.unscaledTime;
            }
            else if (_outsideDown && Input.GetMouseButtonUp(0))
            {
                _outsideDown = false;
                if (((Vector2)Input.mousePosition - _outsideAt).magnitude < Screen.height * 0.03f && Time.unscaledTime - _outsideTime < 0.5f) SetPanelOpen(false, false);
            }
        }

        static readonly List<UnityEngine.EventSystems.RaycastResult> _hits = new List<UnityEngine.EventSystems.RaycastResult>();

        /// <summary>True when something under this screen point handles touches: a button or other control, or the stick.</summary>
        static bool OverTouchTarget(Vector2 screen)
        {
            if (UnityEngine.EventSystems.EventSystem.current == null) return false;
            var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = screen };
            _hits.Clear();
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(data, _hits);
            foreach (var h in _hits)
                if (h.gameObject != null && (h.gameObject.GetComponentInParent<Selectable>() != null || h.gameObject.GetComponentInParent<BbJoystick>() != null)) return true;
            return false;
        }

        void Update()
        {
            UpdateQuestGuide();
            HandleOutsideTap();
            RefreshLiveStats();
            if (_fade < 1f)
            {
                _fade = Mathf.Min(1f, _fade + Time.unscaledDeltaTime * 5f);
                bodyGroup.alpha = _fade;
            }
            if (toastText.gameObject.activeSelf && Time.unscaledTime > _toastUntil) toastText.gameObject.SetActive(false);
            if (_selectedCell != -1 && !popup.IsOpen) { _selectedCell = -1; RefreshSelection(); }
            float targetX = _panelOpen ? 0f : -PanelWidth;
            if (!Mathf.Approximately(_panelX, targetX))
            {
                _panelX = Mathf.MoveTowards(_panelX, targetX, PanelWidth * 5f * Time.unscaledDeltaTime);
                ApplyPanelX();
            }
            if (chooserRoot.activeSelf || popup.IsOpen || !_panelOpen || (functions != null && functions.BlocksSwipe)) { _pressing = false; return; }

            // swipe on the panel: a horizontal drag of at least 8% of the screen width changes the tab (touches count as the mouse)
            if (Input.GetMouseButtonDown(0))
            {
                _pressing = RectTransformUtility.RectangleContainsScreenPoint(panelRect, Input.mousePosition, GetComponent<Canvas>().worldCamera);
                _pressAt = Input.mousePosition;
            }
            if (_pressing && Input.GetMouseButtonUp(0))
            {
                _pressing = false;
                Vector2 d = (Vector2)Input.mousePosition - _pressAt;
                if (Mathf.Abs(d.x) > Screen.width * 0.08f && Mathf.Abs(d.x) > Mathf.Abs(d.y) * 1.5f) Go(d.x < 0 ? 1 : -1);
            }
        }
    }
}
