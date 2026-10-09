# Protocol: Teamobi2026 (server side)

> BattleBall analysis has not started yet.
> Chỉ ghi những gì đã đọc trong source. Không suy đoán format. Thiếu thì ghi `UNKNOWN`.

`S:` = `E:\PRJNRO\Teamobi2026\SRC\src\nro\models\`

## Message structure
Source: `S:network\Message.java`, `S:network\MessageSendCollect.java`.

- CONFIRMED: `Message` có `byte command` và một luồng ghi (`DataOutputStream` trên `ByteArrayOutputStream`) hoặc luồng đọc (`DataInputStream` trên `ByteArrayInputStream`).
- Serialization: các hàm `Message.writeByte/Short/Int/Long/UTF/…` uỷ quyền cho `DataOutputStream` của Java: **big-endian**, chuỗi `writeUTF` là "modified UTF-8" kèm 2 byte độ dài. CONFIRMED (code uỷ quyền trực tiếp).
- Ảnh: `Message.writeImage` ghi `int length` + bytes PNG (`ImageIO`). CONFIRMED.
- Không có schema; mỗi handler tự đọc đúng thứ tự trường. CONFIRMED.

## Header và khung gói
CONFIRMED (`MessageSendCollect.readMessage`/`doSendMessage`):

| Trường | Kích thước | Ghi chú |
|---|---|---|
| `cmd` | 1 byte có dấu | opcode |
| `size` | 2 byte big-endian | độ dài payload; hoặc **3 byte** cho một số cmd (xem dưới) |
| `data` | `size` byte | payload |

- Size 3 byte khi gửi cho `cmd` ∈ {`-32`, `-66`, `-74`, `11`, `-67`, `-87`, `66`}: ba byte `size`, `size>>8`, `size>>16`, mỗi byte qua `writeKey` rồi **trừ 128**. Xem `MessageSendCollect.doSendMessage`.
- Hướng nhận (client → server): `readMessage` **luôn đọc size 2 byte**, không có nhánh 3 byte.
- Gói không có payload (`data == null`): ghi `writeShort(0)`.

## Encoding/encryption
- CONFIRMED: XOR vòng với key. Mỗi `cmd`, mỗi byte `size`, mỗi byte `data` XOR với `KEYS[curR/curW]`; con trỏ tăng dần theo từng byte và quay vòng theo độ dài key. `curR`, `curW` nằm trong đối tượng `MessageSendCollect` (một đối tượng cho mỗi session, tạo ở `ServerManager.sessionInit`).
- CONFIRMED: chỉ bật sau khi `session.sentKey()` là true; gói key `-27` được gửi **không mã hoá**.
- CONFIRMED: key mặc định `"NRO"` (`Session.KEYS`); `Network.randomKey` bật thì sinh 7 ký tự ngẫu nhiên cho mỗi session. UNKNOWN có được bật ở đâu không.
- Nén: **không có** trong lớp mạng (UNKNOWN với dữ liệu trong `data\` đã nén sẵn hay chưa).

## Handshake key
CONFIRMED:
1. Client gửi `Message(-27)`.
2. `Collector.run` chặn `-27` và gọi `session.sendKey()`. Case `-27` trong `Controller.onMessage` không bao giờ chạy (INFERRED từ thứ tự trong `Collector`).
3. `KeyHandler.sendKey` (`S:network\KeyHandler.java`) gửi `Message(-27)` gồm: `byte length`, `byte KEYS[0]`, rồi mỗi `KEYS[i] ^ KEYS[i-1]`; gửi đồng bộ qua `session.doSendMessage` rồi `setSentKey(true)`.
4. `MyKeyHandler.sendKey` gửi thêm `DataGame.sendDataImageVersion` và `DataGame.sendVersionRes`.

## Opcode
- CONFIRMED: không có bảng opcode chính thức. `S:consts\Cmd_message.java` chỉ có vài hằng (`LOGIN=0`, `REGISTER=1`, `CLIENT_INFO=2`…) và **không được dùng trong `Controller`**. Opcode là số nguyên trần trong `switch`.
- Dispatcher: `S:server\Controller.java` `onMessage` (khoảng dòng 77–718, khoảng 99 `case`).

### Opcode cha có sub-command (CONFIRMED)
| Opcode | Router | Sub-command đã thấy |
|---|---|---|
| `-29` | `messageNotLogin` | `0` login (UTF user, UTF pass); `2` client info |
| `-28` | `messageNotMap` | `2` tạo nhân vật; `6/7/8` tải map, skill, item; `10` map template; `13` client OK |
| `-30` | `messageSubCommand` | `16` cộng điểm; `64` submenu |

### Một số opcode client → server đã xác nhận trong `Controller.onMessage`
| Opcode | Việc | Xử lý |
|---|---|---|
| `-27` | Handshake key | `Collector` (không phải `Controller`) |
| `-7` | Di chuyển (byte, short x, short y) | `PlayerService.playerMove` |
| `54` | Đánh mob (byte mobId) | `Service.attackMob` |
| `-60` | Đánh người (int playerId) | `Service.attackPlayer` |
| `-45` | Skill không mục tiêu (byte status…) | `SkillService.useSkill` |
| `34` | Chọn skill (short) | `SkillService.selectSkill` |
| `-43` | Dùng item | `UseItem.doItem` |
| `-23`, `-33` | Qua waypoint | `ChangeMapService.changeMapWaypoint` |
| `-16` | Hồi sinh | `PlayerService.hoiSinh` |
| `-15` | Về nhà | `ChangeMapService.changeMapBySpaceShip` |
| `-71` | Chat toàn server | `ChatGlobalService.chat` |
| `-74`, `-87`, `-67`, `66`, `-66`, `-32`, `11`, `-111` | Tải tài nguyên | `DataGame.*` |
| `-39` | Tải map xong | `ChangeMapService.finishLoadMap` |
| `-38` | Finish update | `Controller.finishUpdate` |

UNKNOWN: bảng đầy đủ và ý nghĩa mọi opcode còn lại.

### Một số opcode server → client đã xác nhận
| Opcode | Nội dung | Source |
|---|---|---|
| `-27` | Key | `KeyHandler.sendKey` |
| `-25` | Thông báo (UTF) | `Service.sendThongBao` |
| `-24` | Thông tin map | `Zone.mapInfo` |
| `-7` | Người khác di chuyển: int id, short x, short y | `MapService.sendPlayerMove` |
| `-60` | Kết quả đánh người: int attacker, byte skill, byte count, int target, byte, byte, int dame, bool isDie, bool crit | `SkillService.playerAttackPlayer` |
| `-12` | Mob chết: byte id, int dame, bool crit, danh sách drop | `Mob.sendMobDieAffterAttacked` |
| `-17` | Mình chết: byte (`(byte) pl.id`), short x, short y | `Service.charDie` |
| `-16` | Hồi sinh | `Service.hsChar` |
| `-29/2` | Link server list | `DataGame.sendLinkIP` |
| `122` | Chờ đăng nhập (short giây) | `Service.sendWaitToLogin` |
| `2` | Chuyển sang tạo nhân vật | `Service.switchToCreateChar` |

## Important protocol flows
### Login (CONFIRMED)
1. Client `-27` → server key `-27` (+ version tài nguyên).
2. Client `-29/2` (client info: `typeClient`, `zoomLevel`, kích thước, nền tảng|version) → server `Service.setClientType` rồi `DataGame.sendLinkIP` (`-29/2`).
3. Client `-29/0` (UTF user, UTF pass; client còn gửi thêm UTF version và byte type, server bỏ qua phần thừa) → `MySession.login` → `MrBlue.login`.
4. Có nhân vật: `DataGame.sendSmallVersion`, `sendBgItemVersion`, `sendVersionGame` (`-28/4`), `sendDataItemBG` (`-31`), `Controller.sendInfo`. Chưa có nhân vật: `Message(2)` (tạo nhân vật), client gửi `-28/2` (UTF tên, byte giới tính, byte tóc).
5. Client gửi `-28/13` (client OK) → server gửi thông tin player, map…

### Tải dữ liệu tài nguyên
`-74` (version/size/res), `-87` (update_data), `-67` (icon), `66` (ảnh theo tên), `-66` (effect), `-32` (bg item), `11` (mob template), `-28/6,7,8,10`. Nội dung đọc từ `data\` (`DataGame`, `FileIO.readFile`). UNKNOWN: định dạng nhị phân của từng loại.

### Hành động và broadcast
`Controller.onMessage` → service → sửa trạng thái → `Message` → `Service.sendMessAllPlayerInMap`/`player.sendMessage`. Xem `docs\gameplay\Teamobi2026.md`.

## Chưa xác định (UNKNOWN)
- Bảng opcode đầy đủ; payload từng lệnh ngoài các lệnh đã liệt kê.
- Định dạng nhị phân của dữ liệu tài nguyên.
- Ý nghĩa trường `byte` đầu tiên của `-17` (`(byte) pl.id`) so với client đọc vào `cPk`.
