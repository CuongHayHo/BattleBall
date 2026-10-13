# PROMPT CHO ĐỘI AGENT (Antigravity, Teamwork Preview): PHÂN TÍCH DỰ ÁN BATTLEBALL VÀ VIẾT BÁO CÁO ĐỒ ÁN

> Cách dùng: trong Antigravity 2.0 (gói trả phí), mở workspace chứa dự án, gõ lệnh `/teamwork-preview` kèm yêu cầu (xem mẫu lệnh ở đầu file `ANTIGRAVITY_REPORT_PROMPT_LOCAL.md`). Agent chính sẽ phỏng vấn để chốt phạm vi rồi tạo "prompt artifact": kiểm tra artifact đó khớp với file này trước khi duyệt. Câu hỏi ở mục 13 đã được trả lời sẵn.

---

## 0. VAI TRÒ VÀ MỤC TIÊU

Bạn là một đội gồm nhiều agent chuyên môn, cùng **phân tích toàn bộ mã nguồn và tài liệu của dự án BattleBall, rồi viết một báo cáo đồ án hoàn chỉnh bằng tiếng Việt** để tác giả nộp cho nhà trường.

Báo cáo phải:
1. **Đúng sự thật**: mọi khẳng định kỹ thuật có bằng chứng trong mã nguồn hoặc tài liệu của repo (`đường_dẫn_file:số_dòng` hoặc `tài liệu §mục`).
2. **Đầy đủ** về: thể loại game, cơ chế chơi, nhân vật (chủng tộc, đồng đội AI, NPC, quái, boss, trang phục, aura, hợp thể), map (làng tân thủ, sân phó bản, nền, thời tiết, chuyển cảnh), hiệu ứng kỹ năng và âm thanh, kiến trúc client, kiến trúc server, cơ sở dữ liệu, giao thức mạng, bảo mật, kiểm thử, triển khai.
3. Đúng **khung mẫu báo cáo môn học của trường** (bố cục và định dạng, mục 8), có sơ đồ Mermaid, bảng biểu, đánh giá trung thực ưu điểm và hạn chế.
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
| Công cụ | PowerShell/Bash: chạy server, build, triển khai | `BattleBallServer/tools/*.ps1`, `BattleBallServer/tools/deploy.sh` |
| Kiểm thử | JUnit; test cần DB chỉ chạy khi có biến môi trường `BB_TEST_DB_URL` | `BattleBallServer/server/src/test/` |
| Triển khai | VPS Ubuntu: service systemd, Nginx + PHP-FPM, chứng chỉ Let's Encrypt, firewall, script triển khai một lệnh có sao lưu DB | `BattleBallServer/tools/deploy.sh` |

**Quy mô gần đúng (BẮT BUỘC đếm lại):** khoảng 54 file Java (khoảng 10.600 dòng ở mã chính), khoảng 62 file C# không tính file sinh tự động, 10 file PHP, 12 file test (125 test, tất cả đạt khi chạy với cơ sở dữ liệu thật ngày 12/10/2026), 51 migration (`V001` đến `V051`), file `.proto` khoảng 945 dòng, khoảng 18 commit từ 09/10/2026. File lớn nhất: `MatchEngine.java` (khoảng 2.650 dòng), `BbTraining.cs` (khoảng 2.260 dòng), `EconomyService.java` (khoảng 1.290 dòng), `BbVillageView.cs` (khoảng 1.020 dòng), `BbSkillFx.cs` (khoảng 940 dòng).

**Hình ảnh thật và dữ liệu cho báo cáo (CÓ TRONG REPO):**
- `docs/report_media/screenshots/`: **138 ảnh chụp thật** của game (làng, NPC, bảng nhân vật, túi, skin, aura, hợp thể, trang bị, GEN, cửa hàng, kỹ năng, huấn luyện, từng khung hiệu ứng kỹ năng và tuyệt kỹ, thời tiết, hòm thư, điểm danh, nhiệm vụ, game chạy trên Android). Mục lục chia nhóm: `docs/report_media/README.md`. **Hãy xem ảnh** khi mô tả nhân vật, map, hiệu ứng, giao diện, và **chèn ảnh vào báo cáo** bằng đường dẫn tương đối, có số hình và chú thích.
- `BattleBall/SourceData/data/skill/skill_fx.json`, `skill_poses.json`: bảng hiệu ứng và tư thế của từng kỹ năng mà client dùng; `BattleBall/SourceData/data/map/village_start/chunk.json`: bố cục bản đồ làng (địa hình, cảnh vật, vị trí).
- Phần còn lại của tài nguyên (khoảng 950 MB, 167 nghìn file hình, âm thanh) không nằm trong repo; gói tài nguyên được đóng gói sẵn (`BattleBallServer/web/assets`) và **server gửi cho client qua chính kết nối game** khi đăng nhập.
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

## 3. PHÂN VIỆC THEO CÁC VAI TRÒ CÓ SẴN CỦA TEAMWORK

