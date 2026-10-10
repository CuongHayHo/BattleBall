# BattleBall Client ↔ BattleBallServer: Protocol Contract (Design)

> **Trạng thái:** DESIGN ONLY. Đây là **nguồn sự thật cho thiết kế protocol** của BattleBall. Chưa có implementation ở client lẫn server (`E:\PRJNRO\BattleBallServer` trống; BattleBall chưa có lớp mạng).
> **Không có opcode số chính thức.** Mọi message dùng **tên logic**. Giá trị số, kiểu dữ liệu chi tiết và serialization sẽ gán khi chốt mục 11.
> Nhãn: **CONFIRMED** (người dùng đã chốt hoặc có evidence), **PROPOSED** (đề xuất thiết kế mới), **UNKNOWN**, **OPEN** (cần chốt trước implementation).
> Dựa trên mô hình đã chốt trong `docs\database\BattleBall.md` (đọc file đó để biết quyết định gốc), GDD `docs\gameplay\game-design-doc.md`, và bài học từ Teamobi2026 (`docs\protocol\Teamobi2026.md`).
> Tài liệu `docs\protocol\BattleBallServer.md` viết trước khi biết game chơi đơn; **tài liệu này thay thế** các phần message của nó.

---

## 0. Mô hình đã chốt ảnh hưởng tới protocol (tóm tắt)

| Điểm | Quyết định | Nguồn |
|---|---|---|
| Chơi đơn, cần mạng liên tục, không PvP | CONFIRMED | người dùng |
| Server lưu dữ liệu và **mô phỏng trận** (mob, boss, AI đồng đội) ở **20 Hz** | CONFIRMED | người dùng |
| Combat theo Teamobi2026: server tính sát thương, cooldown, mana, HP, drop; client báo **vị trí và mục tiêu** | CONFIRMED | người dùng |
| Server **không kiểm tra** vị trí và tầm đánh (như Teamobi2026) | CONFIRMED | người dùng |
| Đăng nhập chồng: **kick phiên cũ** | CONFIRMED | người dùng |
| **Mất kết nối giữa trận (Android): tạm dừng tối đa 30 giây, tối đa 2 lần mỗi trận, vào lại được bằng token; quá hạn thì hủy trận và giữ phần thưởng đã nhận** (thay quyết định "hủy ngay") | CONFIRMED | người dùng |
| Boss và mob rơi **item/vàng tự động vào túi** ngay lúc chết; ghi ngay | CONFIRMED | người dùng |
| EXP tính theo **số boss đã hạ** (thua vẫn có), thắng trận **+20%**; kill streak, hệ số quá/thấp cấp | CONFIRMED (công thức: `docs\database\BattleBall.md` mục 4.1) | người dùng |
| Túi giới hạn **2.000 món**; đầy thì mất đồ rơi, có cảnh báo; bán/hủy hàng loạt, giá bán thấp, hủy có xác nhận | CONFIRMED | người dùng |
| Hợp thể: 1 tài khoản **1 bông tai** (không nằm trong 10 ô), cả 2 nhân vật cấp 50, cặp **chốt khi vào trận** đến hết trận, nhân vật phụ mất skill; chỉ số = cộng rồi nhân buff | CONFIRMED | người dùng |
| Hồi sinh bằng vàng **tối đa 2 lần mỗi trận** | CONFIRMED | người dùng |
| Minimap lưu **server theo tài khoản** | CONFIRMED | người dùng |
| Skin = costume id, theo tài khoản, khóa theo chủng tộc | CONFIRMED | người dùng |
| **Chiến đấu 2D, di chuyển 8 hướng, mặc định là bay; map chiến đấu KHÔNG có trọng lực** nên không có chạy bộ, rơi, tiếp đất. **Trạng thái không di chuyển: `IDLE` (đứng im) và `IDLE_BREATH` (đứng im thở)**, chính là trạng thái lơ lửng đứng yên. **Trạng thái di chuyển: `FLY`, `FLY_MOVE`, `JUMP`** (tên do người dùng chốt; ý nghĩa chi tiết từng trạng thái: OPEN). Chạy bộ và nhảy trên nền chỉ có ở **map làng tân thủ** (map trao đổi/mua bán) | CONFIRMED | người dùng |
| **Hai chế độ trúng đòn:** (1) **hướng**: trúng mọi mục tiêu trong vùng/đường thẳng, dễ bị né nếu boss di chuyển tự do; (2) **chọn mục tiêu**: kỹ năng định hướng đuổi theo mục tiêu | CONFIRMED | người dùng |
| **Đánh thường:** tự đấm, hết frame thì gây sát thương **một lần** | CONFIRMED | người dùng |
| Cùng một map: người chơi điều khiển 1 nhân vật, **2 AI đồng đội tự di chuyển và đánh** (server điều khiển) | CONFIRMED | người dùng |
| **Target platform: Android (mobile)**; **server Java, transport TCP, khung mạng Netty**; serialization **Protobuf**; đăng nhập **tài khoản + mật khẩu (băm)**; **TLS ngay từ đầu**; đăng ký tài khoản **cả trong game và qua web** (không cần email; **trang web đăng ký riêng ghi trực tiếp vào DB như Teamobi2026**; 3 nhân vật khởi đầu được tạo sẵn ở cấp 1); **TLS**: chứng chỉ tự ký khi phát triển, chứng chỉ thật (CA) khi chạy thật, ghim chứng chỉ ở bản phát hành; tọa độ gameplay là **pixel gốc kiểu NRO (số nguyên)**, client quy đổi sang đơn vị Unity khi vẽ | CONFIRMED | người dùng |

---

## 1. Nguyên tắc contract
1. **Server là nơi giữ trạng thái và kết quả**: HP, Ki, cooldown, sát thương, drop, EXP, vàng, item. Client gửi *ý định* và *dữ liệu đầu vào* (vị trí, mục tiêu), không bao giờ gửi chỉ số, item hay kết quả.
2. **Ngoại lệ đã chấp nhận:** vị trí và mục tiêu do client báo được tin (CONFIRMED). Hệ quả (CONFIRMED ghi nhận): server không chặn teleport hay đánh từ xa; ảnh hưởng chủ yếu tới độ tin cậy phần thưởng, giảm nhẹ bằng giới hạn phần thưởng theo thời gian và thống kê trận (PROPOSED).
3. **Một định nghĩa message duy nhất** cho hai phía (tên, trường, kiểu, version); không có số opcode rải trong code (bài học: reference lệch `-4/67`, `12`, `-17`).
4. **Mọi request có `requestId`**; response mang lại `requestId`. Event do server chủ động thì `requestId = 0`.
5. **Mọi lỗi trả `Error` có mã chuẩn** (mục 10), không im lặng nuốt lỗi.
6. **Message chỉ hợp lệ trong state session nhất định** (mục 2).
7. **Chỉ gửi thay đổi** trong trận (không gửi lại toàn bộ trạng thái mỗi tick 20 Hz): spawn/despawn, delta trạng thái, sự kiện (PROPOSED).
8. **Server không gửi frame/sprite**: gửi trạng thái hành động; client tự ánh xạ sang `NroPose` (mục 12).

---

## 2. Connection Lifecycle

```
CONNECTED ─Hello/HelloAck─► HANDSHAKED ─LoginRequest/LoginResult(ok)─► AUTHENTICATED (menu/kho/cửa hàng)
   ─StartMatchRequest/MatchStarted─► IN_MATCH ─MatchEnded─► AUTHENTICATED
   ─(Logout | Disconnect | kick)─► CLOSED
```
(Đã bỏ phase "ENTER WORLD" kiểu MMO: chơi đơn không có thế giới chung; "vào trận" thay cho "vào world".)

| Phase | Client gửi | Server trả | State | Failure |
|---|---|---|---|---|
| CONNECT | mở kết nối | chấp nhận | `CONNECTED` | quá giới hạn: đóng (kèm `DisconnectNotice(SERVER_FULL)`); không gửi `Hello` trong thời hạn: đóng |
| HANDSHAKE | `Hello` | `HelloAck` | `HANDSHAKED` | `Error(PROTOCOL_VERSION_UNSUPPORTED)` rồi đóng |
| AUTHENTICATION | `LoginRequest` | `LoginResult` | `AUTHENTICATED` | `AUTH_FAILED`/`AUTH_RATE_LIMITED`/`ACCOUNT_BANNED`; quá số lần sai thì đóng |
| SESSION | (trong `LoginResult`) | `sessionId`, thông tin tài khoản; sau đó `PlayerDataSnapshot` | giữ | **Đăng nhập chồng: kick phiên cũ** bằng `DisconnectNotice(LOGGED_IN_ELSEWHERE)` (CONFIRMED); trận đang chạy của phiên cũ bị hủy theo quy tắc mất kết nối |
| MENU/KHO | các request kho, cửa hàng, ghép, bán, hợp thể (mục 8) | response/event | `AUTHENTICATED` | `Error` theo request |
| VÀO TRẬN | `StartMatchRequest` | `MatchStarted` + chuỗi `EntitySpawn` | `IN_MATCH` | `Error(NOT_FOUND\|INVALID_STATE\|FUSION_NOT_ELIGIBLE)` giữ `AUTHENTICATED` |
| PLAY | báo trạng thái, tấn công, skill, hồi sinh, `Ping` | events, results, `Pong` | `IN_MATCH` | lỗi theo request → `Error`; vi phạm nặng → `DisconnectNotice(PROTOCOL_VIOLATION)` |
| KẾT THÚC TRẬN | (không bắt buộc) hoặc `LeaveMatchRequest` | `MatchEnded` | → `AUTHENTICATED` | xem mất kết nối |
| DISCONNECT | `LogoutRequest` / đóng kết nối | `DisconnectNotice` nếu server chủ động | `CLOSED` | **Mất kết nối khi `IN_MATCH`: tạm dừng trận ≤ 30 giây (≤ 2 lần mỗi trận) cho vào lại (CONFIRMED); quá hạn: hủy trận, giữ phần thưởng đã ghi, không có `MatchEnded` cuối, không cộng +20%** |

**Reconnect vào trận (CONFIRMED, người dùng chốt, thay quyết định "hủy ngay"):** server **tạm dừng** mô phỏng tối đa 30 giây và tối đa 2 lần mỗi trận; client tự kết nối lại bằng `ResumeSessionRequest{sessionToken}` (không đăng nhập lại), nhận `MatchResumed`; **quá hạn thì hủy trận** và giữ phần thưởng đã nhận như quy tắc cũ. Chi tiết mục 14.3.

