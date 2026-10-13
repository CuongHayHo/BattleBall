using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BattleBall.Proto;

namespace BattleBall.Net
{
    /// <summary>
    /// Account flow on top of <see cref="BbConnection"/>: connect, register, log in with a password or a stored refresh token, and keep the latest
    /// <see cref="PlayerDataSnapshot"/> the server pushes after login. Plain C#: call <see cref="Pump"/> from the main thread to read pushed events.
    /// </summary>
    public sealed class BbClient : IDisposable
    {
        public BbConnection Connection { get; private set; }
        public LoginResult Login { get; private set; }
        public PlayerDataSnapshot Snapshot { get; private set; }
        public DisconnectNotice LastDisconnect { get; private set; }
        public bool Dropped { get; private set; }

        public event Action<PlayerDataSnapshot> SnapshotChanged;
        public event Action<Envelope> EventReceived;

        public async Task ConnectAsync(string host, int port, bool acceptAnyCertificate, string clientVersion, string platform, Action<float> progress = null)
        {
            Connection = new BbConnection { AcceptAnyCertificate = acceptAnyCertificate };
            Connection.Disconnected += n => { LastDisconnect = n; Dropped = true; };
            await Connection.ConnectAsync(host, port, clientVersion, platform, 8000, progress);
        }

        public async Task<RegisterResult> RegisterAsync(string username, string password)
        {
            var answer = await Connection.RequestAsync(new Envelope { RegisterRequest = new RegisterRequest { Username = username, Password = password } });
            if (answer.PayloadCase != Envelope.PayloadOneofCase.RegisterResult)
                throw new InvalidOperationException("register failed: " + BbConnection.Describe(answer));
            return answer.RegisterResult;
        }

        public Task<LoginResult> LoginWithPasswordAsync(string username, string password, string deviceInfo)
        {
            return DoLogin(new LoginRequest { Username = username, Password = password, DeviceInfo = deviceInfo ?? "" });
        }

        public Task<LoginResult> LoginWithTokenAsync(string username, string refreshToken, string deviceInfo)
        {
            return DoLogin(new LoginRequest { Username = username ?? "", RefreshToken = refreshToken, DeviceInfo = deviceInfo ?? "" });
        }

        async Task<LoginResult> DoLogin(LoginRequest req)
        {
            var answer = await Connection.RequestAsync(new Envelope { LoginRequest = req });
            if (answer.PayloadCase != Envelope.PayloadOneofCase.LoginResult)
                throw new InvalidOperationException("login failed: " + BbConnection.Describe(answer));
            Login = answer.LoginResult;
            return Login;
        }

        // ---- requests of a logged-in player: each answer is the reply of the server to that request; on success the server also pushes a fresh PlayerDataSnapshot

        async Task<Envelope> Ask(Envelope request, Envelope.PayloadOneofCase expected)
        {
            var answer = await Connection.RequestAsync(request);
            if (answer.PayloadCase != expected)
                throw new InvalidOperationException("unexpected answer: " + BbConnection.Describe(answer));
            return answer;
        }