Teamwork (`/teamwork-preview`) tự tạo các vai trò: **Sentinel** (điều phối chung), **Project Orchestrator** (chia mốc, chạy song song, giao việc), **Explorer** (chỉ đọc, nghiên cứu), **Worker** (viết file, chạy lệnh), và các cổng kiểm tra **Critic**, **Challenger**, **Auditor**, **Success Auditor**. Không tự tạo đội riêng; Project Orchestrator dùng bảng dưới đây để giao việc.

**Lưu ý cho Project Orchestrator:** mỗi agent con bắt đầu với ngữ cảnh trống. Khi giao việc, luôn đưa kèm: đường dẫn hai file prompt (prompt chính và, nếu chạy trên máy tác giả, prompt local), mục tương ứng trong mục 7, danh sách file cần đọc, file sản phẩm cần viết, và các quy tắc ở mục 2, 9, 10, 12.

### 3.1 Sáu mảng phân tích (mỗi mảng: Explorer đọc và ghi chú bằng chứng, rồi đúng MỘT Worker viết file sản phẩm)

| Mảng | Nội dung (mục 7) | Đọc chủ yếu | File sản phẩm (một Worker sở hữu) |
|---|---|---|---|
| **A. Server** | 7.8 | `BattleBallServer/server/src/main/java/**`, `docs/architecture/BattleBallServer.md`, `docs/networking/BattleBallServer.md` | `01_server.md` |
| **B. Client** | 7.7 | `BattleBall/Assets/BattleNet/**` (trừ phần của mảng F), scene, `Editor/` | `02_client.md` |
| **C. Dữ liệu và giao thức** | 7.9, 7.10 | `BattleBallServer/db/migrations/**`, `BattleBallServer/proto/battleball.proto`, `docs/database/BattleBall.md`, `docs/protocol/BattleBall.md` | `03_du_lieu_giao_thuc.md` |
| **D. Thể loại và cơ chế game** | 7.1, 7.2 | `docs/gameplay/game-design-doc.md`, `game/*.java`, `game/match/*.java`, migration dữ liệu | `04_the_loai_co_che.md` |
| **E. Hạ tầng, bảo mật, kiểm thử** | 7.11, 7.12 | `server/src/test/**`, `web/`, `admin/`, `tools/deploy.sh`, `tools/mvn.ps1`, `tools/run_*.ps1`, `tools/make_dev_keystore.ps1` (chỉ đọc nội dung script, không mở file khóa), `docs/architecture/BattleBallServer.md` §16; không phân tích file `.py` (mục 7.13) | `05_ha_tang_bao_mat_kiem_thu.md` |
| **F. Nhân vật, map, hiệu ứng, giao diện** | 7.3 đến 7.6 | `Bb{CharacterView,CostumeAvatar,MobView,SkillFx,SkillPoses,UltimateFx,FusionFx,Sfx,AuraAssets,VillageView*,ChunkMaps,MapChunk,Weather,SpaceBackground,Transport,Joystick}.cs`, `BattleBall/Assets/NroMannequin/`, `BattleBallServer/web/assets/manifest.json`, phần boss/quái trong `MatchEngine.java`, `Entities.java`, ảnh trong `docs/report_media/` | `06_nhan_vat_map_hieu_ung.md` |

Hai mảng cần nhau thì trao đổi qua ghi chú của Explorer (ví dụ F cần biết server gửi vị trí boss thế nào thì hỏi A và C), không tự đoán.

### 3.2 Việc của các Worker khác (mỗi file một Worker)

| Việc | File sở hữu |
|---|---|
| Kế hoạch, bảng đếm số liệu (kèm lệnh đếm), bảng thuật ngữ dùng chung | `00_ke_hoach.md`, `00_so_lieu.md`, `00_thuat_ngu.md` |
| Chọn và chép ảnh, viết mã Mermaid và xuất PNG cho các sơ đồ ở mục 8 | thư mục `hinh/` |
| Hợp nhất nội dung theo bố cục mục 8 | `BAO_CAO_DO_AN.md` |
| Dựng file Word trên bản sao khung mẫu | `BAO_CAO_DO_AN.docx` |
| Ghi lỗi của các cổng kiểm tra và trạng thái đã sửa | `99_ra_soat.md` |

### 3.3 Cổng kiểm tra (áp dụng cho mọi mốc)

- **Critic**: đúng nội dung mục 7, đủ mẫu mục 5, đúng bố cục và định dạng mục 8, văn phong, thuật ngữ thống nhất.
- **Challenger**: lấy ngẫu nhiên ít nhất 10 khẳng định mỗi file và mở đúng `file:dòng` để kiểm; tìm số liệu bịa, quan hệ sơ đồ sai, câu còn sót của chữ mẫu cũ (Linux, Shell Script, Kernel Module), thông tin nhạy cảm (mục 9), nguồn gốc dữ liệu game bị nhắc ngoài Tài liệu tham khảo (mục 10).
- **Auditor**: mọi số liệu đếm và kết quả test trong báo cáo khớp với đầu ra lệnh thật đã ghi trong `00_so_lieu.md`; không có kết quả nào được viết mà không chạy hoặc không đếm.
- **Success Auditor**: kiểm toàn bộ cuối cùng: mở `BAO_CAO_DO_AN.docx`, đúng thứ tự phần và định dạng của mẫu, mục lục và danh mục hình cập nhật được, mọi hình được chèn tồn tại trong `hinh/`, checklist mục 11 đạt.

