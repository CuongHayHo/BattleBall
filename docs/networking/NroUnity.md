# Networking: NroUnity (client)

> BattleBall analysis has not started yet.
> Nhãn: **CONFIRMED** / **INFERRED** / **UNKNOWN**.

`C:` = `E:\PRJNRO\NroUnity\Assets\Scripts\Assembly-CSharp\Game1\` (Game2 giống, khác namespace)

## Network layer
- CONFIRMED: `Session_ME` (kết nối chính) và `Session_ME2` (kết nối thứ hai), singleton tĩnh dựa trên `System.Net.Sockets.TcpClient`, `BinaryReader`/`BinaryWriter` (UTF8). Source: `C:Session_ME.cs`, `C:Session_ME2.cs`.
- Không dùng thư viện mạng bên ngoài. Không có Unity Netcode/UnityWebRequest cho gameplay (CONFIRMED qua `Packages\manifest.json` và `Session_ME.cs`).

## Connection lifecycle
1. `GameCanvas.connect()` → `Session_ME.gI().connect(GameMidlet.IP, GameMidlet.PORT)` (`C:GameCanvas.cs:900`).
2. `connect` tạo thread `NetworkInit` → `doConnect`: `TcpClient.Connect`, tạo thread sender và thread collector (`MessageCollector.run`), gửi `Message(-27)`, đặt `key = null` (`Session_ME.cs:303–358`). CONFIRMED.
3. `mSystem.onConnectOK/onConnectionFail/onDisconnected` chỉ đặt cờ tĩnh `Controller.isConnectOK/isConnectionFail/isDisconnected`. `GameCanvas.update` đọc cờ và xử lý (hiện hộp thoại, `Session_ME.gI().close()`, về màn đăng nhập) (`C:GameCanvas.cs` khoảng dòng 690–810). CONFIRMED.
4. Phân biệt lỗi: `MessageCollector.run` thoát khi `readMessage` trả null; nếu đã nối hơn 500 ms thì `onDisconnected`, ngược lại `onConnectionFail`. CONFIRMED.
- **Reconnect:** không có tự động. Người dùng đăng nhập lại qua `LoginScr`; tài khoản và mật khẩu lưu trong `Rms` (xem `docs\database\NroUnity.md`). CONFIRMED (không thấy vòng thử lại).
- Địa chỉ: `Management.IpServer` (chuỗi `a:host:port:...`) → `ServerListScreen.ListIP`; server có thể gửi lại link qua `-29/2` (`Controller.messageNotLogin`). Cách tách chuỗi thành IP/PORT: UNKNOWN (chưa đọc `ServerListScreen`).

## Thread model (CONFIRMED)
| Thread | Việc |
|---|---|
| `NetworkInit` | Kết nối |
| `sender.run` | Rút `List<Message>`, ngủ 5 ms; chỉ gửi khi `getKeyComplete` |
| `MessageCollector.run` | Đọc liên tục, `Sleep(5)` giữa các gói |
| Main thread | `Session_ME.update()` gọi từ `Main.OnGUI` xử lý hàng đợi nhận |
- Hàng đợi gửi `List<Message>` được thêm từ main thread và rút từ sender thread, **không có khoá**. Hàng đợi nhận `recieveMsg` là `MyVector`, cũng không có khoá. CONFIRMED (đọc code); hệ quả race là INFERRED.

## Session
- CONFIRMED: không có đối tượng session dữ liệu; trạng thái là các biến tĩnh (`connected`, `connecting`, `getKeyComplete`, `key`, `curR`, `curW`…).
- Danh tính người chơi không được lưu trong session mạng; sau đăng nhập server gắn danh tính vào kết nối TCP (xem `docs\networking\Teamobi2026.md`).

## Send flow (CONFIRMED)
```
Input/UI → Service.gI().xxx()            [C:Service.cs, khoảng 101 chỗ new Message(op)]
 → message.writer().writeXxx(...) → session.sendMessage(message)
 → sender.AddMessage → thread sender: doSendMessage → writeKey XOR → BinaryWriter.Write → Flush
```
- `Service.session` mặc định là `Session_ME.gI()`; một số hàm tạm đổi sang `Session_ME2` rồi đặt lại.
- Helper: `Service.messageNotLogin(sub)` = `Message(-29)` + byte sub; `messageNotMap(sub)` = `Message(-28)` + sub.

## Receive flow (CONFIRMED)
```
thread MessageCollector → readMessage (cmd, size, data, giải XOR)
 → cmd == -27: getKey;  else onRecieveMsg
 → onRecieveMsg: nếu thread hiện tại không phải "Main" → recieveMsg.addElement
 → Main.OnGUI → Session_ME.update() → Controller.onMessage(msg)
 → Controller2.readMessage(msg) (bộ opcode mở rộng, gọi trước) → switch(msg.command)
```
- `Controller.isStopReadMessage` dừng xử lý hàng đợi.
- Collector không đặt tên "Main", nên gói luôn đi qua hàng đợi (INFERRED từ `Thread.CurrentThread.Name == Main.mainThreadName`).

## Dispatch và Handler
- CONFIRMED: `Controller.onMessage` (`C:Controller.cs:82`, khoảng 4.400 dòng) là `switch (msg.command)`; sub-router `messageNotMap` (dòng 5063), `messageNotLogin` (5331), `messageSubCommand` (5374). Không có bảng đăng ký handler hay callback request/response.
- CONFIRMED: `Assets\src\f\Controller2.cs` xử lý thêm một số opcode (ví dụ `-125`, `-127`, `-126`) trước `switch` chính. Có thể có opcode bị xử lý hai lần: UNKNOWN.

## Authentication (phía client)
`LoginScr.doLogin()` (`C:LoginScr.cs:351`) → `GameCanvas.connect()` nếu chưa nối → `Service.gI().login(user, pass, GameMidlet.VERSION, type)` (gói `-29/0`). Không có gói "login OK" riêng. Server gửi dữ liệu game (INFERRED): client nhận `-28/4` (version game), tải dữ liệu, gửi `-28/13` (`clientOk`). Thất bại: server gửi thông báo.

## Synchronization
- CONFIRMED: nhân vật của mình tự di chuyển rồi gửi `-7` (`Service.charMove`); nhân vật khác được cập nhật khi nhận `-7` → `Char.moveTo` (nội suy hoặc dịch chuyển tức thì). Xem `docs\gameplay\NroUnity.md`.

## Important files
| File | Vai trò |
|---|---|
| `C:Session_ME.cs`, `Session_ME2.cs` | Socket, thread, mã hoá, hàng đợi nhận |
| `C:Message.cs`, `myReader.cs`, `myWriter.cs` | Gói và (de)serialize |
| `C:Controller.cs`, `Assets\src\f\Controller2.cs` | Dispatcher |
| `C:Service.cs` | Dựng và gửi gói |
| `C:GameCanvas.cs` | Xử lý vòng đời kết nối |
| `C:mSystem.cs` | Đặt cờ callback |
| `C:LoginScr.cs`, `ServerListScreen.cs`, `Assembly-CSharp\Management.cs` | Đăng nhập và địa chỉ |
| `C:Main.cs` | Gọi `Session_ME.update()` mỗi frame |

## UNKNOWN
- `myReader`/`myWriter` có đúng chuẩn Java `DataInput/Output` (big-endian, modified UTF-8) không.
- Mục đích thực sự của `Session_ME2`/`isConnect2`.
- Cách `ServerListScreen` tách địa chỉ.
