# BattleBall

Game hack & slash chơi đơn cho Android: đội 3 nhân vật (1 người chơi điều khiển + 2 nhân vật AI), đánh quái dồn dập, boss theo khu vực, phó bản và hệ thống trang bị. Máy chủ giữ dữ liệu người chơi và duyệt kết quả trận đấu.

> **Trạng thái: đang phát triển, chưa chơi được một trận hoàn chỉnh.** Server đã có đủ nền tảng (tài khoản, kinh tế, đồ trang bị, GEN, trận đấu 20 Hz). Client có màn đăng nhập, làng tân thủ với các NPC (cửa hàng, nâng cấp đồ), huấn luyện với mộc nhân. Chưa có đường vào trận đấu từ giao diện.

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
- Kinh tế: học và nâng kỹ năng bằng vàng và thẻ nâng cấp (thẻ nhận từ nhiệm vụ; số thẻ tăng dần theo vị trí kỹ năng), chế tạo, thư và thông báo, mua cải trang bằng đá quý.
- Đồ trang bị: ba bộ (Thần Linh, Hủy Diệt, Thiên Sứ) cho từng hành tinh (45 món), mặc đúng hành tinh và đúng cấp, nâng cấp bằng đá, đổi mảnh lấy món, bán. Chỉ số mặc đồ cập nhật ngay cả trong phiên huấn luyện đang chạy, kể cả khi hợp thể.
- GEN: 13 bộ kích hoạt như game gốc (đủ số món cùng bộ thì kỹ năng mạnh hơn); mỗi món có nhiều dòng GEN, đổi ngẫu nhiên bằng đá GEN, mở thêm ô bằng đá mở ô.
- Hợp thể bằng Bông tai Potara; tài nguyên game gửi qua chính kết nối game (không dùng HTTP).
- Trận đấu có thẩm quyền ở server, tick 20 Hz, có phần thưởng, nhật ký trận và cho phép vào lại khi mất kết nối.
- Phiên huấn luyện với mộc nhân trong làng.
- Có 98 kiểm thử tự động. Các kiểm thử cần DB chỉ chạy khi đặt `BB_TEST_DB_URL`.

**Cơ sở dữ liệu** (`BattleBallServer/db/migrations/`): các migration đánh số `V001` đến `V039`, tự áp dụng khi server khởi động. Đã kiểm chứng trên MariaDB 10.4; chưa thử trên 10.6 trở lên.

**Web** (`BattleBallServer/web/`, `admin/`): trang đăng ký bằng PHP (có captcha) và trang quản trị chỉnh dữ liệu tĩnh. Sao chép `config.sample.php` thành `config.php` rồi điền cấu hình riêng.

**Client** (`BattleBall/Assets/`):
- `BattleNet`: tầng mạng (Protobuf, TLS, đăng ký/đăng nhập, tải tài nguyên qua kết nối game) và giao diện menu: làng tân thủ, bảng nhân vật và túi (xem skin hoặc xem đồ và đá), tab Aura, tab Kỹ năng, tab Chức năng (thư, thông báo, tài khoản).
- NPC trong làng: Ông Trưởng làng (điểm danh hằng ngày, bảng điểm danh của tháng), Ông Gôhan (nhiệm vụ: chuỗi 13 nhiệm vụ đầu và 6 nhiệm vụ hằng ngày, thưởng vàng, ngọc, đá và thẻ nâng cấp kỹ năng; mũi tên vàng chỉ tới NPC cần gặp), Urôn bán cải trang, Bunma bán đồ và đá, Bà Hạt Mít nâng cấp đồ, đổi GEN và mở ô GEN (bỏ đồ và đá vào bảng của bà), Cui (chọn phó bản; màn tàu bay vào và ra), hai mục tiêu huấn luyện. Focus tự chọn mục tiêu hoặc NPC gần nhất; nút nắm đấm nói chuyện với NPC đang focus.
- Huấn luyện với mộc nhân: kỹ năng có hình, số sát thương, thanh máu mục tiêu, khung HP/KI của đội, tự động dùng kỹ năng.
- `NroMannequin`: công cụ xem trước bộ trang phục. Hình ảnh trang phục tải từ server qua kết nối game, không nằm trong client.

## Chưa có

- Cân bằng phó bản sau khi chơi thử, bản đồ riêng cho từng phó bản, kiểm trên điện thoại thật.
- Đá chỉ số, đá pha lê và các chức năng khác của Bà Hạt Mít (ép sao, pha lê hóa) chưa có luật; GEN chưa có nguồn rơi; mảnh đồ Hủy Diệt và Thiên Sứ chưa có nơi rơi.
- Chưa thử trên điện thoại cho các phần mới (đồ, GEN, NPC).
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
