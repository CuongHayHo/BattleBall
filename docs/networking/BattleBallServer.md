# BattleBallServer: Networking Design (Proposed)

> **Cập nhật:** tài liệu này viết khi chưa biết game chơi đơn và chưa chốt công nghệ. **Nguồn sự thật cho mạng nay là `docs\networking\BattleBall.md`** (Java + Netty, TCP + TLS, Protobuf, Android); nội dung dưới đây chỉ còn tham khảo.

> **Lỗi thời thêm (2026-10-07):** "map loop"/"mỗi map một luồng" trong tài liệu này nay là **mỗi trận một tác vụ, 20 Hz, pool luồng cố định**; game chơi đơn, không có người chơi khác; Reconnect (mục 6) đã chốt hướng (b): tạm dừng ≤ 30 giây, tối đa 2 lần mỗi trận, token vào lại 30 giây; TLS đã chốt (mục 7).

> ARCHITECTURE DESIGN ONLY. `E:\PRJNRO\BattleBallServer` trống; BattleBall client chưa có lớp mạng (CONFIRMED).
> Nhãn: **CONFIRMED** / **PROPOSED** / **UNKNOWN / OPEN**.
> Tổng quan: `docs\architecture\BattleBallServer.md`. Giao thức: `docs\protocol\BattleBallServer.md`.

## 1. Bài học từ reference (CONFIRMED, `docs\networking\*.md`)
| Reference | Hệ quả | Hướng thiết kế |
|---|---|---|
| 2 thread blocking cho mỗi session | Không mở rộng; nhiều thread sửa state | I/O bất đồng bộ (PROPOSED) |
| Handler chạy trên thread nhận | Race với tick | Network chỉ decode; logic chạy trong map loop (PROPOSED) |
| Không heartbeat/timeout | Session treo | Heartbeat + idle timeout (PROPOSED) |
| Nuốt exception | Không debug được | Log có cấu trúc, đóng kết nối có lý do (PROPOSED) |
| Không reconnect | Mất trạng thái khi rớt mạng | Chính sách reconnect: OPEN |
| Giới hạn theo IP, AntiLogin | Tốt | Giữ concept (PROPOSED) |
| Hàng đợi gửi theo session | Tốt | Giữ concept (PROPOSED) |

## 2. Thành phần (PROPOSED)
| Thành phần | Trách nhiệm |
|---|---|
| Listener | Nhận kết nối mới; áp giới hạn tổng và theo IP; tạo Connection |
| Connection | Trạng thái socket, buffer đọc, outbound queue, thời điểm nhận gần nhất |
| Framing/Decoder | Tách frame theo length prefix; kiểm tra kích thước tối đa; decode thành Message |
| Encoder | Message → bytes; đưa vào outbound queue |
| Outbound queue | Theo connection, có giới hạn; nếu đầy (client chậm) thì ngắt kết nối có lý do |
| Inbound processing | Message đã decode → Dispatch → kiểm tra trạng thái session → chuyển thành command vào hàng đợi của map (hoặc vào Authentication/Session service nếu chưa vào world) |
| Dispatcher | Registry `message id → handler`, kèm trạng thái session hợp lệ cho mỗi message |
| Handler | Chuyển message thành command; **không** sửa game state trực tiếp |
| Heartbeat | Ping/pong định kỳ; đo RTT (nếu cần cho sync) |
| Timeout | Đóng connection khi quá hạn không nhận dữ liệu; giá trị: UNKNOWN |
| Rate limit | Số message mỗi giây mỗi session; vượt thì cảnh báo hoặc ngắt |

Transport (TCP/WebSocket/UDP) và thư viện mạng: **OPEN** (phụ thuộc ngôn ngữ server và target platform client, cả hai đều UNKNOWN).

## 3. Lifecycle (PROPOSED)
```
CONNECT
  Listener accept → Connection (state: CONNECTED) → bắt đầu timeout
HANDSHAKE
  Client gửi version giao thức → server chấp nhận / từ chối (version mismatch)
AUTHENTICATE
  Credentials/token → Authentication service (rate limit) → OK: state AUTHENTICATED; FAIL: lỗi + có thể đóng
CREATE SESSION
  Gắn Connection ↔ Account; nếu account đang có session khác: áp chính sách (kick cũ, như reference; hoặc từ chối: OPEN)
ENTER WORLD
  Load player (persistence, async) → chọn map → command "spawn" vào map loop → gửi trạng thái ban đầu → state IN_WORLD
PLAY
  Inbound: decode → dispatch → command vào map loop
  Outbound: map loop/Sync → encode → outbound queue → socket
  Heartbeat định kỳ
DISCONNECT
  Nguyên nhân: client đóng, timeout, lỗi giao thức, kick, server shutdown
  → state CLOSING → command "despawn" vào map → lưu player (async) → giải phóng session
```

## 4. Session state machine (PROPOSED)
| State | Message cho phép |
|---|---|
| CONNECTED | Handshake, Heartbeat |
| HANDSHAKED | Authenticate, Heartbeat |
| AUTHENTICATED | Enter world, (chọn nhân vật/costume: OPEN), Heartbeat |
| IN_WORLD | Gameplay intents, Heartbeat, Leave |
| CLOSING | không nhận thêm |
Message không đúng state → từ chối, log, (lặp lại thì ngắt).

## 5. Threading (PROPOSED)
- Network I/O: event loop bất đồng bộ (số thread nhỏ, cố định).
- Logic: mỗi map một luồng/tick loop, nhận command qua hàng đợi thread-safe.
- Persistence: worker pool riêng.
- Không có thread nào khác ngoài map loop được sửa entity của map đó.

## 6. Reconnect (OPEN)
Lựa chọn: (a) không hỗ trợ, phải đăng nhập lại (giống reference); (b) giữ entity trong thời gian ân hạn và cho phép gắn lại bằng session token. Chưa quyết định.

## 7. Security (PROPOSED / OPEN)
- Không dùng XOR key tĩnh làm "mã hoá" (điểm yếu CONFIRMED ở reference).
- TLS hoặc transport an toàn: OPEN (thời điểm áp dụng).
- Mật khẩu không gửi lại sau đăng nhập; session token có hạn (nếu dùng token): OPEN.