---

## 3. Message Categories (logic)

| Category | Messages |
|---|---|
| Connection | `Hello`, `HelloAck`, `Ping`, `Pong`, `DisconnectNotice`, `ResumeSessionRequest`, `PauseMatchRequest`, `MatchPaused`, `MatchResumed` |
| Authentication/Session | `RegisterRequest`, `RegisterResult`, `LoginRequest`, `LoginResult`, `LogoutRequest`, `PlayerDataSnapshot` |
| Player/Squad | `SetSquadRequest`, `SetAiTacticRequest`, `SetFusionPairRequest`, `SquadResult` |
| Inventory | `InventoryPageRequest`, `InventoryPage`, `InventoryChanged`, `EquipRequest`, `UnequipRequest`, `EquipResult`, `SetSkinRequest`, `SellItemsRequest`, `DestroyItemsRequest`, `ItemsRemovedResult` |
| Economy/Craft | `BuyShardRequest`, `CraftRequest` (ghép mảnh, bông tai), `EnhanceItemRequest` (đá nâng cấp/chỉ số/pha lê/GEN), `CraftResult`, `WalletChanged` |
| Match | `StartMatchRequest`, `MatchStarted`, `LeaveMatchRequest`, `MatchEnded` |
| Gameplay (client→server) | `PlayerStateReport`, `SelectTargetRequest`, `SetAutoAttackRequest`, `UseSkillRequest`, `ReviveRequest` |
| Gameplay (server→client) | `EntitySpawn`, `EntityDespawn`, `EntityStateUpdate`, `PlayerStatsUpdate`, `SkillCast`, `CombatEvent`, `SkillResult`, `CooldownUpdate`, `EntityDied`, `Revived`, `KillStreakUpdate` |
| Reward | `RewardGranted`, `RewardRejected`, `ExpGained`, `BagWarning` |
| Minimap | `MinimapRevealReport` (client→server), `MinimapSnapshot` (trong `PlayerDataSnapshot` hoặc `MatchStarted`) |
| Error | `Error` |

Số lượng message nhỏ có chủ ý; skill, item, GEN cụ thể là **dữ liệu**, không phải message riêng (mỗi skill không có message riêng).

---

## 4. Message Contracts
Kiểu logic: `u8/u16/u32/i32/i64`, `f32`, `bool`, `string` (UTF-8), `list<T>`, `Vec2 {x, y}`. **Tọa độ (CONFIRMED, người dùng chốt): pixel gốc kiểu NRO, số nguyên 32 bit (`i32`), trục Y hướng xuống như NRO**; **tile 24 px, chunk minimap 16×16 tile (384 px)**; client quy đổi sang Unity units và đổi chiều Y khi vẽ (`NroMannequin`: pixel gốc chia PPU, Y xuống rồi đổi). `EntityId` u32 trong một trận; `RequestId` u32. Định dạng số chi tiết: TO BE DESIGNED.
Hướng: **C→S** / **S→C**. Loại: Request (R), Response (Rp), Event (E).

### 4.1 Connection

**Hello** C→S R · required: `requestId`, `protocolVersion`, `clientVersion` · optional `platform` · validation: version hỗ trợ, độ dài chuỗi · response `HelloAck` · failure `Error(PROTOCOL_VERSION_UNSUPPORTED)` + đóng.
**HelloAck** S→C Rp · `requestId`, `protocolVersion`, `serverTime`, `heartbeatIntervalMs`, `tickRateHz` (= 20, thông tin).
**Ping/Pong** hai chiều · `Ping{requestId, clientTime}` → `Pong{requestId, clientTime, serverTime}`. Quá timeout không có dữ liệu: server đóng (giá trị OPEN).
**DisconnectNotice** S→C E · `reason` ∈ {`SERVER_SHUTDOWN`, `KICKED`, `LOGGED_IN_ELSEWHERE`, `TIMEOUT`, `PROTOCOL_VIOLATION`, `SERVER_FULL`}, optional `message`.

### 4.2 Authentication / Session

**RegisterRequest** C→S R · state `HANDSHAKED` (CONFIRMED: đăng ký **trong game** và **qua web (PHP, ghi trực tiếp vào DB)**; hai cửa phải dùng chung đặc tả tài khoản) · `requestId`, `username`, `password` (chỉ truyền qua TLS); **không cần email** (CONFIRMED, giai đoạn đầu: chỉ tài khoản và mật khẩu) · validation: tên **4 đến 20 ký tự (a-z, 0-9, `_`)**, mật khẩu **từ 8 ký tự**, tên chưa tồn tại, rate limit theo IP · server authority: tạo tài khoản với **mật khẩu băm**, **tự tạo sẵn 3 nhân vật (Xayda, Namec, Trái Đất) ở cấp 1** (CONFIRMED, người dùng chốt) · response `RegisterResult` · failure `Error(INVALID_REQUEST | ACCOUNT_EXISTS | RATE_LIMITED)`.
**RegisterResult** S→C Rp · `requestId`, `ok`, `error?`.
**LoginRequest** C→S R · state `HANDSHAKED` · `requestId`, `credential` (**mật khẩu** hoặc **`refreshToken` dài hạn để tự đăng nhập**: CONFIRMED hướng, xem `docs\networking\BattleBall.md` mục 5.1) · validation: định dạng, rate limit theo IP và tài khoản · server authority: xác thực bằng mật khẩu **băm** hoặc token (không so mật khẩu dạng rõ như reference) · response `LoginResult` · failure `LoginResult(ok=false, error)`; không phân biệt "sai user" và "sai mật khẩu".
**LoginResult** S→C Rp · `requestId`, `ok`; khi ok: `accountId`, `sessionId`, **`refreshToken` mới (chỉ khi đăng nhập bằng mật khẩu)**; khi lỗi: `error` (`AUTH_FAILED`/`AUTH_RATE_LIMITED`/`ACCOUNT_BANNED`/`TOKEN_EXPIRED`/`TOKEN_REVOKED`). Sau `ok`, server gửi `PlayerDataSnapshot`.
**PlayerDataSnapshot** S→C E · gồm: 3 nhân vật (`race`, `level`, `exp`, cấp skill, chỉ số cuối do server tính), 10 ô trang bị mỗi nhân vật, `Wallet` (vàng, đá quý), vật liệu xếp chồng (`MaterialStack`), `Squad` (nhân vật điều khiển, chiến thuật AI, cặp hợp thể), trạng thái bông tai (`potaraEarringOwned`), skin sở hữu, tiến trình (phó bản đã mở), `MinimapSnapshot`, số item trong kho và bộ đếm mua mảnh hôm nay. **Danh sách đồ trong kho không nằm ở đây** (có thể rất nhiều): lấy bằng `InventoryPageRequest`.
**LogoutRequest** C→S R · state ≥ `AUTHENTICATED` · lưu dữ liệu, đóng; response `Ack` hoặc đóng sạch.

### 4.3 Squad và hợp thể (ngoài trận)

**SetSquadRequest** C→S R · state `AUTHENTICATED` · `requestId`, `controlledCharacterId` · validation: nhân vật thuộc tài khoản. *(Cập nhật 2026-10-07: **bỏ trường `aiTactic`**; người dùng chốt AI tự dùng skill theo quy tắc "hồi chiêu nào dùng chiêu đó theo thứ tự": mục 10.9 của `docs\database\BattleBall.md`; server điều khiển AI, client không gửi chiến thuật.)*
**SetAiTacticRequest** C→S R · state `AUTHENTICATED` **hoặc `IN_MATCH`** (CONFIRMED: chiến thuật đặt ngoài trận bằng `SetSquadRequest` và **đổi được ngay trong trận bằng nút nhanh**) · `requestId`, `aiCharacterId`, `tactic` (`DPS` | `CC` | `SUPPORT`) · validation: nhân vật AI thuộc đội hiện tại; trong trận áp dụng từ tick kế tiếp · response `SquadResult`.
**SetFusionPairRequest** C→S R · state `AUTHENTICATED` (ngoài trận) · `requestId`, `partnerCharacterId` (cùng nhân vật điều khiển tạo cặp), hoặc `clear=true` · validation: tài khoản **có bông tai**; cả 2 nhân vật **đạt cấp 50**; không đang trong trận · server authority: lưu cặp; **không tiêu bông tai** · response `SquadResult` · failure `Error(FUSION_NOT_ELIGIBLE)` (kèm lý do: `NO_EARRING`/`LEVEL_TOO_LOW`).
**SquadResult** S→C Rp · `requestId`, squad hiện tại (đội thường 1 điều khiển + 2 AI, hoặc 1 hợp thể + 1 AI; nhân vật chủ = nhân vật điều khiển; nhân vật phụ mất skill).
Đổi cặp trong trận: **không cho phép** (cặp chốt khi vào trận); `Error(INVALID_STATE)`.

### 4.4 Inventory, trang bị, bán/hủy

**InventoryPageRequest** C→S R · `requestId`, `cursor` hoặc `pageIndex`, `pageSize` (giới hạn tối đa), optional filter (loại, slot, phẩm chất: OPEN) · phân trang bắt buộc (kho tới 2.000 món).
**InventoryPage** S→C Rp · `requestId`, `items: list<ItemView>`, `nextCursor`, `totalBagCount`; `ItemView{ itemId, templateId, stat lines, version }`.
**InventoryChanged** S→C E · thay đổi tăng/giảm kho (thêm, bớt, đổi) kèm `totalBagCount`; dùng sau cấp thưởng, bán, hủy, ghép, nâng cấp.
**EquipRequest** C→S R · `requestId`, `characterId`, `slot` (`ARMOR|PANTS|GLOVES|BOOTS|RING|SUPPORT_1..4`), `itemId`, `expectedVersion` · validation: item thuộc tài khoản, đang `BAG`, đúng loại ô, không đang trong trận · response `EquipResult`. Mặc đồ chuyển `BAG → EQUIPPED` (đồ mặc **không nằm trong kho** và **không tính 2.000**); đồ đang mặc được tháo (`UnequipRequest{characterId, slot}`) và trở lại `BAG`; **tháo vào kho đầy** (≥ 2.000) thì `Error(BAG_FULL)`.
**SetSkinRequest** C→S R · `requestId`, `characterId`, `costumeId` · validation: tài khoản **sở hữu** skin; **đúng chủng tộc** (khóa theo `gender` costume: 0/1/2 theo chủng tộc, 3 dùng chung) · response `EquipResult`. Thất bại: `PERMISSION_DENIED` hoặc `INVALID_REQUEST`.
**EquipResult** S→C Rp · `requestId`, ô mới, chỉ số cuối của nhân vật do server tính lại.
**SellItemsRequest** C→S R · `requestId`, `itemIds: list` (**tối đa 200 món mỗi lô**: CONFIRMED, người dùng chọn; với 2.000 món cần tối đa 10 lô, client tự chia lô), `idempotencyKey` · validation: mỗi món thuộc tài khoản và `location = BAG` (đồ đang mặc không có trong kho và bị từ chối nếu gửi sai) · server authority: giá bán **thấp** theo `sellPriceGold` · response `ItemsRemovedResult` (đã bán, vàng nhận, món bị bỏ qua và lý do). Mỗi lô là **một transaction** có `ItemLog`/`CurrencyLedger` (`SELL`).
**DestroyItemsRequest** C→S R · như trên nhưng thêm `confirmed: true` (hủy cần xác nhận); thiếu `confirmed` → `Error(INVALID_REQUEST)`; ghi nhật ký `DESTROY`, không nhận vàng.
Không có `LockItem` (không có khái niệm khóa: CONFIRMED).