---

## 4. CÁC MỐC VÀ TIÊU CHÍ HOÀN THÀNH

| Mốc | Việc | Tiêu chí hoàn thành (kiểm được) |
|---|---|---|
| **M1. Khảo sát** | Đọc `README.md`, `docs/gameplay/game-design-doc.md`, `docs/architecture/BattleBallServer.md` (§1 đến §4, §15, §16); cây thư mục hai cấp; đếm file, dòng, test, migration, commit; xác định điểm vào (`Main.java`, `Login.unity`, `Menu.unity`, `BbLoginUi.cs`, `BbClient.cs`, `BbVillageView.cs`); dòng thời gian commit | `00_ke_hoach.md`, `00_so_lieu.md` (mỗi số kèm lệnh đếm), `00_thuat_ngu.md` tồn tại |
| **M2. Phân tích sáu mảng** (song song) | Mảng A đến F theo mục 3.1 và mục 7 | Mỗi file có đủ 7 phần của mục 5, ít nhất một sơ đồ Mermaid, bảng bằng chứng; Challenger kiểm ngẫu nhiên không thấy khẳng định sai |
| **M3. Hình và sơ đồ** | Chọn ảnh; sơ đồ ở mục 8 viết bằng Mermaid và xuất PNG | Mỗi sơ đồ có cả mã và PNG; Critic xác nhận ít nhất 5 quan hệ mỗi sơ đồ khớp mã nguồn |
| **M4. Hợp nhất** | `BAO_CAO_DO_AN.md` theo mục 8, thống nhất thuật ngữ, viết lời mở đầu, tiểu kết, kết luận | Đúng thứ tự phần của mẫu; Auditor xác nhận số liệu khớp `00_so_lieu.md` |
| **M5. Word và rà soát cuối** | `BAO_CAO_DO_AN.docx` trên bản sao khung mẫu; chạy checklist mục 11 | Success Auditor đạt; `99_ra_soat.md` không còn lỗi mở, hoặc ghi rõ lỗi tồn đọng |

---

## 5. MẪU FILE SẢN PHẨM CỦA MỖI MẢNG PHÂN TÍCH

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

### 7.1 Thể loại (mảng D)
- Xếp loại game có lập luận dựa trên cơ chế thật: hack & slash, hành động 2D, RPG trang bị chuyên sâu, chơi đơn có kết nối (online single-player), đội hình 3 người với AI đồng đội, phó bản/boss rush có độ khó.
- So sánh ngắn với thể loại gần (ARPG, MMORPG): vì sao chọn chơi đơn mà vẫn có server (chống gian lận, lưu dữ liệu, kinh tế).
- Vòng lặp chơi chính và vòng lặp dài hạn (lên cấp, đồ, GEN, aura theo cấp, nhiệm vụ, điểm danh).

