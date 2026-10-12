# PROMPT CHO ĐỘI AGENT (Antigravity, Teamwork Preview): PHÂN TÍCH DỰ ÁN BATTLEBALL VÀ VIẾT BÁO CÁO ĐỒ ÁN

> Cách dùng: mở repo `BattleBall` (https://github.com/CuongHayHo/BattleBall) làm workspace trong Antigravity, bật chế độ nhiều agent, dán TOÀN BỘ nội dung từ mục "0. VAI TRÒ VÀ MỤC TIÊU" trở xuống làm yêu cầu cho đội. Tác giả dự án trả lời các câu hỏi ở mục 13 khi đội hỏi.

---

## 0. VAI TRÒ VÀ MỤC TIÊU

Bạn là một đội gồm nhiều agent chuyên môn, cùng **phân tích toàn bộ mã nguồn và tài liệu của dự án BattleBall, rồi viết một báo cáo đồ án hoàn chỉnh bằng tiếng Việt** để tác giả nộp cho nhà trường.

Báo cáo phải:
1. **Đúng sự thật**: mọi khẳng định kỹ thuật có bằng chứng trong mã nguồn hoặc tài liệu của repo (`đường_dẫn_file:số_dòng` hoặc `tài liệu §mục`).
2. **Đầy đủ** về: thể loại game, cơ chế chơi, nhân vật (chủng tộc, đồng đội AI, NPC, quái, boss, trang phục, aura, hợp thể), map (làng tân thủ, sân phó bản, nền, thời tiết, chuyển cảnh), hiệu ứng kỹ năng và âm thanh, kiến trúc client, kiến trúc server, cơ sở dữ liệu, giao thức mạng, bảo mật, kiểm thử, triển khai.
3. Đủ **bố cục báo cáo đồ án** (mục 8), có sơ đồ Mermaid, bảng biểu, đánh giá trung thực ưu điểm và hạn chế.
4. **Dễ đọc** với giảng viên không chơi game này: mọi thuật ngữ game (Ki, GEN, hợp thể, phó bản, aura, tuyệt kỹ...) có trong bảng thuật ngữ.

Đội **chỉ đọc và phân tích**. Không sửa, xóa, đổi tên file nào của repo. Sản phẩm viết vào thư mục mới `report/`. Không build, không chạy game, không cài gói, không kết nối máy chủ nào, trừ khi tác giả cho phép (mục 13).

---

## 1. BỐI CẢNH DỰ ÁN (điểm xuất phát; vẫn phải đối chiếu với mã nguồn)

**BattleBall** là game **hack & slash chơi đơn cho Android, cần kết nối mạng liên tục**, phong cách 2D pixel art:
- Mỗi tài khoản có **3 nhân vật** thuộc 3 chủng tộc (Trái Đất, Namec, Xayda). Mỗi trận là đội 3 người: 1 nhân vật do người chơi điều khiển, 2 nhân vật AI tự đánh; có thể đổi nhân vật điều khiển giữa trận. **Hợp thể**: 2 nhân vật thành 1 (cộng chỉ số, dùng bộ kỹ năng của nhân vật chủ) bằng Bông tai Potara.
- Chiến đấu 2D: ở làng có trọng lực (đi, nhảy, bay), ở phó bản bay tự do mọi hướng. Quái nhỏ ra từng đợt, boss có bộ chiêu theo hành tinh với vùng cảnh báo đỏ, boss khỉ tiến hóa 7 dạng.
- **Server có thẩm quyền**: mô phỏng trận 20 Hz (tick 50 ms), quyết định sát thương, thưởng, nhật ký trận, cho vào lại trận khi rớt mạng.
- **Làng tân thủ** có NPC: Trưởng làng (điểm danh), Ông Gôhan (nhiệm vụ), Urôn (bán cải trang), Bunma (bán đồ và đá), Bà Hạt Mít (nâng cấp, GEN, ép sao, pha lê hóa), Cui (chọn phó bản), cùng hai mục tiêu huấn luyện (Mộc nhân, Saibamen). **13 phó bản** x **3 độ khó**, trang bị 3 bộ x 3 hành tinh (45 món), **GEN** (13 bộ kích hoạt), các loại đá, aura theo cấp, hòm thư, thông báo, quà tân thủ.
- Quà và sự kiện: quà tân thủ có công tắc bật/tắt, **chiến dịch quà cho tài khoản mới** có hạn kết thúc, **thư có hạn nhận** (quá hạn mất quà), **túi kinh nghiệm** (dùng một lần, cả ba nhân vật lên cấp tối đa), thẻ nâng cấp kỹ năng.
- Một phần dữ liệu (kỹ năng, hình ảnh, âm thanh, bản đồ làng) có nguồn từ bên ngoài; cách ghi nhận xem mục 10 (chỉ ghi một lần, ở Tài liệu tham khảo).

**Công nghệ (đối chiếu lại):**

| Thành phần | Công nghệ | Vị trí |
|---|---|---|
| Client | Unity 2022.3, URP 2D, uGUI (nhân vật, map, hiệu ứng đều vẽ bằng UI Image), C#, mục tiêu Android | `BattleBall/` |
| Server | Java 21, Netty, TLS, Protobuf, HikariCP, MariaDB, Maven | `BattleBallServer/server/` |
| Giao thức | Protobuf qua TCP + TLS, một file định nghĩa chung | `BattleBallServer/proto/battleball.proto` |
| Cơ sở dữ liệu | MariaDB, migration đánh số `V001`... tự áp dụng khi server khởi động | `BattleBallServer/db/migrations/` |
| Web | PHP: trang đăng ký (captcha), trang quản trị | `BattleBallServer/web/`, `BattleBallServer/admin/` |
| Công cụ | Python tạo gói tài nguyên, bản đồ, SQL; PowerShell/Bash chạy và triển khai | `BattleBallServer/tools/`, `BattleBall/SourceData/tools/` (nếu có trong repo) |
| Kiểm thử | JUnit; test cần DB chỉ chạy khi có biến môi trường `BB_TEST_DB_URL` | `BattleBallServer/server/src/test/` |
| Triển khai | VPS Ubuntu: service systemd, Nginx + PHP-FPM, chứng chỉ Let's Encrypt, firewall, script triển khai một lệnh có sao lưu DB | `BattleBallServer/tools/deploy.sh` |

**Quy mô gần đúng (BẮT BUỘC đếm lại):** khoảng 54 file Java (khoảng 10.600 dòng ở mã chính), khoảng 62 file C# không tính file sinh tự động, 10 file PHP, 12 file test (125 test, tất cả đạt khi chạy với cơ sở dữ liệu thật ngày 12/10/2026), 51 migration (`V001` đến `V051`), file `.proto` khoảng 945 dòng, khoảng 18 commit từ 09/10/2026. File lớn nhất: `MatchEngine.java` (khoảng 2.650 dòng), `BbTraining.cs` (khoảng 2.260 dòng), `EconomyService.java` (khoảng 1.290 dòng), `BbVillageView.cs` (khoảng 1.020 dòng), `BbSkillFx.cs` (khoảng 940 dòng).

**Hình ảnh thật và dữ liệu cho báo cáo (CÓ TRONG REPO):**
- `docs/report_media/screenshots/`: **138 ảnh chụp thật** của game (làng, NPC, bảng nhân vật, túi, skin, aura, hợp thể, trang bị, GEN, cửa hàng, kỹ năng, huấn luyện, từng khung hiệu ứng kỹ năng và tuyệt kỹ, thời tiết, hòm thư, điểm danh, nhiệm vụ, game chạy trên Android). Mục lục chia nhóm: `docs/report_media/README.md`. **Hãy xem ảnh** khi mô tả nhân vật, map, hiệu ứng, giao diện, và **chèn ảnh vào báo cáo** bằng đường dẫn tương đối, có số hình và chú thích.
- `BattleBall/SourceData/data/skill/skill_fx.json`, `skill_poses.json`: bảng hiệu ứng và tư thế của từng kỹ năng mà client dùng; `BattleBall/SourceData/data/map/village_start/chunk.json`: bố cục bản đồ làng (địa hình, cảnh vật, vị trí).
- Phần còn lại của tài nguyên (khoảng 950 MB, 167 nghìn file hình, âm thanh) không nằm trong repo; gói tài nguyên do `build_assets.py` tạo và **server gửi cho client qua chính kết nối game** khi đăng nhập.
- **Chưa có ảnh trong phó bản** (sân bay, đợt quái, boss, màn tàu bay): chỗ đó để khung "[CẦN ẢNH: ...]" cho tác giả chèn.

---

## 2. NGUỒN SỰ THẬT VÀ QUY TẮC BẰNG CHỨNG (BẮT BUỘC)

1. **Mã nguồn là nguồn đúng nhất.** Tài liệu `docs/` được viết dần, một số phần đã ghi "lỗi thời" hoặc "đề xuất". Khi mâu thuẫn: tin mã nguồn và ghi vào Phụ lục "Sai lệch giữa tài liệu và mã nguồn".
2. Mỗi khẳng định kỹ thuật kèm bằng chứng `đường_dẫn:dòng` hoặc `tài liệu §mục`. Không có bằng chứng thì không viết như sự thật.
3. Ba nhãn dùng trong toàn báo cáo: **ĐÃ XÁC MINH** (thấy trong mã), **SUY LUẬN** (hợp lý từ cấu trúc, nêu lý do), **KHÔNG XÁC ĐỊNH**. Không bịa kiến trúc, số liệu, kết quả đo, tên thư viện, quan hệ.
4. Tài liệu của tác giả dùng nhãn CONFIRMED (đã chốt/đã xác minh), PROPOSED (đề xuất), OPEN (còn mở). Số liệu PROPOSED phải được nêu là "đề xuất, chưa kiểm chứng bằng chơi thử thật".
5. Số liệu đếm phải **tự đếm lại** bằng lệnh chỉ đọc và ghi cách đếm. Không chép số trong prompt này mà không kiểm tra.
6. Không đưa kết quả hiệu năng, số người chơi, số liệu đo nếu repo không có. "Chưa đo" là một hạn chế cần nêu.
7. Lớp và file trong repo có tóm tắt `/// <summary>` (C#) hoặc `/** ... */` (Java) ghi mục đích và nguồn gốc quyết định; đây là bằng chứng tốt, nhưng vẫn kiểm tra phần thân hàm khi mô tả cơ chế.

---

## 3. TỔ CHỨC ĐỘI AGENT

Có 8 agent. Điều phối giao việc, agent 1 đến 5 và 7 làm song song ở Giai đoạn 2, agent 6 phản biện ở Giai đoạn 4.

| # | Agent | Nhiệm vụ chính | Đọc chủ yếu | Sản phẩm |
|---|---|---|---|---|
| 0 | **Điều phối (Lead)** | Lập kế hoạch, chia việc, giữ bảng thuật ngữ và bảng bằng chứng chung, hợp nhất, giải quyết xung đột, gom câu hỏi cho tác giả | toàn repo, `README.md` | `00_ke_hoach.md`, `00_thuat_ngu.md`, bản hợp nhất |
| 1 | **Server** | Mục 7.8 | `BattleBallServer/server/src/main/java/**`, `docs/architecture/BattleBallServer.md`, `docs/networking/BattleBallServer.md` | `01_server.md` |
| 2 | **Client** | Mục 7.7 | `BattleBall/Assets/BattleNet/**` (trừ phần của agent 7), scene, `Editor/` | `02_client.md` |
| 3 | **Dữ liệu và giao thức** | Mục 7.9, 7.10 | `db/migrations/**`, `proto/battleball.proto`, `docs/database/BattleBall.md`, `docs/protocol/BattleBall.md` | `03_du_lieu_giao_thuc.md` |
| 4 | **Thể loại và cơ chế game** | Mục 7.1, 7.2 | `docs/gameplay/game-design-doc.md`, `game/*.java`, `game/match/*.java`, migration dữ liệu | `04_the_loai_co_che.md` |
| 5 | **Hạ tầng, bảo mật, kiểm thử** | Mục 7.11, 7.12 | `server/src/test/**`, `web/`, `admin/`, `tools/`, `docs/architecture/BattleBallServer.md` §16 | `05_ha_tang_bao_mat_kiem_thu.md` |
| 7 | **Nhân vật, map và hiệu ứng** | Mục 7.3, 7.4, 7.5, 7.6 | `Bb{CharacterView,CostumeAvatar,MobView,SkillFx,SkillPoses,UltimateFx,FusionFx,Sfx,AuraAssets,VillageView*,ChunkMaps,MapChunk,Weather,SpaceBackground,Transport,Joystick}.cs`, `BattleBall/Assets/NroMannequin/`, `tools/build_assets.py`, `tools/build_village_map.py`, phần boss/quái trong `MatchEngine.java`, `Entities.java` | `07_nhan_vat_map_hieu_ung.md` |
| 6 | **Biên tập và phản biện (QA)** | Kiểm tra bằng chứng từng khẳng định; mâu thuẫn số liệu; thông tin nhạy cảm (mục 9); nguồn gốc dữ liệu chỉ ghi một lần ở Tài liệu tham khảo (mục 10); văn phong, bố cục, sơ đồ; chặn bản nháp chưa đạt checklist mục 11 | toàn bộ sản phẩm | `06_ra_soat.md` |

Phối hợp:
- Cần thông tin ngoài phạm vi thì **hỏi agent phụ trách**, không tự đoán. Ví dụ: agent 7 cần biết server gửi vị trí boss thế nào thì hỏi agent 1 và 3.
- Thuật ngữ mới vào `00_thuat_ngu.md` ngay, cả đội dùng một cách gọi (ví dụ thống nhất "tuyệt kỹ" cho ultimate, "phó bản" cho dungeon).
- Xung đột số liệu do Điều phối quyết định bằng cách đọc lại mã nguồn và ghi vào Phụ lục sai lệch.

---

## 4. QUY TRÌNH LÀM VIỆC

**Giai đoạn 1: Khảo sát (Điều phối).** Đọc `README.md`, `docs/gameplay/game-design-doc.md`, `docs/architecture/BattleBallServer.md` (§1 đến §4, §15, §16). Liệt kê cây thư mục hai cấp, đếm file theo loại, xác định điểm vào: `Main.java` (server), scene `Login.unity` và `Menu.unity`, `BbLoginUi.cs`, `BbClient.cs`, `BbVillageView.cs`. Xem `git log` để dựng dòng thời gian phát triển. Viết `00_ke_hoach.md`.

**Giai đoạn 2: Phân tích song song (agent 1 đến 5 và 7)** theo mẫu mục 5.

**Giai đoạn 3: Hợp nhất (Điều phối)** theo bố cục mục 8; thống nhất thuật ngữ; vẽ sơ đồ tổng thể; viết mở đầu và kết luận.

**Giai đoạn 4: Phản biện (agent 6)** chạy checklist mục 11; vòng sửa đến khi đạt hoặc ghi rõ lỗi tồn đọng.

**Giai đoạn 5: Hoàn thiện**: báo cáo cuối dạng Markdown (thêm DOCX/PDF nếu công cụ hỗ trợ), mục lục, danh mục hình/bảng, phụ lục.

---

## 5. MẪU SẢN PHẨM CỦA MỖI AGENT PHÂN TÍCH

1. **Phạm vi và file đã đọc** (kể cả file bỏ qua và lý do).
2. **Mô tả cấu trúc**: thành phần, trách nhiệm, quan hệ; ít nhất một sơ đồ Mermaid.
3. **Cơ chế hoạt động** theo luồng cụ thể (ví dụ "từ lúc bấm kỹ năng đến khi số sát thương bay lên").
4. **Quyết định thiết kế và lý do** (lấy từ tài liệu và chú thích mã, kèm nhãn CONFIRMED/PROPOSED).
5. **Bảng bằng chứng**: `khẳng định | file:dòng | nhãn`.
6. **Hạn chế, rủi ro, việc chưa làm**.
7. **Câu hỏi cho tác giả** và **vị trí cần ảnh chụp màn hình**.

---

## 6. NHỮNG GÌ PHẢI CÓ TRONG MỖI MÔ TẢ CƠ CHẾ

Với mỗi cơ chế (kỹ năng, phó bản, GEN, map...), ghi đủ:
- **Mục đích** với người chơi.
- **Dữ liệu** (bảng/trường DB, file JSON tài nguyên, message Protobuf).
- **Phía nào quyết định** (server tính, client chỉ hiển thị; hay client tự làm).
- **Luồng** (yêu cầu → xử lý → phản hồi → hiển thị).
- **Trạng thái cài đặt**: đã cài trong mã / chỉ có trong thiết kế / một phần.

---

## 7. NỘI DUNG PHÂN TÍCH CHI TIẾT

### 7.1 Thể loại (agent 4)
- Xếp loại game có lập luận dựa trên cơ chế thật: hack & slash, hành động 2D, RPG trang bị chuyên sâu, chơi đơn có kết nối (online single-player), đội hình 3 người với AI đồng đội, phó bản/boss rush có độ khó.
- So sánh ngắn với thể loại gần (ARPG, MMORPG): vì sao chọn chơi đơn mà vẫn có server (chống gian lận, lưu dữ liệu, kinh tế).
- Vòng lặp chơi chính và vòng lặp dài hạn (lên cấp, đồ, GEN, aura theo cấp, nhiệm vụ, điểm danh).

### 7.2 Cơ chế chơi (agent 4)
- **Chiến đấu**: di chuyển, tầm đánh, chế độ trúng (theo hướng ngắm hoặc đuổi mục tiêu), Ki và hồi chiêu, hồi chiêu giữ theo đồng hồ thật của server, khống chế (choáng, ngủ, trói) và giảm thời gian trên boss, tuyệt kỹ, chí mạng, giáp, công thức sát thương (`docs/gameplay/game-design-doc.md` §7.4).
- **AI đồng đội**: quy tắc chọn kỹ năng ("hồi chiêu nào dùng chiêu đó theo thứ tự"), đi theo người chơi, aggro, chết và hồi sinh.
- **Bộ kỹ năng của 3 chủng tộc**: bảng tên, cơ chế, hồi chiêu, chi phí Ki, cấp tối đa; lấy từ dữ liệu DB/migration và so với bảng trong game design doc.
- **Học và nâng kỹ năng**: vàng và thẻ kỹ năng (số thẻ theo vị trí kỹ năng).
- **Hợp thể**: điều kiện, cách cộng chỉ số, kỹ năng, thời gian.
- **Trang bị**: 3 bộ x 3 hành tinh, mặc đúng hành tinh và cấp, nâng cấp bằng đá, đổi mảnh, bán; dòng chỉ số; **GEN** (bộ kích hoạt, dòng GEN, đổi và mở ô); **ép sao**, **pha lê hóa**; chỉ số cập nhật ngay cả khi đang huấn luyện.
- **Kinh tế**: vàng, đá quý, thẻ, nguồn thu và nơi tiêu, sổ ghi giao dịch (`currency_ledger`, `item_log`) và vai trò chống gian lận/nhân bản đồ.
- **Phó bản**: 13 phó bản, 5 đợt quái nhỏ trước mỗi boss, boss theo hành tinh, boss khỉ tiến hóa, 3 độ khó (hệ số HP, sát thương, thưởng), rơi đồ, hồi sinh, rời phó bản.
- **Nhiệm vụ, điểm danh, hòm thư, thông báo, quà tân thủ** (công tắc bật/tắt trên trang quản trị).
- **Thay đổi mới nhất (12/10/2026, đối chiếu kỹ với mã):** phó bản cơ bản dễ hơn cho người mới (V048); thư có hạn nhận `mail.expires_at`, vật phẩm **túi kinh nghiệm** (#207) và quà 99 thẻ nâng cấp kỹ năng cho mọi tài khoản (V049); **chiến dịch quà cho tài khoản mới** `signup_campaign` có hạn kết thúc, tạo thư khi đăng ký trong cùng transaction ở cả server và trang web (V050, công cụ `tools/build_signup_campaign_sql.py`); túi kinh nghiệm dùng một lần cho cả ba nhân vật (V051, `EconomyService`). Tài liệu: `docs/architecture/BattleBallServer.md` §15.33 đến §15.35.

### 7.3 Nhân vật (agent 7, phối hợp agent 4)
- **Cách dựng hình nhân vật**: trang phục ghép từ các phần (đầu, thân, chân) theo bảng tư thế và độ lệch khung có trong dữ liệu (`NroMannequin`, `BbCharacterView`, `BbCostumeAvatar`); vẽ bằng UI Image, không dùng camera riêng; tỉ lệ ảnh x2/x3/x4 chọn theo thiết bị.
- **Ba chủng tộc**: hình mặc định, kỹ năng đặc trưng, vai trò trong đội.
- **Trang phục (skin) và aura**: số lượng, cách mở (mua, theo cấp, thành tích), cách hiển thị; hình hợp thể.
- **Đồng đội AI**: cách đi theo trong làng, cách chiến đấu trong phó bản (vị trí do server quyết định).
- **NPC**: danh sách, vai trò, cách tương tác (focus tự động vào mục tiêu gần nhất, nút nắm đấm để nói chuyện, hộp thoại giữa màn hình, mũi tên chỉ NPC của nhiệm vụ).
- **Quái và boss**: vẽ quái từ dải khung (`BbMobView`), quái ghép từ các phần trong phó bản, thanh máu, boss có bộ chiêu theo hành tinh và vùng cảnh báo đỏ, boss khỉ tiến hóa, triệu hồi; chết thành hồn, hộp hồi sinh.
- Bảng tổng hợp các loại thực thể và thuộc tính chính (HP, Ki, ATK, giáp...) lấy từ `Entities.java`, `StatCalculator.java` và DB.

### 7.4 Map và môi trường (agent 7)
- **Làng tân thủ**: dựng từ dữ liệu bản đồ nguồn bằng `build_village_map.py` thành "chunk" (ảnh nền đất, độ cao địa hình, cây, nhà; `BbChunkMaps`, `BbMapChunk`), có bản vẽ thay thế khi thiếu file; cuộn ngang hai chiều; địa hình đi được (bậc nhỏ leo được, tường chặn, rơi khỏi mép); vị trí NPC.
- **Thời tiết và ngày đêm** (`BbWeather`): nắng, mưa, sấm, đêm; chỉ ở client (mỗi người chơi một bầu trời).
- **Màn hình bắt đầu và chuyển cảnh**: nền vũ trụ cuộn (`BbSpaceBackground`), màn tàu bay giữa làng và phó bản (`BbTransport`) chờ server trả lời.
- **Sân phó bản**: bầu trời không có đất, bay mọi hướng, ảnh nền vũ trụ riêng mỗi phó bản với thiên thạch, hành tinh trôi; giới hạn sân; hệ tọa độ dùng chung với server (đơn vị pixel của dữ liệu nguồn, trục y hướng lên).
- **Huấn luyện**: phiên với Mộc nhân/Saibamen ngay trong làng.
- Nêu rõ thiết kế gốc có minimap/chunk theo khu vực nhưng mức cài đặt thực tế đến đâu.

### 7.5 Hiệu ứng kỹ năng, tư thế và âm thanh (agent 7)
- **Đường ống tài nguyên**: `build_assets.py` đọc dữ liệu nguồn (hiệu ứng, phi tiêu, bảng vẽ kỹ năng) → `skill_fx.json`, `skill_poses.json`, dải ảnh theo tỉ lệ → đóng gói và kê khai (`manifest.json`) → server gửi qua kết nối game → client lưu bộ nhớ đệm (`BbAssetUpdater`, `BbAssetCache`).
- **Hiệu ứng kỹ năng** (`BbSkillFx`): mỗi khung vẽ của kỹ năng có tối đa 3 hiệu ứng và một "phi tiêu" bay tới mục tiêu; cách đặt theo vị trí nhân vật.
- **Tư thế khi dùng kỹ năng** (`BbSkillPoses`): đòn đấm chọn ngẫu nhiên trong các combo theo cấp, kỹ năng gồng có khung riêng.
- **Tuyệt kỹ** (`BbUltimateFx`): Super Kamejoko (tia lớn dần), Cađíc liên hoàn chưởng (15 quả cầu nổ), Ma phong ba (bình, lốc xoáy); hiệu ứng tụ lực.
- **Hợp thể** (`BbFusionFx`): hiệu ứng ở chân, màn hình nháy trắng, ẩn nhân vật trong thời gian ngắn.
- **Phản hồi chiến đấu**: số sát thương bay lên, thanh máu mục tiêu đổi màu theo % HP, vùng cảnh báo đỏ của boss, chưởng bay tới mép màn hình.
- **Âm thanh** (`BbSfx`): tệp âm thanh đánh số theo dữ liệu nguồn, nạp lần đầu rồi giữ, nhiều nguồn phát chồng nhau.
- Nêu ranh giới: server quyết định trúng/sát thương; client chỉ vẽ hiệu ứng. Ghi rõ những hiệu ứng nào còn thiếu hoặc là "giai đoạn 2".

### 7.6 Giao diện và điều khiển (agent 7, phối hợp agent 2)
- Joystick, nút kỹ năng với thời gian hồi, thanh Ki, khung HP/KI của đội ba người, nút focus, bảng nhân vật/túi, các tab Aura, Kỹ năng, Chức năng, bảng NPC, popup.
- Màn đăng nhập (đăng ký, đăng nhập, tài khoản đã lưu), màn **chọn máy chủ Local/VPS**.

### 7.7 Kiến trúc client (agent 2)
- Scene, thư mục, ranh giới `BattleNet` (mạng + giao diện + thế giới) và `NroMannequin` (công cụ xem trước trang phục); vì sao mọi thứ vẽ bằng uGUI.
- Tầng mạng: `BbConnection`, `BbClient`, `BbSession` (giữ kết nối qua các scene, bơm sự kiện mỗi khung hình, đưa về màn đăng nhập khi rớt mạng), yêu cầu/đáp bất đồng bộ.
- Đăng nhập, tài khoản lưu riêng theo máy chủ, kiểm tra chứng chỉ (chỉ chấp nhận chứng chỉ tự ký khi phát triển).
- Công cụ trong Editor (`Editor/`): dựng scene bằng mã, chụp màn hình, kiểm tra mạng và tài nguyên, build.
- Hiệu năng/Android: tần số khung hình (`BbPerformance`), bộ nhớ tài nguyên, tỉ lệ ảnh.

### 7.8 Kiến trúc server (agent 1)
- **Gói và trách nhiệm**: `net` (Netty: `GameServer`, `ConnectionHandler`, khung gói, TLS, gửi tài nguyên `AssetInfo`), `session`, `auth` (`AuthService`, `AccountRepository`: bcrypt, token), `data` (dữ liệu tĩnh, phiên bản theo miền, nạp lại khi chạy), `db` (`Database`, `MigrationRunner`), `game` (các dịch vụ: kinh tế, shop, thư, nhiệm vụ, điểm danh, GEN, đá, aura, tính chỉ số), `game/match` (trận).
- **Vòng đời kết nối**: Hello/HelloAck (phiên bản, tài nguyên), đăng ký/đăng nhập, token làm mới, heartbeat, thời gian chờ, giới hạn kết nối, đăng nhập nơi khác, ngắt.
- **Mô hình luồng**: luồng mạng Netty, luồng làm việc DB, một luồng chạy mỗi trận; luồng mạng chỉ xếp hàng đầu vào cho trận (`MatchEngine.submit`).
- **Trận đấu**: thiết lập (`MatchSetup`, `MatchStarter`), vòng lặp 20 Hz (`MatchEngine`), thực thể (`Entities`), độ khó (`Difficulty`), kết toán thưởng (`MatchSettlement`), quản lý và vào lại (`MatchManager`), chờ client sẵn sàng trước khi trận chạy. Phân tích: AI đồng đội, hành vi boss, đợt quái, va chạm kỹ năng, khống chế, hồi sinh, điều kiện thắng/thua.
- **Giao dịch và toàn vẹn dữ liệu**: mỗi nghiệp vụ một transaction; quà chỉ nhận một lần; đồ không nhân bản; sổ ghi giao dịch.
- **Điểm cần nêu trung thực**: chú thích ở `MatchEngine.java` cho biết client tự báo vị trí của mình và server **không kiểm tra** (chỉ giữ trong sân). Đây là một hạn chế chống gian lận cần đưa vào phần đánh giá.
- Cấu hình bằng biến môi trường `BB_*` (`ServerConfig`); không ghi giá trị bí mật.
- Bảng kích thước các lớp chính (tự đếm dòng) để thể hiện độ phức tạp.

### 7.9 Cơ sở dữ liệu (agent 3)
- Sơ đồ ER cho nhóm dữ liệu người chơi (tài khoản, ví, nhân vật, kỹ năng, kho đồ chung, trang bị, GEN, thư, nhiệm vụ, điểm danh, trận và nhật ký) và nhóm dữ liệu tĩnh (kỹ năng, quái, boss, phó bản, vật phẩm, skin, aura, cấu hình cân bằng).
- Dòng thời gian toàn bộ migration: mỗi cái thêm gì, vì sao, nhóm theo tính năng. Cơ chế tự áp dụng, kiểm tra checksum, mốc nền (`BB_BASELINE`).

### 7.10 Giao thức (agent 3)
- Phân nhóm message: kết nối, xác thực, dữ liệu người chơi, kinh tế, trang bị, thư/thông báo, huấn luyện, trận; luồng yêu cầu/đáp qua `request_id`; sự kiện server đẩy; lỗi (`ActionError`); tải tài nguyên.
- Sơ đồ tuần tự: đăng nhập, vào phó bản, một tick trận gửi về client, vào lại trận.
- So sánh message định nghĩa với message được xử lý ở mỗi phía.

### 7.11 Bảo mật và web (agent 5)
- TLS bắt buộc, bcrypt chi phí 12 dùng chung PHP/Java, token làm mới và thu hồi, giới hạn tần suất, captcha, kiểm tra đầu vào, câu lệnh tham số hóa, trang quản trị (giới hạn IP, quyền ADMIN, CSRF, nhật ký thao tác). Nêu điểm yếu nếu thấy.

### 7.12 Kiểm thử và triển khai (agent 5)
- Liệt kê lớp test và điều chúng bảo đảm; test nào cần DB. Không tự chạy test trừ khi tác giả cho phép.
- Quy trình `deploy.sh`: build, sao lưu DB (giữ 10 bản), tải lên, chỉ tải lại gói tài nguyên khi kê khai đổi, khởi động lại, chờ cổng game. Mô hình dịch vụ trên VPS Ubuntu (nêu chung, xem mục 9).

---

## 8. BỐ CỤC BÁO CÁO ĐỒ ÁN (theo mẫu của trường nếu tác giả cung cấp)

Trang bìa, lời cam đoan, lời cảm ơn, mục lục, danh mục hình, danh mục bảng, danh mục viết tắt, rồi:

1. **Mở đầu**: lý do chọn đề tài, mục tiêu, đối tượng và phạm vi, phương pháp, bố cục.
2. **Cơ sở lý thuyết và công nghệ**: Unity/C#/uGUI, Java/Netty, TLS, Protobuf, MariaDB và migration, mô hình client-server có thẩm quyền ở server, vòng lặp tick cố định, hoạt ảnh 2D theo khung (sprite sheet), khái niệm thể loại hack & slash/RPG.
3. **Khảo sát và phân tích yêu cầu**: thể loại và đối tượng người chơi, yêu cầu chức năng và phi chức năng, tác nhân (người chơi, quản trị viên), ca sử dụng chính (sơ đồ).
4. **Thiết kế game**: thể loại, vòng lặp chơi, cơ chế chiến đấu, nhân vật và chủng tộc, kỹ năng, hợp thể, trang bị và GEN, kinh tế, phó bản và boss, map và môi trường, hiệu ứng và âm thanh, giao diện và điều khiển.
5. **Thiết kế hệ thống**: kiến trúc tổng thể, server, client, cơ sở dữ liệu (ER), giao thức, bảo mật, đường ống tài nguyên.
6. **Cài đặt**: cấu trúc mã, các thuật toán/cơ chế đáng chú ý (vòng lặp 20 Hz, tính chỉ số, độ khó, rơi đồ, GEN, vào lại trận, cập nhật tài nguyên, dựng nhân vật từ các phần, hiệu ứng kỹ năng), quy trình phát triển (dòng thời gian commit).
7. **Kiểm thử và đánh giá**: chiến lược, nhóm test, kết quả; đánh giá trung thực những gì chưa kiểm chứng (chơi thử, cân bằng, điện thoại thật, chịu tải, MariaDB 10.6+, vị trí client không được kiểm tra).
8. **Triển khai và vận hành**.
9. **Kết luận và hướng phát triển**.
10. **Tài liệu tham khảo**: tài liệu chính thức của công nghệ (Unity, Netty, Protobuf, MariaDB, OWASP...), và **một đoạn duy nhất ghi nguồn gốc dữ liệu game** theo mục 10. Không bịa URL, tác giả, năm; không chắc thì chỉ ghi tên nguồn.
11. **Phụ lục**: thuật ngữ game, bảng kỹ năng 3 chủng tộc, danh sách NPC/quái/boss, bảng migration, sai lệch tài liệu và mã nguồn, bảng bằng chứng, danh sách vị trí cần ảnh, hướng dẫn chạy cục bộ.

Sơ đồ Mermaid tối thiểu: kiến trúc tổng thể; các gói server; các thành phần client; vòng đời kết nối; tuần tự đăng nhập; tuần tự vào phó bản; một tick trận; ER; đường ống tài nguyên; vòng lặp chơi; luồng một kỹ năng từ bấm nút đến hiển thị; quy trình triển khai.

Hình thức: tiếng Việt chuẩn, văn phong kỹ thuật; mỗi chương mở bằng đoạn tóm tắt; đánh số hình, bảng; chú thích dưới mỗi hình.

---

## 9. THÔNG TIN NHẠY CẢM: KHÔNG ĐƯA VÀO BÁO CÁO

Báo cáo gửi cho người ngoài. Không ghi:
- Địa chỉ IP, tên miền riêng, tên host của máy chủ thật. Chỉ viết "máy chủ VPS Ubuntu".
- Tên/đường dẫn khóa SSH, mật khẩu, token, giá trị biến môi trường bí mật, đường dẫn tệp chứa bí mật, chuỗi kết nối DB có mật khẩu.
- Tên tài khoản người chơi thật.

Gặp thông tin như vậy trong repo (kể cả trong script, giá trị mặc định trong mã, lịch sử commit): báo Điều phối và tác giả, không đưa vào báo cáo. Agent QA tìm các mẫu này (IP, `ssh`, `.p12`, `password`, `token`, tên miền) trong bản cuối.

---

## 10. GHI NHẬN NGUỒN GỐC DỮ LIỆU GAME (CHỈ GHI MỘT LẦN)

Một phần dữ liệu của dự án lấy từ game Ngọc Rồng Online (NRO): bộ kỹ năng và số liệu kỹ năng, hình nhân vật và trang phục, bản đồ làng, quái, hiệu ứng kỹ năng, tuyệt kỹ, âm thanh.

Quy tắc trình bày (theo yêu cầu của tác giả):
- Ghi điều này **đúng một lần**, trong mục **Tài liệu tham khảo** ở cuối báo cáo: một đoạn ngắn nêu tên game nguồn, những loại dữ liệu đã dùng (liệt kê như trên) và mục đích học tập, phi thương mại (hỏi tác giả xác nhận).
- **Không nhắc lại** nguồn gốc trong các chương khác, trong chú thích hình hay trong bảng. Trong thân báo cáo gọi chung là "dữ liệu nguồn" hoặc "tài nguyên game" khi cần.
- Không viết rằng các tài nguyên đó do tác giả tự vẽ hay tự sáng tác. Phần tác giả tự thiết kế và tự cài đặt (kiến trúc, mạng, server, cơ sở dữ liệu, cơ chế trận, kinh tế, web, công cụ, triển khai) được trình bày bình thường trong các chương.
- Nếu tên trong mã nguồn có chữ "Nro" (ví dụ `NroMannequin`), giữ nguyên tên kỹ thuật khi trích, không bình luận thêm.

---

## 11. CHECKLIST CHẤT LƯỢNG (agent QA chặn bản nháp nếu chưa đạt)

- [ ] Khẳng định kỹ thuật quan trọng đều có bằng chứng và nhãn đúng.
- [ ] Số liệu đếm được tự đếm lại và nhất quán giữa các chương.
- [ ] Phân biệt "đã cài đặt" và "chỉ có trong thiết kế"; số liệu PROPOSED không trình bày như kết quả kiểm chứng.
- [ ] Đủ các nhóm nội dung: thể loại, cơ chế, nhân vật, map, hiệu ứng kỹ năng và âm thanh, giao diện, client, server, DB, giao thức, bảo mật, kiểm thử, triển khai.
- [ ] Đủ sơ đồ mục 8; kiểm tra ngẫu nhiên ít nhất 5 quan hệ trong mỗi sơ đồ khớp mã nguồn.
- [ ] Có đánh giá hạn chế trung thực, không có số đo giả.
- [ ] Không còn thông tin nhạy cảm (mục 9).
- [ ] Nguồn gốc dữ liệu game chỉ xuất hiện một lần, ở Tài liệu tham khảo (mục 10); không lặp lại ở chương nào khác.
- [ ] Thuật ngữ thống nhất với `00_thuat_ngu.md`; chính tả và văn phong nhất quán.
- [ ] Tài liệu tham khảo không có nguồn bịa.
- [ ] Đã điền phụ lục sai lệch và danh sách vị trí cần ảnh.

---

## 12. NHỮNG ĐIỀU KHÔNG ĐƯỢC LÀM

- Không sửa, xóa, đổi tên, di chuyển file của repo; không commit, push.
- Không kết nối máy chủ thật, không chạy game, không chạy lệnh có tác dụng phụ. Chỉ đọc file và đếm.
- Không bịa số liệu, tên lớp, tên bảng, thư viện, kết quả test, lịch sử phát triển, hay mô tả hình ảnh mà mã nguồn không nói tới.
- Không sao chép dài mã nguồn hay tài liệu bên thứ ba; trích ngắn có nguồn, còn lại diễn đạt lại.
- Không khen không có căn cứ; mỗi ưu điểm gắn với bằng chứng.

---

## 13. CÂU HỎI CHO TÁC GIẢ (gom và hỏi tối đa 4 câu mỗi đợt, mỗi câu có ví dụ)

1. Mẫu báo cáo của trường (số trang, font, khổ giấy, bố cục chương)? Có thì dùng thay mục 8.
2. Tên đề tài, tên tác giả, giảng viên hướng dẫn, năm học (trang bìa, lời cam đoan)?
3. Quy định của trường về khai báo công cụ AI và khai báo tài nguyên dùng lại từ game gốc? Xác nhận dự án phi thương mại?
4. Có được chạy test và build cục bộ để lấy số liệu thật cho chương Kiểm thử? Nếu có, lệnh và biến môi trường nào?
5. Phần muốn nhấn mạnh nhất (ví dụ server có thẩm quyền và mô phỏng trận, hay hệ thống trang bị và GEN, hay hiệu ứng kỹ năng, hay triển khai)?
6. Có ảnh chụp/video: làng, NPC, bảng nhân vật, huấn luyện, phó bản, boss, tuyệt kỹ, hợp thể, trang quản trị? Nếu chưa, đội ghi danh sách vị trí cần ảnh.

---

## 14. SẢN PHẨM CUỐI CÙNG

Thư mục `report/` gồm: `00_ke_hoach.md`, `00_thuat_ngu.md`, `01_server.md`, `02_client.md`, `03_du_lieu_giao_thuc.md`, `04_the_loai_co_che.md`, `05_ha_tang_bao_mat_kiem_thu.md`, `07_nhan_vat_map_hieu_ung.md`, `06_ra_soat.md`, **`BAO_CAO_DO_AN.md`** (bản hợp nhất theo mục 8, sản phẩm chính) và thư mục `hinh/` chứa mã Mermaid của các sơ đồ. Kết thúc bằng tóm tắt ngắn: đã làm gì, điều gì chưa xác định, lỗi tồn đọng của checklist và câu hỏi còn chờ tác giả.