### 4.5 Kinh tế và ghép

**BuyShardRequest** C→S R · `requestId`, `shardKind` (mảnh bông tai Potara; mảnh đồ), `quantity`, `idempotencyKey` · validation: đủ vàng; **giới hạn mua mỗi ngày** (`DailyPurchaseCounter`); **tài khoản đã có bông tai thì chặn mua mảnh bông tai** (CONFIRMED) · response `WalletChanged` + `InventoryChanged`/vật liệu · failure `Error(DAILY_LIMIT_REACHED | NOT_ENOUGH_GOLD | ALREADY_OWNED)`.
**CraftRequest** C→S R · `requestId`, `recipeId` (ghép đủ mảnh thành món; ghép bông tai: 100 mảnh) · validation: đủ mảnh; bông tai: chưa có (mỗi tài khoản 1) · server authority: trừ mảnh, tạo item, **RNG ở server**, một transaction · response `CraftResult` (+ `InventoryChanged`). Kho đầy: `Error(BAG_FULL)` và **không trừ mảnh**.
**EnhanceItemRequest** C→S R · `requestId`, `itemId`, `kind` (`UPGRADE_STONE|STAT_STONE|CRYSTAL_STONE|GEN`), tham số theo loại (ví dụ chỉ số muốn đổi: OPEN), `expectedVersion`, `idempotencyKey` · validation: sở hữu, luật đá (không trùng chỉ số trên cùng món, trần từng chỉ số theo GDD) · server authority: **toàn bộ xác suất thành công và kết quả RNG ở server** · response `CraftResult{ success, item mới }`; thất bại do xác suất vẫn là phản hồi hợp lệ (`success=false`), không phải `Error`. Chi tiết quy tắc từng loại đá: UNKNOWN đến khi GDD đủ.
**CraftResult** S→C Rp · kết quả, vật liệu đã tiêu, item sau thao tác.
**WalletChanged** S→C E · số dư vàng/đá quý mới, lý do.

### 4.6 Trận đấu

**StartMatchRequest** C→S R · state `AUTHENTICATED` · `requestId`, `dungeonId` · validation: phó bản đã mở; cặp hợp thể (nếu có) hợp lệ (cả hai cấp ≥ 50, có bông tai); **cảnh báo túi gần đầy** (xem `BagWarning`, không chặn vào trận) · server authority: **chốt cặp hợp thể và cấp từng nhân vật lúc vào trận**; tạo `RunSession` và instance mô phỏng riêng · response `MatchStarted` · failure `Error(NOT_FOUND | INVALID_STATE | FUSION_NOT_ELIGIBLE)`.
**MatchStarted** S→C Rp · `requestId`, `matchId`, `dungeonId`, `tickRateHz`, `serverTick`, `self` (các nhân vật trong đội, chỉ số cuối đã tính, HP/Ki, hợp thể: ai chủ, ai phụ), `mapInfo`, `minimapState`; sau đó `EntitySpawn` cho mob, boss, AI đồng đội đang có.
**LeaveMatchRequest** C→S R · rời trận có chủ đích (thoát/đầu hàng) · xử lý như **thua**: không +20%, giữ EXP theo boss và thưởng đã nhận; **giống mất kết nối quá hạn** (CONFIRMED, người dùng chốt).
**MatchEnded** S→C E · `matchId`, `result` (`WIN|LOSE|ABORT`), `reason?` (`SERVER_ERROR` khi **DB trục trặc không ghi được thưởng: hủy trận và báo lỗi**, CONFIRMED), tổng kết: số boss đã hạ, EXP tổng từng nhân vật (đã gồm +20% nếu thắng), vàng, item đã nhận và bị từ chối (túi đầy), **các nhân vật lên cấp sau trận và chỉ số mới** (lên cấp **áp dụng sau trận**, không đổi chỉ số giữa trận), thống kê `RunProgress`. Sau đó session về `AUTHENTICATED`.

### 4.7 Gameplay trong trận (client→server)

**PlayerStateReport** C→S (không cần response; gửi khi có thay đổi) · `matchId`, `seq`, `position: Vec2`, `moveDir` (8 hướng, hoặc 0 khi đứng yên), `facing`, `action: ActionState` (chỉ trạng thái đứng yên và chiến đấu; trạng thái di chuyển là hiển thị phía client, không gửi), optional `selectedTargetId` · **server không kiểm tra** vị trí và tầm (CONFIRMED) và dùng trực tiếp cho AI mob/boss và tính trúng · `seq` chỉ để bỏ gói cũ. **Tần suất (CONFIRMED, người dùng chọn): tối đa 20 gói/giây, chỉ gửi khi có thay đổi** (khớp nhịp server 20 Hz; đứng yên thì không gửi, tiết kiệm pin và dữ liệu); server bỏ gói vượt giới hạn. Trong trận `action` gồm `IDLE`, `IDLE_BREATH`, `FLY`, `FLY_MOVE`, `JUMP`, `ATTACK`, `CAST`/`CHARGE`, `HIT`, `DEAD`; **không có `RUN`, `FALL`** (map chiến đấu không có trọng lực, CONFIRMED); chạy bộ trên nền chỉ ở map làng tân thủ.
**SelectTargetRequest** C→S R · `matchId`, `targetEntityId` hoặc `clear` · dùng cho **chế độ chọn mục tiêu**; server xác nhận mục tiêu tồn tại (không kiểm khoảng cách).
**SetAutoAttackRequest** C→S R · `matchId`, `enabled`, `mode` (`DIRECTIONAL` | `TARGETED`), optional `targetEntityId` · bật/tắt **tự đấm**: khi bật, server lặp đòn thường cho đến khi tắt, chết hoặc hết mục tiêu; mỗi đòn **hết frame thì gây sát thương một lần** (mục 6). **Điều kiện tự đấm (CONFIRMED, người dùng chốt):** bật tự đấm thì nhân vật **đấm liên tục khi có mục tiêu trong tầm đấm ngắn phía trước**; không có mục tiêu trong tầm thì đứng hoặc bay bình thường. Con số tầm đấm: UNKNOWN (dữ liệu tĩnh, chốt khi cân bằng).
**UseSkillRequest** C→S R · `matchId`, `seq`, `skillId`, `mode` (`DIRECTIONAL` hướng/vùng | `TARGETED` chọn mục tiêu), `direction` (chế độ hướng; **góc tự do** theo vector đã chuẩn hóa hoặc góc: CONFIRMED, người dùng chốt, hướng chuột hoặc ngón tay tới mục tiêu) hoặc `targetEntityId` (chế độ chọn mục tiêu) · validation: skill thuộc **nhân vật đang điều khiển** (hợp thể: skill của nhân vật chủ; skill nhân vật phụ bị từ chối), **cooldown** (giờ server), **đủ Ki**, không bị khống chế, `mode` hợp lệ: **mọi skill dùng được cả hai chế độ (hướng, chọn mục tiêu), trừ skill tự buff/hồi** (không cần mục tiêu, như Kaioken, Tái tạo năng lượng, Biến khỉ, Khiên): CONFIRMED, người dùng chốt · **không kiểm khoảng cách** (CONFIRMED) · response `SkillResult` · failure `Error(COOLDOWN | NOT_ENOUGH_KI | INVALID_TARGET | INVALID_STATE)`.
**ReviveRequest** C→S R · `matchId` · validation: nhân vật đã chết; **còn lượt hồi sinh (tối đa 2 lần mỗi trận)**; đủ vàng · server authority: trừ vàng, hồi sinh, giảm lượt · response `Revived` (+ `WalletChanged`) · failure `Error(REVIVE_LIMIT_REACHED | NOT_ENOUGH_GOLD | INVALID_STATE)`.
(`AttackRequest` ở bản nháp đầu được thay bằng `SetAutoAttackRequest` và `SelectTargetRequest`.)

### 4.8 Gameplay trong trận (server→client)

**EntitySpawn** E · `entityId`, `kind` (`MOB|BOSS|AI_ALLY|DROP_EFFECT?`: chỉ các loại cần hiển thị), `templateId`, `position`, `facing`, `hp/hpMax`, `appearance` (với AI đồng đội: `costumeId`), `elite?`. **Không có người chơi khác** (không PvP).
**EntityDespawn** E · `entityId`, `reason` (`DIED_REMOVED`, `OUT_OF_ZONE`...).
**EntityStateUpdate** E · batch theo tick, chỉ các entity thay đổi: `entityId`, `position`, `facing`, `action: ActionState`, `hp`, trạng thái hiệu ứng. Boss: `phase` (enrage dưới 30% HP, triệu hồi) qua `action`/cờ (chi tiết boss: UNKNOWN).
**PlayerStatsUpdate** E · HP, Ki hiện tại, buff đang chạy, hồi HP (0,5%/giây khi Ki > 0), trạng thái hợp thể.
**CombatEvent** E · `attackerId`, `targetId`, `skillId`, `damage` (**kiểu đủ rộng, không ép 32-bit**), `crit`, `killed`, `serverTick` (để client vẽ số sát thương).
**SkillCast** E (PROPOSED) · server báo kỹ năng đã tung: `casterId`, `skillId`, `mode`, `origin`, `direction` hoặc `targetEntityId`, `travelMs` (thời gian bay/đòn), để client vẽ hiệu ứng; **kết quả trúng/né chỉ có trong `CombatEvent` tại thời điểm tính trúng**.
**SkillResult / CooldownUpdate / EntityDied / Revived** như tên: `SkillResult{requestId, ok}`; `CooldownUpdate{skillId, readyAtServerTime}`; `EntityDied{entityId, killerId?}`; `Revived{entity state}`.
**KillStreakUpdate** E · chuỗi hiện tại, thưởng EXP đang áp dụng (`+5%…+20%`); client chỉ hiển thị.

