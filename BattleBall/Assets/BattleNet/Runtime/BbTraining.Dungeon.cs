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
    /// The combat controller run in a dungeon (owner request 2026-10-11, docs/architecture/BattleBallServer.md 15.25). The skill bar, the focus, the fist, the party frames, the
    /// damage numbers and the skill effects are the ones of the village training; what is different is that the world is the flying arena of <see cref="BbVillageView"/>
    /// (distances are in two dimensions, the monsters and the AI characters are drawn where the server says), the player reports the part of the arena his screen shows, and
    /// the waves, the boss, the deaths and the telegraphs of the boss have their own pictures.
    /// </summary>
    public sealed partial class BbTraining
    {
        bool _dungeon;
        float _arenaScale;
        string _dungeonName = "";
        readonly Dictionary<uint, string> _enemyLabel = new Dictionary<uint, string>();
        Text _banner;
        float _bannerUntil, _bannerFrom;
        RectTransform _reviveRoot;
        Text _reviveText;
        Button _reviveButton;
        Text _leaveLabel;
        Button _leaveButton;
        float _leaveArmedUntil, _reviveAskedAt;
        uint _revivesLeft;
        bool _dungeonEnded;

        sealed class Telegraph
        {
            public RectTransform rect;
            public Image image;
            public float start, until;
            public uint source;                                       // the monster that attacks: its warning goes when it dies (its blow never lands)
        }

        readonly List<Telegraph> _telegraphs = new List<Telegraph>();
        readonly HashSet<uint> _bossIds = new HashSet<uint>();            // the entities that are bosses (only they get the big HP bar)

        public bool InDungeon { get { return _dungeon; } }
        /// <summary>For the play scenario: presses the button "Rời phó bản" as a finger does (the first touch arms it, the second leaves).</summary>
        public void DebugPressLeave() { OnLeavePressed(); }

        /// <summary>For the play scenario: the leave button is on the screen and can be pressed, and what it says.</summary>
        public string DebugLeaveButton { get { return _leaveButton == null ? "none" : (_leaveButton.gameObject.activeSelf ? "shown" : "hidden") + (_leaveButton.interactable ? ", can be pressed" : ", LOCKED") + ", says '" + (_leaveLabel == null ? "" : _leaveLabel.text) + "'"; } }

        public string DebugBanner { get { return _banner != null && _banner.gameObject.activeSelf ? _banner.text : ""; } }

        /// <summary>For the play scenario: draws the two telegraphs of a boss near the main character, and makes the main character fall as if killed (the box of the revive appears).</summary>
        public void DebugDungeonUi(bool die)
        {
            ulong main;
            Vector2 feet;
            int facing;
            if (!village.PartyMember(0, out main, out feet, out facing)) return;
            float s = _arenaScale;
            OnDungeonEffect(new EffectEvent { Effect = Effect.TelegraphRect, X = feet.x / s - 20f, Y = feet.y / s + 30f, DirX = 1f, DirY = 0.2f, Length = 280f, Width = 70f, DurationMs = 6000 });
            OnDungeonEffect(new EffectEvent { Effect = Effect.TelegraphCircle, X = feet.x / s - 120f, Y = feet.y / s - 40f, Length = 90f, DurationMs = 6000 });
            if (die) { _debugHoldDead = true; village.DieParty(main, new Vector2(1f, 0.3f)); }
        }

        bool _debugHoldDead;                      // the play scenario kills the main character on the client only: the server still says it is alive

        /// <summary>Distance between two points of the world: in the village only the way along the ground counts, in the arena it is the straight distance.</summary>
        float Dist(Vector2 a, Vector2 b)
        {
            return _dungeon ? Vector2.Distance(a, b) : Mathf.Abs(a.x - b.x);
        }

        /// <summary>The main character goes to a target (a skill needs it close): along the ground in the village, flying straight to it in the arena.</summary>
        void Approach(Vector2 foe, float stop)
        {
            if (_dungeon) village.ApproachToPoint(foe, stop); else village.ApproachTo(foe.x, stop);
        }

        // ---------------------------------------------------------------------------------------------------------------- start and end

        /// <summary>
        /// The dungeon has started (the server's MatchStarted): the village becomes the arena, the squad and the monsters are drawn where the server put them, and the combat
        /// controller runs on the dungeon's frames until the match ends.
        /// </summary>
        public void BeginDungeon(MatchStarted started, ulong mainCharacter, ulong partner, string tier)
        {
            Subscribe();
            EnsureBuilt();
            Stop();                                                    // whatever the village session left
            if (_ultimate != null) _ultimate.CancelAll();
            _dungeon = true;
            _dungeonEnded = false;
            _dungeonName = started.DungeonName;
            _sessionPartner = partner;
            _revivesLeft = started.RevivesLeft;
            _auto = false;
            _pendingSlot = -1;
            _focusKey = null;
            float uiScale = village.characterScale;
            _arenaScale = uiScale;
            village.EnterArena(started.Arena.Width, started.Arena.Height, tier, (int)started.DungeonId - 1);
            _enemyLabel.Clear();
            _bossIds.Clear();
            Begin(started, mainCharacter);
            EnsureDungeonUi();
            _reviveRoot.gameObject.SetActive(false);
            // pressing it twice locked the button ("Đang rời...") and the next dungeon showed it locked: every run starts with the button as new (owner report 2026-10-12)
            _leaveArmedUntil = 0f;
            _leaveButton.interactable = true;
            _leaveLabel.text = "Rời phó bản";
            _leaveButton.gameObject.SetActive(true);
            ShowBanner(started.DungeonName, new Color(1f, 0.92f, 0.45f), 2.2f);
        }

        /// <summary>The match is over (the server's MatchEnded) or the player is going back: the arena goes away and the village comes back.</summary>
        public void EndDungeon()
        {
            if (!_dungeon) return;
            _dungeon = false;
            if (_phase != Phase.Idle) Stop();
            foreach (var t in _telegraphs) if (t.rect != null) Kill(t.rect.gameObject);
            _telegraphs.Clear();
            if (_ultimate != null) _ultimate.CancelAll();
            if (_reviveRoot != null) _reviveRoot.gameObject.SetActive(false);
            if (_leaveButton != null) _leaveButton.gameObject.SetActive(false);
            if (_banner != null) _banner.gameObject.SetActive(false);
            _dummyKey.Clear();
            _dummyId.Clear();
            _enemyLabel.Clear();
            _bossIds.Clear();
            village.LeaveArena();
        }

        /// <summary>
        /// The menu: the player took another character in the arena (owner request 2026-10-11). The character becomes the main one at once (the stick, the screen, the skill
        /// buttons after the server's answer) and the old one an AI where it stands; nothing of either is reset.
        /// </summary>
        public bool SwitchDungeonMain(ulong character)
        {
            if (!_dungeon || _dungeonEnded || _phase != Phase.Active || village == null || character == _myCharacter) return false;
            if (!_entityOf.ContainsKey(character) || !village.SwitchMain(character)) return false;
            RequestControl(character);
            return true;
        }

        public string DebugControl()
        {
            var sb = new System.Text.StringBuilder("control char " + _myCharacter + " entity " + _myId + " skills");
            foreach (var sl in _skills) if (sl.Level > 0) sb.Append(' ').Append(sl.SkillId).Append('@').Append(sl.Level);
            sb.Append(" map");
            foreach (var kv in _entityOf) sb.Append(' ').Append(kv.Key).Append("=>").Append(kv.Value);
            return sb.ToString();
        }

        public ulong MyCharacter { get { return _myCharacter; } }

        /// <summary>The server ended the match: the controller stops (the menu shows the result); the arena stays as it was until the player goes back.</summary>
        void OnDungeonEnded()
        {
            _dungeonEnded = true;
            if (_phase != Phase.Idle) _phase = Phase.Idle;
            if (_reviveRoot != null) _reviveRoot.gameObject.SetActive(false);
            if (_leaveButton != null) _leaveButton.gameObject.SetActive(false);
            _auto = false;
            Refresh();
        }

        // ---------------------------------------------------------------------------------------------------------------- the monsters

        void AddEnemySpawn(EntitySpawn e)
        {
            if (!_dungeon || village == null) return;
            string key = "e" + e.EntityId;
            if (_dummyKey.ContainsKey(e.EntityId))
            {
                // the spawn of the same boss again: it evolved (docs 15.30) and has the parts and the name of its next form
                if (e.Kind == EntityKind.Boss) { village.ReskinEnemy(key, e.Name, (int)e.PartHead, (int)e.PartBody, (int)e.PartLeg); _enemyLabel[e.EntityId] = e.Name; }
                return;
            }
            _dummyKey[e.EntityId] = key;
            _dummyId[key] = e.EntityId;
            _enemyLabel[e.EntityId] = e.Name;
            float s = _arenaScale;
            if (e.Kind == EntityKind.Boss) _bossIds.Add(e.EntityId);
            village.AddEnemy(key, e.Name, (int)e.PartHead, (int)e.PartBody, (int)e.PartLeg, e.Kind == EntityKind.Boss, new Vector2(e.X * s, e.Y * s));
        }

        /// <summary>Every frame of the dungeon: the monsters, the AI characters and the main character (only when the server moved it) are where the server says.</summary>
        void ApplyDungeonStates(MatchFrame f)
        {
            float s = _arenaScale;
            foreach (var st in f.States)
            {
                string key;
                if (_dummyKey.TryGetValue(st.EntityId, out key))
                {
                    if (st.Alive) village.SetEnemy(key, new Vector2(st.X * s, st.Y * s), st.DirX, st.HpMax > 0 ? (float)((double)st.Hp / st.HpMax) : 0f);
                    continue;
                }
                ulong who;
                if (!_characterOf.TryGetValue(st.EntityId, out who)) continue;
                if (st.Alive && village.IsGhost(who) && !(_debugHoldDead && who == _myCharacter)) village.Revive(who);
                if (who != _myCharacter && st.Alive) village.SetRemote(who, new Vector2(st.X * s, st.Y * s), st.DirX);
            }
            foreach (uint id in f.Despawns)
            {
                string key;
                if (_dummyKey.TryGetValue(id, out key)) village.RemoveEnemy(key);
            }
            _revivesLeft = f.RevivesLeft;
        }

        // ---------------------------------------------------------------------------------------------------------------- damage and effects

        /// <summary>The blows of the monsters on the characters (numbers, the injured pose) and the attack poses of the monsters.</summary>
        void OnDamageDungeon(DamageEvent d)
        {
            string attackerKey;
            ulong victim;
            Vector2 at = Vector2.zero;
            bool victimIsMine = _characterOf.TryGetValue(d.TargetId, out victim);
            if (victimIsMine) PartyPosition(victim, out at);
            if (_dummyKey.TryGetValue(d.SourceId, out attackerKey)) EnemyAttack(d.SourceId, d.SkillId, victimIsMine ? at.x : float.NaN);
            if (victimIsMine)
            {
                string text = d.Absorbed ? "Né" : "-" + Money(d.Amount);
                Color c = d.Absorbed ? new Color(0.8f, 0.95f, 1f) : d.TargetId == _myId ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.55f, 0.3f);
                Float(text, at + new Vector2(UnityEngine.Random.Range(-18f, 18f), 96f * _arenaScale / 2.2f), c, d.TargetId == _myId ? 24 : 18, 0.9f);
                if (!d.Absorbed && !village.IsActing(victim) && !village.IsLocked(victim)) village.PlayAction(victim, new[] { (int)NroPose.Hit_Injured }, 0.25f);
            }
        }

        bool PartyPosition(ulong who, out Vector2 feet)
        {
            feet = Vector2.zero;
            for (int i = 0; i < village.PartyCount; i++)
            {
                ulong id;
                int facing;
                Vector2 f;
                if (village.PartyMember(i, out id, out f, out facing) && id == who) { feet = f; return true; }
            }
            return false;
        }

        /// <summary>A monster attacks: the poses of its skill at level 1 (the Nro paint of the punch or of the chuong).</summary>
        void EnemyAttack(uint entityId, uint skillId, float faceX)
        {
            string key;
            if (!_dummyKey.TryGetValue(entityId, out key)) return;
            string gate = entityId + ":" + skillId;
            float last;
            if (_lastAction.TryGetValue(gate, out last) && Time.unscaledTime - last < 0.4f) return;
            _lastAction[gate] = Time.unscaledTime;
            var picked = BbSkillPoses.UsePicked(skillId, 1u, true);
            if (picked.poses.Length == 0) return;
            village.EnemyAction(key, picked.poses, BbSkillPoses.Seconds(picked.poses), faceX);
            if (skillId == 1 || skillId == 3 || skillId == 5) SkillSound(skillId);
        }

        /// <summary>
        /// A boss casts a skill of the Xayda (docs 15.30): skill 14 is the charge of the evolution (the self-destruct pose and a pulsing glow, no blast), 23 Trói, 21 Huýt sáo
        /// (the whistle effect over it), 8 Tái tạo năng lượng.
        /// </summary>
        void OnEnemyCast(EffectEvent ef)
        {
            string key;
            Vector2 at;
            float h;
            if (!_dummyKey.TryGetValue(ef.SourceId, out key) || !village.DummyInfo(key, out at, out h)) return;
            float seconds = ef.DurationMs / 1000f;
            if (ef.SkillId == 14)
            {
                EnsureFx();
                int fxFrom, paint;
                bool fly;
                var poses = BbSkillPoses.Charge(14u, 1u, true, true, Mathf.Max(0.5f, seconds), out fxFrom, out paint, out fly);
                village.EnemyAction(key, poses, Mathf.Max(0.5f, seconds), float.NaN);
                village.SetEnemyCharge(key, seconds);
                ShowBanner("Khỉ đột gồng!", new Color(1f, 0.85f, 0.4f), 1.4f);
                return;
            }
            float faceX = float.NaN;
            ulong target;
            Vector2 where;
            if (ef.TargetId != ef.SourceId && _characterOf.TryGetValue(ef.TargetId, out target) && PartyPosition(target, out where)) faceX = where.x;
            EnemyAttack(ef.SourceId, ef.SkillId, faceX);
            if (ef.SkillId == 21) { EnsureFx(); _fx.PlayData(39, at, 3); }
        }

        /// <summary>A monster shoots (the server's projectile of the monster appears): the pose of the chuong, turned the way the shot goes.</summary>
        void EnemyShot(uint entityId, uint skillId, float dirX)
        {
            string key;
            Vector2 at;
            float h;
            if (!_dummyKey.TryGetValue(entityId, out key) || !village.DummyInfo(key, out at, out h)) return;
            EnemyAttack(entityId, skillId, at.x + (dirX >= 0f ? 1000f : -1000f));
        }

        /// <summary>The effects that only a dungeon has. True when the effect was one of them.</summary>
        bool OnDungeonEffect(EffectEvent ef)
        {
            switch (ef.Effect)
            {
                case Effect.Wave:
                    ShowBanner("Đợt " + ef.SkillId + "/" + (int)ef.Length, new Color(1f, 0.75f, 0.35f), 1.6f);
                    return true;
                case Effect.BossComing:
                {
                    string name;
                    _enemyLabel.TryGetValue(ef.TargetId, out name);
                    ShowBanner("BOSS xuất hiện!" + (string.IsNullOrEmpty(name) ? "" : "\n" + name), new Color(1f, 0.35f, 0.3f), 2.4f);
                    BbSfx.Play(BbSfx.Gong, 0.7f);
                    return true;
                }
                case Effect.Summon:
                {
                    string key;
                    Vector2 at;
                    float h;
                    if (_dummyKey.TryGetValue(ef.TargetId, out key) && village.DummyInfo(key, out at, out h)) { EnsureFx(); _fx.PlayHit(60, at); }
                    return true;
                }
                case Effect.Died:
                    OnDied(ef);
                    return true;
                case Effect.CastEnd:
                    if (ef.SkillId != 0 || ef.SourceId != ef.TargetId || !_dummyKey.ContainsKey(ef.SourceId)) return false;
                    // a boss was stunned (put to sleep, sealed...): the red zone of the blow it was preparing goes away
                    for (int i = _telegraphs.Count - 1; i >= 0; i--)
                        if (_telegraphs[i].source == ef.SourceId) { if (_telegraphs[i].rect != null) Kill(_telegraphs[i].rect.gameObject); _telegraphs.RemoveAt(i); }
                    return true;
                case Effect.CastStart:
                    if (!_dummyKey.ContainsKey(ef.SourceId)) return false;
                    OnEnemyCast(ef);                                  // a boss uses a skill of the Xayda: the charge to evolve, Trói, Huýt sáo, Tái tạo năng lượng
                    return true;
                case Effect.BossEvolve:
                {
                    string ekey;
                    Vector2 eat;
                    float eh;
                    if (_dummyKey.TryGetValue(ef.TargetId, out ekey))
                    {
                        village.SetEnemyCharge(ekey, 0f);
                        if (village.DummyInfo(ekey, out eat, out eh)) { EnsureFx(); _fx.PlayHit(60, eat); }
                    }
                    string ename;
                    _enemyLabel.TryGetValue(ef.TargetId, out ename);
                    ShowBanner((string.IsNullOrEmpty(ename) ? "Tiến hóa" : ename) + "!\n(" + ef.SkillId + "/" + (int)ef.Length + ")", new Color(1f, 0.55f, 0.2f), 2.2f);
                    BbSfx.Play(BbSfx.Gong, 0.7f);
                    return true;
                }
                case Effect.Hold:
                {
                    // a boss ties one of the characters with Trói: the rope of the skill on it
                    ulong tied;
                    if (_dummyKey.ContainsKey(ef.SourceId) && _characterOf.TryGetValue(ef.TargetId, out tied))
                    {
                        Vector2 where;
                        if (PartyPosition(tied, out where)) { EnsureFx(); _fx.PlayData(32, where + new Vector2(0f, -24f * village.UiScale), 0, ef.DurationMs / 1000f); }
                    }
                    return false;
                }
                case Effect.TelegraphRect:
                case Effect.TelegraphCircle:
                    AddTelegraph(ef);
                    return true;
            }
            return false;
        }

        void OnDied(EffectEvent ef)
        {
            string key;
            Vector2 away = new Vector2(ef.DirX, ef.DirY);
            for (int i = _telegraphs.Count - 1; i >= 0; i--)
                if (_telegraphs[i].source == ef.TargetId) { if (_telegraphs[i].rect != null) Kill(_telegraphs[i].rect.gameObject); _telegraphs.RemoveAt(i); }
            if (_dummyKey.TryGetValue(ef.TargetId, out key))
            {
                village.KillEnemy(key, away);
                return;
            }
            ulong who;
            if (_characterOf.TryGetValue(ef.TargetId, out who))
            {
                village.DieParty(who, away);
                if (_fx != null) _fx.CancelCharacter(who);
                if (_ultimate != null) _ultimate.Cancel(who);
                if (who == _myCharacter) { _auto = false; _pendingSlot = -1; _reviveAskedAt = 0f; village.CancelApproach(); }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- the boss telegraphs

        /// <summary>The area a boss attack will hit after a moment (Effect TELEGRAPH_RECT or TELEGRAPH_CIRCLE): a red zone that pulses until it lands.</summary>
        void AddTelegraph(EffectEvent ef)
        {
            var world = village.WorldLayer;
            if (world == null) return;
            float s = _arenaScale;
            var t = new Telegraph { source = ef.SourceId, start = Time.unscaledTime, until = Time.unscaledTime + Mathf.Max(0.3f, ef.DurationMs / 1000f) };
            bool rect = ef.Effect == Effect.TelegraphRect;
            t.image = NewImage("Telegraph", world, rect ? whiteSprite : (glowSprite != null ? glowSprite : whiteSprite), new Color(1f, 0.15f, 0.1f, 0.3f));
            t.rect = t.image.rectTransform;
            if (rect)
            {
                t.rect.pivot = new Vector2(0f, 0.5f);
                t.rect.anchoredPosition = new Vector2(ef.X * s, ef.Y * s);
                t.rect.sizeDelta = new Vector2(ef.Length * s, ef.Width * s);
                t.rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(ef.DirY, ef.DirX) * Mathf.Rad2Deg);
            }
            else
            {
                t.rect.pivot = new Vector2(0.5f, 0.5f);
                t.rect.anchoredPosition = new Vector2(ef.X * s, ef.Y * s);
                t.rect.sizeDelta = new Vector2(ef.Length * 2f * s, ef.Length * 2f * s);
            }
            _telegraphs.Add(t);
        }

        void UpdateTelegraphs()
        {
            for (int i = _telegraphs.Count - 1; i >= 0; i--)
            {
                var t = _telegraphs[i];
                float now = Time.unscaledTime;
                if (t.image == null || now >= t.until) { if (t.rect != null) Kill(t.rect.gameObject); _telegraphs.RemoveAt(i); continue; }
                float k = (now - t.start) / Mathf.Max(0.1f, t.until - t.start);
                float pulse = 0.5f + 0.5f * Mathf.Sin(now * (6f + 10f * k));
                t.image.color = new Color(1f, 0.15f, 0.1f, 0.18f + 0.30f * k * (0.6f + 0.4f * pulse));          // it grows more urgent as it is about to land
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- the player's report

        /// <summary>Every 100 ms: where the main character is and the part of the arena the screen shows (the AI fight only what is on it).</summary>
        void ReportDungeon(float dt)
        {
            ulong main;
            Vector2 feet;
            int facing;
            if (!village.PartyMember(0, out main, out feet, out facing)) return;
            _reportClock += dt;
            if (_reportClock < ReportEvery) return;
            _reportClock = 0f;
            float s = _arenaScale;
            var report = new PlayerStateReport { Seq = ++_seq, X = feet.x / s, Y = feet.y / s, DirX = facing, DirY = 0f };
            Rect view;
            if (village.ViewRect(out view)) { report.ViewX = view.x; report.ViewY = view.y; report.ViewW = view.width; report.ViewH = view.height; }
            try { BbSession.Client.Send(new Envelope { PlayerStateReport = report }); } catch (Exception) { }
        }

        // ---------------------------------------------------------------------------------------------------------------- the dungeon's own screen parts

        void EnsureDungeonUi()
        {
            if (_banner != null) return;
            _banner = NewText("Banner", _root, 44, new Color(1f, 0.92f, 0.45f), TextAnchor.MiddleCenter);
            _banner.rectTransform.anchorMin = _banner.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            _banner.rectTransform.pivot = new Vector2(0.5f, 1f);
            _banner.rectTransform.anchoredPosition = new Vector2(0f, -230f);
            _banner.rectTransform.sizeDelta = new Vector2(900f, 120f);
            _banner.horizontalOverflow = HorizontalWrapMode.Wrap;
            _banner.gameObject.SetActive(false);

            // leave: a small button at the top right; two taps (the second within 3 seconds) so that it is not pressed by mistake
            var leave = NewImage("Leave", _root, skillFrame, Color.white);
            leave.raycastTarget = true;
            leave.rectTransform.anchorMin = leave.rectTransform.anchorMax = new Vector2(1f, 1f);
            leave.rectTransform.pivot = new Vector2(1f, 1f);
            leave.rectTransform.anchoredPosition = new Vector2(-14f, -14f);
            leave.rectTransform.sizeDelta = new Vector2(150f, 52f);
            _leaveButton = leave.gameObject.AddComponent<Button>();
            _leaveButton.targetGraphic = leave;
            _leaveLabel = NewText("Label", leave.transform, 18, Color.white, TextAnchor.MiddleCenter);
            Stretch(_leaveLabel.rectTransform, 0f);
            _leaveLabel.text = "Rời phó bản";
            _leaveButton.onClick.AddListener(OnLeavePressed);

            // the box that offers to revive the main character
            _reviveRoot = NewRect("Revive", _root);
            _reviveRoot.anchorMin = _reviveRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _reviveRoot.pivot = new Vector2(0.5f, 0.5f);
            _reviveRoot.sizeDelta = new Vector2(520f, 220f);
            _reviveRoot.anchoredPosition = new Vector2(0f, 30f);
            var back = NewImage("Back", _reviveRoot, whiteSprite, new Color(0.05f, 0.04f, 0.08f, 0.82f));
            back.rectTransform.anchorMin = Vector2.zero;
            back.rectTransform.anchorMax = Vector2.one;
            back.rectTransform.offsetMin = back.rectTransform.offsetMax = Vector2.zero;
            _reviveText = NewText("Text", _reviveRoot, 24, Color.white, TextAnchor.MiddleCenter);
            _reviveText.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            _reviveText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _reviveText.rectTransform.offsetMin = new Vector2(10f, 0f);
            _reviveText.rectTransform.offsetMax = new Vector2(-10f, -8f);
            _reviveText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var revive = NewImage("Button", _reviveRoot, skillFrame, Color.white);
            revive.raycastTarget = true;
            revive.rectTransform.anchorMin = revive.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            revive.rectTransform.pivot = new Vector2(0.5f, 0f);
            revive.rectTransform.anchoredPosition = new Vector2(0f, 20f);
            revive.rectTransform.sizeDelta = new Vector2(260f, 70f);
            _reviveButton = revive.gameObject.AddComponent<Button>();
            _reviveButton.targetGraphic = revive;
            var label = NewText("Label", revive.transform, 24, Color.white, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 0f);
            label.text = "Hồi sinh";
            _reviveButton.onClick.AddListener(OnRevivePressed);
            _reviveRoot.gameObject.SetActive(false);
        }

        void ShowBanner(string text, Color color, float seconds)
        {
            if (_banner == null) return;
            _banner.text = text;
            _banner.color = color;
            _bannerFrom = Time.unscaledTime;
            _bannerUntil = Time.unscaledTime + seconds;
            _banner.gameObject.SetActive(true);
        }

        void OnLeavePressed()
        {
            if (!_dungeon || _dungeonEnded) return;
            if (Time.unscaledTime > _leaveArmedUntil)
            {
                _leaveArmedUntil = Time.unscaledTime + 3f;
                _leaveLabel.text = "Chạm lại để rời";
                return;
            }
            _leaveArmedUntil = 0f;
            _leaveLabel.text = "Đang rời...";
            _leaveButton.interactable = false;
            if (menu != null) menu.LeaveDungeonNow();
        }

        void OnRevivePressed()
        {
            if (!_dungeon || _dungeonEnded) return;
            try { BbSession.Client.Send(new Envelope { ReviveRequest = new ReviveRequest() }); } catch (Exception) { }
            _reviveAskedAt = Time.unscaledTime;
        }

        /// <summary>Every frame of the dungeon: the banner, the telegraphs, the box that offers a revive, the leave button.</summary>
        void UpdateDungeon()
        {
            if (!_dungeon) return;
            UpdateTelegraphs();
            if (_banner != null && _banner.gameObject.activeSelf)
            {
                float now = Time.unscaledTime;
                if (now >= _bannerUntil) _banner.gameObject.SetActive(false);
                else
                {
                    float t = now - _bannerFrom, left = _bannerUntil - now;
                    var c = _banner.color;
                    c.a = Mathf.Clamp01(Mathf.Min(t / 0.2f, left / 0.5f));
                    _banner.color = c;
                    float pop = 1f + 0.25f * Mathf.Clamp01(1f - t / 0.25f);
                    _banner.rectTransform.localScale = new Vector3(pop, pop, 1f);
                }
            }
            if (_leaveButton != null && _leaveButton.gameObject.activeSelf && _leaveLabel != null && _leaveArmedUntil > 0f && Time.unscaledTime > _leaveArmedUntil)
            {
                _leaveArmedUntil = 0f;
                _leaveLabel.text = "Rời phó bản";
            }
            // the main character lies as a ghost: offer a revive while there are revives left
            bool dead = village != null && !_dungeonEnded && village.IsGhost(_myCharacter);
            if (_reviveRoot != null)
            {
                if (_reviveRoot.gameObject.activeSelf != dead) _reviveRoot.gameObject.SetActive(dead);
                if (dead)
                {
                    bool can = _revivesLeft > 0;
                    _reviveText.text = "Bạn đã gục!\n" + (can ? "Còn " + _revivesLeft + " lần hồi sinh (tốn vàng)" : "Đã hết lượt hồi sinh");
                    _reviveButton.interactable = can && Time.unscaledTime - _reviveAskedAt > 1.2f;
                    if (_reviveAskedAt > 0f && Time.unscaledTime - _reviveAskedAt > 1.2f && Time.unscaledTime - _reviveAskedAt < 1.4f && menu != null) menu.Toast("Chưa hồi sinh được (không đủ vàng?)");
                }
            }
        }
    }
}
