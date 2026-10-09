# Database: Teamobi2026

> BattleBall analysis has not started yet.
> Chỉ đọc **schema**, không đọc dữ liệu hàng trong các file `.sql`. Không ghi lại mật khẩu hay khoá.
> Nhãn: **CONFIRMED** / **INFERRED** / **UNKNOWN**.

`S:` = `E:\PRJNRO\Teamobi2026\SRC\src\nro\models\`

## Technology
- CONFIRMED: MySQL/MariaDB, tên DB `team2026` (`E:\PRJNRO\Teamobi2026\SRC\Config.properties`; dump ghi `MariaDB 10.4.32`, `E:\PRJNRO\Teamobi2026\team2026_full_database.sql`).
- CONFIRMED: JDBC thuần + HikariCP 5.1.0. **Không có ORM.** Driver MongoDB có trong `SRC\lib\` nhưng source Java không dùng (không có `import mongodb`).

## Connection
- CONFIRMED: `S:data\LocalManager.java` tạo `HikariDataSource` trong khối `static`, đọc `Config.properties` (driver, host, port, tên DB, user, mật khẩu, min/max, lifetime). URL: `jdbc:mysql://host:port/db?useUnicode=yes&characterEncoding=UTF-8`; bật cache prepared statement.
- CONFIRMED: `database.min/max` trong `Config.properties` đều là 1, nên pool có một kết nối.
- CONFIRMED: web PHP kết nối cùng DB bằng PDO (`E:\PRJNRO\Teamobi2026\WEB\config\db.php`).
- CONFIRMED: `S:server\ServerManager.java` `resetNhanQuaHangNgay` dùng URL JDBC cứng sang DB tên `ngocrong` (khác `team2026`) với tài khoản `root` và mật khẩu rỗng.