### 4.9 Thưởng trong trận
**RewardGranted** S→C E · **gửi ngay khi boss/mob chết và server đã ghi** (tự động vào túi): `source` (`BOSS|MOB`, `entityId`), `items` (nhận được), `gold`, `materials`. Ghi bền vững **trước hoặc cùng lúc** gửi event; mất kết nối ngay sau đó không mất phần đã ghi (CONFIRMED).
**RewardRejected** S→C E · item rơi nhưng **túi đã đủ 2.000 món**: mất; `reason = BAG_FULL`, kèm item bị bỏ lỡ (để hiển thị). Vàng và vật liệu xếp chồng vẫn nhận bình thường.
**ExpGained** S→C E · **khi mỗi boss chết**: EXP hiệu dụng cho từng nhân vật (đã nhân hệ số vượt/thấp cấp và thưởng kill streak). **EXP được ghi ngay, nhưng không lên cấp giữa trận**; lên cấp báo trong `MatchEnded`.
**BagWarning** S→C E · gửi khi vào trận (cạnh `MatchStarted`) và khi `bagCount` vượt ngưỡng gần đầy (đề xuất 1.800); chứa `bagCount`, `limit = 2000`.

### 4.10 Minimap
**MinimapRevealReport** C→S · `mapId`, `chunkIds` mới khám phá (batch, không quá thường xuyên). Server lưu **bitmask theo chunk, theo tài khoản** và coi dữ liệu này là phần tiện ích (sai lệch không ảnh hưởng thưởng); validate `mapId`, `chunkId` hợp lệ. Gửi lại khi vào trận: `minimapState`.

---

## 5. Movement Flow (CONFIRMED mô hình, PROPOSED chi tiết)
```
Input → client di chuyển ngay (2D, 8 hướng, mặc định BAY; đứng im / đứng im thở khi không di chuyển)
 → PlayerStateReport{seq, position, moveDir, facing, action}
 → server: bỏ gói seq cũ; ghi vị trí vào trạng thái trận (KHÔNG validate); dùng cho AI mob/boss và tính trúng
 → không có PositionCorrection, không broadcast (chơi đơn, không PvP)
 → mob/boss/AI đồng đội do server mô phỏng và di chuyển tự do: EntityStateUpdate; client nội suy
```
- **Map chiến đấu không có trọng lực (CONFIRMED, người dùng chốt):** nhân vật lơ lửng, không rơi, không chạy, không tiếp đất. Hệ quả: (1) server **không cần trọng lực, tiếp đất hay `yPhysicInTop`** cho nhân vật trong trận; (2) **trạng thái không di chuyển** là `IDLE` và `IDLE_BREATH` (lơ lửng đứng yên); (3) **trạng thái di chuyển là `FLY`, `FLY_MOVE`, `JUMP`** (CONFIRMED tên; ý nghĩa từng trạng thái và khi nào chuyển giữa chúng: **OPEN**). **Ánh xạ tư thế di chuyển (CONFIRMED, người dùng chốt):** `FLY_MOVE` = `Fly_Horizontal`; `FLY` = `Jump_Up` và là **trạng thái chuyển tiếp lúc bắt đầu rời `IDLE` sang tư thế khác** (sau đó vào `FLY_MOVE` khi đang di chuyển); **bay chéo dùng `Fly_Horizontal`**; **bay xuống là `Fall_Down` rồi `Fly_Horizontal`, phát nối tiếp** (CONFIRMED, người dùng chốt). **Bay lên thẳng đứng: `Jump_Up` rồi `Fly_Horizontal`** (CONFIRMED, người dùng chốt, nối tiếp). **Thời lượng `FLY` (`Jump_Up`) là 2 giây** trước khi sang `FLY_MOVE` (CONFIRMED, người dùng chốt; **nếu dừng di chuyển trước 2 giây thì `FLY` kết thúc ngay và trở về `IDLE`**: CONFIRMED). `JUMP` trong trận: **giải pháp đề xuất (PROPOSED)** ở mục 5.1. **Máy trạng thái hiển thị (dựa trên câu trả lời của người dùng):** `IDLE` →(bắt đầu di chuyển)→ `FLY` (`Jump_Up`, 2 giây) →(đang di chuyển)→ `FLY_MOVE` (`Fly_Horizontal`) →(dừng)→ `IDLE`/`IDLE_BREATH`; đang bay xuống thêm `Fall_Down`. (4) chạy bộ và nhảy trên nền chỉ ở **map làng tân thủ**.
- **Làng tân thủ (CONFIRMED, người dùng chốt):** chỉ một người chơi với NPC cửa hàng (chơi đơn); là map riêng của từng người, không đồng bộ người chơi khác và không có trao đổi giữa người chơi.
- `facing` ±1 khớp `NroMannequin.facing`; 8 hướng là hướng di chuyển, không phải số hướng vẽ.
- **Vật cản (CONFIRMED: người dùng sẽ bổ sung sau):** hiện chưa có dữ liệu vật cản hay vùng cấm trong map chiến đấu; mặc định chỉ giữ nhân vật trong biên map (server không kiểm vị trí do client báo theo quyết định đã chốt, nên vật cản nếu có sẽ chủ yếu ảnh hưởng mob/boss/đạn của server, và việc client tự chặn). Khi có vật cản: thêm dữ liệu map và quyết định server có kiểm tra va chạm cho nhân vật hay không (OPEN).

### 5.1 `JUMP` chỉ ở làng tân thủ, trong trận không có `JUMP` (CONFIRMED, người dùng đồng ý)
**Quyết định: `JUMP` chỉ tồn tại ở map làng tân thủ (cùng `RUN`); trong trận không có `JUMP`.**
- Lý do: `JUMP` chỉ có nghĩa khi có trọng lực (nhảy rồi rơi). Map chiến đấu không có trọng lực (CONFIRMED), và tư thế `Jump_Up` trong trận **đã được dùng làm trạng thái `FLY`** (chuyển tiếp lúc bắt đầu bay). Nếu giữ thêm `JUMP` trong trận thì hai trạng thái trùng một tư thế và cùng ý nghĩa "bay lên".
- Cách thực hiện: `JUMP` và `RUN` là trạng thái hiển thị riêng của **làng tân thủ** (client tự suy ra từ chạy và nhảy; làng tân thủ chơi đơn, không cần gửi lên server); trạng thái hiển thị của **trận** chỉ gồm `IDLE`, `IDLE_BREATH`, `FLY`, `FLY_MOVE`, bay xuống (`Fall_Down` rồi `Fly_Horizontal`) cùng các trạng thái chiến đấu.
- Phương án khác (không khuyến nghị): dùng `JUMP` làm một hành động có chủ đích trong trận (ví dụ lướt/né nhanh). GDD hiện không có hành động này; thêm sau cũng không đổi protocol vì trạng thái di chuyển là hiển thị.
- Làng tân thủ cần server hay không: chỉ cho NPC cửa hàng, ghép, hợp thể (các message kho và kinh tế), không cần `PlayerStateReport`.

## 6. Combat Flow
**Đánh thường (tự đấm).** `SetAutoAttackRequest(enabled)` → server lặp: bắt đầu đòn → phát `ActionState = ATTACK` (client chạy animation) → **hết thời lượng frame của đòn thì gây sát thương một lần** (CONFIRMED) → `CombatEvent`. Thời lượng đòn = số frame animation ÷ tốc độ đánh (GDD có chỉ số "tốc độ đánh"); cần `attackDurationMs` trong dữ liệu tĩnh của chủng tộc/skill (UNKNOWN). %ATK đấm theo chủng tộc: Xayda 110%, Namec 100%, Trái Đất 90% (GDD).

**Kỹ năng, hai chế độ trúng (CONFIRMED):**
1. **Chế độ hướng (`DIRECTIONAL`):** hướng tung là **góc tự do**; trúng **mọi mục tiêu trong vùng/đường thẳng** theo vị trí và hướng lúc tung. **Dễ bị né nếu boss di chuyển tự do**: tính trúng xảy ra sau thời gian bay/tung (không tức thì), nên mục tiêu rời vùng thì trượt. Server tính trúng tại thời điểm hiệu ứng đến hạn, dựa trên vị trí **hiện tại** của mục tiêu do server mô phỏng.
2. **Chế độ chọn mục tiêu (`TARGETED`):** **kỹ năng định hướng đuổi theo mục tiêu đã chọn** (homing); server cập nhật vị trí mục tiêu mỗi tick cho đến khi trúng hoặc hết hạn.
- Hai chế độ cần server mô phỏng **hiệu ứng bay của skill** (hoặc ít nhất tính trúng trễ theo `travelMs`): khác Teamobi2026 (server không có thực thể đạn). Đề xuất: hộp va chạm theo đường thẳng/vùng, so khớp với mục tiêu mỗi tick 20 Hz (PROPOSED).
- Công thức damage, trúng/né, chí mạng: UNKNOWN. Server **không kiểm khoảng cách tung** (CONFIRMED); vùng/đường trúng từng skill là dữ liệu tĩnh (Kamehame/Atomic xuyên đường thẳng, Flash diện rộng, Quả cầu khênh ki tụ lực 1,5 đến 2 giây có thể bị ngắt bởi CC: GDD).
- Nhân vật điều khiển là người tung; client không gửi `attackerId`. AI đồng đội do **server** điều khiển và tung skill theo chiến thuật.
- Chết và thưởng: `EntityDied` → `RewardGranted` → `ExpGained` → `KillStreakUpdate`. Sát thương lên người chơi: server mô phỏng boss/mob, gửi `CombatEvent` + `PlayerStatsUpdate`. Không có PvP.

### 6.1 Giải pháp mô phỏng skill và thời lượng đòn (CONFIRMED, người dùng duyệt nguyên đề xuất; không rewind vị trí mục tiêu ở giai đoạn đầu; dùng giá trị tạm rồi thay bằng giá trị trích từ dữ liệu)

**Vấn đề 1: hiệu ứng bay, né, đuổi theo của skill ở 20 Hz.**
Giải pháp đề xuất: **mô hình hóa mỗi skill bằng một "hình dạng" lấy từ dữ liệu tĩnh**, server chạy các đối tượng nhẹ trong vòng lặp trận:

