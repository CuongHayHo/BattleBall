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
            menu.DemoNpc(village.Map.Npcs.IndexOf(uron));
            bool opened = await WaitFor(() => menu.shop.IsOpen, 8f);
            await Wait(0.8f);
            Log("shop open=" + opened + " rows=" + menu.shop.DebugRowCount + " menu on the right=" + (menu.panelRect.anchorMin.x > 0.5f));
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
            bool beams = Arg("-bbScenario", "") == "beams", gong = Arg("-bbScenario", "") == "gong" || Arg("-bbScenario", "") == "fusion" || Arg("-bbScenario", "") == "ult";
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
            if (scenario == "beams") { await Beams(menu, training, village); return; }
            if (scenario == "gong") { await Gong(menu, training, village, client); return; }
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