## Data access
Ba kiểu cùng tồn tại (CONFIRMED):
1. `LocalManager.executeQuery/executeUpdate(sql, params...)`: prepared statement, tự đóng kết nối; `executeQuery` trả `ResultSetImpl` đã sao chép dữ liệu.
2. `LocalManager.getConnection()` rồi `prepareStatement` thủ công (ví dụ `Manager.loadDatabase`, `Clan.java`).
3. DAO tĩnh trong `S:database\`: `MrBlue` (đăng nhập, `loadPlayer`, `loadById`), `PlayerDAO` (`createNewPlayer`, `updatePlayer`, trừ tiền tài khoản), `ShopDAO`, `SuperRankDAO`, `HistoryTransactionDAO`, `TraningDAO`, `EventDAO`.
- `Clan` tự truy cập DB (`S:clan\Clan.java`, `S:services\ClanService.java`), không qua DAO.
- SQL là chuỗi nối tay; đa số có tham số `?`. `MrBlue.login` ghép timestamp và IP vào một câu `update account set last_time_login = '…'`. CONFIRMED.

## Transaction
- CONFIRMED: gần như không có ở server Java (`setAutoCommit/commit/rollback/addBatch` chỉ khớp 4 lần trong `WebBossService.java` và `ClanService.java`). Mỗi `executeUpdate` tự commit.
- CONFIRMED: lưu player là **một câu `UPDATE player` (62 cột)** nên có tính nguyên tử cho riêng bảng đó (`PlayerDAO.updatePlayer`).
- CONFIRMED: web PHP dùng `beginTransaction` khi tạo tài khoản game (`WEB\dashboard.php`).

## Migration/schema
- CONFIRMED: không có framework migration. Schema là file dump:
  - `SRC\sql\nro1.sql` (41 bảng), `database team2026.sql` (41), `team2026_full_database.sql` và `DEPLOY_PACKAGE\DATABASE_team2026.sql` (48; thêm 7 bảng `web_*`), `clean_utf8_database.sql` (đếm được tối đa 53, số chính xác UNKNOWN).
- CONFIRMED: bảng `web_*` còn được tạo bằng `CREATE TABLE IF NOT EXISTS` trong code: `WEB\database_migrate.php` (5 bảng) và `S:services\WebBossService.java` (6 bảng).
- CONFIRMED: dump có `PRIMARY KEY` và vài `UNIQUE KEY` (`account.username`, `payments.refNo`, `bank_transfers.transaction_id`, `account_id`…). Có 5 chỗ khớp `FOREIGN KEY/REFERENCES` trong `nro1.sql`; thuộc bảng nào: UNKNOWN. Quan hệ chính là quy ước của ứng dụng.

## Entities
**Dữ liệu người chơi (động)**
- `account` (34 cột): `username` (UNIQUE), `password`, `ban`, `is_admin`, `last_time_login/logout`, `ip_address`, `vnd`, `tongnap`, `vip`, `thoi_vang`, `vang`, `luotquay`, `event_point`, `token`, `xsrf_token`, `newpass`…
- `player` (khoảng 75 cột): `account_id` (UNIQUE), `name`, `head`, `gender`, `clan_id`; cùng nhiều cột `data_*`/`items_*` kiểu `text` chứa JSON.
- `clan` (`members`, `tops` là text), `super_rank`, `shop_ky_gui`, `history_transaction`, `giftcode`, `napthe`, `payments`, `bank_transfers`, `achievement_template`, `notify`.

**Dữ liệu tĩnh/template**: `item_template`, `item_option_template`, `map_template`, `mob_template`, `npc_template`, `skill_template`, `task_*_template`, `side_task_template`, `clan_task_template`, `shop`, `tab_shop`, `item_shop`, `item_shop_option`, `intrinsic`, `flag_bag`, `radar`, `part`, `bg_item_template`, `head_avatar`, `array_head_2_frames`, `img_by_name`, `data_badges`.

**Cấu hình site**: `settings`, `adminpanel` (có các cột nhạy cảm; không đọc dữ liệu).

**Không có** bảng riêng cho item của người chơi, túi đồ, skill của người chơi, nhiệm vụ của người chơi, bạn bè, session (CONFIRMED qua danh sách bảng).

### Định dạng lưu của `player` (CONFIRMED, `S:database\PlayerDAO.java`, `S:database\MrBlue.java`)
- `data_inventory`: JSON mảng `[gold, gem, ruby, coupon, event]`; `data_location`: `[mapId, x, y]`; `data_point`: mảng chỉ số kèm hp, mp. Ý nghĩa theo vị trí, không có tên trường.
- `items_body`, `items_bag`, `items_box`, … : mảng JSON; mỗi phần tử là một chuỗi JSON `[templateId, quantity, "[[optId,param],…]", createTime]`; ô trống là `[-1, 0, "[]", createTime]`. Parser `json-simple`; khi đọc phải gỡ `\"` bằng `replaceAll`.
- Mọi cột `data_*` khác: UNKNOWN chi tiết (chỉ trace vài nhóm).

## Persistence flow
**Khởi động (DB → RAM)**: `Manager.loadDatabase()` (`S:server\Manager.java:288`) đọc các bảng template vào `Manager.*_TEMPLATES`. Cũng **ghi file** `data/update_data/part` từ bảng `part` (`Manager.java:270, 314`). `initMap()` dựng `Map`/`Zone` từ `map_template` và file `data/map/tile_map_data/<id>`.

**Đăng nhập (DB → Player)**: `MrBlue.login` → `select … from account where username=? and password=?` → `select * from player where account_id=? limit 1` → `loadPlayer(rs)` parse từng cột JSON → `Player`/`Inventory`/`Item` (`ItemService.createNewItem(templateId, quantity)`).

**Lưu (Player → DB)**: `PlayerDAO.updatePlayer` tạo JSON từng nhóm rồi chạy một câu `update player set … where id = ?` qua `LocalManager.executeUpdate`. Gọi khi:
- thread `AutoSaveAllPlayers` (mỗi 2 phút, `ServerManager.run`);
- ngắt kết nối (`Client.remove(Player)`).
Người chơi đang chết hoặc đứng ở map không được phép vào thì vị trí lưu bị thay bằng nhà (`mapId = gender + 21`, x=300, y=336).