| Kiểu (`shape`) | Dùng cho (ví dụ GDD) | Cách tính trúng ở server |
|---|---|---|
| `AREA_FRONT` | đấm cơ bản, đòn cận chiến | vùng chữ nhật phía trước tại thời điểm trúng |
| `LINE_PIERCE` | Kamehame, Atomic (xuyên đường thẳng) | đoạn thẳng dài `length`, rộng `width`, tính tại thời điểm trúng (sau `windup`); mỗi mục tiêu trúng một lần |
| `PROJECTILE` | chưởng bay, thú của Namec | điểm di chuyển với `speed`, quét (swept) vòng tròn mỗi tick, dừng ở mục tiêu đầu hoặc xuyên theo cờ |
| `HOMING` | kỹ năng định hướng ở chế độ chọn mục tiêu | mỗi tick quay hướng về vị trí hiện tại của mục tiêu, `speed`, `lifetime`; trúng khi khoảng cách nhỏ hơn bán kính |
| `AREA_AT` | Flash, Quả cầu khênh ki (có thời gian tụ lực) | vùng tròn/chữ nhật tại vị trí chọn lúc `windup` kết thúc |

- **Dữ liệu từng skill** (`windupMs`, `speed`, `length/width/radius`, `lifetimeMs`, `pierce`, `hitOnce`, `shape`) nằm trong dữ liệu tĩnh có version, **không phải code mới cho mỗi skill**; skill mới chỉ thêm một dòng dữ liệu.
- **Chi phí CPU không đáng kể:** một trận chỉ có vài chục thực thể và vài chục đối tượng bay; mỗi tick vài trăm phép kiểm tra va chạm. Chơi đơn, mô phỏng riêng từng trận nên 20 Hz không phải vấn đề.
- **Xuyên khung (tunneling):** dùng kiểm tra quét (swept) hoặc giới hạn `speed × 50 ms` nhỏ hơn bán kính trúng, để vật bay nhanh không "nhảy qua" mục tiêu giữa hai tick.
- **Độ trễ mạng và độ chính xác khi né/đuổi:** người chơi thấy vị trí boss muộn khoảng nửa vòng trễ cộng độ trễ nội suy. Đề xuất: (1) **vùng trúng có khoảng rộng dự phòng** theo dữ liệu (`slack`), (2) gốc tung lấy từ vị trí mới nhất trong `PlayerStateReport` (đã tin theo quyết định hiện tại), (3) client hiển thị cảnh báo vùng trúng từ `SkillCast`, (4) **không rewind** vị trí mục tiêu ở giai đoạn đầu (OPEN: có thể bật rewind giới hạn khoảng 100 ms nếu thấy khó trúng).
- **Phía client:** từ `SkillCast` (`origin`, `direction`/`targetEntityId`, `speed`, `travelMs`) client **tự mô phỏng hình ảnh bằng cùng công thức** (khớp quy ước hiển thị của NroUnity với `PlayerDart`); **chỉ `CombatEvent` mới quyết định trúng/né và sát thương**. Nếu hình ảnh lệch server vài chục ms thì chỉ là thẩm mỹ. Đạn đuổi theo (homing) có thể gửi bổ sung `ProjectileCorrection` ít khi (OPEN); khởi đầu không cần.
- **Hợp lý với Teamobi2026:** reference chỉ tính tức thì, không có thực thể bay ở server (CONFIRMED trong `docs\gameplay\Teamobi2026.md`); đề xuất này là phần **mới**, giới hạn trong vòng lặp trận.

**Vấn đề 2: thời lượng frame của đòn thường (và skill) để gây sát thương "hết frame".**
Giải pháp đề xuất: **một bảng `AttackProfile` trong dữ liệu tĩnh của server làm nguồn sự thật**, client đồng bộ theo bảng này.
1. **Nguồn dữ liệu có sẵn:** client NroUnity đã nạp bảng `SkillPaint` (`GameScr.sks[]`) với mảng frame `skillStand` và `skillfly` cho từng skill (`Game1\GameScr.cs:1455–1493`), dữ liệu do server gửi từ `data/update_data/skill`. Số frame mỗi skill có thể **trích bằng script một lần** (giống cách đã trích `CostumeCatalog`/`PartDatabase`); `durationMs = số frame × thời lượng một frame`. Thời lượng một frame ở client NRO dùng 25 tick/giây (`NroMannequin.ticksPerSecond = 25`, CONFIRMED trong code BattleBall; đối chiếu với tick thực của NroUnity cần kiểm trước khi chốt).
2. **Đòn đấm cơ bản:** bảng theo chủng tộc gồm chuỗi tư thế (`Punch_1`, `Punch_2`, ... theo `NroPose`) và `frameMs`; `attackDurationMs = Σ frameMs` của một nhịp. **Tốc độ đánh** (chỉ số, ví dụ Biến khỉ) chia thời lượng: `attackDurationMs ÷ (1 + bonus tốc đánh)`.
3. **Đồng bộ với client:** server gửi bảng (hoặc `staticDataVersion` để client tải) và **client co giãn tốc độ phát animation cho khớp `attackDurationMs`**; sát thương luôn do server áp dụng khi hết thời lượng, nên không có lệch kết quả.
4. **Giai đoạn đầu (để không bị chặn):** dùng **giá trị tạm** theo chủng tộc (ví dụ cùng một `attackDurationMs` mặc định), thay bằng giá trị trích từ dữ liệu khi có.
5. **Kiểm chứng:** thêm một bước kiểm tra tự động (tương tự `NroCostumeTesterVerification`) so khớp bảng với số frame thực của từng animation (không cần thực hiện trong task thiết kế này).

## 7. Skill Flow
`UseSkillRequest → trạng thái hợp lệ → skill thuộc nhân vật điều khiển (hợp thể: nhân vật chủ) → cooldown (giờ server) → đủ Ki → mục tiêu → hiệu ứng (sát thương/CC/buff, từ dữ liệu tĩnh) → SkillResult + CombatEvent/EntityStateUpdate + CooldownUpdate + PlayerStatsUpdate`. Chi tiết từng skill (bảng cấp 1–7 trong GDD) là **dữ liệu tĩnh**, không đổi protocol.

## 8. Item / Inventory Flow
| Thao tác | Flow |
|---|---|
| Load | `PlayerDataSnapshot` + `InventoryPageRequest` (phân trang) |
| Nhận đồ trong trận | server ghi ngay; `RewardGranted`/`RewardRejected` |
| Equip/Unequip | `EquipRequest`/`UnequipRequest` ngoài trận → `EquipResult`; đồ đang mặc không nằm trong kho |
| Skin | `SetSkinRequest` (đúng chủng tộc, thuộc tài khoản) |
| Bán / hủy | `SellItemsRequest` / `DestroyItemsRequest(confirmed)`; một transaction mỗi lô; giá thấp |
| Ghép, nâng cấp | `CraftRequest`/`EnhanceItemRequest`; RNG ở server; một transaction |
| Mua mảnh | `BuyShardRequest`; giới hạn mỗi ngày; chặn khi đã có bông tai |
Dùng `version` (khóa lạc quan) và `idempotencyKey` để chống lệch và gửi lặp. Bất kỳ thao tác item/vàng bị từ chối nếu đang `IN_MATCH` (ngoại trừ `ReviveRequest`).

## 9. Đồng bộ trong trận
| Sự kiện | Message |
|---|---|
| Vào trận | `MatchStarted` + `EntitySpawn` |
| Mob/boss/AI đồng đội di chuyển, đổi trạng thái | `EntityStateUpdate` (delta, 20 Hz, chỉ khi đổi) |
| Sát thương, chết | `CombatEvent`, `EntityDied` |
| Thưởng | `RewardGranted`, `ExpGained` |
| Kết thúc | `MatchEnded` |

| Client local | Server authoritative |
|---|---|
| **Vị trí nhân vật** (CONFIRMED), pose/frame, nội suy thực thể, camera, UI, minimap hiển thị, cache sprite, zoom | HP/Ki, sát thương, cooldown, drop, EXP, vàng, item, trạng thái mob/boss/AI đồng đội, kill streak, bộ đếm hồi sinh, cặp hợp thể và cấp lúc vào trận |

## 10. Error Handling
| Category | `ErrorCode` | Khi nào |
|---|---|---|
| Connection | `PROTOCOL_VERSION_UNSUPPORTED`, `TIMEOUT`, `SERVER_FULL` | handshake, mất kết nối |
| Authentication | `AUTH_FAILED`, `AUTH_RATE_LIMITED`, `ACCOUNT_BANNED`, `ACCOUNT_EXISTS` | đăng nhập, đăng ký |
| Invalid request | `INVALID_REQUEST` | thiếu trường, sai kiểu, thiếu `confirmed` khi hủy |
| Invalid state | `INVALID_STATE` | sai state session, `version` lệch, đang trong trận |
| Permission | `PERMISSION_DENIED` | không sở hữu, skin sai chủng tộc |
| Not found | `NOT_FOUND` | phó bản/item/skill/costume không tồn tại |
| Cooldown/Resource | `COOLDOWN`, `NOT_ENOUGH_KI`, `NOT_ENOUGH_GOLD` | skill, mua, hồi sinh |
| Invalid target | `INVALID_TARGET` | mục tiêu không hợp lệ (không kiểm tầm) |
| Limit | `BAG_FULL`, `DAILY_LIMIT_REACHED`, `REVIVE_LIMIT_REACHED`, `ALREADY_OWNED`, `RATE_LIMITED` | giới hạn túi, mua, hồi sinh, bông tai, spam |
| Fusion | `FUSION_NOT_ELIGIBLE` (kèm `NO_EARRING`/`LEVEL_TOO_LOW`) | vào trận hoặc đặt cặp |
| Server error | `SERVER_ERROR` | lỗi nội bộ (log chi tiết kèm id tương quan, không gửi stack trace, SQL, đường dẫn) |

---

