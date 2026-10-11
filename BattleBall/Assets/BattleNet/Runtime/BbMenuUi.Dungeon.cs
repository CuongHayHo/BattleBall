using System;
using System.Text;
using BattleBall.Proto;
using UnityEngine;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// The way into a dungeon and back (owner request 2026-10-11, docs/architecture/BattleBallServer.md 15.25, stage 1): the NPC Cui lists the dungeons, the player chooses one, the
    /// ship of <see cref="BbTransport"/> flies while the server starts the match, then the dungeon is shown; at the end the result is shown and the ship flies back to the village.
    /// Until the dungeon scene exists (stages 2 to 4) the dungeon is a plain screen with the button "Rời phó bản".
    /// </summary>
    public sealed partial class BbMenuUi
    {
        [Header("Dungeons (the NPC Cui)")]
        public BbDungeonPicker dungeonPicker;
        public BbTransport transport;

        enum DungeonPhase { None, Flying, InDungeon, Leaving, Returning }

        DungeonPhase _dungeon;
        MatchStarted _dungeonStarted;
        bool _dungeonSubscribed, _panelBefore;
        ulong _dungeonMain, _dungeonPartner;
        string _dungeonTier = "early";

        /// <summary>True from the moment the player chose a dungeon until the ship has brought him back to the village.</summary>
        public bool InDungeon { get { return _dungeon != DungeonPhase.None; } }

        /// <summary>The skill buttons and the like stay hidden while the ship flies or the match is being closed; during the fight they are shown.</summary>
        public bool DungeonBlocksCombatUi { get { return _dungeon == DungeonPhase.Flying || _dungeon == DungeonPhase.Leaving || _dungeon == DungeonPhase.Returning; } }

        void WireDungeon()
        {
            if (dungeonPicker != null) dungeonPicker.Entered = EnterDungeon;
        }

        /// <summary>The player talks to Cui: the dungeons of the server in a box in the middle of the screen.</summary>
        void OpenDungeonList(NpcSpec npc)
        {
            string team;
            bool ok = DungeonTeam(out team);
            dungeonPicker.Show(npc.Name, _snapshot, team, ok && BbSession.IsActive && !InDungeon);
        }

        ulong DungeonMain(out ulong partner)
        {
            partner = 0;
            ulong main = 0;
            Vector2 feet;
            int facing;
            if (village == null || !village.PartyMember(0, out main, out feet, out facing)) return 0;
            if (FusionMainCharacterId == main) partner = FusionSubCharacterId;
            return main;
        }

        string RaceTitle(ulong characterId, out uint level)
        {
            level = 0;
            if (_snapshot == null) return "?";
            foreach (var c in _snapshot.Characters)
            {
                if (c.CharacterId != characterId) continue;
                level = c.Level;
                int i = Array.IndexOf(Races, c.Race);
                return i >= 0 ? Titles[i] : c.Race;
            }
            return "?";
        }

        /// <summary>Who goes into the dungeon (the character the village follows and the fusion partner), as the line of the list says.</summary>
        bool DungeonTeam(out string team)
        {
            ulong partner;
            ulong main = DungeonMain(out partner);
            if (main == 0) { team = "Chưa có nhân vật."; return false; }
            uint lm, lp;
            string tm = RaceTitle(main, out lm);
            if (partner != 0)
            {
                string tp = RaceTitle(partner, out lp);
                team = "Đội: Hợp thể " + tm + " cấp " + lm + " + " + tp + " cấp " + lp + ", cùng 1 AI";
            }
            else
            {
                team = "Đội: " + tm + " cấp " + lm + ", cùng 2 AI";
            }
            return true;
        }

        async void EnterDungeon(DungeonInfo d)
        {
            if (InDungeon || !BbSession.IsActive) return;
            ulong partner;
            ulong main = DungeonMain(out partner);
            if (main == 0) return;
            _dungeon = DungeonPhase.Flying;
            _dungeonMain = main;
            _dungeonPartner = partner;
            _dungeonTier = string.IsNullOrEmpty(d.Tier) ? "early" : d.Tier;
            dungeonPicker.HideQuietly();
            if (popup.IsOpen) popup.Hide();
            if (training != null) training.SetSuspended(true);               // the village session closes by itself on the server when the dungeon starts
            transport.Begin("Đang bay tới " + d.Name + "...", OnDungeonFailed);
            try
            {
                var started = await BbSession.Client.StartMatchAsync(new StartMatchRequest { DungeonId = d.DungeonId, ControlledCharacterId = main, FusionPartnerId = partner, Difficulty = dungeonPicker.SelectedDifficulty });
                if (this == null) return;
                _dungeonStarted = started;
                BbSession.DungeonToken = started.MatchToken;
                SubscribeDungeon();
                transport.Complete(ShowDungeon);
            }
            catch (Exception e)
            {
                if (this == null) return;
                transport.Fail(e.Message);
            }
        }

        /// <summary>The connection was lost in a dungeon and came back (BbLoginUi gave the match token to the server): the dungeon goes on where it stopped.</summary>
        void ResumeDungeonAfterDrop()
        {
            var m = BbSession.ResumedMatch;
            BbSession.ResumedMatch = null;
            if (m == null || InDungeon) return;
            ulong main = 0, partner = 0;
            foreach (var e in m.Entities) if (e.Controlled) { main = e.CharacterId; partner = e.PartnerCharacterId; }
            // the menu starts again without the fusion (it is the menu's own state): the match says which characters are merged, and which tab is the one under control
            RestoreTeamAfterDrop(m, ref main);
            _dungeon = DungeonPhase.Flying;
            _dungeonMain = main;
            _dungeonPartner = partner;
            _dungeonTier = "early";
            if (_snapshot != null) foreach (var d in _snapshot.Dungeons) if (d.DungeonId == m.DungeonId && !string.IsNullOrEmpty(d.Tier)) _dungeonTier = d.Tier;
            _dungeonStarted = m;
            BbSession.DungeonToken = m.MatchToken;
            if (training != null) training.SetSuspended(true);
            SubscribeDungeon();
            transport.Begin("Đang vào lại phó bản...", OnDungeonFailed);
            transport.Complete(ShowDungeon);
        }

        /// <summary>
        /// A match came back after a lost connection: the fusion of the team (a fused fighter carries its partner) and the character under control (the player may have taken
        /// over another one meanwhile) are shown again by the tabs, and the village party behind the arena is the same.
        /// </summary>
        void RestoreTeamAfterDrop(MatchStarted m, ref ulong main)
        {
            if (_snapshot == null || _pages.Count == 0) return;
            foreach (var e in m.Entities)
            {
                if (e.Kind != EntityKind.PlayerCharacter && e.Kind != EntityKind.AllyAi) continue;
                if (e.PartnerCharacterId == 0) continue;
                int mainRace = RaceIndexOf(e.CharacterId), subRace = RaceIndexOf(e.PartnerCharacterId);
                if (mainRace >= 0 && subRace >= 0 && (_fusionMain != mainRace || _fusionSub != subRace)) Fuse(mainRace, subRace, false);
            }
            ShowControlledPage(main);
        }

        int RaceIndexOf(ulong characterId)
        {
            foreach (var c in _snapshot.Characters) if (c.CharacterId == characterId) return Array.IndexOf(Races, c.Race);
            return -1;
        }

        /// <summary>The tab of the character under control lights up (the fusion form's tab for a fused main character), without taking over anything.</summary>
        void ShowControlledPage(ulong character)
        {
            int target = -1;
            for (int i = 0; i < _pages.Count; i++)
            {
                var pg = _pages[i];
                if (pg.main < 0) continue;
                var ch = CharacterOf(Races[pg.main]);
                if (ch == null || ch.CharacterId != character) continue;
                if (pg.fusion) { target = i; break; }
                if (target < 0) target = i;
            }
            if (target >= 0 && target != _page) { _page = target; ShowPage(_page, false); }
        }

        void OnDungeonFailed(string message)
        {
            _dungeon = DungeonPhase.None;
            if (training != null) training.SetSuspended(false);
            Toast("Không vào được phó bản: " + message);
        }

        void SubscribeDungeon()
        {
            if (_dungeonSubscribed || !BbSession.IsActive) return;
            BbSession.Client.EventReceived += OnDungeonEvent;
            _dungeonSubscribed = true;
        }

        void UnsubscribeDungeon()
        {
            if (!_dungeonSubscribed) return;
            if (BbSession.Client != null) BbSession.Client.EventReceived -= OnDungeonEvent;
            _dungeonSubscribed = false;
        }

        void OnDungeonEvent(Envelope e)
        {
            if (e.PayloadCase == Envelope.PayloadOneofCase.MatchEnded && (_dungeon == DungeonPhase.InDungeon || _dungeon == DungeonPhase.Leaving))
            {
                BbSession.DungeonToken = null;                         // over: nothing to come back to
                ShowDungeonResult(e.MatchEnded);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- the dungeon

        void ShowDungeon()
        {
            _dungeon = DungeonPhase.InDungeon;
            _panelBefore = _panelOpen;
            SetPanelOpen(false, true, false);                              // the menu is out of the way; the orange triangle is hidden in a dungeon
            if (training != null) training.BeginDungeon(_dungeonStarted, _dungeonMain, _dungeonPartner, _dungeonTier);
        }

        /// <summary>The button of the dungeon screen: the player gives up (the server settles what was done and sends the result).</summary>
        public void LeaveDungeonNow()
        {
            if (_dungeon != DungeonPhase.InDungeon) return;
            _dungeon = DungeonPhase.Leaving;
            try { BbSession.Client.Send(new Envelope { LeaveMatchRequest = new LeaveMatchRequest() }); } catch (Exception) { }
        }

        static string Duration(uint seconds)
        {
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        /// <summary>The result of the match (a win, a defeat or leaving): what it brought, and the button that sends the ship back.</summary>
        void ShowDungeonResult(MatchEnded ended)
        {
            var sb = new StringBuilder();
            string verdict = ended.Result == MatchResult.Won ? "Chiến thắng!" : ended.Result == MatchResult.Lost ? "Thất bại" : ended.Result == MatchResult.Abandoned ? "Đã rời phó bản" : "Hết hạn";
            sb.Append(verdict).Append("\nBoss đã hạ: ").Append(ended.BossesDefeated).Append("/").Append(ended.BossCount).Append("   Thời gian: ").Append(Duration(ended.DurationS));
            sb.Append("\nVàng: ").Append(ended.Gold.ToString("N0"));
            if (ended.Gems > 0) sb.Append("   Đá quý: ").Append(ended.Gems);
            foreach (var c in ended.Characters)
            {
                uint level;
                string name = RaceTitle(c.CharacterId, out level);
                sb.Append("\n").Append(name).Append(": +").Append(c.ExpGained.ToString("N0")).Append(" EXP");
                if (c.LevelAfter > c.LevelBefore) sb.Append("  lên cấp ").Append(c.LevelAfter);
            }
            if (ended.Drops.Count > 0) sb.Append("\nVật phẩm rơi: ").Append(ended.Drops.Count);
            if (ended.FirstClear) sb.Append("\nLần đầu hạ boss cuối!");
            var specs = new System.Collections.Generic.List<BbPopup.ButtonSpec> { new BbPopup.ButtonSpec("Về làng", true, true) };
            popup.centerOnScreen = true;
            popup.Show(null, _dungeonStarted != null ? _dungeonStarted.DungeonName : "Phó bản", "", sb.ToString(), null, null, specs, i => popup.Hide(), ReturnToVillage);
        }

        void ReturnToVillage()
        {
            if (_dungeon == DungeonPhase.Returning || _dungeon == DungeonPhase.None) return;
            _dungeon = DungeonPhase.Returning;
            BbSession.DungeonToken = null;
            UnsubscribeDungeon();
            transport.Begin("Đang bay về làng...", null);
            transport.Complete(() =>
            {
                if (training != null) { training.EndDungeon(); training.SetSuspended(false); }
                SetPanelOpen(_panelBefore, true, false);
            }, () => { _dungeon = DungeonPhase.None; _dungeonStarted = null; });
        }

        // ---------------------------------------------------------------------------------------------------------------- for the play scenario

        /// <summary>For the play scenario: what the screen shows now.</summary>
        public string DebugDungeon()
        {
            return _dungeon + (transport != null && transport.IsPlaying ? " (ship)" : "") + (village != null && village.InArena ? " (arena)" : "") + (dungeonPicker != null && dungeonPicker.IsOpen ? " (list)" : "");
        }

        /// <summary>For the play scenario: the difficulty of the dungeon being run.</summary>
        public string DebugDifficulty { get { return _dungeonStarted == null ? "" : _dungeonStarted.Difficulty; } }

        public void DebugOpenDungeonList() { OpenDungeonList(new NpcSpec { Id = "cui", Name = "Cui" }); }
        public void DebugEnterDungeon(uint id, string name) { EnterDungeon(new DungeonInfo { DungeonId = id, Name = name }); }
        public void DebugLeaveDungeon() { LeaveDungeonNow(); }
        public void DebugSelectRace(int race) { SelectPage(PageOfRace(race)); }
        public string DebugPage() { return "tab " + _page + (_pages.Count > _page && _pages[_page].fusion ? " (fusion)" : "") + ", fusion " + _fusionMain + "+" + _fusionSub + ", toast '" + (toastText.gameObject.activeSelf ? toastText.text : "") + "'"; }
        public void DebugResultButton() { if (popup.IsOpen) popup.Hide(); }
    }
}