### 7.2 Cơ chế chơi (mảng D)
- **Chiến đấu**: di chuyển, tầm đánh, chế độ trúng (theo hướng ngắm hoặc đuổi mục tiêu), Ki và hồi chiêu, hồi chiêu giữ theo đồng hồ thật của server, khống chế (choáng, ngủ, trói) và giảm thời gian trên boss, tuyệt kỹ, chí mạng, giáp, công thức sát thương (`docs/gameplay/game-design-doc.md` §7.4).
- **AI đồng đội**: quy tắc chọn kỹ năng ("hồi chiêu nào dùng chiêu đó theo thứ tự"), đi theo người chơi, aggro, chết và hồi sinh.
- **Bộ kỹ năng của 3 chủng tộc**: bảng tên, cơ chế, hồi chiêu, chi phí Ki, cấp tối đa; lấy từ dữ liệu DB/migration và so với bảng trong game design doc.
- **Học và nâng kỹ năng**: vàng và thẻ kỹ năng (số thẻ theo vị trí kỹ năng).
- **Hợp thể**: điều kiện, cách cộng chỉ số, kỹ năng, thời gian.
- **Trang bị**: 3 bộ x 3 hành tinh, mặc đúng hành tinh và cấp, nâng cấp bằng đá, đổi mảnh, bán; dòng chỉ số; **GEN** (bộ kích hoạt, dòng GEN, đổi và mở ô); **ép sao**, **pha lê hóa**; chỉ số cập nhật ngay cả khi đang huấn luyện.
- **Kinh tế**: vàng, đá quý, thẻ, nguồn thu và nơi tiêu, sổ ghi giao dịch (`currency_ledger`, `item_log`) và vai trò chống gian lận/nhân bản đồ.
- **Phó bản**: 13 phó bản, 5 đợt quái nhỏ trước mỗi boss, boss theo hành tinh, boss khỉ tiến hóa, 3 độ khó (hệ số HP, sát thương, thưởng), rơi đồ, hồi sinh, rời phó bản.
- **Nhiệm vụ, điểm danh, hòm thư, thông báo, quà tân thủ** (công tắc bật/tắt trên trang quản trị).
- **Thay đổi mới nhất (12/10/2026, đối chiếu kỹ với mã):** phó bản cơ bản dễ hơn cho người mới (V048); thư có hạn nhận `mail.expires_at`, vật phẩm **túi kinh nghiệm** (#207) và quà 99 thẻ nâng cấp kỹ năng cho mọi tài khoản (V049); **chiến dịch quà cho tài khoản mới** `signup_campaign` có hạn kết thúc, tạo thư khi đăng ký trong cùng transaction ở cả server và trang web (V050); túi kinh nghiệm dùng một lần cho cả ba nhân vật (V051, `EconomyService`). Tài liệu: `docs/architecture/BattleBallServer.md` §15.33 đến §15.35.

### 7.3 Nhân vật (mảng F, phối hợp mảng D)
- **Cách dựng hình nhân vật**: trang phục ghép từ các phần (đầu, thân, chân) theo bảng tư thế và độ lệch khung có trong dữ liệu (`NroMannequin`, `BbCharacterView`, `BbCostumeAvatar`); vẽ bằng UI Image, không dùng camera riêng; tỉ lệ ảnh x2/x3/x4 chọn theo thiết bị.
- **Ba chủng tộc**: hình mặc định, kỹ năng đặc trưng, vai trò trong đội.
- **Trang phục (skin) và aura**: số lượng, cách mở (mua, theo cấp, thành tích), cách hiển thị; hình hợp thể.
- **Đồng đội AI**: cách đi theo trong làng, cách chiến đấu trong phó bản (vị trí do server quyết định).
- **NPC**: danh sách, vai trò, cách tương tác (focus tự động vào mục tiêu gần nhất, nút nắm đấm để nói chuyện, hộp thoại giữa màn hình, mũi tên chỉ NPC của nhiệm vụ).
- **Quái và boss**: vẽ quái từ dải khung (`BbMobView`), quái ghép từ các phần trong phó bản, thanh máu, boss có bộ chiêu theo hành tinh và vùng cảnh báo đỏ, boss khỉ tiến hóa, triệu hồi; chết thành hồn, hộp hồi sinh.
- Bảng tổng hợp các loại thực thể và thuộc tính chính (HP, Ki, ATK, giáp...) lấy từ `Entities.java`, `StatCalculator.java` và DB.

### 7.4 Map và môi trường (mảng F)
- **Làng tân thủ**: dữ liệu bản đồ dạng "chunk" (ảnh nền đất, độ cao địa hình, cây, nhà; `BbChunkMaps`, `BbMapChunk`), có bản vẽ thay thế khi thiếu file; cuộn ngang hai chiều; địa hình đi được (bậc nhỏ leo được, tường chặn, rơi khỏi mép); vị trí NPC.
- **Thời tiết và ngày đêm** (`BbWeather`): nắng, mưa, sấm, đêm; chỉ ở client (mỗi người chơi một bầu trời).
- **Màn hình bắt đầu và chuyển cảnh**: nền vũ trụ cuộn (`BbSpaceBackground`), màn tàu bay giữa làng và phó bản (`BbTransport`) chờ server trả lời.
- **Sân phó bản**: bầu trời không có đất, bay mọi hướng, ảnh nền vũ trụ riêng mỗi phó bản với thiên thạch, hành tinh trôi; giới hạn sân; hệ tọa độ dùng chung với server (đơn vị pixel của dữ liệu nguồn, trục y hướng lên).
- **Huấn luyện**: phiên với Mộc nhân/Saibamen ngay trong làng.
- Nêu rõ thiết kế gốc có minimap/chunk theo khu vực nhưng mức cài đặt thực tế đến đâu.

### 7.5 Hiệu ứng kỹ năng, tư thế và âm thanh (mảng F)
- **Phân phối tài nguyên**: tài nguyên được đóng gói sẵn theo nhóm và theo tỉ lệ ảnh (x1 đến x4) kèm bảng kê khai `manifest.json` (phiên bản, kích thước) → server gửi qua kết nối game khi đăng nhập → client kiểm tra phiên bản, tải phần thiếu và lưu bộ nhớ đệm (`BbAssetUpdater`, `BbAssetCache`); bảng hiệu ứng và tư thế kỹ năng (`skill_fx.json`, `skill_poses.json`) nằm trong các gói này.
- **Hiệu ứng kỹ năng** (`BbSkillFx`): mỗi khung vẽ của kỹ năng có tối đa 3 hiệu ứng và một "phi tiêu" bay tới mục tiêu; cách đặt theo vị trí nhân vật.
- **Tư thế khi dùng kỹ năng** (`BbSkillPoses`): đòn đấm chọn ngẫu nhiên trong các combo theo cấp, kỹ năng gồng có khung riêng.
- **Tuyệt kỹ** (`BbUltimateFx`): Super Kamejoko (tia lớn dần), Cađíc liên hoàn chưởng (15 quả cầu nổ), Ma phong ba (bình, lốc xoáy); hiệu ứng tụ lực.
- **Hợp thể** (`BbFusionFx`): hiệu ứng ở chân, màn hình nháy trắng, ẩn nhân vật trong thời gian ngắn.
- **Phản hồi chiến đấu**: số sát thương bay lên, thanh máu mục tiêu đổi màu theo % HP, vùng cảnh báo đỏ của boss, chưởng bay tới mép màn hình.
- **Âm thanh** (`BbSfx`): tệp âm thanh đánh số theo dữ liệu nguồn, nạp lần đầu rồi giữ, nhiều nguồn phát chồng nhau.
- Nêu ranh giới: server quyết định trúng/sát thương; client chỉ vẽ hiệu ứng. Ghi rõ những hiệu ứng nào còn thiếu hoặc là "giai đoạn 2".

### 7.6 Giao diện và điều khiển (mảng F, phối hợp mảng B)
- Joystick, nút kỹ năng với thời gian hồi, thanh Ki, khung HP/KI của đội ba người, nút focus, bảng nhân vật/túi, các tab Aura, Kỹ năng, Chức năng, bảng NPC, popup.
- Màn đăng nhập (đăng ký, đăng nhập, tài khoản đã lưu), màn **chọn máy chủ Local/VPS**.

### 7.7 Kiến trúc client (mảng B)
- Scene, thư mục, ranh giới `BattleNet` (mạng + giao diện + thế giới) và `NroMannequin` (công cụ xem trước trang phục); vì sao mọi thứ vẽ bằng uGUI.
- Tầng mạng: `BbConnection`, `BbClient`, `BbSession` (giữ kết nối qua các scene, bơm sự kiện mỗi khung hình, đưa về màn đăng nhập khi rớt mạng), yêu cầu/đáp bất đồng bộ.
- Đăng nhập, tài khoản lưu riêng theo máy chủ, kiểm tra chứng chỉ (chỉ chấp nhận chứng chỉ tự ký khi phát triển).
- Công cụ trong Editor (`Editor/`): dựng scene bằng mã, chụp màn hình, kiểm tra mạng và tài nguyên, build.
- Hiệu năng/Android: tần số khung hình (`BbPerformance`), bộ nhớ tài nguyên, tỉ lệ ảnh.

### 7.8 Kiến trúc server (mảng A)
- **Gói và trách nhiệm**: `net` (Netty: `GameServer`, `ConnectionHandler`, khung gói, TLS, gửi tài nguyên `AssetInfo`), `session`, `auth` (`AuthService`, `AccountRepository`: bcrypt, token), `data` (dữ liệu tĩnh, phiên bản theo miền, nạp lại khi chạy), `db` (`Database`, `MigrationRunner`), `game` (các dịch vụ: kinh tế, shop, thư, nhiệm vụ, điểm danh, GEN, đá, aura, tính chỉ số), `game/match` (trận).
- **Vòng đời kết nối**: Hello/HelloAck (phiên bản, tài nguyên), đăng ký/đăng nhập, token làm mới, heartbeat, thời gian chờ, giới hạn kết nối, đăng nhập nơi khác, ngắt.
- **Mô hình luồng**: luồng mạng Netty, luồng làm việc DB, một luồng chạy mỗi trận; luồng mạng chỉ xếp hàng đầu vào cho trận (`MatchEngine.submit`).
- **Trận đấu**: thiết lập (`MatchSetup`, `MatchStarter`), vòng lặp 20 Hz (`MatchEngine`), thực thể (`Entities`), độ khó (`Difficulty`), kết toán thưởng (`MatchSettlement`), quản lý và vào lại (`MatchManager`), chờ client sẵn sàng trước khi trận chạy. Phân tích: AI đồng đội, hành vi boss, đợt quái, va chạm kỹ năng, khống chế, hồi sinh, điều kiện thắng/thua.
- **Giao dịch và toàn vẹn dữ liệu**: mỗi nghiệp vụ một transaction; quà chỉ nhận một lần; đồ không nhân bản; sổ ghi giao dịch.
- **Điểm cần nêu trung thực**: chú thích ở `MatchEngine.java` cho biết client tự báo vị trí của mình và server **không kiểm tra** (chỉ giữ trong sân). Đây là một hạn chế chống gian lận cần đưa vào phần đánh giá.
- Cấu hình bằng biến môi trường `BB_*` (`ServerConfig`); không ghi giá trị bí mật.
- Bảng kích thước các lớp chính (tự đếm dòng) để thể hiện độ phức tạp.

### 7.9 Cơ sở dữ liệu (mảng C)
- Sơ đồ ER cho nhóm dữ liệu người chơi (tài khoản, ví, nhân vật, kỹ năng, kho đồ chung, trang bị, GEN, thư, nhiệm vụ, điểm danh, trận và nhật ký) và nhóm dữ liệu tĩnh (kỹ năng, quái, boss, phó bản, vật phẩm, skin, aura, cấu hình cân bằng).
- Dòng thời gian toàn bộ migration: mỗi cái thêm gì, vì sao, nhóm theo tính năng. Cơ chế tự áp dụng, kiểm tra checksum, mốc nền (`BB_BASELINE`).

### 7.10 Giao thức (mảng C)
- Phân nhóm message: kết nối, xác thực, dữ liệu người chơi, kinh tế, trang bị, thư/thông báo, huấn luyện, trận; luồng yêu cầu/đáp qua `request_id`; sự kiện server đẩy; lỗi (`ActionError`); tải tài nguyên.
- Sơ đồ tuần tự: đăng nhập, vào phó bản, một tick trận gửi về client, vào lại trận.
- So sánh message định nghĩa với message được xử lý ở mỗi phía.

### 7.11 Bảo mật và web (mảng E)
- TLS bắt buộc, bcrypt chi phí 12 dùng chung PHP/Java, token làm mới và thu hồi, giới hạn tần suất, captcha, kiểm tra đầu vào, câu lệnh tham số hóa, trang quản trị (giới hạn IP, quyền ADMIN, CSRF, nhật ký thao tác). Nêu điểm yếu nếu thấy.

### 7.12 Kiểm thử và triển khai (mảng E)
- Liệt kê lớp test và điều chúng bảo đảm; test nào cần DB. Không tự chạy test trừ khi tác giả cho phép.
- Quy trình `deploy.sh`: build, sao lưu DB (giữ 10 bản), tải lên, chỉ tải lại gói tài nguyên khi kê khai đổi, khởi động lại, chờ cổng game. Mô hình dịch vụ trên VPS Ubuntu (nêu chung, xem mục 9).

---

### 7.13 Không đưa công cụ Python vào báo cáo (quyết định của tác giả, 12/10/2026)

Các file `.py` (`BattleBallServer/tools/*.py`, `BattleBall/SourceData/tools/*.py`) là công cụ tạo dữ liệu một lần trên máy phát triển; game, server và web **không** dùng Python khi chạy. **Không phân tích, không nhắc tên các file `.py` trong báo cáo.** Chỉ mô tả **sản phẩm đầu ra** như dữ liệu của hệ thống: gói tài nguyên `web/assets/packs` và `manifest.json`, file migration SQL, bản đồ làng (`chunk.json` và ảnh), ảnh nền phó bản, bảng hiệu ứng/tư thế kỹ năng (`skill_fx.json`, `skill_poses.json`). Có thể đọc phần mô tả trong file `.py` chỉ để hiểu định dạng của dữ liệu đầu ra.

---

## 8. BỐ CỤC VÀ ĐỊNH DẠNG THEO KHUNG MẪU CỦA TRƯỜNG (BẮT BUỘC)

**Khung mẫu:** `docs/report_template/ADRnangcaobaocao.docx` (Học viện Kỹ thuật Mật mã, Khoa Công nghệ thông tin, "BÁO CÁO MÔN HỌC", nhóm L02). Mở file này trước khi viết. Phần chữ mẫu trong file (lời cảm ơn, lời mở đầu nói về Linux, Shell Script, Kernel Module) là **chữ của một đề tài khác**, chỉ để tham khảo giọng văn: phải viết lại hoàn toàn cho BattleBall, không giữ câu nào nói về Linux.

**Thứ tự các phần (giữ đúng như mẫu):**
1. **Trang bìa**: HỌC VIỆN KỸ THUẬT MẬT MÃ, KHOA CÔNG NGHỆ THÔNG TIN, BÁO CÁO MÔN HỌC, "ĐỀ TÀI:" + tên đề tài, bảng sinh viên thực hiện (giữ nguyên tên nhóm và hai thành viên có trong mẫu), "Giảng viên hướng dẫn: Nguyễn Đức Hiếu", "Hà Nội, 2026".

   **Thông tin đã được tác giả xác nhận (CONFIRMED, 12/10/2026):**
   - Môn học: **Phát triển game trên Android**.
   - Tên đề tài theo quy tắc của tác giả "tên thể loại + tên game": **"Game Hack & Slash BattleBall"** (có thể viết hoa toàn bộ trên trang bìa như mẫu). Dùng đúng tên này ở trang bìa, lời cảm ơn và lời mở đầu.
   - Giảng viên hướng dẫn: **Nguyễn Đức Hiếu**.
   - **Không** ghi phân công công việc giữa các thành viên.
2. **NHẬN XÉT CỦA GIÁO VIÊN**: giữ nguyên trang dòng chấm để giáo viên viết tay.
3. **LỜI CẢM ƠN**: viết mới cho môn **Phát triển game trên Android** và đề tài "Game Hack & Slash BattleBall", cảm ơn giảng viên Nguyễn Đức Hiếu.
4. **MỤC LỤC**: trường mục lục tự động của Word, cập nhật sau khi có nội dung.
5. **DANH MỤC HÌNH ẢNH** (thêm **DANH MỤC BẢNG** và **DANH MỤC TỪ VIẾT TẮT** nếu cần, cùng kiểu Heading 1).
6. **LỜI MỞ ĐẦU**, gồm đúng 4 mục con như mẫu: *Lý do chọn đề tài*, *Mục tiêu xây dựng*, *Phạm vi đề tài*, *Cấu trúc báo cáo* (liệt kê Chương 1 đến Chương 5, mỗi chương một câu).
7. **Năm chương nội dung** (Chương 1 đã có tên trong mẫu là "GIỚI THIỆU CHUNG"):
   - **Chương 1. GIỚI THIỆU CHUNG**: tổng quan game BattleBall, thể loại và đối tượng người chơi (mục 7.1), mô hình chơi đơn có kết nối với server có thẩm quyền, nền tảng Android, công nghệ sử dụng và lý do chọn (Unity, Java/Netty, TLS, Protobuf, MariaDB, PHP), kiến trúc tổng thể ở mức khái quát.
   - **Chương 2. PHÂN TÍCH YÊU CẦU VÀ THIẾT KẾ HỆ THỐNG**: tác nhân và ca sử dụng, yêu cầu chức năng và phi chức năng, kiến trúc client và server (mục 7.7, 7.8), cơ sở dữ liệu (ER, migration; mục 7.9), giao thức (mục 7.10), bảo mật (mục 7.11), đường ống tài nguyên.
   - **Chương 3. THIẾT KẾ GAME**: vòng lặp chơi, cơ chế chiến đấu, đồng đội AI, ba chủng tộc và kỹ năng, hợp thể, trang bị, GEN, kinh tế, phó bản và boss, nhiệm vụ, điểm danh, quà (mục 7.2); nhân vật, NPC, quái (7.3); map và môi trường (7.4); hiệu ứng kỹ năng và âm thanh (7.5); giao diện và điều khiển cảm ứng (7.6).
   - **Chương 4. CÀI ĐẶT VÀ TRIỂN KHAI**: cấu trúc mã client và server, các cơ chế cài đặt đáng chú ý (vòng lặp 20 Hz, tính chỉ số, độ khó, rơi đồ, GEN, vào lại trận, tải tài nguyên qua kết nối game, dựng nhân vật từ các phần, hiệu ứng kỹ năng), **phần riêng cho Android** (build Android, điều khiển cảm ứng, tần số khung hình, chọn tỉ lệ ảnh theo thiết bị, các sửa lỗi riêng cho Android tìm được trong lịch sử commit), trang web đăng ký và trang quản trị, triển khai trên VPS Ubuntu và quy trình cập nhật một lệnh (mục 7.12), dòng thời gian phát triển theo commit.
   - **Chương 5. KIỂM THỬ, ĐÁNH GIÁ VÀ KẾT LUẬN**: chiến lược và kết quả kiểm thử tự động, kiểm tra bằng ảnh chụp và kịch bản Play mode, đánh giá trung thực (chưa chơi thử thật để cân bằng, số liệu PROPOSED, vị trí client không được server kiểm tra, chưa đo tải), kết quả đạt được so với mục tiêu, hạn chế, hướng phát triển.
8. **TÀI LIỆU THAM KHẢO**: tài liệu chính thức của công nghệ (Unity, Android, Netty, Protobuf, MariaDB, OWASP...) và **đoạn duy nhất** ghi nguồn gốc dữ liệu game theo mục 10. Không bịa URL, tác giả, năm; không chắc thì chỉ ghi tên nguồn.
9. (Tùy chọn, hỏi tác giả) **PHỤ LỤC**: thuật ngữ game, bảng kỹ năng ba chủng tộc, danh sách NPC/quái/boss, bảng migration, sai lệch tài liệu và mã nguồn, bảng bằng chứng.

Nếu mục lục của trường không cho phép tách "Kết luận" ra ngoài 5 chương thì giữ trong Chương 5 như trên; nếu tác giả muốn, có thể thêm "KẾT LUẬN" riêng trước Tài liệu tham khảo.

**Định dạng (lấy từ file mẫu, giữ nguyên):**
- Khổ A4; lề trên 2,5 cm, dưới 2 cm, trái 3 cm, phải 2 cm; số trang ở chân trang.
- Chữ thường: Times New Roman 14 pt, giãn dòng 1,5, căn đều hai bên.
- Heading 1 (tên phần, tên chương): 16 pt, đậm, căn giữa. Heading 2: 15 pt, đậm. Heading 3: đậm.
- Chú thích hình và bảng (kiểu Caption): 12 pt, đánh số theo chương (Hình 3.2, Bảng 2.1), chú thích hình đặt dưới hình, chú thích bảng đặt trên bảng.
- Dùng đúng các kiểu (style) có sẵn trong mẫu để mục lục và danh mục hình tự động hoạt động.

**Sản phẩm định dạng:** bản cuối là **file Word dựng trên chính file mẫu** (`report/BAO_CAO_DO_AN.docx`: mở bản sao của mẫu, thay nội dung, giữ trang bìa, kiểu chữ, trường mục lục, chân trang). Nếu công cụ không tạo được DOCX thì xuất `report/BAO_CAO_DO_AN.md` với cấu trúc tiêu đề khớp từng mục ở trên, kèm danh sách hình (đường dẫn file) để tác giả dán vào mẫu.

**Sơ đồ (Mermaid, xuất ra ảnh PNG để chèn vào Word):** kiến trúc tổng thể; các gói server; các thành phần client; vòng đời kết nối; tuần tự đăng nhập; tuần tự vào phó bản; một tick trận; ER; đường ống tài nguyên; vòng lặp chơi; luồng một kỹ năng từ lúc bấm nút đến khi hiển thị; quy trình triển khai.

**Văn phong:** tiếng Việt chuẩn, ngôi "nhóm chúng em" như mẫu, giọng báo cáo kỹ thuật; mỗi chương mở bằng một đoạn giới thiệu ngắn và kết bằng một đoạn tiểu kết.

---

## 9. THÔNG TIN NHẠY CẢM: KHÔNG ĐƯA VÀO BÁO CÁO

Báo cáo gửi cho người ngoài. Không ghi:
- Địa chỉ IP, tên miền riêng, tên host của máy chủ thật. Chỉ viết "máy chủ VPS Ubuntu".
- Tên/đường dẫn khóa SSH, mật khẩu, token, giá trị biến môi trường bí mật, đường dẫn tệp chứa bí mật, chuỗi kết nối DB có mật khẩu.
- Tên tài khoản người chơi thật.

Gặp thông tin như vậy trong repo (kể cả trong script, giá trị mặc định trong mã, lịch sử commit): báo Sentinel và tác giả, không đưa vào báo cáo. Challenger tìm các mẫu này (IP, `ssh`, `.p12`, `password`, `token`, tên miền) trong bản cuối.

---

## 10. GHI NHẬN NGUỒN GỐC DỮ LIỆU GAME (CHỈ GHI MỘT LẦN)

Một phần dữ liệu của dự án lấy từ game Ngọc Rồng Online (NRO): bộ kỹ năng và số liệu kỹ năng, hình nhân vật và trang phục, bản đồ làng, quái, hiệu ứng kỹ năng, tuyệt kỹ, âm thanh.

Quy tắc trình bày (theo yêu cầu của tác giả):
- Ghi điều này **đúng một lần**, trong mục **Tài liệu tham khảo** ở cuối báo cáo: một đoạn ngắn nêu tên game nguồn, những loại dữ liệu đã dùng (liệt kê như trên) và mục đích học tập, phi thương mại (hỏi tác giả xác nhận).
- **Không nhắc lại** nguồn gốc trong các chương khác, trong chú thích hình hay trong bảng. Trong thân báo cáo gọi chung là "dữ liệu nguồn" hoặc "tài nguyên game" khi cần.
- Không viết rằng các tài nguyên đó do tác giả tự vẽ hay tự sáng tác. Phần tác giả tự thiết kế và tự cài đặt (kiến trúc, mạng, server, cơ sở dữ liệu, cơ chế trận, kinh tế, web, công cụ, triển khai) được trình bày bình thường trong các chương.
- Nếu tên trong mã nguồn có chữ "Nro" (ví dụ `NroMannequin`), giữ nguyên tên kỹ thuật khi trích, không bình luận thêm.

---

## 11. CHECKLIST CHẤT LƯỢNG (Critic, Challenger, Auditor, Success Auditor chặn mốc nếu chưa đạt)

- [ ] Khẳng định kỹ thuật quan trọng đều có bằng chứng và nhãn đúng.
- [ ] Số liệu đếm được tự đếm lại và nhất quán giữa các chương.
- [ ] Phân biệt "đã cài đặt" và "chỉ có trong thiết kế"; số liệu PROPOSED không trình bày như kết quả kiểm chứng.
- [ ] Đủ các nhóm nội dung: thể loại, cơ chế, nhân vật, map, hiệu ứng kỹ năng và âm thanh, giao diện, client, server, DB, giao thức, bảo mật, kiểm thử, triển khai.
- [ ] Đúng thứ tự phần và định dạng của khung mẫu (mục 8); không còn câu nào của chữ mẫu cũ (Linux, Shell Script, Kernel Module); mục lục và danh mục hình cập nhật được.
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

1. (Đã trả lời) Môn, tên đề tài, giảng viên, phân công: xem mục 8.
2. (Đã trả lời) Không ghi phân công công việc.
3. Quy định của trường về khai báo công cụ AI và khai báo tài nguyên dùng lại từ game gốc? Xác nhận dự án phi thương mại?
4. Có được chạy test và build cục bộ để lấy số liệu thật cho chương Kiểm thử? Nếu có, lệnh và biến môi trường nào?
5. Phần muốn nhấn mạnh nhất (ví dụ server có thẩm quyền và mô phỏng trận, hay hệ thống trang bị và GEN, hay hiệu ứng kỹ năng, hay triển khai)?
6. Có ảnh chụp/video: làng, NPC, bảng nhân vật, huấn luyện, phó bản, boss, tuyệt kỹ, hợp thể, trang quản trị? Nếu chưa, đội ghi danh sách vị trí cần ảnh.

---

## 14. SẢN PHẨM CUỐI CÙNG

Thư mục `report/` gồm: `00_ke_hoach.md`, `00_so_lieu.md`, `00_thuat_ngu.md`, `01_server.md`, `02_client.md`, `03_du_lieu_giao_thuc.md`, `04_the_loai_co_che.md`, `05_ha_tang_bao_mat_kiem_thu.md`, `06_nhan_vat_map_hieu_ung.md`, `99_ra_soat.md`, **`BAO_CAO_DO_AN.docx`** (sản phẩm chính: báo cáo dựng trên khung mẫu của trường theo mục 8), `BAO_CAO_DO_AN.md` (cùng nội dung, dạng văn bản để rà soát) và thư mục `hinh/` chứa ảnh chèn vào báo cáo, mã Mermaid và ảnh PNG của các sơ đồ. Kết thúc bằng tóm tắt ngắn: đã làm gì, điều gì chưa xác định, lỗi tồn đọng của checklist và câu hỏi còn chờ tác giả.