## 11. Protocol Format (PROPOSED / OPEN)
| Thành phần | Đề xuất | Lý do | Trạng thái |
|---|---|---|---|
| Framing | length-prefixed | ý tưởng tốt của `MessageSendCollect` (Teamobi2026) | PROPOSED |
| Length | u32, có giới hạn tối đa cấu hình | reference phải chắp nhánh 3 byte cho gói lớn (CONFIRMED) | PROPOSED |
| Message ID | **Không gán số tay:** dùng **phong bì Protobuf với `oneof`** (ví dụ `Envelope{ requestId, oneof payload { LoginRequest, ... } }`); số trường (field number) trong `.proto` đóng vai trò mã loại message nên không có bảng opcode viết tay (bài học: reference lệch `-4/67`, `12`) | — | PROPOSED; giá trị số do `.proto` quyết định, chưa viết |
| Request ID | u32 trong header (0 = event) | ghép response/request | PROPOSED |
| Flags | u8 dự phòng | | PROPOSED |
| Byte order | little-endian | khớp Unity/.NET | PROPOSED |
| Chuỗi | UTF-8 + độ dài | | PROPOSED |
| Serialization | **Protobuf** (CONFIRMED, người dùng chọn). Tệp `.proto` là **nguồn duy nhất** của message, sinh code **Java** (server) và **C#** (Unity). Cần kiểm tra chạy trên **Unity IL2CPP/Android** (stripping) trước khi dùng thật | — | CONFIRMED |
| Transport | **TCP** và server **Java** (CONFIRMED); khung mạng **Netty** (CONFIRMED, người dùng chọn): `SslHandler` (TLS), bộ tách khung theo độ dài, `IdleStateHandler` (heartbeat/timeout), bộ mã hóa/giải mã Protobuf | — | CONFIRMED |
| Mã hóa | **TLS ngay từ đầu** (CONFIRMED, người dùng chốt) trên TCP; không dùng XOR tĩnh (XOR `"NRO"` không phải bảo mật: CONFIRMED). Cần quyết định chứng chỉ (tự ký khi phát triển, chứng chỉ thật khi chạy thật) và có ghim chứng chỉ (pinning) trên Android không: OPEN | — | CONFIRMED / OPEN |
| Nén | không giai đoạn đầu | message nhỏ | PROPOSED |
| Version | `protocolVersion` trong `Hello` | reference không có | PROPOSED |
| Kênh | một kết nối; message trong trận có thể gộp theo tick | đơn giản | PROPOSED |
| Luồng server | **Pool luồng cố định chạy các trận**: mỗi trận là một tác vụ lặp 20 Hz trên một luồng của pool (một luồng có thể phục vụ nhiều trận, không tạo luồng cho mỗi người chơi như Teamobi2026); **Netty I/O** dùng nhóm luồng riêng; **DB** dùng pool luồng riêng, tác vụ trận không chờ DB trong tick | — | CONFIRMED (người dùng chọn) |
| Chịu mất gói | TCP tin cậy và theo thứ tự nên không mất gói; `PlayerStateReport` và `EntityStateUpdate` vẫn mang `seq`/`serverTick` để bỏ dữ liệu cũ khi nghẽn (đầu hàng đợi) | — | PROPOSED |

---

## 12. Client Mapping (BattleBall hiện tại)

| Client class/system (hiện có) | Liên quan protocol | Trạng thái |
|---|---|---|
| `NroCostumeCatalog` (`Assets\NroMannequin\Runtime\NroCostumeCatalog.cs`) | tra `costumeId`/`partId` cho skin và AI đồng đội | **Đã có**, tái dùng |
| `NroMannequin` (`…\NroMannequin.cs`) | vẽ nhân vật/AI từ `facing` + `ActionState` + `Appearance` | **Đã có** (render); cần component điều khiển từ dữ liệu server (**chưa có**) |
| `NroPoseTable`/`NroPose` | bảng ánh xạ `ActionState → NroPose` | enum **đã có**; ánh xạ **chưa có** |
| `NroSpriteResolver`, `NroCostumeCatalog` | không liên quan protocol; hiện đọc bằng `System.IO.File` với `Application.dataPath` và **đường dẫn tuyệt đối tới Teamobi2026**, **không chạy được trên Android** (target đã chốt); cần chuyển sang StreamingAssets/Resources/Addressables hoặc tải từ server | **Cần sửa sau này** |
| Input hiện tại (`Input.GetKeyDown`, chuột) | trên Android cần **joystick ảo 8 hướng, nút skill, chạm để chọn mục tiêu, kéo để chọn hướng skill** (hướng ngón tay) | **Chưa có** |
| Network client, codec, dispatcher, session | tất cả message | **Chưa có** |
| UI đăng nhập, kho, cửa hàng, ghép, hợp thể, HUD trận | các message tương ứng | **Chưa có** |
| Input/controller nhân vật, camera theo nhân vật | `PlayerStateReport`, `SelectTargetRequest`, `SetAutoAttackRequest`, `UseSkillRequest` | **Chưa có** |
| Entity registry cho mob/boss/AI, nội suy | `EntitySpawn/Despawn/StateUpdate` | **Chưa có** |
| Minimap | `MinimapRevealReport`, `MinimapSnapshot` | **Chưa có** |

`ActionState` (tên PROPOSED, trong trận): **không di chuyển** `IDLE`, `IDLE_BREATH` (CONFIRMED); **di chuyển** `FLY`, `FLY_MOVE`, `JUMP` (CONFIRMED tên, ý nghĩa OPEN); chiến đấu `ATTACK`, `CAST`/`CHARGE` (tụ lực), `HIT`, `DEAD`; ở **làng tân thủ** thêm `RUN`. Không có `FALL` trong trận (không trọng lực). Ánh xạ sang `NroPose` là trách nhiệm client; đề xuất: `IDLE`→`Stand_1`, `IDLE_BREATH`→`Stand_2_Breathing`, `FLY_MOVE`→`Fly_Horizontal` (CONFIRMED), `FLY`→`Jump_Up` (CONFIRMED), bay xuống→`Fall_Down` rồi `Fly_Horizontal` nối tiếp (CONFIRMED), `HIT`→`Hit_Injured`, `DEAD`→`Knockdown_Dead`; còn `JUMP` (xem mục dưới: **đề xuất giải pháp, chờ bạn xác nhận**). **Giảm tải protocol (CONFIRMED, người dùng đồng ý):** các trạng thái di chuyển (`FLY`, `FLY_MOVE`, bay xuống) chỉ là **trạng thái hiển thị**, client **tự suy ra từ vận tốc và hướng di chuyển**; **server không nhận và không gửi chúng**: `PlayerStateReport` chỉ cần vị trí, `moveDir` và các trạng thái có nghĩa chiến đấu (`ATTACK`, `CAST`, `HIT`, `DEAD`), còn mob/boss/AI do server điều khiển được gửi bằng vị trí và `moveDir` (client suy ra tư thế). Hệ quả: `ActionState` trong protocol chỉ gồm các trạng thái có nghĩa chiến đấu và đứng yên (`IDLE`, `IDLE_BREATH`, `ATTACK`, `CAST`/`CHARGE`, `HIT`, `DEAD`); `FLY`, `FLY_MOVE`, bay xuống, bay lên **không nằm trong message**, chỉ nằm trong máy trạng thái hiển thị của client. Thời lượng `FLY` 2 giây là dữ liệu hiển thị phía client. Tư thế chạy `Run_1..5` chỉ dùng ở làng tân thủ.

## 13. Server Mapping (BattleBallServer, chưa có code)

| Message | Handler | Service/System | State | Response/Event |
|---|---|---|---|---|
| `Hello`, `Ping` | ConnectionHandler | Networking | connection | `HelloAck`, `Pong` |
| `LoginRequest` | AuthHandler | AuthenticationService (rate limit, kick phiên cũ) | Session | `LoginResult`, `PlayerDataSnapshot` |
| `SetSquadRequest`, `SetFusionPairRequest` | SquadHandler | SquadService (kiểm bông tai, cấp 50) | Squad | `SquadResult` |
| `InventoryPageRequest`, `Equip/Unequip/SetSkinRequest` | InventoryHandler | InventoryService | Inventory | `InventoryPage`, `EquipResult` |
| `SellItemsRequest`, `DestroyItemsRequest` | InventoryHandler | InventoryService (transaction, ledger) | Inventory, Wallet | `ItemsRemovedResult`, `WalletChanged` |
| `BuyShardRequest`, `CraftRequest`, `EnhanceItemRequest` | EconomyHandler | EconomyService (RNG, daily counter) | Inventory, Wallet | `CraftResult`, `WalletChanged` |
| `StartMatchRequest` | MatchHandler | MatchService → tạo instance mô phỏng | RunSession | `MatchStarted` |
| `PlayerStateReport`, `SelectTargetRequest`, `SetAutoAttackRequest`, `UseSkillRequest`, `ReviveRequest` | MatchHandler | **Match loop 20 Hz**: Movement (nhận vị trí), Combat, Skill, Revive | trạng thái trận | events (mục 4.8–4.9) |
| (nội bộ) boss/mob chết | MatchLoop | RewardService, ExpService | Inventory, EXP, RunProgress | `RewardGranted`, `ExpGained` |
| (nội bộ) hết trận | MatchLoop | MatchService | Character EXP/level | `MatchEnded` |
| `MinimapRevealReport` | MinimapHandler | MinimapService | Minimap bitmask | (không response hoặc Ack) |
Handler không sửa trạng thái từ thread mạng: chuyển thành command vào hàng đợi của instance trận hoặc gọi service (PROPOSED, `docs\architecture\BattleBallServer.md`).

---

## 14. Giải pháp cho Android (14.1, 14.2 và 14.3 CONFIRMED, người dùng duyệt; 14.4 PROPOSED)

Target platform là Android (CONFIRMED). Ba hệ quả và hướng giải quyết đề xuất:

### 14.1 Nạp dữ liệu và sprite không phụ thuộc đường dẫn tuyệt đối (CONFIRMED: hai giai đoạn, đóng gói vào dự án trước, cập nhật từ xa (CDN) sau)
Vấn đề (CONFIRMED trong code): `NroSpriteResolver` và `NroCostumeCatalog` dùng `System.IO.File` với `Application.dataPath` và `E:\PRJNRO\Teamobi2026\SRC`; trên Android `StreamingAssets` nằm trong APK nên không đọc được bằng `File`, và đường dẫn tuyệt đối không tồn tại.

