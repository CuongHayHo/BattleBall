# Networking: Teamobi2026 vs NroUnity

> BattleBall analysis has not started yet.
> Chi tiết: `Teamobi2026.md`, `NroUnity.md` cùng thư mục; giao thức: `docs\protocol\COMPARISON.md`.

## Kết luận
CONFIRMED: hai project dùng **cùng một giao thức tầng khung gói** (cmd 1 byte + size 2 byte + payload, XOR theo key do server gửi, handshake `-27`, đăng nhập `-29/0`). Mức khớp từng opcode gameplay mới kiểm tra một phần (INFERRED).

## Bảng so sánh
| Hạng mục | Teamobi2026 (server) | NroUnity (client) |
|---|---|---|
| Socket | NIO `Selector` chỉ để accept, I/O blocking mỗi session | `TcpClient` đồng bộ trong thread riêng |
| Thread/kết nối | `Collector` + `Sender` (Sender khởi động sau handshake) | `NetworkInit` + `sender` + `MessageCollector` |
| Hàng đợi gửi | `LinkedBlockingDeque` (an toàn thread) | `List<Message>` (không khoá) |
| Hàng đợi nhận | Không; xử lý trên thread nhận | `MyVector`, chuyển sang main thread qua `Session_ME.update()` |
| Dispatch | `switch(cmd)` ở `Controller.onMessage` | `switch(msg.command)` ở `Controller.onMessage` + `Controller2` |
| Sub-command | `-29`, `-28`, `-30` | `-29`, `-28`, `-30` (cùng số) |
| Session | `Session`/`MySession` (đối tượng, có dữ liệu game) | biến tĩnh trong `Session_ME` |
| Heartbeat | Không thấy | Không thấy |
| Reconnect | Không (mỗi lần là session mới) | Không tự động |
| Lỗi | Nuốt exception, log tối đa 5 lần (`Controller`) | Nuốt exception, `Debug.Log` |
| Giới hạn | Theo IP (`Manager.MAX_PER_IP`), `AntiLogin` | Không |

## Giống nhau
- Cả hai có Sender/Collector thread; cả hai không có heartbeat; cả hai không có tự động reconnect.
- Cùng mô hình `Session` + `Message` + `Controller`.

## Khác nhau
- Server xử lý gói ngay trên thread nhận; client chuyển sang main thread.
- Hàng đợi gửi của server an toàn thread, của client thì không.
- Server có giới hạn IP và chống đoán mật khẩu; client không có phía tương ứng.

## Điểm lệch (CONFIRMED)
1. Client đọc thêm `IP2 (UTF)`, `PORT2 (int)`, `isConnect2 (byte)` trong gói key `-27`; server `KeyHandler.sendKey` không gửi. Exception bị nuốt (`Session_ME.getKey`), `isConnect2` giữ mặc định.
2. Danh sách cmd dùng size 3 byte: client có thêm `12`; server không.
3. Client gửi `-4`/`67` (đòn trúng cả mob lẫn người); server không có `case` tương ứng.

## UNKNOWN
- Timeout/heartbeat thực tế; cơ chế khoá giữa thread nhận và tick của server.
- Mục đích của `Session_ME2`.
