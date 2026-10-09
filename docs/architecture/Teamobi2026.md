# Teamobi2026: Architecture

> Reference project. Not part of BattleBall. BattleBall analysis has not started yet.
> Nhãn: **CONFIRMED** (đọc trực tiếp trong source), **INFERRED** (suy luận), **UNKNOWN** (chưa xác định).
> Source of truth: source code. Các file `*_TREE.md` chỉ là tham khảo.

`S:` = `E:\PRJNRO\Teamobi2026\SRC\src\nro\models\`

## Overview
- CONFIRMED: Teamobi2026 là **phía server + web** của một game kiểu Ngọc Rồng Online (NRO). Gồm 4 phần:
  1. Game server Java (NetBeans/Ant) ở `E:\PRJNRO\Teamobi2026\SRC\`.
  2. Web portal PHP thuần ở `E:\PRJNRO\Teamobi2026\WEB\`.
  3. Database MySQL/MariaDB (các file `.sql` ở thư mục gốc và `SRC\sql\nro1.sql`).
  4. Gói deploy (`DEPLOY_PACKAGE\`) và một bản build client PC thử nghiệm (`local\`, Unity IL2CPP, chỉ có binary).
- CONFIRMED: Java 17 (`SRC\nbproject\project.properties`: `javac.source/target=17`). Một project Java duy nhất, không có module con kiểu Maven/Gradle.
- CONFIRMED: 550 file `.java` trong `SRC\src\`.

## Entry points
| Entry | Source |
|---|---|
| Game server | `S:server\ServerManager.java` `main()` (dòng 85); `Main-Class` trong `SRC\manifest.mf`; chạy bằng `SRC\run.bat` / `run.sh` (`java -jar 20.jar`) |
| Web | `WEB\index.php`, `login.php`, `dashboard.php`, `admin\*`, `api\*` |

Startup flow (CONFIRMED, `ServerManager.java`, `server\Manager.java`):
1. `main` chạy thread `ServerMain` → `ServerManager.gI().run()`, rồi `activeCommandLine()`.
2. `gI()` → `init()` → `Manager.gI()`: `loadProperties()` (`Config.properties`), `loadDatabase()` (nạp bảng template), tạo NPC, `initMap()`.
3. `run()`: `activeServerSocket()` (port từ `Config.properties`), rồi bật khoảng 25 thread nền (boss, bot, minigame, Shenron, Super Rank, ĐHVT, cầu nối web, tự lưu 2 phút, cập nhật top).

## Modules
Các "module" là package trong `S:`. Không có ranh giới build giữa chúng. (CONFIRMED qua cấu trúc thư mục và import.)

| Package | Vai trò |
|---|---|
| `server\` | Khởi động, `Manager` (kho dữ liệu tĩnh), `Controller` (router gói tin), `Client` (người chơi online) |
| `network\`, `interfaces\` | Session, Message, socket NIO |
| `data\` | `LocalManager` (HikariCP), `DataGame` (gửi tài nguyên cho client) |
| `database\` | DAO: `MrBlue`, `PlayerDAO`, `ShopDAO`, `SuperRankDAO`, `HistoryTransactionDAO`… |
| `player\`, `player_system\`, `player_badges\` | `Player`, `NPoint`, `Inventory`, … |
| `map\`, `map\service\` | `Map`, `Zone`, `ItemMap`, `ChangeMapService`, `MapService` |
| `mob\`, `mob_bigboss\`, `boss\`, `Bot\` | Mob, boss (159 file), bot |
| `npc\`, `npc_list\` | NPC |
| `skill\`, `item\`, `combine\`, `shop\`, `shop_ky_gui\`, `task\`, `clan\` | Gameplay |
| `services\`, `services_func\`, `services_dungeon\` | Lớp nghiệp vụ (singleton `gI()`) |
| `matches\`, `minigame\`, `event*\`, `managers\` | Giải đấu, minigame, sự kiện |
| `consts\`, `utils\` | Hằng số, tiện ích |

## Core systems
- CONFIRMED: `Manager` giữ trạng thái tĩnh dùng chung: `MAPS`, `ITEM_TEMPLATES`, `MAP_TEMPLATES`… (`S:server\Manager.java`).
- CONFIRMED: `Zone` giữ danh sách entity của một kênh map (`S:map\Zone.java`).
- CONFIRMED: mẫu singleton `gI()` dùng thống nhất (`Controller`, `Manager`, `Network`, `Service`, `LocalManager`…).
- CONFIRMED: phụ thuộc vòng tròn giữa `network\` và `server\` (ví dụ `MySession` import `Controller`, `Client`, `MrBlue`).

## Dependencies
- CONFIRMED (`SRC\manifest.mf`, `SRC\lib\`): HikariCP 5.1.0, Gson 2.8.2, json-simple, log4j 1.2.17, Lombok, MySQL connector 5.1.23, slf4j, driver MongoDB 5.1.3.
- CONFIRMED: không thấy `import mongodb` trong source Java; MongoDB chỉ có driver trong `lib\`.
- CONFIRMED: web PHP dùng PDO, kết nối cùng DB `team2026` (`WEB\config\db.php`).

## Data flow
```
Config.properties ──► Manager.loadProperties
MySQL(team2026) ──► Manager.loadDatabase ──► *_TEMPLATES (RAM)
data/map/tile_map_data/<id> ──► Manager.initMap ──► Map ──► Zone
Client TCP ──► Network ──► MySession(Collector thread) ──► Controller.onMessage
   ──► *Service ──► Player/Zone/Mob (state in RAM) ──► Message ──► Sender thread ──► Client
Player ──► PlayerDAO.updatePlayer ──► MySQL   (mỗi 2 phút + khi ngắt kết nối)
WEB (PHP) ──► MySQL web_* tables ◄── WebBossService / WebRewardService (server poll 1–1,5 s)
```
Chi tiết: xem `docs\networking`, `docs\protocol`, `docs\database`, `docs\gameplay`.

## Important files
| File | Vai trò |
|---|---|
| `SRC\manifest.mf`, `SRC\Config.properties`, `SRC\nbproject\project.properties` | Build và cấu hình |
| `S:server\ServerManager.java` | Entry point, khởi động thread, socket |
| `S:server\Manager.java` | Nạp dữ liệu, khởi tạo map |
| `S:server\Controller.java` | Router gói tin |
| `S:network\*` | Hạ tầng mạng |
| `S:data\LocalManager.java` | Pool DB |
| `S:player\Player.java`, `S:map\Zone.java`, `S:mob\Mob.java` | Thực thể và thế giới |
| `SRC\sql\nro1.sql` | Schema 41 bảng |

## Confirmed / Inferred / Unknown
- CONFIRMED: tất cả mục trên có nguồn trích kèm.
- INFERRED: mô hình server-authoritative (xem `docs\gameplay\Teamobi2026.md`).
- UNKNOWN:
  - `SessionManager.startCleanupThread` và `Network.randomKey` có được bật ở đâu không.
  - Ai chạy `Map.run()`.
  - Dump SQL nào đang chạy thật (5 file không đồng nhất: 41, 41, 48, 48, tối đa 53 bảng).
  - Nội dung thư mục `SRC\Assets` (rỗng) và vai trò của `SRC\data\` do công cụ nào sinh ra.

## Discrepancies với `Teamobi2026_TREE.md`
- `SRC\Assets\` rỗng (báo cáo liệt kê nhưng không nói rỗng).
- MongoDB chỉ có driver trong `lib\`, không dùng trong source Java.
- `DEPLOY_PACKAGE\` còn có thêm `DATABASE_team2026.sql`, `DEPLOY_TO_*_VPS.txt`.