Đề xuất theo hai giai đoạn:
1. **Giai đoạn 1: đóng gói trong dự án.** Một công cụ editor (cùng loại với `NroIngestionMenu`) **trích đúng các sprite mà 414 trang phục cần** (atlas `Big0..4` hoặc icon rời) từ nguồn reference vào `Assets` của BattleBall, gom thành **Sprite Atlas / Addressables group**; JSON và `image.bytes` nạp bằng `TextAsset`/Addressables (không dùng `File`). Mỗi thiết bị chỉ cần **một mức zoom** (ví dụ x2 hoặc x3 theo độ phân giải màn hình), không nạp cả x1..x4, để tiết kiệm bộ nhớ.
**Trạng thái 2026-10-07:** giai đoạn tải từ server cho sprite trang phục đã làm theo hướng gói zip qua HTTP (không dùng Addressables); `HelloAck` có `asset_base_url` và `asset_version`; xem `docs/architecture/BattleBallServer.md` mục 15.6.
**Cập nhật 2026-10-09 (CONFIRMED):** tài nguyên đi qua chính kết nối game, không qua HTTP nữa (như Teamobi cmd -74): `AssetManifestRequest` (61) / `AssetManifestReply` (62) và `AssetChunkRequest` (63) / `AssetChunk` (64), cho phép sau Hello trước cả đăng nhập; `HelloAck.asset_base_url` lỗi thời, `asset_version` vẫn dùng. Chi tiết: `docs/architecture/BattleBallServer.md` mục 15.14.
2. **Giai đoạn 2: cập nhật từ xa.** Addressables remote (CDN hoặc server) với danh mục có version; server báo `staticDataVersion` trong `HelloAck`, client tải bản mới khi lệch.
- Hệ quả thiết kế (PROPOSED): một lớp trừu tượng nguồn tài nguyên (ví dụ `IAssetSource`), để `NroCostumeCatalog`/`NroSpriteResolver` không biết dữ liệu đến từ APK, Addressables hay mạng. Chưa sửa code hiện tại trong task thiết kế này.
- Atlas thay cho từng PNG rời giúp giảm số `Texture2D`, bộ nhớ và draw call trên mobile (CONFIRMED rủi ro: hiện mỗi PNG rời tạo một `Texture2D`).

### 14.2 Điều khiển cảm ứng, vẫn khớp protocol hiện tại (CONFIRMED, người dùng chốt: joystick 8 hướng, chạm chọn mục tiêu, kéo nút skill để chọn hướng)
Không cần đổi protocol; chỉ thêm lớp nhập liệu trên client:

