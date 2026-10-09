using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BattleBall.Proto;

namespace BattleBall.Net
{
    /// <summary>
    /// End-to-end check of the account flow against a running local server (plain C#, no Unity API, so the Editor menu, batch mode and a console
    /// harness can all run it). Throws on the first failed expectation; returns a readable report.
    /// </summary>
    public static class BbNetSelfTest
    {
        public static async Task<string> RunAsync(string host, int port)
        {
            var log = new StringBuilder();
            void Ok(string what) { log.AppendLine("OK   " + what); }
            void Expect(bool cond, string what) { if (!cond) throw new Exception("FAILED: " + what); Ok(what); }

            string user = "unity" + new Random().Next(100000, 999999);
            const string pass = "password123";

            // 1. connect + handshake (TLS with the self-signed development certificate)
            var a = new BbClient();
            await a.ConnectAsync(host, port, true, "selftest", "editor");
            Expect(a.Connection.ServerInfo.ProtocolVersion == BbConnection.ProtocolVersion && a.Connection.ServerInfo.TickRateHz > 0,
                "TLS connect and Hello: protocol " + a.Connection.ServerInfo.ProtocolVersion + ", tick " + a.Connection.ServerInfo.TickRateHz + " Hz");
            Expect(a.Connection.ServerInfo.AssetBaseUrl.StartsWith("http") && a.Connection.ServerInfo.AssetVersion.Length > 0,
                "HelloAck announces the assets: " + a.Connection.ServerInfo.AssetBaseUrl + " version " + a.Connection.ServerInfo.AssetVersion);
            Expect(a.Connection.ServerInfo.ServerName.Length > 0, "HelloAck carries the server name: " + a.Connection.ServerInfo.ServerName);

            // 2. register: bad name, then good, then duplicate
            var bad = await a.RegisterAsync("x", pass);
            Expect(!bad.Ok && bad.Error == RegisterError.UsernameInvalid, "register with a short name is refused (" + bad.Error + ")");
            var reg = await a.RegisterAsync(user, pass);
            Expect(reg.Ok, "register " + user);
            var dup = await a.RegisterAsync(user, pass);
            Expect(!dup.Ok && dup.Error == RegisterError.UsernameTaken, "duplicate name is refused (" + dup.Error + ")");

            // 3. wrong password, then the right one; the server pushes the player data
            var wrong = await a.LoginWithPasswordAsync(user, "not-the-password", "selftest");
            Expect(!wrong.Ok && wrong.Error == LoginError.AuthFailed, "wrong password is refused (" + wrong.Error + ")");
            var login = await a.LoginWithPasswordAsync(user, pass, "selftest");
            Expect(login.Ok && login.AccountId != 0 && login.RefreshToken.Length > 0, "login with password, account " + login.AccountId);
            await WaitFor(() => { a.Pump(); return a.Snapshot != null; }, "player data snapshot");
            var snap = a.Snapshot;
            Expect(snap.Characters.Count == 3, "snapshot has 3 characters (" + string.Join(",", snap.Characters.Select(c => c.Race)) + ")");
            await Task.Delay(100);

            // 4. log in again with the refresh token on a second connection: the first one is kicked (LOGGED_IN_ELSEWHERE)
            var b = new BbClient();
            await b.ConnectAsync(host, port, true, "selftest", "editor");
            var viaToken = await b.LoginWithTokenAsync(user, login.RefreshToken, "selftest");
            Expect(viaToken.Ok && viaToken.AccountId == login.AccountId, "login with the refresh token");
            await WaitFor(() => a.Dropped, "the first connection is closed by the server");
            Expect(a.LastDisconnect != null && a.LastDisconnect.Reason == DisconnectReason.LoggedInElsewhere, "first connection got LOGGED_IN_ELSEWHERE");
            await WaitFor(() => { b.Pump(); return b.Snapshot != null; }, "player data snapshot on the second connection");

            // 5. the shared inventory, the mailbox and the notices, wearing a skin, changing the password
            Expect(b.Snapshot.Skins.Count == 9 && b.Snapshot.Characters.All(c => c.DefaultCostumeId > 0),
                "the three characters share the " + b.Snapshot.Skins.Count + " starter skins, every race has a default costume");
            Expect(b.Snapshot.Gold == 0 && b.Snapshot.Gems == 0 && b.Snapshot.BagCount == 0, "one wallet and one bag for the account");
            var mails = await b.MailListAsync();
            Expect(mails.Mails.Count == 0 && b.Snapshot.UnreadMail == 0, "the mailbox of a new account is empty");
            var notices = await b.NoticeListAsync();
            Ok("notice list received (" + notices.Notices.Count + " notice(s))");
            var earth = b.Snapshot.Characters.First(c => c.Race == "EARTH");
            uint worn = earth.Equipment.First(e => e.Slot == "SKIN").CostumeId;
            var other = b.Snapshot.Skins.First(sk => sk.GenderId == 0 && sk.CostumeId != worn);
            var equip = await b.EquipSkinAsync(earth.CharacterId, other.CostumeId);
            Expect(equip.Ok, "wear skin " + other.CostumeId + " on the Earth character");
            await WaitFor(() => { b.Pump(); return b.Snapshot.Characters.First(c => c.Race == "EARTH").Equipment.First(e => e.Slot == "SKIN").CostumeId == other.CostumeId; }, "the snapshot shows the new skin");
            var xayda = b.Snapshot.Skins.First(sk => sk.GenderId == 2);
            var locked = await b.EquipSkinAsync(earth.CharacterId, xayda.CostumeId);
            Expect(!locked.Ok && locked.Error == ActionError.RaceLocked, "a Xayda skin on the Earth character is refused (" + locked.Error + ")");
            // auras: unlocked by the highest character (a new account is level 1), one worn at a time, shared by the account
            Expect(b.Snapshot.Auras.Count >= 60 && b.Snapshot.EquippedAuraId == 0, "the snapshot lists " + b.Snapshot.Auras.Count + " auras and none is worn");
            var openAura = b.Snapshot.Auras.First(x => x.UnlockLevel <= 1);
            var lockedAura = b.Snapshot.Auras.First(x => x.UnlockLevel > 50);
            var auraOk = await b.EquipAuraAsync(openAura.AuraId);
            Expect(auraOk.Ok, "wear aura " + openAura.AuraId + " (level " + openAura.UnlockLevel + ")");
            await WaitFor(() => { b.Pump(); return b.Snapshot.EquippedAuraId == openAura.AuraId; }, "the snapshot shows the aura that is worn");
            var auraLocked = await b.EquipAuraAsync(lockedAura.AuraId);
            Expect(!auraLocked.Ok && auraLocked.Error == ActionError.LevelTooLow, "aura " + lockedAura.AuraId + " (level " + lockedAura.UnlockLevel + ") is refused to a level 1 account (" + auraLocked.Error + ")");
            var auraOff = await b.EquipAuraAsync(0);
            Expect(auraOff.Ok, "the aura is taken off");
            await WaitFor(() => { b.Pump(); return b.Snapshot.EquippedAuraId == 0; }, "the snapshot shows no aura");
            // the Kỹ năng tab: the snapshot carries the skill table; a new account has no gold, so learning Kamejoko is refused
            Expect(b.Snapshot.SkillCatalog.Count >= 25 && b.Snapshot.SkillCatalog.All(e => e.Levels.Count == e.MaxLevel && e.IconId > 0),
                "the snapshot lists " + b.Snapshot.SkillCatalog.Count + " skills with every level, icon and gold cost");
            var learn = await b.UpgradeSkillAsync(earth.CharacterId, 1);
            Expect(!learn.Ok && learn.Error == ActionError.NotEnoughGold, "learning Kamejoko without gold is refused (" + learn.Error + ")");
            var foreign = await b.UpgradeSkillAsync(earth.CharacterId, 3);
            Expect(!foreign.Ok && foreign.Error == ActionError.NotFound, "a Namếc skill cannot be learned by the Trái Đất character (" + foreign.Error + ")");
            // 6. training with the dummies of the village (docs/architecture/BattleBallServer.md 15.12): start, punch the Mộc nhân until a hit lands, leave
            var treq = new StartTrainingRequest { ControlledCharacterId = earth.CharacterId, ArenaW = 8000, ArenaH = 1200 };
            treq.Spots.Add(new TrainingSpot { Key = "moc_nhan", X = 880, Y = 165 });
            treq.Spots.Add(new TrainingSpot { Key = "saibamen", X = 1060, Y = 165 });
            var started = await b.StartTrainingAsync(treq);
            var moc = started.Entities.First(e => e.Kind == EntityKind.TrainingDummy && e.DummyKey == "moc_nhan");
            Expect(started.Training && started.RunId == 0 && started.Entities.Count(e => e.Kind == EntityKind.TrainingDummy) == 2 && moc.HpMax == 2000000000UL,
                "training starts with the two dummies (Mộc nhân " + moc.HpMax + " HP)");
            Expect(started.Skills.Count == 1 && started.Skills[0].SkillId == 0 && started.Skills.All(sl => sl.IconId > 0 && sl.Name.Length > 0),
                "only the learned skills (a new account: the punch) are usable while training, with icon and name (" + started.Skills.Count + " slot)");
            uint me = started.Entities.First(e => e.Controlled).EntityId;
            bool hit = false;
            ulong dummyHp = moc.HpMax, hitAmount = 0;
            MatchEnded ended = null;
            Action<Envelope> onEvent = e =>
            {
                if (e.PayloadCase == Envelope.PayloadOneofCase.MatchFrame)
                {
                    foreach (var d in e.MatchFrame.Damages) if (d.TargetId == moc.EntityId && d.SourceId == me && d.Amount > 0) { hit = true; hitAmount = d.Amount; }
                    foreach (var st in e.MatchFrame.States) if (st.EntityId == moc.EntityId) dummyHp = st.Hp;
                }
                else if (e.PayloadCase == Envelope.PayloadOneofCase.MatchEnded) ended = e.MatchEnded;
            };
            b.EventReceived += onEvent;
            uint seq = 0;
            await WaitFor(() =>
            {
                b.Send(new Envelope { PlayerStateReport = new PlayerStateReport { Seq = ++seq, X = moc.X - 30, Y = moc.Y, DirX = 1 } });
                b.Send(new Envelope { UseSkillRequest = new UseSkillRequest { Seq = ++seq, Slot = 0, TargetId = moc.EntityId, DirX = 1 } });
                b.Pump();
                return hit && dummyHp < moc.HpMax;
            }, "a punch of the controlled character lands on the Mộc nhân", 10000);
            Ok("the punch hits the Mộc nhân for " + hitAmount + ", its HP is now " + dummyHp + " / " + moc.HpMax);
            b.Send(new Envelope { LeaveMatchRequest = new LeaveMatchRequest() });
            await WaitFor(() => { b.Pump(); return ended != null; }, "the end of the training session", 8000);
            Expect(ended.Result == MatchResult.Abandoned, "leaving ends the training session (" + ended.Result + ")");
            b.EventReceived -= onEvent;
            var punchEntry = b.Snapshot.SkillCatalog.First(x => x.SkillId == 0);
            Expect(punchEntry.Levels.All(l => l.RangePx > 0), "the skill table tells how close the punch has to be (" + punchEntry.Levels[0].RangePx + " Nro px)");
            var claim = await b.MailActionAsync(999999, MailAction.MailClaim);
            Expect(!claim.Ok && claim.Error == ActionError.NotFound, "taking the gift of a mail that does not exist is refused (" + claim.Error + ")");
            var wrongOld = await b.ChangePasswordAsync("not-the-password", "another-pass-99");
            Expect(!wrongOld.Ok && wrongOld.Error == ChangePasswordError.WrongOldPassword, "password change with a wrong old password is refused (" + wrongOld.Error + ")");
            var changed = await b.ChangePasswordAsync(pass, "another-pass-99");
            Expect(changed.Ok && changed.RefreshToken.Length > 0, "password changed, a new refresh token was issued");
            b.Logout();
            a.Dispose();
            b.Dispose();
            var c2 = new BbClient();
            await c2.ConnectAsync(host, port, true, "selftest", "editor");
            var oldToken = await c2.LoginWithTokenAsync(user, login.RefreshToken, "selftest");
            Expect(!oldToken.Ok && oldToken.Error == LoginError.TokenRevoked, "the refresh token of before the change is revoked (" + oldToken.Error + ")");
            var withNew = await c2.LoginWithPasswordAsync(user, "another-pass-99", "selftest");
            Expect(withNew.Ok, "login with the new password");
            c2.Logout();
            c2.Dispose();
            return log.ToString();
        }

        static async Task WaitFor(Func<bool> cond, string what, int timeoutMs = 5000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!cond())
            {
                if (DateTime.UtcNow > until) throw new TimeoutException("timed out waiting for: " + what);
                await Task.Delay(20);
            }
        }
    }
}
