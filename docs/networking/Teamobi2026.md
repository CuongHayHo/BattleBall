# Networking: Teamobi2026 (server)

> BattleBall analysis has not started yet.
> Nhãn: **CONFIRMED** / **INFERRED** / **UNKNOWN**.

`S:` = `E:\PRJNRO\Teamobi2026\SRC\src\nro\models\`

## Network layer
- CONFIRMED: server TCP, Java NIO chỉ để nhận kết nối. Source: `S:network\Network.java` `run()`: thread `Network`, `Selector.select(500)`, chỉ đăng ký accept; mỗi kết nối: `setTcpNoDelay(true)`, `SessionFactory.cloneSession(MySession.class)`, `acceptHandler.sessionInit`, `SessionManager.putSession`.
- CONFIRMED: sau khi accept, I/O là **blocking** trên thread riêng của từng session (`DataInputStream`/`DataOutputStream`). Source: `S:network\Collector.java`, `Sender.java`.
- CONFIRMED: buffer socket 1 MB mỗi hướng. Source: `S:network\Session.java` (constructor).
- Cổng: đọc từ `Config.properties` (`server.port`). Source: `E:\PRJNRO\Teamobi2026\SRC\Config.properties`.

## Connection lifecycle
1. Accept (`Network.run`).
2. `ServerManager.activeServerSocket()` → `sessionInit`: kiểm tra giới hạn theo IP (`canConnectWithIp`, `Manager.MAX_PER_IP`), rồi gắn `Controller` (handler), `MessageSendCollect`, `MyKeyHandler`, và `startCollect()`. CONFIRMED.
3. Thread `Sender` chỉ khởi động sau khi gửi key (`MySession.sendKey()` → `startSend()`). CONFIRMED.
4. Đóng: `Collector.run` thoát khi có exception → `acceptHandler.sessionDisconnect` (`Client.kickSession`, `ServerManager.disconnect`) → `session.disconnect()` → `SessionManager.removeSession`. CONFIRMED.
5. Dọn khi ngắt: `Client.remove(Player)` (rời map, huỷ giao dịch, lưu DB) và ghi `last_time_logout`. Source: `S:server\Client.java`.
- **Reconnect:** không có phía server; mỗi lần nối là một `MySession` mới. Đăng nhập chồng: `MrBlue.login` kick session cũ bằng `Client.kickSession`. CONFIRMED.
- **Heartbeat/timeout đọc:** không thấy. `Collector` chặn mãi trên `readByte()`. UNKNOWN ngưỡng timeout. (`MySession.timeWait` giảm mỗi giây trong `Client.update`, ý nghĩa chưa trace.)

## Thread model
| Thread | Số lượng | Source |
|---|---|---|
| `Network` | 1 | `Network.java` |
| `Collector - IP:x` | 1/session | `Session.java` |
| `Sender - IP:x` | 1/session, chỉ chạy sau handshake | `Session.java`, `MySession.sendKey` |
| `SessionCleanup` | **UNKNOWN có được bật không** (`SessionManager.startCleanupThread` tồn tại, chưa thấy chỗ gọi) | `SessionManager.java` |
- CONFIRMED: `QueueHandler.java` (hàng đợi 500 gói, tick 33 ms) không được tham chiếu nơi nào khác: code chết.

## Session
- CONFIRMED: `Session` (key, thread, socket) và `MySession extends Session` (thông tin game: `player`, `userId`, `isAdmin`, `version`, `vnd`, `vip`, `uu/pp` tên đăng nhập và mật khẩu lưu trong session…). Source: `S:network\Session.java`, `MySession.java`.
- CONFIRMED: không có token hay session id; danh tính gắn vào đối tượng `MySession` trên kết nối TCP.
- `SessionManager` giữ danh sách `CopyOnWriteArrayList<ISession>`.

## Send flow (CONFIRMED)
```
Service.xxx() → new Message(cmd) + writer().writeXxx()
 → player.sendMessage(msg) / session.sendMessage(msg)      [Player.java, Session.java]
 → Sender.messages (LinkedBlockingDeque).add(msg)          [Sender.sendMessage]
 → thread Sender: poll → doSendMessage → MessageSendCollect.doSendMessage (XOR, ghi cmd/size/data)