| Thao tác trên màn hình | Message |
|---|---|
| **Joystick ảo** (trái màn hình), quy về 8 hướng | `PlayerStateReport.moveDir` |
| **Chạm vào mục tiêu** | `SelectTargetRequest` (chế độ chọn mục tiêu) |
| **Nút skill, chạm nhanh** | `UseSkillRequest` chế độ `TARGETED` nếu đang có mục tiêu; không có mục tiêu thì `DIRECTIONAL` theo hướng nhìn/**hỗ trợ nhắm tự động** tới mục tiêu gần nhất (client chọn hướng, server nhận hướng) |
| **Nút skill, nhấn giữ rồi kéo** | `UseSkillRequest` chế độ `DIRECTIONAL` với **góc tự do theo hướng ngón tay** (đã chốt) |
| **Nút tự đấm** (bật/tắt) | `SetAutoAttackRequest` |
| Skill tự buff/hồi | `UseSkillRequest` không cần mục tiêu |
- Dùng UGUI (`IPointerDownHandler`, `IDragHandler`) với `EventSystem`, chạy được với Input Manager hiện tại (`activeInputHandler: 0`), không bắt buộc chuyển sang Input System mới.
- Joystick và nút phải đặt ở vùng ngón cái, kích thước chạm tối thiểu; giao diện và kích thước dùng `CanvasScaler` theo độ phân giải (client đã dùng uGUI).

### 14.3 Mạng di động: mất kết nối giữa trận (CONFIRMED: chọn hướng B)
Hiện tại (CONFIRMED, người dùng chốt): mất kết nối thì hủy trận, giữ thưởng đã nhận. Trên Android, TCP dễ đứt (đổi Wi-Fi/4G), app vào nền (cuộc gọi, thông báo), hoặc tắt màn hình. Ba hướng:

| Hướng | Cách hoạt động | Ưu | Nhược |
|---|---|---|---|
| **A. Giữ nguyên hủy trận** | như đã chốt | đơn giản nhất | người chơi mobile dễ mất cả trận (đã giữ thưởng đã nhận, nhưng mất EXP +20% khi thắng) |
| **B. Tạm dừng trận trong thời gian ân hạn, vào lại được (ĐÃ CHỌN)** | server **tạm dừng mô phỏng** tối đa N giây (đề xuất **30 giây**) và tối đa **2 lần mỗi trận**; client tự kết nối lại bằng `ResumeSessionRequest` + `sessionToken`; thành công: `MatchResumed`; quá hạn: **hủy trận như quy tắc hiện tại** | chịu được rớt mạng ngắn, đổi mạng, cuộc gọi; vẫn chặn lạm dụng | cần thêm 3 message và state "tạm dừng" |
| **C. Trận tiếp tục chạy khi mất kết nối** | boss tiếp tục, nhân vật đứng yên | không bị lạm dụng tạm dừng | người chơi bị đánh khi không điều khiển được; AI đồng đội vẫn đánh |

- **Hướng B, thêm vào protocol (PROPOSED):** `ResumeSessionRequest{sessionToken}` (không cần đăng nhập lại; **token hết hạn sau 30 giây kể từ lúc mất kết nối** (CONFIRMED, người dùng chọn, bằng thời gian tạm dừng); gắn với session cũ; mỗi lần mất kết nối cấp token mới), `MatchPaused{reason, graceMs}`, `MatchResumed{state snapshot}`, và `PauseMatchRequest` do client gửi khi app sắp vào nền (`OnApplicationPause(true)` trên Android) để server dừng chủ động thay vì chờ timeout. Quy tắc **kick phiên cũ** (đã chốt) vẫn áp dụng: phiên vào lại thay phiên cũ.
- **Phát hiện đứt nhanh:** heartbeat 5 giây, timeout 15 giây (TCP mặc định mất nhiều phút); client tự reconnect với thời gian chờ tăng dần.
- **Giới hạn lạm dụng tạm dừng:** tối đa 2 lần mỗi trận, mỗi lần ≤ 30 giây; thời gian tạm dừng không tính cooldown/hiệu ứng (trận dừng hẳn); boss không được hồi máu quá mức trong lúc dừng (PROPOSED).
- Thưởng đã ghi ngay khi nhặt/hạ boss nên không bị ảnh hưởng bởi hướng nào.

### 14.4 Ghi chú mobile khác (PROPOSED)
- **Băng thông:** 20 Hz chỉ gửi delta và gộp theo tick; bật nén frame nếu cần (OPEN); TLS handshake một lần mỗi kết nối.
- **Pin và nhiệt:** client tách tần suất gửi `PlayerStateReport` (chỉ khi thay đổi, giới hạn tối đa N gói/giây).
- **Độ phân giải và tỉ lệ màn hình:** UI dùng neo (anchor) và `CanvasScaler`; tọa độ gameplay độc lập độ phân giải (đơn vị tọa độ: OPEN).

## Architecture Decision Record

### CONFIRMED
- Mọi mục trong bảng mục 0, gồm: chơi đơn; server mô phỏng 20 Hz; combat theo Teamobi2026; client quyết định vị trí; kick phiên cũ; mất kết nối: tạm dừng ≤ 30 giây (≤ 2 lần mỗi trận) cho vào lại, quá hạn hủy trận và giữ thưởng đã nhận; loot tự động ghi ngay; EXP theo boss; túi 2.000 và mất đồ khi đầy; bán/hủy hàng loạt; hợp thể (bông tai 1 cái, cặp chốt đầu trận, nhân vật phụ mất skill); hồi sinh 2 lần mỗi trận; minimap lưu server; skin khóa chủng tộc.
- Giao thức NRO có điểm yếu đã xác minh (XOR tĩnh, không version, opcode số trần, lệch client/server) nên không dùng làm mặc định (`docs\protocol\COMPARISON.md`).

### PROPOSED
- Header, ID, request ID, flags, little-endian, UTF-8, TLS, phân trang kho, delta trong trận, `ActionState`, bộ `ErrorCode`, khóa lạc quan `version`, `idempotencyKey`, và toàn bộ danh sách message ở mục 3–4.
- Không có `PositionCorrection` và không broadcast (khác bản nháp đầu cho multiplayer).

### UNKNOWN
- Công thức sát thương, hệ số trúng/né, chi tiết boss (pha enrage, triệu hồi), hiệu ứng skill CC/buff cụ thể.
- Quy tắc từng loại đá/GEN; loại và hiệu ứng 4 vật phẩm hỗ trợ vĩnh viễn; phó bản rơi mảnh Potara; số mảnh ghép các món khác; giá bán từng món.

### OPEN QUESTIONS (cần chốt trước implementation)
1. Kiểm tra Protobuf chạy trên Unity IL2CPP/Android (stripping, thư viện `Google.Protobuf`) và chọn phiên bản; cấu hình pipeline Netty (kích thước khung tối đa, idle timeout).
2. **Web đăng ký ghi trực tiếp vào DB (CONFIRMED, người dùng chọn, giống Teamobi2026)**: để tránh lệch với server cần **một đặc tả dùng chung** cho (a) thuật toán băm mật khẩu và tham số (ví dụ bcrypt hoặc argon2, cùng cost), (b) quy tắc tên tài khoản và mật khẩu, (c) việc **tạo 3 nhân vật khởi đầu** (web và server phải tạo cùng một dữ liệu mặc định), (d) ràng buộc `UNIQUE` ở mức DB (không chỉ kiểm tra ở ứng dụng). Rủi ro: web (công nghệ chưa chọn) và server Java dễ lệch quy tắc (reference đã lệch: mật khẩu web `password_hash` còn game dạng rõ). **Đã chốt (người dùng):** web dùng **PHP** (như Teamobi2026), **giới hạn theo IP và CAPTCHA** để chống tạo tài khoản hàng loạt; **tên tài khoản 4 đến 20 ký tự (a-z, 0-9, `_`)**, **mật khẩu từ 8 ký tự**, không ép ký tự đặc biệt. **Đã chốt (CONFIRMED, người dùng chọn):** băm **bcrypt, cost 12**, **mật khẩu tối đa 64 byte** (dưới giới hạn 72 byte của bcrypt); PHP dùng `password_hash` với `PASSWORD_BCRYPT`, Java dùng thư viện bcrypt tương thích; chuỗi băm lưu trong cột đủ rộng; cùng quy tắc kiểm tra ở web và server.
3. Tọa độ: đã chốt (mục 4); còn mở **kích thước map thực** (có thể phải chỉnh lại chunk khi có map thật) và hệ chiếu cho camera trên Android.
3a. (Đã giải quyết bằng hướng B, mục 14.3; token vào lại đã chốt 30 giây; còn mở chi tiết trạng thái tạm dừng.) **Rủi ro Android:** TCP bị đứt khi đổi mạng (Wi-Fi/4G) hoặc khi app vào nền; theo quyết định hiện tại, mất kết nối giữa trận thì hủy trận (giữ thưởng đã nhận). Có cần thời gian ân hạn cho vào lại trận hay giữ nguyên quyết định không (OPEN, đã nêu ở `docs\\database\\BattleBall.md`); heartbeat/timeout phù hợp mạng di động.
4. Tần suất tối đa `PlayerStateReport`/`MinimapRevealReport` và giới hạn kích thước lô bán/hủy; thời hạn heartbeat/timeout.
5. `LeaveMatchRequest` (thoát chủ động) có khác "hủy do mất kết nối" không (đề xuất: như nhau).
6. Chiến thuật AI đồng đội: giao thức đặt (`SetSquadRequest`) và AI do server điều khiển (INFERRED từ reference) có đúng không.
7. Chiến đấu (đã chốt: góc tự do, tự đấm khi có mục tiêu trong tầm, mọi skill dùng cả hai chế độ trừ skill tự buff/hồi, làng tân thủ chơi đơn): còn mở các **con số** (tầm đấm, `travelMs`, hộp va chạm từng skill, thời lượng frame đòn thường, tầm hiệu lực của chế độ chọn mục tiêu).
8. **Chống gian lận phần thưởng (CONFIRMED, người dùng chọn): chỉ ghi log bất thường (thời gian trận quá ngắn, số boss quá nhanh, thưởng mỗi giờ vượt ngưỡng, sát thương vượt ngưỡng), xử lý thủ công**; không có chặn tự động. Hệ quả: cần bảng nhật ký `AnomalyLog` (PROPOSED) và công cụ cho admin xem; rủi ro đã ghi nhận: người chơi dùng client sửa vẫn nhận được phần thưởng cho tới khi bị xem xét thủ công. Ngưỡng cụ thể: chốt khi cân bằng.
9. Nguồn dữ liệu tĩnh chính thức cho client (costume/part/sprite) thay vì thư mục Teamobi2026; server có cần danh sách costume để kiểm skin.

## Cập nhật 2026-10-08: kho chung, hòm thư, thông báo, đổi mật khẩu (CONFIRMED, người dùng chốt)

- `PlayerDataSnapshot` nay mang dữ liệu **dùng chung của tài khoản**: `gold`, `gems`, `materials`, `skins` (`SkinInfo`: tên, `gender_id`, `kind`, `fusion_race`, `fusion_main_only`, dòng chỉ số có `StatLine.text` đã dịch sẵn), `supports`, `bag_count`, `unread_mail`. `CharacterInfo` mất `gold`, `gems`, `materials`, `owned_skins`, `supports`, `bag_count` (số trường được `reserved`) và thêm `default_costume_id` (trang phục của chủng tộc khi không có skin riêng để hiện).
- Tin nhắn mới (số trường trong `Envelope`): `MailListRequest`(41) → `MailList`(42); `MailActionRequest`(43; hành động `MAIL_READ`, `MAIL_CLAIM`, `MAIL_DELETE`, `MAIL_CLAIM_ALL`) trả `ActionResult`, thành công thì server đẩy thêm `MailList` và `PlayerDataSnapshot`; `NoticeListRequest`(44) → `NoticeList`(45); `ChangePasswordRequest`(46) → `ChangePasswordResult`(47, trả `refresh_token` mới).
- `EquipSkinRequest` đã có từ trước, nay dùng thật từ popup cải trang; server kiểm tra sở hữu theo tài khoản và chủng tộc.
- Phải sinh lại C# (`Generated/Battleball.cs`) và Java sau khi sửa proto; `BbClient` có `EquipSkinAsync`, `MailListAsync`, `MailActionAsync`, `NoticeListAsync`, `ChangePasswordAsync`.

**Cập nhật 2026-10-08 (hợp thể):** `EquipSkinRequest.fusion` (true = cải trang hợp thể vào ô `FUSION_SKIN` của nhân vật chủ); `EquipSlotInfo.slot` có thêm `FUSION_SKIN`; `CharacterInfo.fusion_stats` và `fusion_bonuses` (chỉ số khi là chủ hợp thể, đã tính skin hợp thể). Migration V028.

**Cập nhật 2026-10-08 (Hỗ trợ 5):** `EquipSkinRequest.costume_id = 0` = tháo cải trang khỏi ô (`fusion=false`: ô `SKIN`; `fusion=true`: ô `FUSION_SKIN`). Kết quả vẫn là `ActionResult`, snapshot mới được đẩy như khi mặc.

**Cập nhật 2026-10-08 (vứt cải trang):** `DiscardSkinRequest { costume_id }` (Envelope `discard_skin_request` = 48), trả `ActionResult`; thành công thì server đẩy `PlayerDataSnapshot` mới. Lỗi: `NOT_OWNED`, `DISABLED` (đang được một nhân vật mặc).

**Cập nhật 2026-10-08 (aura):** `PlayerDataSnapshot.auras` (repeated `AuraInfo{aura_id, name, unlock_level, layers[]{layer, frames}}`) và `equipped_aura_id`; yêu cầu `EquipAuraRequest{aura_id}` (Envelope `equip_aura_request` = 49; 0 = tháo) trả `ActionResult` (`NOT_FOUND`, `LEVEL_TOO_LOW`), thành công thì server đẩy `PlayerDataSnapshot` mới.

**Cập nhật 2026-10-08 (luyện tập ở làng tân thủ, docs/architecture/BattleBallServer.md 15.12):** yêu cầu `StartTrainingRequest{controlled_character_id, fusion_partner_id, arena_w, arena_h, spots[]{key, x, y}}` (Envelope `start_training_request` = 60), trả `MatchStarted` với `training = true`, `run_id = 0`, `dungeon_name = "Luyện tập"`, arena là kích thước bản đồ làng; hoặc `ActionResult` lỗi (đang có trận thật: INVALID_ARGUMENT; không NPC nào có chỗ đứng: NOT_FOUND). Mọi vị trí tính bằng **px Nro** (máy khách chia tọa độ canvas cho `UiScale` của bản đồ, 2,2). NPC luyện tập là `EntitySpawn{kind = TRAINING_DUMMY (6), dummy_key, mob_template_id (0 = Mộc nhân, -1 = vẽ bằng costume_id), costume_id}`. Sau đó vẫn là `MatchFrame` (20 Hz), `UseSkillRequest`, `LeaveMatchRequest` như trận thường; `PlayerStateReport.allies[]{entity_id, x, y, dir_x, dir_y}` báo vị trí 2 AI (chỉ dùng khi luyện tập vì làng có trọng lực và do máy khách mô phỏng). NPC hết máu thì có `EffectEvent{effect = REGEN, target = NPC}` và máu về đầy. Kết thúc bằng `MatchEnded{result = ABANDONED}` (rời, bỏ không quá `idle_end_s`, hay mất kết nối: không tạm dừng, không vào lại). `SkillSlot` thêm `icon_id` (icon Nro của nút skill) và `name`.

**Cập nhật 2026-10-08 (tab Kỹ năng):** `PlayerDataSnapshot.skill_catalog` (field 15) liệt kê mọi skill đang bật: `SkillCatalogEntry{skill_id, race (EARTH/NAMEC/XAYDA/COMMON), name, icon_id, slot, max_level, description (dam_info của NRO), levels[]{level, gold_cost, damage_pct_atk, cooldown_ms, ki_cost_pct}}`. Học và nâng cấp vẫn qua `UpgradeSkillRequest{character_id, skill_id}` (trả `ActionResult`: NOT_ENOUGH_GOLD, NOT_FOUND khi skill không thuộc hành tinh, MAXED), thành công thì server đẩy snapshot mới.

**Cập nhật 2026-10-09 (hồi chiêu giữ qua đăng xuất, tự động dùng skill):** `CharacterInfo.cooldowns` (field 21, `SkillCooldown{skill_id, left_ms}`) và `SkillSlot.cooldown_left_ms` (field 9, lúc phiên bắt đầu) cho biết hồi chiêu còn lại theo giờ thật của server; `SkillLevelInfo.range_px` (field 6) là khoảng cách (px Nro, tâm tới tâm, đã tính bán kính Mộc nhân) mà skill đáng dùng, bằng con số AI đồng đội dùng (`PlayerRepository.reachPx`), 0 = skill không cần mục tiêu. Máy khách dùng nó để tự chạy tới mục tiêu trước khi đánh. Không thêm thông điệp mới: dùng skill vẫn là `UseSkillRequest`.

## Cập nhật 2026-10-09: cửa hàng NPC (CONFIRMED, người dùng chốt)

`ShopRequest{npc_id}` (payload 65, cần đăng nhập) trả `ShopContent{npc_id, npc_name, tabs[]{name, items[]{skin: SkinInfo, gem_price}}}` (66), hoặc `ActionResult` NOT_FOUND khi NPC không bán gì. Mua một cải trang dùng `BuySkinRequest` (không có message mua riêng của cửa hàng); máy chủ kiểm lại mọi điều kiện, và gửi `PlayerDataSnapshot` mới khi thành công. Cơ chế: `docs/architecture/BattleBallServer.md` 15.15.

### Cập nhật 2026-10-10: gồng, bình Ma phong ba, chỉ số thời gian thực (CONFIRMED)

- `Effect` thêm `CAST_END = 13` (một cast, gồng, kênh hoặc dây trói kết thúc sớm: máy khách dừng tư thế; `skill_id`, `source_id`, và `target_id` với dây trói), `MAFUBA_GATHER = 14` (mục tiêu bị hút vào bình, `duration_ms` đến lúc thành bình, `x, y` điểm đặt bình), `JAR = 15` (mục tiêu là cái bình trong `duration_ms`, tính từ lúc nhả chiêu) và `ULTIMATE_BEAM = 16` (chùm của Super Kamejoko hoặc Cađíc: `x, y, dir`, `length`, `width`, `duration_ms` thời gian gây sát thương).
- `EntityState.effects` thêm bit 256 (bận: gồng, tụ lực, chùm tuyệt kỹ), 512 (là cái bình), 1024 (đang bị hút vào bình), 2048 (khỉ); máy khách khoá nhân vật theo các bit này. `EntityState` thêm `atk = 12`, `armor = 13`, `crit_rate_pct = 14`, `crit_dmg_pct = 15` (số liệu hiện tại, đã tính buff) cho menu.
