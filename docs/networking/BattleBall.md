# BattleBall: Networking Architecture (Design)

> **Trạng thái:** DESIGN ONLY. Chưa có code ở client lẫn server (`E:\PRJNRO\BattleBallServer` trống; BattleBall chưa có lớp mạng).
> Nhãn: **CONFIRMED** (người dùng đã chốt hoặc có evidence), **PROPOSED** (đề xuất thiết kế), **UNKNOWN**, **OPEN** (cần chốt trước implementation).
> Message, luồng và quyết định gốc: `docs\protocol\BattleBall.md` (đọc trước). Dữ liệu và persistence: `docs\database\BattleBall.md`. Bài học từ reference: `docs\networking\Teamobi2026.md`, `docs\networking\NroUnity.md`.

---

## 1. Công nghệ đã chốt (CONFIRMED)

| Hạng mục | Quyết định |
|---|---|
| Transport | **TCP** |
| Server | **Java**, khung mạng **Netty** |
| Client | Unity 2022.3, **Android (mobile)** |
| Mã hóa | **TLS ngay từ đầu** (tự ký khi phát triển, CA khi chạy thật, ghim chứng chỉ ở bản phát hành) |
| Serialization | **Protobuf** (`.proto` là nguồn duy nhất, sinh Java và C#) |
| Mô hình | Chơi đơn, cần mạng liên tục, không PvP; server mô phỏng trận **20 Hz** |
| Kết nối lại | Tạm dừng trận ≤ 30 giây (≤ 2 lần mỗi trận), token vào lại 30 giây |
| Đăng nhập chồng | Kick phiên cũ |
| Tần suất gửi | Client → server tối đa 20 gói/giây, chỉ khi có thay đổi |
| Luồng server | Pool luồng cố định chạy các trận; Netty I/O riêng; DB pool riêng |
| Giới hạn kết nối | Gói tin tối đa **256 KB**; chờ `Hello` **10 giây**, chờ đăng nhập **30 giây**; tối đa **20 kết nối mỗi IP** (mạng di động nhiều người dùng chung IP); đăng nhập sai tối đa **5 lần theo tài khoản** (không chỉ theo IP). Chỉnh được trong cấu hình |
| Lỗi DB giữa trận | **Hủy trận ngay và báo lỗi** (người dùng chọn); thưởng đã ghi bền vững trước đó vẫn giữ |
| Đăng nhập lại | **Token đăng nhập dài hạn (ví dụ 30 ngày) lưu trên máy để tự đăng nhập**, không gõ lại mật khẩu |

---

## 2. Kiến trúc tổng thể (PROPOSED)

```
Unity client (Android)                                     BattleBallServer (Java)
 UI / Input / Render                                        ┌────────────────────────────────────────────┐
   │  intents                  events/results               │ Netty I/O (boss + worker event loops)      │
   ▼                              ▲                         │  SslHandler → frame (length) →             │
 Network thread(s) ── TCP+TLS ───────────────────────────►  │  IdleStateHandler → Protobuf codec →       │
  (send queue / recv thread)                                │  SessionHandler (state check, rate limit)  │
   │                              ▲                         │        │ commands (queue)                   │
 Main-thread queue (Update)       │                         │        ▼                                    │
                                                            │ Match executor pool (20 Hz tasks)          │
                                                            │   MatchLoop: input → sim → events → flush  │
                                                            │        │ async                              │
                                                            │ Persistence pool (DB, pool > 1)            │
                                                            └────────────────────────────────────────────┘
```

---

## 3. Server: pipeline Netty (PROPOSED)

Thứ tự handler cho mỗi kết nối:
1. **`SslHandler`**: TLS.
2. **Tách và ghép khung theo độ dài** (length-prefix, u32, giới hạn kích thước tối đa cấu hình; vượt giới hạn thì đóng kết nối). Ý tưởng length-prefix lấy từ `MessageSendCollect` của Teamobi2026 (CONFIRMED trong `docs\protocol\Teamobi2026.md`), bỏ nhánh 3 byte và XOR.
3. **`IdleStateHandler`**: không đọc được dữ liệu quá **15 giây** thì xử lý như mất kết nối; heartbeat `Ping`/`Pong` mỗi **5 giây** (CONFIRMED trong `docs\protocol\BattleBall.md` mục 14.3).
4. **Protobuf decoder/encoder** cho phong bì `Envelope` (`oneof` payload; không có bảng opcode viết tay).
5. **`SessionHandler`**: kiểm tra **trạng thái session** (CONNECTED → HANDSHAKED → AUTHENTICATED → IN_MATCH → CLOSED) và **giới hạn tần suất**; message sai trạng thái thì trả `Error(INVALID_STATE)`; vi phạm lặp lại thì ngắt.
6. **Dispatcher**: tra `loại message → handler` (đăng ký bằng bảng, không dùng `switch` khổng lồ như reference).

Quy tắc cứng:
- **Luồng Netty chỉ giải mã, kiểm tra trạng thái và chuyển lệnh**; **không sửa trạng thái trận** và **không gọi DB** trên luồng này (bài học: Teamobi2026 xử lý trên luồng nhận và truy vấn DB ngay trong `Controller`).
- Lỗi phải được **ghi log có cấu trúc** (kèm mã tương quan); không nuốt exception như reference.
- Không gửi stack trace, SQL hay đường dẫn cho client.

---

## 4. Luồng thực thi (CONFIRMED mô hình, PROPOSED chi tiết)

| Thành phần | Luồng | Vai trò |
|---|---|---|
| Netty boss/worker | nhóm event loop riêng | Chấp nhận kết nối, I/O, giải mã |
| **Match executor pool** | pool cố định (số luồng cấu hình) | Mỗi **trận** là một tác vụ lặp **20 Hz** (50 ms); một luồng có thể chạy nhiều trận; **không** tạo luồng cho mỗi người chơi |
| Persistence pool | pool riêng | Mọi truy vấn DB và ghi thưởng; tác vụ trận **không chờ** DB trong tick |
| Tác vụ nền | luồng riêng | Quét token hết hạn, nhật ký bất thường (`AnomalyLog`), dọn dẹp |

**Nhịp một tick (50 ms) của trận (PROPOSED):**
1. Lấy lệnh từ hàng đợi vào (`PlayerStateReport`, `UseSkillRequest`, `SelectTargetRequest`, `SetAutoAttackRequest`, `ReviveRequest`, `SetAiTacticRequest`).
2. Mô phỏng: di chuyển mob/boss/AI đồng đội, tự đấm, đối tượng bay của skill (xem `docs\protocol\BattleBall.md` mục 6.1), hiệu ứng, kill streak.
3. Sinh sự kiện (`EntityStateUpdate`, `CombatEvent`, `RewardGranted`, `ExpGained`...).
4. **Gộp theo tick và flush một lần** xuống kênh của người chơi (giảm số lần ghi socket).
5. Khi thưởng phát sinh: đẩy tác vụ ghi bền vững sang Persistence pool; **chỉ gửi `RewardGranted` sau khi ghi thành công** (PROPOSED, để người chơi không thấy thưởng rồi bị mất); nếu ghi thất bại thì **hủy trận và báo lỗi** (xem mục 7).

Đồng bộ: chỉ **một luồng** của trận sửa trạng thái trận (không khóa trong tick); hàng đợi lệnh dùng cấu trúc an toàn cho nhiều luồng ghi một luồng đọc.

---

## 5. Vòng đời session và đăng nhập chồng (PROPOSED)

```
CONNECTED ─Hello─► HANDSHAKED ─Login/Register─► AUTHENTICATED ─StartMatch─► IN_MATCH
      ▲                                             ▲                           │
      │                                             └──── MatchEnded ◄──────────┤
      └──── (đứt kết nối) ◄── PAUSED (≤ 30 s, ≤ 2 lần/trận) ◄───────────────────┘
```
- **Một session đang hoạt động cho mỗi tài khoản** (bảng `accountId → session`, thao tác thay thế nguyên tử). Đăng nhập mới: **kick phiên cũ** bằng `DisconnectNotice(LOGGED_IN_ELSEWHERE)` (CONFIRMED); trận đang chạy của phiên cũ được xử lý như mất kết nối.
- **Đăng ký** (`RegisterRequest`) ở trạng thái `HANDSHAKED`, có giới hạn theo IP; web PHP là cửa vào thứ hai, ghi trực tiếp DB và dùng chung đặc tả tài khoản (bcrypt cost 12, mật khẩu tối đa 64 byte): `docs\database\BattleBall.md`.
- **Giới hạn kết nối (CONFIRMED):** tối đa **20 kết nối mỗi IP** (khái niệm `canConnectWithIp` của Teamobi2026, nới lỏng vì IP dùng chung trên mạng di động); đăng nhập sai tối đa **5 lần theo tài khoản** (khái niệm `AntiLogin`); gói tin tối đa **256 KB**. Giới hạn tổng số kết nối: OPEN.
- **Timeout (CONFIRMED):** không `Hello` trong **10 giây** hoặc không đăng nhập trong **30 giây** sau `Hello` thì đóng kết nối.

### 5.1 Đăng nhập lại tự động bằng token dài hạn (CONFIRMED hướng, PROPOSED chi tiết)
- Người dùng chọn: **lưu token đăng nhập dài hạn (ví dụ 30 ngày) trên máy để tự đăng nhập** (không lưu mật khẩu).
- Cách hoạt động (PROPOSED): đăng nhập bằng mật khẩu thành công thì server cấp `refreshToken` (chuỗi ngẫu nhiên đủ dài), **lưu dạng băm** trong DB cùng ngày hết hạn và thông tin thiết bị; client lưu token trong **kho khóa an toàn của Android** (Keystore/EncryptedSharedPreferences), không lưu mật khẩu. Lần sau hoặc khi đứt mạng ngoài trận, client gửi `LoginRequest` với `credential = refreshToken`; server kiểm tra băm, hạn, chưa bị thu hồi.
- **Thu hồi:** khi đổi mật khẩu, đăng xuất chủ động, nghi ngờ lộ token; mỗi tài khoản có **nhiều token (mỗi thiết bị một token)** và **hạn trượt 30 ngày** (mỗi lần dùng gia hạn thêm): **CONFIRMED (người dùng chốt 2026-10-07)**.
- Rủi ro (đã ghi nhận): ai lấy được token thì vào được tài khoản cho tới khi hết hạn hoặc bị thu hồi; giảm bằng lưu an toàn trên máy, băm ở server, thu hồi được.
- Token này **khác** `sessionToken` 30 giây dùng để vào lại trận.

---

## 6. Mất kết nối, tạm dừng và vào lại (CONFIRMED hướng, PROPOSED chi tiết)

**Server:**
1. Phát hiện đứt (kênh đóng, hoặc `IdleStateHandler` 15 giây không đọc) khi `IN_MATCH`.
2. Nếu trận còn lượt tạm dừng (**tối đa 2 lần mỗi trận**): **tạm dừng mô phỏng** trận (không tick, không hiệu ứng, boss không hồi quá mức), phát hành `sessionToken` ngắn hạn (**30 giây kể từ lúc đứt**, mỗi lần đứt cấp token mới), đặt hẹn giờ **30 giây**.
3. `ResumeSessionRequest{sessionToken}` hợp lệ (đúng token, chưa hết hạn, đúng trận) → gắn kênh mới vào trận, bỏ tạm dừng, gửi `MatchResumed` (ảnh chụp trạng thái cần thiết).
4. Hết 30 giây hoặc hết lượt tạm dừng: **hủy trận**: giữ phần thưởng và EXP theo boss đã ghi, không cộng 20%, không có `MatchEnded` cuối (CONFIRMED).
5. Khi không `IN_MATCH` (menu/kho): đóng session; client **tự đăng nhập lại bằng token đăng nhập dài hạn** (mục 5.1), không dùng `sessionToken` 30 giây (token này chỉ dành cho vào lại trận).

**Client (Android):**
- **App vào nền** (`OnApplicationPause(true)`): gửi `PauseMatchRequest` nếu đang trong trận; có thể đóng kết nối chủ động; **quay lại**: tự kết nối lại.
- **Mạng đứt:** phát hiện sau heartbeat 15 giây, hoặc lỗi ghi/đọc; **tự kết nối lại với thời gian chờ tăng dần** trong cửa sổ 30 giây, gửi `ResumeSessionRequest`; hết cửa sổ thì quay về màn hình đăng nhập kèm thông báo.
- Mọi quyết định tạm dừng hay hủy luôn do **server** (client chỉ báo).

---

## 7. Gửi, nhận và độ bền phần thưởng (PROPOSED)

**Từ client lên server:**
- Chỉ gửi `PlayerStateReport` khi có thay đổi, tối đa **20 gói/giây** (CONFIRMED), kèm `seq`; server bỏ gói vượt giới hạn hoặc `seq` cũ.
- Các request có `requestId` và (với thao tác kinh tế) `idempotencyKey`; thao tác kinh tế (ghép, nâng cấp, mua, bán/hủy ≤ 200 món mỗi lô) không thực thi trong trận.

**Từ server xuống client:**
- Gộp theo tick; chỉ gửi **thay đổi** (delta) của mob, boss, AI đồng đội; gửi sự kiện (`CombatEvent`, `RewardGranted`...) đúng thứ tự phát sinh.
- **Thứ tự và độ bền thưởng:** server ghi thưởng bền vững rồi mới báo cho client (PROPOSED); mất kết nối ngay sau khi boss chết **không** làm mất phần đã ghi (CONFIRMED trong `docs\database\BattleBall.md`). Tác vụ ghi chạy trên Persistence pool theo thứ tự. **Khi DB trục trặc và không ghi được (CONFIRMED, người dùng chọn): hủy trận ngay và báo lỗi** (`MatchEnded{result=ABORT, reason=SERVER_ERROR}`), không giữ thưởng tạm trong bộ nhớ và không có sổ ghi tạm trên đĩa; phần thưởng chưa ghi bị mất cùng trận, phần đã ghi trước đó vẫn giữ; ghi log chi tiết kèm mã tương quan. Rủi ro đã ghi nhận: lỗi DB thoáng qua có thể làm người chơi mất thưởng vừa nhận; cần giám sát DB để giảm xác suất.
- Đồ rơi khi túi đã đủ 2.000 món: `RewardRejected(BAG_FULL)`; vàng và vật liệu xếp chồng vẫn nhận.

---

## 8. Client Unity: kiến trúc mạng (PROPOSED)

| Thành phần | Mô tả |
|---|---|
| **Luồng mạng riêng** | Kết nối TCP và TLS (ví dụ `TcpClient` + `SslStream`, cần kiểm trên Unity IL2CPP/Android), luồng đọc và luồng ghi tách nhau |
| **Hàng đợi nhận về main thread** | Luồng đọc giải mã Protobuf và đẩy vào **hàng đợi an toàn đa luồng**; `Update()` lấy ra xử lý (ý tưởng `Session_ME.update()` của NroUnity, **nhưng dùng cấu trúc thread-safe**: hàng đợi gốc `MyVector` không an toàn: CONFIRMED trong `docs\networking\NroUnity.md`) |
| **Hàng đợi gửi** | Main thread đưa message vào hàng đợi an toàn; luồng ghi lấy ra; không chặn main thread (khác `Rms` chặn tới 2,5 giây ở reference) |
| **Dispatcher client** | Bảng `loại message → handler`, không `switch` 4.400 dòng như `Controller.cs` của reference |
| **Kết nối lại** | Tự động trong cửa sổ 30 giây; hàm xử lý `OnApplicationPause` (xem mục 6) |
| **Kiểm tra TLS** | Bản dev: chấp nhận chứng chỉ tự ký qua callback xác thực; bản phát hành: **ghim chứng chỉ** |
| **Dữ liệu tĩnh** | `staticDataVersion` trong `HelloAck`; tải/cập nhật qua Addressables/CDN (không đi qua kênh game) |
| Quan hệ với code hiện tại | BattleBall chưa có lớp mạng; `NroCostumeCatalog`/`NroSpriteResolver` chưa liên quan mạng và cần sửa để chạy trên Android (xem `docs\protocol\BattleBall.md` mục 14.1) |

---

## 9. Bảo mật và giới hạn (PROPOSED)
- TLS bắt buộc; không XOR tĩnh (CONFIRMED điểm yếu của reference).
- **Giới hạn kích thước khung**, **giới hạn tần suất** theo message (đã chốt 20 gói/giây cho trạng thái), **giới hạn lô** bán/hủy 200 món.
- Mật khẩu không bao giờ log; chỉ truyền trong `RegisterRequest`/`LoginRequest` qua TLS.
- Server tin vị trí và mục tiêu do client báo (CONFIRMED quyết định); chống gian lận **chỉ bằng log bất thường và xử lý thủ công** (CONFIRMED): bảng `AnomalyLog`.
- Không lộ thông tin nội bộ trong `Error`.

---

## 10. Đối chiếu với Teamobi2026 / NroUnity

| Reference (CONFIRMED) | BattleBall | Quyết định |
|---|---|---|
| NIO `Selector` chỉ nhận kết nối, 2 luồng blocking mỗi session | Netty event loop | **THAY** (không luồng mỗi session) |
| Xử lý gói trên luồng nhận, gọi DB ngay | Giải mã trên Netty; xử lý trong tick trận; DB ở pool riêng | **THAY** |
| `LinkedBlockingDeque` gửi theo session | Hàng đợi gửi theo kênh, gộp theo tick | **GIỮ Ý TƯỞNG** |
| Khung `cmd(1)+size(2)`, XOR key `"NRO"` | length u32 + Protobuf + TLS | **THAY** |
| `switch(cmd)` + sub-command | Dispatcher bảng, `oneof` | **THAY** |
| Handshake `-27` và key | `Hello`/`HelloAck` với `protocolVersion` | **THAY** |
| Không heartbeat/timeout, không reconnect | Heartbeat 5 s, timeout 15 s, tạm dừng 30 s, token vào lại | **THÊM** |
| Kick phiên cũ khi đăng nhập chồng | Kick phiên cũ | **GIỮ** |
| Giới hạn kết nối theo IP, `AntiLogin` | Giữ khái niệm, thêm rate limit message | **ĐIỀU CHỈNH** |
| Mất kết nối: lưu và dọn | Tạm dừng, vào lại, rồi hủy; thưởng đã ghi giữ nguyên | **ĐIỀU CHỈNH** |
| Hàng đợi nhận client `MyVector` không thread-safe | Hàng đợi an toàn đa luồng | **THAY** |
| `Session_ME2` kết nối thứ hai | Không dùng | **BỎ** |

---

## 11. Quyết định

### CONFIRMED
- Bảng mục 1; heartbeat 5 s và timeout 15 s; tạm dừng ≤ 30 s (≤ 2 lần); token 30 s; kick phiên cũ; 20 gói/giây từ client; lô 200 món; thưởng ghi ngay; chống gian lận chỉ bằng log; pool luồng cố định cho trận, Netty I/O riêng, DB pool riêng.

### PROPOSED
- Pipeline Netty (mục 3), nhịp tick (mục 4), vòng đời session (mục 5), chi tiết tạm dừng/vào lại (mục 6), thứ tự và độ bền thưởng (mục 7), kiến trúc client mạng (mục 8), bảo mật (mục 9).

### UNKNOWN
- Số trận đồng thời mục tiêu và kích thước pool luồng; băng thông thực tế mỗi người chơi; hành vi Protobuf (`Google.Protobuf`) trên Unity IL2CPP/Android (stripping).

### OPEN QUESTIONS
1. ~~Kích thước ảnh chụp trạng thái trong `MatchResumed`~~ **CONFIRMED (2026-10-07):** gửi **toàn bộ trạng thái trận hiện tại**.
1a. ~~Token dài hạn~~ **đã chốt** (nhiều token, hạn trượt 30 ngày). ~~Giới hạn tổng số kết nối~~ **CONFIRMED (2026-10-07): 500, chỉnh được trong config** (tăng khi có số liệu tải).
5. Chứng chỉ: **CONFIRMED (2026-10-07)** tự ký khi phát triển, **Let's Encrypt** khi chạy thật; **chưa ghim chứng chỉ** (có thể thêm sau; rủi ro đã ghi nhận: công cụ chặn gói như Fiddler/Charles có thể đọc gói nếu người dùng cài chứng chỉ riêng). Còn mở: thư viện TLS phía Unity (SslStream) chạy ổn trên Android thực tế (cần thử khi implement; chứng chỉ tự ký cần client chấp nhận riêng ở bản dev).
6. ~~Số luồng của Match executor pool~~ **CONFIRMED (2026-10-07): bằng số nhân CPU, chỉnh được trong config**; trận mới gán vào luồng đang ít việc nhất (PROPOSED cho chính sách phân bổ chi tiết).
7. ~~Hạ tầng triển khai~~ **CONFIRMED (2026-10-07):** một VPS Linux chạy server Java + MariaDB + web PHP; chứng chỉ thật Let's Encrypt. Còn mở: giám sát (metrics) và cấu hình VPS cụ thể.
