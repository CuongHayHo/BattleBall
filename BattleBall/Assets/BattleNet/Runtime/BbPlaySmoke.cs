using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BattleBall.Proto;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattleBall.Net
{
    /// <summary>
    /// A scripted run of the real game in Play mode, against the local server, for the checks that screenshots of one frame cannot do (owner request 2026-10-09:
    /// "chạy Play mode"). Started by the Editor method BbPlaySmokeEditor.Enter, which passes -bbPlaySmoke; nothing happens in a normal run. It registers a test
    /// account, waits for the gold to be put on it from outside (Logs/play_go.txt), learns the skills it needs, loads the menu, lets the AI fight, focuses the far
    /// dummy and uses skills while writing what the server and the pictures do to Logs/play_smoke.txt and Logs/play_*.png.
    /// </summary>
    public sealed class BbPlaySmoke : MonoBehaviour
    {
        StringBuilder _log = new StringBuilder();
        string _dir;
        float _t0;

        static bool HasArg(string name)
        {
            foreach (var a in Environment.GetCommandLineArgs()) if (a == name) return true;
            return false;
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (!HasArg("-bbPlaySmoke")) return;
            var go = new GameObject("BbPlaySmoke");
            DontDestroyOnLoad(go);
            go.AddComponent<BbPlaySmoke>();
        }

        void Log(string text)
        {
            string line = "[" + (Time.realtimeSinceStartup - _t0).ToString("0.00") + "] " + text;
            _log.AppendLine(line);
            File.WriteAllText(Path.Combine(_dir, "play_smoke.txt"), _log.ToString());
        }

        async void Start()
        {
            _t0 = Time.realtimeSinceStartup;
            _dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            Directory.CreateDirectory(_dir);
            Application.logMessageReceived += (msg, stack, type) =>
            {
                if (msg.StartsWith("[SkillFx]") || type == LogType.Exception || type == LogType.Error) Log("unity " + type + ": " + msg);
            };
            try { await Run(); Log("DONE"); }
            catch (Exception e) { Log("FAILED " + e); }
            Finish();
        }

        void Finish()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }

        async Task Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) await Task.Yield();
        }

        async Task<bool> WaitFor(Func<bool> cond, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) { if (cond()) return true; await Task.Yield(); }
            return cond();
        }

        void Shoot(string name)
        {
            try
            {
                var canvas = FindObjectOfType<Canvas>();
                var cam = canvas != null ? canvas.worldCamera : Camera.main;
                if (cam == null) { Log("no camera for " + name); return; }
                int w = 1280, h = 720;
                var old = cam.targetTexture;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                Canvas.ForceUpdateCanvases();
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(_dir, "play_" + name + ".png"), tex.EncodeToPNG());
                RenderTexture.active = null;
                cam.targetTexture = old;
                Destroy(rt);
                Destroy(tex);
            }
            catch (Exception e) { Log("screenshot " + name + " failed: " + e.Message); }
        }

        string Where(BbVillageView v)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < v.PartyCount; i++)
            {
                ulong id; Vector2 feet; int facing;
                v.PartyMember(i, out id, out feet, out facing);
                sb.Append(i == 0 ? "main " : "ai" + i + " ").Append(Mathf.RoundToInt(feet.x)).Append(',').Append(Mathf.RoundToInt(feet.y)).Append(i == 0 ? "; " : "; ");
            }
            foreach (string key in v.DummyKeys)
            {
                Vector2 at; float h;
                if (v.DummyInfo(key, out at, out h)) sb.Append(key).Append(' ').Append(Mathf.RoundToInt(at.x)).Append(',').Append(Mathf.RoundToInt(at.y)).Append("; ");
            }
            return sb.ToString();
        }

        /// <summary>Watches {@code seconds}: every 0.1 s the darts on the screen and what the server reported (projectiles and hits), and some screenshots.</summary>
        async Task Observe(string name, float seconds, BbVillageView village, params float[] shotsAt)
        {
            float start = Time.realtimeSinceStartup, nextLog = 0f;
            int shot = 0;
            Log("--- " + name + " (" + Where(village) + ")");
            while (Time.realtimeSinceStartup - start < seconds)
            {
                float t = Time.realtimeSinceStartup - start;
                if (t >= nextLog)
                {
                    nextLog += 0.1f;
                    var sb = new StringBuilder("t=" + t.ToString("0.0") + " darts:");
                    foreach (var d in FindObjectsOfType<BbDart>()) sb.Append(' ').Append(d.name).Append('@').Append(d.DebugHead);
                    foreach (var im in village.WorldLayer.GetComponentsInChildren<Image>())
                        if (im.enabled && (im.name == "SkillFx" || im.name == "SkillHit" || im.name == "Head" || im.name == "HeadBorder" || im.name == "Tail" || im.name == "Spark" || im.name == "Projectile")) sb.Append(' ').Append(im.name).Append(':').Append(im.sprite != null ? im.sprite.name : "-").Append('@').Append(Mathf.RoundToInt(im.rectTransform.anchoredPosition.x)).Append(',').Append(Mathf.RoundToInt(im.rectTransform.anchoredPosition.y));
                    if (_training != null && _training.DebugFlashOn) sb.Append(" WHITE");
                    sb.Append(" | server:").Append(_serverNote);
                    Log(sb.ToString());
                    _serverNote = "";
                }
                if (shot < shotsAt.Length && t >= shotsAt[shot]) { Shoot(name + "_" + shotsAt[shot].ToString("0.0")); shot++; }
                await Task.Yield();
            }
        }

        string _serverNote = "";

        void OnServer(Envelope e)
        {
            if (e.PayloadCase != Envelope.PayloadOneofCase.MatchFrame) return;
            foreach (var p in e.MatchFrame.Projectiles) _serverNote += " P" + p.ProjectileId + "(skill" + p.SkillId + " " + Mathf.RoundToInt(p.X) + "," + Mathf.RoundToInt(p.Y) + ")";
            foreach (var d in e.MatchFrame.Damages) _serverNote += " HIT(target" + d.TargetId + " by" + d.SourceId + " skill" + d.SkillId + " " + d.Amount + ")";
            foreach (var f in e.MatchFrame.Effects) _serverNote += " FX(" + f.Effect + " skill" + f.SkillId + ")";
        }

        BbTraining _training;

        /// <summary>The Earth skills the owner asked to compare with the original game (only these are learned, so that the AI do not use them first).</summary>
        async Task AuditEarth(BbMenuUi menu, BbTraining training, BbVillageView village)
        {
            menu.DemoSelect(0);                                              // Trái Đất is the main character (the saved page of an earlier run may be another)
            await Wait(2f);
            village.TeleportMain(1850f);                                      // next to the first dummy: Thái dương hạ san needs the enemy near
            await Wait(0.5f);
            await WaitFor(() => training.Active, 10f);
            training.Focus("moc_nhan");
            await Wait(1f);
            training.DebugUse(6);
            await Observe("solar", 3.5f, village, 0.1f, 0.25f, 0.4f, 0.6f, 0.9f, 1.4f, 2.0f, 2.8f);
            await Wait(2f);
            training.DebugUse(10);
            await Observe("spiritbomb", 6f, village, 0.4f, 1.0f, 2.2f, 3.4f, 4.2f, 4.8f, 5.4f);
            Log("final state: " + Where(village));
        }

        /// <summary>Urôn's shop with the real server: tap the NPC, the shop opens on the left and the menu moves to the right, a skin is bought, the shop closes and the menu comes back.</summary>
        async Task ShopRun(BbMenuUi menu, BbVillageView village)
        {
            var uron = village.Map.Npcs.Find(n => n.Id == "uron");
            if (uron == null) { Log("FAILED: no NPC uron on the map"); return; }
            village.TeleportMain(uron.X - 260f);
            await Wait(1.2f);
            Shoot("shop_village");
            var snap = BbSession.Snapshot;
            ulong gemsBefore = snap.Gems;
            int skinsBefore = snap.Skins.Count;
            float hitch = await OpenShopMeasured(menu, village, uron);
            bool opened = menu.shop.IsOpen;
            await Wait(0.8f);
            Log("shop open=" + opened + " rows=" + menu.shop.DebugRowCount + " live rows=" + menu.shop.DebugLiveRows + " longest frame while it opened: " + (hitch * 1000f).ToString("0") + " ms (Open " + menu.shop.DebugOpenMs.ToString("0") + " ms, of it icons " + menu.shop.DebugIconMs.ToString("0") + " ms) | menu on the right=" + (menu.panelRect.anchorMin.x > 0.5f));
            Shoot("shop_open");
            uint item = menu.shop.DebugFirstUnowned();
            Log("first unowned skin: " + item);
            if (item != 0)
            {
                menu.shop.DemoTap(item);
                await Wait(0.6f);
                Shoot("shop_popup");
                menu.popup.Hide();
                menu.shop.DebugBuy(item);
                bool bought = await WaitFor(() => BbSession.Snapshot != snap && BbSession.Snapshot.Skins.Count > skinsBefore, 8f);
                await Wait(0.6f);
                Log("bought=" + bought + " gems " + gemsBefore + " -> " + BbSession.Snapshot.Gems + " skins " + skinsBefore + " -> " + BbSession.Snapshot.Skins.Count);
                Shoot("shop_bought");
            }
            menu.shop.Close();
            await Wait(1.2f);
            Log("shop closed=" + !menu.shop.IsOpen + " menu on the left=" + (menu.panelRect.anchorMin.x < 0.5f));
            Shoot("shop_closed");
        }

        /// <summary>Gear with the real server: the bag in gear mode, a piece worn by the Earth character (its stats grow), upgraded with a stone, taken off again.</summary>
        async Task GearRun(BbMenuUi menu, BbVillageView village)
        {
            menu.DemoSelect(0);                                              // Trái Đất
            await Wait(1.0f);
            Shoot("gear_start");
            string headerBefore = menu.headerText.text.Replace("\n", " | ");
            menu.DebugToggleGear();
            bool loaded = await WaitFor(() => menu.DebugGearLoaded, 8f);
            await Wait(0.6f);
            Log("gear bag loaded=" + loaded + " pieces=" + menu.DebugGearCount + " mode=" + menu.DebugGearMode);
            Shoot("gear_bag");
            var piece = menu.DebugFirstGearOfOpenPlanet();
            if (piece == null) { Log("FAILED: no piece for the Earth character in the bag"); return; }
            Log("first piece of this planet: " + piece.Name + " (" + piece.MainStat.Text + ")");
            menu.DebugOpenGearPopup(piece);
            await Wait(0.6f);
            Shoot("gear_popup");
            var before = BbSession.Snapshot;
            menu.DebugEquip(piece);
            await WaitFor(() => BbSession.Snapshot != before, 8f);
            await Wait(1.0f);
            Log("header before: " + headerBefore);
            Log("header after wearing: " + menu.headerText.text.Replace("\n", " | "));
            Shoot("gear_worn");
            before = BbSession.Snapshot;
            menu.DebugUpgrade(piece);
            await WaitFor(() => BbSession.Snapshot != before, 8f);
            await Wait(1.0f);
            Log("header after the upgrade: " + menu.headerText.text.Replace("\n", " | "));
            Shoot("gear_upgraded");
            before = BbSession.Snapshot;
            menu.DebugUnequip(piece.Slot);
            await WaitFor(() => BbSession.Snapshot != before, 8f);
            await Wait(1.0f);
            Log("header after taking it off: " + menu.headerText.text.Replace("\n", " | ") + " | pieces in the bag " + menu.DebugGearCount);
            Shoot("gear_off");
        }

        /// <summary>
        /// Gear worn or taken off while the fused form trains (owner report 2026-10-10): the live figures of the session must follow at once. The run fuses Trái Đất and Namếc,
        /// waits for the fused session, wears a piece of the Earth character, and logs how long the live armor takes to change; then takes it off and logs again.
        /// </summary>
        async Task GearFuseRun(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            await Learn(client, "EARTH", 1);
            menu.SetPanelOpen(true, true);
            menu.DemoSelect(0);
            menu.DebugToggleGear();
            await WaitFor(() => menu.DebugGearLoaded, 8f);
            await Wait(0.6f);
            var piece = menu.DebugFirstGearOfOpenPlanet();
            if (piece == null) { Log("FAILED: no piece for the Earth character in the bag"); return; }
            menu.DebugToggleGear();                                            // back to the skins; the gear stays in the list
            menu.DemoFuse(0, 2);                                               // Trái Đất is the main character, Namếc its partner
            village.TeleportMain(1850f);
            await WaitFor(() => training.Active && training.FusedSession, 15f);
            training.Focus("moc_nhan");
            await Wait(1.5f);
            ulong main = menu.FusionMainCharacterId;
            BbTraining.Live live;
            training.TryLive(main, out live);
            ulong armor0 = live.Armor;
            Log("fused session: " + training.FusedSession + ", live armor " + armor0 + " (" + piece.Name + " " + piece.MainStat.Text + ")");
            var before = BbSession.Snapshot;
            float t0 = Time.realtimeSinceStartup;
            menu.DebugEquip(piece);
            bool changed = await WaitFor(() => training.TryLive(main, out live) && training.FusedSession && live.Armor > armor0, 15f);
            Log("after wearing: live armor " + live.Armor + (changed ? " changed after " : " NOT changed, waited ") + (Time.realtimeSinceStartup - t0).ToString("0.00") + " s, fused " + training.FusedSession);
            await Wait(1.0f);
            Shoot("gearfuse_worn");
            t0 = Time.realtimeSinceStartup;
            menu.DemoSelect(0);
            menu.DebugUnequip(piece.Slot);
            changed = await WaitFor(() => training.TryLive(main, out live) && training.FusedSession && live.Armor == armor0, 15f);
            Log("after taking it off: live armor " + live.Armor + (changed ? " changed after " : " NOT changed, waited ") + (Time.realtimeSinceStartup - t0).ToString("0.00") + " s, fused " + training.FusedSession);
            Shoot("gearfuse_off");
        }

        /// <summary>GEN with the real server (docs 15.21): a stone puts a set on the empty line, a slot stone opens a second line, the second line gets another set, the piece is worn and the sets show under the character.</summary>
        async Task GenRun(BbMenuUi menu, BbVillageView village, BbClient client)
        {
            menu.DemoSelect(0);
            await Wait(1.0f);
            menu.DebugToggleGear();
            await WaitFor(() => menu.DebugGearLoaded, 8f);
            await Wait(0.6f);
            var piece = menu.DebugFirstGearOfOpenPlanet();
            if (piece == null) { Log("FAILED: no piece for the Earth character in the bag"); return; }
            ulong character = client.Snapshot.Characters[0].CharacterId;
            Log("piece " + piece.Name + ": slots " + piece.GenSlots + ", next slot costs " + piece.GenUnlockCost + ", stones in the snapshot " + StoneCount(client, 204) + " GEN, " + StoneCount(client, 205) + " slot");
            var r = await client.RerollGenAsync(character, piece.ItemId, 0);
            Log("reroll line 1: " + r.Ok + " " + r.Error);
            r = await client.RerollGenAsync(character, piece.ItemId, 1);
            Log("reroll line 2 before it exists: " + r.Ok + " " + r.Error);
            r = await client.UnlockGenSlotAsync(character, piece.ItemId);
            Log("open a slot: " + r.Ok + " " + r.Error);
            r = await client.RerollGenAsync(character, piece.ItemId, 1);
            Log("reroll line 2: " + r.Ok + " " + r.Error);
            var items = await client.InventoryAsync(character);
            ItemInfo now = null;
            foreach (var it in items) if (it.ItemId == piece.ItemId) now = it;
            if (now == null) { Log("FAILED: the piece is gone"); return; }
            var sb = new System.Text.StringBuilder();
            foreach (var g in now.Gens) sb.Append(g.GenKey.Length == 0 ? "(empty)" : g.GenKey).Append(" / ");
            Log("the piece now has " + now.GenSlots + " slots: " + sb + "next slot costs " + now.GenUnlockCost);
            client.Pump();
            menu.DebugToggleGear();
            menu.DebugToggleGear();
            await WaitFor(() => menu.DebugGearLoaded, 8f);
            await Wait(0.8f);
            var shown = menu.DebugFirstGearOfOpenPlanet();
            menu.DebugOpenGearPopup(shown ?? now);
            await Wait(0.6f);
            Shoot("gen_popup");
            var before = BbSession.Snapshot;
            menu.DebugEquip(now);
            await WaitFor(() => BbSession.Snapshot != before, 8f);
            await Wait(1.0f);
            Log("worn: " + menu.bonusText.text.Replace("\n", " | "));
            Shoot("gen_worn");
        }

        /// <summary>The daily check-in of the elder with the real server (docs 15.25): his dialog, the claim, the table of the month, a second claim refused.</summary>
        async Task AttendRun(BbMenuUi menu, BbVillageView village, BbClient client)
        {
            NpcSpec elder = null;
            foreach (var n in village.Map.Npcs) if (n.Id == "elder") elder = n;
            if (elder == null) { Log("FAILED: no elder in the village map"); return; }
            Log("elder: action " + elder.Action + ", x " + elder.X);
            menu.SetPanelOpen(false, true);
            village.TeleportMain(elder.X - 80f);
            await Wait(1.2f);
            ulong goldBefore = client.Snapshot.Gold, gemsBefore = client.Snapshot.Gems;
            menu.DemoNpc(village.Map.Npcs.IndexOf(elder));
            await WaitFor(() => menu.popup.IsOpen, 8f);
            await Wait(0.6f);
            Log("his dialog is open: " + menu.popup.IsOpen);
            Shoot("attend_dialog");
            menu.popup.buttons[0].onClick.Invoke();                                      // "Điểm danh": the table opens and checks in
            await WaitFor(() => menu.attendance.IsOpen && menu.attendance.DebugClaimed, 10f);
            await Wait(1.0f);
            Log("table open: " + menu.attendance.IsOpen + ", result: " + menu.attendance.DebugResult.Replace("\n", " | "));
            Log("gold " + goldBefore + " -> " + client.Snapshot.Gold + ", gems " + gemsBefore + " -> " + client.Snapshot.Gems);
            Shoot("attend_table");
            menu.attendance.DebugClaim();
            await Wait(0.6f);
            menu.attendance.Close();
            await Wait(0.8f);
            menu.DemoNpc(village.Map.Npcs.IndexOf(elder));
            await WaitFor(() => menu.popup.IsOpen, 8f);
            await Wait(0.6f);
            Log("second visit: button 1 enabled " + menu.popup.buttons[0].interactable);
            Shoot("attend_again");
        }

        /// <summary>
        /// The quests of Ông Gôhan with the real server (docs 15.32): the first quest and where the arrow points, the arrow at the edge of the screen, a finished quest (the arrow goes back to
        /// Gôhan), his dialog, the reward, and the skill cards that learning a skill takes.
        /// </summary>
        async Task QuestRun(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            menu.SetPanelOpen(false, true);
            NpcSpec gohan = village.NpcOfId("gohan"), elder = village.NpcOfId("elder");
            if (gohan == null || elder == null) { Log("FAILED: no Gôhan or elder in the map"); return; }
            Log("gohan: action " + gohan.Action + ", x " + gohan.X + "; quests " + client.Snapshot.Quests.Count);
            foreach (var q in client.Snapshot.Quests) Log("quest " + q.Id + " " + q.Kind + " " + q.Progress + "/" + q.Target + " npc '" + q.NpcId + "' reward " + BbQuestPanel.RewardText(q));
            var active = menu.ActiveQuest();
            Log("active quest " + (active != null ? active.Id : "none") + ", tracked " + menu.TrackedQuestId);
            village.TeleportMain(gohan.X - 150f);
            await Wait(1.4f);
            Log("arrow points to " + menu.DebugGuideNpc + " (the elder is in view)");
            Log("view width " + village.ViewWidth + ", canvas width " + ((RectTransform)menu.transform).rect.width);
            Shoot("quest_arrow_world");
            menu.TrackQuest("d02");                                                     // the dungeon NPC Cui is off the screen: the arrow goes to the edge
            await Wait(0.8f);
            Log("tracked d02: arrow points to " + menu.DebugGuideNpc);
            Shoot("quest_arrow_edge");
            menu.TrackQuest("d02");                                                     // back to the automatic choice
            ulong goldBefore = client.Snapshot.Gold, gemsBefore = client.Snapshot.Gems;
            var check = await client.ClaimAttendanceAsync();                            // the first quest: check in at the elder
            Log("check-in ok " + check.Ok);
            await WaitFor(() => { var a = menu.ActiveQuest(); return a != null && a.Id == "t01" && a.Progress >= a.Target; }, 8f);
            await Wait(0.8f);
            Log("quest t01 finished: arrow points to " + menu.DebugGuideNpc + ", tracker: " + (menu.questTrackerText != null ? menu.questTrackerText.text.Replace("\n", " | ") : ""));
            Shoot("quest_back_to_gohan");
            menu.DemoNpc(village.Map.Npcs.IndexOf(gohan));
            await WaitFor(() => menu.popup.IsOpen, 8f);
            await Wait(0.6f);
            Log("his dialog is open: " + menu.popup.IsOpen + ", button 1 enabled " + menu.popup.buttons[0].interactable);
            Shoot("quest_dialog");
            menu.popup.buttons[0].onClick.Invoke();                                     // take the reward of everything finished
            await WaitFor(() => menu.quest.IsOpen && menu.quest.DebugResult.Length > 0, 10f);
            await Wait(1.0f);
            Log("list open: " + menu.quest.IsOpen + ", rows " + menu.quest.DebugRows + ", result: " + menu.quest.DebugResult.Replace("\n", " | "));
            Log("gold " + goldBefore + " -> " + client.Snapshot.Gold + ", gems " + gemsBefore + " -> " + client.Snapshot.Gems + ", cards " + BbQuestPanel.CardsOf(client.Snapshot));
            Shoot("quest_list");
            menu.quest.Close();
            await Wait(0.8f);
            active = menu.ActiveQuest();
            Log("next quest " + (active != null ? active.Id + " " + active.Title : "none") + ", arrow points to '" + menu.DebugGuideNpc + "'");
            // learning a skill takes the cards: Kamejoko (the second skill of the list) costs 2 cards a level
            ulong earth = 0;
            foreach (var c in client.Snapshot.Characters) if (c.Race == "EARTH") earth = c.CharacterId;
            ulong cardsBefore = BbQuestPanel.CardsOf(client.Snapshot);
            var learn = await client.UpgradeSkillAsync(earth, 1);
            await Wait(0.8f);
            Log("learn Kamejoko: ok " + learn.Ok + " (" + learn.Error + "), cards " + cardsBefore + " -> " + BbQuestPanel.CardsOf(client.Snapshot));
            var learnAgain = await client.UpgradeSkillAsync(earth, 24);                 // the ultimate costs 9 cards a level and only 3 are left
            Log("learn the ultimate without enough cards: ok " + learnAgain.Ok + " (" + learnAgain.Error + ")");
            await WaitFor(() => { var a = menu.ActiveQuest(); return a != null && a.Id == "t02" && a.Progress >= a.Target; }, 8f);
            Log("quest t02 (learn a skill) finished: " + (menu.ActiveQuest() != null && menu.ActiveQuest().Progress >= menu.ActiveQuest().Target));
            // the daily quest "tập đánh Mộc nhân" counts the first blow on a dummy of a training session
            village.TeleportMain(village.NpcOfId("moc_nhan").X - 100f);
            await Wait(1.2f);
            training.Focus("moc_nhan");
            await Wait(0.3f);
            training.DebugFist();
            bool trained = await WaitFor(() => { foreach (var q in client.Snapshot.Quests) if (q.Id == "d04" && q.Progress >= 1) return true; return false; }, 20f);
            Log("quest d04 (hit the dummy) counted by the server: " + trained);
        }

        /// <summary>Pictures of the places that were added to the village (owner request 2026-10-11): the capsule behind Bunma, Cui in front of the two ships, the end of the map.</summary>
        async Task SightsRun(BbMenuUi menu, BbVillageView village)
        {
            menu.SetPanelOpen(false, true);
            Log("map width " + village.MapWidth.ToString("0") + ", npcs " + string.Join(",", village.Map.Npcs.ConvertAll(n => n.Id + "@" + n.X.ToString("0"))));
            foreach (var n in village.Map.Npcs)
            {
                if (n.Id != "bunma" && n.Id != "cui") continue;
                village.TeleportMain(n.X - 60f);
                await Wait(1.5f);
                Shoot("sight_" + n.Id);
            }
            village.TeleportMain(village.MapWidth);
            await Wait(1.5f);
            Shoot("sight_end");
        }

        /// <summary>Owner request 2026-10-10: the focus goes to the nearest target or NPC by itself and only the stick resets it; the fist talks to an NPC in focus; the dialog of an NPC is in the middle.</summary>
        async Task FocusRun(BbMenuUi menu, BbTraining training, BbVillageView village)
        {
            NpcSpec hatMit = null, uron = null;
            foreach (var n in village.Map.Npcs) { if (n.Id == "shop") hatMit = n; if (n.Id == "uron") uron = n; }
            menu.SetPanelOpen(false, true);
            village.TeleportMain(hatMit.X - 90f);
            await Wait(1.5f);
            Log("near Bà Hạt Mít: focus " + training.FocusKey + " (nobody tapped anything)");
            Shoot("focus_npc");
            var seen = new System.Text.StringBuilder();
            for (int i = 0; i < 4; i++) { training.DebugCycle(); await Wait(0.2f); seen.Append(training.FocusKey).Append(" > "); }
            Log("the focus button goes through: " + seen);
            training.Focus("shop");
            await Wait(0.3f);
            training.DebugFist();
            await Wait(0.8f);
            var rect = (RectTransform)menu.popup.box.transform;
            float canvasW = ((RectTransform)menu.popup.root.transform).rect.width;
            Log("fist with her in focus: dialog open " + menu.popup.IsOpen + ", automatic skills on " + training.AutoOn + ", box centre x " + (rect.anchoredPosition.x + rect.sizeDelta.x * 0.5f).ToString("0") + " of " + canvasW.ToString("0"));
            Shoot("focus_dialog");
            menu.popup.Hide();
            village.DebugStick = new Vector2(1f, 0f);
            await Wait(0.3f);
            Log("the stick is pushed: focus " + (training.FocusKey ?? "none"));
            village.DebugStick = Vector2.zero;
            await Wait(0.6f);
            Log("the stick is let go: focus " + (training.FocusKey ?? "none"));
            village.TeleportMain(hatMit.X - 420f);
            await Wait(1.0f);
            training.Focus("shop");
            await Wait(0.3f);
            Log("far away, " + training.FocusKey + " in focus (in view: " + string.Join(",", village.VisibleTargetKeys()) + ")");
            training.DebugFist();
            bool opened = await WaitFor(() => menu.popup.IsOpen, 8f);
            Log("the fist walked to her and talked: " + opened);
            menu.popup.Hide();
            village.TeleportMain(1850f);
            await Wait(1.0f);
            training.Focus("moc_nhan");
            await Wait(0.3f);
            training.DebugFist();
            Log("fist with a dummy in focus: automatic skills on " + training.AutoOn + ", dialog open " + menu.popup.IsOpen);
        }

        /// <summary>
        /// The way into a dungeon with the real server (docs 15.25, stage 1): Cui is in focus, the fist opens his dialog, the list shows the dungeons of the server, Vào starts the
        /// ship (a screenshot every few tenths of a second), the dungeon screen appears, leaving shows the result, the ship brings the player back and the village fights again.
        /// </summary>
        async Task DungeonRun(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            NpcSpec cui = null;
            foreach (var n in village.Map.Npcs) if (n.Id == "cui") cui = n;
            if (cui == null) { Log("FAILED: no Cui in the village map"); return; }
            Log("Cui: action " + cui.Action + ", costume " + cui.CostumeId + ", x " + cui.X + "; the snapshot lists " + client.Snapshot.Dungeons.Count + " dungeons");
            village.TeleportMain(cui.X - 90f);
            await Wait(1.5f);
            Log("near Cui: focus " + training.FocusKey);
            training.DebugFist();
            await Wait(0.8f);
            Log("fist: dialog open " + menu.popup.IsOpen + ", button " + menu.popup.buttons[0].gameObject.activeSelf + " " + menu.popup.buttonLabels[0].text.Replace("\n", " "));
            Shoot("dungeon_dialog");
            menu.popup.buttons[0].onClick.Invoke();
            await Wait(0.6f);
            Log("the list is open: " + menu.DebugDungeon());
            Shoot("dungeon_list");
            var picker = menu.dungeonPicker;
            Log("Vào is enabled before a choice: " + picker.enter.interactable);
            picker.DemoSelect(12);
            await Wait(0.3f);
            Log("Vào is enabled after a choice: " + picker.enter.interactable);
            Shoot("dungeon_list_chosen");
            // the server refuses a dungeon it does not know: the ship flies in, the player is back in the village with a message and the village fights again
            Log("refusal: entering a dungeon the server does not know");
            menu.DebugEnterDungeon(9999, "Phó bản lỗi");
            await Wait(1.0f);
            Shoot("dungeon_fail_ship");
            bool back0 = await WaitFor(() => !menu.InDungeon, 12f);
            await Wait(0.3f);
            Log("after the refusal: back " + back0 + ", " + menu.DebugDungeon() + ", training suspended " + training.Suspended + ", toast '" + menu.toastText.text + "'");
            Shoot("dungeon_fail_back");
            menu.DebugOpenDungeonList();
            await Wait(0.4f);
            picker.DemoSelect(12);
            await Wait(0.3f);
            picker.DemoEnter();
            float t0 = Time.unscaledTime;
            float[] marks = { 0.4f, 1.0f, 1.8f, 2.4f };
            string[] names = { "ship_a", "ship_b", "ship_c", "ship_d" };
            for (int i = 0; i < marks.Length; i++)
            {
                float wait = marks[i] - (Time.unscaledTime - t0);
                if (wait > 0) await Wait(wait);
                Log("t=" + (Time.unscaledTime - t0).ToString("0.0") + ": " + menu.DebugDungeon());
                Shoot("dungeon_" + names[i]);
            }
            bool arrived = await WaitFor(() => menu.DebugDungeon().Contains("InDungeon") && !menu.DebugDungeon().Contains("(ship)"), 15f);
            Log("arrived in the dungeon: " + arrived + " after " + (Time.unscaledTime - t0).ToString("0.0") + " s; " + menu.DebugDungeon() + "; training suspended " + training.Suspended);
            await Wait(1.5f);
            Shoot("dungeon_screen");
            await Wait(2.0f);
            menu.DebugLeaveDungeon();
            bool result = await WaitFor(() => menu.popup.IsOpen, 10f);
            Log("result popup: " + result);
            await Wait(0.5f);
            Shoot("dungeon_result");
            menu.popup.buttons[0].onClick.Invoke();
            await Wait(1.2f);
            Log("on the way back: " + menu.DebugDungeon());
            Shoot("dungeon_return_ship");
            bool home = await WaitFor(() => !menu.InDungeon, 15f);
            await Wait(1.0f);
            Log("back in the village: " + home + "; " + menu.DebugDungeon() + "; training suspended " + training.Suspended + "; gold " + client.Snapshot.Gold);
            Shoot("dungeon_back");
            await Wait(3.0f);
            Log("the village fights again: focus " + (training.FocusKey ?? "none"));
        }

        /// <summary>The parts of the dungeon screen that a real fight rarely shows on cue: the telegraphs of a boss, the fall of the main character and the box that offers a revive.</summary>
        async Task DungeonUiRun(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            menu.DemoSelect(0);
            await Wait(1.0f);
            menu.DebugOpenDungeonList();
            await Wait(0.5f);
            menu.dungeonPicker.DemoSelect(12);
            menu.dungeonPicker.DemoEnter();
            bool arena = await WaitFor(() => village.InArena && training.InDungeon && !menu.transport.IsPlaying, 25f);
            Log("in the arena: " + arena);
            if (!arena) return;
            await Wait(1.0f);
            training.DebugDungeonUi(false);
            await Wait(2.5f);
            Shoot("dgui_telegraph");
            training.DebugDungeonUi(true);
            await Wait(0.5f);
            Shoot("dgui_falling");
            await Wait(2.5f);
            Shoot("dgui_dead");
            Log("main fell: " + Party(training, village));
            menu.DebugLeaveDungeon();
            await WaitFor(() => menu.popup.IsOpen, 10f);
            menu.popup.buttons[0].onClick.Invoke();
            await WaitFor(() => !menu.InDungeon, 15f);
            Log("back: " + menu.DebugDungeon());
        }

        /// <summary>
        /// A fused team in a dungeon (docs 15.26): the earring is refused inside, the tabs of the fusion form and of its two characters are one fighter, the other character can be
        /// taken over, and after a lost connection the fusion and the tab under control come back.
        /// </summary>
        async Task DungeonFusionRun(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            menu.DemoSelect(0);
            await Wait(1.0f);
            menu.DemoFuse(0, 1);                                        // Trái Đất (chủ) + Xayda, Namếc stays an AI
            await Wait(0.5f);
            Log("fused in the menu: " + menu.DebugPage() + "; party " + village.PartyCount);
            menu.DebugOpenDungeonList();
            await Wait(0.5f);
            menu.dungeonPicker.DemoSelect(12);
            menu.dungeonPicker.DemoEnter();
            bool arena = await WaitFor(() => village.InArena && training.InDungeon && !menu.transport.IsPlaying, 25f);
            if (!arena) { Log("FAILED: no arena"); return; }
            await Wait(3.0f);
            Log("in the dungeon: party " + village.PartyCount + "; " + training.DebugControl() + "; " + menu.DebugPage() + "; " + Party(training, village));
            menu.DemoEarring();
            await Wait(0.3f);
            Log("the earring in the dungeon: " + menu.DebugPage() + "; popup " + menu.popup.IsOpen);
            menu.DebugSelectRace(2);                                    // the tab of Namếc: the AI is taken over
            await Wait(1.0f);
            Log("Namếc taken over: " + menu.DebugPage() + "; " + training.DebugControl());
            menu.DebugSelectRace(1);                                    // the tab of Xayda, who is merged into the main character: the fused fighter is taken over
            await Wait(1.0f);
            Log("the fused fighter again: " + menu.DebugPage() + "; " + training.DebugControl() + "; " + Party(training, village));
            menu.DebugSelectRace(2);
            await Wait(1.0f);
            Log("Namếc again before the cut: " + menu.DebugPage());
            client.Connection.Close();
            bool back = await WaitFor(() =>
            {
                var m2 = FindObjectOfType<BbMenuUi>();
                var t2 = FindObjectOfType<BbTraining>();
                var v2 = FindObjectOfType<BbVillageView>();
                return m2 != null && t2 != null && v2 != null && t2 != training && v2.InArena && t2.InDungeon && !m2.transport.IsPlaying;
            }, 60f);
            Log("back after the drop: " + back);
            if (!back) return;
            var menu2 = FindObjectOfType<BbMenuUi>();
            var training2 = FindObjectOfType<BbTraining>();
            var village2 = FindObjectOfType<BbVillageView>();
            _menu = menu2;
            _training = training2;
            await Wait(2.0f);
            Log("after the resume: party " + village2.PartyCount + "; " + training2.DebugControl() + "; " + menu2.DebugPage() + "; " + Party(training2, village2));
            Shoot("dgfu_after");
            menu2.DebugLeaveDungeon();
            await WaitFor(() => menu2.popup.IsOpen, 10f);
            menu2.popup.buttons[0].onClick.Invoke();
            await WaitFor(() => !menu2.InDungeon, 15f);
            Log("back: " + menu2.DebugDungeon());
        }

        /// <summary>The same in the village: the training session goes on (same match token) when the player takes over another character through the tabs.</summary>
        async Task TrainSwitchRun(BbMenuUi menu, BbTraining training, BbVillageView village)
        {
            menu.DemoSelect(0);
            NpcSpec dummy = null;
            foreach (var n in village.Map.Npcs) if (n.Id == "moc_nhan") dummy = n;
            menu.SetPanelOpen(false, true);
            village.TeleportMain(dummy.X - 260f);
            bool on = await WaitFor(() => training.SessionMainCharacter != 0, 15f);
            string token = training.SessionToken;
            Log("session: " + on + ", token " + token + ", " + training.DebugControl());
            menu.DebugSelectRace(1);
            await Wait(1.5f);
            Log("after Xayda: session main " + training.SessionMainCharacter + ", same token " + (training.SessionToken == token) + ", " + training.DebugControl());
            menu.DebugSelectRace(2);
            await Wait(1.5f);
            Log("after Namếc: session main " + training.SessionMainCharacter + ", same token " + (training.SessionToken == token) + ", " + training.DebugControl());
            Shoot("trainsw");
        }

        /// <summary>The player takes over another character in the dungeon through the tabs of the menu: nothing is reset (the ape form stays an ape), the skill buttons follow (docs 15.26).</summary>
        async Task DungeonSwitchRun(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            menu.DemoSelect(0);
            await Wait(1.0f);
            menu.DebugOpenDungeonList();
            await Wait(0.5f);
            menu.dungeonPicker.DemoSelect(12);
            menu.dungeonPicker.DemoEnter();
            bool arena = await WaitFor(() => village.InArena && training.InDungeon && !menu.transport.IsPlaying, 25f);
            if (!arena) { Log("FAILED: no arena"); return; }
            await Wait(5.0f);
            ulong earth = training.MyCharacter, xayda = 0;
            for (int i = 1; i < village.PartyCount; i++) { ulong id; Vector2 f; int fc; village.PartyMember(i, out id, out f, out fc); if (xayda == 0) xayda = id; }
            Log("start: " + training.DebugControl() + "; " + Party(training, village));
            menu.DebugSelectRace(1);                                    // the tab of the characters: Xayda
            await Wait(0.8f);
            ulong now1;
            Vector2 ft;
            int fcg;
            village.PartyMember(0, out now1, out ft, out fcg);
            Log("after the tab of Xayda: main " + now1 + " (xayda tab char " + (BbSession.Snapshot.Characters.Count > 1 ? BbSession.Snapshot.Characters[1].CharacterId : 0) + "); " + training.DebugControl() + "; " + Party(training, village));
            Shoot("dgsw_a");
            training.DebugUse(13);                                      // Biến hình: the great ape
            await Wait(3.0f);
            ulong ape = now1;
            Log("the ape: look " + village.LookOf(ape) + "; " + Party(training, village));
            Shoot("dgsw_ape");
            menu.DebugSelectRace(2);                                    // Namếc
            await Wait(1.0f);
            village.PartyMember(0, out now1, out ft, out fcg);
            Log("after the tab of Namếc: main " + now1 + "; ape look " + village.LookOf(ape) + "; " + training.DebugControl() + "; " + Party(training, village));
            Shoot("dgsw_b");
            await Wait(2.0f);
            menu.DebugSelectRace(1);
            await Wait(1.0f);
            village.PartyMember(0, out now1, out ft, out fcg);
            Log("back to Xayda: main " + now1 + "; ape look " + village.LookOf(ape) + "; " + training.DebugControl() + "; " + Party(training, village));
            Shoot("dgsw_c");
            menu.DebugLeaveDungeon();
            await WaitFor(() => menu.popup.IsOpen, 10f);
            menu.popup.buttons[0].onClick.Invoke();
            await WaitFor(() => !menu.InDungeon, 15f);
            Log("back: " + menu.DebugDungeon());
        }

        /// <summary>The connection is lost in the middle of a dungeon: the login screen connects again with the match token and the fight goes on where it stopped (docs 15.25).</summary>
        async Task DungeonResumeRun(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            menu.DemoSelect(0);
            await Wait(1.0f);
            menu.DebugOpenDungeonList();
            await Wait(0.5f);
            menu.dungeonPicker.DemoSelect(12);
            menu.dungeonPicker.DemoEnter();
            bool arena = await WaitFor(() => village.InArena && training.InDungeon && !menu.transport.IsPlaying, 25f);
            if (!arena) { Log("FAILED: no arena"); return; }
            await Wait(7.0f);
            Log("before the drop: enemies " + village.EnemyCount + "; " + Party(training, village) + "; token " + (BbSession.DungeonToken != null));
            Shoot("dgres_before");
            Log("the connection is cut");
            client.Connection.Close();
            bool back = await WaitFor(() =>
            {
                var m2 = FindObjectOfType<BbMenuUi>();
                var t2 = FindObjectOfType<BbTraining>();
                var v2 = FindObjectOfType<BbVillageView>();
                return m2 != null && t2 != null && v2 != null && t2 != training && v2.InArena && t2.InDungeon && !m2.transport.IsPlaying;          // new objects: the scene was loaded again
            }, 60f);
            Log("back in the dungeon after the drop: " + back + "; scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            if (!back) return;
            var menu2 = FindObjectOfType<BbMenuUi>();
            var training2 = FindObjectOfType<BbTraining>();
            var village2 = FindObjectOfType<BbVillageView>();
            _menu = menu2;
            _training = training2;
            await Wait(2.0f);
            Log("after the resume: enemies " + village2.EnemyCount + "; " + Party(training2, village2) + "; " + menu2.DebugDungeon());
            Shoot("dgres_after");
            await Wait(4.0f);
            Log("still fighting: enemies " + village2.EnemyCount + "; " + Party(training2, village2));
            menu2.DebugLeaveDungeon();
            bool result = await WaitFor(() => menu2.popup.IsOpen, 10f);
            Log("result popup: " + result);
            menu2.popup.buttons[0].onClick.Invoke();
            bool home = await WaitFor(() => !menu2.InDungeon, 15f);
            Log("back in the village: " + home + "; token " + (BbSession.DungeonToken != null));
        }

        float _fpsT;
        int _fpsF;

        string Party(BbTraining training, BbVillageView village)
        {
            var sb = new System.Text.StringBuilder();
            float since = Time.realtimeSinceStartup - _fpsT;
            int frames = Time.frameCount - _fpsF;
            _fpsT = Time.realtimeSinceStartup;
            _fpsF = Time.frameCount;
            sb.Append("fps ").Append(since > 0.3f ? (frames / since).ToString("0") : "-").Append("  ");
            for (int i = 0; i < village.PartyCount; i++)
            {
                ulong id;
                Vector2 feet;
                int facing;
                village.PartyMember(i, out id, out feet, out facing);
                BbTraining.Live live;
                bool ok = training.TryLive(id, out live);
                sb.Append(i == 0 ? "main " : "ai ").Append(ok ? (live.Hp * 100 / Math.Max(1UL, live.HpMax)) + "%" : "?").Append(village.IsGhost(id) ? " DEAD" : "").Append(" @").Append(Mathf.RoundToInt(feet.x)).Append(",").Append(Mathf.RoundToInt(feet.y)).Append("  ");
            }
            return sb.ToString();
        }

        /// <summary>
        /// A dungeon fought for real with the server (docs 15.25): in through Cui, the arena, the waves rushing in, the fist on auto, a chuong and an ultimate, flying about, and out
        /// again. Screenshots and the state every few seconds go to the log.
        /// </summary>
        async Task DungeonFightRun(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            menu.DemoSelect(0);                                         // the Earth character is the main one: it has the chuong and the ultimate
            await Wait(1.0f);
            menu.DebugOpenDungeonList();
            await Wait(0.5f);
            menu.dungeonPicker.DemoSelect(12);
            await Wait(0.2f);
            menu.dungeonPicker.DemoEnter();
            bool arena = await WaitFor(() => village.InArena && training.InDungeon && !menu.transport.IsPlaying, 25f);
            Log("in the arena: " + arena + "; " + menu.DebugDungeon() + "; " + Party(training, village));
            if (!arena) return;
            await Wait(0.6f);
            Shoot("dg_start");
            await WaitFor(() => training.DebugBanner.StartsWith("Đợt"), 6f);
            await Wait(0.3f);
            Log("banner: " + training.DebugBanner);
            Shoot("dg_wave1");
            Log("wave 1: enemies " + village.EnemyCount + ", on screen " + village.EnemiesOnScreen + ", focus " + (training.FocusKey ?? "none") + "; " + Party(training, village));
            training.DebugFist();                                       // the fist: use the skill in focus again and again
            for (int i = 0; i < 6; i++)
            {
                await Wait(2.0f);
                Log("t+" + (2 * (i + 1)) + "s: enemies " + village.EnemyCount + ", on screen " + village.EnemiesOnScreen + ", focus " + (training.FocusKey ?? "none") + "; " + Party(training, village));
                if (i == 1) Shoot("dg_fight_a");
                if (i == 4) Shoot("dg_fight_b");
            }
            training.DebugUse(1);                                       // a chuong
            await Wait(0.45f);
            Shoot("dg_chuong");
            await Wait(1.0f);
            training.DebugUse(24);                                      // the ultimate
            await Wait(2.4f);
            Shoot("dg_ultimate_gong");
            await Wait(1.6f);
            Shoot("dg_ultimate_beam_a");
            await Wait(1.4f);
            Shoot("dg_ultimate_beam_b");
            await Wait(2.2f);
            Log("after the ultimate: enemies " + village.EnemyCount + ", on screen " + village.EnemiesOnScreen + "; " + Party(training, village));
            village.DebugStick = new Vector2(1f, 0.4f);                 // fly about a little: the screen follows
            await Wait(0.5f);
            village.DebugStick = Vector2.zero;
            Shoot("dg_flown");
            Log("flown: " + Party(training, village) + "; " + village.DebugCamera);
            bool shotDead = false, shotBoss = false;
            for (int i = 0; i < 40; i++)
            {
                await Wait(3.0f);
                Log("t+" + (3 * (i + 1)) + "s: enemies " + village.EnemyCount + ", on screen " + village.EnemiesOnScreen + ", focus " + (training.FocusKey ?? "none") + ", banner '" + training.DebugBanner.Replace("\n", " ") + "'; " + Party(training, village));
                if (i == 2) Shoot("dg_fight_c");
                if (!shotBoss && training.DebugBanner.StartsWith("BOSS")) { shotBoss = true; Shoot("dg_boss_coming"); await Wait(2.5f); Shoot("dg_boss_fight"); }
                ulong mainId;
                Vector2 f0;
                int fc;
                village.PartyMember(0, out mainId, out f0, out fc);
                if (!shotDead && village.IsGhost(mainId)) { shotDead = true; await Wait(1.0f); Shoot("dg_dead"); }
                if (!menu.DebugDungeon().Contains("InDungeon") || menu.popup.IsOpen) break;
            }
            menu.DebugLeaveDungeon();
            bool result = await WaitFor(() => menu.popup.IsOpen, 10f);
            Log("result popup: " + result);
            await Wait(0.5f);
            Shoot("dg_result");
            menu.popup.buttons[0].onClick.Invoke();
            bool home = await WaitFor(() => !menu.InDungeon, 15f);
            await Wait(1.0f);
            Log("back in the village: " + home + "; arena " + village.InArena + "; " + menu.DebugDungeon());
            Shoot("dg_back");
        }

        /// <summary>
        /// The evolving ape boss of dungeon 3 (docs 15.30) with the real server: the waves, then the boss; the charge (banner "gồng", the glow) and the evolution are photographed.
        /// The run script lowers the waves and the HP of the bosses by SQL so that this takes a minute (the server must be restarted after that).
        /// </summary>
        async Task DungeonApeRun(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            menu.DebugOpenDungeonList();
            await Wait(0.5f);
            menu.dungeonPicker.DemoSelect(3);
            await Wait(0.2f);
            menu.dungeonPicker.DemoEnter();
            bool arena = await WaitFor(() => village.InArena && training.InDungeon && !menu.transport.IsPlaying, 25f);
            Log("in the arena: " + arena + "; " + menu.DebugDungeon());
            if (!arena) return;
            training.DebugFist();
            int charges = 0, evolutions = 0;
            string last = "";
            for (int i = 0; i < 400 && menu.DebugDungeon().Contains("InDungeon"); i++)
            {
                await Wait(0.5f);
                string banner = training.DebugBanner.Replace("\n", " ");
                if (banner != last && banner.Length > 0)
                {
                    last = banner;
                    Log("t=" + (i / 2f) + "s banner '" + banner + "'; focus " + (training.FocusKey ?? "none") + "; " + Party(training, village));
                    if (banner.Contains("gồng")) { charges++; await Wait(1.0f); Shoot("ape_charge_" + charges); }
                    else if (banner.Contains("(") && banner.Contains("/")) { evolutions++; await Wait(1.2f); Shoot("ape_evolve_" + evolutions); }
                    else if (banner.StartsWith("BOSS")) { await Wait(1.5f); Shoot("ape_boss"); }
                }
                if (evolutions >= 2) break;
            }
            Log("charges " + charges + ", evolutions " + evolutions);
            menu.DebugLeaveDungeon();
            await WaitFor(() => menu.popup.IsOpen, 10f);
            menu.popup.buttons[0].onClick.Invoke();
            await WaitFor(() => !menu.InDungeon, 15f);
            Log("back: " + menu.DebugDungeon());
        }

        /// <summary>
        /// The difficulty of a dungeon (docs 15.33) with the real server: the list of Cui shows Dễ / Khó / Địa ngục, the chosen one is lit; the run starts at the one chosen
        /// (the server says which one in MatchStarted) and every skill is ready.
        /// </summary>
        async Task DungeonLevelsRun(BbMenuUi menu, BbTraining training, BbVillageView village)
        {
            menu.SetPanelOpen(false, true, false);                                                  // the menu is folded away: its button must be there after the dungeon
            menu.DebugOpenDungeonList();
            await Wait(0.5f);
            menu.dungeonPicker.DemoSelect(12);
            await Wait(0.3f);
            Shoot("levels_default");
            menu.dungeonPicker.DemoDifficulty(2);
            await Wait(0.3f);
            Shoot("levels_hell");
            menu.dungeonPicker.DemoDifficulty(1);
            await Wait(0.3f);
            Shoot("levels_hard");
            menu.dungeonPicker.DemoEnter();
            bool arena = await WaitFor(() => village.InArena && training.InDungeon && !menu.transport.IsPlaying, 25f);
            Log("in the arena: " + arena + "; difficulty " + menu.DebugDifficulty + "; " + menu.DebugDungeon());
            await Wait(2f);
            Shoot("levels_arena");
            menu.DebugLeaveDungeon();
            await WaitFor(() => menu.popup.IsOpen, 10f);
            menu.popup.buttons[0].onClick.Invoke();
            await WaitFor(() => !menu.InDungeon, 15f);
            await Wait(0.5f);
            Log("back: " + menu.DebugDungeon() + "; panel open " + menu.PanelOpen + "; menu button visible " + menu.panelToggle.gameObject.activeSelf);
        }

        /// <summary>The panel of Bà Hạt Mít with the real server: the piece and the stone are put in, the button runs the request (docs 15.22).</summary>
        async Task SmithRun(BbMenuUi menu, BbVillageView village, BbClient client)
        {
            menu.DemoSelect(0);
            await Wait(1.0f);
            menu.DebugToggleGear();
            await WaitFor(() => menu.DebugGearLoaded, 8f);
            await Wait(0.6f);
            var piece = menu.DebugFirstGearOfOpenPlanet();
            if (piece == null) { Log("FAILED: no piece for the Earth character in the bag"); return; }
            var smith = menu.smith;
            NpcSpec hatMit = null;
            foreach (var n in village.Map.Npcs) if (n.Id == "shop") hatMit = n;
            if (hatMit == null) { Log("FAILED: no Bà Hạt Mít in the village map"); return; }
            Log("Bà Hạt Mít: action " + hatMit.Action + ", costume " + hatMit.CostumeId + ", x " + hatMit.X);
            village.TeleportMain(hatMit.X - 90f);
            await Wait(1.0f);
            menu.DemoNpc(village.Map.Npcs.IndexOf(hatMit));
            await Wait(0.8f);
            Log("her menu is open: " + menu.popup.IsOpen);
            Shoot("smith_npc_menu");
            menu.popup.Hide();
            MaterialAmount Stone(string kind) { foreach (var m in client.Snapshot.Materials) if (m.Kind == kind) return m; return null; }
            var modes = new[] { BbSmithPanel.Mode.Upgrade, BbSmithPanel.Mode.Reroll, BbSmithPanel.Mode.Unlock, BbSmithPanel.Mode.Reroll };
            var kinds = new[] { "UPGRADE_STONE", "GEN_STONE", "GEN_SLOT_STONE", "GEN_STONE" };
            for (int i = 0; i < modes.Length; i++)
            {
                menu.DemoSmith(modes[i]);
                await Wait(0.6f);
                smith.PutItem(piece);
                smith.PutStone(Stone(kinds[i]));
                if (i == 3) smith.DebugLine(1);
                await Wait(0.3f);
                Log("step " + i + " " + modes[i] + ": can press " + smith.DebugCanPress + ", text \"" + smith.DebugResult + "\"");
                if (i == 1) Shoot("smith_ready");
                smith.DebugPress();
                string before = smith.DebugResult;
                await WaitFor(() => smith.DebugResult != before && smith.DebugResult.Length > 0 && !smith.DebugResult.StartsWith("Chạm") && !smith.DebugResult.StartsWith("Chọn"), 8f);
                await Wait(0.8f);
                Log("step " + i + " result: " + smith.DebugResult);
                if (i == 3) Shoot("smith_done");
                smith.Close();
                await Wait(0.8f);
                piece = menu.ItemById(piece.ItemId) ?? piece;
            }
        }

        /// <summary>
        /// Ép sao and Pha lê hóa of Bà Hạt Mít with the real server (docs 15.27). The account needs a piece in the bag, stat stones (202) and crystal stones (203): the run script gives
        /// them by SQL before it lets the scenario go.
        /// </summary>
        async Task SmithStarRun(BbMenuUi menu, BbVillageView village, BbClient client)
        {
            menu.DemoSelect(0);
            await Wait(1.0f);
            menu.DebugToggleGear();
            await WaitFor(() => menu.DebugGearLoaded, 8f);
            await Wait(0.6f);
            var piece = menu.DebugFirstGearOfOpenPlanet();
            if (piece == null) { Log("FAILED: no piece for the Earth character in the bag"); return; }
            var smith = menu.smith;
            MaterialAmount Stone(string kind) { foreach (var m in client.Snapshot.Materials) if (m.Kind == kind) return m; return null; }
            string Lines(ItemInfo it) { var sb = new System.Text.StringBuilder(); foreach (var l in it.Lines) sb.Append(l.LineNo).Append('=').Append(l.StatKey).Append(' ').Append(l.Value).Append('/').Append(l.Cap).Append(' ').Append(l.SuccessPct).Append("% ").Append(l.Source).Append("; "); return sb.ToString(); }
            menu.DemoSmithMenu();
            await Wait(0.8f);
            Shoot("star_menu");
            menu.popup.Hide();
            menu.DemoSmith(BbSmithPanel.Mode.Star);
            await Wait(0.6f);
            smith.PutItem(piece);
            smith.PutStone(Stone("STAT_STONE"));
            await Wait(0.3f);
            Log("star, empty: rows " + smith.DebugRows + "can press " + smith.DebugCanPress + ", alt " + smith.DebugAltVisible + ", info: " + smith.DebugInfo.Replace("\n", " / "));
            Shoot("star_empty");
            for (int row = 0; row < 3; row++)
            {
                smith.DebugLine(row);
                await Wait(0.2f);
                smith.DebugPress();
                await Wait(1.2f);
                piece = menu.ItemById(piece.ItemId) ?? piece;
                Log("added a line (row " + row + "): " + Lines(piece) + " result \"" + smith.DebugResult + "\"");
            }
            ulong earthId = 0;
            foreach (var ch in client.Snapshot.Characters) if (ch.Race == "EARTH") earthId = ch.CharacterId;
            var worn = await client.EquipItemAsync(earthId, piece.ItemId);                          // worn, so that the preview shows the stat of the character too
            Log("worn: " + worn.Ok);
            await Wait(1.5f);
            piece = menu.ItemById(piece.ItemId) ?? piece;
            smith.DebugLine(0);
            await Wait(0.3f);
            Log("a filled line: rows " + smith.DebugRows + "alt " + smith.DebugAltVisible + ", info: " + smith.DebugInfo.Replace("\n", " / "));
            Shoot("star_lines");
            for (int i = 0; i < 4; i++)
            {
                smith.DebugPress();
                await Wait(1.2f);
                piece = menu.ItemById(piece.ItemId) ?? piece;
                Log("raise " + i + ": " + Lines(piece) + " result \"" + smith.DebugResult + "\"");
            }
            smith.DebugAlt();
            await Wait(1.2f);
            piece = menu.ItemById(piece.ItemId) ?? piece;
            Log("retype: " + Lines(piece) + " result \"" + smith.DebugResult + "\"");
            Shoot("star_retype");
            smith.Close();
            await Wait(0.8f);
            menu.DemoSmith(BbSmithPanel.Mode.Crystal);
            await Wait(0.6f);
            smith.PutItem(piece);
            smith.PutStone(Stone("CRYSTAL_STONE"));
            await Wait(0.3f);
            Log("crystal: can press " + smith.DebugCanPress + ", info: " + smith.DebugInfo.Replace("\n", " / "));
            Shoot("crystal_ready");
            smith.DebugPress();
            await Wait(1.5f);
            piece = menu.ItemById(piece.ItemId) ?? piece;
            Log("crystallised: " + Lines(piece) + " result \"" + smith.DebugResult + "\"");
            Shoot("crystal_done");
            smith.Close();
            await Wait(0.8f);
            menu.DemoSmith(BbSmithPanel.Mode.Star);
            await Wait(0.6f);
            smith.PutItem(piece);
            smith.PutStone(Stone("STAT_STONE"));
            smith.DebugLine(3);
            await Wait(0.3f);
            Log("the red line: rows " + smith.DebugRows + "alt " + smith.DebugAltVisible + ", info: " + smith.DebugInfo.Replace("\n", " / "));
            smith.DebugPress();
            await Wait(1.2f);
            piece = menu.ItemById(piece.ItemId) ?? piece;
            Log("the red line raised: " + Lines(piece) + " result \"" + smith.DebugResult + "\"");
            Shoot("star_red");
            smith.Close();
            await Wait(0.6f);
            menu.DebugShowGearPopup(piece);
            await Wait(0.5f);
            Shoot("star_popup");
        }

        static ulong StoneCount(BbClient client, uint template)
        {
            foreach (var m in client.Snapshot.Materials) if (m.TemplateId == template) return m.Quantity;
            return 0;
        }

        /// <summary>Taps the NPC and returns the longest frame (seconds) from the tap until the shop is open and two more frames have passed: the "khựng" the owner reported.</summary>
        async Task<float> OpenShopMeasured(BbMenuUi menu, BbVillageView village, NpcSpec npc)
        {
            await Wait(0.3f);
            float longest = 0f, last = Time.realtimeSinceStartup;
            menu.DemoNpc(village.Map.Npcs.IndexOf(npc));
            float start = last;
            int after = 0;
            while (Time.realtimeSinceStartup - start < 8f && after < 3)
            {
                await Task.Yield();
                float now = Time.realtimeSinceStartup;
                longest = Mathf.Max(longest, now - last);
                last = now;
                if (menu.shop.IsOpen) after++;
            }
            return longest;
        }

        /// <summary>Bunma's shop with the real server: clothes and stones, each bought once, the money and the bag checked.</summary>
        async Task BunmaRun(BbMenuUi menu, BbVillageView village)
        {
            var bunma = village.Map.Npcs.Find(n => n.Id == "bunma");
            if (bunma == null) { Log("FAILED: no NPC bunma on the map"); return; }
            village.TeleportMain(bunma.X + 260f);
            await Wait(1.2f);
            Shoot("bunma_village");
            var snap = BbSession.Snapshot;
            float hitch = await OpenShopMeasured(menu, village, bunma);
            await Wait(0.8f);
            Log("bunma shop open=" + menu.shop.IsOpen + " items in the first tab=" + menu.shop.DebugRowCount + " longest frame: " + (hitch * 1000f).ToString("0") + " ms");
            Shoot("bunma_open");
            menu.shop.DemoTapIndex(2, 7);                                    // Thiên Sứ, the Namếc pants
            await Wait(0.6f);
            Shoot("bunma_angel_namec");
            menu.popup.Hide();
            menu.shop.DemoTapIndex(2, 12);                                   // Thiên Sứ, the Xayda gloves
            await Wait(0.6f);
            Shoot("bunma_angel_xayda");
            menu.popup.Hide();
            menu.shop.DemoTapIndex(1, 0);
            await Wait(0.6f);
            Shoot("bunma_hd_popup");
            menu.popup.Hide();
            menu.shop.DebugBuyAt(0, 1);                                      // Hủy Diệt needs level 30: refused
            await Wait(1.0f);
            menu.shop.DemoTapIndex(3, 0);
            await Wait(0.6f);
            Shoot("bunma_exchange_popup");
            menu.popup.Hide();
            var swapBefore = BbSession.Snapshot;
            menu.shop.DebugBuyAt(0, 1);                                      // 10 fragments of Áo Thần Linh were put in the account: the piece comes, the fragments go
            await WaitFor(() => BbSession.Snapshot != swapBefore, 8f);
            await Wait(0.5f);
            ulong frag = 0;
            foreach (var m in BbSession.Snapshot.Materials) if (m.TemplateId == 101) frag = m.Quantity;
            Log("exchanged: bag " + BbSession.Snapshot.BagCount + ", fragments of the armor left " + frag);
            Shoot("bunma_exchanged");
            menu.shop.DemoTapIndex(0, 0);
            await Wait(0.6f);
            Shoot("bunma_gear_popup");
            menu.popup.Hide();
            ulong gold0 = BbSession.Snapshot.Gold;
            var before = BbSession.Snapshot;
            menu.shop.DebugBuyAt(0, 1);
            await WaitFor(() => BbSession.Snapshot != before, 8f);
            await Wait(0.5f);
            Log("bought a piece of gear: gold " + gold0 + " -> " + BbSession.Snapshot.Gold + ", bag " + BbSession.Snapshot.BagCount);
            Shoot("bunma_bought_gear");
            menu.shop.DemoTapIndex(4, 0);
            await Wait(0.6f);
            Shoot("bunma_stone_popup");
            menu.popup.Hide();
            before = BbSession.Snapshot;
            menu.shop.DebugBuyAt(0, 10);
            await WaitFor(() => BbSession.Snapshot != before, 8f);
            await Wait(0.5f);
            ulong stones = 0;
            foreach (var m in BbSession.Snapshot.Materials) if (m.TemplateId == 201) stones = m.Quantity;
            Log("bought 10 upgrade stones: gold " + BbSession.Snapshot.Gold + ", stones in the account " + stones);
            Shoot("bunma_bought_stones");
            menu.shop.Close();
            await Wait(1.2f);
            Log("shop closed=" + !menu.shop.IsOpen);
        }

        /// <summary>The beam of each planet (Kamejoko, Masenko, Antomic): where the dart is next to the hand of the character, the body and the target (owner remark 2026-10-10: it came out of the feet).</summary>
        async Task Beams(BbMenuUi menu, BbTraining training, BbVillageView village)
        {
            int[] mains = { 0, 2, 1 };                                       // Trái Đất, Namếc, Xayda
            uint[] skills = { 1, 3, 5 };
            for (int i = 0; i < 3; i++)
            {
                menu.DemoSelect(mains[i]);
                await Wait(2.5f);
                village.TeleportMain(1720f);
                await WaitFor(() => training.Active, 10f);
                training.Focus("moc_nhan");
                await Wait(1.5f);
                Log("beam " + skills[i] + ": " + Where(village));
                training.DebugUse(skills[i]);
                await Observe("beam" + skills[i], 2.0f, village, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f);
                await Wait(1.5f);
            }
        }

        /// <summary>
        /// The skills that gather power (owner request 2026-10-10): while they last the character must not walk (the scenario pushes the stick all the time and logs where it is),
        /// the ultimates go through every target, Ma phong ba turns the targets into jars, Biến hình changes the numbers of the menu.
        /// </summary>
        async Task Learn(BbClient client, string race, params uint[] skills)
        {
            BattleBall.Proto.CharacterInfo ch = null;
            foreach (var c in client.Snapshot.Characters) if (c.Race == race) ch = c;
            foreach (uint skill in skills)
            {
                var r = await client.UpgradeSkillAsync(ch.CharacterId, skill);
                Log(race + ": learn skill " + skill + ": " + r.Ok + " " + r.Error);
                client.Pump();
            }
            await Wait(0.5f);
        }

        /// <summary>
        /// One skill at a time (owner remark 2026-10-10): the chưởng and the fist are pressed in the same frame; the fist must wait for the chưởng to end. The run logs when
        /// each skill was fired ("[SkillFx] fire ... at t" in the editor log) and the damage numbers of the server.
        /// </summary>
        async Task Order(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            menu.DemoSelect(0);                                              // the run raised the chưởng of Trái Đất before (earthRun)
            await Wait(2.5f);
            village.TeleportMain(1800f);
            await WaitFor(() => training.Active, 10f);
            training.Focus("moc_nhan");
            await Wait(1.5f);
            Log("order: pressing chưởng (1) then fist (0) twice in the same frame");
            training.DebugUse(1);
            training.DebugUse(0);
            training.DebugUse(0);
            await Observe("order", 4.0f, village, 0.2f, 0.5f, 0.8f, 1.2f, 1.8f);
        }

        /// <summary>Khiên năng lượng (owner remark 2026-10-11: "thiếu hoạt ảnh"): the bubble of Nro (Effect 33) follows the character for as long as the shield lasts, also while it walks.</summary>
        async Task ShieldRun(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            await Learn(client, "EARTH", 19);
            menu.DemoSelect(0);
            await Wait(2.5f);
            village.TeleportMain(1800f);
            await WaitFor(() => training.Active, 10f);
            training.Focus("moc_nhan");
            await Wait(1.0f);
            Shoot("shield_before");
            training.DebugUse(19);
            float[] at = { 0.3f, 0.5f, 0.7f, 1.0f, 3.0f };
            float last = 0f;
            foreach (float t in at)
            {
                await Wait(t - last);
                last = t;
                Shoot("shield_" + (int)(t * 10));
            }
            Log("shield walking: " + Where(village));
            await ObserveWalking("shield_walk", 1.5f, village, 0.4f, 1.0f);
            Shoot("shield_end");
        }

        /// <summary>The fusion effects of the Nro client (owner request 2026-10-10): the character vanishes under the white blinking, the Potara effect rises, then the aura of the merged form.</summary>
        async Task FusionFxRun(BbMenuUi menu, BbVillageView village)
        {
            menu.SetPanelOpen(true, true);
            menu.DemoSelect(0);
            await Wait(2.0f);
            Shoot("fx_before");
            menu.DemoDance(0);                                               // the fusion dance of Nro (type 4): Effect(34)
            await Wait(0.3f);
            Shoot("fx_dance_30");
            await Wait(0.4f);
            Shoot("fx_dance_70");
            await Wait(1.6f);
            menu.SetPanelOpen(true, true);
            Log("fuse with the earring effects: panel open " + menu.PanelOpen);
            menu.DemoFuseWithEffect(0, 2);
            if (await WaitFor(() => menu.FusionEffects.WhiteNow, 1f)) Shoot("fx_white");
            float[] at = { 0.05f, 0.15f, 0.3f, 0.5f, 0.8f, 1.2f, 1.6f };
            float last = 0f;
            foreach (float t in at)
            {
                await Wait(t - last);
                last = t;
                Shoot("fx_fuse_" + (int)(t * 100));
            }
            await Wait(1.2f);
            Log("fused: panel open " + menu.PanelOpen);
            for (int i = 0; i < 3; i++) { await Wait(0.3f); Shoot("fx_aura_" + i); }
            Log("split");
            menu.SetPanelOpen(true, true, false);
            menu.DemoSplit();
            last = 0f;
            foreach (float t in new[] { 0.05f, 0.3f, 1.0f, 1.6f })
            {
                await Wait(t - last);
                last = t;
                Shoot("fx_split_" + (int)(t * 100));
            }
            await Wait(1.0f);
            Shoot("fx_after");
        }

        async Task Gong(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            // 1. Super Kamejoko of Trái Đất
            menu.DemoSelect(0);
            await Wait(2.5f);
            village.TeleportMain(1500f);
            await WaitFor(() => training.Active, 10f);
            training.Focus("moc_nhan");
            await Wait(1.5f);
            Log("kame: " + Where(village));
            training.DebugUse(24);
            await ObserveWalking("kame", 6.5f, village, 0.3f, 1.0f, 2.2f, 2.5f, 3.0f, 3.6f, 4.2f, 5.0f);
            await Wait(1.5f);
            // 2. Ma phong ba of Namếc: the two dummies are within 500 px (learned only now: the AI of the same race would have used it first)
            await Learn(client, "NAMEC", 26);
            menu.DemoSelect(2);
            await Wait(2.5f);
            village.TeleportMain(2050f);
            await WaitFor(() => training.Active, 10f);
            training.Focus("moc_nhan");
            await Wait(1.5f);
            Log("mafuba: " + Where(village));
            training.DebugUse(26);
            _pushSeconds = 1.0f;
            await ObserveWalking("mafuba", 22f, village, 0.5f, 2.3f, 2.8f, 3.4f, 4.0f, 4.8f, 5.6f, 6.3f, 7.5f, 9f, 12f, 16f, 18.5f, 20f);
            await Wait(1f);
            // 3. Biến hình of Xayda: the numbers of the menu
            await Learn(client, "XAYDA", 13);
            menu.DemoSelect(1);
            await Wait(2.5f);
            village.TeleportMain(1850f);
            await WaitFor(() => training.Active, 10f);
            training.Focus("moc_nhan");
            menu.SetPanelOpen(true, true);
            await Wait(1.5f);
            _pushSeconds = 1000f;
            Log("monkey before: " + menu.headerText.text.Replace("\n", " | "));
            training.DebugUse(13);
            await ObserveWalking("monkey", 5f, village, 0.3f, 1.0f, 1.6f, 2.2f, 3.5f);
            Log("monkey after: " + menu.headerText.text.Replace("\n", " | "));
            menu.SetPanelOpen(false, true);
        }

        /// <summary>
        /// The live numbers of the menu with a fusion (owner request 2026-10-10): the session of a fused character has one fighter carrying the sum; the fusion tab shows it live,
        /// the tabs of the two characters keep their own figures. The run logs the header of every tab before, during and after the session.
        /// </summary>
        async Task Fusion(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            await Learn(client, "EARTH", 1);                                   // the action brings the snapshot with the level 50 and the earring the run put in the database
            menu.SetPanelOpen(true, true);
            // a session that started before the fusion: using the earring must end it (the partner would go on fighting beside the fused character)
            menu.DemoSelect(0);
            village.TeleportMain(1850f);
            await WaitFor(() => training.Active, 10f);
            training.Focus("moc_nhan");
            await Wait(2f);
            Log("before the fusion: session active " + training.Active + ", fused " + training.FusedSession);
            menu.DemoSelect(1);
            await Wait(0.3f);
            menu.DemoEarring();                                                // the popup of the earring on another planet's tab (not in use yet)
            await Wait(0.6f);
            Shoot("earring_unfused");
            menu.popup.Hide();
            menu.DemoFuse(0, 2);                                               // Trái Đất is the main character, Namếc its partner
            await Wait(0.3f);
            Log("just after the fusion: session active " + training.Active);
            await WaitFor(() => training.Active && training.FusedSession, 12f);
            Log("a while after: session active " + training.Active + ", fused " + training.FusedSession);
            menu.DemoSelect(1);
            await Wait(0.3f);
            menu.DemoEarring();                                                // in use: "Tách" on a tab of another planet too
            await Wait(0.6f);
            Shoot("earring_fused");
            menu.popup.Hide();
            await Wait(0.5f);
            string[] names = { "Trái Đất", "Xayda", "Namếc", "Hợp thể" };
            for (int page = 0; page < 4; page++)
            {
                menu.DemoSelect(page);
                await Wait(0.4f);
                Log("static " + names[page] + ": " + menu.headerText.text.Replace("\n", " | "));
            }
            menu.DemoSelect(0);                                                // the main character's own tab: the session starts for the fusion
            village.TeleportMain(1850f);
            await WaitFor(() => training.Active, 10f);
            training.Focus("moc_nhan");
            await Wait(1.5f);
            Log("session fused: " + training.FusedSession);
            for (int page = 0; page < 4; page++)
            {
                menu.DemoSelect(page);
                await Wait(0.6f);
                Log("live " + names[page] + ": " + menu.headerText.text.Replace("\n", " | "));
                if (page == 3) Shoot("fusion_tab");
            }
        }

        /// <summary>
        /// What a player does: the automatic fist is on (the default) and he presses the ultimate; the poses of the character must not be replaced by the punches that the fist
        /// sends before the server has said that the character is busy (owner report 2026-10-10: the ultimate of Trái Đất loses its animation when the player uses it).
        /// </summary>
        async Task AutoUlt(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            await Learn(client, "XAYDA", 25);
            await Learn(client, "NAMEC", 26);
            int[] pages = { 0, 1, 2 };
            uint[] skills = { 24, 25, 26 };
            string[] names = { "earth", "xayda", "namek" };
            for (int i = 0; i < 3; i++)
            {
                menu.DemoSelect(pages[i]);
                await Wait(2.5f);
                village.TeleportMain(1700f);
                await WaitFor(() => training.Active, 10f);
                training.Focus("moc_nhan");
                training.DemoAuto(true);
                await Wait(2f);
                Log(names[i] + ": " + Where(village));
                training.DebugUse(skills[i]);
                _pushSeconds = 0f;
                await ObserveWalking(names[i], 5f, village, 0.1f, 0.2f, 0.3f, 0.5f, 0.8f, 1.2f, 1.8f, 2.4f, 3.0f);
                training.DemoAuto(false);
                await Wait(8f);
            }
        }

        async Task Push(BbVillageView village, Vector2 stick, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) { village.Tick(stick, Time.unscaledDeltaTime); await Task.Yield(); }
        }

        /// <summary>The ultimate of Trái Đất by the controlled character in the situations of a player: standing, flying, and walking when the button is pressed.</summary>
        async Task UltVariants(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            string[] names = { "stand", "fly", "walk", "xayda", "namek" };
            int only = int.Parse(Arg("-bbVariant", "0"));                       // one situation a run: the ultimate cools down for 170 s
            for (int i = only; i <= only; i++)
            {
                if (i == 3) await Learn(client, "XAYDA", 25);
                if (i == 4) await Learn(client, "NAMEC", 26);
                menu.DemoSelect(i == 3 ? 1 : i == 4 ? 2 : 0);
                await Wait(2.5f);
                village.TeleportMain(1700f);
                await WaitFor(() => training.Active, 10f);
                training.Focus("moc_nhan");
                await Wait(1.5f);
                if (i == 1) await Push(village, new Vector2(0f, 1f), 1.4f);               // up: a jump that turns into flying
                if (i == 2) { _walkWhilePressing = true; }
                ulong id; Vector2 feet; int facing;
                village.PartyMember(0, out id, out feet, out facing);
                Log(names[i] + ": main at " + Mathf.RoundToInt(feet.x) + "," + Mathf.RoundToInt(feet.y) + " airborne " + village.Airborne(id));
                training.DebugUse(i == 3 ? 25u : i == 4 ? 26u : 24u);
                _pushSeconds = i == 2 ? 0.3f : 0f;
                await ObserveWalking("var_" + names[i], 4.2f, village, 0.15f, 0.4f, 0.9f, 1.5f, 2.2f, 2.6f, 3.2f, 3.9f);
                await Wait(10f);
            }
        }

        bool _walkWhilePressing;

        /// <summary>The three ultimates used by the three characters at once (the AI use theirs by themselves), the main one on the right of the dummy so that its beam goes left.</summary>
        async Task Ultimates(BbMenuUi menu, BbTraining training, BbVillageView village, BbClient client)
        {
            await Learn(client, "XAYDA", 25);
            await Learn(client, "NAMEC", 26);
            menu.DemoSelect(1);                                                  // Xayda is the main character
            await Wait(2.5f);
            village.TeleportMain(2100f);                                         // right of the first dummy (1936): the target is on its left
            await WaitFor(() => training.Active, 10f);
            training.Focus("moc_nhan");
            await Wait(1.2f);
            Log("ult: " + Where(village));
            training.DebugUse(25);
            _pushSeconds = 1.0f;
            await ObserveWalking("ult", 25f, village, 0.3f, 2.2f, 2.6f, 3.0f, 3.6f, 4.4f, 5.5f, 7f, 9f, 12f, 15f, 20f);
        }

        /// <summary>Like Observe, but the stick is pushed to the right all the time: a character that is held must not move, and the log says where the main one is.</summary>
        float _pushSeconds = 1000f;

        async Task ObserveWalking(string name, float seconds, BbVillageView village, params float[] shotsAt)
        {
            float start = Time.realtimeSinceStartup, nextLog = 0f;
            int shot = 0;
            Log("--- " + name);
            ulong id;
            Vector2 feet;
            int facing;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                float t = Time.realtimeSinceStartup - start;
                if (t >= nextLog)
                {
                    nextLog += 0.25f;
                    village.PartyMember(0, out id, out feet, out facing);
                    var sb = new StringBuilder("t=" + t.ToString("0.00") + " main x=" + Mathf.RoundToInt(feet.x) + " locked=" + village.IsLocked(id) + " header: " +
                                               (_menu != null ? _menu.headerText.text.Replace("\n", " | ") : ""));
                    Log(sb.ToString());
                }
                if (shot < shotsAt.Length && t >= shotsAt[shot]) { Shoot(name + "_" + shotsAt[shot].ToString("0.0")); shot++; }
                if (t < _pushSeconds) village.Tick(new Vector2(1f, 0f), Time.unscaledDeltaTime);                 // the stick, over the stick of the real joystick (zero in this run)
                await Task.Yield();
            }
        }

        BbMenuUi _menu;

        /// <summary>Biến hình alone (the server refuses it while another bonus of HP, e.g. the whistle, is on).</summary>
        async Task AuditMonkey(BbMenuUi menu, BbTraining training, BbVillageView village)
        {
            menu.DemoSelect(1);
            await Wait(2.5f);
            village.TeleportMain(1850f);
            await WaitFor(() => training.Active, 10f);
            training.Focus("moc_nhan");
            await Wait(1.5f);
            training.DebugUse(13);
            await Observe("monkey", 6f, village, 0.3f, 0.8f, 1.3f, 1.6f, 1.9f, 2.4f, 3.5f, 5.0f);
            Log("final state: " + Where(village));
        }

        /// <summary>The Xayda skills (Huýt sáo first: nothing else may use it before).</summary>
        async Task AuditXayda(BbMenuUi menu, BbTraining training, BbVillageView village)
        {
            menu.DemoSelect(1);                                              // Xayda
            await Wait(2.5f);
            village.TeleportMain(1850f);
            await WaitFor(() => training.Active, 10f);
            training.Focus("moc_nhan");
            await Wait(1.5f);
            Log("main is now: " + Where(village));
            training.DebugUse(21);
            await Observe("whistle", 4f, village, 0.15f, 0.4f, 0.8f, 1.3f, 2.0f, 3.0f);
            await Wait(1f);
            training.DebugUse(14);
            await Observe("selfdestruct", 4.5f, village, 0.4f, 1.2f, 2.0f, 2.35f, 2.6f, 3.1f, 3.8f);
            await Wait(5f);
            training.DebugUse(23);
            await Observe("hold", 4.5f, village, 0.2f, 0.5f, 0.9f, 1.5f, 2.5f, 3.5f);
            await Wait(2f);
            training.DebugUse(13);
            await Observe("monkey", 6f, village, 0.3f, 0.9f, 1.4f, 1.8f, 2.6f, 4.0f);
            Log("final state: " + Where(village));
        }

        async Task Run()
        {
            string user = Arg("-bbUser", "play" + new System.Random().Next(100000, 999999));
            const string pass = "password123";
            string host = Arg("-bbHost", "127.0.0.1");
            int port = int.Parse(Arg("-bbPort", "7443"));
            var client = new BbClient();
            await client.ConnectAsync(host, port, true, "playsmoke", "editor");
            var reg = await client.RegisterAsync(user, pass);
            Log("register " + user + ": " + reg.Ok);
            var login = await client.LoginWithPasswordAsync(user, pass, "playsmoke");
            if (!login.Ok) throw new Exception("login failed " + login.Error);
            await WaitFor(() => { client.Pump(); return client.Snapshot != null; }, 8f);
            Log("logged in, account " + login.AccountId + ", characters " + client.Snapshot.Characters.Count);

            // the gold is put on the account from outside (a SQL statement), then the skills are learned
            File.WriteAllText(Path.Combine(_dir, "play_wait.txt"), user);
            string go = Path.Combine(_dir, "play_go.txt");
            if (File.Exists(go)) File.Delete(go);
            Log("waiting for " + go);
            if (!await WaitFor(() => File.Exists(go), 300f)) throw new Exception("nobody put the gold on the account");
            BattleBall.Proto.CharacterInfo earth = null;
            foreach (var c in client.Snapshot.Characters) if (c.Race == "EARTH") earth = c;
            bool beams = Arg("-bbScenario", "") == "beams", gong = Arg("-bbScenario", "") == "gong" || Arg("-bbScenario", "") == "fusion" || Arg("-bbScenario", "") == "ult" || Arg("-bbScenario", "") == "autoult" || Arg("-bbScenario", "") == "ultvar";
            bool earthRun = Arg("-bbScenario", "") != "auditXayda" && Arg("-bbScenario", "") != "auditMonkey" && !beams && !gong, xaydaRun = Arg("-bbScenario", "") != "auditEarth" && !beams && !gong;
            foreach (uint skill in gong ? new uint[] { 10, 24 } : beams ? new uint[] { 1 } : earthRun ? new uint[] { 1, 1, 1, 10, 24, 6, 9 } : new uint[0])
            {
                var r = await client.UpgradeSkillAsync(earth.CharacterId, skill);
                Log("learn/raise skill " + skill + ": " + r.Ok + " " + r.Error);
                client.Pump();
            }
            BattleBall.Proto.CharacterInfo xayda = null;
            foreach (var c in client.Snapshot.Characters) if (c.Race == "XAYDA") xayda = c;
            foreach (uint skill in gong ? new uint[0] : beams ? new uint[] { 5 } : Arg("-bbScenario", "") == "auditMonkey" ? new uint[] { 13 } : xaydaRun ? new uint[] { 13, 14, 21, 23 } : new uint[0])
            {
                var r = await client.UpgradeSkillAsync(xayda.CharacterId, skill);
                Log("Xayda: learn skill " + skill + ": " + r.Ok + " " + r.Error);
                client.Pump();
            }
            if (beams || gong)
            {
                BattleBall.Proto.CharacterInfo namek = null;
                foreach (var c in client.Snapshot.Characters) if (c.Race == "NAMEC") namek = c;
                if (!gong)
                {
                    var r = await client.UpgradeSkillAsync(namek.CharacterId, 3u);
                    Log("Namek: learn skill 3: " + r.Ok + " " + r.Error);
                    client.Pump();
                }
            }
            await WaitFor(() => { client.Pump(); return false; }, 0.5f);
            client.EventReceived += OnServer;
            BbSession.Begin(client, user);
            SceneManager.LoadScene(BbSession.MenuScene);
            await Wait(1.0f);
            var menu = FindObjectOfType<BbMenuUi>();
            var training = FindObjectOfType<BbTraining>();
            var village = FindObjectOfType<BbVillageView>();
            if (menu == null || training == null || village == null) throw new Exception("the menu scene did not come up");
            await WaitFor(() => village.PartyCount > 0, 8f);
            menu.SetPanelOpen(false, true);
            Log("menu scene up: " + Where(village));

            _training = training;
            _menu = menu;
            string scenario = Arg("-bbScenario", "");
            if (scenario == "shop") { await ShopRun(menu, village); return; }
            if (scenario == "gear") { await GearRun(menu, village); return; }
            if (scenario == "gen") { await GenRun(menu, village, client); return; }
            if (scenario == "smith") { await SmithRun(menu, village, client); return; }
            if (scenario == "attend") { await AttendRun(menu, village, client); return; }
            if (scenario == "quest") { await QuestRun(menu, training, village, client); return; }
            if (scenario == "smithstar") { await SmithStarRun(menu, village, client); return; }
            if (scenario == "focus") { await FocusRun(menu, training, village); return; }
            if (scenario == "dungeon") { await DungeonRun(menu, training, village, client); return; }
            if (scenario == "dungeonape") { await DungeonApeRun(menu, training, village, client); return; }
            if (scenario == "dungeonlevels") { await DungeonLevelsRun(menu, training, village); return; }
            if (scenario == "dungeonfight") { await DungeonFightRun(menu, training, village, client); return; }
            if (scenario == "dungeonfusion") { await DungeonFusionRun(menu, training, village, client); return; }
            if (scenario == "trainswitch") { await TrainSwitchRun(menu, training, village); return; }
            if (scenario == "dungeonswitch") { await DungeonSwitchRun(menu, training, village, client); return; }
            if (scenario == "dungeonui") { await DungeonUiRun(menu, training, village, client); return; }
            if (scenario == "dungeonresume") { await DungeonResumeRun(menu, training, village, client); return; }
            if (scenario == "sights") { await SightsRun(menu, village); return; }
            if (scenario == "gearfuse") { await GearFuseRun(menu, training, village, client); return; }
            if (scenario == "bunma") { await BunmaRun(menu, village); return; }
            if (scenario == "beams") { await Beams(menu, training, village); return; }
            if (scenario == "gong") { await Gong(menu, training, village, client); return; }
            if (scenario == "order") { await Order(menu, training, village, client); return; }
            if (scenario == "shield") { await ShieldRun(menu, training, village, client); return; }
            if (scenario == "fusionfx") { await FusionFxRun(menu, village); return; }
            if (scenario == "ultvar") { await UltVariants(menu, training, village, client); return; }
            if (scenario == "autoult") { await AutoUlt(menu, training, village, client); return; }
            if (scenario == "ult") { await Ultimates(menu, training, village, client); return; }
            if (scenario == "fusion") { await Fusion(menu, training, village, client); return; }
            if (scenario == "auditEarth") { await AuditEarth(menu, training, village); return; }
            if (scenario == "auditMonkey") { await AuditMonkey(menu, training, village); return; }
            if (scenario == "auditXayda") { await AuditXayda(menu, training, village); return; }
            // the dummies are in view of the party (the main character is put next to the first one); nothing else is done
            village.TeleportMain(1500f);
            await Wait(0.5f);
            // 1. nobody touched anything: do the AI fight by themselves (a session starts, they walk to the dummies)?
            await Observe("auto", 6f, village, 5f);
            Log("session active after 6 s without any action: " + training.Active);

            // 2. the menu is opened: does the fight go on?
            menu.SetPanelOpen(true, true);
            await Observe("menu_open", 3f, village, 2.5f);
            Log("session active with the menu open: " + training.Active);
            menu.SetPanelOpen(false, true);

            // 3. the far dummy is focused: the near one stands in its way
            training.Focus("saibamen");
            await WaitFor(() => training.Active, 5f);
            Log("session active: " + training.Active);
            await Wait(0.5f);
            foreach (uint skill in new uint[] { 1, 10, 24 })
            {
                training.DebugUse(skill);
                await Observe("skill" + skill, skill == 24 ? 5f : 3.5f, village, 0.5f, 1.2f, 2.2f, 2.8f, 3.4f);
                await Wait(skill == 10 ? 8f : 0.3f);                  // the Ki comes back before the ultimate
            }
            Log("final state: " + Where(village));
        }
    }
}
