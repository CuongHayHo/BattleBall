# Protocol: NroUnity (client side)

> BattleBall analysis has not started yet.
> Chỉ ghi những gì đã đọc trong source. Thiếu thì ghi `UNKNOWN`.

`C:` = `E:\PRJNRO\NroUnity\Assets\Scripts\Assembly-CSharp\Game1\`

## Message structure
Source: `C:Message.cs`, `C:myReader.cs`, `C:myWriter.cs`, `C:Session_ME.cs`.

- CONFIRMED: `Message` có `sbyte command`, `myReader dis`, `myWriter dos`. Gói đọc được dựng từ `new Message(sbyte, sbyte[])`, gói ghi dựng từ `new Message(int/sbyte)`.
- CONFIRMED: `Message.readLong()` có hook `ModFunc.isReadInt` (mặc định `true`): đọc `int` thay vì `long` (`C:Message.cs:57`, `C:ModFunc.cs:41`). Đây là cờ tương thích với server gửi sát thương dạng `int`.
- UNKNOWN: `myReader`/`myWriter` có tuân thủ đúng chuẩn Java `DataInput/Output` (big-endian, modified UTF-8) không; chưa đọc hai file này.

## Header và khung gói
CONFIRMED (`Session_ME.cs` `MessageCollector.readMessage`, `doSendMessage`):

| Trường | Kích thước | Ghi chú |
|---|---|---|
| `cmd` | 1 byte có dấu | |
| `size` | 2 byte (high, low) | khi chưa có key: `(ushort)` ghi trực tiếp; sau khi có key: mỗi byte qua `writeKey` |
| `data` | `size` byte | |

- Nhận size 3 byte (`readMessage2`) khi `cmd` ∈ {`-32`, `-66`, `11`, `-67`, `-74`, `-87`, `66`, `12`}: mỗi byte `readKey(...) + 128`, ghép `((b3*256 + b2)*256 + b1)`.
- Gói gửi không có payload: ghi 2 byte `0` (có XOR nếu đã có key).

## Encoding/encryption
- CONFIRMED: XOR vòng với `key[]` theo `curR`/`curW` (kiểu `sbyte`, tăng dần, quay vòng). Chỉ bật khi `getKeyComplete == true`.
- CONFIRMED: client nhận key trong `getKey(Message)`: đọc `sbyte len`, đọc `len` byte, rồi `key[j+1] ^= key[j]` (khôi phục chuỗi XOR dây chuyền do server gửi), đặt `getKeyComplete = true`.
- CONFIRMED: ngay sau đó client đọc thêm `IP2 (UTF)`, `PORT2 (int)`, `isConnect2 (byte)` từ cùng gói; server không gửi các trường này (xem `COMPARISON.md`). Exception bị nuốt.
- Nén: không có trong lớp mạng. Mật khẩu gửi đi nằm trong payload `-29/0`, chỉ bị XOR, không băm.

## Opcode
- CONFIRMED: không có bảng opcode; số nguyên trần trong `Service.cs` (gửi) và `Controller.cs` (nhận).
- Dispatcher: `C:Controller.cs` `onMessage` (dòng 82, khoảng 4.400 dòng), sub-router `messageNotMap` (dòng 5063), `messageNotLogin` (5331), `messageSubCommand` (5374); lớp mở rộng gọi trước: `Assembly-CSharp\Game1\Assets\src\f\Controller2.cs`.

### Gửi (client → server), đã đọc trong `Service.cs`
| Opcode | Hàm | Payload |
|---|---|---|
| `-27` | `Session_ME.doConnect` | không |
| `-29/0` | `login(user, pass, version, type)` | UTF, UTF, UTF, byte |
| `-29/2` | `setClientType` | byte typeClient, byte zoom, bool, int w, int h, bool, bool, UTF "platform\|version", rồi tuỳ chọn short length + bytes từ file `/info` |
| `-7` | `charMove` | byte (1 nếu đang bay), short x, short y (xem đoạn code còn lại: UNKNOWN chi tiết) |
| `54` | `sendPlayerAttack` (chỉ mob) | danh sách mobId |
| `-60` | `sendPlayerAttack` (chỉ người) | danh sách charID |
| `-4` hoặc `67` | `sendPlayerAttack` (cả hai) | byte mobCount, mobId…, charID… |
| `34` | `selectSkill` | short skillTemplateId |
| `-45` | skill không mục tiêu | byte status |
| `-43` | dùng item | byte type, byte where, byte index, [short template] |
| `-23` | `requestChangeMap` | không |
| `-34` | `magicTree` | byte type |
| `21` | `requestChangeZone` | byte zoneId |
| `-125` | `sendClientInput` | byte n, UTF… |
| `-28/6,7,8,10,13` | `updateMap`, `updateSkill`, `updateItem`, … `clientOk` | UNKNOWN chi tiết |

### Nhận (server → client), đã xác nhận trong `Controller.cs`
| Opcode | Việc |
|---|---|
| `-27` | `getKey` (trong `MessageCollector`, không qua `Controller`) |
| `-29/2` | Link server list (`messageNotLogin`) |
| `-28/4,6,7,8,…` | Version game, dữ liệu map/skill/item (`messageNotMap`) |
| `-7` | Người khác di chuyển (int id, short x, short y) → `Char.moveTo` |
| `-60` | Kết quả đánh người |
| `-12`, `-9`, `-10`, `-11` | Mob chết, HP mob, mob đánh người |
| `-17`, `-16`, `-8` | Mình chết, hồi sinh, người khác chết |
| `-25` | Thông báo |
| `-74` | Version/tải tài nguyên |
| `-30` | Sub-command |
UNKNOWN: payload chi tiết `-9`, `-10`, `-11`.

## Important protocol flows (CONFIRMED trừ khi ghi khác)
### Kết nối và key
`Session_ME.doConnect` gửi `-27` → nhận `-27` → `getKey` → `getKeyComplete=true`.

### Đăng nhập
`LoginScr.doLogin` (`C:LoginScr.cs:351`) → `Service.login` (`-29/0`). Không có gói "login OK" riêng; INFERRED: nhận `-28/4` → tải dữ liệu qua `Rms` (`NRmap`, `NRskill`, `NRitem*`) → gửi `-28/13`.

### Di chuyển
`Char.updateChar*` → `Service.charMove` (`-7`). Client khác nhận `-7` → `Char.moveTo`.

### Tấn công
`Char.setSkillPaint` → `Service.sendPlayerAttack` (54, -60, -4/67). Kết quả nhận qua `-60`, `-12`, `-9`…

## Chưa xác định (UNKNOWN)
- Định dạng nhị phân của `NRmap`, `NRskill`, `NRitem*` (so với `DataGame.updateMap/updateSkill`, `ItemData.updateItem` phía server).
- Bảng opcode đầy đủ; opcode nào chỉ xử lý ở `Controller2`.
- Chi tiết payload của đoạn còn lại trong `charMove`.