```
- Vòng lặp Sender: `while(connected){ while(!empty) poll+send; sleep(10 ms) }`, độ trễ gửi tối đa khoảng 10 ms.
- Broadcast theo zone: `Service.sendMessAllPlayerInMap(zone, msg)` lặp `pl.sendMessage(msg)` cho từng người, dùng chung một `Message`; an toàn vì `getData()` tạo bản sao mảng byte trước khi XOR. Source: `S:services\Service.java`.

## Receive flow (CONFIRMED)
```
thread Collector: MessageSendCollect.readMessage (cmd, size, data; giải XOR)
 → new Message(cmd, data)
 → Collector.run: cmd == -27 ? session.sendKey() : handler.onMessage(session, msg)
 → Controller.onMessage (switch cmd) → service → Player/Zone
```
- Không có hàng đợi trung gian: handler chạy ngay trên thread Collector của session đó.
- Lỗi: `Controller.onMessage` có `try/catch` bao toàn bộ, log tối đa 5 lỗi (`errors < 5`); lỗi đọc socket bị nuốt và đóng session.

## Dispatch và Handler
- CONFIRMED: không có bảng đăng ký handler; dispatch bằng `switch (cmd)` trong `S:server\Controller.java` `onMessage` (khoảng dòng 77–718, khoảng 99 `case`).
- CONFIRMED: ba opcode cha mang sub-command ở byte đầu payload: `-29` → `messageNotLogin`, `-28` → `messageNotMap`, `-30` → `messageSubCommand`.
- CONFIRMED: không có request-id/callback; phản hồi là các gói do server chủ động gửi.
- Quy tắc chung: hầu hết `case` kiểm tra `player != null`; các lệnh tải tài nguyên (`-87`, `-67`, `66`, `-66`, `-32`, `11`) chỉ cần session.

## Authentication
`-29/0` (user, pass) → `Controller.messageNotLogin` → `MySession.login` → `MrBlue.login` → (`select … from account where username=? and password=?`, `AntiLogin`, chặn bảo trì, giới hạn người chơi, đăng nhập chồng) → `loadPlayer` → `Client.put` → `Controller.sendInfo` → `player.start()`.
- `AntiLogin`: tối đa 5 lần sai, khoá 60 giây theo IP (`S:player_system\AntiLogin.java`). CONFIRMED.
- Chi tiết bước và file: `docs\protocol\Teamobi2026.md` mục Login.

## Synchronization
- CONFIRMED: đồng bộ trạng thái bằng gói sự kiện tức thì (không snapshot/delta theo tick). Ví dụ `MapService.sendPlayerMove` (`-7`) gửi cho người khác trong zone.
- CONFIRMED: handler gói chạy trên thread Collector song song với tick của `Zone`; đồng bộ chỉ cục bộ (`Player.injured` có `synchronized`).

## Important files
| File | Vai trò |
|---|---|
| `S:network\Network.java` | Accept NIO |
| `S:network\Session.java`, `MySession.java` | Session, key, thread, trạng thái login |
| `S:network\Collector.java`, `Sender.java` | Thread nhận/gửi |
| `S:network\MessageSendCollect.java` | Khung gói và XOR |
| `S:network\KeyHandler.java`, `MyKeyHandler.java` | Gửi key sau `-27` |
| `S:network\Message.java` | Gói tin |
| `S:server\ServerManager.java` | Cài handler, giới hạn IP |
| `S:server\Controller.java` | Dispatcher |
| `S:server\Client.java` | Danh sách player online, kick |
| `S:database\MrBlue.java`, `S:player_system\AntiLogin.java` | Xác thực |
| `S:data\DataGame.java` | Gửi tài nguyên |

## UNKNOWN
- Timeout/heartbeat; `SessionCleanup` và `randomKey` có bật không.
- Cơ chế khoá giữa thread Collector và tick của `Zone`/`Player`.
