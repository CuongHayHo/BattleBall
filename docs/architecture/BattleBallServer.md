# BattleBallServer: Architecture Design (Proposed)

> **Trạng thái:** ARCHITECTURE DESIGN ONLY. `E:\PRJNRO\BattleBallServer` hiện là **thư mục trống** (0 file, CONFIRMED 2026-10-07). Không có source code nào được tạo.
> Nhãn: **CONFIRMED** (có bằng chứng trong source/docs), **PROPOSED** (đề xuất thiết kế), **UNKNOWN / OPEN** (chưa đủ thông tin, cần quyết định).
> Tài liệu liên quan: `docs\networking\BattleBallServer.md`, `docs\protocol\BattleBallServer.md`.

---

## 1. Bối cảnh và bằng chứng đầu vào

| Nguồn | Điều đã xác minh | Ảnh hưởng tới thiết kế |
|---|---|---|
| BattleBall client (`E:\PRJNRO\BattleBall`) | Unity 2022.3.62f2, URP 2D, uGUI. Nội dung duy nhất là công cụ **NroMannequin/Costume Tester**: rig 4 slot (Head/Body/Leg/Aura), 33 tư thế, 414 trang phục, 984 part, zoom x1–x4. **Không có networking, login, gameplay, save.** | Server chưa có yêu cầu gameplay cụ thể từ client. Thiết kế phải là bộ khung tối thiểu và mở rộng được, không lấp đầy bằng tính năng NRO. |
| BattleBall data (`Assets\NroMannequin\Data\`) | Trang phục định danh bằng `id`, gồm `headPartId/bodyPartId/legPartId`; part định danh bằng `partId`. Dữ liệu là JSON tĩnh trong client. | Ngoại hình nhân vật có thể biểu diễn bằng costume id/part id. Có thể là dữ liệu dùng chung (shared) giữa client và server. |
| BattleBall poses (`NroPoseTable.cs`) | Tư thế gồm đứng, chạy, nhảy, bay, rơi, đấm, đá, chưởng, bị đánh, chết. | Gợi ý (INFERRED) game có di chuyển 2D và combat; chưa có logic. |
| Reference server (`docs\*\Teamobi2026.md`) | Server-authoritative về HP, damage, cooldown, nhưng **client quyết định vị trí và mục tiêu**; nhiều thread tick khoảng 1 giây; XOR key tĩnh; mật khẩu dạng rõ; lưu player là JSON theo vị trí; không có heartbeat; dispatch bằng `switch` khổng lồ. | Học concept (Session/Message/Controller, Map → Zone, template id, auto-save, hàng đợi gửi theo session), **tránh** các điểm yếu đã ghi nhận. |
| Reference client (`docs\*\NroUnity.md`) | Client tự tính vật lý, nội suy người khác, hàng đợi nhận chuyển sang main thread. | Concept "network thread → main-thread queue" phù hợp cho BattleBall client tương lai. |

**Kết luận đầu vào (CONFIRMED):** hiện tại chưa có yêu cầu gameplay nào được xác minh từ BattleBall. Mọi hệ thống gameplay trong tài liệu này là **PROPOSED** hoặc **UNKNOWN**, và phải được xác nhận trước khi implement.

---

## 2. Server Requirements

> **Cập nhật (sau khi có GDD):** game là **chơi đơn**, server chỉ lưu dữ liệu và duyệt kết quả; đồng bộ nhiều người chơi cùng map là NOT REQUIRED. Combat, Skill, Item, Monster/Boss chuyển sang REQUIRED. Chi tiết mô hình dữ liệu: `docs\database\BattleBall.md`. Các mục Movement/Sync/Visibility trong tài liệu này viết cho trường hợp nhiều người chơi, cần xem lại.

> **Cập nhật (trả lời OPEN):** 3 nhân vật mỗi tài khoản, hợp thể 2 thành 1 (đội 1 hợp thể + 1 AI), raid = 3 nhân vật của một người chơi, cần mạng liên tục, combat theo Teamobi2026 (server mô phỏng và quyết định kết quả). Hệ quả: server phải chạy **vòng lặp mô phỏng cho mỗi trận** (mob, boss, AI đồng đội) thay vì chỉ lưu dữ liệu. Chi tiết: `docs\database\BattleBall.md`.

> **Cập nhật (Android):** target platform là Android; server Java, TCP, TLS; mất kết nối giữa trận thì tạm dừng ≤ 30 giây (≤ 2 lần mỗi trận) cho vào lại. Chi tiết: `docs\\protocol\\BattleBall.md` mục 14.

> **Cập nhật (quyết định thêm):** không kiểm tra vị trí/tầm đánh (như Teamobi2026); mỗi trận chạy kiểu Zone của Teamobi2026 và **không có PvP** (bỏ mọi phần PvP/visibility giữa người chơi khỏi thiết kế); mất kết nối thì hủy trận nhưng giữ phần thưởng đã nhặt (ghi ngay khi nhặt); hợp thể = cộng chỉ số, skill theo nhân vật chủ, cặp chốt từ đầu trận đến hết combat; EXP tính riêng cho từng trong 3 nhân vật. Các mục Movement validation, Combat validation (tầm) trong tài liệu này cần xem lại theo quyết định không kiểm vị trí.

> **Cập nhật (quyết định thêm):** boss chết thì phần thưởng item/vàng tự động vào túi và được ghi ngay; EXP chỉ cấp khi thắng trận; minimap lưu trên server theo tài khoản; skin = costume id sở hữu theo tài khoản. Chi tiết: `docs\database\BattleBall.md`.

> **Cập nhật (quyết định thêm):** mob thường cũng rơi đồ tự động vào túi (như boss); mỗi trận là một combat liên tục nên hợp thể kéo dài đến hết trận; EXP theo độ khó phó bản; skin khóa theo chủng tộc. Chi tiết: `docs\database\BattleBall.md`.

> **Cập nhật (quyết định thêm):** tick mô phỏng **20 Hz (50 ms)** (reference 1 giây cho mob/item); đăng nhập chồng: kick phiên cũ; hồi sinh tối đa 2 lần mỗi trận. Chi tiết: `docs\database\BattleBall.md`.

> **Dọn dẹp (2026-10-07, CONFIRMED):** người dùng xác nhận game **chơi đơn hoàn toàn: không kết bạn, không có người chơi khác ở bất kỳ map nào (kể cả làng tân thủ)**. Mọi nội dung **nhiều người chơi** dưới đây (người chơi thấy nhau, visibility/interest, broadcast tới người khác, Sync nhiều người, concurrent players theo map, xác thực vị trí theo intent) là **LỖI THỜI (OBSOLETE)** và đã được gắn nhãn tại chỗ. Thiết kế hiện hành: mỗi trận/làng là **một zone riêng cho một người chơi**, server mô phỏng 20 Hz, `Sync` chỉ gửi trạng thái cho một client, vị trí do client báo và **không kiểm tra**. Nguồn sự thật hiện hành: `docs\protocol\BattleBall.md`, `docs\networking\BattleBall.md`, `docs\database\BattleBall.md`. Database đã chốt: **MariaDB/MySQL**.

| Hệ thống | Phân loại | Lý do / bằng chứng |
|---|---|---|
| Connection | **REQUIRED** | Bất kỳ client–server nào cũng cần. |
| Protocol (framing, serialization) | **REQUIRED** | Như trên. |
| Session | **REQUIRED** | Cần gắn kết nối với danh tính và trạng thái. |
| Logging | **REQUIRED** | Reference nuốt lỗi và log tối đa 5 lần (`docs\networking\Teamobi2026.md`); cần khắc phục để debug được. |
| Error handling | **REQUIRED** | Như trên. |
| Heartbeat/timeout | **REQUIRED** | Reference không có (`docs\networking\COMPARISON.md`); kết nối chết sẽ treo session. |
| Authentication | **LIKELY REQUIRED** | Chưa có UI login trong client; nếu có tài khoản thì cần. Kiểu (tài khoản, guest, token): UNKNOWN. |
| Player | **LIKELY REQUIRED** | Cần đại diện người chơi trong thế giới. |
| Character appearance (costume/part) | **LIKELY REQUIRED** | Client đã có hệ thống costume. (~~người chơi khác cần biết ngoại hình để vẽ~~ LỖI THỜI: không có người chơi khác; server chỉ lưu costume id của chính người chơi.) |
| Movement | **REQUIRED ở mức báo cáo vị trí** | ~~Server validate di chuyển~~ LỖI THỜI: client báo vị trí, server **không kiểm tra** (CONFIRMED). |
| World/Map | **LIKELY REQUIRED** | Cần không gian để đặt người chơi; cấu trúc map: UNKNOWN (client chưa có map). |
| Entity | **LIKELY REQUIRED** | Đại diện chung cho player và các đối tượng khác. |
| Game state | **REQUIRED** | Server giữ trạng thái trận (HP, Ki, cooldown, hợp thể) cho một người chơi. |
| Synchronization | **Chỉ một client** (cập nhật) | ~~Để người chơi thấy nhau~~ LỖI THỜI: chỉ gửi trạng thái trận/delta cho đúng một người chơi, không có visibility giữa người chơi. |
| Persistence/Database | **LIKELY REQUIRED** | Nếu có tài khoản và tiến trình. Phạm vi: UNKNOWN. |
| Combat | **REQUIRED** (cập nhật theo GDD) | GDD `docs\gameplay\game-design-doc.md`: hack & slash, boss, kỹ năng. Mô hình chơi đơn; mức authority: OPEN (xem `docs\database\BattleBall.md`). |
| Skill | **REQUIRED** (cập nhật theo GDD) | Bảng kỹ năng 3 chủng tộc, cấp 1–7. |
| Item/Inventory | **REQUIRED** (cập nhật theo GDD) | 5 slot trang bị, mảnh đồ, đá, GEN, tiền tệ; lưu trên server. |
| Equipment | **REQUIRED** (cập nhật theo GDD) | 5 slot; liên hệ với costume/skin: OPEN. |
| NPC | **UNKNOWN** | Không có bằng chứng. |
| Monster / Boss | **REQUIRED** (cập nhật theo GDD) | Mob đàn, boss theo vùng, phó bản, raid boss; chỉ số tuyệt đối từ dữ liệu tĩnh. Không phải đặc thù NRO. |
| Boss, bot, phó bản, minigame, clan, shop, giftcode, nạp thẻ, web portal | **NOT REQUIRED** | Không có bằng chứng; là đặc thù NRO. |

---

## 3. Proposed Server Architecture

**Nguyên tắc (PROPOSED):**
1. **Server-authoritative** cho mọi state quan trọng.
2. **Đơn tiến trình (single process), nhiều map**, không microservice.
3. **Mỗi trận/zone một tác vụ logic (single-threaded per match) trên pool luồng cố định, tick 20 Hz** (cập nhật; trước đây ghi "mỗi map"); không có thread riêng cho mỗi người chơi; không sửa state từ thread mạng.
4. Phân lớp rõ ràng, phụ thuộc một chiều: `Networking → Protocol → Session → Application/Gameplay → Persistence`.
5. Nhỏ nhất có thể; hệ thống gameplay chỉ thêm khi yêu cầu được xác nhận.

```
BattleBallServer
├── Bootstrap        Khởi động, nối các module, shutdown an toàn
├── Configuration    Đọc config (port, tick rate, DB, giới hạn); không hardcode
├── Infrastructure   Logging, clock/time, metrics cơ bản, ID generator
├── Networking       Listener, Connection, framing I/O, hàng đợi in/out, heartbeat, timeout
├── Protocol         Message (header, id, payload), Encoder/Decoder, registry message
├── Dispatch         Map message id → handler, kiểm tra trạng thái session trước khi gọi
├── Authentication   Xác thực tài khoản/token, chống brute-force
├── Session          Vòng đời session, gắn connection ↔ account ↔ player
├── Player           Dữ liệu người chơi trong RAM, appearance (costume), vào/ra world
├── World            Danh sách map, định tuyến player tới map, MapLoop (tick)
│   ├── Map          Không gian, ranh giới, (tile/collision: UNKNOWN), danh sách entity
│   └── Entity       Định danh, vị trí, trạng thái; lifecycle spawn/update/despawn
├── Gameplay         (mỗi hệ thống chỉ bật khi yêu cầu được xác nhận)
│   ├── Movement     Nhận vị trí client báo (KHÔNG validate: CONFIRMED; "validate" cũ là LỖI THỜI)
│   ├── Combat       Resolve tấn công, damage, chết     [REQUIRED]
│   ├── Skill        Cooldown, cost, hiệu ứng           [REQUIRED]
│   └── Item         Inventory, sử dụng item            [REQUIRED]
├── Sync             Tạo gói state/event gửi MỘT client (Visibility/interest nhiều người: LỖI THỜI)
├── Persistence      Load/save player qua repository, lưu định kỳ + khi thoát, transaction
├── Database         Kết nối/pool, migration, repository implementation
├── StaticData       Nạp dữ liệu tĩnh dùng chung (costume, part…, nếu server cần)
└── Events           Event nội bộ đơn giản trong cùng map loop (không bus phân tán)
```

| Module | Trách nhiệm | Không làm |
|---|---|---|
| Bootstrap | Đọc config, khởi tạo theo thứ tự (Infrastructure → Database → StaticData → World → Networking), đăng ký shutdown hook (lưu tất cả player, đóng kết nối). | Không chứa logic game. |
| Networking | Chấp nhận kết nối, đọc/ghi byte, tách frame, hàng đợi ra theo connection, heartbeat/timeout, giới hạn kết nối. | Không đụng game state. |
| Protocol | Định nghĩa khung message, encode/decode, version. | Không có logic nghiệp vụ. |
| Dispatch | Tra message id → handler đã đăng ký; từ chối message không hợp lệ theo trạng thái session. | Không dùng `switch` khổng lồ. |
| Authentication | Kiểm tra thông tin đăng nhập (mật khẩu băm hoặc token), rate limit. | |
| Session | `CONNECTED → AUTHENTICATED → IN_WORLD → CLOSING`; đăng nhập chồng; dọn dẹp. | |
| Player | Trạng thái người chơi (id, tên, costume, map, vị trí…). | Không tự gửi gói; đi qua Sync. |
| World/Map | Mỗi map một hàng đợi lệnh và một tick loop; giữ entity; xử lý vào/ra map. | |
| Gameplay | Luật game, chạy bên trong map tick. | Không đọc socket hay DB trực tiếp. |
| Sync | Gom thay đổi của trận và gửi delta/event cho một client (~~xác định ai thấy ai~~ LỖI THỜI). | |
| Persistence/Database | Repository, transaction, migration, lưu bất đồng bộ ngoài map tick. | Không chặn map tick bằng I/O DB. |
| Events | Thông báo nội bộ (ví dụ "player entered map") trong cùng luồng map. | Không dùng bus xuyên thread khi chưa cần. |

---

## 4. Client ↔ Server Architecture

```
BattleBall Client (Unity)
   Input → Intent (yêu cầu)          Presentation ← State/Event
        ↓                                   ↑
   Network client (thread) ──frames──►  Networking (Listener/Connection)
   main-thread queue ◄──frames───────   Outbound queue (per connection)
                                            ↓ decode
                                        Protocol → Dispatch → Session check
                                            ↓ enqueue command
                                        Map loop (tick): validate → apply → Game State
                                            ↓                         ↓
                                        Sync (outbound, một client)  Persistence (async)
```

| Trách nhiệm | Client | Server | Trạng thái |
|---|---|---|---|
| Input, rendering, UI, animation, costume rendering | ✔ | | CONFIRMED (client đã có rendering) |
| Gửi intent (muốn di chuyển, muốn tấn công…) thay vì kết quả | ✔ | | PROPOSED |
| Validation, game state, luật, persistence | | ✔ | PROPOSED |
| Vị trí authoritative | | ✔ | PROPOSED (reference cho client quyết định, đã ghi là rủi ro) |
| Local prediction / interpolation | ✔ (tuỳ chọn) | | PROPOSED; mức độ prediction: **UNKNOWN** (phụ thuộc thể loại và yêu cầu độ trễ) |
| Vật lý/collision | | ✔ (nguồn sự thật) | PROPOSED; mô hình collision: **UNKNOWN** (chưa có map ở client) |
| Ownership dữ liệu costume/part | Client có bản tĩnh | Server có cần không: **UNKNOWN** | Xem Open Questions |

---

## 5. Networking và Protocol (tóm tắt)
Chi tiết: `docs\networking\BattleBallServer.md` và `docs\protocol\BattleBallServer.md`.
Vòng đời: `CONNECT → (HANDSHAKE/VERSION) → AUTHENTICATE → CREATE SESSION → ENTER WORLD → PLAY → DISCONNECT`.

---

## 6. Server Authority (PROPOSED; một phần LỖI THỜI)

> **LỖI THỜI (2026-10-07):** các dòng "Vị trí", "Movement" và "Combat (kiểm tầm)" dưới đây (server kẹp tốc độ, kiểm tầm, không gán thẳng tọa độ) **bị thay thế** bởi quyết định CONFIRMED: client báo vị trí và mục tiêu, server **không kiểm tra** vị trí/tầm; server vẫn tính damage, HP, cooldown, drop, EXP. Xem `docs\database\BattleBall.md` mục 6 và `docs\protocol\BattleBall.md`.

| State | Server chịu trách nhiệm | Ghi chú |
|---|---|---|
| Vị trí | Nhận intent (hướng/đích), tính và kẹp theo tốc độ tối đa, ranh giới map, (collision: UNKNOWN), gửi vị trí authoritative | Không gán thẳng tọa độ client như `PlayerService.playerMove` của reference |
| Movement | Kiểm tra tần suất, khoảng cách theo thời gian, trạng thái (chết, bị khống chế) | |
| Combat (nếu có) | Server chọn/kiểm tra mục tiêu, tầm đánh, cooldown, cost; tính damage | Reference chỉ kiểm tầm cho đòn cận chiến; ở đây mọi đòn đều phải kiểm |
| Damage | Chỉ tính ở server; kiểu số đủ rộng (không ép `int` như reference) | |
| Skill (nếu có) | Cooldown, tài nguyên, điều kiện dùng | |
| Item/Inventory (nếu có) | Mọi thay đổi chỉ qua server, trong transaction | |
| Costume/appearance | Server xác nhận người chơi được dùng costume đó (nếu có sở hữu); phát cho người khác | Quy tắc sở hữu: UNKNOWN |
| Player state, world state | Chỉ thay đổi trong map tick | |

Client **không** authoritative đối với: vị trí cuối cùng, HP, damage, kết quả combat, inventory, tài nguyên.

---

## 7. World & Gameplay Architecture (PROPOSED)

```
World (registry các Map)
 └─ Map instance (1 hàng đợi lệnh + 1 tick loop)
     ├─ Entity store (id → Entity)
     │   ├─ PlayerEntity (gắn Session)
     │   └─ [NPC/Monster] — chưa yêu cầu (UNKNOWN/NOT REQUIRED)
     ├─ Systems theo thứ tự trong tick: Input commands → Movement → (Combat) → (Skill) → Cleanup
     └─ Sync: visibility → outbound
```
- **Tick:** cố định cho mỗi map (giá trị cụ thể: UNKNOWN, phụ thuộc thể loại; reference chạy khoảng 1 Hz, quá chậm cho combat thời gian thực).
- **Entity lifecycle:** `Spawn` (tạo, gán id, thêm vào map, thông báo cho người thấy) → `Update` (trong tick) → `Despawn` (rời map, thông báo, giải phóng).
- **Đổi map:** rút entity khỏi map A (trong tick của A) → chuyển lệnh sang hàng đợi map B → spawn ở B. Không cho hai map cùng sửa một entity.
- **Visibility:** ~~ban đầu toàn map; chia vùng/kênh khi nhiều người~~ **LỖI THỜI**: mỗi zone chỉ có một người chơi, không cần visibility/interest management. (Mob/boss/AI đồng đội trong zone đều do server điều khiển và gửi cho đúng một client.)
- **Synchronization:** ~~PROPOSED event + vị trí định kỳ cho người khác~~ **thay bằng** thiết kế trong `docs\protocol\BattleBall.md` (delta theo tick 20 Hz cho một client, `SkillCast`, `CombatEvent`). Danh sách entity chỉ gồm người chơi (nhân vật điều khiển), AI đồng đội, mob, boss, đạn; **không có người chơi khác**.
- **State:** trạng thái entity là enum rõ ràng (ví dụ Alive/Dead) thay cho cờ rải rác như reference.

---

## 8. Database Architecture

| Mục | Quyết định | Trạng thái |
|---|---|---|
| Có database | **Có** (tài khoản, nhân vật, túi đồ, ví, token): `docs\database\BattleBall.md` | CONFIRMED |
| Công nghệ | **MariaDB/MySQL (InnoDB)**, cùng loại reference. ~~PostgreSQL~~ không chọn. | **CONFIRMED** (người dùng chốt 2026-10-07) |
| Connection layer | Connection pool với kích thước > 1 (reference là 1: CONFIRMED); cấu hình từ file config | PROPOSED |
| Access | Repository interface trong domain, implementation trong Database; SQL tham số hoá; không nối chuỗi | PROPOSED |
| Migration | Có công cụ migration có version (thay vì dump SQL như reference) | PROPOSED |
| Model | Bảng chuẩn hoá cho dữ liệu cần truy vấn (account, player, và item/inventory nếu có). Không dùng JSON theo vị trí không tên trường (điểm yếu của reference). Nếu cần JSON thì có tên trường và version. | PROPOSED |
| Mật khẩu | **bcrypt cost 12**, tối đa 64 byte (reference lưu dạng rõ: CONFIRMED) | CONFIRMED |
| Transaction | Một transaction cho mỗi thao tác nghiệp vụ nhiều bước (ví dụ đổi inventory, giao dịch) | PROPOSED |
| Save/Load | Load khi vào world; lưu khi đổi trạng thái quan trọng, định kỳ, khi thoát; I/O chạy ngoài tick của trận (worker/DB pool riêng), tick chỉ tạo bản chụp dữ liệu cần lưu. Phần thưởng ghi ngay khi nhận; DB lỗi giữa trận thì hủy trận (CONFIRMED) | PROPOSED / CONFIRMED |
| Schema cụ thể | **Chưa tạo**: chưa đủ yêu cầu | UNKNOWN |

---

## 9. Scalability (giai đoạn đầu)

- **Single server process**, nhiều trận đồng thời, mỗi trận một tác vụ trên pool luồng cố định (số luồng: OPEN); ~~mỗi map một luồng~~ LỖI THỜI. PROPOSED.
- Networking dùng I/O bất đồng bộ (không phải 2 thread cho mỗi kết nối như reference). PROPOSED.
- Persistence chạy trên worker pool riêng; map tick không chờ DB. PROPOSED.
- Giới hạn: ~~người mỗi map~~ (LỖI THỜI), kết nối mỗi IP (≤ 20), tốc độ gói (≤ 20/giây), kích thước gói (≤ 256 KB): CONFIRMED; tổng số kết nối: OPEN.
- Chưa cần microservice, cluster hay cache phân tán. Chỉ xét lại khi có số liệu tải thực tế.

---

## 10. Teamobi2026 Mapping

| Teamobi2026 | BattleBallServer | Decision | Lý do |
|---|---|---|---|
| `Session` + `Message` + `Controller` (`docs\architecture\Teamobi2026.md`) | Connection + Message + Dispatch | **ADAPT** | Concept tốt; thay `switch` bằng registry handler |
| `MessageSendCollect` framing `cmd(1)+size(2)` | Protocol framing | **ADAPT** | Ý tưởng length-prefixed tốt; kích thước, id width, version: TO BE DESIGNED |
| XOR key `"NRO"` | Bảo mật transport | **REPLACE** | Không phải bảo mật thực sự; **TLS** (CONFIRMED) |
| Sender queue mỗi session (`LinkedBlockingDeque`) | Outbound queue mỗi connection | **REUSE (concept)** | Tách gửi khỏi logic |
| Xử lý gói trên thread Collector | Decode ở network, logic ở map loop | **REPLACE** | Tránh race với tick |
| `Map` → `Zone` | Mỗi trận = một Zone riêng cho một người chơi | **ADAPT** | ~~vùng visibility/kênh~~ LỖI THỜI: không có người chơi khác |
| Tick 1 giây, thread riêng cho mỗi player | Tick 20 Hz cho mỗi trận trên pool luồng cố định, không thread mỗi player | **REPLACE** | Reference có thể cập nhật trùng, khó đồng bộ |
| `Manager` nạp template vào RAM | StaticData loader | **ADAPT** | Concept đúng; tránh singleton toàn cục |
| `MrBlue.login` + `AntiLogin` | Authentication + rate limit | **ADAPT** | Giữ chống brute-force; băm mật khẩu |
| Kick session cũ khi đăng nhập chồng | Session policy | **REUSE (concept)** | Hành vi hợp lý |
| `PlayerDAO` JSON theo vị trí | Repository + bảng chuẩn hoá | **REPLACE** | Mong manh, khó migrate |
| Auto-save 2 phút + lưu khi ngắt | Persistence policy | **ADAPT** | Giữ ý tưởng; chạy bất đồng bộ |
| HikariCP pool size 1 | Pool cấu hình > 1 | **ADAPT** | |
| `DataGame` phục vụ tài nguyên qua TCP | Phân phối asset | **DROP (hiện tại)** | Client đã có dữ liệu tĩnh; phân phối asset: OPEN |
| `PlayerService.playerMove` gán thẳng vị trí | Gán thẳng vị trí client báo, không kiểm tra | **REUSE (CONFIRMED, người dùng chốt)** | ~~REPLACE / Movement validation~~ LỖI THỜI; rủi ro xử lý bằng `AnomalyLog` |
| `SkillService` (cooldown/mana server-side) | Skill (nếu có) | **ADAPT (concept)** | Giữ ý tưởng kiểm cooldown/cost ở server |
| Boss, Bot, phó bản, minigame, clan, shop, giftcode, nạp thẻ | Boss, phó bản, mob, shop (làng tân thủ, vàng) **có** theo GDD; Bot, minigame, clan, giftcode, nạp thẻ: **DROP** | **ADAPT / DROP** | Cập nhật theo GDD (dòng cũ ghi DROP toàn bộ là LỖI THỜI) |
| Web ↔ server qua polling bảng DB | Web PHP đăng ký ghi trực tiếp vào DB (cùng bcrypt, quy tắc tên/mật khẩu) | **ADAPT** | CONFIRMED (xem `docs\protocol\BattleBall.md` câu 2) |
| `Controller` nuốt exception | Error handling + logging có cấu trúc | **REPLACE** | Cần debug được |

---

## 11. BattleBall Mapping

| BattleBall client (hiện có) | BattleBallServer | Ghi chú |
|---|---|---|
| `NroCostumeEntry` (`id`, `head/body/legPartId`) | Player appearance = costume id (hoặc 3 part id) | Chọn biểu diễn nào: **OPEN** |
| `NroMannequin` (pose, facing) | Entity state: vị trí, hướng, trạng thái hành động | Client tự chọn pose; trạng thái di chuyển hiển thị (`FLY`...) là phía client, không gửi lên server |
| `CostumeCatalog.json`, `PartDatabase.json` | StaticData (nếu server cần kiểm costume hợp lệ) | Shared hay client-only: **OPEN** |
| `CostumeTesterUI` | — | Công cụ editor/test, không cần server |
| (chưa có) PlayerController | PlayerEntity gắn với Session | Chỉ một người chơi mỗi zone |

**Flow di chuyển (PROPOSED; LỖI THỜI: bước "Movement validate" và "Sync broadcast tới người thấy" không còn. Hiện hành: client báo vị trí → server gán thẳng, không kiểm tra → chỉ gửi state cho một client):**
`Client input → MoveIntent → Network → Decode → Dispatch (session IN_WORLD) → enqueue vào map → tick: Movement validate (tốc độ, ranh giới, trạng thái) → vị trí authoritative → Sync broadcast tới người thấy → client hiển thị (và hiệu chỉnh nếu có prediction)`

**Flow combat (PROPOSED, chỉ khi combat được xác nhận):**
`Client input → AttackIntent (skill, hướng/mục tiêu) → Dispatch → map tick: kiểm cooldown, cost, tầm, mục tiêu → Combat resolution (damage server-side) → cập nhật HP/trạng thái → event kết quả tới người thấy → client phát animation (pose đánh, bị đánh, chết)`

**Flow vào world (PROPOSED):**
`Connect → Handshake/version → Authenticate → Session AUTHENTICATED → load Player (Persistence, async) → EnterWorld → spawn vào map → gửi trạng thái ban đầu (bản thân + entity xung quanh) → IN_WORLD` *(LỖI THỜI: "entity xung quanh" chỉ gồm mob/boss/AI của zone riêng; không có người chơi khác)*

---

## 12. Project Structure (đề xuất, CHƯA TẠO)

Ngôn ngữ/runtime: **OPEN** (xem Open Questions). Cấu trúc dưới đây độc lập ngôn ngữ.
```
BattleBallServer/
├── src/
│   ├── Bootstrap/
│   ├── Configuration/
│   ├── Infrastructure/      (Logging, Time, Ids)
│   ├── Networking/          (Listener, Connection, Framing, Heartbeat)
│   ├── Protocol/            (Message, Codec, MessageRegistry)
│   ├── Dispatch/            (HandlerRegistry, handlers theo feature)
│   ├── Authentication/
│   ├── Session/
│   ├── Player/
│   ├── World/               (World, Map, MapLoop, Entity)
│   ├── Gameplay/            (Movement/, sau này Combat/, Skill/, Item/)
│   ├── Sync/
│   ├── StaticData/
│   ├── Persistence/         (repository interfaces, save scheduler)
│   └── Database/            (connection pool, repository impl, migrations)
├── shared/                  (định nghĩa message dùng chung với client, nếu chọn cách chia sẻ: OPEN)
├── tests/                   (unit cho codec, validation; integration cho login → enter world)
├── config/                  (file cấu hình mẫu, không chứa mật khẩu thật)
└── docs/
```

---

## 13. Architecture Decisions

### Confirmed Decisions (bắt buộc theo evidence)
1. Server phải được xây từ đầu: `BattleBallServer` trống; BattleBall chưa có lớp mạng (CONFIRMED).
2. Không tái sử dụng giao thức NRO nguyên trạng: BattleBall không có code giao thức nào phụ thuộc vào nó; giao thức NRO có XOR tĩnh và các điểm lệch đã ghi nhận (`docs\protocol\COMPARISON.md`).
3. Không thêm hệ thống đặc thù NRO (boss, bot, phó bản, clan, nạp thẻ…) khi chưa có yêu cầu.
4. Phải có heartbeat/timeout, logging lỗi, và mật khẩu băm (nếu có tài khoản), vì thiếu chúng là điểm yếu đã xác minh ở reference.

### Proposed Decisions
1. Server-authoritative cho vị trí, combat, inventory, tài nguyên.
2. Đơn tiến trình; mỗi map một luồng logic với tick cố định; command queue từ network vào map.
3. Networking I/O bất đồng bộ; outbound queue theo connection; decode ngoài map loop.
4. Message có header, id, length, payload; handler registry; version giao thức trong handshake.
5. Persistence qua repository, migration có version, transaction cho thao tác nhiều bước, lưu bất đồng bộ.
6. Bắt đầu với Movement + Appearance + Session/Auth; các hệ thống gameplay khác thêm theo yêu cầu.

### Open Questions
> **Cập nhật 2026-10-07:** các câu 1 đến 7 và 11, 12 dưới đây **đã được trả lời** (xem mục 4 của `docs\HANDOFF.md`). Giữ lại danh sách cũ để truy vết.

1. ~~Thể loại/gameplay~~ **ĐÃ CHỐT:** hack & slash thời gian thực, chơi đơn, không PvP, không người chơi khác, đội 3 người (1 điều khiển + 2 AI).
2. ~~Ngôn ngữ/runtime server~~ **ĐÃ CHỐT:** Java + Netty.
3. ~~Transport~~ **ĐÃ CHỐT:** TCP (client Android).
4. ~~Có tài khoản không~~ **ĐÃ CHỐT:** tài khoản (đăng ký trong game và web PHP), token dài hạn nhiều thiết bị, hạn trượt 30 ngày.
5. ~~Serialization~~ **ĐÃ CHỐT:** Protobuf.
6. ~~Bảo mật transport~~ **ĐÃ CHỐT:** TLS ngay từ đầu.
7. ~~Tick rate~~ **ĐÃ CHỐT:** 20 Hz; client báo vị trí, server không kiểm tra; delta cho một client.
8. Map/collision ở server: có tile map như NRO không? Dữ liệu map từ đâu? (**vẫn OPEN**, chờ dữ liệu map/vật cản của người dùng)
9. Costume: biểu diễn bằng costume id hay part id; người chơi có "sở hữu" costume không; dữ liệu costume là shared hay client-only.
10. Dữ liệu tĩnh hiện được đọc từ thư mục Teamobi2026 qua đường dẫn tuyệt đối trong client; nguồn chính thức cho cả client và server là gì?
11. ~~Có cần web/admin tool không?~~ **ĐÃ CHỐT:** web PHP đăng ký ghi trực tiếp vào DB; công cụ admin xem `AnomalyLog` (PROPOSED).
12. ~~DB cụ thể~~ **ĐÃ CHỐT: MariaDB/MySQL (InnoDB)**. Môi trường deploy: vẫn OPEN.

---

## 14. Risks

| Rủi ro | Mô tả | Giảm thiểu (PROPOSED) |
|---|---|---|
| Protocol mismatch | Reference có lệch giữa client/server (size 3 byte, `-4/67`, trường `-17`) do không có schema chung | Định nghĩa message một nơi; version trong handshake; test codec hai chiều |
| Client/server coupling | Client hiện đọc dữ liệu từ thư mục server reference; dễ lan sang server | Tách StaticData thành nguồn chính thức; không dùng đường dẫn tuyệt đối |
| State sync | Tick chậm + event lẻ (reference) gây giật; nhiều thread sửa state gây race | Một luồng logic mỗi map; quyết định tick/sync sớm |
| Cheating | Reference tin vị trí và mục tiêu từ client | ~~Intent-based, validate ở server~~ LỖI THỜI: **đã chốt** không kiểm vị trí; chỉ ghi log bất thường (`AnomalyLog`), xử lý thủ công, giới hạn tốc độ gói 20/giây |
| Persistence | JSON theo vị trí, không transaction, pool 1 (reference) | Repository, migration, transaction, pool cấu hình |
| Concurrent players | Thread mỗi player/session không mở rộng được | I/O bất đồng bộ, pool luồng cố định chạy mỗi trận một tác vụ |
| Performance | DB I/O chặn tick | Lưu bất đồng bộ (~~broadcast toàn map/visibility~~ LỖI THỜI: không có người chơi khác) |
| Error recovery | Nuốt exception, không heartbeat | Logging có cấu trúc, heartbeat, đóng session sạch, shutdown lưu dữ liệu |
| Reference limitations | Kiến trúc NRO là J2ME port + nhiều đặc thù | Chỉ lấy concept; mapping ở mục 10 |
| Over-engineering | Thiết kế quá xa so với yêu cầu chưa rõ | Bắt đầu tối thiểu; Open Questions phải trả lời trước |
| Scope chưa rõ | Gameplay chưa được định nghĩa | Không implement Combat/Skill/Item trước khi có yêu cầu |

---

## 15. Trạng thái triển khai: lát cắt đầu tiên của server Java (CONFIRMED, 2026-10-07)
Người dùng yêu cầu "bắt đầu" viết server (sau khi cho phép tải Maven và thư viện); đã xây **lát cắt đầu tiên chạy được**, các phần còn lại (đồ, cửa hàng, trận đấu...) **chưa làm**.

**Công cụ (CONFIRMED, người dùng cho phép tải):** Apache Maven 3.9.16 (bản portable, đã kiểm tra SHA-512) trong `BattleBallServer\tools\apache-maven-3.9.16` (không cài vào hệ thống, nằm trong `.gitignore`); thư viện tải từ Maven Central vào `.m2`: Netty 4.2.19, Protobuf 4.36.2 (kèm `protoc` do plugin `protobuf-maven-plugin` tải), MariaDB JDBC 3.5.10, HikariCP 7.1.0, bcrypt (`at.favre`) 0.10.2, SLF4J, JUnit 5. Java 21 (Temurin 21.0.12 có sẵn).

**Cấu trúc:** `BattleBallServer\proto\battleball.proto` (nguồn duy nhất của message, dùng chung với client Unity); `BattleBallServer\server\` (Maven, gói `com.battleball.server`): `Main`, `ServerConfig` (biến môi trường `BB_*`), `net\GameServer` (TCP + TLS 1.2/1.3, pipeline: SslHandler → IdleStateHandler → khung độ dài u32 little-endian → Protobuf `Envelope` → `ConnectionHandler`), `net\ConnectionHandler` (máy trạng thái CONNECTED → HANDSHAKED → AUTHENTICATED), `net\ConnectionLimiter` (tối đa 500 kết nối, vượt thì `DisconnectNotice(SERVER_FULL)`), `auth\*` (`AccountRules`, `PasswordHasher` bcrypt cost 12, `RateLimiter`, `AccountRepository` JDBC tham số hóa, `AuthService`), `session\SessionRegistry`, `db\Database` (HikariCP). Công cụ: `tools\mvn.ps1` (chạy Maven đúng Java), `tools\make_dev_keystore.ps1` (chứng chỉ tự ký cho phát triển), `tools\run_server.ps1` (dựng và chạy; mặc định MariaDB `battleball` của XAMPP, cổng 7443).

**Đã làm (khớp `docs\protocol\BattleBall.md` mục 2, 4.1, 4.2):** `Hello/HelloAck` (kiểm tra phiên bản giao thức, trả `heartbeatIntervalMs` và `tickRateHz`), `Ping/Pong`, `RegisterRequest/Result` (tên 4 đến 20 ký tự a-z0-9_, mật khẩu 8 đến 64 byte, giới hạn 10 lần đăng ký mỗi IP mỗi giờ; **tạo trong một transaction tài khoản + 3 nhân vật (3 chủng tộc, cấp 1) + ví + avatar mặc định + ô skin đã mặc**), `LoginRequest/Result` (mật khẩu hoặc `refresh token`; token chỉ lưu dạng băm SHA-256, hạn trượt 30 ngày; sai mật khẩu và tên không tồn tại cho cùng một lỗi `AUTH_FAILED` và tốn thời gian như nhau; giới hạn 8 lần sai mỗi tên và 30 lần sai mỗi IP trong 10 phút → `AUTH_RATE_LIMITED`; `ACCOUNT_BANNED`, `TOKEN_EXPIRED`, `TOKEN_REVOKED`), **đăng nhập chồng: phiên cũ nhận `DisconnectNotice(LOGGED_IN_ELSEWHERE)` rồi bị đóng**, hết thời gian chờ `Hello` (10 giây) và không hoạt động (60 giây) → `DisconnectNotice(TIMEOUT)`, 3 message sai trạng thái → `PROTOCOL_VIOLATION` rồi đóng, khung quá lớn bị từ chối. **bcrypt và SQL chạy trên luồng riêng, không chạy trên luồng I/O của Netty.** Hash `$2y$` của PHP đọc được (kiểm thử bằng hash thật từ `password_hash` của PHP 8.2).
**Khác thiết kế ghi trong protocol (PROPOSED, cập nhật):** `requestId` nằm trong `Envelope` (một chỗ), không lặp trong từng message; số trường của `oneof` là mã loại message.

**Kiểm chứng (CONFIRMED):** 19 bài kiểm tra tự động (13 đầu cuối qua TLS thật với Protobuf thật và MariaDB 10.4.32 thật cài V001 đến V020 trên cơ sở dữ liệu tạm; 6 bài đơn vị) đều qua: Hello/Ping, sai phiên bản, đăng nhập trước Hello, đăng ký tạo đúng 3 nhân vật + 3 ví + 9 avatar + 3 ô skin, đăng ký sai/trùng, đăng nhập mật khẩu và token (token thô không nằm trong cơ sở dữ liệu), token hết hạn/bị thu hồi/tài khoản bị khóa, hạn token trượt về 30 ngày, đăng nhập chồng, giới hạn thử sai, hết giờ chờ Hello, đạt giới hạn kết nối. Chạy `java -jar` (bản đóng gói 5,5 MB) khởi động và lắng nghe TLS đúng. **Chưa kiểm:** tải lớn (500 kết nối thật), kết nối từ Unity/Android thật, chứng chỉ thật (Let's Encrypt), HTTPS cho admin.

**Một lỗi môi trường Windows đã gặp và đã xử lý:** trên JDK 21 bộ chọn NIO của Netty và của plugin protobuf tự đánh thức bằng socket Unix-domain đặt trong thư mục tạm; khi thư mục tạm là đường dẫn rút gọn kiểu 8.3 (`C:\Users\CUONGH~1\...`, như máy này) thì `connect` lỗi "Unable to establish loopback connection". Cách xử lý: `PlatformFixes.apply()` đặt `jdk.net.unixdomain.tmpdir` về `%USERPROFILE%\.battleball\tmp` trước khi mở bộ chọn đầu tiên (server và bài kiểm tra), và `tools\mvn.ps1` đặt `MAVEN_OPTS` tương tự cho Maven.

**Cách chạy:** (1) `powershell -File tools\make_dev_keystore.ps1` (tự chạy ở lần đầu của `run_server.ps1`); (2) cơ sở dữ liệu `battleball` đã có V001 đến V020; (3) `powershell -File tools\run_server.ps1`; kiểm thử: đặt `BB_TEST_DB_URL` (cơ sở dữ liệu tạm đã chạy V001 đến V020) rồi `powershell -File tools\mvn.ps1 -f server\pom.xml test`.

**OPEN (việc kế tiếp của server):** gửi `PlayerDataSnapshot` sau đăng nhập; nạp dữ liệu tĩnh từ MariaDB (skin, skill, boss, rơi đồ) vào bộ nhớ theo `static_data_version`; kho đồ, cửa hàng, ghép; mở trận và vòng lặp 20 Hz, trả thưởng theo `match_run`; `ResumeSessionRequest` (kết nối lại vào trận); công cụ migration có phiên bản (Flyway) thay cho chạy tay các file V001 đến V020; đăng ký qua web PHP dùng chung đặc tả; chứng chỉ thật và đặt cổng/cấu hình triển khai.

### 15.1 Tiến độ (2026-10-07, người dùng yêu cầu "hoàn thành OPEN sau đó đến bước tiếp theo")
**Đã xong và kiểm thử (37 bài kiểm tra tự động, MariaDB 10.4.32 thật):**
1. **Migration có phiên bản** (`db\MigrationRunner`): chạy các file `V###__*.sql` theo thứ tự, ghi vào bảng `schema_version` (có checksum; sửa file đã chạy bị từ chối); tách câu lệnh đúng với chuỗi/chú thích; **nhận nuôi cơ sở dữ liệu cài tay bằng `BB_BASELINE=<số cuối đã chạy>`**; server tự chạy khi khởi động (tắt bằng `BB_MIGRATE=0`, thư mục bằng `BB_MIGRATIONS_DIR`). Các bài kiểm tra tự tạo cơ sở dữ liệu tạm bằng chính migration (V001 đến V021) rồi xóa.
2. **Nạp dữ liệu tĩnh** (`data\StaticData`, `StaticDataLoader`, `StaticDataStore`): toàn bộ bảng tĩnh (skin, chỉ số, skill, đồ, vật liệu, đồ hỗ trợ, công thức, rơi đồ, đá quý, thành tích, phó bản, boss, chỉ số quái, cân bằng) thành một ảnh chụp bất biến; **tự nạp lại trong 30 giây khi trang admin đổi `static_data_version`** (ảnh chụp đang dùng trong trận không bị đổi giữa chừng).
3. **Dữ liệu người chơi** (`game\PlayerRepository`, `StatCalculator`): sau đăng nhập server đẩy `PlayerDataSnapshot` (3 nhân vật, ví, kỹ năng, trang bị, vật liệu, skin sở hữu, đồ hỗ trợ, bông tai, phó bản đã qua, chỉ số cuối do server tính). **Chỉ số khớp bảng thiết kế** (cấp 1: HP 288.945, ATK 13.065, giáp 11.055; cấp 50: 430.528, 19.467, 16.472; cấp 100: 575.000, 26.000, 22.000), thứ tự tính đúng quyết định (cơ bản + đồ rồi nhân tổng % cộng dồn; chỉ số loại bị tắt trên admin không tính; tỷ lệ chí mạng kẹp 100%).
4. **Kinh tế ngoài trận** (`game\EconomyService`, `PlayerActions`): mua và mặc skin (khóa chủng tộc, giá đá quý), học và nâng skill bằng vàng, mua vật liệu (giá theo cấp nhân vật), ghép mảnh thành đồ, mặc/cởi/bán/hủy đồ (đồ đang mặc không bán được; túi tối đa 2.000 món), dùng đá nâng cấp (+5%, tối đa 75%), mua và mặc đồ hỗ trợ, **ghép sách tuyệt kỹ và nâng sách 1 lên sách 2 (tỉ lệ, con dấu, mất vật liệu khi thất bại)**, mua mảnh bông tai (tối đa 20 mỗi ngày, mốc 00:00 UTC+7) và ghép bông tai (một cái mỗi tài khoản), phân trang kho. Mỗi hành động: một giao dịch, khóa ví trước (chống tiêu trùng), ghi `currency_ledger` và `item_log`; trả `ActionResult` và, khi thành công, đẩy `PlayerDataSnapshot` mới. Mỗi kết nối xử lý yêu cầu lần lượt (`SerialExecutor`).
   Một lỗi thật do kiểm thử phát hiện và đã sửa: trừ vật liệu bằng `INSERT ... ON DUPLICATE KEY UPDATE` với số âm làm cột không dấu báo "out of range".
**Quy ước tôi đặt (PROPOSED, ghi để người dùng duyệt):** giá vàng theo "trận thắng" dùng **cấp nhân vật làm R** (vùng chưa có cấp riêng); nhân vật mới có sẵn **đòn đấm cơ bản của chủng tộc ở cấp 1** (không có thì không đánh được); sách ghép ra đã giám định ngay (một dòng chỉ số, thêm dòng khi dùng con dấu); đá chỉ số, đá pha lê và GEN **chưa có cơ chế** (luật GDD chưa đủ chi tiết).

### 15.2 Trận đấu: mô phỏng 20 Hz, thưởng, kết nối lại (2026-10-07)
**Đã xong và kiểm thử (52 bài kiểm tra tự động; MariaDB 10.4.32 thật, TLS thật):**
- **Bộ máy trận (`game\match\MatchEngine`):** mô phỏng quyền uy server ở 20 Hz (mỗi 50 ms); máy khách chỉ báo vị trí (không kiểm tra, chỉ kẹp trong sân: đã chốt) và xin dùng skill. Thực thể: nhân vật (1 điều khiển + AI đồng đội; hợp thể = 1 nhân vật cộng chỉ số hai người + 1 AI), boss, quái nhỏ, trứng (Đẻ trứng), đạn bay (480 px/s), vùng báo trước.
- **Công thức sát thương đúng GDD 7.4:** `ATK x %skill` → chí mạng `x (1 + sát thương chí mạng)` → dao động ±5% → `x (1 − giảm thương mục tiêu)` → khiên/né → tối thiểu 1; giảm thương nhân vật `Giáp / (Giáp + 50.000)` (trần 80%); Tự phát nổ bỏ qua giáp, boss chịu một nửa. Bài kiểm tra đo trung bình đòn đấm cấp 7 = ATK × 160% × (1 − giáp boss) với sai số trong ±4%.
- **Skill:** hầu hết 25 skill chạy theo dữ liệu (`skill_level`): đòn đấm, Kamejoko/Masenko/Antomic (đạn bay), Kaioken (mất 10% HP), Liên hoàn, Super Kamejoko và Cađíc (gồng 2 s rồi chữ nhật lớn), Makankosappo (nạp 3 s, toàn bộ Ki), Quả cầu kênh khi, Tự phát nổ, Thái Dương Hạ San (choáng), Trị thương (hồi sinh được người chết), Tái tạo năng lượng, Đẻ trứng, Biến hình (chỉ cộng HP), Khiên, Huýt sáo, Dịch chuyển (choáng + đòn kế tiếp chí mạng), Thôi miên, Trói, Biến Sôcôla, Ma phong ba. **Hiệu ứng khống chế lên boss bị rút ngắn (hệ số 0,3, tối đa 3 s: PROPOSED, `match_rules`).**
- **AI đồng đội:** theo thứ tự ưu tiên của dữ liệu (`ai_skill_order`: ô skill từ cao xuống thấp, dùng chiêu đầu tiên đã hồi), không dùng Tự phát nổ, có điều kiện hợp lý (không phí skill), tránh vùng báo trước. **Boss (GDD 7.5):** bộ chiêu theo ưu tiên (tối thượng báo trước 2 s, khiên, choáng diện rộng, tự khóa mục tiêu có nhiều HP nhất và không né được, chưởng, đòn đánh), nhịp 1,3/1,8/2,2 s theo cấp vùng, **enrage dưới 30% HP**, **giới hạn vùng và quay về hồi 5%/giây**. Quái nhỏ (Xên con) sinh theo đợt (2 con mỗi 8 s, tối đa 6 con sống và 20 con mỗi trận).
- **Luồng trận:** các boss lần lượt (boss cuối cuối cùng, mini-boss trước nó, còn lại bốc ngẫu nhiên theo `bosses_per_match`); thắng khi hạ boss cuối; thua khi cả đội chết; hết giờ 30 phút thì hết hạn; **hồi sinh tối đa 2 lần** (0,5 rồi 1,0 trận thắng vàng, trả khi kết thúc).
- **Thưởng khi kết thúc (một giao dịch, `MatchSettlement`):** vàng (60% boss, 40% quái nhỏ, thua nhận một nửa), EXP riêng từng nhân vật (EXP mỗi boss `100·R²/(1,2·B·3)`, hệ số vượt cấp/thấp cấp, kill streak, thắng +20%, lên cấp theo `100·L²`), đồ rơi theo `drop_rule` và hệ số phó bản (mảnh bông tai cộng vào tài khoản), đá quý lần đầu hạ boss cuối và hoàn thành phó bản giới hạn thời gian, thành tích (đạt cấp, tổng trận thắng, hạ boss), điểm giáp tập luyện, nhật ký bất thường (thắng quá nhanh, hạ boss nhanh hơn 10 lần thiết kế). **Không trả thưởng hai lần** (cờ `reward_applied` + `session_token_hash` duy nhất).
- **Mất kết nối:** trận tạm dừng tối đa 30 giây, hai lần mỗi trận (CONFIRMED), máy khách vào lại bằng `ResumeMatchRequest{match_token}` (không đăng nhập lại) và nhận `MatchResumed` đầy đủ; quá hạn hoặc hết lượt tạm dừng thì bỏ trận và trả thưởng theo phần đã làm. Server khởi động lại đánh dấu các trận còn dở là `EXPIRED`.
- **Luồng:** `MatchManager` dùng **nhóm luồng cố định** (số lõi CPU), mỗi trận một tác vụ lặp 50 ms (một luồng phục vụ nhiều trận), thưởng chạy trên luồng DB riêng nên tick không chờ cơ sở dữ liệu (CONFIRMED).
**Giao thức mới (`battleball.proto`):** `StartMatchRequest/MatchStarted`, `MatchFrame` (trạng thái mọi thực thể, sát thương, hiệu ứng, sinh/mất, đạn, ack), `PlayerStateReport`, `UseSkillRequest`, `ReviveRequest`, `LeaveMatchRequest`, `MatchEnded`, `ResumeMatchRequest/MatchResumed`; cộng các thông điệp dữ liệu người chơi và kinh tế ở 15.1.
**PROPOSED (ghi để người dùng duyệt):** kích thước sân 1600×900, tốc độ (người 220, đồng đội 200, boss 150 px/s), bán kính, tầm đòn, cấp quy chiếu theo cấp vùng (sớm 15, giữa 45, cuối 80), hồi giữa hai boss 15% HP, thưởng đá quý chỉ cho nhân vật điều khiển, Biến hình chỉ +HP, Tái tạo năng lượng kéo dài 15 s, vàng khi thua = vàng đã kiếm × 0,5. Tất cả nằm ở `balance_config.match_rules`, chỉnh trên trang admin.
**Chưa làm hoặc chưa kiểm (nói thẳng):** cân bằng thực tế chưa được thử bằng người chơi thật (bài kiểm tra dùng quái yếu để thắng nhanh); thiên hướng AI còn đơn giản; chưa có đá chỉ số và đá pha lê (luật GDD chưa đủ), chưa có hiệu ứng pet khác ngoài buff; chưa kiểm tải 500 kết nối và chưa kiểm với client Unity/Android thật; chiêu riêng của từng boss (ngoài bộ chiêu chung) chưa có; nhóm "Luyện tập" (NPC) đánh như boss thường.

### 15.3 Trang đăng ký web (PHP) và V023 (2026-10-07)
**CONFIRMED (đã chạy và kiểm):** thư mục `BattleBallServer/web/` (`lib.php`, `register.php`, `captcha.php`, `style.css`, `config.sample.php`, `.htaccess`) là cửa đăng ký thứ hai, ghi **đúng các hàng** như `AccountRepository.createAccount` của server Java: 1 tài khoản, 3 nhân vật (EARTH, NAMEC, XAYDA), ví, kỹ năng đấm cơ bản, skin mặc định và skin đang mặc, trong **một giao dịch**.
- Luật tên và mật khẩu giống server: tên `[a-z0-9_]{4,20}`, mật khẩu 8 đến 64 byte; băm bcrypt cost 12 (`$2y$`), server Java xác minh được (đã thử: băm do web tạo được chấp nhận, sai mật khẩu bị từ chối).
- Chống lạm dụng: CSRF, ô bẫy (honeypot), captcha chữ vẽ bằng SVG (PHP ở đây không có GD), giới hạn 10 lần thử mỗi giờ và 5 tài khoản mới mỗi ngày theo IP (bảng `web_register_attempt`, migration `V023`). Sau proxy ngược đặt `BB_TRUST_PROXY=1`.
- Đã kiểm bằng kịch bản: tài khoản hợp lệ, trùng tên, captcha sai, tên và mật khẩu sai, hai mật khẩu khác nhau, honeypot, thiếu CSRF, vượt giới hạn; số hàng tạo ra (3 nhân vật, 3 ví, 3 kỹ năng, 9 skin, 3 skin mặc) khớp cửa đăng ký của server.
- `V023` đã áp vào DB `battleball` (ghi `schema_version` 23 kiểu BASELINE).
**Chưa làm (nói thẳng):** chưa có HTTPS cho trang web (cần khi đưa lên mạng); biến `BB_WEB_TEST_CAPTCHA` chỉ dành cho kiểm thử, không bật trên máy chủ thật; chưa có đặt lại mật khẩu, xác minh email.
**Bước kế tiếp (OPEN):** máy khách Unity kết nối, đăng ký, đăng nhập qua giao thức: cần sinh C# từ `battleball.proto` và thư viện Google.Protobuf (tải NuGet cần người dùng cho phép).

### 15.4 Máy khách Unity: kết nối, đăng ký, đăng nhập (2026-10-07)
**CONFIRMED (đã viết; đã kiểm trong Unity 2022.3 batch mode và bằng .NET 8 với server thật; CHƯA kiểm trên Android):** thư mục `BattleBall/Assets/BattleNet/`:
- `Generated/Battleball.cs`: sinh từ `BattleBallServer/proto/battleball.proto` bằng protoc 36.2 (`--csharp_out`); `Plugins/Google.Protobuf.dll` (NuGet 3.36.2, netstandard2.0, người dùng cho phép tải 2026-10-07; sha256 gói `1182590d...cb4d38dcb`). Khi sửa `.proto` phải sinh lại cả Java (Maven) và C#.
- `Runtime/BbConnection.cs`: TCP + TLS 1.2 (`SslStream`), khung u32 little-endian + `Envelope`, luồng đọc nền, `RequestAsync` ghép theo `request_id`, hàng đợi sự kiện server đẩy, Ping theo `heartbeat_interval_ms`. `AcceptAnyCertificate` chỉ để dev (chứng chỉ tự ký); bản thật phải để false. TLS 1.3 không dùng được trên Mono của Unity 2022 (không có `SslProtocols.Tls13`); server vẫn nhận TLS 1.2.
- `Runtime/BbClient.cs` (đăng ký, đăng nhập mật khẩu hoặc refresh token, giữ `PlayerDataSnapshot`), `BbLoginScreen.cs` (màn IMGUI tạm: đăng nhập, đăng ký, "Continue as" bằng token lưu trong PlayerPrefs), `BbNetSelfTest.cs` (kịch bản kiểm thuần C#), `Editor/BbNetVerification.cs` (menu `BattleBall/Verify Network`; chế độ batch `-executeMethod BattleBall.EditorTools.BbNetVerification.RunBatch`, ghi `Logs/network_verification.txt`), `link.xml` (giữ Google.Protobuf và `BattleBall.Proto` khỏi bị IL2CPP cắt).
- **Kết quả đã chạy** (cùng mã nguồn biên dịch bằng Roslyn của Unity, chạy trên .NET 8, server local TLS thật + MariaDB thật): Hello (protocol 1, 20 Hz), đăng ký tên ngắn bị từ chối, đăng ký đúng, tên trùng bị từ chối, sai mật khẩu bị từ chối, đăng nhập mật khẩu (nhận snapshot 3 nhân vật EARTH/NAMEC/XAYDA), đăng nhập bằng refresh token ở kết nối thứ hai làm kết nối thứ nhất nhận LOGGED_IN_ELSEWHERE.
**Chưa làm hoặc chưa kiểm (nói thẳng):** Unity Editor đang mở nên chưa chạy batch mode (không mở bản Unity thứ hai); cần bấm menu `BattleBall/Verify Network` khi server đang chạy (hoặc đóng Editor để tôi chạy batch). Chưa chạy trên Android (Mono TLS, link.xml, quyền INTERNET trong Player Settings chưa kiểm). Màn đăng nhập chỉ là IMGUI tạm. Chưa có cảnh trận đấu (nhận `MatchFrame`).
**Kết quả Unity batch mode (2026-10-07, Editor đã đóng):** `BbNetVerification.RunBatch` PASSED với đủ 9 bước trên (tài khoản thử tạo trong DB `battleball`). Phát hiện khi chạy: `Google.Protobuf.dll` cần `System.Runtime.CompilerServices.Unsafe.dll`, Unity không tự cung cấp; đã chép bản có sẵn trong Unity (`MonoBleedingEdge/lib/mono/4.5`, không tải thêm) vào `Assets/BattleNet/Plugins`. Mục 'chưa kiểm' còn lại: Android.

### 15.5 Giao diện đăng nhập / đăng ký trong Unity (2026-10-07)
**CONFIRMED (người dùng chọn: dùng ảnh NRO, một màn hai tab Đăng nhập/Đăng ký, scene riêng `Login.unity` làm scene đầu của Build Settings):**
- `Assets/Scenes/Login.unity` do script Editor dựng lại được: menu `BattleBall/Build Login Scene` (`Editor/BbLoginSceneBuilder.cs`, batch `BuildBatch`). Canvas ScreenSpaceCamera, CanvasScaler 1280x720 (match 0,5), chạy được cả màn ngang và dọc.
- Ảnh: khung và nút chép từ `SourceData/ui_audio/ui_menu` vào `Assets/BattleNet/UI/Sprites` (khung `panel_popup.png` ghép từ 8 mảnh `slice_*`; nút vàng/xanh; nền ô nhập `input_bg.png` tự vẽ theo bảng màu NRO). Dùng `UnityEngine.UI.Text` với font dựng sẵn `LegacyRuntime.ttf` (hiển thị đủ dấu tiếng Việt, đã chụp kiểm); chưa dùng TextMeshPro vì dự án chưa import TMP Essentials.
- `Runtime/BbLoginUi.cs`: luật kiểm tra giống server (tên `[a-z0-9_]` 4 đến 20, mật khẩu 8 đến 64 byte, nhập lại mật khẩu khi đăng ký), thông báo lỗi tiếng Việt cho từng mã lỗi của server, trạng thái bận (khóa nút khi đang kết nối), mất kết nối hoặc `LOGGED_IN_ELSEWHERE` thì quay về form. Sau đăng nhập thành công chỉ hiện tên tài khoản và nút Đăng xuất (chưa có màn chờ nhân vật, chưa tự đăng nhập bằng token: người dùng không chọn).
- Địa chỉ server `127.0.0.1:7443` và `acceptAnyCertificate` là trường Inspector của `BbLoginUi` (không có ô nhập trên giao diện). Trên điện thoại 127.0.0.1 là chính điện thoại: phải đổi sang IP máy chạy server.
- `ProjectSettings.asset`: `ForceInternetPermission` đổi 0 sang 1 (Android cần quyền INTERNET cho TCP).
- Thay `BbLoginScreen.cs` (IMGUI tạm) bằng giao diện này.
**Đã kiểm:** Unity batch dựng scene không lỗi; chụp ảnh scene (`Editor/BbLoginSceneCapture.cs`, chạy KHÔNG kèm `-nographics`) ở màn ngang 1280x720 và dọc 720x1280, các trạng thái đăng nhập, đăng ký có lỗi, đã đăng nhập; kiểm 8 trường hợp luật nhập. **Chưa kiểm:** bấm thật trong Play mode với server (luồng mạng dưới giao diện đã kiểm ở mục 15.4), bàn phím ảo và Android thật.

**Cập nhật 15.5 (2026-10-07, người dùng gửi ảnh giao diện NRO và logo `Desktop/data/logogame.png`):** giao diện được làm lại theo bố cục NRO, thay logo bằng logo BattleBall (`Assets/BattleNet/UI/Sprites/logo.png`, thu về 1024x512 từ ảnh gốc 1774x887):
- **Màn tải** (nền đen, logo, thanh tiến trình theo các bước kết nối thật 10/40/70/100% = bắt đầu/TCP/TLS/Hello, nút Hủy rồi Thử lại, `v` + `Application.version` góc trên phải, "Xóa dữ liệu" góc dưới phải). **Menu tài khoản đã lưu** (Chơi TK, Chơi mới, Đổi tài khoản, Máy chủ kèm chấm trạng thái). **Đăng nhập** (hai ô, nút OK và Đăng ký) và **Đăng ký** (thêm ô nhập lại, nút Quay lại). Bỏ hai tab cũ.
- Luồng: mở game, kết nối (màn tải); có tài khoản đã lưu thì vào menu, không thì vào form đăng nhập. Token làm mới (refresh token, server cấp sau đăng nhập mật khẩu) lưu trong PlayerPrefs (`bb.user`, `bb.token`, không mã hóa); "Chơi TK" đăng nhập bằng token, token hết hạn thì xóa và quay về form; "Xóa dữ liệu" xóa token đã lưu. Mất kết nối hoặc đăng nhập nơi khác thì về màn tải và kết nối lại.
- **Nền rừng NRO CHƯA có:** các lớp nền không nằm trong `SourceData`, `NroUnity` hay `Teamobi2026`; tạm dùng nền chuyển màu `bg_gradient_placeholder.png`. Nút cam được đổi màu từ nút vàng NRO; panel và ô nhập tự vẽ theo bảng màu NRO. Cần người dùng cung cấp ảnh nền nếu muốn giống hệt.
- **Đã kiểm:** Unity batch dựng scene không lỗi; ảnh chụp 6 trạng thái (`BattleBall/Logs/login_*.png`: tải, menu, đăng nhập, đăng ký, đăng ký dọc, đã đăng nhập). **Chưa kiểm:** bấm thật trong Play mode với server (luồng mạng đã kiểm ở 15.4, nhưng `ConnectAsync` mới có thêm tham số tiến trình chưa chạy lại bài kiểm mạng), Android thật.

**Font chữ (2026-10-07, người dùng chọn Baloo 2):** toàn bộ chữ của scene `Login.unity` dùng `Assets/BattleNet/UI/Fonts/Baloo2.ttf` (Google Fonts, gấp thành một file biến thiên `Baloo2[wght].ttf`, sha256 `d47a6852...b025c2`, giấy phép SIL OFL 1.1, kèm `OFL.txt` cùng thư mục; người dùng cho phép tải). Unity dùng kiểu mặc định của font biến thiên, chữ "đậm" là đậm giả của Unity. Đã chụp kiểm: đủ dấu tiếng Việt (ă â ê ô ơ ư và dấu thanh) ở màn tải, menu, đăng nhập, đăng ký. Builder tự dùng font mặc định của Unity nếu thiếu file.

**Đổi font lần hai (2026-10-07, người dùng yêu cầu thử font trong `Desktop/data/damage_font`):** cả ba font `barmeneb.ttf`, `chelthm.ttf`, `staccato.ttf` có đủ dấu tiếng Việt (đã kiểm bằng ảnh vẽ thử). Scene `Login.unity` đang dùng `Assets/BattleNet/UI/Fonts/barmeneb.ttf` (hằng `FontPath` trong `Editor/BbLoginSceneBuilder.cs`; đổi hằng rồi chạy lại menu `BattleBall/Build Login Scene` để thử font khác; `Baloo2.ttf` vẫn còn trong thư mục). Nút nhỏ lại (menu 400x58, OK/Đăng ký 200x60, Hủy 170x56, Đăng xuất 220x60), chữ nút cỡ 27 đến 30, màu nâu nhạt (0,91 0,75 0,54) viền nâu đậm mỏng 1 px (viền dày 2 px làm mờ dấu ở cỡ chữ nhỏ). **UNKNOWN: giấy phép của ba font này** (không có tệp giấy phép đi kèm, chưa rõ nguồn); cần xác nhận trước khi phát hành bản thật. Hạn chế thấy khi chụp: dấu móc của "ơ" trong barmeneb nhỏ ở cỡ chữ nút.

**Nền không gian và hành tinh (2026-10-07, người dùng cung cấp `Desktop/data/background.jpg` 1024x338 và `hanhtinh.png` 1774x887):** thay nền chuyển màu tạm bằng nền sao cuộn chậm sang trái; các ô nền nối đuôi nhau, ô chẵn lật gương để mép khớp nhau nên vòng lặp không thấy mối nối. Hai hành tinh trong `hanhtinh.png` được cắt rời (`planet_a.png`, `planet_b.png`, 512x512, trong suốt) và nhân bản thành 4 hành tinh cỡ khác nhau (7% đến 17% chiều cao màn hình), xoay chậm (3 đến 5 độ mỗi giây, hướng xoay xen kẽ) và trôi chéo xuống phải; ra khỏi màn hình thì vào lại từ phía đối diện. Mã: `Runtime/BbSpaceBackground.cs` (kích thước tính theo chiều cao khung nên giống nhau ở màn ngang và dọc; thông số chỉnh trong `Build()`); builder `BbLoginSceneBuilder` gắn vào đối tượng `Background`. Màn tải vẫn nền đen. Đã chụp kiểm hai thời điểm cách 45 giây (vị trí sao và hành tinh khác nhau). Hạn chế: ảnh nền gốc có hai dải tối dọc ở mép (cố hữu của ảnh) và một biểu tượng ngôi sao nhỏ ở góc dưới phải; chưa kiểm hiệu năng trên Android thật (khoảng 8 ảnh nền và 4 hành tinh).

**Cắt ảnh nền (2026-10-07, người dùng yêu cầu bỏ phần có biểu tượng Gemini):** `Assets/BattleNet/UI/Sprites/space_bg.jpg` là bản cắt cột x từ 165 đến 930 của `Desktop/data/background.jpg` (765x338; ảnh gốc ở Desktop không bị sửa). Cách cắt bỏ luôn dải tối bên trái và vùng bên phải chứa biểu tượng ngôi sao; hai mép cắt nằm ở cột gần đen nên mối nối lật gương ít lộ. Đã kiểm điểm sáng nhất trong 80 cột cuối chỉ còn 9/255 và chụp lại cảnh.

**Chỉnh lại cách cắt (2026-10-07, người dùng: "chỉ cắt phần bên phải thôi"):** `space_bg.jpg` nay là cột x từ 0 đến 930 của ảnh gốc (930x338): giữ nguyên phần bên trái, chỉ bỏ phần bên phải chứa biểu tượng Gemini. Thay cho bản cắt cả hai bên ở đoạn trên.

### 15.6 Tài nguyên tải từ server (2026-10-07, người dùng duyệt đề xuất "tải từ server, cùng máy với web PHP")
**CONFIRMED (đã làm và kiểm):** giai đoạn 1 của tài nguyên tải từ server, với nhóm đầu tiên là sprite trang phục (`docs/protocol/BattleBall.md` mục 14.1).
- **Phân phối:** file tĩnh qua HTTP, không đi qua kênh game, cùng thư mục `BattleBallServer/web` với trang đăng ký PHP: `web/assets/manifest.json` và `web/assets/packs/*.zip`. Chạy thử: `tools/run_web.ps1` (PHP built-in server, cổng 8081; cổng 8080 đang dành cho trang admin). Bản thật: Apache hoặc nginx qua HTTPS, cùng thư mục `web`.
- **Công cụ đóng gói:** `tools/build_assets.py` (Python chuẩn, không cần thêm gói). Đọc `PartDatabase.json`, `CostumeCatalog.json`, `image.bytes` và `SourceData`; tìm 5.123 icon mà trang phục cần (91 cắt từ atlas Big0..Big4, 5.032 file icon rời); ghi `common.zip` (bảng `update_data/image`), và với mỗi thang x2, x3, x4: `xN_atlas.zip` cùng các gói `xN_icons_###.zip` (400 file mỗi gói). Zip ghi cố định thứ tự và dấu thời gian, nên gói không đổi thì sha256 không đổi (chạy lại đúng 2 lần cho cùng `version`). Dung lượng tải: x2 15,9 MB; x3 28,0 MB; x4 38,5 MB. Kết quả sinh ra không đưa vào git (`web/.gitignore`).
- **Thang x1 CHƯA phát hành:** `SourceData` không có atlas x1 và thiếu 249 icon rời ở x1; thang nhỏ nhất là x2 (lệch so với ghi chú "x1 đến x4" trước đó, PROPOSED).
- **Giao thức:** `HelloAck` thêm `asset_base_url` (5) và `asset_version` (6); server đọc `version` từ file manifest (`BB_ASSET_MANIFEST`, tự đọc lại khi file đổi, không cần khởi động lại) và địa chỉ từ `BB_ASSET_BASE_URL`. Cả hai rỗng nghĩa là server không phát hành tài nguyên. `tools/run_server.ps1` đặt mặc định `http://127.0.0.1:8081/assets/` và `web/assets/manifest.json`. Số phiên bản giao thức vẫn là 1 (chỉ thêm trường).
- **Máy khách (`Assets/BattleNet/Runtime/BbAssetUpdater.cs`):** nếu phiên bản và thang đã cài trùng với server thì không gửi yêu cầu nào; nếu không thì tải `manifest.json`, chỉ tải các gói của thang này (và gói thang 0) bị thiếu hoặc đổi, kiểm sha256, giải nén vào `Application.persistentDataPath/bbassets` (cùng cấu trúc thư mục với `SourceData`), ghi `installed.json` sau từng gói (ngắt giữa chừng thì lần sau tải tiếp phần còn lại). Chặn zip-slip (tên có `..`, đường dẫn tuyệt đối, ổ đĩa, dấu `\`). Thang chọn một lần theo độ phân giải và RAM (`ChooseScale`: x4 nếu cạnh ngắn từ 1080 và RAM từ 3,5 GB; x3 nếu từ 720 và 2,5 GB; còn lại x2) rồi lưu trong PlayerPrefs; đổi thang sau này vẫn OPEN. Màn tải: 20% đầu của thanh là kết nối, 80% sau là tải tài nguyên (hiện số MB), nút Hủy hủy cả việc tải, lỗi thì hiện "Thử lại".
- **`NroSpriteResolver`:** `GetDefaultServerRoot()` dùng `SourceData` nếu có (Editor), nếu không thì dùng thư mục tài nguyên đã tải (trên thiết bị); sửa một hàm này, phần còn lại của bộ giải sprite giữ nguyên.
- **Project:** `ProjectSettings.asset` `insecureHttpOption` từ 0 sang 1 (cho phép HTTP khi dev); **bản phát hành phải dùng HTTPS và đặt lại 0**.
**Đã kiểm:** Maven 53 bài kiểm tra đạt (có test mới về đọc phiên bản manifest) với MariaDB thật; Unity batch `BbAssetVerification` đạt 15 bước (tải lần đầu 15 gói 15,2 MB, tiến trình đạt 100%, cùng phiên bản thì không gửi yêu cầu, một gói hỏng thì chỉ tải lại gói đó, server không với tới được thì báo lỗi, các trường hợp zip-slip, bộ giải sprite tìm đủ 91 sprite atlas và 5.031 icon rời từ thư mục tải với 0 thiếu); `BbNetVerification` đạt, gồm kiểm `HelloAck` có địa chỉ và phiên bản tài nguyên.
**Chưa làm hoặc chưa kiểm (nói thẳng):** chưa chạy thử trên Android thật (HTTPS qua UnityWebRequest, quyền ghi, tốc độ giải nén); chưa tải qua mạng thật hay HTTPS; các nhóm tài nguyên còn lại (hiệu ứng, âm thanh, icon vật phẩm, ảnh quái, JSON bảng dữ liệu) vẫn đóng trong dự án hoặc chưa có; không xóa file của gói đã bị gỡ khỏi manifest; tải tuần tự từng gói (chưa song song); `CostumeCatalog.json` và `PartDatabase.json` vẫn là TextAsset trong dự án (chưa tải từ server); logo, nền, hành tinh, font của màn đăng nhập vẫn nằm trong APK (cần có trước khi kết nối). Mật mã xác thực manifest: chỉ dựa vào HTTPS ở bản thật (manifest chưa ký).

### 15.7 Cấu hình máy khách, tên máy chủ và build Android (2026-10-07, người dùng chọn mục 1 và 2 sau khi so với NroUnity và Config.properties)
**CONFIRMED (đã làm và kiểm):**
- **`BbConfig` (mục 2):** địa chỉ game server nằm ở một asset duy nhất `Assets/BattleNet/Resources/BbConfig.asset` (lớp `Runtime/BbConfig.cs`; menu `BattleBall/Create Config`): `gameHost`, `gamePort`, `serverName`, `acceptAnyCertificate`. Thay các trường lẻ trước đây trong `BbLoginUi`. `acceptAnyCertificate` chỉ có hiệu lực ở Editor và Development Build (`Debug.isDebugBuild`); bản phát hành luôn kiểm chứng chỉ. Trên điện thoại `127.0.0.1` là chính điện thoại: phải đổi `gameHost` sang IP máy chạy server. Đã bỏ cách ghi cứng kiểu `Management.IpServer` của NroUnity và không lấy cơ chế hai scene.
- **Tên máy chủ do server gửi:** `HelloAck.server_name` (trường 7), server đọc từ biến `BB_SERVER_NAME` (mặc định `BattleBall`); menu hiện "Máy chủ: <tên>", `BbConfig.serverName` chỉ là giá trị dự phòng. Maven 53 bài kiểm tra đạt (có kiểm tên trong HelloAck); `BbNetVerification` đạt.
- **Android (mục 1):** scripting backend IL2CPP, kiến trúc ARM64 và ARMv7 (menu `BattleBall/Apply Android Settings`, ghi vào `ProjectSettings.asset`). Build thử bằng `BbBuild.BuildAndroidBatch` (menu `BattleBall/Build Android (development APK)`): **Succeeded sau 8,5 phút, 0 lỗi, 3 cảnh báo**, ra `BattleBall/Builds/Android/BattleBall-dev.apk` (khoảng 52 MB trên đĩa; báo cáo của Unity ghi 584,5 MB là tổng kích thước đầu ra gồm các tệp trung gian), 3 scene (Login, SampleScene, CostumeTester). Đây là bản Development (để chấp nhận chứng chỉ tự ký của server dev).
**Chưa kiểm (nói thẳng):** chưa cài và chạy APK trên điện thoại hay giả lập (chưa biết Google.Protobuf, TLS của Mono và `UnityWebRequest` chạy đúng dưới IL2CPP trên máy thật); bản phát hành (không Development, AAB, ký) chưa build; mã gói ứng dụng và tên công ty vẫn là mặc định `com.DefaultCompany.BattleBall` (cần người dùng chọn tên thật trước khi phát hành); target SDK vẫn để tự động; `Application.targetFrameRate` chưa đặt; giới hạn kết nối theo IP và hệ số EXP sự kiện (mục 3 của đề xuất) chưa làm.

### 15.8 Menu chính của máy khách (2026-10-07, người dùng gửi ảnh bảng Hành trang của NRO: "đây là menu tôi muốn")
**CONFIRMED (đã dựng và chụp kiểm bằng dữ liệu giả; CHƯA kiểm với server thật trong Play mode):**
- **Luồng:** đăng nhập xong, `BbLoginUi` đợi `PlayerDataSnapshot`, giao kết nối cho `BbSession` (`Runtime/BbSession.cs`, giữ kết nối khi đổi scene, tự về scene Login khi mất kết nối hoặc đăng nhập nơi khác và nói lý do) rồi mở scene `Assets/Scenes/Menu.unity` (thứ tự build: Login, Menu, các scene cũ). Bỏ panel "đã đăng nhập" tạm. Mở Menu trực tiếp mà chưa đăng nhập thì quay về Login.
- **Bố cục theo bảng NRO (`BbMenuUi`, dựng bởi `BbLoginSceneBuilder.BuildMenu`):** bảng bên trái rộng 440 (độ phân giải tham chiếu 1280x720, cao hết màn hình): chân dung (icon của trang phục) và 4 dòng số liệu vàng (HP, KI, Sức đánh, Giáp và Chí mạng), thanh EXP kèm "Cấp N EXP a / b", hàng 3 tab một tab một nhân vật theo thứ tự người dùng nêu **Trái Đất, Xayda, Namếc** (tab đang chọn màu xanh, còn lại viền cam), nhân vật giữa các ô trang bị (trái: Áo, Quần, Găng, Giày, Nhẫn = ARMOR, PANTS, GLOVES, BOOTS, RING; phải: 4 ô "Hỗ trợ" = SUPPORT_1 đến SUPPORT_4), "Hành trang: n / 2.000" cùng lưới 2x5 ô, dòng tiền (vàng và đá quý). Bên phải (nền không gian có hành tinh): nút VÀO TRẬN, Kỹ năng, Cửa hàng, Trang phục (chưa nối, bấm hiện "Sắp có") và Đăng xuất cùng tên tài khoản. Chuyển tab: bấm tab, vuốt ngang, hoặc phím trái phải; tab cuối cùng nhớ trong PlayerPrefs.
- **Nhân vật:** `Runtime/BbCharacterView.cs` vẽ trang phục đang mặc (slot SKIN) bằng 3 ảnh UI (đầu, chân, thân) theo đúng công thức vị trí của `NroMannequin.PlaceSlot`, tư thế đứng thở xen kẽ 0,6 giây; không dùng camera hay render texture. Avatar chỉ có đầu mặc thân và chân mặc định theo hành tinh (bộ phận 157/158, 149/150, 151/152 của `CreateCharScr.defaultBody/defaultLeg` của NRO).
- **Dữ liệu bổ sung:** `PartDatabase.json` thiếu 6 bộ phận thân và chân mặc định này nên đã thêm từ `SourceData/tables/json/part.json` (575 bộ phận); `build_assets.py` chạy lại (phiên bản tài nguyên đổi, server tự báo phiên bản mới). Server: `CharacterInfo.exp_to_next` (trường 16) tính theo luật EXP (hệ số x cấp lũy thừa), máy khách không tự tính; Maven 53 bài kiểm tra đạt.
- **Thử nghiệm kết xuất:** thử vẽ nhân vật bằng camera và RenderTexture rồi bỏ: sprite trong prefab `NroMannequin` dùng shader Lit của 2D renderer và không hiện khi vẽ tay bằng camera ở chế độ Edit; cách vẽ bằng UI không dính vấn đề này.
**Chưa làm hoặc chưa kiểm (nói thẳng):** chưa kiểm trong Play mode với server thật và trên Android; ô trang bị, ô hỗ trợ và lưới túi chưa vẽ hình vật phẩm (máy khách chưa có bảng vật phẩm, hiện chỉ đổi màu ô khi có đồ); chân dung dùng icon của trang phục; nhãn "Hỗ trợ 1 đến 4" chưa gắn tên loại (pet, giáp luyện tập, cờ/cánh/vũ khí, sách kỹ năng: chưa rõ ô nào chứa loại nào); màn hình menu chỉ thiết kế cho chiều ngang (cần khóa hướng ngang cho Android, chưa làm); các nút VÀO TRẬN, Kỹ năng, Cửa hàng, Trang phục chưa nối; chưa có nút X như NRO (đăng xuất ở góc trên phải).

**Cập nhật 15.8 (2026-10-07, người dùng: nhỏ nhân vật 1/3, thêm bông tai gộp 2 tab thành 1, dòng thuộc tính tổng hợp dưới nhân vật):**
- **Nhân vật nhỏ lại một phần ba:** `BbCharacterView.uiScale` từ 6 xuống 4 (UI pixel mỗi pixel NRO), chân đặt cao hơn để chừa chỗ cho khung thuộc tính bên dưới.
- **Khung thuộc tính tổng hợp dưới nhân vật** (khối nâu trong suốt, chữ vàng, như khối của bảng NRO): 4 dòng "HP: a%, KI: b%", "SĐ: c%, SĐCM: d%" (SĐCM = sát thương chí mạng), "Hút HP: e%, Hút KI: f%", "Giáp: g%, Né: h%". Các dòng NRO "Kháng choáng" và "Chống lạnh" không có trong BattleBall nên không hiện. Số liệu là tổng các % cộng thêm của trang bị, skin và đồ hỗ trợ do server tính: giao thức `CharacterInfo.bonuses` (trường 17, danh sách `StatLine` nguồn `TOTAL`, lấy từ bảng tổng của `StatCalculator`, chỉ các loại chỉ số đang bật); các khóa dùng: HP_PCT, KI_PCT, ATK_PCT, CRIT_DMG_PCT, LIFESTEAL_PCT, KI_STEAL_PCT, ARMOR_PCT, DODGE_PCT. Maven 53 bài kiểm tra đạt.
- **Bông tai Potara (hợp thể) trong menu (PROPOSED, theo luật đã chốt trong GDD):** nút "Bông tai" ở bên phải (dòng chữ bên dưới: "Đã có Bông tai Potara" hoặc "Mảnh bông tai: n / 100"). Bấm: cần có bông tai (`PotaraInfo.owned`), nhân vật ở tab đang mở là nhân vật chủ và cần cấp từ 50, đối tác cũng từ 50 (hằng `FusionMinLevel` = 50 trong `BbMenuUi`, lấy từ GDD; chưa đọc từ dữ liệu tĩnh). Một đối tác hợp lệ thì hợp thể ngay; hai đối tác hợp lệ thì hiện hộp chọn. Hai tab gộp thành một tab "Trái Đất + Xayda" (đặt ở vị trí của tab đứng trước) và nút đổi thành "Tách hợp thể". Tab hợp thể hiện tổng chỉ số của cả hai nhân vật (HP, KI, Sức đánh, Giáp, và Chí mạng cộng nhưng tối đa 100%) và tổng các % cộng thêm; trang phục, ô trang bị, tiền, EXP lấy theo nhân vật chủ ("Chủ cấp a + Phụ cấp b"). Cặp được giữ trong menu (`FusionMainCharacterId`, `FusionSubCharacterId`) để gửi làm đội hình khi nút VÀO TRẬN được nối; chưa lưu giữa các lần mở game.
**Chưa làm hoặc chưa kiểm:** kiểu hiển thị nhân vật hợp thể (đang hiện nhân vật chủ; trang phục hợp thể riêng của NRO chưa xử lý), hiện thông tin hai bộ trang bị của cặp hợp thể (đang hiện của nhân vật chủ), kiểm Play mode với server thật.

**Chọn thang sprite trong BbConfig (2026-10-07, người dùng: thử x4 vì vẫn thấy rõ pixel):** `BbConfig.assetScale` (2, 3 hoặc 4; 0 = tự chọn một lần theo độ phân giải và RAM của máy như trước) đang đặt là 4. `BbAssetCache.Scale` đọc giá trị này trước. Kiểm bằng ảnh chụp menu: x4 mịn hơn rõ rệt (các thang x2, x3, x4 của NRO là ảnh vẽ chi tiết hơn, không chỉ phóng to điểm ảnh). Đánh đổi: tải 38,7 MB thay vì 16,1 MB cho x2 (x3 là 28,2 MB); đổi thang thì lần chạy sau máy khách tải các gói của thang mới. Sprite vẫn lọc điểm (Point) như NRO; nếu muốn mềm hơn nữa có thể đổi sang lọc tuyến tính (chưa làm).

**Chỉnh lần nữa (2026-10-07, người dùng: thử x3 và nhỏ nhân vật còn một nửa):** `BbConfig.assetScale` = 3; `BbCharacterView.uiScale` từ 4 xuống 2 (nhân vật cao khoảng 70 điểm ảnh UI trên màn hình tham chiếu 1280x720), đặt giữa vùng phía trên khung thuộc tính. Vì sprite x3 vẽ nhỏ hơn kích thước ảnh (2/3), `BbCharacterView` đặt lọc tuyến tính cho ảnh đang thu nhỏ để không nhấp nháy, và lọc điểm khi vẽ 1:1 hoặc lớn hơn; ảnh này dùng chung với bộ giải sprite nên scene thử trang phục cũng chịu lọc đó khi chạy cùng lúc (chưa kiểm).

**Bông tai chuyển vào hành trang (2026-10-07, người dùng):** nút "Bông tai" bên phải được bỏ; bông tai là ô đầu tiên của lưới hành trang (**ĐÃ SỬA: bông tai là #1819, icon 15614; mảnh là #1820, icon 15613**, V025, mục 10.35; số 454 và icon 3896 là bản cũ của V024). Bấm ô đó để hợp thể (hoặc "Tách" khi đang hợp thể); khi chưa đủ 100 mảnh ô mờ và hiện số mảnh. Cấp tối thiểu và số mảnh do server gửi (`PotaraInfo.min_fusion_level`, `shards_needed`).


### 15.9 Kho chung, popup cải trang, tab Chức năng, làng tân thủ và cần điều khiển (2026-10-08, người dùng yêu cầu)

**Kho chung (CONFIRMED):** ba nhân vật dùng một kho (ví, nguyên liệu, skin, đồ trong túi, đồ hỗ trợ); V026; `docs/database/BattleBall.md` 10.36. Hành trang trên màn hình là một cho mọi tab: ô đầu là bông tai (hoặc mảnh), các ô sau là skin của tài khoản (9 ô một trang, mũi tên đổi trang); skin của chủng tộc khác bị làm mờ ở tab đang mở.

**Popup cải trang (CONFIRMED):** bấm một skin mở popup theo mẫu popup vật phẩm của NRO (hình và tên, dòng chỉ số xanh, đường kẻ, mô tả, nút). Nút "Sử dụng" cho nhân vật của tab đang mở và "Dùng cho <hành tinh>" cho các nhân vật khác được phép mặc (đề xuất đối tượng sử dụng). Skin hợp thể chỉ bấm được khi nhân vật đó đang là chủ của hợp thể. Lệnh gửi `EquipSkinRequest`; kết quả về qua snapshot mới.

**Tab "Chức năng" (CONFIRMED):** tab thứ tư (luôn ở cuối): Hòm thư (huy hiệu số thư chưa đọc, danh sách cuộn, bấm thư mở popup có hình quà và nút Nhận quà hoặc Xóa thư, nút "Nhận tất cả"), Thông báo, Tài khoản (Đổi mật khẩu, Đăng xuất). Mã: `BbMenuFunctions.cs`, `BbPopup.cs`. Nút Đăng xuất và tên tài khoản ở góc trên phải đã bỏ (nay nằm trong Tài khoản).

**Làng tân thủ (CONFIRMED yêu cầu; nội dung map là PLACEHOLDER):** phần bên phải menu là làng (`BbVillageView`): nhân vật chủ (nhân vật của tab đang mở) đứng cạnh hai nhân vật còn lại do AI điều khiển (đi theo sau chủ, nhảy theo), có ba NPC (Ông Trưởng làng, Bà Hạt Mít, Cổng phó bản). **Chưa có chunk map thật nên mặc định "có chunk map" và dùng bản vẽ tạm** (`BbPlaceholderMaps`: trời, đồi, mây, nhà, cây, hàng rào; hình NPC lấy tạm từ costume 430 đến 432). Để thay bằng chunk thật: gán `BbMaps.Provider` cho một `IBbMapProvider` đọc chunk (id bản đồ `village_start`). Bấm NPC hiện popup lời thoại; "Cổng phó bản" chưa chọn được phó bản.
- **Các nút chức năng bên phải đã bỏ theo yêu cầu** (VÀO TRẬN, Kỹ năng, Cửa hàng, Trang phục). Hệ quả (OPEN): chưa còn đường vào trận trên giao diện; dự kiến vào qua NPC "Cổng phó bản", cần làm màn chọn phó bản. Kỹ năng và Cửa hàng cũng chờ NPC hoặc mục menu mới.
- **Menu thu vào được:** mũi tên ở mép bảng thu bảng sang trái hoặc mở ra (nhớ lựa chọn); khi mở, làng căn giữa phần còn trống.
- **Cần điều khiển** (`BbJoystick`, hình cắt từ `joystick.png`: vòng đế và viên tròn trắng; còn thừa nút cầu cam và nút mũi tên chưa dùng): kéo trái phải để đi, kéo lên để nhảy; trên máy tính dùng phím mũi tên hoặc WASD. Phím mũi tên không còn đổi tab (vuốt trên bảng vẫn đổi tab).
- Chưa kiểm trong Play mode với server thật: vòng cập nhật đã chạy mô phỏng bằng `BbVillageView.Tick` trong công cụ chụp ảnh (`Logs/menu_village_*.png`); chưa thử cảm ứng thật và chưa thử trên Android.

**Công cụ:** `BattleBallServer/start_server.bat` bật MariaDB, web tài nguyên (8081) và game server (7443) và ghi `logs/server.log`; `tools/build_assets.py` thêm icon 11569, 11572, 15412 (nguyên liệu #301 đến #303); trang admin có "Hòm thư" và "Thông báo" (`admin/mail_pages.php`).

**Cập nhật 2026-10-08 (hợp thể, cần điều khiển):** (1) menu có năm tab khi đang hợp thể: Trái Đất, Xayda, Namếc, **Hợp thể** (tab mới, tổng chỉ số, hình thái hợp thể mặc cải trang hợp thể), Chức năng; xem `docs/database/BattleBall.md` 10.38. Ở làng, tab Hợp thể hiện hình thái hợp thể (nhân vật phụ nhập vào chủ) cạnh AI còn lại. (2) Cần điều khiển dùng hình mới `joystickv2.png`: nửa phải là khung (`joy_base`), nửa trái là điểm chạm (`joy_knob`); cần điều khiển **đặt cố định ở góc dưới trái, nằm sau menu khi menu mở** và lộ ra khi menu thu vào (không còn dịch theo bảng). (3) Tab nhỏ lại (80 px) vì có năm tab.

**Cập nhật 2026-10-08 (hình đầu nhiều khung, cấu hình độ phân giải; người dùng yêu cầu):**
- **Đầu nhiều khung (CONFIRMED):** `BbCharacterView` chưa kế thừa hết `NroMannequin.prefab`: thiếu `headCycle` (đầu đổi khung như cải trang hợp thể #1816 có 3 khung 1866, 1867, 1868 với tia sáng). Nay vẽ luân phiên các part đầu như `NroMannequin` (25 tick/giây, đổi khung mỗi 4 tick = 0,16 giây); 28 cải trang trong danh mục có đầu nhiều khung. Tên trên đầu nhân vật đặt theo chiều cao của từng cải trang (`hpBarOffsetY`, dạng hợp thể cao hơn). Chưa dùng: hào quang (`aura`, không có cải trang nào gán) và cờ `isMonkey/isFusion` (chỉ ảnh hưởng chiều cao va chạm, chưa cần). Ảnh: `Logs/menu_fused.png`, `menu_fused_head2.png`, `menu_fused_head3.png`.
- **Cấu hình độ phân giải x1 đến x4 (CONFIRMED):** tab Chức năng có nút "Cấu hình" với bốn nút x1, x2, x3, x4. Chọn sẽ lưu lựa chọn (ưu tiên hơn `BbConfig.assetScale` và lựa chọn tự động), tải thêm gói của độ phân giải đó từ máy chủ nếu thiếu (hiện tiến độ MB), rồi vẽ lại mọi hình của menu và làng. Nếu tải lỗi thì giữ độ phân giải cũ. `tools/build_assets.py` nay xuất cả x1 (5,0 MB gồm gói chung): `x1_atlas_from_x2` (atlas x2 mà `NroSpriteResolver` thu nhỏ còn x1), `x1_icons_###` (icon rời x1 có sẵn) và `x1_icons_fallback` (bản x2 của 250 icon rời không có file x1). Kiểm chứng trên server thật: `BbAssetVerification` bước 7 tải x1 và giải được 91 sprite atlas + 5.118 icon rời, 0 thiếu. Chưa thử đổi độ phân giải trong Play mode và trên Android.

**Cập nhật 2026-10-08 (tốc độ khung đầu x2, người dùng yêu cầu "tăng tốc độ frame lên x2"):** `BbCharacterView.headFrameSpeed = 2` nhân đôi tốc độ đổi khung đầu của cải trang đầu nhiều khung (mỗi khung 0,08 giây thay vì 0,16 giây). Hệ số chỉnh được trên component. Tốc độ thở và chạy chưa đổi (UNKNOWN người dùng có muốn nhân đôi cả hai không). Chưa biên dịch và chưa chụp lại ảnh vì Unity Editor của người dùng đang mở.

**Cập nhật 2026-10-08 (tốc độ khung của nhân vật, khung của AI, trạng thái bay; người dùng yêu cầu):** (1) khung đứng và chạy của nhân vật chủ nhanh gấp đôi (`BbCharacterView.animSpeed` = 2); (2) khung chạy của AI nay chạy theo tốc độ đi thật: đi theo chậm thì bước chậm (tối thiểu 0,7), bắt kịp nhân vật chủ đang chạy thì nhanh bằng chủ (tối đa 2) — trước đây AI chạy hết tốc độ khung dù đi chậm (INFERRED: đây là ý "khung của AI khi di chuyển bình thường khá nhanh"); (3) thêm trạng thái bay (tư thế `Fly_Horizontal` của bảng NRO): kéo cần lên để nhảy, giữ lên trên không 0,3 giây thì chuyển sang bay (cần điều khiển quyết định độ cao: lên thì bay lên, xuống thì hạ, giữa thì lơ lửng; đi ngang nhanh hơn 1,25 lần; trần 380 px; chạm đất thì hết bay); AI bay theo nhân vật chủ thấp hơn một chút và hạ cánh khi chủ hạ cánh. Ảnh: `Logs/menu_village_fly.png`.

**Cập nhật 2026-10-08 (sửa lần hai, người dùng yêu cầu):** (1) khung chạy của AI vẫn nhanh nên giảm còn tối đa 0,55 lần tốc độ gốc (một khung mỗi 0,15 giây khi AI chạy hết tốc độ, chậm dần khi đi chậm, tối thiểu 0,3); (2) **tốc độ chạy của nhân vật điều khiển x2** (300 lên 600 px/giây; AI đuổi theo với tốc độ gấp 1,15 lần); (3) **bay tự hạ cánh:** buông cần điều khiển (không còn chạm) khi đang bay thì nhân vật tự hạ 240 px/giây rồi chạm đất, AI hạ theo; vẫn lơ lửng khi còn đẩy ngang mà cần dọc ở giữa; (4) **dao động khi bay như IDLE:** tư thế bay chỉ có một khung trong bảng NRO nên đầu và thân nhích lên 1 pixel NRO xen kẽ cùng nhịp thở như đứng yên. Ảnh: `Logs/menu_village_fly.png`, `menu_village_sinking.png`, `menu_village_landed.png`.

**Cập nhật 2026-10-08 (khung chạy của AI giảm một nửa, người dùng yêu cầu):** `AiRunAnimSpeed` 0,55 xuống 0,275 (một khung mỗi 0,29 giây khi AI chạy hết tốc độ; tối thiểu bằng 0,55 lần mức đó khi đi chậm).

**Cập nhật 2026-10-08 (khung của AI so với nhân vật điều khiển):** theo mã, hai bên **không bằng nhau**: nhân vật điều khiển `animSpeed` = 2 (một khung chạy mỗi 0,04 giây), AI tối đa 0,275 (0,29 giây). Nguyên nhân khung AI vẫn trông nhanh (INFERRED): AI đuổi kịp chủ đang chạy nên chênh lệch vị trí dao động quanh ngưỡng 10 px, trạng thái đảo qua lại giữa chạy và đứng mỗi vài khung và mỗi lần đảo lại bắt đầu lại từ khung chạy đầu. Đã thêm vùng chết (bắt đầu đi khi lệch hơn 24 px, dừng khi dưới 6 px) và AI đứng thở với tốc độ gốc (1) thay vì 2.

**Cập nhật 2026-10-08 (AI chỉ có một động tác nhấc chân mỗi lần di chuyển, người dùng báo):** nguyên nhân (INFERRED từ mã): tốc độ khung của AI tính theo quãng đường mỗi khung nên khi bám theo chủ (bước rất ngắn) rơi xuống mức tối thiểu, một khung mất tới 0,5 giây, và AI hay chạy rất ngắn rồi dừng nên chỉ thấy một tư thế. Đã đổi: khung chạy của AI cố định `AiRunAnimSpeed` = 0,4 (một khung mỗi 0,2 giây), AI chạy tối thiểu 0,6 giây mỗi lần bắt đầu chạy (`MinRunTime`, thấy ít nhất 3 tư thế), và chu kỳ khung chạy tiếp tục từ chỗ dừng thay vì luôn bắt đầu từ khung đầu.

**Cập nhật 2026-10-08 (hạ cánh dùng Fall_Down, người dùng yêu cầu):** khi nhân vật đang rời mặt đất mà đi xuống (bay hạ xuống vì kéo cần xuống hoặc buông cần, hoặc rơi sau cú nhảy) thì dùng tư thế `Fall_Down` của bảng NRO (`BbCharacterView.PoseState.Fall`); lơ lửng hoặc bay lên vẫn là `Fly_Horizontal`, nhảy lên là `Jump_Up`. AI hạ theo cũng dùng `Fall_Down`.

**Sửa kèm (2026-10-08):** AI bay theo chủ ở thấp hơn nên chạm đất trước; trước đây AI vẫn giữ tư thế bay nằm ngang sát đất cho đến khi chủ hạ cánh. Nay AI xuống tới mặt đất thì đứng luôn (không còn cờ bay), dù chủ còn đang hạ. Ảnh: `Logs/menu_village_sinking.png` (chủ ở tư thế `Fall_Down`, hai AI đã đứng).

**Cập nhật 2026-10-08 (hạ cánh nhanh gấp đôi, người dùng yêu cầu):** tự hạ khi buông cần 240 lên 480 px/giây (`SinkSpeed`); kéo cần xuống khi bay cũng nhanh gấp đôi (`DescentBoost` = 2); AI hạ theo với tốc độ gấp 1,2 lần tốc độ hạ của chủ.

### 15.10 Chunk map thật cho làng tân thủ (2026-10-08, người dùng gửi `map_5.png`, `map_5.json` và ảnh cây nhà `item_bg_temp/x4/0.png` đến `16.png`)

- **CONFIRMED yêu cầu, thay bản vẽ tạm của 15.9:** làng tân thủ nay dùng **ảnh map 5 của NRO** (`map_5.png`: 72x25 ô 96 px ở zoom x4, tileset 3) làm chunk, **nối 5 bản liên tiếp** (bản thứ 2 và thứ 4 lật gương; hai đầu ảnh đều là nền cao cùng độ cao nên khớp liền, ghép không lặp y hệt), cùng cây, nhà, đá, cỏ lấy từ `item_bg_temp` (ảnh 0 đến 8 và 10 đến 16; ảnh 9 là đảo nổi, không dùng). Tổng cộng 8.400 pixel NRO ngang (khoảng 18.500 pixel màn hình, đi hết mất khoảng 30 giây ở tốc độ chạy).
- **Công cụ:** `BattleBallServer/tools/build_village_map.py` cắt hai cột ô đen ở rìa và vùng trời trống phía trên (còn 1680x360 pixel x1), xuất ảnh nền hai nửa mỗi thang (`ground_a_x1..x4.png`, `ground_b_x1..x4.png`; hai nửa để mỗi ảnh không vượt 4096 pixel trên máy yếu), đo **độ cao mặt đất theo từng cột ảnh** (lấy chỗ đặc liền ít nhất 40 pixel nên bụi cỏ không tính, làm phẳng các nền, làm mượt mép), đặt cây nhà lên các nền phẳng (bản nào cũng khác nhau, bản đầu và bản cuối chắc chắn có nhà) và 3 NPC; ghi vào `BattleBall/SourceData/data/map/village_start/` (`chunk.json`, ảnh nền, `bg/x1..x4`). `tools/build_assets.py` đóng gói: `chunk.json` vào gói chung, ảnh theo thang vào `map_village_x1..x4` (0,2 đến 1,8 MB mỗi thang). Chạy lại hai script khi đổi map.
- **Phía máy khách:** `BbChunkMaps` (nay là `BbMaps.Provider` mặc định, bản vẽ tạm là dự phòng khi thiếu file) đọc `chunk.json` và ảnh của thang đang dùng; `BbMapChunk` có địa hình (`GroundAt(x)`), ảnh nền, đồ trang trí; `BbVillageView` vẽ theo đó. **Địa hình đi được:** bậc thấp đến 36 pixel thì bước lên, vách cao hơn chặn người đi bộ (người chơi bay hoặc nhảy qua), bước ra khỏi mép nền thì rơi (tư thế Fall_Down), hạ cánh đứng trên đá ở khe nước; AI gặp vách thì tự bay lên rồi đi tiếp; camera cuộn cả chiều dọc theo nhân vật chủ. NPC đứng đúng độ cao nền. Đổi thang x1 đến x4 trong Cấu hình tải lại ảnh map ở thang mới.
- **Hạn chế/OPEN:** nước chỉ là hình (không bơi, không chìm); tường chỉ tính theo độ cao nền chứ chưa dùng cờ loại ô của NRO (`tile_set_Info`); đồ trang trí chỉ nằm sau nhân vật; NPC vẫn dùng hình tạm (costume 430 đến 432); dữ liệu map chưa nằm ở server (chỉ gói tải qua HTTP); chưa kiểm Play mode và Android (ảnh x4 mỗi nửa 3360x1440, bộ nhớ khoảng 40 MB cho ảnh nền ở x4). Ảnh kiểm tra: `BattleBall/Logs/menu_village_folded.png`, `menu_village_walk.png`, `menu_fused.png`.

**Sửa 2026-10-08 (nhân vật đứng hơi cao, người dùng báo):** mặt đất đi được hạ thêm 11 pixel x1 (SINK1 trong 	ools/build_village_map.py, khoảng 24 pixel màn hình) so với đỉnh đo được (đỉnh đó là ngọn bụi cây nên chân nhân vật lơ lửng trên mặt cỏ); cây nhà hạ theo cùng mức. Chân nhân vật nay đứng trong dải cỏ. Chỉnh SINK1 rồi chạy lại hai script (uild_village_map.py, uild_assets.py) nếu cần thêm hoặc bớt.

**Sửa 2026-10-08 (vùng đen dưới chunk, người dùng báo):** ảnh `map_5.png` có một hàng ô đen ở đáy (96 pixel x4, tức 24 pixel x1) ngoài hai cột đen ở rìa; `build_village_map.py` nay cắt luôn hàng đó (ảnh còn 1680x336 pixel x1) nên đáy map là nước. Mọi độ cao nền tính lại theo ảnh mới. Ảnh kiểm tra: `Logs/menu_village_walk.png` (vị trí thấp nhất, nước kín đáy).

**Sửa 2026-10-08 (cây nhà nâng lại, người dùng yêu cầu):** chỉ mặt đất đi được hạ 11 pixel x1 (`SINK1`); cây, nhà, đá, cỏ giữ nguyên độ cao như trước khi hạ (đặt theo đỉnh đo được), nên nhân vật đứng thấp hơn đồ trang trí một chút.

**Nền xa mới cho làng tân thủ (2026-10-08, người dùng gửi `Gemini_Generated_Image_3bhh3b3bhh3b3bhh.png`, 5856x704):** thay trời, đồi và mây vẽ tạm bằng ảnh núi này (`BbMapChunk.Background`). `build_village_map.py` cắt hai nửa mỗi thang (`background_a_x1..x4.jpg`, `background_b_x1..x4.jpg`, JPEG chất lượng 88, 0,07 đến 0,7 MB mỗi thang), ghi màu trung bình hàng trên và hàng dưới vào `chunk.json`; máy khách (`BbChunkMaps`, `BbVillageView.BuildBackground`) hiện ảnh cao 640 pixel, **cuộn chậm 0,25 lần tốc độ map** (cả hai chiều), nối nhiều bản liên tiếp (bản chẵn lật gương để mối nối khớp), đáy ảnh đặt ở độ cao 90 của thế giới (nền map che), phía dưới đáy ảnh tô một màu phẳng (màu hàng dưới của ảnh) để không lộ trời giữa các nền; màu trời sau ảnh lấy theo hàng trên. Thiếu file thì quay lại đồi mây vẽ tạm. Chỉnh `BG_HEIGHT_CANVAS`, `BG_PARALLAX`, `BG_BASE_Y` trong script rồi chạy lại `build_village_map.py` và `build_assets.py`. Ảnh: `Logs/menu_village_folded.png`, `menu_village_walk.png`.

**Ô "Hỗ trợ 5" cho cải trang đang mặc (2026-10-08, người dùng yêu cầu):** ngay dưới 4 ô hỗ trợ có thêm ô thứ 5 (`BbMenuUi.skinSlot*`, dựng trong `BbLoginSceneBuilder`). Ô hiện hình cải trang mà **tab đang mở** đang mặc: tab Trái Đất/Xayda/Namếc là ô `SKIN` của nhân vật, tab "Hợp thể" là ô `FUSION_SKIN`. Cải trang đang mặc **tự rời khỏi kho** (kho chỉ liệt kê cải trang chưa mặc ở tab đó; nhãn "Mặc" cũ bỏ) và nằm ở ô này. Bấm ô → popup của cải trang (tên, chỉ số, mô tả) với nút **Tháo**. Tháo xong nhân vật hiện trang phục mặc định của hành tinh (`default_costume_id`), cải trang trở lại kho, ô trống ghi "Hỗ trợ 5". Giao thức: `EquipSkinRequest.costume_id = 0` nghĩa là tháo ô đó (`fusion` chọn ô `FUSION_SKIN` hay `SKIN`), không thêm trường hay số Envelope mới; `EconomyService.equipSkin` xóa dòng `character_equipment` tương ứng (tháo hai lần vẫn thành công). `MatchStarter` dùng trang phục mặc định của hành tinh khi ô `SKIN` trống. Test: thêm vào `aFusionSkinIsWornByTheFusionFormAndCountsOnlyThere` (tháo ô hợp thể, tháo ô thường, tháo lần 2, mặc lại); 62 test đạt. Ảnh: `Logs/menu_0.png` (ô có hình), `menu_skin_popup_worn.png`, `menu_skin_popup_fused.png`, `menu_skin_taken_off.png`. Chưa chạy Play mode / Android.

**Kho chung không tự sắp xếp lại, Ghim và Vứt (2026-10-08, người dùng yêu cầu):** (1) Thứ tự kho cải trang giờ **giống nhau ở mọi tab**: trước đây kho bỏ cải trang đang mặc ở tab mở nên danh sách xê dịch khi đổi hành tinh. Nay kho chỉ ẩn cải trang **đang được bất kỳ nhân vật nào của tài khoản mặc** (ô `SKIN` hoặc `FUSION_SKIN`), cải trang đó nằm ở "Hỗ trợ 5" của nhân vật mặc nó. Hệ quả: một cải trang mọi hành tinh đang do nhân vật A mặc thì phải Tháo ở tab A mới mặc cho nhân vật B. (2) **Ghim:** trong popup cải trang có nút Ghim / Bỏ ghim; cải trang ghim lên đầu kho (ghim sau đứng trước), ô hiện chữ "Ghim", phần còn lại xếp theo mã. Danh sách ghim lưu trên máy khách theo tài khoản (`PlayerPrefs bb.pins.<account_id>`), không lên server, nên đổi máy thì mất (OPEN: đưa lên server nếu muốn đồng bộ). (3) **Vứt:** nút Vứt → hộp xác nhận "Vứt cải trang này? Vứt rồi sẽ không lấy lại được" → `DiscardSkinRequest` (Envelope 48, `costume_id`) → `EconomyService.discardSkin`: xóa dòng `owned_skin` của tài khoản, ghi `item_log` (action DESTROY, kind SKIN); từ chối `NOT_OWNED` nếu không có, `DISABLED` ("take the skin off first") nếu một nhân vật của tài khoản đang mặc. Không cần migration. (4) Popup chi tiết giờ có tối đa 6 nút xếp 1 hoặc 2 hàng (`BbPopup`). Test mới `aSkinCanBeThrownAwayOnlyWhenNoCharacterWearsIt`; 63 test đạt. Ảnh: `Logs/menu_skin_popup_plain.png`, `menu_bag_pinned.png`, `menu_bag_pinned_other_tab.png`, `menu_skin_discard_ask.png`. Chưa chạy Play mode / Android.

**Nút bớt bo tròn (2026-10-08, người dùng yêu cầu):** `btn_orange.png` (nút cam: Sử dụng, Dùng cho…, Ghim, Vứt, Tháo, đăng nhập, X của popup) trước là hình viên thuốc, nay là hình chữ nhật bo nhẹ góc (bán kính 9 px trên ảnh 272x100, viền đỏ sẫm 4 px, cùng dải cam, thêm lớp bóng nhẹ ở nửa trên); viền 9-slice đổi từ 44/36 thành 12. Các tab (`tab_idle.png`, `tab_on.png`) cũng giảm bo góc còn 7 px (viền 9-slice 10). Ô đồ (`slot*.png`), khung (`panel_login.png`) và thanh EXP giữ nguyên. Các ảnh vẽ lại bằng script, bản cũ đã lưu trong thư mục tạm của phiên làm việc; muốn đổi độ bo chỉ cần sửa bán kính rồi vẽ lại. Ảnh kiểm tra: `Logs/menu_0.png`, `menu_skin_popup_plain.png`.

**Thanh EXP đổi màu và bớt bo (2026-10-08, người dùng yêu cầu):** phần đầy của thanh EXP/cấp đổi từ vàng sang xanh lá nhạt vừa (0,45; 0,78; 0,35), thành hình chữ nhật (ảnh `white`, lùi vào 2 px) trong khung thanh bo nhẹ 4 px; `bar_capsule.png` (khung thanh, dùng chung với thanh tải ở màn đăng nhập nên khung đó cũng bớt bo, màu thanh tải giữ nguyên) viền 9-slice 6. Ảnh: `Logs/menu_0.png`.

**Tách hợp thể trả cải trang hợp thể về hành trang (2026-10-08, người dùng yêu cầu):** khi người chơi bấm Bông tai "Tách" (`BbMenuUi.SplitFusion`), nếu nhân vật chủ đang mặc cải trang hợp thể (`FUSION_SKIN`) thì máy khách gửi `EquipSkinRequest{costume_id=0, fusion=true}` ngay sau khi tách (cùng lệnh "Tháo"), cải trang hợp thể rời ô và nằm lại trong kho; thông báo "Đã tách hợp thể, cải trang hợp thể về hành trang". Chỉ áp dụng khi người chơi chủ động tách; việc tự hủy cặp do snapshot thiếu nhân vật hoặc mở menu lại không đụng tới ô. Mỗi lần hợp thể lại phải mặc lại cải trang hợp thể. Ảnh: `Logs/menu_split_fusion.png`, `menu_split_fusion_bag2.png` (cải trang #1816 ở trang 2 của kho). Chưa chạy Play mode / Android.

**Tab hợp thể gọn hơn, dải vàng/đá quý nhỏ lại (2026-10-08, người dùng yêu cầu):** (1) tab "Hợp thể" ẩn 5 ô trang bị (Áo, Quần, Găng, Giày, Nhẫn) và 4 ô Hỗ trợ 1 đến 4, chỉ còn **một ô dành cho cải trang hợp thể** ở góc trên bên phải của khung nhân vật (ô trống ghi "Hợp thể"; có cải trang thì chỉ hiện hình, bấm vào để xem và Tháo). Ở các tab hành tinh ô này vẫn là "Hỗ trợ 5" ở cuối cột phải. Phần hành trang và Bông tai ("Tách") giữ nguyên. (2) Dải vàng và đá quý trước cao 54 px (đặt ở y 626 trong khung 720, chừa khoảng be trống phía dưới) nay cao 36 px, **dính sát đáy bảng** (neo đáy, không phụ thuộc chiều cao màn hình), biểu tượng và chữ nhỏ hơn (chữ 18 thay vì 24). Ảnh: `Logs/menu_fused.png`, `menu_0.png`, `menu_mails.png`. Chưa chạy Play mode / Android.

**Hành trang thêm hàng thứ 3 (2026-10-08, người dùng yêu cầu):** hành trang có 3 hàng x 5 ô (trước 2 hàng); ô đầu vẫn là Bông tai nên mỗi trang chứa 14 cải trang (trước 9). Hàng mới nằm ở y 622 đến 674, ngay trên dải vàng/đá quý. Ảnh: `Logs/menu_0.png`.

**Xem trước toàn thân trong popup cải trang (2026-10-08, người dùng yêu cầu):** popup cải trang (bấm trong hành trang, bấm ô Hỗ trợ 5, hộp xác nhận Vứt) có thêm khung xem trước bên trái hiện **nguyên nhân vật mặc cải trang** đó (dùng chính `BbCharacterView`, đứng thở và đổi khung đầu như trong màn hình chính, `uiScale` 2,4), ảnh đầu nhỏ ở góc vẫn là biểu tượng. Các dòng chỉ số dịch sang phải (rộng 250 px, tự thu chữ từ 22 xuống 15 nếu dài). Popup hòm thư và thông báo không có khung này. `BbPopup.Show(..., onClose, previewCostume)`. Ảnh: `Logs/menu_skin_popup.png`, `menu_skin_popup_worn.png`. Cải trang giả lập trong công cụ chụp ảnh không có trong bảng trang phục nên không hiện khung (chỉ ảnh giả lập). Chưa chạy Play mode / Android.

**Aura: công cụ phân bố cấp độ (2026-10-08, người dùng yêu cầu):**
- CONFIRMED (người dùng): aura là **thành tựu theo cấp độ**: người chơi đạt mốc cấp thì nhận aura, cất trong **tab "Aura" mới**, trang bị tùy ý và dùng chung cho **cả 3 hành tinh cùng lúc**. Người dùng gửi danh sách ảnh `aura_<id>_<lớp>.png` (thư mục `Teamobi2026\SRC\data\img_by_name\x4`, trừ `aura_0_*`), mỗi ảnh là dải khung xếp dọc chia đều, từ 4 khung trở lên.
- Công cụ làm xong: `BattleBallServer\tools\aura_planner\index.html` (mở thẳng bằng cách bấm đúp, không cần máy chủ), dữ liệu `data.js` và 88 ảnh lớp (bản x2, 15 MB) trong `img\`, sinh bởi `tools\build_aura_planner.py` (đọc thư mục Teamobi2026 chỉ để đọc). 65 aura (mã 1 đến 94), mỗi aura có lớp 0 (sau người) và/hoặc lớp 1 (trước người); các lớp 1x1 điểm ảnh là lớp trống nên bỏ. Trang hiện mỗi aura đang chạy hoạt hình (tốc độ chỉnh được, có hình người giả để thấy chỗ đè lên), cho nhập **cấp độ** và tên, tắt aura không dùng, chỉnh số khung từng lớp, lọc/tìm/xếp theo cấp, bảng các mốc cấp ở bên phải, **phân bố tự động** (rải đều aura chưa có cấp trong khoảng cấp từ đến, làm tròn theo bội số), **Xuất JSON** (`aura_levels.json`) và **Nhập JSON**; cấp tối đa mặc định 100 (khớp `char_stats_lv100`). Lựa chọn lưu tạm trong trình duyệt (localStorage).
- Số khung: INFERRED từ hình bằng thuật toán trong `build_aura_planner.py` (cắt đều từ 24 xuống 4 khung, lấy số lớn nhất mà các đường cắt rơi vào hàng trong suốt và khung không dẹt hơn 0,8 chiều rộng; không có thì đếm số dải nội dung). Kết quả: 77 lớp 4 khung, aura 2, 3, 4 có 8 khung, aura 50 có 19 khung, aura 62 và 80 có 11, aura 66 lớp 1 có 5, aura 77 có 12, aura 81 có 15; nhiều lớp máy chưa chắc (dấu "?" trên thẻ). Người dùng sửa số khung ngay trong trang, rồi xuất JSON.
- OPEN (chưa làm, chờ người dùng phân bố xong): bảng DB lưu aura theo tài khoản (đã mở khóa, đang trang bị), cách nhận (tự mở khi đạt cấp của nhân vật nào cao nhất tài khoản?), aura có cộng chỉ số hay chỉ là hiệu ứng, chỉ một aura được trang bị hay nhiều, vị trí vẽ aura quanh nhân vật (cần căn theo chân), đóng gói ảnh vào `build_assets.py`, tab Aura trên menu và hiển thị aura trong làng và trận.

**Sửa số khung aura 28 và 63 (2026-10-08, người dùng báo hiệu ứng như nối đuôi nhau):** ảnh `aura_28_0` và `aura_63_0` (600x1500 ở x4) gồm **5 khung rộng 600x300**, các khung chỉ cách nhau 3 hàng điểm ảnh; thuật toán trước coi khung phải cao ít nhất 0,8 lần chiều rộng nên loại hết và rơi về 4 khung (cắt sai, các khung dính vào nhau). Nay đếm dải nội dung với khe gộp 2 hàng và chấp nhận khung dẹt tới 0,4 (`MIN_BAND_RATIO`): cả hai thành 5 khung (300x150 ở x2). Các aura còn lại không đổi. Nếu thấy aura khác bị nối đuôi tương tự thì báo mã aura, hoặc sửa số khung ngay trong trang.

**Sửa số khung aura 77 (2026-10-08, người dùng báo):** `aura_77_0` (595x10992 ở x4) là **24 khung** (mỗi khung 595x458, đường cắt rơi vào hàng trong suốt chỉ khi n là ước của 24), nhưng ngưỡng khung/chiều rộng 0,8 loại 24 nên trước đó máy chọn 12 (hai khung dính làm một). Hạ ngưỡng xuống 0,7 (`MIN_FRAME_RATIO`): chỉ aura 77 đổi (12 thành 24), các aura khác giữ nguyên. Aura này chạy một lần từ nhỏ, to dần rồi tan (hai khung cuối trống). Chạy lại: `python tools\build_aura_planner.py`.

**Bảng cấp độ aura đã được người dùng phân bố (2026-10-08):** người dùng gửi `aura_levels.json` xuất từ trang phân bố; bản lưu trong dự án là `BattleBallServer\tools\aura_planner\aura_levels.json` (CONFIRMED: đây là nguồn mốc cấp của aura). 65 aura, không aura nào bị tắt hay chưa có cấp, cấp tối đa 100, mốc từ cấp 1 đến 100 (cấp 1 có 2 aura là 8 và 93; các cấp còn lại mỗi cấp tối đa một aura). Một vài mốc: aura 93 và 8 cấp 1, aura 94 cấp 2, aura 92 cấp 3, aura 12 cấp 100, aura 28 cấp 99. Số khung trong file khớp dữ liệu của công cụ trừ aura 77: file ghi 12 vì xuất trước lần sửa, bản lưu trong dự án đã đổi thành 24 (chỉ sửa trường này). Chưa tạo bảng DB, tab Aura hay hiển thị trong game; các câu hỏi mở về cách hoạt động của aura (chỉ số hay chỉ hiệu ứng, một hay nhiều aura cùng lúc, mở theo cấp nhân vật nào) vẫn còn.

**Hệ thống Aura (2026-10-08, người dùng gửi `aura_levels.json` và chọn cách hoạt động):**
- CONFIRMED (người dùng): aura **chỉ là hiệu ứng hình ảnh** (không cộng chỉ số); **một aura mỗi lần** (chọn aura mới thì aura cũ tự tháo), **dùng chung cho cả 3 hành tinh**; **mở khóa theo nhân vật cấp cao nhất của tài khoản** (cấp cao nhất đạt mốc của aura là mở, cấp không giảm nên không cần bảng "đã mở"). Mốc cấp lấy từ bảng người dùng phân bố (65 aura, cấp 1 đến 100).
- DB: migration `V029__auras.sql` (sinh bởi `tools/make_aura_migration.py` từ `tools/aura_planner/aura_levels.json`): `balance_config 'auras'` = `{"items":[{id, name, level, enabled, layers:[{layer, frames}]}]}` (sửa được ở trang admin "Cân bằng"; tăng phiên bản BALANCE) và cột `account.equipped_aura_id` (NULL = không đeo). Đã áp dụng vào DB thật khi khởi động lại server.
- Giao thức: `PlayerDataSnapshot.auras` (mọi aura đang bật: id, tên, `unlock_level`, các lớp `{layer, frames}`) và `equipped_aura_id`; yêu cầu `EquipAuraRequest{aura_id}` (Envelope 49, 0 = tháo) trả `ActionResult` rồi đẩy snapshot mới; lỗi `NOT_FOUND` (không có aura), `LEVEL_TOO_LOW` (nhân vật cao nhất chưa đủ cấp). Server: `game/Auras.java`, `EconomyService.equipAura`, `PlayerActions`. Test `anAuraIsWornWhenTheHighestCharacterReachedItsLevel` (64 test đạt); `BbNetSelfTest` bước aura đạt với server thật.
- Tài nguyên: `build_assets.py` chép ảnh aura từ thư mục Nro `img_by_name/x{N}` (chỉ đọc) vào `BattleBall\SourceData\data\aura\x{N}\`, **nén bảng màu 256 màu có trong suốt** (nhỏ hơn 5 lần, hình gần như y hệt) và đóng gói `aura_x{N}`: x1 1,05 MB, x2 3,35 MB, x3 6,29 MB, x4 9,59 MB; tải về cho thang x3 từ 30 lên 36,5 MB. Ảnh là dải khung xếp dọc; số khung lấy từ bảng người dùng (aura 28, 63: 5 khung dẹt; aura 77: 24 khung).
- Máy khách: `BbAuraAssets` (nạp và cắt ảnh theo khung, giữ tối đa khoảng 48 MB texture, bỏ cái lâu không dùng), `BbAuraRender` (vẽ theo Nro `Char.paintAuraBehind/paintAuraFront`: lớp 0 sau người, lớp 1 trước người thấp hơn 2 điểm ảnh Nro, đáy khung đặt đúng chân nhân vật, đổi khung mỗi 4 tick của 25 tick/giây), `BbAuraThumb`, `BbCharacterView.SetAura` (aura **chỉ hiện khi nhân vật đứng yên**, như Nro; đổi bằng hằng `AuraOnlyWhenStanding`), `BbVillageView.SetAura` (cả đội trong làng), `BbMenuAuras` (tab **Aura**: 16 aura mỗi trang xếp theo cấp mở khóa, aura chưa mở mờ đi và ghi "Cấp N", aura đang dùng viền xanh "Đang dùng", mũi tên chuyển trang), popup aura có khung xem trước rộng hiện nhân vật đang chọn với aura (nút Trang bị / Tháo / "Chưa đủ cấp N"). Tab thêm vào giữa Hợp thể và Chức năng (tối đa 6 tab, chữ nhỏ lại). `BbClient.EquipAuraAsync`.
- Ảnh kiểm tra: `Logs/menu_aura_tab.png`, `menu_aura_tab_last.png`, `menu_aura_popup_worn.png`, `menu_aura_popup_locked.png`, `menu_aura_on_character.png`. Chưa chạy Play mode / Android.
- OPEN: aura chưa hiện trong trận (chỉ làng và menu); texture lớn (aura 77 ở x4 cao 10.992 điểm ảnh, vượt 8192 của một số máy Android cũ; ở x3 là 8.244, có thể phải cắt ảnh thành nhiều texture); tải tài nguyên aura bắt buộc cho mọi người chơi (6 MB ở x3); mọi tên aura đang để trống nên hiện "Aura N" (đặt tên trong trang phân bố hoặc admin); chưa có thông báo khi lên cấp mở khóa aura mới.

**Avatar của cải trang lấy đúng hình đầu đang vẽ (2026-10-08, người dùng báo):** hình đại diện của cải trang (góc trên trái của menu, ô Hỗ trợ 5, hành trang, popup cải trang, quà trong hòm thư) trước lấy `iconId` của bảng cải trang. Với cải trang đầu nhiều khung (dạng hợp thể như #1816: `iconId` 15622) hình đó **không nằm trong khung đầu nào** (các khung đầu dùng 15615, 15616, 15617), nên avatar khác với đầu nhân vật. Nay `BbCostumeAvatar.Of(costumeId)` lấy hình **đầu ở tư thế đứng của khung đầu tiên** (phần đầu `HeadCyclePartIds[0]` hoặc `HeadPartId`, khung Stand_1), cùng hình nhân vật đang vẽ; cải trang thường không đổi vì ở đó `iconId` đúng là hình đầu (đã đối chiếu #412, #415, #418). Không có phần đầu để vẽ thì quay lại `iconId`. Sửa kèm: aura không còn gắn lên các NPC của làng, chỉ các nhân vật của người chơi. Ảnh: `Logs/menu_fused.png`.

**Aura hiện cả khi nhân vật chạy, bớt việc vẽ lại (2026-10-08, người dùng yêu cầu và báo giật):** `BbCharacterView.AuraOnlyWhenStanding` đổi thành `false`: aura hiện ở mọi tư thế (chạy, nhảy, bay, rơi), không còn chớp tắt mỗi lần nhân vật dừng hoặc đi như cách Nro vẽ (đặt `true` để quay lại kiểu Nro). Về nghi vấn "tab Aura vẫn chạy khi không mở": CONFIRMED trong code là **không**, `BbMenuAuras.Hide` tắt cả gốc của tab (`root.SetActive(false)`), nên 16 ô aura không chạy `Update`, không vẽ và không nạp ảnh khi đang ở tab khác. Việc đang chạy liên tục là aura quanh nhân vật (3 nhân vật ở làng và hình nhân vật của tab) cộng thêm giờ hiện cả lúc chạy. Đã giảm việc thừa: `BbAuraRender.Tick` trước đặt lại ảnh, kích thước và vị trí ở **mỗi khung hình của game**, nay chỉ làm khi khung aura đổi (7,5 lần mỗi giây), hoặc đổi tỉ lệ hoặc ẩn hiện. UNKNOWN (chưa đo trên máy/điện thoại): nguyên nhân giật thực sự; nghi vấn còn lại là ảnh aura rất lớn và trong suốt bị vẽ chồng nhiều lớp (mỗi nhân vật 2 lớp, 3 nhân vật và hình nhân vật của tab) và cả menu nằm chung một Canvas nên mỗi lần đổi khung phải dựng lại. Nếu vẫn giật cần biết máy nào, thang x mấy, và aura số mấy.

**Aura: chủ luôn hiện, AI chỉ hiện khi đứng yên (2026-10-08, người dùng yêu cầu):** hằng `BbCharacterView.AuraOnlyWhenStanding` thành trường `auraOnlyWhenStanding` cho từng nhân vật; `BbVillageView.SetCast` đặt `true` cho các nhân vật AI và `false` cho nhân vật chủ (đổi được khi nhân vật chủ đổi theo tab). Nhân vật chủ giữ aura khi chạy, nhảy, bay; AI chỉ có aura khi đứng yên (như Nro). Hình nhân vật ở tab menu và khung xem trước không đổi (luôn hiện). Chưa chụp ảnh kiểm tra vì người dùng đang mở Unity Editor với project BattleBall (không mở bản thứ hai); code sẽ biên dịch lại khi Editor nạp lại script.

**Aura nhanh gấp đôi (2026-10-08, người dùng yêu cầu):** `BbAuraRender.FramesPerSecond` từ 6,25 (Nro: đổi khung mỗi 4 tick của 25 tick/giây) lên 12,5 khung mỗi giây, cho cả aura quanh nhân vật, khung xem trước và các ô của tab Aura. Chỉnh một hằng số trong `BbAuraAssets.cs`.

**AI đứng xa nhau và xa nhân vật chủ; tab Aura dùng ảnh tĩnh (2026-10-08, người dùng yêu cầu):**
- Khoảng cách trong làng (`BbVillageView.FollowOffset`): AI đầu tiên cách nhân vật chủ 150 px, mỗi AI tiếp theo cách AI trước 130 px (trước đây cả đội cách nhau 82 px). Dùng cho vị trí lúc xuất hiện, lúc đổi nhân vật chủ và khi AI đi theo; các hằng `FirstFollowGap` và `AiFollowGap`.
- Tab Aura không còn hoạt hình: mỗi ô là một **ảnh tĩnh nhỏ 128x128** (`data/aura/thumb/aura_<id>.png`, 65 ảnh, tổng 1,2 MB) do `build_assets.py` tạo từ ảnh x2: chọn khung có nhiều nội dung nhất, gộp hai lớp (lớp sau vẽ trước), đáy đặt sát đáy ảnh. Nằm trong gói `common` (không phụ thuộc thang x), nên tab mở ra tức thì: chỉ đọc vài chục file nhỏ thay vì nạp các dải ảnh lớn, và không có gì chạy khi mở tab. `BbAuraAssets.GetThumb` giữ ảnh nhỏ trong bộ nhớ; thiếu file thì lấy khung đầu của ảnh lớn. Hoạt hình chỉ còn ở khung xem trước trong popup (bấm vào một aura) và aura quanh nhân vật. Bỏ lớp `BbAuraThumb`. Phiên bản tài nguyên đổi (`6237dc5858814d49`), đã khởi động lại server; kiểm tra tải tài nguyên đạt. Ảnh: `Logs/menu_aura_tab.png`, `menu_village_folded.png`.

**Avatar cải trang TD: không dùng ảnh ngoài khung (2026-10-08, người dùng nhắc):** với cải trang TD (#1816) ảnh `icon/x4/15621.png` (256x256, tranh to) và `15622.png` (96x96, `iconId` của bảng cải trang và của `skin_template.icon_id`) là các ảnh đơn, **không nằm trong khung nào** của nhân vật (đầu dùng 15615 đến 15620, thân và chân dùng 15561 đến 15610), nên không được làm avatar. Đã kiểm: không có chỗ nào trong DB, server, trang admin hay mã máy khách dùng 15621; avatar đã lấy hình đầu khung đầu (15615) từ lần sửa trước (`BbCostumeAvatar`). Sửa thêm: khi hình đầu chưa tải được, cải trang đầu nhiều khung **không còn quay về `iconId`** (trả về trống) thay vì hiện một ảnh ngoài khung; cải trang thường vẫn quay về `iconId` vì ở đó trùng với hình đầu.

### 15.11 Menu theo đúng giao diện NRO (2026-10-08, người dùng yêu cầu "mô phỏng lại menu từ NroUnity NROL.unity")

Đã chạy NroUnity (scene `NROL`, Unity 2022.3.62f2) với server Teamobi local (127.0.0.1:14445) bằng tài khoản người dùng đưa (tdno01) và chụp đủ 5 tab của panel (Nhiệm vụ, Hành trang, Kỹ năng, Bang hội, Chức năng) cùng popup chi tiết đồ. Số đo lấy từ mã `Game1/Panel.cs`, `Paint.cs`, `PopUp.cs`, `Menu.cs`, `Command.cs` (CONFIRMED): panel rộng 176 px game (`WIDTH_PANEL`), nền `#DAC9B4` viền `#664400` (`paintFrameSimple`), dải đầu cao 50 màu `#987B55`, tab ở y 52 cao 25 vẽ bằng `myTexture2dimgPopup.png` (thường, ruột `#FFE5C7`) và `myTexture2dimgPopup2.png` (đang chọn, ruột `#B7E8B0`), vạch cam `#CE5E0C` dưới hàng tab, ô trang bị 31x21 màu `#987B55`, ô hành trang màu `#B49F84`, ô đang chọn `#F8FF4B`, mỗi ô có viền trắng mờ 50%, khung chỉ số giữa hai cột 100x50 màu `#987B55` chữ vàng, dải tiền dưới cùng cao 14 màu `#B49F84` (vàng `myTexture2dimgMoney`, ngọc `myTexture2dimgDiamond`, hồng ngọc `luongkhoa`), dòng danh sách tab Chức năng `#E7DFD2` chữ đậm căn giữa, nút đóng `myTexture2dbtX.png`, nút lệnh của popup đồ là ô vuông bo tròn 58x58 (`myTexture2dbtMenu2.png` xanh cho nút đầu, `myTexture2dbtMenu1.png` cam cho các nút còn lại), nút mở panel là tam giác cam sát mép trái (`myTexture2dmenu.png`).

Người dùng chọn (CONFIRMED):
- **Giữ hàng tab của BattleBall** (Trái Đất, Xayda, Namếc, Hợp thể khi có, Aura, Chức năng); chỉ đổi giao diện cho giống NRO.
- **Chép ảnh giao diện NRO** (các file trên, từ `NroUnity/Assets/Resources/res/x4/mainimage/`) sang `BattleBall/Assets/BattleNet/UI/Sprites/` với tiền tố `nro_`.
- **Panel hẹp như NRO**: 320 trên khung tham chiếu 1280x720 (25%), mọi số đo NRO nhân 320/176 ≈ 1,82.
- **Giữ thanh EXP** (nằm giữa dải đầu và hàng tab).

Thực hiện (CONFIRMED, đã kiểm bằng ảnh chụp Unity batch, chưa chạy Play mode hay Android):
- Ảnh: sinh một lần bằng script Python (không đưa vào `tools`) từ ảnh x4 của NRO vào `BattleBall/Assets/BattleNet/UI/Sprites/`: `nro_tab_idle.png`, `nro_tab_on.png` (ghép 8 mảnh của dải tab thành ảnh 9 mảnh; ruột lấy màu bên trong mảnh viền vì màu NRO tô ở giữa lệch nhẹ, khi kéo giãn sẽ lộ ô vuông), `nro_cell_body.png`, `nro_cell_bag.png`, `nro_cell_sel.png` (ô 9 mảnh: ruột, khe 1 px, viền trắng 50%), `nro_popup.png` (hộp trắng viền đen, góc đỏ `myTexture2dbd3`), `nro_btn_orange.png`, `nro_btn_green.png`, `nro_close.png`, `nro_gold.png`, `nro_gem.png`, `nro_menu_arrow.png`, `nro_scroll_down.png` (mũi tên cuộn xanh, 36x24 đầu của `myTexture2dmobHP.png`). Ảnh 9 mảnh nhập với 220 pixel mỗi đơn vị để 1 px NRO thành 1,82 đơn vị như phần còn lại. Không dùng hồng ngọc (`luongkhoa`) vì BattleBall không có loại tiền này.
- `BbLoginSceneBuilder`: panel 320, nền `#DAC9B4`, mép phải `#664400`; dải đầu cao 91 (ảnh đầu bên trái, 4 dòng chỉ số tự thu nhỏ, nút X); thanh EXP cao 14 dưới dải đầu; hàng tab y 110 cao 45 và vạch cam; cột trái 5 ô trang bị, cột phải Hỗ trợ 1 đến 4 và Hỗ trợ 5 (không còn chữ trong ô, như NRO); nhân vật đứng trên khung chỉ số 182x91; dòng "Hành trang" cùng số trang và mũi tên cuộn NRO; 6 hàng hành trang 5 ô; dải tiền cao 27. Popup: hộp trắng 327x300 đứng trên hàng nút (102x92, 3 nút một hàng, hàng đầu ở trên, nút chính màu xanh), không làm tối nền, bấm ra ngoài hộp để đóng (bỏ nút X của popup). Tab Aura 4x5 ô (20 aura một trang), tab Chức năng là các dòng `#E7DFD2` chữ đậm căn giữa, Tài khoản dùng dòng, Cấu hình dùng nút NRO, form đổi mật khẩu dùng hộp `nro_popup`.
- `BbMenuUi`: X gập panel, tam giác cam sát mép trái (ở 3/4 chiều cao tính từ dưới) mở lại, chỉ hiện khi panel đang gập; tên tab xuống 2 dòng như NRO; ảnh đầu luôn hiện (cả tab Aura và Chức năng); ô của món đang mở popup tô vàng (`#F8FF4B`) cho đến khi popup đóng; số ô hành trang một trang tính theo chiều cao màn hình (máy dài có khung thấp hơn 720 nên các hàng chạm dải tiền bị ẩn, trang chứa ít ô hơn). `BbPopup` đặt hộp và nút lúc chạy. Ảnh: `Logs/menu_0.png`, `menu_skin_popup.png`, `menu_skin_popup_plain.png`, `menu_functions.png`, `menu_mails.png`, `menu_mail_popup.png`, `menu_aura_tab.png`, `menu_aura_popup_worn.png`, `menu_config.png`, `menu_password.png`, `menu_fused.png`, `menu_village_folded.png`.
- OPEN: chưa có ảnh món đồ trang bị (máy khách chưa có bảng vật phẩm) nên ô trang bị chỉ đổi sang màu sáng khi có đồ; nút X và tam giác nhỏ (31 và 25x55 đơn vị) theo đúng tỉ lệ NRO, chưa thử độ dễ bấm trên điện thoại.

**Làng: nhanh gấp đôi cho cả 3 nhân vật (2026-10-08, người dùng yêu cầu "tăng tốc độ frame chạy bộ của AI lên gấp đôi, tăng tốc độ di chuyển của cả 3 lên gấp đôi (cả frame và tốc độ di chuyển/bay)"):** trong `BbVillageView`:
- Đi bộ `Speed` 600 → 1200 (AI theo sau bằng `Speed * 1,15`, nên cũng gấp đôi); bay `FlySpeed` 300 → 600 (AI bay và leo tường theo hệ số của tốc độ này); hạ xuống `SinkSpeed` 480 → 960. Nhảy (`Jump`, `Gravity`) giữ nguyên để độ cao nhảy không đổi.
- Khung chạy của AI `AiRunAnimSpeed` 0,4 → 1,6 (INFERRED theo câu yêu cầu: gấp đôi riêng cho AI, rồi gấp đôi thêm cùng cả nhóm; một khung mỗi 0,05 s). Khung chạy của nhân vật chủ 2 → 4 (`MainRunAnimSpeed`), khi đứng vẫn 2 để nhịp thở không bị nhanh theo.
- Kiểm tra: Unity batch biên dịch và chụp ảnh không lỗi (`Logs/menu_village_walk.png`); chưa chạy Play mode nên chưa xem được chuyển động thật.

**Nhịp thở nhanh gấp đôi (2026-10-08, người dùng yêu cầu "tốc độ thở tăng gấp đôi"):** `BbCharacterView.breathSeconds` 0,6 → 0,3 (khoảng cách giữa hai khung thở khi `animSpeed` = 1), áp dụng cho mọi nhân vật: nhân vật chủ trong làng thở mỗi 0,15 s (`animSpeed` 2), AI đứng yên mỗi 0,3 s, nhân vật trong panel và khung xem trước của popup mỗi 0,3 s. Đã dựng lại scene Menu để hai nhân vật dựng sẵn trong scene nhận giá trị mới (Unity batch, không lỗi); chưa xem bằng Play mode.

### 15.12 NPC luyện tập (test sát thương và skill) ở làng tân thủ (2026-10-08, người dùng yêu cầu; ĐANG LÊN KẾ HOẠCH)

Yêu cầu (CONFIRMED): một NPC kẻ địch ở làng tân thủ, **2 tỷ máu, hết máu thì tự hồi đầy**, dùng để test sát thương và skill; **mũi tên mục tiêu** trên đầu đối tượng đang chọn **đổi màu theo máu**; **thanh máu của NPC ở giữa màn hình, hơi lệch lên trên, kèm tên NPC**; ảnh hiệu ứng skill tối thượng lấy từ `NroUnity/Assets/Resources/res/x4/e`, ảnh trạng thái từ `res/x4/mainimage`.

Người dùng chọn (CONFIRMED): **toàn bộ bộ skill NRO** (không chỉ đấm và tuyệt kỹ); **server tính sát thương**; **cả 3 nhân vật cùng đánh** (2 AI tự đánh mục tiêu); hình NPC: trả lời "cả 2" (cải trang có sẵn và Mộc nhân của NRO; cách hiểu chính xác: OPEN).

Đã kiểm trong NRO (CONFIRMED): mũi tên mục tiêu là dải 10 tam giác `myTexture2dmobHP.png` (9x6 px mỗi cái), `Char.paintHp` chọn ô `6 * (9 - (hp% / 10 - 1))` theo mỗi mốc 10% (100%: xanh nhạt, rồi xanh dương, xanh ngọc, xanh lá, vàng chanh, cam, đỏ, hồng, đỏ sẫm, dưới 10%: xám); thanh máu dùng `myTexture2dHP.png` (đỏ) trên `myTexture2dhpLost.png` (vàng, phần vừa mất); thanh nhỏ trên đầu `tm-xanh/vang/do` theo 60%/30%. Mộc nhân là `mob_template` id 0 (`SourceData/data/mob/x4/0`). Server đã có bộ máy trận 20 Hz với 25 skill, AI đồng đội và `DamageEvent` (15.2); máy khách chưa có màn chiến đấu nào.

Người dùng chốt thêm (CONFIRMED): **2 NPC riêng** (một Mộc nhân, một NPC mặc cải trang có sẵn), cả hai 2 tỷ máu và tự hồi; **làm theo 2 đợt**: đợt 1 là chế độ luyện tập trên server (dùng lại bộ máy trận, NPC không đánh lại, không giờ, không thưởng, hết máu hồi đầy, AI dùng skill như trong trận, di chuyển trong làng vẫn do máy khách và máy khách báo vị trí cả 3 nhân vật), cùng chọn mục tiêu, mũi tên đổi màu, thanh máu giữa màn hình kèm tên, 9 ô skill, số sát thương, động tác đấm; đợt 2 là hình ảnh riêng của từng skill theo NRO.

**Đợt 1 đã làm (CONFIRMED, 2026-10-08):**
- **Server:** `MatchSetup.Training` và `MatchSetup.Dummy`, thực thể `Entities.Dummy` (phe địch, không hành động). `MatchEngine` ở chế độ luyện tập: arena là bản đồ làng, không boss, không quái, không thua, không hết giờ (chỉ đóng khi bỏ không `idle_end_s`); NPC hết máu thì máu về đầy và có hiệu ứng REGEN; nhân vật chết đứng dậy sau `revive_after_s`; AI không tự đi (đứng theo `PlayerStateReport.allies`) nhưng vẫn chọn và dùng skill theo `ai_skill_order`, đánh đúng NPC mà người chơi đánh gần nhất; NPC luyện tập được coi như boss cho điều kiện dùng skill của AI. `MatchStarter.startTraining` (không ghi DB, run id 0; skill chưa học thêm ở `unlearned_skill_level`). `MatchManager.startTraining` (thay phiên luyện tập cũ, từ chối khi đang có trận thật), mất kết nối thì kết thúc ngay, kết thúc không trả thưởng và chỉ gửi `MatchEnded`. `ConnectionHandler` nhận `START_TRAINING_REQUEST`. Skill có thêm `icon_id`. Migration V030.
- **Kiểm thử server:** 67/67 đạt (thêm `MatchEngineTest.aTrainingDummyRefillsItsHpAndTrainingNeverEndsByItself`, `trainingEndsAfterTheIdleTime`, `MatchIntegrationTest.aTrainingSessionOffersEverySkillOfTheRaceAndRecordsNothing`). Server dev đã khởi động lại và áp V030.
- **Tài nguyên:** `build_assets.py` thêm icon của 25 skill và dải khung mob (`data/mob/strip/x{N}/0.png`, `data/mob/strip/0.json`, gói `mob_x{N}`), cắt từ file mob Nro trong `SourceData/data/mob` (byte kiểu đọc, dữ liệu khung, ảnh PNG như `Controller` case 11 của NroUnity). Phiên bản tài nguyên `bef844cc20dba4aa`.
- **Bản đồ:** `build_village_map.py` và `SourceData/data/map/village_start/chunk.json` thêm 2 NPC `TrainingDummy` (Mộc nhân x = 880, Saibamen x = 1060 px Nro, trên bậc đất bên phải Ông Trưởng làng); bản đồ dự phòng cũng có.
- **Máy khách:** `BbTraining` (mới): chạm NPC để chọn mục tiêu và vào luyện tập; báo vị trí cả 3 nhân vật 10 lần/giây; mũi tên mục tiêu (dải `nro_target_arrows.png`, màu theo mỗi 10% máu như `Char.paintHp`); thanh máu kèm tên ở giữa phía trên màn hình (`nro_hp_red.png` trên `nro_hp_lost.png`, phần vàng tụt dần); 9 ô skill ở góc phải dưới (`nro_skill.png`, `nro_skill_on.png`, icon skill, hồi chiêu, thiếu Ki thì xám) và thanh KI; số sát thương bay lên (chí mạng to và vàng, "Hụt" khi bị chặn), chữ "Hồi đầy máu" khi NPC hồi; động tác đấm, bắn chưởng, gồng của cả 3 nhân vật (`BbCharacterView.PlayAction`); đạn tạm vẽ bằng vầng sáng (đợt 2 sẽ thay bằng ảnh thật). Phiên đóng khi đi xa NPC hơn 1.600 px quá 3 s, đổi nhân vật chính hay rời menu. `BbMobView` (mới) vẽ Mộc nhân (bị đánh thì hiện 2 khung nghiêng nón). `BbVillageView`: NPC luyện tập tên đỏ, AI đứng hai bên NPC đang chọn (cách 100 px canvas, trong tầm đấm). `BbNetSelfTest` thêm bước luyện tập.
- **Kiểm tra máy khách:** dựng scene và chụp ảnh giả lập không lỗi (`Logs/menu_training.png`, `menu_training_saibamen.png`); kiểm tra mạng thật với server: vào phiên có 2 NPC, đủ 9 skill có icon và tên, cú đấm của nhân vật cấp 1 trúng Mộc nhân 12.731 (Sức đánh cấp 1 = 26.000 / 1,99 ≈ 13.065, ±5%), rời phiên thì `ABANDONED`; kiểm tra tải tài nguyên đạt. **Chưa chạy Play mode** nên chưa thử bằng tay (chạm NPC, bấm skill, nhìn AI đánh).
- OPEN / chưa làm: đợt 2 (hình ảnh riêng từng skill theo NRO); Dịch chuyển tức thời đổi vị trí trên server nhưng máy khách không dịch nhân vật theo; trứng (Đẻ trứng) chưa vẽ; ô skill dùng hồi chiêu tính ở máy khách (server không gửi hồi chiêu từng ô).

**Tư thế skill theo dữ liệu NRO, tab Kỹ năng (2026-10-08, người dùng yêu cầu "đòn đánh thường thiếu frame Kick_1 Kick_2 Kick_High, thêm 1 tab gồm full kỹ năng ... dùng vàng để nâng cấp từng kỹ năng lên lv7, các frame tụ năng lượng khi dùng quả cầu kênh khi, biến khỉ đang dùng sai frame, xem lại đã dùng đủ 33 frame chưa"):**
- Đã kiểm trong NroUnity (CONFIRMED): tư thế khi dùng skill lấy từ dữ liệu SkillPaint (`GameScr.readSkill` đọc `NR_skill` = `Teamobi2026/SRC/data/update_data/skill`): mỗi mã paint có dãy `stand` và `fly`, mỗi phần tử `status` là số tư thế (0..32) hiện trong **một khung vẽ** (`Char.paintCharWithSkill` tăng `indexSkill` mỗi lần vẽ; NroUnity mặc định 30 khung/giây). Mã paint của từng cấp nằm ở `skill_template.skills[cấp-1].id`. Đấm (Dragon 0..6, Demon 14..20, Galick 28..34, Kaioken 63..69, Liên hoàn 107..109) chọn ngẫu nhiên một trong (cấp + 3) combo đầu (`Char.setAutoSkillPaint`), các combo này có Kick_1 (9), Kick_2 (10), Kick_High (11), Jump_Up (7), Run (2..5). Quả cầu kênh khi (`isFlyAndCharge`): bay lên với tư thế 7 trong 20 khung rồi giữ tư thế 32 khi tụ năng lượng. Makankosappo, Tự phát nổ (`isStandAndCharge`): giữ tư thế 17 (tư thế 12 khi đang bay, trừ Xayda). Biến hình (`isWaitMonkey`): paint của skill (24 x 8, rồi 17) rồi giữ tư thế 17 khi chờ biến khỉ. Ba tuyệt kỹ không có paint trong dữ liệu (Nro vẽ bằng `Effect_End`).
- **33 tư thế:** dữ liệu skill dùng mọi tư thế 0..32 trừ 1 (Stand_2_Breathing, đã dùng khi đứng thở) và 25 (Dash_Teleport, nay dùng cho Dịch chuyển tức thời). Đứng/thở 0, 1; chạy 2..6; nhảy 7; bay 8; đá 9, 10, 11; rơi 12; đấm 13..16; gồng 17, 18; chưởng 19..21; 22; bị đánh 23 (Saibamen khi bị đánh); 24; 25; các tư thế bay 26..32.
- **Làm:** `build_assets.py` xuất `data/skill/skill_poses.json` (gói common; nguồn chép vào `SourceData/data/update_data/skill`). Máy khách: `BbSkillPoses` (mới) đọc dữ liệu, chọn combo đấm như NRO, dựng dãy gồng (`Charge`) và biến khỉ (`Monkey`); `BbTraining` dùng nó cho cả 3 nhân vật: bấm skill (skill gồng đợi `CAST_START` của server để biết thời gian gồng), đòn của AI (sát thương, đạn, hiệu ứng có `skill_id`), dùng dãy `fly` khi nhân vật đang ở trên không; 30 khung/giây (`BbSkillPoses.FramesPerSecond`).
- **Tab Kỹ năng:** tab thứ 7 (sau các hành tinh và Hợp thể, trước Aura), một danh sách gồm skill của **cả 3 nhân vật** theo nhóm hành tinh (8 skill của hành tinh và Khiên năng lượng), mỗi dòng như danh sách skill của NRO: icon trong khung `nro_skill`, tên, "Cấp x/7" và giá vàng cấp sau, nút Học/Nâng (xám khi thiếu vàng). Chạm dòng mở popup: số liệu cấp hiện tại và cấp sau (sát thương % Sức đánh, hồi chiêu, % Ki), mô tả NRO, nút nâng cấp. Dùng `UpgradeSkillRequest` có sẵn (vàng chung của tài khoản; học = cấp 0 lên 1). Cấp tối đa theo dữ liệu: **7** cho đấm và skill thường, **9** cho 3 tuyệt kỹ (GDD mục 4); người dùng nói "lên lv7", nếu muốn tuyệt kỹ cũng dừng ở 7 thì đổi `max_level` trên trang admin (OPEN).

**Khung HP/KI của 3 hành tinh (2026-10-09, người dùng yêu cầu; CONFIRMED):** trong lúc luyện tập, góc trên trái màn hình có **mỗi nhân vật một khung** `myTexture2dpanel.png` của NRO, xếp ngang theo thứ tự Trái Đất, Xayda, Namếc (hợp thể chỉ còn các nhân vật đang ra sân). Vùng xanh lá ghi tên hành tinh (dòng trên) và "Cấp N" (dòng dưới, thay cho sức mạnh của NRO); thanh trên là HP (`myTexture2dHP` đỏ, phần vừa mất `myTexture2dhpLost` vàng), thanh dưới là KI (`myTexture2dMP` xanh lơ, phần vừa mất `myTexture2dmpLost` xanh đậm). Vị trí khe theo `GameScr.paintImageBar` (đơn vị x4: HP 264x40 tại (332,20), KI 236x24 tại (332,80)); thanh được cắt theo tỷ lệ (Image Filled) như NRO cắt bằng clip, phần "mất" thu dần về giá trị thật. Máu và KI lấy từ trạng thái thực thể của server (cả AI). Thanh KI cũ ở góc dưới phải đã bỏ. Mã: `BbTraining.BuildHuds/RefreshHuds`, sprite `nro_hud_*` trong `UI/Sprites`. **Khung thanh máu mục tiêu (boss):** dùng `i_pve_bar_1.png` của NRO (người dùng chỉ định 2026-10-09), vẽ dạng 9-slice (viền 24) chỉ bao thanh HP (tên nằm trên khung, người dùng chỉnh lại cùng ngày), rộng 440 cao 74 ở giữa trên màn hình (`nro_boss_frame`). **Nút skill luôn hiện ở làng (2026-10-09, người dùng yêu cầu):** không cần chọn mục tiêu nữa; chỉ ẩn khi bảng menu, popup (NPC nói chuyện, vật phẩm), danh sách Chức năng hoặc hộp chọn đang mở (`BbMenuUi.BlocksCombatUi`). Khi chưa có phiên, máy khách dựng nút từ `skill_catalog` của snapshot (skill của chủng tộc nhân vật chính và skill chung, theo ô `slot`, cấp đã học hoặc 1 như `unlearned_skill_level`), dựng lại khi đổi nhân vật hay cấp skill. Bấm khi đã chọn mục tiêu: vào luyện tập như cũ (bấm lại để dùng skill); bấm khi chưa chọn mục tiêu: chỉ diễn tư thế skill và hồi chiêu ở máy khách, không gửi gì cho server, không tính Ki (OPEN: chưa có số Ki của nhân vật ngoài trận để làm xám nút thiếu Ki). **Nắm đấm tự động, nút đổi mục tiêu, hồi chiêu (2026-10-09, người dùng yêu cầu; CONFIRMED):** ngoài cùng bên phải chuỗi skill có nắm đấm `myTexture2dfirebtn0/1`: bấm để bật tắt chế độ tự động dùng **skill đang focus** (skill vừa bấm, mặc định đấm thường; nút được tô vàng) liên tục, chờ hồi chiêu (có chừa 0,12 s cho độ trễ mạng), bật thì nắm đấm có viền xanh và chữ AUTO. Khi dùng skill mà chưa có mục tiêu thì tự focus mục tiêu **gần nhất trong tầm nhìn màn hình** (không có thì báo "Không có mục tiêu trong tầm nhìn"), vào phiên luyện tập nếu chưa có, và với skill tầm ngắn (theo `range_px` của server) thì nhân vật chính **tự chạy tới** mục tiêu rồi mới đánh; đẩy cần điều khiển là lấy lại quyền điều khiển. Nút `myTexture2dfocus` (trên nắm đấm) đổi sang mục tiêu kế tiếp trong tầm nhìn, từ trái sang phải. Mở menu, popup NPC hay danh sách Chức năng thì nút ẩn và tự động tạm dừng. Mỗi nút skill hiện **số giây hồi chiêu còn lại**. Hồi chiêu chạy theo đồng hồ thật của server và được lưu lại khi phiên kết thúc (V031, xem `docs/database/BattleBall.md` 10.41). Bỏ chế độ "bấm skill khi chưa có mục tiêu chỉ diễn tư thế ở máy khách" của 2026-10-09 sáng (mọi lần dùng giờ đều qua server, nếu không đăng xuất sẽ reset được hồi chiêu). Mã: `BbTraining` (`Engage`, `DriveCombat`, `CycleFocus`), `BbVillageView.ApproachTo/VisibleDummyKeys`. Chưa kiểm trong Play mode và trên Android. Skill không cần mục tiêu (`range_px` = 0: khiên, hồi máu, buff, Thái dương hạ san, Tự phát nổ...) dùng được khi không có mục tiêu nào (người dùng yêu cầu 2026-10-09): máy khách vẫn vào phiên luyện tập (cần để server tính hồi chiêu và hiệu ứng) nhưng không đòi focus, không tự chạy tới; phiên giữ mở thêm 3 s sau mỗi lần dùng skill dù không có mục tiêu.
**Sửa 2026-10-09 (người dùng yêu cầu, CONFIRMED):** (1) **Liên hoàn của Namếc** không có hoạt ảnh vì paint 107..113 của NRO rỗng: `Char.setSkillPaint` đổi chúng thành paint 63..69 (kèm hiệu ứng khí), nên giờ Liên hoàn dùng chuỗi đòn của Kaioken (`BbSkillPoses`, nền 63). (2) **Tầm đòn đấm** ngắn lại (xem `docs/database/BattleBall.md` 10.42), AI đứng cách mục tiêu 80 px canvas thay cho 100. (3) **AI tự đánh trong tầm nhìn người chơi**: khi phiên luyện tập chạy, mỗi AI tự đi tới mục tiêu **gần nó nhất trong màn hình** (không còn đánh mục tiêu mà nhân vật điều khiển đang focus; server cũng chọn mục tiêu gần nhất của chính AI), không có mục tiêu nào trong màn hình thì đi theo nhân vật chính. AI vẫn chỉ gây sát thương khi có phiên luyện tập (phiên bắt đầu khi người chơi chọn mục tiêu hoặc dùng skill). (4) **Skill chưa học không dùng được**: thanh skill vẫn hiện đủ skill của hành tinh nhưng skill chưa học bị làm tối, ghi "chưa học", bấm thì báo "Chưa học kỹ năng này (học ở tab Kỹ năng)"; server chỉ cấp ô cho skill đã học (cả AI), nên AI cũng chỉ dùng skill đã học.
**Hình ảnh skill, giai đoạn 2a (2026-10-09, người dùng yêu cầu "thêm hiệu ứng kỹ năng/frame kỹ năng", CONFIRMED):** dữ liệu lấy từ `update_data/{effect,dart,skill}` của NRO (đọc như `GameScr.readEfect/readDart/readSkill`), đóng thành `data/skill/skill_fx.json` bởi `tools/build_assets.py` (`skill_fx()`; 359 ảnh nhỏ của hiệu ứng và đạn được thêm vào các gói tài nguyên). Mỗi frame của một paint skill có tối đa 3 hiệu ứng (danh sách ảnh nhỏ, mỗi frame 1 ảnh, đặt cách nhân vật dx, dy theo Nro px, lật khi quay trái) và một "mũi tên": mã >= 100 là **đạn** (`dart` = mã - 100). Mã: `BbSkillFx.cs` (`BbSkillFxData`, `BbSkillFx` chạy hiệu ứng theo pose của hành động, `BbDart` vẽ đạn: đầu đổi ảnh theo hướng bay và theo tick, đuôi, viền, tia lửa), `BbSkillPoses.UsePicked/Charge(out ...)` trả thêm paint đã chọn, `BbTraining.PlayPicked`. **Hiệu ứng trúng đích** (`effectHappenOnMob` của paint đã chơi, 80 nếu không có) hiện dưới chân mục tiêu khi server báo sát thương. Đạn của máy chủ (Kamejoko, Masenko, Antomic...) vẽ bằng ảnh đạn NRO thay cho vệt sáng. Ảnh kiểm tra `BattleBall/Logs/menu_training_fx_*.png`. **Giai đoạn 2b, mục 1: ba tuyệt kỹ (2026-10-09, người dùng yêu cầu làm từng mục một; CONFIRMED):** hình ảnh nằm ở `res/x4/e/e_9..e_28` của NroUnity (đã chép vào `BattleBall/SourceData/data/effect_end`, đóng vào gói `common`), cách vẽ theo `Effect_End.cs` (loại 18-26) và bảng kích thước khung `arrInfoEff`; mã `BbUltimateFx.cs`, kích hoạt từ `CAST_START` của server (cả nhân vật AI). Mọi tuyệt kỹ mở đầu bằng **khí bao quanh** (Pow) và **tia lửa gồng** (Gong) trong lúc gồng 2 s. Sau đó: **Super Kamejoko (24)**: chùm tia dài ra 20 px một nhịp (30 nhịp/giây) tới mục tiêu, giữ 1,2 s rồi mờ dần; **Cađíc liên hoàn chưởng (25)**: 15 quả cầu năng lượng cách nhau 80 ms bay tới mục tiêu (rải ngẫu nhiên quanh nó, quả cuối to) rồi nổ; **Ma phong ba (26)**: bình mở ở chân, xoáy xanh trên cao, bóng khí rơi vào bình, bình đóng và rung khoảng 4 s rồi bốc khói. Tia và cầu nhằm vào mục tiêu đang focus, không có thì mục tiêu gần nhất trong màn hình. Rút gọn so với NRO (OPEN): chưa vẽ các ngôi sao xoay quanh xoáy của Ma phong ba, mục tiêu bị nhốt chưa biến mất khỏi màn hình, chưa có âm thanh, dùng bộ hình mặc định (typePaint 0) của NRO, chưa có biến thể màu xanh/cam/rồng theo vật phẩm. Vị trí tia và tia lửa lấy cao ngang ngực (18 px Nro) thay cho chân nhân vật như số của NRO (đoán, cần kiểm khi chạy thật).
**Sửa 2026-10-09 (người dùng: skill hồi chiêu lâu chưa có hoạt ảnh, chưởng bay rời rạc; CHƯA kiểm bằng ảnh chụp vì Unity Editor đang mở):** (1) **đạn bay mượt**: máy chủ báo vị trí 20 lần/giây nên hình đạn trước đây chỉ cập nhật theo nhịp đó; nay `BbDart` vẽ mỗi khung hình theo vận tốc suy ra từ hai báo cáo gần nhất, đuôi rải đều theo quãng đường giữa hai nhịp 30 Hz, và đạn chỉ hiện ở tay nhân vật khi tới frame ném của paint (rồi đuổi kịp vị trí của máy chủ) để khớp với tư thế. (2) **skill ném mà server không có đạn** (Quả cầu kênh khí, Trói, Thôi miên, Biến Sôcôla): máy khách tự bắn đạn NRO từ tay tới mục tiêu ở frame ném (`BbDart.Fly`). (3) **Dịch chuyển tức thời**: khói (hiệu ứng 60) ở chỗ cũ và chỗ mới, nhân vật chính dịch tới cạnh mục tiêu (`BbVillageView.TeleportMain`). Vẫn OPEN: Biến hình (thân khỉ, bộ phận 192-200), Khiên và Huýt sáo không có paint trong dữ liệu NRO (chờ hiệu ứng trạng thái), nổ của Tự phát nổ chỉ là các hiệu ứng trong paint của nó.
**Chưa làm (giai đoạn 2b, OPEN):** hiệu ứng trạng thái (choáng, ngủ, khiên, trói), hiệu ứng tụ khí của Makankosappo và Quả cầu kênh khí, Biến hình (bộ phận 192 đến 200), vệt máu văng, âm thanh; chưa chạy Play mode.
Chưa làm: hiển thị khung ngoài lúc luyện tập (làng không có HP/KI từ server) và hiệu ứng nhấp nháy khi hết KI của NRO.
- **Giao thức:** `PlayerDataSnapshot.skill_catalog` (15) = `SkillCatalogEntry{skill_id, race, name, icon_id, slot, max_level, description, levels[]{level, gold_cost, damage_pct_atk, cooldown_ms, ki_cost_pct}}`. Server: `StaticData.Skill` thêm `damInfo`.
- **Kiểm tra:** server 68/68 (thêm `theSnapshotCarriesTheSkillTableForTheSkillTab`); kiểm tra mạng thật đạt (bảng 25 skill, học khi thiếu vàng bị từ chối, skill hành tinh khác bị từ chối, luyện tập); kiểm tra tải tài nguyên đạt; ảnh `Logs/menu_skills.png`, `menu_skills_end.png`, `menu_skill_popup.png`, `menu_skills_learned.png`, `menu_training_poses.png`, dãy tư thế mẫu `Logs/skill_poses_check.txt`. Chưa chạy Play mode.
- OPEN: hình dạng khỉ (part 192..200) sau khi Biến hình chưa vẽ (mới có tư thế gồng); 30 khung/giây là mặc định của NroUnity, chưa đo trên máy NRO thật.