**Khác**: clan lưu khi thay đổi (`Clan.java:344,403,433`, `ClanService.java:986`); `HistoryTransactionDAO.insert` ghi sau giao dịch; `TraningDAO.updatePlayer` cập nhật riêng `data_luyentap`.

**Tạo nhân vật**: `Controller.createChar` kiểm tra tên rồi `PlayerDAO.createNewPlayer` chèn một hàng `player` với JSON mặc định (map `39 + gender`).

## Quan hệ dữ liệu
```
account ──(account_id)──► player ──(clan_id)──► clan(members JSON)
 │                         ├─ items_body/bag/box (JSON) ──► item_template / item_option_template (theo id, không FK)
 │                         ├─ skills, pet, data_task, friends… (JSON)
 │                         ├─ super_rank.player_id
 │                         └─ shop_ky_gui.player_id
web_users ──web_game_accounts──► account
map_template ──(mobs/npcs/waypoints JSON)──► mob_template / npc_template
```
- CONFIRMED: một tài khoản chỉ có một nhân vật (`select … from player where account_id=? limit 1`; `UNIQUE KEY account_id`, gắn với `player` là INFERRED).

## Web ↔ server qua DB (CONFIRMED)
- Server **poll** các bảng `web_*`: `WebBossService` (thread, `sleep(1000)`) đọc `web_boss_actions WHERE status='pending'`, `web_boss_queue`, `web_boss_config`, `web_boss_auto_spawn`, ghi `web_live_bosses`; `WebRewardService` (1,5 giây) đọc `web_reward_queue`.
- Web tạo và sửa trực tiếp bảng `account` (`INSERT INTO account`, `UPDATE account SET password`) (`WEB\dashboard.php`, `WEB\admin\users.php`).

## Vấn đề ghi nhận (chỉ ghi lại, không sửa)
- CONFIRMED (code): mật khẩu tài khoản game lưu **dạng rõ**: `MrBlue.login` so `password = ?` với giá trị client gửi; `WEB\dashboard.php` so `$gAcc['password'] !== $oldPass`. Ngược lại `web_users` dùng `password_hash/password_verify` (`WEB\config\auth.php`). Nội dung dữ liệu trong dump không được kiểm tra.
- CONFIRMED: `WEB\database_migrate.php` tạo tài khoản quản trị với mật khẩu mặc định ghi sẵn trong mã.
- INFERRED: pool một kết nối nên mọi truy vấn dùng chung; lưu toàn bộ người chơi tuần tự trên một thread có thể chậm khi đông người.
- INFERRED: cấu trúc JSON theo vị trí nên thêm trường mới phải sửa đồng bộ ở `createNewPlayer`, `updatePlayer`, `loadPlayer`.

## Important files
| File | Vai trò |
|---|---|
| `S:data\LocalManager.java` | Pool và helper truy vấn |
| `S:database\MrBlue.java`, `PlayerDAO.java` | Đăng nhập, load/lưu player |
| `S:server\Manager.java` | `loadDatabase`, `initMap` |
| `S:server\Client.java` | Lưu khi ngắt kết nối |
| `S:clan\Clan.java`, `S:services\ClanService.java` | Lưu clan |
| `S:services\WebBossService.java`, `WebRewardService.java` | Cầu nối web ↔ server |
| `E:\PRJNRO\Teamobi2026\SRC\sql\nro1.sql`, `team2026_full_database.sql` | Schema |
| `WEB\config\db.php`, `WEB\database_migrate.php`, `WEB\dashboard.php` | Phía web |

## UNKNOWN
- Dump SQL nào đang chạy thật; 5 chỗ `FOREIGN KEY` thuộc bảng nào.
- Ý nghĩa đầy đủ từng cột `data_*` của `player`.
- Mật khẩu trong dữ liệu có băm hay không (kết luận "dạng rõ" dựa trên code).
- Cách sinh các file `data/update_data/*` ngoài `part`.
