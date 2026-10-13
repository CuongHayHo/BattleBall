using System.Collections.Generic;
using System.IO;
using BattleBall.Net;
using BattleBall.Proto;
using ProtoCharacter = BattleBall.Proto.CharacterInfo;
using NroRig;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BattleBall.EditorTools
{
    /// <summary>
    /// Renders the menu scene with made-up player data to Logs/menu_*.png (the tabs, the skin popup, the Chức năng tab), to check the layout without a server.
    /// The menu is made of UI only, so one manual camera render is enough. Needs a graphics device: run Unity WITHOUT -nographics.
    /// Batch: -executeMethod BattleBall.EditorTools.BbMenuSceneCapture.CaptureBatch
    /// </summary>
    public static class BbMenuSceneCapture
    {
        [MenuItem("BattleBall/Capture Menu Screenshots")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(BbLoginSceneBuilder.MenuScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory("Logs");
            var ui = Object.FindObjectOfType<BbMenuUi>();
            var cam = Camera.main;
            NroSpriteResolver.Instance.Initialize();            // Awake does not run outside play mode
            NroCostumeCatalog.Instance.Initialize();

            var snapshot = Fake(false);
            // tab 0 = Trái Đất, 1 = Xayda, 2 = Namếc
            for (int page = 0; page < 3; page++)
            {
                ui.ShowDemo(snapshot, page);
                Canvas.ForceUpdateCanvases();
                BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_" + page + ".png");
            }

            // the earring merged Trái Đất (main) and Xayda into one tab
            ui.ShowDemo(snapshot, 0);
            ui.DemoFuse(0, 1);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_fused.png");

            // while fused the characters keep their own tabs (here Namếc), and the earring there starts another fusion instead of splitting this one
            ui.ShowDemo(snapshot, 0);
            ui.DemoFuse(0, 1);
            // the head of the fusion skin has three frames: at double speed the second and third come 0.08 s and 0.16 s later
            ui.view.Tick(0.085f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_fused_head2.png");
            ui.view.Tick(0.085f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_fused_head3.png");
            ui.DemoSelect(2);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_fused_other_tab.png");
            ui.DemoSelect(1);          // the partner's own tab: the village still shows the fused form
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_fused_partner_tab.png");

            // no earring yet: the first bag slot is faded and shows the fragments
            var poor = Fake(true);
            poor.Potara.Owned = false;
            poor.Potara.Fragments = 37;
            ui.ShowDemo(poor, 0);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_no_earring.png");

            // Xayda is level 50 or more, Namếc is not: both planets are listed, Namếc cannot be chosen
            var mixed = Fake(true);
            mixed.Characters[1].Level = 12;
            ui.ShowDemo(mixed, 0);
            ui.DemoEarring();
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_chooser_partial.png");

            // all three characters are level 50 or more: the earring asks for the planet
            var all = Fake(true);
            ui.ShowDemo(all, 0);
            ui.DemoEarring();
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_chooser.png");

            // splitting the fusion with the earring: the fusion skin #1816 comes off and is back in the bag (page 2), the Earth tab opens
            ui.ShowDemo(all, 0);
            ui.DemoFuse(0, 1);
            ui.DemoEarring();
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_split_fusion.png");
            ui.ChangeBagPageForDemo(1);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_split_fusion_bag2.png");

            // the skin popup: the fusion skin #1816 can only be used by the main character of a fusion
            ui.ShowDemo(all, 0);
            ui.DemoOpenSkin(1816);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_skin_popup.png");
            ui.popup.Hide();
            ui.DemoFuse(0, 1);
            ui.DemoWornSkin();                                    // the fusion form wears #1816: it sits in Hỗ trợ 5 and the popup offers "Tháo"
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_skin_popup_fused.png");
            ui.DemoUnequip();                                     // taken off: the slot is empty again and the skin is back in the bag
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_skin_taken_off.png");
            ui.popup.Hide();
            ui.ShowDemo(all, 0);
            ui.DemoOpenSkin(435);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_skin_popup_plain.png");
            ui.DemoPin(435);                                      // pinned: the skin goes to the front of the bag, on every tab
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_bag_pinned.png");
            ui.DemoSelect(1);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_bag_pinned_other_tab.png");
            ui.DemoDiscardAsk(435);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_skin_discard_ask.png");
            ui.popup.Hide();
            ui.DemoSelect(0);
            ui.DemoWornSkin();
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_skin_popup_worn.png");
            ui.popup.Hide();

            // the village: the panel folded away (the main character stands next to the two AI characters), and an NPC popup
            ui.ShowDemo(all, 0);
            ui.SetPanelOpen(false, true);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_village_folded.png");
            // walk to the right for two seconds with the joystick pushed (the followers come after, the view scrolls), then jump
            for (int i = 0; i < 120; i++) ui.village.Tick(new Vector2(1f, 0f), 1f / 60f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_village_walk.png");
            ui.village.Tick(new Vector2(0.6f, 1f), 1f / 60f);
            for (int i = 0; i < 18; i++) ui.village.Tick(new Vector2(0.6f, 0f), 1f / 60f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_village_jump.png");
            // hold up in the air: jump, then fly (the main character and the AI use the flying pose and rise), let go of the stick sideways to hover, then sink
            ui.village.Tick(new Vector2(0f, 1f), 1f / 60f);
            for (int i = 0; i < 45; i++) ui.village.Tick(new Vector2(0.4f, 1f), 1f / 60f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_village_fly.png");
            ui.village.Tick(Vector2.zero, 1f / 60f);              // stick let go in the air: it sinks, the AI after it
            for (int i = 0; i < 60; i++) ui.village.Tick(Vector2.zero, 1f / 60f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_village_sinking.png");
            for (int i = 0; i < 240; i++) ui.village.Tick(Vector2.zero, 1f / 60f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_village_landed.png");
            ui.SetPanelOpen(true, true);
            ui.ShowDemo(all, 1);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_village_xayda.png");
            // training with the dummies (docs/architecture/BattleBallServer.md 15.12): the panel folded, the main character next to the Mộc nhân and the AI on both
            // sides of it; the HP bar with the name at the top, the arrow coloured by the HP left (63%: green, 25%: red), damage numbers, the skill buttons
            ui.ShowDemo(all, 0);
            ui.SetPanelOpen(false, true);
            ui.village.DemoPlaceMain("moc_nhan", -150f);
            // before any target: the skill buttons are already there (hidden only while the panel or an NPC popup is open)
            ui.training.DemoPreview();
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_village_skills.png");
            ui.SetPanelOpen(true, true);
            ui.training.DemoPreview();                                   // Refresh runs every frame in the game
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_village_skills_hidden.png");
            ui.SetPanelOpen(false, true);
            ui.training.DemoPreview();
            ui.training.DemoShow("moc_nhan", 0.63f, DemoSkills());
            ui.training.DemoAuto(true);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_training.png");
            ui.training.DemoAuto(false);
            ui.training.Leave();
            ui.village.DemoPlaceMain("saibamen", 150f);
            ui.training.DemoShow("saibamen", 0.25f, DemoSkills());
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_training_saibamen.png");
            ui.training.Leave();
            // the poses of the skills (Nro skill paints): the main character in Kick_1 of a punch combo, the first AI holding Kame_Release_Fly (the charge of
            // Quả cầu kênh khi), the second holding Charge_Ki (the wait of Biến hình); the log lists the combos of the punch at level 7
            ui.village.DemoPlaceMain("moc_nhan", -200f);
            ulong[] ids = new ulong[3];
            for (int i = 0; i < 3; i++) { Vector2 f; int dir; ui.village.PartyMember(i, out ids[i], out f, out dir); }
            var charge = BbSkillPoses.Charge(10, 1, false, false, 2f);
            var monkey = BbSkillPoses.Monkey(1);
            ui.village.PlayAction(ids[0], new[] { (int)NroPose.Kick_1 }, 10f);
            ui.village.PlayAction(ids[1], new[] { charge[30] }, 10f);
            ui.village.PlayAction(ids[2], new[] { monkey[monkey.Length - 1] }, 10f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_training_poses.png");
            var combos = new System.Text.StringBuilder("Skill poses loaded: " + BbSkillPoses.Loaded).AppendLine();
            for (int i = 0; i < 6; i++) combos.Append("punch lv7: ").Append(string.Join(",", BbSkillPoses.Use(0, 7, false))).AppendLine();
            combos.Append("Quả cầu kênh khi lv1 (2 s): ").Append(string.Join(",", charge)).AppendLine();
            combos.Append("Biến hình lv1: ").Append(string.Join(",", monkey)).AppendLine();
            combos.Append("Makankosappo lv1 in the air (Namếc, 3 s): ").Append(string.Join(",", BbSkillPoses.Charge(11, 1, true, false, 3f)));
            File.WriteAllText("Logs/skill_poses_check.txt", combos.ToString());
            // the effects of the skills (Nro skill paints): Kamejoko at its fourth pose with its dart, Kaioken, Makankosappo, Masenko
            foreach (var spec in new[] { new object[] { 1u, 1u, 3f, "fx_kamejoko" }, new object[] { 9u, 7u, 8f, "fx_kaioken" }, new object[] { 3u, 1u, 3f, "fx_masenko" },
                                         new object[] { 5u, 5u, 4f, "fx_antomic" }, new object[] { 12u, 1u, 6f, "fx_taidong" } })
            {
                ui.village.DemoPlaceMain("moc_nhan", -330f);
                Vector2 f0; int d0;
                ui.village.PartyMember(0, out ids[0], out f0, out d0);
                ui.training.DemoSkill((uint)spec[0], (uint)spec[1], (float)spec[2], f0 + new Vector2(260f, 60f));
                Canvas.ForceUpdateCanvases();
                BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_training_" + (string)spec[3] + ".png");
            }
            // the weather: sun, night, rain, heavy rain at night
            if (ui.village.Weather != null)
            {
                foreach (var w in new[] { new object[] { 0f, 0f, "weather_sun" }, new object[] { 1f, 0f, "weather_night" }, new object[] { 0f, 0.9f, "weather_rain" }, new object[] { 1f, 0.9f, "weather_storm" } })
                {
                    ui.village.Weather.Force((float)w[0], (float)w[1]);
                    Canvas.ForceUpdateCanvases();
                    BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_" + (string)w[2] + ".png");
                }
                ui.village.Weather.Force(0f, 0f);
            }
            // Đẻ trứng (the pet), Quả cầu kênh khí (the rising ball), Tự phát nổ (the white blast), Biến hình (the great ape)
            ui.village.DemoPlaceMain("moc_nhan", -330f);
            ui.training.DemoPet(8);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_training_pet.png");
            ui.training.Leave();
            ui.village.DemoPlaceMain("moc_nhan", -330f);
            ui.training.DemoBomb(1.6f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_training_bomb.png");
            ui.training.Leave();
            ui.training.DemoFlash(0.45f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_training_blast_a.png");
            ui.training.DemoFlash(0.8f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_training_blast_b.png");
            ui.training.Leave();
            ui.training.DemoFlash(5f);                // the white of the blast is over (Leave does not hide it until the next frame)
            ui.village.DemoPlaceMain("moc_nhan", -330f);
            { Vector2 fa; int da; ulong ma; ui.village.PartyMember(0, out ma, out fa, out da); ui.training.DemoApe(ma, 3); }
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_training_ape.png");
            ui.training.Leave();
            // the skills with a long cooldown, one shot each (what the player sees of them)
            foreach (uint sk in new uint[] { 6, 7, 8, 10, 11, 12, 13, 14, 18, 19, 20, 21, 22, 23 })
            {
                ui.village.DemoPlaceMain("moc_nhan", -330f);
                Vector2 f0; int d0;
                ui.village.PartyMember(0, out ids[0], out f0, out d0);
                ui.training.DemoSkill(sk, 1, 6f, f0 + new Vector2(260f, 60f));
                Canvas.ForceUpdateCanvases();
                BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_training_sk" + sk + ".png");
            }
            // the three ultimates (Nro Effect_End): the charge, then the beam / the balls / the jar
            foreach (var u in new[] { new object[] { 24u, 30, "ult24_charge" }, new object[] { 24u, 75, "ult24_beam" }, new object[] { 25u, 66, "ult25_balls" },
                                      new object[] { 25u, 62, "ult25_start" }, new object[] { 26u, 30, "ult26_charge" }, new object[] { 26u, 90, "ult26_open" },
                                      new object[] { 26u, 130, "ult26_vortex" }, new object[] { 26u, 190, "ult26_wisp" }, new object[] { 26u, 230, "ult26_closed" } })
            {
                ui.village.DemoPlaceMain("moc_nhan", -330f);
                ui.training.DemoUltimate((uint)u[0], 2f, (int)u[1], "moc_nhan");
                Canvas.ForceUpdateCanvases();
                BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_training_" + (string)u[2] + ".png");
            }
            ui.village.DemoPlaceMain("moc_nhan", -150f);
            ui.training.DemoHit("moc_nhan", 0, 1, 1);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_training_fx_hit.png");
            ui.village.PlayAction(ids[0], new[] { 0 }, 0.01f);
            ui.village.PlayAction(ids[1], new[] { 0 }, 0.01f);
            ui.village.PlayAction(ids[2], new[] { 0 }, 0.01f);
            ui.SetPanelOpen(true, true);
            ui.ShowDemo(all, 0);
            ui.DemoNpc(2);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_npc_popup.png");
            ui.popup.Hide();

            // the Chức năng tab: menu, mails, one mail, notices, account, password form
            var mails = FakeMails();
            var notices = FakeNotices();
            ui.ShowDemoFunctions(all, mails, notices, 0);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_functions.png");
            ui.ShowDemoFunctions(all, mails, notices, 1);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_mails.png");
            ui.functions.DemoOpenMail(0);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_mail_popup.png");
            ui.popup.Hide();
            ui.ShowDemoFunctions(all, mails, notices, 2);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_notices.png");
            ui.functions.DemoOpenNotice(0);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_notice_popup.png");
            ui.popup.Hide();
            ui.ShowDemoFunctions(all, mails, notices, 3);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_account.png");
            ui.ShowDemoFunctions(all, mails, notices, 4);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_password.png");
            ui.ShowDemoFunctions(all, mails, notices, 5);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_config.png");

            // the Aura tab: the highest character is level 60, so the auras up to level 60 are open; #59 is worn (level 16), #28 needs level 99
            ui.ShowDemo(all, 0);
            ui.DemoSelectAura();
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_aura_tab.png");
            ui.auras.DemoPageOf(28);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_aura_tab_last.png");
            ui.auras.DemoTap(59);
            ui.popup.preview.Tick(0.4f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_aura_popup_worn.png");
            ui.popup.Hide();
            ui.auras.DemoTap(28);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_aura_popup_locked.png");
            ui.popup.Hide();
            ui.DemoSelect(0);                                                   // back on a character: the aura stands around it and around the party in the village
            for (int i = 0; i < 6; i++) ui.village.Tick(Vector2.zero, 0.1f);
            ui.view.Tick(0.3f);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_aura_on_character.png");

            // the Kỹ năng tab: the three characters in one list; the details of Kamejoko (level 5, the next level costs 8.000), then learning Masenko of Namếc
            ui.ShowDemo(all, 0);
            ui.DemoSelectSkills();
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_skills.png");
            ui.skills.scroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_skills_end.png");
            ui.skills.DemoDetails("EARTH", 1);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_skill_popup.png");
            ui.popup.Hide();
            ui.DemoUpgradeSkill("NAMEC", 3);
            ui.skills.scroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_skills_learned.png");

            // gear on the characters (docs 15.19): worn pieces in the five slots, the bag in gear mode, the popup of a piece of the bag and of a worn piece
            var gearSnap = Fake(true);
            gearSnap.Characters[0].EquippedItems.Add(Piece(101, "Áo Thần Linh Trái Đất", 4647, "EARTH", 1, "ARMOR", 2500, 15, "Giáp cộng thẳng"));
            gearSnap.Characters[0].EquippedItems.Add(Piece(102, "Găng Hủy Diệt Trái Đất", 6318, "EARTH", 2, "GLOVES", 30000, 0, "Sức đánh cộng thẳng"));
            foreach (var it in gearSnap.Characters[0].EquippedItems) gearSnap.Characters[0].Equipment.Add(new EquipSlotInfo { Slot = it.Slot, ItemId = it.ItemId });
            gearSnap.Materials.Add(new MaterialAmount { TemplateId = 201, Quantity = 3, Name = "Đá nâng cấp", IconId = 10204, Kind = "UPGRADE_STONE", Note = "+5% chỉ số cơ bản mỗi bậc, tối đa 15 bậc" });
            gearSnap.Materials.Add(new MaterialAmount { TemplateId = 204, Quantity = 4, Name = "Đá GEN", IconId = 10208, Kind = "GEN_STONE", Note = "Đổi một dòng GEN sang bộ ngẫu nhiên" });
            gearSnap.Materials.Add(new MaterialAmount { TemplateId = 205, Quantity = 1, Name = "Đá mở ô GEN", IconId = 10209, Kind = "GEN_SLOT_STONE", Note = "Mở thêm một ô GEN" });
            gearSnap.GenRules = new GenRulesInfo { RerollStones = 1, MaxSlots = 3, StoneTemplateId = 204, SlotStoneTemplateId = 205 };
            AddGenSet(gearSnap, "SONGOKU", "Set Sôngôku", "EARTH", Tier(5, "Kamejoko +100% sát thương"));
            AddGenSet(gearSnap, "THIEN_XIN_HANG", "Set Thiên Xin Hăng", "EARTH", Tier(5, "Thái dương hạ san: thời gian choáng x2"));
            AddGenSet(gearSnap, "KAIO", "Set Thần Vũ Trụ Kaio", "EARTH", Tier(2, "Tăng 10% tỷ lệ chí mạng"), Tier(4, "Kaioken tốn 5% HP thay vì 10%"), Tier(5, "Kaioken +30% sát thương, chỉ tốn 3% HP"));
            SetGens(gearSnap.Characters[0].EquippedItems[0], 2, 2, "SONGOKU|Set Sôngôku", "KAIO|Set Thần Vũ Trụ Kaio");
            SetGens(gearSnap.Characters[0].EquippedItems[1], 3, 0, "SONGOKU|Set Sôngôku", "KAIO|Set Thần Vũ Trụ Kaio", "THIEN_XIN_HANG|Set Thiên Xin Hăng");
            gearSnap.Characters[0].ActiveGens.Add(new GenActive { GenKey = "SONGOKU", Pieces = 2 });
            gearSnap.Characters[0].ActiveGens.Add(new GenActive { GenKey = "KAIO", Pieces = 2 });
            ui.ShowDemo(gearSnap, 0);
            var genPiece = Piece(201, "Quần Thần Linh Trái Đất", 4648, "EARTH", 1, "PANTS", 200000, 0, "HP cộng thẳng");
            SetGens(genPiece, 2, 2, "KAIO|Set Thần Vũ Trụ Kaio", "");
            ui.DemoGearBag(new List<ItemInfo>
            {
                genPiece,
                Piece(202, "Giày Thiên Sứ Trái Đất", 10167, "EARTH", 3, "BOOTS", 400000, 5, "Ki cộng thẳng"),
                Piece(203, "Nhẫn Hủy Diệt Trái Đất", 6323, "EARTH", 2, "RING", 37.5, 0, "Sát thương chí mạng"),
                Piece(204, "Áo Thần Linh Namếc", 4657, "NAMEC", 1, "ARMOR", 2500, 0, "Giáp cộng thẳng"),
                Piece(205, "Quần Hủy Diệt Xayda", 6311, "XAYDA", 2, "PANTS", 300000, 0, "HP cộng thẳng"),
            });
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_gear_bag.png");
            ui.DemoGearBagSlot(0);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_gear_popup.png");
            ui.popup.Hide();
            ui.DemoGearBagSlot(2);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_gen_bag_popup.png");
            ui.popup.Hide();
            ui.DemoSmithMenu();
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_smith_menu.png");
            ui.popup.Hide();
            var stoneUp = gearSnap.Materials[0];
            var stoneGen = gearSnap.Materials[1];
            var stoneSlot = gearSnap.Materials[2];
            ui.DemoSmith(BbSmithPanel.Mode.Upgrade);
            ui.smith.DemoSet(gearSnap.Characters[0].EquippedItems[0], stoneUp, 0);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_smith_upgrade.png");
            ui.smith.Close();
            ui.DemoSmith(BbSmithPanel.Mode.Reroll);
            ui.smith.DemoSet(genPiece, stoneGen, 1);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_smith_reroll.png");
            ui.smith.Close();
            ui.DemoSmith(BbSmithPanel.Mode.Unlock);
            ui.smith.DemoSet(genPiece, null, 0);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_smith_unlock.png");
            ui.smith.Close();

            // the daily check-in of the elder (docs 15.25): his dialog and the table of the month
            var attInfo = FakeAttendance(false);
            ui.DemoAttendanceDialog(attInfo);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_attendance_dialog.png");
            ui.popup.Hide();
            ui.DemoAttendance(FakeAttendance(true));
            ui.attendance.DemoSelect(14);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_attendance_table.png");
            ui.attendance.Close();

            // the quests of Ông Gôhan (docs 15.32): his dialog, the list and the skill cards in the Kỹ năng tab
            var quests = all;
            quests.Quests.Clear();
            quests.Quests.AddRange(FakeQuests());
            quests.Materials.Add(new MaterialAmount { TemplateId = 206, Quantity = 14, Name = "Thẻ nâng cấp", IconId = 13538, Kind = "SKILL_CARD" });
            ui.DemoQuestDialog(quests);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_quest_dialog.png");
            ui.popup.Hide();
            ui.DemoQuest(quests);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_quest_list.png");
            ui.quest.Close();
            ui.popup.Hide();
            ui.DemoGearSlot(0);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_gear_worn_popup.png");
            ui.popup.Hide();
            ui.DemoGearSlot(2);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_gen_worn_full.png");
            ui.popup.Hide();

            // Urôn's shop (docs 15.15): the shop on the left, the menu moved to the right, the popup of an item, then the popup of a skin of the bag on the right
            ui.ShowDemo(all, 0);
            ui.village.TeleportMain(2550f);
            ui.DemoShop(FakeShop());
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_shop.png");
            ui.shop.DemoTap(1566);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_shop_popup.png");
            ui.popup.Hide();
            ui.DemoOpenSkin(431);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_shop_bag_popup.png");
            ui.popup.Hide();
            ui.shop.Close();
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_shop_closed.png");

            // Bunma's shop: clothes and stones
            ui.village.TeleportMain(800f);
            ui.DemoShop(FakeBunma());
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_bunma.png");
            ui.shop.DemoTapIndex(1, 0);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_bunma_popup.png");
            ui.popup.Hide();
            ui.shop.DemoTapIndex(3, 4);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_bunma_exchange.png");
            ui.popup.Hide();
            ui.shop.DemoTapIndex(4, 0);
            Canvas.ForceUpdateCanvases();
            BbLoginSceneCapture.Shoot(cam, 1280, 720, "Logs/menu_bunma_stones.png");
            ui.popup.Hide();
            ui.shop.Close();

        }

        /// <summary>Made-up quests: the one of the chain being done and the six daily ones in different states.</summary>
        static List<QuestEntry> FakeQuests()
        {
            System.Func<string, string, string, string, uint, uint, string, ulong, uint, QuestEntry> make = (id, kind, title, desc, progress, target, npc, gold, gems) =>
                new QuestEntry { Id = id, Kind = kind, Title = title, Description = desc, Progress = progress, Target = target, NpcId = npc, Gold = gold, Gems = gems };
            var list = new List<QuestEntry>();
            var t = make("t05", "TUTORIAL", "Nâng cấp đồ", "Đến Bà Hạt Mít, đặt món đồ và đá nâng cấp vào bảng rồi nâng cấp.", 0, 1, "shop", 2000, 5);
            t.Items.Add(new RewardItem { TemplateId = 206, Name = "Thẻ nâng cấp", IconId = 13538, Quantity = 4 });
            t.Items.Add(new RewardItem { TemplateId = 204, Name = "Đá GEN", IconId = 10208, Quantity = 2 });
            list.Add(t);
            var d1 = make("d01", "DAILY", "Điểm danh", "Gặp Ông Trưởng làng và điểm danh hôm nay.", 1, 1, "elder", 500, 1);
            d1.Items.Add(new RewardItem { TemplateId = 206, Name = "Thẻ nâng cấp", IconId = 13538, Quantity = 2 });
            list.Add(d1);
            var d2 = make("d02", "DAILY", "Thắng một phó bản", "Đến Cui và thắng một phó bản.", 0, 1, "cui", 1000, 2);
            d2.Items.Add(new RewardItem { TemplateId = 206, Name = "Thẻ nâng cấp", IconId = 13538, Quantity = 4 });
            list.Add(d2);
            var d3 = make("d03", "DAILY", "Hạ 5 boss phó bản", "Hạ tổng cộng 5 boss trong các phó bản (thắng hay thua đều tính).", 2, 5, "cui", 1500, 3);
            d3.Items.Add(new RewardItem { TemplateId = 206, Name = "Thẻ nâng cấp", IconId = 13538, Quantity = 5 });
            list.Add(d3);
            var d4 = make("d04", "DAILY", "Tập đánh Mộc nhân", "Đến Mộc nhân và đánh thử một lượt.", 1, 1, "moc_nhan", 250, 1);
            d4.Claimed = true;
            d4.Items.Add(new RewardItem { TemplateId = 206, Name = "Thẻ nâng cấp", IconId = 13538, Quantity = 1 });
            list.Add(d4);
            var d6 = make("d06", "DAILY", "Nâng cấp một kỹ năng", "Mở bảng nhân vật, tab Kỹ năng, dùng thẻ để học hoặc nâng một kỹ năng.", 0, 1, "", 500, 1);
            d6.Items.Add(new RewardItem { TemplateId = 206, Name = "Thẻ nâng cấp", IconId = 13538, Quantity = 3 });
            list.Add(d6);
            return list;
        }

        /// <summary>A made-up table of check-in: the 10th of a month of 31 days, the first nine days claimed except the 4th and the 6th.</summary>
        static AttendanceInfo FakeAttendance(bool claimedToday)
        {
            var info = new AttendanceInfo { Year = 2026, Month = 10, Today = 10, DaysInMonth = 31, ClaimedToday = claimedToday };
            uint claimed = 0;
            for (uint day = 1; day <= 31; day++)
            {
                var d = new AttendanceDay { Day = day, Gold = 250, Gems = day == 7 ? 8u : day == 14 ? 13u : 3u };
                if (day == 1 || day == 3 || day == 7 || day == 14) d.Items.Add(new RewardItem { TemplateId = 201, Name = "Đá nâng cấp", IconId = 10204, Quantity = day == 1 ? 1u : day == 3 ? 2u : day == 7 ? 3u : 5u });
                if (day == 5 || day == 12) d.Items.Add(new RewardItem { TemplateId = 204, Name = "Đá GEN", IconId = 10208, Quantity = day == 5 ? 1u : 2u });
                if (day == 10) d.Items.Add(new RewardItem { TemplateId = 205, Name = "Đá mở ô GEN", IconId = 10209, Quantity = 1 });
                d.Claimed = day < 10 && day != 4 && day != 6 || (day == 10 && claimedToday);
                if (d.Claimed) claimed++;
                info.Days.Add(d);
            }
            info.ClaimedCount = claimed;
            return info;
        }

        static GenTierInfo Tier(uint pieces, string text) { return new GenTierInfo { Pieces = pieces, Text = text }; }

        static void AddGenSet(PlayerDataSnapshot snap, string key, string name, string race, params GenTierInfo[] tiers)
        {
            var set = new GenSetInfo { GenKey = key, Name = name, Race = race };
            set.Tiers.AddRange(tiers);
            snap.GenSets.Add(set);
        }

        /// <summary>The GEN lines of a made-up piece: "KEY|Name" per slot, "" for an empty one.</summary>
        static void SetGens(ItemInfo it, uint slots, uint unlockCost, params string[] lines)
        {
            it.GenSlots = slots;
            it.GenUnlockCost = unlockCost;
            for (int i = 0; i < slots; i++)
            {
                string[] parts = i < lines.Length ? lines[i].Split('|') : new string[0];
                it.Gens.Add(new GenLine { LineNo = (uint)i, GenKey = parts.Length > 0 ? parts[0] : "", Name = parts.Length > 1 ? parts[1] : "" });
            }
        }

        /// <summary>A made-up piece of gear as the server describes it.</summary>
        static ItemInfo Piece(ulong id, string name, uint icon, string race, uint tier, string slot, double main, double upgrade, string statName)
        {
            double shown = main * (1 + upgrade / 100.0);
            var it = new ItemInfo { ItemId = id, TemplateId = 1, Slot = slot, UpgradePercent = upgrade, Name = name, IconId = icon, Race = race, Tier = tier, MinLevel = tier == 1 ? 1u : tier == 2 ? 30u : 60u };
            it.MainStat = new StatLine { StatKey = "X", Value = shown, Text = statName + " +" + shown.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) };
            return it;
        }

        /// <summary>Made-up content of Bunma's shop (what the server sends for a character of level 1): five pieces of gear and three stones.</summary>
        static ShopContent FakeBunma()
        {
            var c = new ShopContent { NpcId = "bunma", NpcName = "Bunma", PortraitIcon = 562 };
            string[] setNames = { "Thần Linh", "Hủy Diệt", "Thiên Sứ" };
            uint[][] setIcons = { new uint[] { 4647, 4648, 4650, 4649, 6314 }, new uint[] { 6315, 6316, 6318, 6317, 6323 }, new uint[] { 10165, 10166, 10168, 10167, 10169 } };
            string[] pieces = { "Áo", "Quần", "Găng", "Giày", "Nhẫn" };
            string[] stats = { "Giáp +2500", "HP +200000", "Sức đánh +20000", "KI +200000", "Sát thương chí mạng +25%" };
            for (int set = 0; set < 3; set++)
            {
                var tab = new ShopTab { Name = setNames[set] };
                for (int i = 0; i < 5; i++)
                    tab.Items.Add(new ShopItem { Kind = "GEAR", TemplateId = (uint)(set * 5 + i + 1), Name = pieces[i] + " " + setNames[set], IconId = setIcons[set][i], GoldPrice = (ulong)(6000 * (set + 1)),
                                                 MinLevel = (uint)(set == 0 ? 1 : set == 1 ? 30 : 60), Description = stats[i] + "\nGhép từ 10 mảnh đồ, mua đủ một món." });
                c.Tabs.Add(tab);
            }
            var swap = new ShopTab { Name = "Đổi mảnh" };
            for (int set = 0; set < 3; set++)
                for (int i = 0; i < 5; i++)
                    swap.Items.Add(new ShopItem { Kind = "EXCHANGE", TemplateId = (uint)(set * 5 + i + 1), Name = pieces[i] + " " + setNames[set], IconId = setIcons[set][i], FragmentTemplateId = (uint)(101 + set * 5 + i),
                                                  FragmentCost = 10, Description = stats[i] + "\nĐổi 10 mảnh lấy món này." });
            c.Tabs.Add(swap);
            var stones = new ShopTab { Name = "Đá" };
            stones.Items.Add(new ShopItem { Kind = "MATERIAL", TemplateId = 201, Name = "Đá nâng cấp", IconId = 10204, GoldPrice = 1500, Description = "+5% chỉ số cơ bản mỗi bậc, tối đa 15 bậc" });
            stones.Items.Add(new ShopItem { Kind = "MATERIAL", TemplateId = 202, Name = "Đá chỉ số", IconId = 10206, GoldPrice = 1000, Description = "Cộng % chỉ số tổng; luật ở GDD 6" });
            stones.Items.Add(new ShopItem { Kind = "MATERIAL", TemplateId = 203, Name = "Đá pha lê", IconId = 6467, GemPrice = 500, Description = "Siêu hiếm; chỉ đổi bằng đá quý" });
            c.Tabs.Add(stones);
            return c;
        }

        /// <summary>Made-up content of Urôn's shop (the server sends the real one: every skin that can be sold, tabs by race).</summary>
        static ShopContent FakeShop()
        {
            var c = new ShopContent { NpcId = "uron", NpcName = "Urôn" };
            var any = new ShopTab { Name = "Mọi tộc" };
            foreach (var spec in new object[][] { new object[] { 885u, 100u, 3u }, new object[] { 1566u, 100u, 3u }, new object[] { 430u, 300u, 3u }, new object[] { 431u, 450u, 3u },
                                                  new object[] { 432u, 600u, 3u }, new object[] { 433u, 800u, 3u }, new object[] { 434u, 1000u, 3u } })
            {
                var skin = new SkinInfo { CostumeId = (uint)spec[0], Name = "Cải trang", GenderId = (uint)spec[2], Kind = "SHOP" };
                skin.Stats.Add(new StatLine { StatKey = "ATK_PCT", Value = 5, Text = "Sức đánh +5%" });
                skin.Stats.Add(new StatLine { StatKey = "HP_PCT", Value = 5, Text = "HP +5%" });
                any.Items.Add(new ShopItem { Skin = skin, GemPrice = (uint)spec[1] });
            }
            c.Tabs.Add(any);
            var earth = new ShopTab { Name = "Trái Đất" };
            var fusion = new SkinInfo { CostumeId = 1816, Name = "CT Hợp thể Trái Đất", GenderId = 3, Kind = "FUSION_FORM", FusionRace = "td", FusionMainOnly = true };
            fusion.Stats.Add(new StatLine { StatKey = "ATK_PCT", Value = 100, Text = "Sức đánh +100%" });
            fusion.Stats.Add(new StatLine { StatKey = "HP_PCT", Value = 100, Text = "HP +100%" });
            earth.Items.Add(new ShopItem { Skin = fusion, GemPrice = 1200 });
            c.Tabs.Add(earth);
            c.Tabs.Add(new ShopTab { Name = "Xayda", Items = { new ShopItem { Skin = new SkinInfo { CostumeId = 435, Name = "Cải trang", GenderId = 2, Kind = "SHOP" }, GemPrice = 500 } } });
            return c;
        }

        [System.Serializable] class AuraLayerFile { public int layer, frames; }
        [System.Serializable] class AuraFileItem { public int id, level; public string name; public bool enabled; public AuraLayerFile[] layers; }
        [System.Serializable] class AuraFile { public AuraFileItem[] auras; }

        /// <summary>The auras of the planner table (tools/aura_planner/aura_levels.json) as the server would send them.</summary>
        static void AddAuras(PlayerDataSnapshot s)
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "BattleBallServer", "tools", "aura_planner", "aura_levels.json"));
            if (!System.IO.File.Exists(path)) return;
            var file = JsonUtility.FromJson<AuraFile>(System.IO.File.ReadAllText(path));
            foreach (var a in file.auras)
            {
                if (!a.enabled) continue;
                var info = new AuraInfo { AuraId = (uint)a.id, Name = a.name ?? "", UnlockLevel = (uint)a.level };
                foreach (var l in a.layers) info.Layers.Add(new AuraLayer { Layer = (uint)l.layer, Frames = (uint)l.frames });
                s.Auras.Add(info);
            }
            s.EquippedAuraId = 59;
        }

        public static void CaptureBatch()
        {
            try { Capture(); }
            catch (System.Exception e) { Debug.LogError(e); EditorApplication.Exit(1); return; }
            EditorApplication.Exit(0);
        }

        static PlayerDataSnapshot Fake(bool allHigh)
        {
            var s = new PlayerDataSnapshot
            {
                AccountId = 1, Username = "nguoi_choi1", Gold = 98765, Gems = 4200, BagCount = 37, UnreadMail = 2,
                Potara = new PotaraInfo { Owned = true, Fragments = 100, ItemId = 1819, IconId = 15614, ShardItemId = 1820, ShardIconId = 15613, ShardsNeeded = 100, MinFusionLevel = 50 }
            };
            s.Characters.Add(Character(1, "EARTH", allHigh ? 55u : 12u, 530, 1500, 412, 412, true));
            s.Characters.Add(Character(2, "NAMEC", allHigh ? 52u : 1u, 40, 100, 415, 415, false));
            s.Characters.Add(Character(3, "XAYDA", allHigh ? 60u : 37u, 8400, 13690, 418, 418, true));

            // the skins of the account: the nine starter avatars, a fusion skin, a few shop skins (enough for a second page of the bag)
            for (uint id = 412; id <= 420; id++)
                s.Skins.Add(new SkinInfo { CostumeId = id, Name = "Avatar", GenderId = (id - 412) / 3, Kind = "COMPONENT" });
            var fusion = new SkinInfo { CostumeId = 1816, Name = "CT Hợp thể Trái Đất", GenderId = 3, Kind = "FUSION_FORM", FusionRace = "td", FusionMainOnly = true };
            fusion.Stats.Add(new StatLine { StatKey = "ATK_PCT", Value = 100, Text = "Sức đánh +100%" });
            fusion.Stats.Add(new StatLine { StatKey = "HP_PCT", Value = 100, Text = "HP +100%" });
            fusion.Stats.Add(new StatLine { StatKey = "KI_PCT", Value = 100, Text = "KI +100%" });
            fusion.Stats.Add(new StatLine { StatKey = "CRIT_RATE_PCT", Value = 20, Text = "Tỉ lệ chí mạng (điểm %) +20%" });
            s.Skins.Add(fusion);
            foreach (uint id in new uint[] { 430, 431, 432, 433, 434, 435 })
            {
                var plain = new SkinInfo { CostumeId = id, Name = "Cải trang " + id, GenderId = 3, Kind = "SHOP" };
                plain.Stats.Add(new StatLine { StatKey = "ATK_PCT", Value = 5, Text = "Sức đánh +5%" });
                plain.Stats.Add(new StatLine { StatKey = "HP_PCT", Value = 5, Text = "HP +5%" });
                s.Skins.Add(plain);
            }
            AddAuras(s);
            AddSkillCatalog(s);
            // learned skills: Trái Đất knows its punch at 7 and Kamejoko at 5, Xayda its punch at 3, Namếc nothing yet (the shield is common)
            s.Characters[0].Skills.Add(new SkillState { SkillId = 0, Level = 7 });
            s.Characters[0].Skills.Add(new SkillState { SkillId = 1, Level = 5 });
            s.Characters[0].Skills.Add(new SkillState { SkillId = 6, Level = 1 });
            s.Characters[2].Skills.Add(new SkillState { SkillId = 4, Level = 3 });
            s.Characters[2].Skills.Add(new SkillState { SkillId = 25, Level = 1 });
            return s;
        }

        /// <summary>The skill table of the server (skill_template and skill_level of the database on 2026-10-08) for the Kỹ năng tab without a server.</summary>
        static void AddSkillCatalog(PlayerDataSnapshot s)
        {
            Skill(s, 0, "EARTH", "Chiêu đấm Dragon", 539, 0, 7, "Tăng sức đánh: #%", new double[,] { {0, 100.00, 500, 0.01}, {10, 110.00, 500, 0.03}, {50, 120.00, 500, 0.06}, {100, 130.00, 500, 0.11}, {500, 140.00, 500, 0.23}, {1000, 150.00, 500, 0.46}, {2000, 160.00, 500, 1.00} });
            Skill(s, 1, "EARTH", "Chiêu Kamejoko", 540, 1, 7, "Tăng sức đánh: #%", new double[,] { {500, 150.00, 2000, 0.12}, {1000, 200.00, 2500, 0.23}, {2000, 250.00, 3000, 0.47}, {4000, 300.00, 3500, 0.94}, {8000, 350.00, 4000, 1.88}, {16000, 400.00, 4500, 3.75}, {32000, 450.00, 5000, 5.00} });
            Skill(s, 2, "NAMEC", "Chiêu đấm Demon", 539, 0, 7, "Tăng sức đánh: #%", new double[,] { {0, 95.00, 400, 0.01}, {10, 105.00, 400, 0.03}, {50, 115.00, 400, 0.06}, {100, 125.00, 400, 0.11}, {1000, 135.00, 400, 0.23}, {2000, 145.00, 400, 0.46}, {4000, 155.00, 400, 1.00} });
            Skill(s, 3, "NAMEC", "Chiêu Masenko", 540, 1, 7, "Tăng sức đánh: #%", new double[,] { {500, 100.00, 800, 0.08}, {1000, 110.00, 790, 0.16}, {2000, 120.00, 780, 0.31}, {4000, 130.00, 760, 0.62}, {8000, 140.00, 740, 1.25}, {16000, 150.00, 720, 2.50}, {32000, 160.00, 700, 5.00} });
            Skill(s, 4, "XAYDA", "Chiêu đấm Galick", 539, 0, 7, "Tăng sức đánh: #%", new double[,] { {0, 110.00, 500, 0.01}, {10, 120.00, 500, 0.03}, {50, 130.00, 500, 0.06}, {100, 140.00, 500, 0.11}, {1000, 150.00, 500, 0.23}, {2000, 160.00, 500, 0.46}, {4000, 170.00, 500, 1.00} });
            Skill(s, 5, "XAYDA", "Chiêu Antomic", 540, 1, 7, "Tăng sức đánh: #%", new double[,] { {500, 110.00, 1000, 0.09}, {1000, 140.00, 1200, 0.17}, {2000, 170.00, 1400, 0.33}, {4000, 200.00, 1600, 0.66}, {8000, 230.00, 1800, 1.26}, {16000, 260.00, 2000, 2.50}, {32000, 290.00, 2200, 5.00} });
            Skill(s, 6, "EARTH", "Thái Dương Hạ San", 717, 2, 7, "Thời gian tác dụng: # mili giây", new double[,] { {500, 0.00, 60000, 4.50}, {1000, 0.00, 55000, 4.00}, {2000, 0.00, 50000, 3.50}, {4000, 0.00, 45000, 3.00}, {8000, 0.00, 40000, 2.50}, {16000, 0.00, 35000, 2.00}, {32000, 0.00, 30000, 1.50} });
            Skill(s, 7, "NAMEC", "Trị thương", 724, 2, 7, "Phục hồi #% HP và KI cho đồng đội", new double[,] { {500, 0.00, 30000, 4.00}, {1000, 0.00, 32000, 3.50}, {2000, 0.00, 34000, 3.00}, {4000, 0.00, 38000, 2.50}, {8000, 0.00, 40000, 2.00}, {16000, 0.00, 42000, 1.50}, {32000, 0.00, 44000, 1.00} });
            Skill(s, 8, "XAYDA", "Tái tạo năng lượng", 720, 2, 7, "Tự tái tạo HP MP #%/s", new double[,] { {500, 0.00, 55000, 0.00}, {1000, 0.00, 50000, 0.00}, {2000, 0.00, 45000, 0.00}, {4000, 0.00, 40000, 0.00}, {8000, 0.00, 35000, 0.00}, {16000, 0.00, 30000, 0.00}, {32000, 0.00, 25000, 0.00} });
            Skill(s, 9, "EARTH", "Kaioken", 716, 3, 7, "Tăng sức đánh: #%", new double[,] { {1000, 160.00, 500, 1.67}, {2000, 170.00, 500, 2.41}, {4000, 180.00, 500, 2.78}, {8000, 190.00, 500, 3.33}, {16000, 200.00, 500, 3.89}, {32000, 210.00, 500, 4.44}, {64000, 220.00, 500, 5.00} });
            Skill(s, 10, "EARTH", "Quả cầu kênh khi", 711, 4, 7, "Gây sát thương #%", new double[,] { {2000, 0.00, 360000, 5.00}, {4000, 0.00, 350000, 5.50}, {8000, 0.00, 340000, 6.00}, {16000, 0.00, 330000, 6.50}, {32000, 0.00, 320000, 7.00}, {64000, 0.00, 310000, 7.50}, {128000, 0.00, 300000, 8.00} });
            Skill(s, 11, "NAMEC", "Makankosappo", 723, 3, 7, "Gây sát thương #%", new double[,] { {1000, 0.00, 360000, 0.00}, {2000, 0.00, 350000, 0.00}, {4000, 0.00, 340000, 0.00}, {8000, 0.00, 330000, 0.00}, {16000, 0.00, 320000, 0.00}, {32000, 0.00, 310000, 0.00}, {64000, 0.00, 300000, 0.00} });
            Skill(s, 12, "NAMEC", "Đẻ trứng", 722, 4, 7, "Tạo quái đi theo hỗ trợ", new double[,] { {2000, 0.00, 360000, 2.00}, {4000, 0.00, 390000, 3.00}, {8000, 0.00, 420000, 4.00}, {16000, 0.00, 450000, 5.00}, {32000, 0.00, 480000, 6.00}, {64000, 0.00, 510000, 7.00}, {128000, 0.00, 540000, 8.00} });
            Skill(s, 13, "XAYDA", "Biến hình", 718, 3, 7, "Tăng sức đánh, HP và tốc độ", new double[,] { {1000, 0.00, 300000, 1.00}, {2000, 0.00, 310000, 1.00}, {4000, 0.00, 320000, 1.00}, {8000, 0.00, 330000, 1.00}, {16000, 0.00, 340000, 1.00}, {32000, 0.00, 350000, 1.00}, {64000, 0.00, 360000, 1.00} });
            Skill(s, 14, "XAYDA", "Tự phát nổ", 2248, 4, 7, "Hy sinh, gây sát thương lớn cho kẻ thù", new double[,] { {2000, 0.00, 120000, 5.00}, {4000, 0.00, 120000, 5.00}, {8000, 0.00, 120000, 5.00}, {16000, 0.00, 120000, 5.00}, {32000, 0.00, 120000, 5.00}, {64000, 0.00, 120000, 5.00}, {128000, 0.00, 120000, 5.00} });
            Skill(s, 17, "NAMEC", "Liên hoàn", 3778, 5, 7, "Tăng sức đánh: #%", new double[,] { {1500, 160.00, 350, 0.43}, {3000, 165.00, 345, 0.86}, {6000, 170.00, 340, 1.29}, {12000, 175.00, 335, 1.71}, {24000, 180.00, 330, 2.14}, {48000, 185.00, 335, 2.57}, {96000, 190.00, 330, 3.00} });
            Skill(s, 18, "NAMEC", "Biến Sôcôla", 3780, 6, 7, "Biến quái thành Sôcôla", new double[,] { {1500, 0.00, 30000, 2.20}, {3000, 0.00, 29000, 2.00}, {6000, 0.00, 28000, 1.80}, {12000, 0.00, 27000, 1.60}, {24000, 0.00, 26000, 1.40}, {48000, 0.00, 25000, 1.20}, {96000, 0.00, 24000, 1.00} });
            Skill(s, 19, "COMMON", "Khiên năng lượng", 3784, 7, 7, "Vô hiệu các đòn tấn công", new double[,] { {1000, 0.00, 75000, 5.10}, {2000, 0.00, 80000, 4.80}, {4000, 0.00, 85000, 4.50}, {8000, 0.00, 90000, 4.20}, {16000, 0.00, 95000, 3.90}, {32000, 0.00, 100000, 3.60}, {64000, 0.00, 105000, 3.30} });
            Skill(s, 20, "EARTH", "Dịch chuyển tức thời", 3783, 5, 7, "Dịch chuyển tức thời và gây choáng kẻ thù", new double[,] { {1500, 0.00, 20000, 1.33}, {3000, 0.00, 19000, 1.87}, {6000, 0.00, 18000, 2.67}, {12000, 0.00, 17000, 4.00}, {24000, 0.00, 16000, 5.33}, {48000, 0.00, 15000, 6.67}, {96000, 0.00, 14000, 8.00} });
            Skill(s, 21, "XAYDA", "Huýt sáo", 3781, 5, 7, "Tăng tạm thời +#%HP cho mọi người xung quanh và +1 đòn chí mạng", new double[,] { {1500, 0.00, 210000, 10.00}, {3000, 0.00, 205000, 9.00}, {6000, 0.00, 200000, 8.00}, {12000, 0.00, 195000, 7.00}, {24000, 0.00, 190000, 6.00}, {48000, 0.00, 185000, 5.00}, {96000, 0.00, 180000, 4.00} });
            Skill(s, 22, "EARTH", "Thôi miên", 3782, 6, 7, "Ru ngủ kẻ thù # giây", new double[,] { {1500, 0.00, 30000, 6.00}, {3000, 0.00, 32000, 6.00}, {6000, 0.00, 34000, 6.00}, {12000, 0.00, 36000, 6.00}, {24000, 0.00, 38000, 6.00}, {48000, 0.00, 40000, 6.00}, {96000, 0.00, 42000, 6.00} });
            Skill(s, 23, "XAYDA", "Trói", 3779, 6, 7, "Trói kẻ thù", new double[,] { {1500, 0.00, 15000, 1.25}, {3000, 0.00, 20000, 2.50}, {6000, 0.00, 25000, 3.75}, {12000, 0.00, 30000, 5.00}, {24000, 0.00, 35000, 6.25}, {48000, 0.00, 40000, 7.50}, {96000, 0.00, 45000, 8.00} });
            Skill(s, 24, "EARTH", "Super Kamejoko", 11162, 8, 9, "Tăng sức đánh: #%", new double[,] { {5000, 550.00, 170000, 8.00}, {7500, 600.00, 160000, 7.50}, {11200, 650.00, 150000, 7.00}, {16900, 700.00, 140000, 6.50}, {25300, 750.00, 130000, 6.00}, {38000, 800.00, 120000, 5.50}, {57000, 850.00, 110000, 5.00}, {85400, 900.00, 100000, 4.50}, {128100, 950.00, 90000, 4.00} });
            Skill(s, 25, "XAYDA", "Cađíc liên hoàn chưởng", 11193, 8, 9, "Tăng sức đánh: #%", new double[,] { {5000, 550.00, 170000, 8.00}, {7500, 600.00, 160000, 7.50}, {11200, 650.00, 150000, 7.00}, {16900, 700.00, 140000, 6.50}, {25300, 750.00, 130000, 6.00}, {38000, 800.00, 120000, 5.50}, {57000, 850.00, 110000, 5.00}, {85400, 900.00, 100000, 4.50}, {128100, 950.00, 90000, 4.00} });
            Skill(s, 26, "NAMEC", "Ma phong ba", 11194, 8, 9, "Nhốt đối thủ vào bình chứa", new double[,] { {5000, 0.00, 170000, 8.00}, {7500, 0.00, 160000, 7.50}, {11200, 0.00, 150000, 7.00}, {16900, 0.00, 140000, 6.50}, {25300, 0.00, 130000, 6.00}, {38000, 0.00, 120000, 5.50}, {57000, 0.00, 110000, 5.00}, {85400, 0.00, 100000, 4.50}, {128100, 0.00, 90000, 4.00} });
        }

        static void Skill(PlayerDataSnapshot s, uint id, string race, string name, uint icon, uint slot, uint maxLevel, string description, double[,] levels)
        {
            var e = new SkillCatalogEntry { SkillId = id, Race = race, Name = name, IconId = icon, Slot = slot, MaxLevel = maxLevel, Description = description };
            for (int i = 0; i < levels.GetLength(0); i++)
                e.Levels.Add(new SkillLevelInfo { Level = (uint)(i + 1), GoldCost = (ulong)levels[i, 0], DamagePctAtk = levels[i, 1], CooldownMs = (uint)levels[i, 2], KiCostPct = levels[i, 3], CardCost = (uint)(slot + 1) });
            s.SkillCatalog.Add(e);
        }

        static ProtoCharacter Character(ulong id, string race, uint level, ulong exp, ulong expToNext, uint skin, uint defaultSkin, bool geared)
        {
            var c = new ProtoCharacter
            {
                CharacterId = id, Race = race, Level = level, Exp = exp, ExpToNext = expToNext, DefaultCostumeId = defaultSkin,
                Stats = new CharacterStats { HpMax = 5750 * level + 1000, KiMax = 5750 * level + 1000, Atk = 260 * level, Armor = 220 * level, CritRatePct = 0.1 * level }
            };
            c.Equipment.Add(new EquipSlotInfo { Slot = "SKIN", CostumeId = skin });
            if (geared)
            {
                c.Equipment.Add(new EquipSlotInfo { Slot = "ARMOR", ItemId = 11 });
                c.Equipment.Add(new EquipSlotInfo { Slot = "BOOTS", ItemId = 12 });
                c.Equipment.Add(new EquipSlotInfo { Slot = "SUPPORT_1", SupportInstanceId = 5 });
                c.Bonuses.Add(new StatLine { StatKey = "HP_PCT", Value = 15, Source = "TOTAL" });
                c.Bonuses.Add(new StatLine { StatKey = "KI_PCT", Value = 10, Source = "TOTAL" });
                c.Bonuses.Add(new StatLine { StatKey = "ATK_PCT", Value = 20, Source = "TOTAL" });
                c.Bonuses.Add(new StatLine { StatKey = "CRIT_DMG_PCT", Value = 8.5, Source = "TOTAL" });
                c.Bonuses.Add(new StatLine { StatKey = "LIFESTEAL_PCT", Value = 3, Source = "TOTAL" });
            }
            if (id == 1)
            {
                // the fusion skin #1816 is worn by the fusion form of the Earth character; in a fusion its stats count with the skin's bonuses
                c.Equipment.Add(new EquipSlotInfo { Slot = "FUSION_SKIN", CostumeId = 1816 });
                c.FusionStats = new CharacterStats { HpMax = c.Stats.HpMax * 2, KiMax = c.Stats.KiMax * 2, Atk = c.Stats.Atk * 2, Armor = c.Stats.Armor, CritRatePct = c.Stats.CritRatePct + 20 };
                foreach (var b in c.Bonuses) c.FusionBonuses.Add(new StatLine { StatKey = b.StatKey, Value = b.StatKey == "ATK_PCT" || b.StatKey == "HP_PCT" || b.StatKey == "KI_PCT" ? b.Value + 100 : b.Value, Source = "TOTAL" });
            }
            return c;
        }

        /// <summary>The skill slots of a Trái Đất character as the server sends them in training (slot, skill id, Nro icon of skill_template, level).</summary>
        static List<SkillSlot> DemoSkills()
        {
            var list = new List<SkillSlot>();
            int[,] rows = { { 0, 0, 539, 7 }, { 1, 1, 540, 5 }, { 2, 6, 717, 1 }, { 3, 9, 716, 1 }, { 4, 10, 711, 1 }, { 5, 20, 3783, 1 }, { 6, 22, 3782, 1 }, { 7, 19, 3784, 1 }, { 8, 24, 11162, 1 } };
            for (int i = 0; i < rows.GetLength(0); i++)
                list.Add(new SkillSlot { Slot = (uint)rows[i, 0], SkillId = (uint)rows[i, 1], IconId = (uint)rows[i, 2], Level = (uint)rows[i, 3], CooldownMs = 5000, KiCost = 100 });
            return list;
        }

        static MailList FakeMails()
        {
            var list = new MailList();
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var gift = new MailInfo { MailId = 3, Title = "Quà tặng thử máy chủ", Body = "Cảm ơn bạn đã tham gia thử nghiệm BattleBall. Quà nhỏ gửi bạn.", SentAtMs = (ulong)(now - 3600_000) };
            gift.Attachments.Add(new MailAttachment { Kind = "GOLD", Quantity = 50000 });
            gift.Attachments.Add(new MailAttachment { Kind = "GEMS", Quantity = 300 });
            gift.Attachments.Add(new MailAttachment { Kind = "SKIN", RefId = 1816, Quantity = 1, Name = "Cải trang td" });
            gift.Attachments.Add(new MailAttachment { Kind = "EARRING", Quantity = 1, Name = "Bông tai Porata", IconId = 15614 });
            list.Mails.Add(gift);
            list.Mails.Add(new MailInfo { MailId = 2, Title = "Bảo trì tối nay", Body = "Máy chủ bảo trì lúc 22h.", SentAtMs = (ulong)(now - 86400_000), Read = true, Claimed = true });
            var old = new MailInfo { MailId = 1, Title = "Quà chào mừng", Body = "Chào mừng đến BattleBall.", SentAtMs = (ulong)(now - 3 * 86400_000), Read = true, Claimed = true };
            old.Attachments.Add(new MailAttachment { Kind = "GEMS", Quantity = 100 });
            list.Mails.Add(old);
            return list;
        }

        static NoticeList FakeNotices()
        {
            var list = new NoticeList();
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            list.Notices.Add(new NoticeInfo { NoticeId = 2, Title = "Sự kiện nhân đôi EXP cuối tuần", Body = "Từ 20h thứ Sáu đến hết Chủ nhật, EXP nhận được từ mọi phó bản nhân đôi.", CreatedAtMs = (ulong)(now - 7200_000) });
            list.Notices.Add(new NoticeInfo { NoticeId = 1, Title = "Cập nhật phiên bản 0.1", Body = "Thêm hòm thư, thông báo và kho đồ dùng chung cho ba nhân vật.", CreatedAtMs = (ulong)(now - 2 * 86400_000) });
            return list;
        }
    }
}