        /// <param name="fusion">true: a fusion skin for the fusion form of this (main) character</param>
        public async Task<ActionResult> EquipSkinAsync(ulong characterId, uint costumeId, bool fusion = false)
        {
            var a = await Ask(new Envelope { EquipSkinRequest = new EquipSkinRequest { CharacterId = characterId, CostumeId = costumeId, Fusion = fusion } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>Wears an aura (0 takes it off); the aura must be unlocked by the highest character of the account.</summary>
        public async Task<ActionResult> EquipAuraAsync(uint auraId)
        {
            var a = await Ask(new Envelope { EquipAuraRequest = new EquipAuraRequest { AuraId = auraId } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>Learns a skill (level 0 to 1) or raises it by one level with the gold of the account; the server pushes a fresh snapshot on success.</summary>
        public async Task<ActionResult> UpgradeSkillAsync(ulong characterId, uint skillId)
        {
            var a = await Ask(new Envelope { UpgradeSkillRequest = new UpgradeSkillRequest { CharacterId = characterId, SkillId = skillId } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>Throws a skin away for good (refused while a character of the account wears it).</summary>
        public async Task<ActionResult> DiscardSkinAsync(uint costumeId)
        {
            var a = await Ask(new Envelope { DiscardSkinRequest = new DiscardSkinRequest { CostumeId = costumeId } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>Every piece of gear in the shared bag (all pages of InventoryRequest), in item id order.</summary>
        public async Task<List<ItemInfo>> InventoryAsync(ulong characterId)
        {
            var all = new List<ItemInfo>();
            ulong after = 0;
            for (int guard = 0; guard < 50; guard++)                 // 50 pages of 200: more than the bag can hold (2.000)
            {
                var a = await Ask(new Envelope { InventoryRequest = new InventoryRequest { CharacterId = characterId, AfterItemId = after, PageSize = 200 } }, Envelope.PayloadOneofCase.InventoryPage);
                var page = a.InventoryPage;
                all.AddRange(page.Items);
                if (!page.HasMore || page.Items.Count == 0) break;
                after = page.Items[page.Items.Count - 1].ItemId;
            }
            return all;
        }

        /// <summary>Wears a piece of the bag on this character (the server checks its planet and level); the piece that was worn goes back to the bag.</summary>
        public async Task<ActionResult> EquipItemAsync(ulong characterId, ulong itemId)
        {
            var a = await Ask(new Envelope { EquipItemRequest = new EquipItemRequest { CharacterId = characterId, ItemId = itemId } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>Takes off the piece worn in a gear slot (ARMOR, PANTS, GLOVES, BOOTS, RING) and puts it in the bag.</summary>
        public async Task<ActionResult> UnequipItemAsync(ulong characterId, string slot)
        {
            var a = await Ask(new Envelope { UnequipItemRequest = new UnequipItemRequest { CharacterId = characterId, Slot = slot } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>Raises a piece (worn or in the bag) by one upgrade stone: +5% of its main stat, up to +75%.</summary>
        public async Task<ActionResult> UpgradeItemAsync(ulong characterId, ulong itemId)
        {
            var a = await Ask(new Envelope { UpgradeItemRequest = new UpgradeItemRequest { CharacterId = characterId, ItemId = itemId } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>A GEN stone changes one GEN line of a piece (line_no from 0) to another random set of the planet of the piece (docs 15.21).</summary>
        public async Task<ActionResult> RerollGenAsync(ulong characterId, ulong itemId, uint lineNo)
        {
            var a = await Ask(new Envelope { RerollGenRequest = new RerollGenRequest { CharacterId = characterId, ItemId = itemId, LineNo = lineNo } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>
        /// "Ép sao" (docs 15.27): a stat stone ADDs a stat line, RAISEs a line (line_no 1 to 3, 4 the crystal line; chance-based, ActionResult.Success says whether it worked) or RETYPEs a stat line.
        /// </summary>
        public async Task<ActionResult> StatStoneAsync(ulong characterId, ulong itemId, string action, uint lineNo)
        {
            var a = await Ask(new Envelope { StatStoneRequest = new StatStoneRequest { CharacterId = characterId, ItemId = itemId, Action = action, LineNo = lineNo } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>"Pha lê hóa": a crystal stone makes (or makes again) the red line of a piece.</summary>
        public async Task<ActionResult> CrystalizeAsync(ulong characterId, ulong itemId)
        {
            var a = await Ask(new Envelope { CrystalizeRequest = new CrystalizeRequest { CharacterId = characterId, ItemId = itemId } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>GEN slot stones open one more GEN line on a piece.</summary>
        public async Task<ActionResult> UnlockGenSlotAsync(ulong characterId, ulong itemId)
        {
            var a = await Ask(new Envelope { UnlockGenSlotRequest = new UnlockGenSlotRequest { CharacterId = characterId, ItemId = itemId } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>Sells pieces of the bag for a little gold (equipped pieces cannot be sold).</summary>
        public async Task<ActionResult> SellItemAsync(ulong characterId, ulong itemId)
        {
            var request = new SellItemsRequest { CharacterId = characterId, Destroy = false };
            request.ItemIds.Add(itemId);
            var a = await Ask(new Envelope { SellItemsRequest = request }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>What an NPC sells (the shop of the NPC with this id on the map); throws when the NPC sells nothing.</summary>
        public async Task<ShopContent> ShopAsync(string npcId)
        {
            var answer = await Connection.RequestAsync(new Envelope { ShopRequest = new ShopRequest { NpcId = npcId } });
            if (answer.PayloadCase == Envelope.PayloadOneofCase.ShopContent) return answer.ShopContent;
            if (answer.PayloadCase == Envelope.PayloadOneofCase.ActionResult) throw new InvalidOperationException(answer.ActionResult.Message);
            throw new InvalidOperationException("unexpected answer: " + BbConnection.Describe(answer));
        }

        /// <summary>The table of the daily check-in of the month (the elder): every day with its reward, which ones were claimed, which day is today.</summary>
        public async Task<AttendanceInfo> AttendanceAsync()
        {
            var answer = await Connection.RequestAsync(new Envelope { AttendanceRequest = new AttendanceRequest() });
            if (answer.PayloadCase == Envelope.PayloadOneofCase.AttendanceInfo) return answer.AttendanceInfo;
            if (answer.PayloadCase == Envelope.PayloadOneofCase.ActionResult) throw new InvalidOperationException(answer.ActionResult.Message);
            throw new InvalidOperationException("unexpected answer: " + BbConnection.Describe(answer));
        }

        /// <summary>Checks in for today: gold, gems and the free items of the day (ALREADY_OWNED when it was done already).</summary>
        public async Task<ActionResult> ClaimAttendanceAsync()
        {
            var a = await Ask(new Envelope { ClaimAttendanceRequest = new ClaimAttendanceRequest() }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>Makes the Potara earring out of the shards of the account (100; one earring per account); the answer is followed by a fresh snapshot.</summary>
        /// <summary>In a dungeon the earring fuses two characters of the squad (docs 15.41). ActionResult not ok: NOT_OWNED, LEVEL_TOO_LOW, NOT_FOUND (checked as for a match) or INVALID_ARGUMENT (dead, busy, already fused); the merge comes in the frames.</summary>
        public async Task<ActionResult> FuseAsync(ulong mainCharacter, ulong partnerCharacter)
        {
            var a = await Ask(new Envelope { FuseRequest = new FuseRequest { MainCharacterId = mainCharacter, PartnerCharacterId = partnerCharacter } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>In a dungeon the earring is taken off: the fused fighter splits into its two characters (docs 15.40). ActionResult not ok when nothing is fused or the fighter is busy; the new fighter comes in the frames.</summary>
        public async Task<ActionResult> SplitFusionAsync()
        {
            var a = await Ask(new Envelope { SplitFusionRequest = new SplitFusionRequest() }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        public async Task<ActionResult> AssemblePotaraAsync()
        {
            var a = await Ask(new Envelope { AssemblePotaraRequest = new AssemblePotaraRequest() }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>Uses one EXP bag on a character (it gets the experience of the bag; ActionResult MAXED at level 100); the answer is followed by a fresh snapshot.</summary>
        public async Task<ActionResult> UseExpBagAsync(ulong characterId)
        {
            var a = await Ask(new Envelope { UseExpBagRequest = new UseExpBagRequest { CharacterId = characterId } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>Takes the reward of a finished quest of Ông Gôhan (an empty id: of every finished one); the answer is followed by a fresh snapshot.</summary>
        public async Task<ActionResult> ClaimQuestAsync(string questId)
        {
            var a = await Ask(new Envelope { ClaimQuestRequest = new ClaimQuestRequest { QuestId = questId ?? "" } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>Buys a piece of gear ("GEAR") or some stones ("MATERIAL", quantity 1 to 1000) in the shop of this NPC; the server prices it for the account's highest character.</summary>
        public async Task<ActionResult> ShopBuyAsync(string npcId, string kind, uint templateId, uint quantity)
        {
            var a = await Ask(new Envelope { ShopBuyRequest = new ShopBuyRequest { NpcId = npcId, Kind = kind, TemplateId = templateId, Quantity = quantity } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        /// <summary>Buys a skin with gems for the account (the character only decides which race may wear it); the server pushes a fresh snapshot on success.</summary>
        public async Task<ActionResult> BuySkinAsync(ulong characterId, uint costumeId)
        {
            var a = await Ask(new Envelope { BuySkinRequest = new BuySkinRequest { CharacterId = characterId, CostumeId = costumeId } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        public async Task<MailList> MailListAsync()
        {
            var a = await Ask(new Envelope { MailListRequest = new MailListRequest() }, Envelope.PayloadOneofCase.MailList);
            return a.MailList;
        }

        /// <summary>Mail actions (read, take the gift, delete, take all); the server also pushes the fresh mail list and snapshot.</summary>
        public async Task<ActionResult> MailActionAsync(ulong mailId, MailAction action)
        {
            var a = await Ask(new Envelope { MailActionRequest = new MailActionRequest { MailId = mailId, Action = action } }, Envelope.PayloadOneofCase.ActionResult);
            return a.ActionResult;
        }

        public async Task<NoticeList> NoticeListAsync()
        {
            var a = await Ask(new Envelope { NoticeListRequest = new NoticeListRequest() }, Envelope.PayloadOneofCase.NoticeList);
            return a.NoticeList;
        }

        public async Task<ChangePasswordResult> ChangePasswordAsync(string oldPassword, string newPassword)
        {
            var a = await Ask(new Envelope { ChangePasswordRequest = new ChangePasswordRequest { OldPassword = oldPassword, NewPassword = newPassword } },
                Envelope.PayloadOneofCase.ChangePasswordResult);
            return a.ChangePasswordResult;
        }

        /// <summary>
        /// Starts a training session of the village (docs/architecture/BattleBallServer.md 15.12): returns the MatchStarted of the session, or throws with the reason
        /// the server gave. The frames then arrive as pushed events (EventReceived with a MatchFrame), the end as a MatchEnded.
        /// </summary>
        public async Task<MatchStarted> StartTrainingAsync(StartTrainingRequest request)
        {
            var answer = await Connection.RequestAsync(new Envelope { StartTrainingRequest = request });
            if (answer.PayloadCase == Envelope.PayloadOneofCase.MatchStarted) return answer.MatchStarted;
            if (answer.PayloadCase == Envelope.PayloadOneofCase.ActionResult) throw new InvalidOperationException(answer.ActionResult.Message);
            throw new InvalidOperationException("unexpected answer: " + BbConnection.Describe(answer));
        }

        /// <summary>
        /// Starts a dungeon (docs/architecture/BattleBallServer.md 15.25): returns the MatchStarted, or throws with the reason the server gave. A training session of the village
        /// that still runs is closed by the server. The frames then arrive as pushed events, the end as a MatchEnded.
        /// </summary>
        public async Task<MatchStarted> StartMatchAsync(StartMatchRequest request)
        {
            var answer = await Connection.RequestAsync(new Envelope { StartMatchRequest = request });
            if (answer.PayloadCase == Envelope.PayloadOneofCase.MatchStarted) return answer.MatchStarted;
            if (answer.PayloadCase == Envelope.PayloadOneofCase.ActionResult) throw new InvalidOperationException(answer.ActionResult.Message);
            throw new InvalidOperationException("unexpected answer: " + BbConnection.Describe(answer));
        }

        /// <summary>Sends a message that has no answer (the position reports, the skills and leaving a match).</summary>
        public void Send(Envelope message)
        {
            Connection?.Send(message);
        }

        /// <summary>Main thread: hands out what the server pushed (the first PlayerDataSnapshot arrives right after a successful login).</summary>
        public void Pump()
        {
            while (Connection != null && Connection.TryDequeueEvent(out var e))
            {
                if (e.PayloadCase == Envelope.PayloadOneofCase.PlayerDataSnapshot)
                {
                    Snapshot = e.PlayerDataSnapshot;
                    SnapshotChanged?.Invoke(Snapshot);
                }
                EventReceived?.Invoke(e);
            }
        }

        public void Logout()
        {
            try { Connection?.Send(new Envelope { LogoutRequest = new LogoutRequest() }); } catch (Exception) { }
            Connection?.Close();
        }

        public void Dispose() { Connection?.Dispose(); }
    }
}
