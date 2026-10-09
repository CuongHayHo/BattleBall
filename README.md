# BattleBall

Game hack & slash chơi đơn cho Android: đội 3 nhân vật (1 người chơi điều khiển + 2 nhân vật AI), đánh quái dồn dập, boss theo khu vực, phó bản và hệ thống trang bị. Máy chủ giữ dữ liệu người chơi và duyệt kết quả trận đấu.

> **Trạng thái: đang phát triển, chưa chơi được một trận hoàn chỉnh.** Server đã có đủ nền tảng (tài khoản, kinh tế, trận đấu 20 Hz). Client mới có màn đăng nhập, làng tân thủ và huấn luyện với mộc nhân. Chưa có đường vào trận đấu từ giao diện.

## Cấu trúc repo

| Thư mục | Nội dung |
|---|---|
| `BattleBall/` | Client Unity 2022.3 (URP 2D, uGUI), mục tiêu Android |
| `BattleBallServer/` | Server Java, cơ sở dữ liệu, trang web đăng ký và trang quản trị |
| `docs/` | Tài liệu thiết kế: kiến trúc, mạng, giao thức, cơ sở dữ liệu, gameplay |

## Hiện đã có

**Server** (`BattleBallServer/server/`): Java 21, Netty, TLS, Protobuf, MariaDB.
- Đăng ký, đăng nhập, đổi mật khẩu; một kho đồ dùng chung cho cả tài khoản.
- Dữ liệu tĩnh (kỹ năng, quái, boss, phó bản, vật phẩm, aura) nạp từ DB, có thể nạp lại khi đang chạy.
- Kinh tế: học và nâng kỹ năng bằng vàng, chế tạo, trang bị, hợp thể, thư và thông báo.
- Trận đấu có thẩm quyền ở server, tick 20 Hz, có phần thưởng, nhật ký trận và cho phép vào lại khi mất kết nối.
- Phiên huấn luyện với mộc nhân trong làng.
- Có kiểm thử tự động. Các kiểm thử cần DB chỉ chạy khi đặt `BB_TEST_DB_URL`.

**Cơ sở dữ liệu** (`BattleBallServer/db/migrations/`): các migration đánh số `V001`, `V002`, ... Đã kiểm chứng trên MariaDB 10.4; chưa thử trên 10.6 trở lên.

**Web** (`BattleBallServer/web/`, `admin/`): trang đăng ký bằng PHP (có captcha) và trang quản trị chỉnh dữ liệu tĩnh. Sao chép `config.sample.php` thành `config.php` rồi điền cấu hình riêng.

**Client** (`BattleBall/Assets/`):
- `BattleNet`: tầng mạng (Protobuf, TLS, đăng ký/đăng nhập) và giao diện menu: làng tân thủ, bảng nhân vật và túi đồ, tab Aura, tab Kỹ năng, tab Chức năng (thư, thông báo, tài khoản), huấn luyện với mộc nhân (đánh thường, kỹ năng, số sát thương, thanh máu mục tiêu, khung HP/KI của đội).
- `NroMannequin`: công cụ xem trước bộ trang phục. Hình ảnh trang phục được tải từ server qua HTTP, không nằm trong client.

## Chưa có

- Đường vào trận đấu từ giao diện, và lối chơi trong trận (boss, phó bản).
- Hình ảnh kỹ năng (giai đoạn 2 của phần huấn luyện).
- Máy chủ chạy thật: hiện chỉ chạy cục bộ để phát triển.

## Chạy thử cục bộ (Windows)

Cần JDK 21, Maven, MariaDB (ví dụ XAMPP) và PHP.

```bash
# Tạo cơ sở dữ liệu "battleball", rồi build server
mvn -f BattleBallServer/server/pom.xml -DskipTests package
```

`BattleBallServer/start_server.bat` khởi động MariaDB, web server (cổng 8081) và game server (cổng 7443, tự áp dụng migration mới). Các script này đang dùng đường dẫn cứng như `E:\xampp\...`, cần sửa theo máy của bạn. Cấu hình server đọc từ biến môi trường `BB_*` (xem `ServerConfig.java`); mặc định dùng DB cục bộ và một keystore TLS tự ký, tạo bằng `tools/make_dev_keystore.ps1`.

**Lưu ý về tài nguyên:** hình ảnh game (`web/assets`) được tạo bằng `tools/build_assets.py` từ thư mục dữ liệu nguồn không nằm trong repo (dung lượng khoảng 1 GB). Clone repo này chưa đủ để dựng lại gói tài nguyên.

## Tài liệu

- Thiết kế game: [`docs/gameplay/game-design-doc.md`](docs/gameplay/game-design-doc.md)
- Kiến trúc server: [`docs/architecture/BattleBallServer.md`](docs/architecture/BattleBallServer.md)
- Mạng: [`docs/networking/BattleBall.md`](docs/networking/BattleBall.md), [`docs/networking/BattleBallServer.md`](docs/networking/BattleBallServer.md)
- Giao thức client–server: [`docs/protocol/BattleBall.md`](docs/protocol/BattleBall.md)
- Mô hình dữ liệu: [`docs/database/BattleBall.md`](docs/database/BattleBall.md)

Một số tài liệu trong `docs/` được viết ở giai đoạn đầu và có thể chưa phản ánh đúng mã nguồn hiện tại. Khi có mâu thuẫn, mã nguồn là nguồn đúng.
