using System;
using System.Collections.Generic;
using System.Globalization;
using BattleBall.Proto;
using NroRig;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// Training with the dummies of the starter village (owner decision 2026-10-08, docs/architecture/BattleBallServer.md 15.12). A tap on a dummy focuses it and starts a
    /// training session on the server (StartTrainingRequest: the match engine without bosses, rewards or records); the server computes every hit of the three
    /// characters. The village keeps walking the characters (side view with gravity): this reports where all three stand ten times a second, in Nro pixels.
    /// On screen, as the Nro client: the target arrow above the focused dummy changes colour with its HP (Mob.imgHP, one colour per 10%), the HP bar of the
    /// focused dummy with its name at the top middle of the screen (red bar over the yellow part just lost), the skill buttons of the controlled character at the
    /// bottom right with their cooldown and the Ki bar, the damage numbers rising from the target. The session ends when the player walks away from the dummies,
    /// changes the main character or leaves the menu. Stage 2 (pictures of every skill) is not done yet: projectiles are drawn as a glow.
    /// </summary>
    public sealed class BbTraining : MonoBehaviour
    {
        [Header("Set by BattleBall/Build Login Scene")]
        public BbVillageView village;
        public BbMenuUi menu;
        public Font font;
        public Sprite whiteSprite, glowSprite;
        public Sprite arrowSheet;                 // myTexture2dmobHP.png of Nro: ten 9 x 6 px triangles, light blue (full HP) at the top to grey at the bottom
        public Sprite hpBar, hpLost;              // myTexture2dHP.png (red) and myTexture2dhpLost.png (yellow) of Nro
        public Sprite fireIdle, firePressed;      // myTexture2dfirebtn0 / 1 of Nro: the fist that switches the automatic use of the skill on and off
        public Sprite focusIdle, focusPressed;    // myTexture2dfocus / focus2 of Nro: the button that changes the target
        public Sprite bossFrame;                  // i_pve_bar_1.png of Nro: the frame of the target (boss) bar, drawn sliced
        public Sprite hudPanel, hudHp, hudHpLost, hudKi, hudKiLost;   // myTexture2dpanel, HP, hpLost, MP and mpLost of Nro: the HP/Ki frame of one character
        public Sprite skillFrame, skillFrameOn;   // myTexture2dskill.png and myTexture2dskill2.png of Nro

        enum Phase { Idle, Starting, Active }

        const float ReportEvery = 0.1f;
        const float LeaveDistance = 1600f;        // canvas px from the focused dummy: walking further away ends the session
        const float ButtonSize = 66f, ButtonGap = 6f, FistSize = 76f, FocusSize = 58f;
        const float HudWidth = 196f, HudGap = 4f;   // one Nro frame (620 x 164 at x4) per planet, side by side at the top left; 3 of them end before the target bar
        static readonly string[] HudRaces = { "EARTH", "XAYDA", "NAMEC" };
        static readonly string[] HudTitles = { "Trái Đất", "Xayda", "Namếc" };
        static readonly HashSet<uint> MeleeSkills = new HashSet<uint> { 0, 2, 4, 9, 17 };
        // skills that charge first: the server sends CAST_START with the duration, the poses start then (BbSkillPoses.Charge)
        static readonly HashSet<uint> ChargeSkills = new HashSet<uint> { 10, 11, 13, 14, 24, 25, 26 };
        const uint MonkeySkill = 13, TeleportSkill = 20;
        const int MonkeyPunchPaint = 105, MonkeyAntomicPaint = 106;       // Nro Char.setSkillPaint: the great ape plays paint 105 for its punches and 106 for Antomic

        Phase _phase;
        string _focusKey;
        MatchStarted _started;
        uint _myId;
        ulong _myCharacter;
        readonly Dictionary<uint, string> _dummyKey = new Dictionary<uint, string>();
        readonly Dictionary<string, uint> _dummyId = new Dictionary<string, uint>();
        readonly Dictionary<uint, ulong> _characterOf = new Dictionary<uint, ulong>();
        readonly Dictionary<ulong, uint> _entityOf = new Dictionary<ulong, uint>();
        readonly Dictionary<uint, EntityState> _states = new Dictionary<uint, EntityState>();
        readonly Dictionary<uint, string> _names = new Dictionary<uint, string>();
        readonly List<SkillSlot> _skills = new List<SkillSlot>();
        readonly Dictionary<long, float> _cooldownEnd = new Dictionary<long, float>();       // per character and skill (Time.unscaledTime when it is ready again)
        object _seededFrom;                       // the snapshot whose cooldowns were already taken over
        bool _auto;                               // the fist: use the chosen skill again and again, walking to the target when the skill is short-range
        uint _selectedSlot;                       // the skill "in focus": the one the player pressed last (the punch at first)
        int _pendingSlot = -1;                    // a skill waiting for the session or for the walk to the target
        float _pendingAt, _sayUntil, _lastFireAt = -100f;
        Button _autoButton;
        Image _autoImage, _autoRing;
        Text _autoLabel;
        uint _seq;
        long _previewKey;
        bool _previewDemo;                        // the screenshot tool shows the skill buttons of the village without a session
        float _reportClock, _farSince = -1f;
        readonly Dictionary<ulong, Dictionary<uint, uint>> _levels = new Dictionary<ulong, Dictionary<uint, uint>>();
        readonly Dictionary<ulong, string> _races = new Dictionary<ulong, string>();
        readonly Dictionary<string, float> _lastAction = new Dictionary<string, float>();
        bool _subscribed, _built, _demo;

        // ---- the screen parts (made at run time under this RectTransform)
        RectTransform _root, _barRoot, _skillRoot, _arrow, _hudRoot;
        Image _arrowImage, _barRed, _barLost;
        Text _barName, _barText, _status;
        readonly List<Hud> _huds = new List<Hud>();

        /// <summary>The Nro HP/Ki frame of one character: the panel with the bars laid in its slots (Nro draws the "lost" bar first, the real one over it).</summary>
        sealed class Hud
        {
            public uint entity;
            public RectTransform root;
            public Image hp, hpLost, ki, kiLost;
            public Text name, level;
            public float hpTrail = 1f, kiTrail = 1f;
        }
        float _lostRatio = 1f, _shownRatio = 1f;
        Sprite[] _arrows;
        readonly List<SkillButton> _buttons = new List<SkillButton>();
        readonly List<Floating> _floating = new List<Floating>();
        readonly Dictionary<uint, Image> _projectiles = new Dictionary<uint, Image>();
        readonly Dictionary<uint, BbDart> _darts = new Dictionary<uint, BbDart>();

        sealed class Launch
        {
            public BbDart dart;
            public Vector2 hand, stop, from;
            public float at;
            public uint tick;
        }

        readonly Dictionary<uint, Launch> _launches = new Dictionary<uint, Launch>();         // beams waiting for the speed of the projectile to be known        // the Nro picture of a projectile (the glow stays for the ones without)
        BbSkillFx _fx;
        BbUltimateFx _ultimate;

        // the pet hatched by Đẻ trứng, drawn from the server's entity (the Nro mob of the level of the skill)
        sealed class PetView
        {
            public RectTransform root;
            public BbMobView mob;
            public Vector2 at;
            public int facing = 1;
            public bool placed;
        }

        readonly Dictionary<uint, PetView> _pets = new Dictionary<uint, PetView>();

        // the white blast of Tự phát nổ (Nro: GameScr.activeSuperPower), and the great ape of Biến hình
        sealed class FlashPlan
        {
            public float at;
            public ulong who;
            public bool atTarget;             // the blast is where the dart lands (Quả cầu kênh khí, Makankosappo), not on the caster (Tự phát nổ)
        }

        // Thái dương hạ san: the whole screen flashes white and normal in turn for about 50 ticks (GameScr.isUseFreez)
        float _strobeStart = float.MinValue;

        sealed class StunPlan
        {
            public string key;
            public float until, nextAt;
        }

        readonly List<StunPlan> _stuns = new List<StunPlan>();

        readonly List<FlashPlan> _flashPlans = new List<FlashPlan>();
        Image _flash, _flashRing;
        float _flashStart = float.MinValue;
        Vector2 _flashWorld;
        readonly Dictionary<ulong, Vector2> _monkeyWindow = new Dictionary<ulong, Vector2>();       // character: from, until (Time.unscaledTime) it is the great ape

        bool IsMonkey(ulong character)
        {
            Vector2 w;
            return _monkeyWindow.TryGetValue(character, out w) && Time.unscaledTime >= w.x && Time.unscaledTime < w.y;
        }

        void AddPet(EntitySpawn e)
        {
            if (village == null || village.WorldLayer == null || e.MobTemplateId < 0 || _pets.ContainsKey(e.EntityId)) return;
            var go = new GameObject("Pet" + e.EntityId, typeof(RectTransform));
            go.transform.SetParent(village.WorldLayer, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
            var mob = go.AddComponent<BbMobView>();
            mob.uiScale = village.UiScale;
            mob.loop = true;
            mob.Show(e.MobTemplateId);
            _pets[e.EntityId] = new PetView { root = rt, mob = mob };
        }

        void UpdatePets(float dt)
        {
            if (_pets.Count == 0) return;
            float s = village.UiScale;
            List<uint> gone = null;
            foreach (var kv in _pets)
            {
                EntityState st;
                if (kv.Value.root == null || !_states.TryGetValue(kv.Key, out st) || !st.Alive)
                {
                    if (gone == null) gone = new List<uint>();
                    gone.Add(kv.Key);
                    continue;
                }
                var p = kv.Value;
                // it flies a little above the ground, bobbing (Nro: the mob of the egg hovers at the height of the head)
                Vector2 target = new Vector2(st.X * s, st.Y * s + (34f + 5f * Mathf.Sin(Time.unscaledTime * 3f + kv.Key)) * s);
                if (!p.placed) { p.at = target; p.placed = true; }
                float dx = target.x - p.at.x;
                if (Mathf.Abs(dx) > 2f) p.facing = dx > 0f ? 1 : -1;
                p.at = Vector2.Lerp(p.at, target, 1f - Mathf.Exp(-10f * dt));
                p.root.anchoredPosition = p.at;
                p.root.localScale = new Vector3(p.facing, 1f, 1f);
                p.root.SetAsLastSibling();
            }
            if (gone != null)
                foreach (uint id in gone)
                {
                    if (_pets[id].root != null) Kill(_pets[id].root.gameObject);
                    _pets.Remove(id);
                }
        }

        /// <summary>The expanding white circle that fills the screen, then fades, with the whole view shaking (Nro: GameScr.activeSuperPower).</summary>
        /// <summary>The stars Nro draws on a stunned enemy (ServerEffect 113 every 5 ticks) for as long as the stun lasts.</summary>
        void UpdateStuns()
        {
            if (_stuns.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _stuns.Count - 1; i >= 0; i--)
            {
                var s = _stuns[i];
                if (now >= s.until) { _stuns.RemoveAt(i); continue; }
                Vector2 at;
                float h;
                if (now >= s.nextAt && village.DummyInfo(s.key, out at, out h))
                {
                    EnsureFx();
                    _fx.PlayEffectAt(113, at, 1);
                    s.nextAt = now + 5f / BbSkillPoses.FramesPerSecond;
                }
            }
        }

        void UpdateFlash()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _flashPlans.Count; i++)
            {
                if (now < _flashPlans[i].at) continue;
                ulong id;
                Vector2 feet;
                int facing;
                for (int k = 0; k < village.PartyCount; k++)
                    if (village.PartyMember(k, out id, out feet, out facing) && id == _flashPlans[i].who) _flashWorld = feet + new Vector2(0f, 30f * village.UiScale);
                Vector2 aimed;
                if (_flashPlans[i].atTarget && UltimateTarget(_flashPlans[i].who, out aimed)) _flashWorld = aimed + new Vector2(0f, 26f * village.UiScale);
                _flashStart = now;
                BbSfx.Play(BbSfx.BigExplode, 0.8f);
                _flashPlans.RemoveAt(i--);
            }
            if (_flash == null) return;
            if (_strobeStart != float.MinValue)
            {
                // white on one tick out of four, then one out of three: the screen blinks (Nro paints white and nothing else on those ticks)
                int n = Mathf.FloorToInt((now - _strobeStart) * BbSkillPoses.FramesPerSecond);
                if (n > 50) _strobeStart = float.MinValue;
                else
                {
                    bool white = n < 30 ? n % 4 == 0 : n % 3 == 0;
                    _flash.enabled = white;
                    _flash.color = Color.white;
                    if (white) return;
                }
            }
            if (_flashStart == float.MinValue) { if (_strobeStart == float.MinValue) _flash.enabled = false; if (_flashRing != null) _flashRing.enabled = false; return; }
            float t = now - _flashStart;
            const float Grow = 0.7f, Fade = 0.3f;
            if (t > Grow + Fade + 0.4f)
            {
                _flashStart = float.MinValue;
                village.ShakeOffset = Vector2.zero;
                _flash.enabled = false;
                if (_flashRing != null) _flashRing.enabled = false;
                return;
            }
            village.ShakeOffset = t < Grow + 0.3f ? new Vector2(Mathf.FloorToInt(t * 30f) % 3 != 0 ? -3f : 3f, 0f) * village.UiScale : Vector2.zero;
            if (t < Grow)
            {
                float k = t / Grow;
                float radius = 1700f * k * k;                                                   // grows faster and faster, like the Nro circle
                if (_flashRing == null)
                {
                    var go = new GameObject("FlashRing", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(village.WorldLayer, false);
                    _flashRing = go.GetComponent<Image>();
                    _flashRing.sprite = glowSprite;
                    _flashRing.raycastTarget = false;
                    _flashRing.canvasRenderer.cullTransparentMesh = false;
                    var rt0 = _flashRing.rectTransform;
                    rt0.anchorMin = rt0.anchorMax = Vector2.zero;
                }
                _flashRing.enabled = true;
                _flashRing.color = Color.white;
                _flashRing.rectTransform.sizeDelta = new Vector2(radius * 2.6f, radius * 2.6f);
                _flashRing.rectTransform.anchoredPosition = _flashWorld;
                _flashRing.transform.SetAsLastSibling();
                _flash.enabled = false;
            }
            else
            {
                if (_flashRing != null) _flashRing.enabled = false;
                _flash.enabled = true;
                float alpha = t < Grow + 0.05f ? 1f : Mathf.Clamp01(1f - (t - Grow - 0.05f) / Fade);
                _flash.color = new Color(1f, 1f, 1f, alpha);
            }
        }

        sealed class LookChange
        {
            public ulong character;
            public int costume;               // 0 = the own costume again
            public float at;
            public bool replay;               // the paint of Biến hình plays again (the server sends the effect when the body changes and when it changes back)
        }

        readonly List<LookChange> _looks = new List<LookChange>();

        void UpdateLooks()
        {
            for (int i = 0; i < _looks.Count; i++)
            {
                var c = _looks[i];
                if (Time.unscaledTime < c.at) continue;
                village.SetLook(c.character, c.costume);
                if (c.replay)
                {
                    var again = BbSkillPoses.UsePaint(BbSkillPoses.PaintOf(MonkeySkill, LevelOf(c.character, MonkeySkill)), false);
                    PlayPicked(c.character, MonkeySkill, again.poses, again.paint, again.fly, 0, float.NaN);
                }
                _looks.RemoveAt(i--);
            }
        }
        readonly Dictionary<ulong, int> _lastPaint = new Dictionary<ulong, int>();       // the paint (the combo hit that was picked) each character played last
        readonly HashSet<uint> _seenProjectiles = new HashSet<uint>();

        sealed class SkillButton
        {
            public SkillSlot slot;
            public RectTransform root;
            public Image frame, icon, shade;
            public Text level, cooldown;
        }

        sealed class Floating
        {
            public Text text;
            public Vector2 start;
            public float age, life;
        }

        public bool Active { get { return _phase == Phase.Active; } }
        public string FocusKey { get { return _focusKey; } }

        void Start()
        {
            if (village != null) village.DummyTapped = Focus;
            Subscribe();
        }

        void Subscribe()
        {
            if (_subscribed || !BbSession.IsActive) return;
            BbSession.Client.EventReceived += OnEvent;
            _subscribed = true;
        }

        void OnDestroy()
        {
            if (_subscribed && BbSession.Client != null) BbSession.Client.EventReceived -= OnEvent;
            if (_phase == Phase.Active) SendLeave();
        }

        // ---------------------------------------------------------------------------------------------------------------- focus and session

        /// <summary>A tap on a training dummy: it becomes the target (the AI walk to it) and the session starts when it is not running.</summary>
        public void Focus(string key)
        {
            SetFocus(key, true);
        }

        void SetFocus(string key, bool start)
        {
            EnsureBuilt();
            _focusKey = key;
            if (village != null) village.CombatTarget = key;
            if (start && _phase == Phase.Idle && !_demo) StartSession();
            Refresh();
        }

        /// <summary>The nearest target in view becomes the focus (nothing in view: false).</summary>
        bool AutoFocusNearest(bool start)
        {
            ulong id;
            Vector2 me, at;
            int facing;
            float h;
            if (village == null || !village.PartyMember(0, out id, out me, out facing)) return false;
            string best = null;
            float bestDistance = float.MaxValue;
            foreach (string key in village.VisibleDummyKeys())
            {
                if (!village.DummyInfo(key, out at, out h)) continue;
                float d = Mathf.Abs(at.x - me.x);
                if (d < bestDistance) { bestDistance = d; best = key; }
            }
            if (best == null) return false;
            SetFocus(best, start);
            return true;
        }

        /// <summary>The focus button: the next target in view, from left to right (the nearest when none is focused).</summary>
        public void CycleFocus()
        {
            var keys = village != null ? village.VisibleDummyKeys() : new List<string>();
            if (keys.Count == 0) { Toast("Không có mục tiêu trong tầm nhìn"); return; }
            int at = _focusKey == null ? -1 : keys.IndexOf(_focusKey);
            if (at < 0) { AutoFocusNearest(true); return; }
            SetFocus(keys[(at + 1) % keys.Count], true);
        }

        void Toast(string text)
        {
            Say(text);
            _sayUntil = Time.unscaledTime + 1.8f;
        }

        async void StartSession()
        {
            if (!BbSession.IsActive || village == null) { Say("Cần đăng nhập để luyện tập"); return; }
            Subscribe();
            ulong main, partner = 0;
            Vector2 feet;
            int facing;
            if (!village.PartyMember(0, out main, out feet, out facing) || main == 0) return;
            if (menu != null && menu.FusionMainCharacterId == main) partner = menu.FusionSubCharacterId;
            float s = village.UiScale;
            var req = new StartTrainingRequest { ControlledCharacterId = main, FusionPartnerId = partner, ArenaW = village.MapWidth / s, ArenaH = village.MapHeight / s };
            foreach (string key in village.DummyKeys)
            {
                Vector2 at;
                float h;
                if (village.DummyInfo(key, out at, out h)) req.Spots.Add(new TrainingSpot { Key = key, X = at.x / s, Y = at.y / s });
            }
            _phase = Phase.Starting;
            _sessionPartner = partner;
            if (!_quiet) Say("Đang vào luyện tập...");
            try
            {
                var started = await BbSession.Client.StartTrainingAsync(req);
                if (_phase != Phase.Starting) return;                    // left again meanwhile
                Begin(started, main);
                Say("");
                _quiet = false;
            }
            catch (Exception e)
            {
                _phase = Phase.Idle;
                Say("");
                _leftAt = Time.unscaledTime + 8f;                         // do not try again at once
                if (menu != null && !_quiet) menu.Toast("Không vào được luyện tập: " + e.Message);
                _quiet = false;
            }
        }

        void Begin(MatchStarted started, ulong mainCharacter)
        {
            _started = started;
            _fusedSession = _sessionPartner != 0;
            _myCharacter = mainCharacter;
            _dummyKey.Clear();
            _dummyId.Clear();
            _characterOf.Clear();
            _entityOf.Clear();
            _states.Clear();
            _names.Clear();
            foreach (var e in started.Entities) AddSpawn(e);
            _levels.Clear();
            _races.Clear();
            var snap = BbSession.Snapshot;
            if (snap != null)
                foreach (var c in snap.Characters)
                {
                    var map = new Dictionary<uint, uint>();
                    foreach (var sk in c.Skills) map[sk.SkillId] = sk.Level;
                    _levels[c.CharacterId] = map;
                    _races[c.CharacterId] = c.Race;
                }
            var mine = new Dictionary<uint, uint>();
            foreach (var sl in started.Skills) mine[sl.SkillId] = sl.Level;
            _levels[mainCharacter] = mine;
            _skills.Clear();
            var snapNow = SnapshotNow();
            BattleBall.Proto.CharacterInfo mainInfo = null;
            if (snapNow != null) PlayerCharacterRef(snapNow, mainCharacter, out mainInfo);
            _skills.AddRange(SlotsOf(snapNow, mainInfo, started.Skills));
            SeedCooldowns(SnapshotNow());
            foreach (var sl in started.Skills) SetCooldown(mainCharacter, sl.SkillId, Time.unscaledTime + sl.CooldownLeftMs / 1000f);       // what the server says is left
            _phase = Phase.Active;
            _farSince = -1f;
            _previewKey = 0;
            BuildSkillButtons();
            BuildHuds();
            Refresh();
        }

        void AddSpawn(EntitySpawn e)
        {
            _names[e.EntityId] = e.Name;
            _states[e.EntityId] = new EntityState { EntityId = e.EntityId, X = e.X, Y = e.Y, Hp = e.Hp, HpMax = e.HpMax, Ki = e.Ki, KiMax = e.KiMax, Alive = true };
            if (e.Kind == EntityKind.TrainingDummy)
            {
                _dummyKey[e.EntityId] = e.DummyKey;
                _dummyId[e.DummyKey] = e.EntityId;
            }
            else if (e.Kind == EntityKind.Pet)
            {
                AddPet(e);
            }
            else if (e.CharacterId != 0)
            {
                _characterOf[e.EntityId] = e.CharacterId;
                _entityOf[e.CharacterId] = e.EntityId;
                if (e.Controlled) _myId = e.EntityId;
            }
        }

        /// <summary>Ends the session (walked away, another main character, the menu closed).</summary>
        public void Leave()
        {
            if (_phase == Phase.Active) SendLeave();
            Stop();
        }

        float _leftAt = -100f, _noTargetSince = -1f;
        bool _quiet;

        /// <summary>
        /// Owner request 2026-10-09: the AI characters fight what is in view of the player without any help from him. A training session starts by itself when a dummy
        /// is in view (and again two seconds after one ended), and it ends when nothing has been in view for five seconds.
        /// </summary>
        void AutoSession()
        {
            if (_demo || village == null || _phase != Phase.Idle || !BbSession.IsActive || Time.unscaledTime - _leftAt < 2f) return;
            if (village.VisibleDummyKeys().Count == 0) return;
            _quiet = true;
            StartSession();
        }

        void Stop()
        {
            _leftAt = Time.unscaledTime;
            _looks.Clear();
            _monkeyWindow.Clear();
            _flashPlans.Clear();
            _stuns.Clear();
            _strobeStart = float.MinValue;
            foreach (var p in _pets.Values) if (p.root != null) Kill(p.root.gameObject);
            _pets.Clear();
            if (village != null)
            {
                village.ShakeOffset = Vector2.zero;
                foreach (var m in _characterOf.Values) { village.SetLook(m, 0); village.SetLift(m, 0f); village.SetLocked(m, false); village.StopAction(m); }
                foreach (string key in village.DummyKeys) village.SetDummyDraw(key, true, null);
                if (_ultimate != null) _ultimate.CancelAll();
            }
            _flashStart = float.MinValue;
            _phase = Phase.Idle;
            _focusKey = null;
            if (village != null) village.CombatTarget = null;
            foreach (var img in _projectiles.Values) if (img != null) Kill(img.gameObject);
            _projectiles.Clear();
            foreach (var d in _darts.Values) if (d != null) Kill(d.gameObject);
            _darts.Clear();
            _launches.Clear();
            foreach (var fl in _floating) if (fl.text != null) Kill(fl.text.gameObject);
            _floating.Clear();
            _seenProjectiles.Clear();
            Refresh();
        }

        void SendLeave()
        {
            try { if (BbSession.IsActive) BbSession.Client.Send(new Envelope { LeaveMatchRequest = new LeaveMatchRequest() }); } catch (Exception) { }
        }

        // ---------------------------------------------------------------------------------------------------------------- input

        /// <summary>A press on a skill button: it becomes the skill in focus and is used on the target (the nearest in view when none is chosen).</summary>
        void PressSkill(SkillSlot slot)
        {
            if (slot.Level == 0) { Toast("Chưa học kỹ năng này (học ở tab Kỹ năng)"); return; }
            _selectedSlot = slot.Slot;
            if (_phase == Phase.Starting) { _pendingSlot = (int)slot.Slot; _pendingAt = Time.unscaledTime; return; }
            Engage(slot, true);
        }

        void ToggleAuto()
        {
            _auto = !_auto;
            if (!_auto) { _pendingSlot = -1; if (village != null) village.CancelApproach(); }
            Toast(_auto ? "Tự động dùng kỹ năng: BẬT" : "Tự động dùng kỹ năng: TẮT");
        }

        SkillSlot SlotOf(uint slot)
        {
            foreach (var sk in _skills) if (sk.Slot == slot) return sk;
            return null;
        }

        bool SkillReady(SkillSlot slot, float margin)
        {
            EntityState mine;
            bool kiOk = _phase != Phase.Active || !_states.TryGetValue(_myId, out mine) || (slot.KiAll ? mine.Ki > 0 : mine.Ki >= slot.KiCost);
            return kiOk && Time.unscaledTime >= CooldownEnd(slot.SkillId) + margin;
        }

        /// <summary>How close to the target (canvas px) the skill is worth using: the server's figure per skill and level; 0 = no walking needed.</summary>
        float ReachOf(SkillSlot slot)
        {
            var snap = SnapshotNow();
            if (snap == null || village == null) return 0f;
            foreach (var e in snap.SkillCatalog)
            {
                if (e.SkillId != slot.SkillId) continue;
                foreach (var l in e.Levels) if (l.Level == Math.Max(1u, slot.Level)) return l.RangePx * village.UiScale;
            }
            return 0f;
        }

        PlayerDataSnapshot SnapshotNow()
        {
            return menu != null && menu.Snapshot != null ? menu.Snapshot : BbSession.Snapshot;
        }

        /// <summary>
        /// Uses a skill on the focused target (the nearest in view is focused first): starts the session when there is none, walks the character to the target when
        /// the skill is short-range, and fires it when it is ready (the cooldown of the real server clock). True when the skill was sent.
        /// </summary>
        bool Engage(SkillSlot slot, bool manual)
        {
            if (village == null || _demo) return false;
            if (village.IsLocked(_myCharacter)) return false;                    // busy or under an effect: the server would refuse, the poses must not start
            Vector2 foe = Vector2.zero, me;
            float h;
            int facing;
            ulong id;
            float reach = ReachOf(slot);
            bool needsTarget = reach > 0f;                                      // shield, healing, buffs and the like are used with no target at all
            bool hasFoe = _focusKey != null && village.DummyInfo(_focusKey, out foe, out h);
            if (!hasFoe && needsTarget)
            {
                if (!AutoFocusNearest(false)) { if (manual) Toast("Không có mục tiêu trong tầm nhìn"); return false; }
                hasFoe = village.DummyInfo(_focusKey, out foe, out h);
            }
            village.PartyMember(0, out id, out me, out facing);
            float dist = hasFoe ? Mathf.Abs(foe.x - me.x) : 0f;
            if (_phase != Phase.Active)
            {
                if (_phase == Phase.Idle)
                {
                    if (needsTarget && dist > LeaveDistance * 0.9f)
                    {
                        if (!village.StickHeld) village.ApproachTo(foe.x, Mathf.Max(reach, 300f) * 0.8f);     // too far for a session: walk there first
                        return false;
                    }
                    StartSession();
                }
                _pendingSlot = (int)slot.Slot;
                _pendingAt = Time.unscaledTime;
                return false;
            }
            if (needsTarget && hasFoe && dist > reach)
            {
                if (!village.StickHeld) village.ApproachTo(foe.x, Mathf.Max(60f, reach * 0.8f));
                if (manual) { _pendingSlot = (int)slot.Slot; _pendingAt = Time.unscaledTime; }
                return false;
            }
            if (!SkillReady(slot, manual ? 0f : 0.12f)) return false;
            Fire(slot, hasFoe, foe);
            return true;
        }

        void Fire(SkillSlot slot, bool hasFoe, Vector2 foe)
        {
            uint target = hasFoe && _dummyId.ContainsKey(_focusKey) ? _dummyId[_focusKey] : 0;
            Vector2 me;
            int facing;
            ulong id;
            village.PartyMember(0, out id, out me, out facing);
            float dir = hasFoe ? Mathf.Sign(foe.x - me.x) : facing;
            if (dir == 0f) dir = facing;
            _lastFireAt = Time.unscaledTime;
            BbSession.Client.Send(new Envelope { UseSkillRequest = new UseSkillRequest { Seq = ++_seq, Slot = slot.Slot, TargetId = target, DirX = dir, DirY = 0f } });
            Debug.Log("[SkillFx] fire slot " + slot.Slot + " skill " + slot.SkillId + " level " + slot.Level + " target " + target);
            // what the player sees at once (the server decides the hit): the pose and the cooldown
            SetCooldown(_myCharacter, slot.SkillId, Time.unscaledTime + slot.CooldownMs / 1000f);
            if (!ChargeSkills.Contains(slot.SkillId)) ShowSkill(_myCharacter, slot.SkillId, hasFoe ? foe.x : float.NaN, true);
        }

        /// <summary>Every frame: a skill waiting for its session or its walk, and the automatic use of the skill in focus.</summary>
        void DriveCombat()
        {
            if (_demo || village == null || _buttons.Count == 0) return;
            var pending = _pendingSlot >= 0 ? SlotOf((uint)_pendingSlot) : null;
            if (pending != null)
            {
                if (Time.unscaledTime - _pendingAt > 8f) _pendingSlot = -1;
                else if (_phase == Phase.Active && Engage(pending, false)) _pendingSlot = -1;
                return;
            }
            _pendingSlot = -1;
            if (!_auto || _phase == Phase.Starting) return;
            var slot = SlotOf(_selectedSlot);
            if (slot == null || slot.Level == 0) slot = _skills.Find(x => x.Level > 0);
            if (slot == null) return;
            if (_phase == Phase.Idle && !SkillReady(slot, 0.12f)) return;                // not worth starting a session for a skill that is cooling down
            Engage(slot, false);
        }

        uint LevelOf(ulong character, uint skillId)
        {
            Dictionary<uint, uint> map;
            uint lv;
            return _levels.TryGetValue(character, out map) && map.TryGetValue(skillId, out lv) && lv > 0 ? lv : 1u;
        }

        /// <summary>The poses of a skill on a character of the party (the Nro skill paint at the character's level; Biến hình and Dịch chuyển their own).</summary>
        void ShowSkill(ulong character, uint skillId, float faceX, bool force)
        {
            string key = character + ":" + skillId;
            float last;
            if (!force && _lastAction.TryGetValue(key, out last) && Time.unscaledTime - last < 0.4f) return;     // one skill reported for several targets
            _lastAction[key] = Time.unscaledTime;
            bool flying = village.Airborne(character);
            uint level = LevelOf(character, skillId);
            if (skillId == TeleportSkill)
            {
                var jump = (int)NroPose.Dash_Teleport;
                PlayPicked(character, skillId, new[] { jump, jump, jump, jump, jump, jump }, -1, false, -1, faceX);
                Teleport(character);
                return;
            }
            var picked = BbSkillPoses.UsePicked(skillId, level, skillId == MonkeySkill ? false : flying);
            if (IsMonkey(character) && (skillId == 4 || skillId == 5)) picked = BbSkillPoses.UsePaint(skillId == 5 ? MonkeyAntomicPaint : MonkeyPunchPaint, flying);
            int[] poses = skillId == MonkeySkill ? BbSkillPoses.Monkey(level) : picked.poses;
            PlayPicked(character, skillId, poses, picked.paint, picked.fly, 0, faceX);
        }

        /// <summary>The sound of a skill when it starts (Nro: SoundMn); the ultimates and the hits have their own sounds.</summary>
        static void SkillSound(uint skillId)
        {
            switch (skillId)
            {
                case 1: BbSfx.Play(BbSfx.EarthKame, 0.9f); break;
                case 3: BbSfx.Play(BbSfx.NamekKame, 0.6f); break;
                case 5: BbSfx.Play(BbSfx.XaydaKame, 0.6f); break;
                case 6: BbSfx.Play(BbSfx.SolarFlare, 0.5f); break;
                case 7: BbSfx.Play(BbSfx.Revive, 0.5f); break;
                case 8:
                case 12: BbSfx.Play(BbSfx.HpUp, 0.5f); break;
                case 10:
                case 11:
                case 13:
                case 14: BbSfx.Play(BbSfx.Gong, 0.4f); break;
            }
        }

        static readonly int[] HitSounds = { BbSfx.LowPunch, BbSfx.LowKick, BbSfx.MediumPunch, BbSfx.MediumKick };

        void EnsureUltimate()
        {
            if (_ultimate != null) return;
            _ultimate = village.GetComponent<BbUltimateFx>();
            if (_ultimate == null) _ultimate = village.gameObject.AddComponent<BbUltimateFx>();
            _ultimate.village = village;
        }

        /// <summary>The enemy an ultimate of this character is aimed at: the focused one, else the nearest in view.</summary>
        bool UltimateTarget(ulong character, out Vector2 feet)
        {
            float h;
            feet = Vector2.zero;
            if (_focusKey != null && village.DummyInfo(_focusKey, out feet, out h)) return true;
            ulong id;
            Vector2 me;
            int facing;
            if (!village.PartyMember(0, out id, out me, out facing)) return false;
            float best = float.MaxValue;
            bool found = false;
            foreach (string key in village.VisibleDummyKeys())
            {
                Vector2 at;
                if (!village.DummyInfo(key, out at, out h)) continue;
                float d = Mathf.Abs(at.x - me.x);
                if (d < best) { best = d; feet = at; found = true; }
            }
            return found;
        }

        void EnsureFx()
        {
            if (_fx != null) return;
            _fx = village.GetComponent<BbSkillFx>();
            if (_fx == null) _fx = village.gameObject.AddComponent<BbSkillFx>();
            _fx.village = village;
        }

        /// <summary>The poses of an action with the effects of its paint (the painted frames start at pose {@code fxFrom}).</summary>
        /// <summary>Dịch chuyển tức thời: the smoke of Nro (effect 60) where the character was and where it lands, next to the target (the main character moves there).</summary>
        void Teleport(ulong character)
        {
            ulong id;
            Vector2 feet;
            int facing;
            if (!village.PartyMember(0, out id, out feet, out facing)) return;
            EnsureFx();
            for (int i = 0; i < village.PartyCount; i++)
                if (village.PartyMember(i, out id, out feet, out facing) && id == character) { _fx.PlayHit(60, feet); break; }
            Vector2 aim;
            if (character != _myCharacter || !UltimateTarget(character, out aim)) return;
            village.PartyMember(0, out id, out feet, out facing);
            float side = aim.x >= feet.x ? -1f : 1f;
            village.TeleportMain(aim.x + side * 90f);
            village.PartyMember(0, out id, out feet, out facing);
            _fx.PlayHit(60, feet);
        }

        /// <summary>
        /// Where a beam ends: the dummy it was shot at (the focused one for the controlled character, the nearest in its way for the others), seen from the
        /// server's projectile; false when nothing lies in its way.
        /// </summary>
        bool DartStop(ProjectileState p, ulong owner, out Vector2 stop)
        {
            stop = Vector2.zero;
            Vector2 dir = new Vector2(p.DirX, p.DirY);
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();
            float s = village.UiScale;
            EntityState chosen = null;
            uint focusId;
            EntityState focused;
            if (owner == _myCharacter && _focusKey != null && _dummyId.TryGetValue(_focusKey, out focusId) && _states.TryGetValue(focusId, out focused)
                && Vector2.Dot(new Vector2(focused.X - p.X, focused.Y - p.Y), dir) > -10f)
                chosen = focused;
            if (chosen == null)
            {
                float best = float.MaxValue;
                foreach (var kv in _dummyKey)
                {
                    EntityState st;
                    if (!_states.TryGetValue(kv.Key, out st)) continue;
                    Vector2 d = new Vector2(st.X - p.X, st.Y - p.Y);
                    float along = Vector2.Dot(d, dir), perp = Mathf.Abs(d.x * dir.y - d.y * dir.x);
                    if (along < -10f || perp > 80f) continue;
                    if (along < best) { best = along; chosen = st; }
                }
            }
            if (chosen == null) return false;
            stop = new Vector2(chosen.X * s, chosen.Y * s + 26f * s);
            return true;
        }

        static readonly HashSet<uint> ServerProjectileSkills = new HashSet<uint> { 1, 3, 5 };      // the skills whose dart is the projectile of the server
        readonly Dictionary<ulong, float> _arrowAt = new Dictionary<ulong, float>();                    // when each character's last paint throws its dart
        readonly Dictionary<ulong, Vector2> _arrowHand = new Dictionary<ulong, Vector2>();              // and where its hand is (Nro px: dx, up)

        void PlayPicked(ulong character, uint skillId, int[] poses, int paint, bool fly, int fxFrom, float faceX)
        {
            float seconds = BbSkillPoses.Seconds(poses);
            SkillSound(skillId);
            Debug.Log("[SkillFx] character " + character + " skill " + skillId + " paint " + paint + " poses " + poses.Length + " fxFrom " + fxFrom);
            if (poses.Length > 0) village.PlayAction(character, poses, seconds, faceX);
            if (paint < 0) return;
            _lastPaint[character] = paint;
            EnsureFx();
            int count = poses.Length;
            if (count == 0)
            {
                var frames = BbSkillFxData.Frames(paint, fly);          // a paint of one standing pose still has its effects
                count = frames == null ? 0 : frames.Length / 12;
                seconds = count / BbSkillPoses.FramesPerSecond;
                fxFrom = 0;
            }
            if (fxFrom >= 0 && count > 0) _fx.Play(character, paint, fly, fxFrom, count, seconds);
            var arrow = BbSkillFxData.ArrowOf(paint, fly);
            if (arrow.frame < 0) return;
            float at = Time.unscaledTime + (Mathf.Max(0, fxFrom) + arrow.frame) / BbSkillPoses.FramesPerSecond;
            _arrowAt[character] = at;
            _arrowHand[character] = new Vector2(arrow.dx, arrow.up);
            if (ServerProjectileSkills.Contains(skillId)) return;
            // a skill that throws something without a projectile of the server (Quả cầu kênh khí, Trói, Thôi miên, Biến Sôcôla): the dart flies by itself to the target
            Vector2 aim, hand;
            int dir;
            bool haveAim = UltimateTarget(character, out aim);
            bool haveHand = HandOf(character, out hand, out dir);
            Debug.Log("[SkillFx] dart " + arrow.dart + " for skill " + skillId + ": target " + haveAim + ", hand " + haveHand);
            if (!haveAim || !haveHand) return;
            var dart = BbDart.Create(village.WorldLayer, arrow.dart, village.UiScale);
            if (dart == null) return;
            _darts[1000000u + (uint)(_flying++ % 100000)] = dart;
            dart.Fly(hand, aim + new Vector2(0f, 20f * village.UiScale), Mathf.Max(0f, at - Time.unscaledTime), 480f);
        }

        int _flying;

        /// <summary>Where the hand of a character is when it throws its dart (canvas px) and which way it faces.</summary>
        bool HandOf(ulong character, out Vector2 hand, out int dir)
        {
            hand = Vector2.zero;
            dir = 1;
            Vector2 offset;
            if (!_arrowHand.TryGetValue(character, out offset)) return false;
            for (int i = 0; i < village.PartyCount; i++)
            {
                ulong id;
                Vector2 feet;
                int facing;
                if (village.PartyMember(i, out id, out feet, out facing) && id == character)
                {
                    dir = facing >= 0 ? 1 : -1;
                    hand = feet + new Vector2(dir * offset.x * village.UiScale, offset.y * village.UiScale);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The skill buttons outside a session (the village): the skills of the main character's race and the common ones, at the level it learned (level 1 when it
        /// has not learned it: what the training allows), in the slots of the catalog. Rebuilt when the main character or a skill level changes.
        /// </summary>
        void EnsurePreviewSkills()
        {
            ulong main = 0;
            Vector2 feet;
            int facing;
            var snap = SnapshotNow();
            SeedCooldowns(snap);
            if (village == null || snap == null || !village.PartyMember(0, out main, out feet, out facing) || main == 0)
            {
                if (_previewKey != 0 && _phase == Phase.Idle) { _previewKey = 0; ClearButtons(); }
                return;
            }
            PlayerCharacterRef(snap, main, out var ch);
            if (ch == null) return;
            long key = (long)main * 1000003L + snap.SkillCatalog.Count;
            foreach (var sk in ch.Skills) key = key * 31 + sk.SkillId * 16 + sk.Level;
            if (key == _previewKey && _buttons.Count > 0) return;
            _previewKey = key;
            var learned = new Dictionary<uint, uint>();
            foreach (var sk in ch.Skills) learned[sk.SkillId] = sk.Level;
            _levels[main] = learned;
            _races[main] = ch.Race;
            _skills.Clear();
            _skills.AddRange(SlotsOf(snap, ch, null));
            BuildSkillButtons();
        }

        /// <summary>
        /// The buttons of a character: every skill of its race and the common ones in the slots of the catalog. A skill the character has not learned stays on the bar
        /// but locked (level 0): it cannot be used (owner request 2026-10-09). {@code live} holds the slots the server sent for the running session.
        /// </summary>
        static List<SkillSlot> SlotsOf(PlayerDataSnapshot snap, BattleBall.Proto.CharacterInfo ch, IList<SkillSlot> live)
        {
            var list = new List<SkillSlot>();
            if (snap == null || ch == null)
            {
                if (live != null) list.AddRange(live);
            }
            else
            {
                var learned = new Dictionary<uint, uint>();
                foreach (var sk in ch.Skills) learned[sk.SkillId] = sk.Level;
                foreach (var entry in snap.SkillCatalog)
                {
                    if (entry.Race != ch.Race && entry.Race != "COMMON") continue;
                    SkillSlot slot = null;
                    if (live != null) foreach (var l in live) if (l.SkillId == entry.SkillId) slot = l;
                    if (slot == null)
                    {
                        uint level;
                        learned.TryGetValue(entry.SkillId, out level);
                        uint cooldown = 0;
                        foreach (var l in entry.Levels) if (l.Level == Math.Max(1u, level)) cooldown = l.CooldownMs;
                        slot = new SkillSlot { Slot = entry.Slot, SkillId = entry.SkillId, Level = level, CooldownMs = cooldown, IconId = entry.IconId, Name = entry.Name };
                    }
                    list.Add(slot);
                }
            }
            list.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            return list;
        }

        static void PlayerCharacterRef(PlayerDataSnapshot snap, ulong id, out BattleBall.Proto.CharacterInfo found)
        {
            found = null;
            foreach (var c in snap.Characters) if (c.CharacterId == id) { found = c; return; }
        }

        void ClearButtons()
        {
            foreach (var b in _buttons) Kill(b.root.gameObject);
            _buttons.Clear();
        }

        float CooldownEnd(uint skillId)
        {
            return CooldownEndOf(_myCharacter, skillId);
        }

        float CooldownEndOf(ulong character, uint skillId)
        {
            float t;
            return _cooldownEnd.TryGetValue(((long)character << 8) | skillId, out t) ? t : 0f;
        }

        void SetCooldown(ulong character, uint skillId, float end)
        {
            _cooldownEnd[((long)character << 8) | skillId] = end;
        }

        /// <summary>The cooldowns the server kept (they run on its real clock, also while the player is logged out), taken over once per snapshot.</summary>
        void SeedCooldowns(PlayerDataSnapshot snap)
        {
            if (snap == null || ReferenceEquals(_seededFrom, snap)) return;
            _seededFrom = snap;
            float now = Time.unscaledTime;
            foreach (var ch in snap.Characters)
                foreach (var cd in ch.Cooldowns)
                    if (now + cd.LeftMs / 1000f > CooldownEndOf(ch.CharacterId, cd.SkillId)) SetCooldown(ch.CharacterId, cd.SkillId, now + cd.LeftMs / 1000f);
        }


        // ---------------------------------------------------------------------------------------------------------------- the server

        void OnEvent(Envelope e)
        {
            if (_phase != Phase.Active) return;
            if (e.PayloadCase == Envelope.PayloadOneofCase.MatchFrame) OnFrame(e.MatchFrame);
            else if (e.PayloadCase == Envelope.PayloadOneofCase.MatchEnded) { Stop(); if (menu != null) menu.Toast("Đã rời luyện tập"); }
        }

        void OnFrame(MatchFrame f)
        {
            foreach (var sp in f.Spawns) AddSpawn(sp);
            foreach (var st in f.States) _states[st.EntityId] = st;
            foreach (var d in f.Damages) OnDamage(d);
            foreach (var ef in f.Effects) OnEffect(ef);
            DrawProjectiles(f);
            ApplyLocks();
        }

        /// <summary>
        /// What the server holds, the client holds: a character that is busy (gong, charge, the beam of an ultimate), stunned, asleep, tied, chocolate or in the jar takes no
        /// input and does not walk (Nro: Char.isLockMove and the effects on it); the AI also stand while they regenerate. The effect mask of the entity says it.
        /// </summary>
        void ApplyLocks()
        {
            if (village == null) return;
            for (int i = 0; i < village.PartyCount; i++)
            {
                ulong id;
                Vector2 feet;
                int facing;
                if (!village.PartyMember(i, out id, out feet, out facing)) continue;
                uint entity;
                EntityState st;
                bool locked = false;
                if (_phase == Phase.Active && _entityOf.TryGetValue(id, out entity) && _states.TryGetValue(entity, out st) && st.Alive)
                {
                    uint m = st.Effects;
                    locked = (m & (1u | 2u | 4u | 16u | 256u | 512u | 1024u)) != 0 || (i != 0 && (m & 32u) != 0);
                }
                bool was = village.IsLocked(id);
                village.SetLocked(id, locked);
                if (locked && !was && i == 0) village.CancelApproach();
            }
        }

        /// <summary>The live numbers of a character of the party (the menu shows them: HP 1.000 / 10.000, the attack and the critical rate with the buffs).</summary>
        public struct Live
        {
            public ulong Hp, HpMax, Ki, KiMax, Atk, Armor;
            public double CritRatePct;
        }

        /// <summary>The running session is a fusion: its controlled fighter carries the summed numbers of the main character and its partner (the partner is not a fighter).</summary>
        public bool FusedSession { get { return _phase == Phase.Active && _fusedSession; } }
        bool _fusedSession;
        /// <summary>The character the running session is controlled by (0 outside a session).</summary>
        public ulong SessionMainCharacter { get { return _phase == Phase.Active ? _myCharacter : 0; } }
        ulong _sessionPartner;

        public bool TryLive(ulong character, out Live live)
        {
            live = new Live();
            uint entity;
            EntityState st;
            if (_phase != Phase.Active || !_entityOf.TryGetValue(character, out entity) || !_states.TryGetValue(entity, out st)) return false;
            live.Hp = st.Hp; live.HpMax = st.HpMax; live.Ki = st.Ki; live.KiMax = st.KiMax; live.Atk = st.Atk; live.Armor = st.Armor; live.CritRatePct = st.CritRatePct;
            return st.HpMax > 0;
        }

        void OnDamage(DamageEvent d)
        {
            string key;
            Vector2 at = Vector2.zero;
            float h = 0f;
            bool onDummy = _dummyKey.TryGetValue(d.TargetId, out key) && village.DummyInfo(key, out at, out h);
            if (onDummy)
            {
                if (!d.Absorbed) village.HurtDummy(key);
                bool mine = d.SourceId == _myId;
                string text = d.Absorbed ? "Hụt" : "-" + Money(d.Amount);
                Color c = d.Absorbed ? new Color(0.85f, 0.85f, 0.85f) : d.Crit ? new Color(1f, 0.92f, 0.2f) : mine ? new Color(1f, 0.35f, 0.25f) : new Color(1f, 0.7f, 0.55f);
                Float(text, at + new Vector2(UnityEngine.Random.Range(-18f, 18f), h + 10f), c, d.Crit ? 30 : mine ? 24 : 20, d.Crit ? 1.1f : 0.9f);
            }
            if (onDummy && !d.Absorbed)
            {
                if (MeleeSkills.Contains(d.SkillId)) BbSfx.Play(HitSounds[UnityEngine.Random.Range(0, HitSounds.Length)], 0.5f);
                else if (d.SkillId == 1 || d.SkillId == 3 || d.SkillId == 5 || d.SkillId == 11) BbSfx.Play(BbSfx.Explode1, 0.5f);
                // the effect of the skill landing on the target (Nro: effectHappenOnMob of the paint that was played)
                ulong hitter;
                int paint = -1;
                if (_characterOf.TryGetValue(d.SourceId, out hitter) && !_lastPaint.TryGetValue(hitter, out paint)) paint = -1;
                if (hitter != 0)
                {
                    int fromSkill = IsMonkey(hitter) && d.SkillId == 5 ? MonkeyAntomicPaint : BbSkillPoses.PaintOf(d.SkillId, LevelOf(hitter, d.SkillId));
                    if (fromSkill >= 0)
                    {
                        EnsureFx();
                        _fx.PlayHit(BbSkillFxData.Happen(MeleeSkills.Contains(d.SkillId) && paint >= 0 ? paint : fromSkill), at);
                    }
                }
            }
            // the AI characters show their attack when it lands (their beams were shown when the beam left)
            ulong who;
            if (d.SourceId != _myId && _characterOf.TryGetValue(d.SourceId, out who) && MeleeSkills.Contains(d.SkillId))
                ShowSkill(who, d.SkillId, onDummy ? at.x : float.NaN, false);
        }

        void OnEffect(EffectEvent ef)
        {
            string key;
            Vector2 at;
            float h;
            if (ef.Effect == Effect.Regen && _dummyKey.TryGetValue(ef.TargetId, out key) && village.DummyInfo(key, out at, out h))
            {
                Float("Hồi đầy máu", at + new Vector2(0f, h + 40f), new Color(0.5f, 1f, 0.5f), 22, 1.4f);
                _lostRatio = 1f;
                return;
            }
            if ((ef.Effect == Effect.Hold || ef.Effect == Effect.Stun) && _dummyKey.TryGetValue(ef.TargetId, out key) && village.DummyInfo(key, out at, out h))
            {
                EnsureFx();
                float seconds = ef.DurationMs / 1000f;
                if (ef.Effect == Effect.Hold)
                    _fx.PlayData(32, at + new Vector2(0f, -24f * village.UiScale), 0, seconds);      // the rope of Trói: Effect(32, x, y + 24) while it holds
                else
                {
                    _stuns.Add(new StunPlan { key = key, until = Time.unscaledTime + seconds, nextAt = Time.unscaledTime });
                    village.StunDummy(key, seconds);
                }
            }
            if (ef.Effect == Effect.Buff && ef.SkillId == 21)
            {
                // Huýt sáo: every ally it strengthens gets the whistle (Nro: Effect(39, cx, cy, 3, 3, 1), three times over)
                ulong ally;
                if (_characterOf.TryGetValue(ef.TargetId, out ally))
                    for (int k = 0; k < village.PartyCount; k++)
                    {
                        ulong pid;
                        Vector2 where;
                        int facing;
                        if (village.PartyMember(k, out pid, out where, out facing) && pid == ally) { EnsureFx(); _fx.PlayData(39, where, 3); }
                    }
            }
            ulong who;
            if (!_characterOf.TryGetValue(ef.SourceId, out who) || ef.SkillId == 0) return;
            if (ef.Effect == Effect.CastEnd)
            {
                // a cast, a gong, a channel or a hold ended (done or broken): the poses and what they draw stop (the monkey is back to its own body)
                if (ef.SkillId == 23)
                {
                    string heldKey;
                    Vector2 heldAt;
                    float heldH;
                    if (_dummyKey.TryGetValue(ef.TargetId, out heldKey) && village.DummyInfo(heldKey, out heldAt, out heldH) && _fx != null)
                        _fx.StopData(32, heldAt + new Vector2(0f, -24f * village.UiScale), 120f);
                    return;
                }
                village.StopAction(who);
                if (_fx != null) _fx.CancelCharacter(who);
                if (_ultimate != null && ef.SkillId >= 24 && ef.SkillId <= 26) _ultimate.Cancel(who);
                if (ef.SkillId == MonkeySkill)
                {
                    _looks.RemoveAll(c => c.character == who);
                    village.SetLook(who, 0);
                    _monkeyWindow.Remove(who);
                }
                return;
            }
            if (ef.Effect == Effect.UltimateBeam)
            {
                // the gong is over: the beam of Super Kamejoko / Cađíc goes through everything up to {length} Nro px away for the time the server hits
                EnsureUltimate();
                _ultimate.Release(who, ef.SkillId, ef.Length, ef.DurationMs / 1000f);
                return;
            }
            if (ef.Effect == Effect.MafubaGather)
            {
                // Ma phong ba: this target is sucked into the jar (it spins into it, then is hidden), and comes out as a jar when the server says so
                string jarKey;
                if (_dummyKey.TryGetValue(ef.TargetId, out jarKey))
                {
                    EnsureUltimate();
                    _ultimate.JarTarget(who, jarKey, ef.DurationMs / 1000f, ef.X * village.UiScale);
                }
                return;
            }
            if (ef.Effect == Effect.Jar)
            {
                string jarKey;
                if (_dummyKey.TryGetValue(ef.TargetId, out jarKey))
                {
                    EnsureUltimate();
                    _ultimate.JarTime(jarKey, ef.DurationMs / 1000f);
                }
                return;
            }
            if (ef.Effect == Effect.CastStart && ef.SkillId == MonkeySkill)
            {
                // Biến hình: the hold of 1.5 s (Nro isWaitMonkey: the character cannot move, the charge effects play); the body changes when the server says so
                ShowSkill(who, MonkeySkill, AimX(who, ef), true);
                EnsureFx();
                _fx.PlayCharge(who, 13, ef.DurationMs / 1000f);
                return;
            }
            if (ef.Effect == Effect.CastStart)
            {
                // the charge of every character, the controlled one too (the server knows how long it lasts); Thái Dương Hạ San has none (duration 0)
                if (ef.DurationMs == 0)
                {
                    if (ef.SkillId == 6 && ef.SourceId == _myId) _strobeStart = Time.unscaledTime;       // Thái dương hạ san: the screen blinks white
                    if (ef.SourceId != _myId) ShowSkill(who, ef.SkillId, AimX(who, ef), false);
                    return;
                }
                string race;
                _races.TryGetValue(who, out race);
                int fxFrom, paint;
                bool fly;
                int[] poses = BbSkillPoses.Charge(ef.SkillId, LevelOf(who, ef.SkillId), village.Airborne(who), race == "XAYDA", ef.DurationMs / 1000f, out fxFrom, out paint, out fly);
                PlayPicked(who, ef.SkillId, poses, paint, fly, fxFrom, AimX(who, ef));
                if (ef.SkillId == 10) { EnsureFx(); _fx.PlaySpiritBomb(who, ef.DurationMs / 1000f); }
                if (ef.SkillId == 11 || ef.SkillId == 14) { EnsureFx(); _fx.PlayCharge(who, (int)ef.SkillId, ef.DurationMs / 1000f); }
                if (ef.SkillId == 11) BbSfx.Play(BbSfx.NamekLazer, 0.6f, ef.DurationMs / 1000f);          // the beam leaves when the charge ends
                // the blast of the whole screen (GameScr.activeSuperPower): where Tự phát nổ goes off, where the dart of Quả cầu kênh khí and Makankosappo lands
                if (ef.SkillId == 10 || ef.SkillId == 11 || ef.SkillId == 14)
                    _flashPlans.Add(new FlashPlan { at = Time.unscaledTime + ef.DurationMs / 1000f, who = who, atTarget = ef.SkillId != 14 });
                if (ef.SkillId >= 24 && ef.SkillId <= 26)
                {
                    Vector2 aim;
                    bool hasAim = UltimateTarget(who, out aim);
                    EnsureUltimate();
                    _ultimate.Cast(who, ef.SkillId, ef.DurationMs / 1000f, hasAim, aim, Mathf.Abs(ef.DirX) > 0.05f ? (ef.DirX > 0f ? 1 : -1) : 0);
                }
                return;
            }
            if (ef.Effect == Effect.Buff && ef.SkillId == MonkeySkill)
            {
                // Biến hình: the hold is over, the character becomes the great ape of its level for the time of the skill (the paint plays again, and again at the end)
                uint level = LevelOf(who, MonkeySkill);
                float now = Time.unscaledTime;
                _looks.RemoveAll(c => c.character == who);
                _looks.Add(new LookChange { character = who, costume = 9000 + (int)Math.Max(1u, Math.Min(7u, level)), at = now, replay = true });
                _looks.Add(new LookChange { character = who, costume = 0, at = now + ef.DurationMs / 1000f, replay = true });
                _monkeyWindow[who] = new Vector2(now, now + ef.DurationMs / 1000f);       // its skills change while it is the ape
                return;
            }
            // the other skills of the AI characters (shield, regeneration, the monkey, teleport, sleep...): their poses when the server reports them
            if (ef.SourceId != _myId) ShowSkill(who, ef.SkillId, FocusX(), false);
        }

        /// <summary>Where the character looks when a skill starts: the way the server aimed it (every character aims at its own target), else the focused dummy.</summary>
        float AimX(ulong who, EffectEvent ef)
        {
            if (Mathf.Abs(ef.DirX) > 0.05f)
                for (int i = 0; i < village.PartyCount; i++)
                {
                    ulong id;
                    Vector2 feet;
                    int facing;
                    if (village.PartyMember(i, out id, out feet, out facing) && id == who) return feet.x + (ef.DirX > 0f ? 1000f : -1000f);
                }
            return FocusX();
        }

        float FocusX()
        {
            Vector2 at;
            float h;
            return _focusKey != null && village.DummyInfo(_focusKey, out at, out h) ? at.x : float.NaN;
        }

        /// <summary>Stage 1: a projectile is a glow at its server position (Nro pixels x the map scale, a little above the feet); stage 2 draws the real pictures.</summary>
        void DrawProjectiles(MatchFrame f)
        {
            var world = village.WorldLayer;
            if (world == null) return;
            float s = village.UiScale;
            var alive = new HashSet<uint>();
            foreach (var p in f.Projectiles)
            {
                alive.Add(p.ProjectileId);
                Image img;
                if (!_projectiles.TryGetValue(p.ProjectileId, out img) || img == null)
                {
                    img = NewImage("Projectile", world, glowSprite, new Color(0.55f, 0.85f, 1f, 0.95f));
                    img.rectTransform.sizeDelta = new Vector2(46f, 46f);
                    _projectiles[p.ProjectileId] = img;
                    ulong who;
                    ulong owner;
                    bool isNew = _seenProjectiles.Add(p.ProjectileId);
                    if (isNew && p.OwnerId != _myId && _characterOf.TryGetValue(p.OwnerId, out who))
                        ShowSkill(who, p.SkillId, FocusX(), false);          // the pose first: the dart appears at the frame that throws it
                    int dartId = _characterOf.TryGetValue(p.OwnerId, out owner) ? BbSkillFxData.DartOf(p.SkillId, LevelOf(owner, p.SkillId)) : -1;
                    if (dartId >= 0 && p.SkillId == 5 && IsMonkey(owner)) dartId = BbSkillFxData.ArrowOf(MonkeyAntomicPaint, false).dart;          // the beam of the great ape
                    var dart = dartId >= 0 ? BbDart.Create(world, dartId, s) : null;
                    if (dart != null)
                    {
                        _darts[p.ProjectileId] = dart;
                        img.enabled = false;
                        Vector2 hand;
                        int handDir;
                        float at;
                        if (HandOf(owner, out hand, out handDir) && _arrowAt.TryGetValue(owner, out at)) dart.Begin(hand, at - Time.unscaledTime);
                        Vector2 stop;
                        if (DartStop(p, owner, out stop))
                        {
                            dart.StopAt(stop);
                            // the picture flies by itself from the hand to the target, at the speed of the server's projectile (measured on the next report)
                            if (HandOf(owner, out hand, out handDir))
                                _launches[p.ProjectileId] = new Launch { dart = dart, hand = hand, stop = stop, at = _arrowAt.TryGetValue(owner, out at) ? at : Time.unscaledTime,
                                                                          from = new Vector2(p.X * s, p.Y * s + 26f * s), tick = f.ServerTick };
                        }
                    }
                }
                img.rectTransform.anchoredPosition = new Vector2(p.X * s, p.Y * s + 26f * s);
                BbDart picture;
                if (_darts.TryGetValue(p.ProjectileId, out picture) && picture != null) picture.Move(img.rectTransform.anchoredPosition, p.DirX, p.DirY, -1f, f.ServerTick);
                Launch launch;
                if (_launches.TryGetValue(p.ProjectileId, out launch) && f.ServerTick > launch.tick)
                {
                    // the server moves it once every 50 ms: its speed in Nro pixels a second, the picture flies at that speed
                    Vector2 now2 = new Vector2(p.X * s, p.Y * s + 26f * s);
                    float serverSpeed = Vector2.Distance(now2, launch.from) / ((f.ServerTick - launch.tick) * 0.05f);          // canvas px a second
                    Vector2 way = new Vector2(p.DirX, p.DirY).normalized;
                    float remaining = Mathf.Max(0f, Vector2.Dot(launch.stop - now2, way));
                    float eta = remaining / Mathf.Max(1f, serverSpeed);                    // when the server's projectile reaches the target (and strikes)
                    float wait = Mathf.Max(0f, launch.at - Time.unscaledTime);             // the picture leaves the hand at the frame of the throw
                    float flight = Mathf.Max(0.15f, eta - wait);                           // ... and arrives together with the strike
                    float visual = Vector2.Distance(launch.hand, launch.stop) / flight / s;
                    if (launch.dart != null) launch.dart.Fly(launch.hand, launch.stop, wait, Mathf.Clamp(visual, 200f, 4000f), true);
                    _launches.Remove(p.ProjectileId);
                }
            }
            var gone = new List<uint>();
            foreach (var kv in _projectiles) if (!alive.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (uint id in gone)
            {
                if (_projectiles[id] != null) Kill(_projectiles[id].gameObject);
                _projectiles.Remove(id);
                BbDart picture;
                Launch pending;
                if (_launches.TryGetValue(id, out pending))
                {
                    // the projectile ended before its speed was known: the picture flies at the usual speed of Nro
                    if (pending.dart != null) pending.dart.Fly(pending.hand, pending.stop, pending.at - Time.unscaledTime, 480f);
                    _launches.Remove(id);
                }
                if (_darts.TryGetValue(id, out picture)) { if (picture != null) picture.Finish(); _darts.Remove(id); }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- every frame

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_phase == Phase.Active && !_demo) ReportAndWatch(dt);
            if (village != null) village.AiHunt = _phase == Phase.Active;
            if (village != null) UpdateLooks();
            if (village != null && _phase == Phase.Active) UpdatePets(dt);
            UpdateStuns();
            AutoSession();
            if (village != null) UpdateFlash();
            DriveCombat();
            if (_sayUntil > 0f && Time.unscaledTime > _sayUntil) { _sayUntil = 0f; if (_status != null) _status.text = ""; }
            AnimateFloating(dt);
            Refresh();
        }

        void ReportAndWatch(float dt)
        {
            ulong main;
            Vector2 feet;
            int facing;
            if (!village.PartyMember(0, out main, out feet, out facing)) return;
            if (main != _myCharacter) { _pendingSlot = -1; Leave(); return; }                       // another main character: a new session is needed
            // the earring was used or taken off while the session ran: the squad of the session is the old one (the partner would go on fighting beside the fused character);
            // the session ends and the next one starts with the right squad (owner report 2026-10-10)
            ulong partnerNow = menu != null && menu.FusionMainCharacterId == main ? menu.FusionSubCharacterId : 0;
            if (partnerNow != _sessionPartner) { Leave(); return; }
            Vector2 foe;
            float h;
            bool inView = village.VisibleDummyKeys().Count > 0;
            if (!inView && (_focusKey == null || !village.DummyInfo(_focusKey, out foe, out h) || Mathf.Abs(foe.x - feet.x) > LeaveDistance))
            {
                if (Time.unscaledTime - _lastFireAt < 3f) _farSince = -1f;       // a skill used with no target keeps the session open
                else
                {
                    if (_farSince < 0f) _farSince = Time.unscaledTime;
                    if (Time.unscaledTime - _farSince > 5f) { Leave(); return; }
                }
            }
            else _farSince = -1f;
            _reportClock += dt;
            if (_reportClock < ReportEvery) return;
            _reportClock = 0f;
            float s = village.UiScale;
            var report = new PlayerStateReport { Seq = ++_seq, X = feet.x / s, Y = feet.y / s, DirX = facing, DirY = 0f };
            for (int i = 1; i < village.PartyCount; i++)
            {
                ulong id;
                Vector2 at;
                int f;
                uint entity;
                if (village.PartyMember(i, out id, out at, out f) && _entityOf.TryGetValue(id, out entity))
                    report.Allies.Add(new AllyPosition { EntityId = entity, X = at.x / s, Y = at.y / s, DirX = f, DirY = 0f });
            }
            try { BbSession.Client.Send(new Envelope { PlayerStateReport = report }); } catch (Exception) { }
        }

        void AnimateFloating(float dt)
        {
            for (int i = _floating.Count - 1; i >= 0; i--)
            {
                var fl = _floating[i];
                fl.age += dt;
                if (fl.age >= fl.life || fl.text == null) { if (fl.text != null) Kill(fl.text.gameObject); _floating.RemoveAt(i); continue; }
                float t = fl.age / fl.life;
                fl.text.rectTransform.anchoredPosition = fl.start + new Vector2(0f, 70f * t);
                var c = fl.text.color;
                c.a = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                fl.text.color = c;
            }
        }

        /// <summary>The arrow over the focused dummy, the HP bar with its name, the skill buttons and the Ki bar.</summary>
        void Refresh()
        {
            if (!_built) return;
            bool on = _phase == Phase.Active;
            EntityState foe = null;
            uint foeId;
            if (on && _focusKey != null && _dummyId.TryGetValue(_focusKey, out foeId)) _states.TryGetValue(foeId, out foe);
            _barRoot.gameObject.SetActive(foe != null);
            if (!on && (!_demo || _previewDemo)) EnsurePreviewSkills();
            bool showSkills = _buttons.Count > 0 && !(menu != null && menu.BlocksCombatUi) && (on || _phase == Phase.Starting || _phase == Phase.Idle);
            _skillRoot.gameObject.SetActive(showSkills);
            Vector2 at;
            float h;
            bool arrow = foe != null && village != null && village.DummyInfo(_focusKey, out at, out h);
            if (_arrow == null) NewArrow();                                       // the map was drawn again (it destroys the world layer and what lies on it)
            _arrow.gameObject.SetActive(arrow);
            if (foe != null)
            {
                double ratio = foe.HpMax > 0 ? (double)foe.Hp / foe.HpMax : 0.0;
                _shownRatio = (float)ratio;
                if (_lostRatio < _shownRatio) _lostRatio = _shownRatio;
                _lostRatio = Mathf.MoveTowards(_lostRatio, _shownRatio, Time.unscaledDeltaTime * 0.6f);
                SetWidth(_barRed, _shownRatio);
                SetWidth(_barLost, _lostRatio);
                string name;
                _barName.text = _names.TryGetValue(foe.EntityId, out name) ? name : _focusKey;
                _barText.text = Money(foe.Hp) + " / " + Money(foe.HpMax);
                // Char.paintHp of Nro: one colour per 10% of HP (index 9 = light blue at full HP, 0 = grey below 10%)
                long idx = foe.HpMax > 0 ? Math.Min(9, Math.Max(0, (long)(foe.Hp * 100 / foe.HpMax) / 10 - 1)) : 0;
                _arrowImage.sprite = _arrows != null ? _arrows[idx] : null;
                if (arrow && village.DummyInfo(_focusKey, out at, out h))
                {
                    if (_arrow.parent != village.WorldLayer) _arrow.SetParent(village.WorldLayer, false);
                    _arrow.anchoredPosition = at + new Vector2(0f, h + 6f);
                    _arrow.SetAsLastSibling();
                }
            }
            RefreshHuds(on);
            if (showSkills) RefreshButtons();
        }

        void RefreshButtons()
        {
            EntityState mine;
            _states.TryGetValue(_myId, out mine);
            float now = Time.unscaledTime;
            foreach (var b in _buttons)
            {
                float left = CooldownEnd(b.slot.SkillId) - now;
                b.shade.fillAmount = left > 0f && b.slot.CooldownMs > 0 ? Mathf.Clamp01(left / (b.slot.CooldownMs / 1000f)) : 0f;
                b.cooldown.gameObject.SetActive(left > 0f);
                if (left > 0f) b.cooldown.text = left >= 1f ? Mathf.CeilToInt(left).ToString(CultureInfo.InvariantCulture) : left.ToString("0.0", CultureInfo.InvariantCulture);
                if (b.slot.Level > 0 && b.slot.SkillId == 5)
                {
                    bool ape = IsMonkey(_myCharacter);          // Antomic is another skill while the character is the great ape
                    b.level.text = ape ? "KHỈ" : b.slot.Level.ToString(CultureInfo.InvariantCulture);
                    b.level.color = ape ? new Color(1f, 0.45f, 0.3f) : new Color(1f, 0.95f, 0.5f);
                }
                bool chosen = b.slot.Slot == _selectedSlot;
                b.frame.color = b.slot.Level == 0 ? new Color(0.5f, 0.5f, 0.55f) : chosen ? new Color(1f, 1f, 0.55f) : Color.white;              // the skill in focus; locked: grey
                b.root.localScale = chosen ? new Vector3(1.1f, 1.1f, 1f) : Vector3.one;
                bool kiOk = mine == null || (b.slot.KiAll ? mine.Ki > 0 : mine.Ki >= b.slot.KiCost);
                b.icon.color = b.slot.Level == 0 ? new Color(0.3f, 0.3f, 0.35f) : kiOk ? Color.white : new Color(0.45f, 0.45f, 0.55f);
                b.frame.sprite = left > 0f ? skillFrameOn : skillFrame;
            }
            if (_autoImage != null)
            {
                _autoImage.color = Color.white;
                _autoLabel.gameObject.SetActive(_auto);
                _autoRing.color = _auto ? new Color(0.5f, 1f, 0.4f, 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f)) : new Color(0.35f, 1f, 0.35f, 0f);
            }
        }

        void RefreshHuds(bool on)
        {
            _hudRoot.gameObject.SetActive(on && _huds.Count > 0);
            if (!on) return;
            foreach (var h in _huds)
            {
                EntityState st;
                if (!_states.TryGetValue(h.entity, out st)) continue;
                float hp = st.HpMax > 0 ? Mathf.Clamp01((float)((double)st.Hp / st.HpMax)) : 0f;
                float ki = st.KiMax > 0 ? Mathf.Clamp01((float)((double)st.Ki / st.KiMax)) : 0f;
                if (h.hpTrail < hp) h.hpTrail = hp;
                if (h.kiTrail < ki) h.kiTrail = ki;
                h.hpTrail = Mathf.MoveTowards(h.hpTrail, hp, Time.unscaledDeltaTime * 0.6f);
                h.kiTrail = Mathf.MoveTowards(h.kiTrail, ki, Time.unscaledDeltaTime * 0.6f);
                h.hp.fillAmount = hp;
                h.hpLost.fillAmount = h.hpTrail;
                h.ki.fillAmount = ki;
                h.kiLost.fillAmount = h.kiTrail;
            }
        }

        /// <summary>One frame per character of the party, in the order of the planets, side by side at the top left.</summary>
        void BuildHuds()
        {
            EnsureBuilt();
            foreach (var h in _huds) Kill(h.root.gameObject);
            _huds.Clear();
            var members = new List<KeyValuePair<int, ulong>>();
            int fallback = 0;
            foreach (var kv in _entityOf)
            {
                string race;
                int order = _races.TryGetValue(kv.Key, out race) ? Array.IndexOf(HudRaces, race) : -1;
                members.Add(new KeyValuePair<int, ulong>(order >= 0 ? order : 10 + fallback++, kv.Key));
            }
            members.Sort((a, b) => a.Key.CompareTo(b.Key));
            var snap = BbSession.Snapshot;
            float k = HudWidth / 620f, height = 164f * k;
            _hudRoot.sizeDelta = new Vector2(3 * HudWidth + 2 * HudGap, height);
            for (int i = 0; i < members.Count; i++)
            {
                ulong character = members[i].Value;
                var h = new Hud { entity = _entityOf[character] };
                h.root = NewRect("Hud" + character, _hudRoot);
                Place(h.root, i * (HudWidth + HudGap), 0f, HudWidth, height);
                var panel = NewImage("Panel", h.root, hudPanel, Color.white);
                Place(panel.rectTransform, 0f, 0f, HudWidth, height);
                // the slots of the Nro panel (x4 pixels): HP 264 x 40 at (332, 20), Ki 236 x 24 at (332, 80)
                h.hpLost = NewBar("HpLost", h.root, hudHpLost, 332f, 20f, 264f, 40f, k);
                h.hp = NewBar("Hp", h.root, hudHp, 332f, 20f, 264f, 40f, k);
                h.kiLost = NewBar("KiLost", h.root, hudKiLost, 332f, 80f, 236f, 24f, k);
                h.ki = NewBar("Ki", h.root, hudKi, 332f, 80f, 236f, 24f, k);
                // the green part: the planet on the first line (Nro: the name), the level under it (Nro: the power)
                string title = members[i].Key < HudTitles.Length ? HudTitles[members[i].Key] : _names.ContainsKey(h.entity) ? _names[h.entity] : "";
                var dark = new Color(0.06f, 0.30f, 0.10f);
                h.name = NewText("Name", h.root, 15, dark, TextAnchor.MiddleCenter);
                h.name.text = title;
                Place(h.name.rectTransform, 30f * k, 20f * k, 270f * k, 54f * k);
                h.level = NewText("Level", h.root, 13, dark, TextAnchor.MiddleCenter);
                Place(h.level.rectTransform, 30f * k, 76f * k, 270f * k, 50f * k);
                uint level = 0;
                if (snap != null) foreach (var c in snap.Characters) if (c.CharacterId == character) level = c.Level;
                h.level.text = level > 0 ? "Cấp " + level : "";
                foreach (var t in new[] { h.name, h.level }) t.GetComponent<Outline>().enabled = false;      // dark green on the light green: no outline
                _huds.Add(h);
            }
        }

        Image NewBar(string name, Transform parent, Sprite sprite, float x, float y, float w, float h, float k)
        {
            var img = NewImage(name, parent, sprite, Color.white);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = 0;
            Place(img.rectTransform, x * k, y * k, w * k, h * k);
            return img;
        }

        // ---------------------------------------------------------------------------------------------------------------- building the screen parts

        void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            _root = (RectTransform)transform;
            // the arrows of Nro: the sheet is 9 x 60 Nro px (36 x 240 at x4), one 9 x 6 triangle per 10% of HP from the top
            if (arrowSheet != null)
            {
                var tex = arrowSheet.texture;
                var r = arrowSheet.rect;
                float cell = r.height / 10f;
                _arrows = new Sprite[10];
                for (int i = 0; i < 10; i++)
                {
                    float topIndex = 9 - i;                                           // index 9 (full HP) is the first triangle at the top
                    _arrows[i] = Sprite.Create(tex, new Rect(r.x, r.y + r.height - (topIndex + 1) * cell, r.width, cell), new Vector2(0.5f, 0f), 100f);
                }
            }
            NewArrow();

            // the white of Tự phát nổ over the whole view, under every button
            _flash = NewImage("Flash", _root, whiteSprite, new Color(1f, 1f, 1f, 1f));
            _flash.canvasRenderer.cullTransparentMesh = false;
            Stretch(_flash.rectTransform, 0f);
            _flash.enabled = false;
            _flash.transform.SetAsFirstSibling();

            // the HP/Ki frames of the party, top left
            _hudRoot = NewRect("PartyHud", _root);
            _hudRoot.anchorMin = _hudRoot.anchorMax = new Vector2(0f, 1f);
            _hudRoot.pivot = new Vector2(0f, 1f);
            _hudRoot.anchoredPosition = new Vector2(8f, -8f);
            _hudRoot.gameObject.SetActive(false);

            // the HP bar of the focused dummy: the middle of the screen, a little below the top edge, its name above it
            _barRoot = NewRect("TargetBar", _root);
            _barRoot.anchorMin = _barRoot.anchorMax = new Vector2(0.5f, 1f);
            _barRoot.pivot = new Vector2(0.5f, 1f);
            _barRoot.anchoredPosition = new Vector2(0f, -62f);
            _barRoot.sizeDelta = new Vector2(440f, 104f);
            _barName = NewText("Name", _barRoot, 22, new Color(1f, 0.92f, 0.45f), TextAnchor.MiddleCenter);      // above the frame
            Place(_barName.rectTransform, 0f, 0f, 440f, 28f);
            var frame = NewImage("Frame", _barRoot, bossFrame, Color.white);                                       // around the HP bar only
            frame.type = Image.Type.Sliced;
            Place(frame.rectTransform, 0f, 30f, 440f, 74f);
            _barLost = NewImage("Lost", _barRoot, hpLost, Color.white);
            _barLost.type = Image.Type.Sliced;
            Place(_barLost.rectTransform, 30f, 54f, 380f, 26f);
            _barRed = NewImage("Hp", _barRoot, hpBar, Color.white);
            _barRed.type = Image.Type.Sliced;
            Place(_barRed.rectTransform, 30f, 54f, 380f, 26f);
            _barText = NewText("Numbers", _barRoot, 15, Color.white, TextAnchor.MiddleCenter);
            Place(_barText.rectTransform, 30f, 54f, 380f, 26f);
            _barRoot.gameObject.SetActive(false);

            _status = NewText("Status", _root, 20, Color.white, TextAnchor.MiddleCenter);
            _status.rectTransform.anchorMin = _status.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            _status.rectTransform.pivot = new Vector2(0.5f, 1f);
            _status.rectTransform.anchoredPosition = new Vector2(0f, -184f);
            _status.rectTransform.sizeDelta = new Vector2(500f, 30f);

            // the skill buttons: bottom right, one row, slot 0 (the punch) on the right
            _skillRoot = NewRect("Skills", _root);
            _skillRoot.anchorMin = _skillRoot.anchorMax = new Vector2(1f, 0f);
            _skillRoot.pivot = new Vector2(1f, 0f);
            _skillRoot.anchoredPosition = new Vector2(-14f, 14f);
            _skillRoot.sizeDelta = new Vector2(10 * (ButtonSize + ButtonGap), ButtonSize + 30f);
            // the fist (Nro's attack button) at the outer end of the skills: switches the automatic use of the skill in focus on and off
            _autoRing = NewImage("AutoRing", _skillRoot, fireIdle, new Color(0.35f, 1f, 0.35f, 0f));     // a green copy of the fist a little bigger behind it: the rim shows when it is on
            _autoRing.canvasRenderer.cullTransparentMesh = false;
            _autoRing.rectTransform.anchorMin = _autoRing.rectTransform.anchorMax = new Vector2(1f, 0f);
            _autoRing.rectTransform.anchoredPosition = new Vector2(-FistSize / 2f, FistSize / 2f);
            _autoRing.rectTransform.sizeDelta = new Vector2(FistSize * 1.26f, FistSize * 1.26f);
            var fist = NewRect("Auto", _skillRoot);
            fist.anchorMin = fist.anchorMax = new Vector2(1f, 0f);
            fist.pivot = new Vector2(1f, 0f);
            fist.anchoredPosition = Vector2.zero;
            fist.sizeDelta = new Vector2(FistSize, FistSize);
            _autoImage = fist.gameObject.AddComponent<Image>();
            _autoImage.sprite = fireIdle;
            _autoButton = fist.gameObject.AddComponent<Button>();
            _autoButton.targetGraphic = _autoImage;
            _autoButton.transition = Selectable.Transition.SpriteSwap;
            _autoButton.spriteState = new SpriteState { pressedSprite = firePressed, highlightedSprite = fireIdle, selectedSprite = fireIdle };
            _autoButton.onClick.AddListener(ToggleAuto);
            _autoLabel = NewText("AutoLabel", fist, 17, new Color(0.45f, 1f, 0.4f), TextAnchor.LowerCenter);
            _autoLabel.text = "AUTO";
            _autoLabel.rectTransform.anchorMin = Vector2.zero;
            _autoLabel.rectTransform.anchorMax = Vector2.one;
            _autoLabel.rectTransform.offsetMin = new Vector2(0f, -4f);
            _autoLabel.rectTransform.offsetMax = Vector2.zero;
            _autoLabel.gameObject.SetActive(false);
            // the focus button above it: the next target in view
            var focus = NewRect("Focus", _skillRoot);
            focus.anchorMin = focus.anchorMax = new Vector2(1f, 0f);
            focus.pivot = new Vector2(1f, 0f);
            focus.anchoredPosition = new Vector2(-(FistSize - FocusSize) / 2f, FistSize + 14f);
            focus.sizeDelta = new Vector2(FocusSize, FocusSize);
            var focusImage = focus.gameObject.AddComponent<Image>();
            focusImage.sprite = focusIdle;
            var focusButton = focus.gameObject.AddComponent<Button>();
            focusButton.targetGraphic = focusImage;
            focusButton.transition = Selectable.Transition.SpriteSwap;
            focusButton.spriteState = new SpriteState { pressedSprite = focusPressed, highlightedSprite = focusIdle, selectedSprite = focusIdle };
            focusButton.onClick.AddListener(CycleFocus);
            _skillRoot.gameObject.SetActive(false);
        }

        void NewArrow()
        {
            _arrow = NewRect("TargetArrow", _root);
            _arrow.pivot = new Vector2(0.5f, 0f);
            _arrow.sizeDelta = new Vector2(9f * 2.6f, 6f * 2.6f);
            _arrowImage = _arrow.gameObject.AddComponent<Image>();
            _arrowImage.raycastTarget = false;
            _arrow.gameObject.SetActive(false);
        }

        void BuildSkillButtons()
        {
            EnsureBuilt();
            foreach (var b in _buttons) Kill(b.root.gameObject);
            _buttons.Clear();
            var resolver = NroSpriteResolver.Instance;
            for (int i = 0; i < _skills.Count; i++)
            {
                var slot = _skills[i];
                var b = new SkillButton { slot = slot };
                b.root = NewRect("Skill" + slot.Slot, _skillRoot);
                b.root.anchorMin = b.root.anchorMax = new Vector2(1f, 0f);
                b.root.pivot = new Vector2(1f, 0f);
                b.root.anchoredPosition = new Vector2(-(i + 1) * (ButtonSize + ButtonGap) - 10f, 0f);       // the fist takes the outer end
                b.root.sizeDelta = new Vector2(ButtonSize, ButtonSize);
                b.frame = b.root.gameObject.AddComponent<Image>();
                b.frame.sprite = skillFrame;
                var button = b.root.gameObject.AddComponent<Button>();
                button.targetGraphic = b.frame;
                var captured = slot;
                button.onClick.AddListener(() => PressSkill(captured));
                Sprite icon = slot.IconId > 0 && resolver != null ? resolver.ResolveSprite((int)slot.IconId, BbAssetCache.Scale) : null;
                b.icon = NewImage("Icon", b.root, icon, Color.white);
                b.icon.preserveAspect = true;
                b.icon.enabled = icon != null;
                Stretch(b.icon.rectTransform, 9f);
                b.shade = NewImage("Cooldown", b.root, whiteSprite, new Color(0f, 0f, 0f, 0.62f));
                b.shade.type = Image.Type.Filled;
                b.shade.fillMethod = Image.FillMethod.Vertical;
                b.shade.fillOrigin = (int)Image.OriginVertical.Top;
                b.shade.fillAmount = 0f;
                Stretch(b.shade.rectTransform, 6f);
                b.cooldown = NewText("Seconds", b.root, 24, Color.white, TextAnchor.MiddleCenter);                 // the time left of the cooldown
                Stretch(b.cooldown.rectTransform, 0f);
                b.cooldown.gameObject.SetActive(false);
                b.level = NewText("Level", b.root, 13, new Color(1f, 0.95f, 0.5f), TextAnchor.LowerRight);
                Stretch(b.level.rectTransform, 6f);
                b.level.text = slot.Level > 0 ? slot.Level.ToString(CultureInfo.InvariantCulture) : "";
                if (slot.Level == 0) b.icon.color = new Color(0.3f, 0.3f, 0.35f);
                if (icon == null) { b.level.alignment = TextAnchor.MiddleCenter; b.level.text = Short(slot.Name); }
                if (slot.Level == 0) { b.level.alignment = TextAnchor.LowerCenter; b.level.fontSize = 11; b.level.text = "chưa học"; }
                _buttons.Add(b);
            }
        }

        static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            return name.Length <= 9 ? name : name.Substring(0, 8) + ".";
        }

        void Float(string text, Vector2 at, Color color, int size, float life)
        {
            var world = village != null ? village.WorldLayer : null;
            if (world == null) return;
            var t = NewText("Damage", world, size, color, TextAnchor.MiddleCenter);
            t.rectTransform.pivot = new Vector2(0.5f, 0f);
            t.rectTransform.sizeDelta = new Vector2(300f, 40f);
            t.rectTransform.anchoredPosition = at;
            t.text = text;
            _floating.Add(new Floating { text = t, start = at, life = life });
            if (_floating.Count > 40) { Kill(_floating[0].text.gameObject); _floating.RemoveAt(0); }
        }

        void Say(string text)
        {
            EnsureBuilt();
            _status.text = text;
        }

        static void SetWidth(Image img, float ratio)
        {
            var r = img.rectTransform;
            img.enabled = ratio > 0.002f;
            r.sizeDelta = new Vector2(Mathf.Max(26f, 380f * Mathf.Clamp01(ratio)), r.sizeDelta.y);
        }

        static string Money(ulong v)
        {
            return v.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
        }

        RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = new Vector2(0.5f, 0.5f);
            return r;
        }

        Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var r = NewRect(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        Text NewText(string name, Transform parent, int size, Color color, TextAnchor align)
        {
            var r = NewRect(name, parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = r.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.08f, 0.03f, 0.01f, 0.95f);
            o.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        /// <summary>Top-left placement inside a parent anchored at its top-left corner (x right, y down).</summary>
        static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        static void Stretch(RectTransform r, float pad)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(pad, pad);
            r.offsetMax = new Vector2(-pad, -pad);
        }

        static void Kill(GameObject go)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        // ---------------------------------------------------------------------------------------------------------------- the screenshot tool

        /// <summary>For the screenshot tool: a skill played on the main character as far as this pose, and its dart (when it has one) flying at {@code dartAt}.</summary>
        public void DemoSkill(uint skillId, uint level, float pose, Vector2 dartAt)
        {
            EnsureBuilt();
            ulong main;
            Vector2 feet;
            int facing;
            village.PartyMember(0, out main, out feet, out facing);
            var map = new Dictionary<uint, uint>();
            map[skillId] = level;
            _levels[main] = map;
            ShowSkill(main, skillId, float.NaN, true);
            if (_fx != null) _fx.DemoAt(pose);
            int dartId = BbSkillFxData.DartOf(skillId, level);
            if (dartId >= 0)
            {
                var dart = BbDart.Create(village.WorldLayer, dartId, village.UiScale);
                for (int i = 0; i < 8; i++) dart.Move(dartAt + new Vector2(-14f * (8 - i) * facing, 0f), facing, 0f, 1f / 30f);
            }
        }

        /// <summary>For the screenshot tool: the effect of a skill landing on a dummy, at this picture.</summary>
        public void DemoHit(string key, uint skillId, uint level, int frame)
        {
            EnsureBuilt();
            EnsureFx();
            Vector2 at;
            float h;
            if (!village.DummyInfo(key, out at, out h)) return;
            _fx.PlayHit(BbSkillFxData.Happen(BbSkillPoses.PaintOf(skillId, level)), at);
            _fx.DemoHitAt(frame);
        }

        /// <summary>For the screenshot tool: an ultimate of the main character shown as if this many game ticks had passed since it began charging.</summary>
        public void DemoUltimate(uint skillId, float chargeSeconds, int tick, string dummyKey)
        {
            EnsureBuilt();
            EnsureUltimate();
            ulong main;
            Vector2 feet, aim;
            int facing;
            float h;
            village.PartyMember(0, out main, out feet, out facing);
            bool has = village.DummyInfo(dummyKey, out aim, out h);
            _ultimate.Cast(main, skillId, chargeSeconds, has, aim);
            _ultimate.DemoAtTick(tick);
        }

        /// <summary>For the screenshot tool: the egg of Đẻ trứng hatched into this mob next to the main character, the spirit bomb charging, the blast of Tự phát nổ, the great ape.</summary>
        public void DemoPet(int mobId)
        {
            EnsureBuilt();
            ulong main;
            Vector2 feet;
            int facing;
            village.PartyMember(0, out main, out feet, out facing);
            float s = village.UiScale;
            AddPet(new EntitySpawn { EntityId = 500, Kind = EntityKind.Pet, MobTemplateId = mobId });
            _states[500] = new EntityState { EntityId = 500, X = (feet.x + 110f) / s, Y = feet.y / s, Alive = true };
            UpdatePets(1f);
        }

        public void DemoBomb(float seconds)
        {
            EnsureBuilt();
            EnsureFx();
            ulong main;
            Vector2 feet;
            int facing;
            village.PartyMember(0, out main, out feet, out facing);
            _fx.PlaySpiritBomb(main, 3f);
            village.PlayAction(main, new[] { (int)NroPose.Kame_Release_Fly }, 10f);
            _fx.DemoBombAt(seconds);
        }

        public void DemoFlash(float seconds)
        {
            EnsureBuilt();
            ulong main;
            Vector2 feet;
            int facing;
            village.PartyMember(0, out main, out feet, out facing);
            _flashWorld = feet + new Vector2(0f, 30f * village.UiScale);
            _flashStart = Time.unscaledTime - seconds;
            UpdateFlash();
        }

        public void DemoApe(ulong character, int level)
        {
            village.SetLook(character, 9000 + level);
        }

        /// <summary>For the scripted run in Play mode: the white of a blast or of the blinking is on the screen now.</summary>
        public bool DebugFlashOn { get { return _flash != null && _flash.enabled && _flash.color.a > 0.9f; } }

        /// <summary>For the scripted run in Play mode: the button of this skill is pressed.</summary>
        public void DebugUse(uint skillId)
        {
            foreach (var sl in _skills) if (sl.SkillId == skillId) { PressSkill(sl); return; }
            Debug.Log("[SkillFx] debug: no button for skill " + skillId);
        }

        /// <summary>For the screenshot tool: the fist switched on.</summary>
        public void DemoAuto(bool on)
        {
            _auto = on;
            Refresh();
        }

        /// <summary>For the screenshot tool: the village as the player sees it before any target is chosen (the skill buttons from the menu's snapshot).</summary>
        public void DemoPreview()
        {
            _previewDemo = true;
            _demo = true;
            _previewKey = 0;
            EnsureBuilt();
            Refresh();
        }

        /// <summary>For the screenshot tool (no server): a session as if it ran, the focused dummy at this share of its HP, a few damage numbers.</summary>
        public void DemoShow(string key, float hpRatio, IList<SkillSlot> skills)
        {
            _demo = true;
            EnsureBuilt();
            ulong main;
            Vector2 feet;
            int facing;
            village.PartyMember(0, out main, out feet, out facing);
            var started = new MatchStarted { Training = true, DungeonName = "Luyện tập" };
            started.Entities.Add(new EntitySpawn { EntityId = 1, Kind = EntityKind.PlayerCharacter, CharacterId = main, Name = HudTitles[0], Controlled = true, HpMax = 1000, Hp = 1000, KiMax = 1000, Ki = 640 });
            uint ally = 2;
            for (int i = 1; i < village.PartyCount; i++)
            {
                ulong other;
                Vector2 f;
                int fc;
                village.PartyMember(i, out other, out f, out fc);
                started.Entities.Add(new EntitySpawn { EntityId = ally, Kind = EntityKind.PlayerCharacter, CharacterId = other, Name = HudTitles[Mathf.Min(i, 2)], HpMax = 1000, Hp = i == 1 ? 620UL : 1000UL, KiMax = 1000, Ki = i == 1 ? 300UL : 880UL });
                ally++;
            }
            uint id = 10;
            foreach (string k in village.DummyKeys)
            {
                ulong max = 2000000000UL;
                started.Entities.Add(new EntitySpawn { EntityId = id++, Kind = EntityKind.TrainingDummy, DummyKey = k, Name = k == "moc_nhan" ? "Mộc nhân" : "Saibamen",
                    HpMax = max, Hp = k == key ? (ulong)(max * hpRatio) : max });
            }
            if (skills != null) started.Skills.AddRange(skills);
            Begin(started, main);
            _focusKey = key;
            village.CombatTarget = key;
            _lostRatio = Mathf.Min(1f, hpRatio + 0.08f);
            if (skills != null && skills.Count > 1) SetCooldown(main, skills[1].SkillId, Time.unscaledTime + 3f);
            Vector2 at;
            float h;
            if (village.DummyInfo(key, out at, out h))
            {
                Float("-12.345.678", at + new Vector2(-10f, h + 30f), new Color(1f, 0.35f, 0.25f), 24, 100f);
                Float("-48.765.432", at + new Vector2(20f, h + 70f), new Color(1f, 0.92f, 0.2f), 30, 100f);
                Float("-3.210.987", at + new Vector2(-30f, h + 110f), new Color(1f, 0.7f, 0.55f), 20, 100f);
            }
            Refresh();
        }
    }
}
