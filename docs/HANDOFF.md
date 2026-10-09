# HANDOFF: bàn giao cho phiên làm việc mới

> Cập nhật: 2026-10-07. Đọc file này **sau** `E:\PRJNRO\CLAUDE.md`, rồi đọc các tài liệu nguồn sự thật ở mục 3.
> Mục đích: phiên mới có thể làm tiếp mà không cần hội thoại cũ. Mọi quyết định quan trọng đã nằm trong file `docs\`; file này chỉ chỉ đường, tóm tắt và ghi các quy ước làm việc.

---

## 1. Dự án và hiện trạng

| Thư mục | Vai trò | Hiện trạng (đã xác minh) |
|---|---|---|
| `E:\PRJNRO\Teamobi2026` | **Tham khảo** (server Java + web PHP của game NRO) | Chỉ đọc. Đã phân tích kiến trúc, mạng, protocol, database, gameplay: `docs\*\Teamobi2026.md` |
| `E:\PRJNRO\NroUnity` | **Tham khảo** (client Unity của NRO) | Chỉ đọc. Đã phân tích: `docs\*\NroUnity.md` |
| `E:\PRJNRO\BattleBall` | **Dự án đang phát triển** (client Unity 2022.3.62f2, URP 2D) | Hiện **chỉ có công cụ xem thử trang phục/rig nhân vật NRO** (`Assets\NroMannequin`): 414 trang phục, 984 part, 33 tư thế. **Chưa có mạng, đăng nhập, gameplay, lưu dữ liệu.** Không sửa nếu chưa được yêu cầu |
| `E:\PRJNRO\BattleBallServer` | **Dự án đang phát triển** (server) | **Thư mục trống**, chưa có code. Mới có thiết kế trong `docs\` |
| `E:\PRJNRO\docs` | Cơ sở kiến thức và thiết kế | Chính là thứ phiên này đã xây dựng |

Không có git repository ở `E:\PRJNRO`; môi trường Windows 11; ngôn ngữ làm việc với người dùng: **tiếng Việt**.

---

## 2. Quy ước làm việc (quan trọng)

1. **Quyền sửa (cập nhật 2026-10-07, người dùng cấp):** được sửa `BattleBall` (kể cả `Assets\`, `SourceData\`) và `BattleBallServer` **trong phạm vi việc người dùng giao**; `Teamobi2026` và `NroUnity` vẫn **chỉ đọc**. Được build và chạy Unity batch/verify để kiểm chứng thay đổi của chính mình (người dùng cho phép 2026-10-07; không mở Unity thứ hai nếu Editor đang mở); không commit/push/cài phụ thuộc nếu chưa được yêu cầu; không sửa tay `Library\`, `Logs\`, `UserSettings\`. Chi tiết ở `CLAUDE.md`. Với phân tích thuần túy vẫn chỉ ghi vào `docs\`. **Chưa có giai đoạn implementation nào được yêu cầu.**
2. **Nhãn bằng chứng bắt buộc:** `CONFIRMED` (người dùng chốt hoặc đã xác minh trong source), `PROPOSED` (đề xuất mới của trợ lý), `INFERRED` (suy luận), `UNKNOWN`, `OPEN` (cần chốt). Không biến suy luận thành sự thật; giữ nguồn (đường dẫn file) cho kết luận quan trọng.
3. **Cách hỏi người dùng:** người dùng từng yêu cầu **giải thích rõ câu hỏi bằng ngôn ngữ đơn giản và ví dụ trước khi hỏi**; hỏi **tối đa 3 đến 4 câu mỗi lượt** bằng công cụ hỏi có lựa chọn, kèm tùy chọn khuyến nghị. Người dùng hay trả lời bằng "tiếp" hoặc "hỏi tiếp" nghĩa là hỏi thêm các câu OPEN còn lại.
4. **Mỗi quyết định người dùng chốt phải được ghi vào đúng tài liệu ngay** (đổi nhãn `PROPOSED/OPEN` sang `CONFIRMED`, cập nhật danh sách câu hỏi mở), rồi báo lại ngắn gọn: đã chốt gì, hệ quả, còn mở gì.
5. **Không sửa file GDD của người dùng** (`docs\gameplay\game-design-doc.md`) nếu họ chưa bảo. (Một lần người dùng yêu cầu xóa cụm "đánh quái nhỏ nhận EXP", đã thực hiện. Ngày 2026-10-07 người dùng yêu cầu sửa mục 3, 4, 5 của GDD sang bộ skill NRO, đã thực hiện.)
6. **Báo cáo trung thực:** nếu không kiểm chứng được thì nói rõ; khi người dùng chỉnh lại thì nhận và sửa, không bảo vệ bản cũ.
7. **Mẹo công cụ:** lệnh `Bash` heredoc với nội dung tiếng Việt dài hay lỗi. Cách ổn định: **ghi script Python vào thư mục scratchpad bằng công cụ Write rồi chạy**; dùng `PYTHONIOENCODING=utf-8`; thay thế chuỗi bằng hàm `rep(a, b)` và báo `MISSING` khi không khớp. Không quét cả `Teamobi2026` bằng `find` (hàng chục nghìn file, quá chậm).

---

## 3. Tài liệu nguồn sự thật cho BattleBall (đọc theo thứ tự)

1. `docs\gameplay\game-design-doc.md`: GDD của người dùng (hack & slash, 3 chủng tộc, đội 3 người, hợp thể, trang bị, EXP...). **Giai đoạn thiết kế, chưa hoàn chỉnh.**
2. `docs\database\BattleBall.md`: **mô hình dữ liệu và hầu hết quyết định gameplay** (EXP, chỉ số theo cấp, túi 2.000 món, hợp thể, bông tai Potara, kinh tế...). Phần "Quyết định" và "OPEN QUESTIONS" ở cuối.
3. `docs\protocol\BattleBall.md`: **hợp đồng message client ↔ server**, luồng di chuyển/chiến đấu/skill, vào trận, tạm dừng/vào lại, Android (mục 14), quyết định định dạng.
4. `docs\networking\BattleBall.md`: kiến trúc mạng (Netty, luồng, vòng đời session, đăng nhập tự động bằng token).
5. `docs\architecture\BattleBallServer.md`: kiến trúc server tổng thể. **Phần đầu viết khi chưa biết game chơi đơn; các ghi chú "Cập nhật" ngay sau mục 2 là đúng; các mục Movement/Sync/Visibility cho nhiều người chơi cần xem lại**.

**Tài liệu cũ, một phần lỗi thời** (viết trước khi biết game chơi đơn, đã có ghi chú trỏ sang bản mới): `docs\protocol\BattleBallServer.md`, `docs\networking\BattleBallServer.md`.

**Tài liệu tham khảo reference** (đã xác minh trong source): `docs\*\Teamobi2026.md`, `docs\*\NroUnity.md`, `docs\*\COMPARISON.md`.

---

## 4. Tóm tắt quyết định đã chốt (chi tiết ở các file trên)

**Game:** chơi đơn, cần mạng liên tục, không PvP. 3 nhân vật mỗi tài khoản (Xayda, Namec, Trái Đất), tự tạo sẵn cấp 1. Đội trận: 1 nhân vật điều khiển + 2 AI do server điều khiển (hoặc 1 hợp thể + 1 AI).
**Kiến trúc:** server **Java + Netty, TCP, TLS, Protobuf**; client Unity **Android**. Server mô phỏng trận **20 Hz**, mỗi trận một tác vụ trên pool luồng cố định; DB pool riêng.
**Chiến đấu:** 2D, di chuyển 8 hướng, **map chiến đấu không trọng lực**, mặc định bay; trạng thái hiển thị di chuyển (`FLY`=`Jump_Up` 2 giây, `FLY_MOVE`=`Fly_Horizontal`, bay xuống `Fall_Down`→`Fly_Horizontal`, bay lên `Jump_Up`→`Fly_Horizontal`) **chỉ là hiển thị phía client, không gửi lên server**. `RUN`/`JUMP` chỉ ở **làng tân thủ** (chơi đơn, NPC cửa hàng). Hai chế độ trúng: **hướng** (vùng/đường thẳng, dễ bị né) và **chọn mục tiêu** (đuổi theo); hướng skill là **góc tự do**; đòn thường **tự đấm**, hết frame thì gây sát thương một lần; mọi skill dùng cả hai chế độ trừ skill tự buff/hồi. Server mô phỏng đạn nhẹ theo dữ liệu skill; **client báo vị trí và mục tiêu, server không kiểm tra** (như Teamobi2026).
**Vị trí/tọa độ:** pixel gốc NRO, `i32`, trục Y xuống; tile 24 px; chunk minimap 16×16 tile; minimap lưu server theo tài khoản.
**Thưởng:** boss và mob rơi item/vàng **tự động vào túi**, ghi ngay; **EXP theo số boss đã hạ** (thua vẫn có, thắng +20%), kill streak, hệ số quá/thấp cấp; **cấp tối đa 100**, bảng EXP `100 × L²`; chỉ số cấp 100: HP/Ki 575k, ATK 26k, Giáp 22k, chí mạng 100% sát thương và 10% tỷ lệ (cơ bản, **không trần tổng**; tỷ lệ chí mạng chỉ kẹp ≤ 100%).
**Kho đồ:** giới hạn **2.000 món** (đồ riêng lẻ), đầy thì mất đồ rơi, có cảnh báo; bán/hủy hàng loạt ≤ 200 món/lô, giá bán thấp, hủy cần xác nhận; **không có hòm thư, không khóa đồ**; 10 ô mỗi nhân vật (5 trang bị + 1 skin + 4 vật phẩm hỗ trợ vĩnh viễn); skin = costume id, sở hữu theo tài khoản, khóa theo chủng tộc.
**Hợp thể:** cần **Bông tai Potara** (100 mảnh, mua bằng vàng có giới hạn mỗi ngày hoặc rơi từ boss một phó bản; **mỗi tài khoản 1 cái**, không tiêu hao, ngoài 10 ô), cả 2 nhân vật **cấp 50**, cặp chốt khi vào trận đến hết trận, nhân vật phụ mất skill; chỉ số hợp thể = cộng chỉ số cuối của 2 nhân vật rồi nhân buff.
**Tài khoản:** đăng ký cả trong game và qua **web PHP ghi trực tiếp vào DB**; không cần email; tên 4 đến 20 ký tự (a-z, 0-9, `_`), mật khẩu ≥ 8 ký tự; **bcrypt cost 12**, tối đa 64 byte; token đăng nhập dài hạn ~30 ngày lưu trên máy; đăng nhập chồng: kick phiên cũ.
**Mạng:** heartbeat 5 s, timeout 15 s; mất kết nối giữa trận thì **tạm dừng ≤ 30 s, tối đa 2 lần mỗi trận**, token vào lại 30 s, quá hạn thì hủy trận và giữ thưởng đã nhận; client gửi tối đa 20 gói/giây; DB lỗi giữa trận thì **hủy trận và báo lỗi**; gói tối đa 256 KB; ≤ 20 kết nối mỗi IP; chống gian lận chỉ bằng **log bất thường, xử lý thủ công**; hồi sinh tối đa 2 lần mỗi trận.

---

## 5. Việc còn mở (cần dữ liệu hoặc quyết định của người dùng)

**Cần dữ liệu từ người dùng (chưa thể chốt):**
- 4 ô vật phẩm hỗ trợ vĩnh viễn mỗi nhân vật (loại, hiệu ứng, nguồn có được); item trang trí vĩnh viễn có phải costume không.
- Danh sách phó bản (phó bản nào rơi mảnh Potara, độ khó, cấp khuyến nghị, số boss), giá mua mảnh bông tai và giới hạn mỗi ngày.
- Giá bán từng món; số mảnh ghép các món đồ khác; tỷ lệ rơi mọi item.
- Vật cản trong map chiến đấu (người dùng sẽ bổ sung); dữ liệu map.
- Nguồn dữ liệu costume/sprite chính thức: người dùng đã chỉ ra 3 vị trí asset (font sát thương/âm thanh/icon/UI ở `C:\Users\cuonghayho\Desktop\data`; asset ở `Teamobi2026\SRC\data`; tọa độ frame/skin trong `Teamobi2026\SRC\sql\nro1.sql`), đã ghi ở `docs\database\BattleBall.md` mục 9. **Đã chốt 2026-10-07:** sao chép asset sang BattleBall (chưa thực hiện, chờ yêu cầu thực thi); thang x1 đến x4 chọn theo độ tương thích thiết bị ở lần tải/nạp game đầu; BattleBall là sản phẩm báo cáo môn học, phi thương mại. **Đã sao chép xong** toàn bộ vào `E:\PRJNRO\BattleBall\SourceData\` (`data\`, `ui_audio\`, `tables\nro1.sql`; chi tiết ở `docs\database\BattleBall.md` mục 9). Chốt thêm: tiêu chí thang = độ phân giải + RAM + DPI; asset tải từ server lần đầu. **Đã xuất `nro1.sql` ra JSON** (18 bảng tĩnh, `SourceData\tables\json\`, script `SourceData\tools\export_sql_to_json.py`); `NroSpriteResolver` đã trỏ sang `SourceData` và kiểm chứng 9/9. Còn mở (PROPOSED chờ xác nhận): phân phối qua HTTPS với manifest, nạp JSON vào MariaDB; client vẫn đang đọc bằng đường dẫn tuyệt đối cũ (chưa đổi).
- Công thức sát thương, bảng skill chi tiết (dữ liệu tĩnh), thời lượng frame đòn thường, tầm đấm, hộp va chạm skill. *(Đã có bản thiết kế bảng skill PROPOSED ở `docs\database\BattleBall.md` mục 10: `SkillTemplate` + `SkillLevel` + `effects` JSON, ví dụ Kamehame; chờ người dùng xác nhận và điền số vùng trúng/nâng cấp; file seed `BattleBall\SourceData\tables\seed\skills_seed.json` (16 skill, 88 cấp) đã sinh từ GDD, mục 10.5. **Cập nhật 2026-10-07: người dùng chọn bộ skill NRO và bỏ cơ chế skill cũ của GDD; thiết kế hiện hành ở mục 10.6 và 10.7, seed `skills_seed.json` nay là 25 skill/181 cấp lấy từ NRO (Ki giảm 10 lần, nâng cấp chỉ vàng, tuyệt kỹ tối đa cấp 9, có `params` giải nghĩa từ code); mục 10.2 đến 10.5 lỗi thời; **GDD đã được sửa theo yêu cầu người dùng (mục 3, 4, 5; có bản cũ sao lưu ở scratchpad phiên làm việc).** Năm điểm mở (giá vàng 130 dòng cấp, Ki tuyệt đối, sát thương tuyệt kỹ, hợp thể, hộp va chạm/tốc độ đạn) đã được trợ lý tối ưu theo yêu cầu "tự tối ưu lại": mục 10.8 (PROPOSED, số do trợ lý chọn); seed dùng `kiCostPct`, `goldCost`, `length/width/radius/projectileSpeed/travelMs/slackPx`. GDD đã chỉnh toàn bộ cho khớp phương án hiện tại. **Mục 10.9 (cùng ngày): kinh tế vàng (Gm(R) = 1.000 x R, Potara 15.000/mảnh, 20 mảnh/ngày, món = 10 mảnh...), `Max_ATK_Total` giữ 500.000 và HP mob/boss tính lại từ thời gian hạ mục tiêu (bảng % cũ quá thấp với skill NRO; script `tools\balance_model.py`), AI dùng skill theo thứ tự hồi chiêu (bỏ `aiTactic`), hợp thể không bonus; GDD mục 2, 6, 7 đã cập nhật. Mục 10.10: U = 75% (15 bậc x 5%), nguồn và giá đá quý, công thức giáp `Giáp/(Giáp+50.000)` và thứ tự tính sát thương (GDD 7.4), HP đã tính lại có giáp, quy trình hiệu chỉnh bằng dữ liệu thật (chưa có dữ liệu nên chưa làm được). Mục 10.11: Ki hồi 5% Ki tối đa/giây; nhịp tấn công boss (bộ chiêu, skill focus không né vào đơn vị nhiều máu nhất, vùng và quay về hồi máu; GDD 7.5). Mục 10.17: skin hợp thể 885/1566/1815 = fusion_race ANY; catalog client đã thu gọn 414 → 221 costume và 984 → 569 part (bản đầy đủ ở `SourceData\tables\backup\`, Unity verify 9/9 PASS); **đã có file SQL đầu tiên trong BattleBallServer**: `db\migrations\V001__static_data_schema.sql` (13 bảng, stat_type.enabled = công tắc kích hoạt, skin_kind/skin_rarity/skin_group_price là bảng tra cứu admin chỉnh được) và `V002__static_data_seed.sql`; chưa chạy trên MariaDB thật (chỉ kiểm cấu trúc bằng SQLite). Mục 10.16: vòng duyệt skin thứ hai: còn 221 costume (86 SHOP, 12 FUSION_FORM, 64 BOSS_MOB, 25 NPC, 34 COMPONENT), đã xóa thêm 100 skin ẩn (tổng 193 bị xóa khỏi seed và HTML; chưa xóa khỏi catalog client); máy chủ thử đã tắt. Mục 10.15: đã nhập kết quả duyệt skin của người dùng (CSV; xóa 93 skin HIDDEN khỏi seed và HTML, còn 321; chưa xóa khỏi catalog client), thêm loại BOSS_MOB, luật skin hợp thể (mua được; chỉ nhân vật chủ khi dùng Bông tai mặc được, AI không mặc), catalog 49 chỉ số của skin đọc từ `NPoint.addOption` (27 dùng được, 13 hiệu ứng, 9 không áp dụng; `tables\seed\skin_stat_catalog.json`), HTML có bộ sửa chỉ số. Mục 10.14: trang `BattleBall\SourceData\skin_browser.html` render 414 skin IDLE để người dùng phân loại (loại, độ hiếm, giá, chỉ số; xuất JSON gửi lại); đã phân loại trước theo dữ liệu NRO: SHOP 313 (105 phổ thông / 154 hiếm / 54 rất hiếm), FUSION_FORM 6, COMPONENT 95. Mục 10.13: dạng khỉ của Biến hình (Xayda) = part đầu 192/195/196/199/197/200/198 theo cấp 1 đến 7 + thân 193 + chân 194, frame 2200 đến 2243 (4 icon 2202/2209/2210/2231 không part nào dùng); không có trong catalog 414 (cần loại khỏi danh mục skin); seed `skill_forms.json`; đã sửa nhận định sai về costume 1788. Mục 10.12: phân loại 414 costume (0 SKILL_FORM, 6 FUSION_FORM td/xd/nm của Potara, 95 COMPONENT, 313 SHOP) và định giá theo nhóm x độ hiếm (100 đến 1.600 đá quý, chỉ số cộng +1/3/5%), sửa được trên admin; seed `SourceData\tables\seed\skin_pricing.json`.**)*

**Chi tiết kỹ thuật chưa chốt (trợ lý có thể đề xuất mặc định):** token đăng nhập dài hạn (một hay nhiều token mỗi tài khoản, hạn trượt), kích thước ảnh chụp khi vào lại trận, số luồng pool, TLS trên Android và ghim chứng chỉ, kiểm tra Protobuf trên Unity IL2CPP, giới hạn tổng số kết nối, hạ tầng triển khai, công nghệ DB cụ thể.

**Rủi ro đã ghi nhận (không đổi quyết định):** server không kiểm vị trí và chỉ chống gian lận bằng log; túi đầy thì mất đồ; DB lỗi thì hủy trận; token 30 giây trên mạng yếu có thể không kịp; hợp thể cộng đủ chỉ số có thể mạnh; chí mạng không trần.

---

## 6. Gợi ý bước tiếp theo
1. Tiếp tục hỏi các câu OPEN còn lại (giải thích rõ từng câu trước khi hỏi). **Mới nhất (2026-10-07):** đã có `V003__player_data_schema.sql` (bảng người chơi, mọi thứ theo từng nhân vật), xem `docs\database\BattleBall.md` mục 10.18; V001 đến V004 đã chạy thử thành công trên MariaDB 10.4.32 thật (mục 10.19, 10.20; V004 là `item_template`/`material_template`). V005/V006 (vật phẩm hỗ trợ: pet, giáp tập luyện, cờ/cánh/vũ khí, sách tuyệt kỹ) cũng đã chạy thử thành công (mục 10.22). V007 (thành tích nhận avatar) và trang admin PHP tại `BattleBallServer\admin\` đã làm và kiểm chứng (mục 10.23). Việc kế tiếp (còn mở): thử lại trên MariaDB 10.6+, HTTPS cho admin, thêm dòng mới trên admin, cách nhận sách tuyệt kỹ, bảng rơi đồ. (Ghi chú cũ, đã xong: cách nhận avatar ngoài nhóm mặc định, rồi thiết kế trang admin; thử lại trên MariaDB 10.6+.)
2. Khi người dùng có danh sách phó bản và vật phẩm: ghi vào dữ liệu tĩnh (mục thiết kế dữ liệu), cập nhật giá trị skill/boss.
3. Khi người dùng yêu cầu bắt đầu implementation: chỉ làm khi được yêu cầu rõ; đề xuất thứ tự: schema `.proto` dùng chung → khung mạng Netty (handshake, đăng nhập, heartbeat) → mô hình DB và migration → vòng lặp trận 20 Hz tối thiểu → client mạng trên Android (bỏ phụ thuộc đường dẫn tuyệt đối của `NroSpriteResolver`/`NroCostumeCatalog`).
4. ~~Dọn dẹp tài liệu cũ~~ **Đã làm 2026-10-07:** các mục multiplayer trong `docs\architecture\BattleBallServer.md` đã gắn nhãn LỖI THỜI (bảng yêu cầu, module, mục 6, 7, 11, 13, 14). Mục 8, 9, 10, 11 cũng đã cập nhật; mục 12 (cây thư mục đề xuất, có `Sync/` và `World/Map`) chưa chỉnh vì chỉ là gợi ý chưa tạo. Hai file cũ `docs\protocol\BattleBallServer.md` và `docs\networking\BattleBallServer.md` đã thêm ghi chú lỗi thời ở đầu file.

**Quyết định mới 2026-10-07:** game chơi đơn hoàn toàn, không có người chơi khác ở bất kỳ map nào (zone riêng mỗi trận/làng); token đăng nhập: nhiều token (mỗi thiết bị một), hạn trượt 30 ngày; cảnh báo túi ở 1.800 món; database MariaDB/MySQL (InnoDB); tổng kết nối tối đa 500 (config); mốc đổi ngày 00:00 UTC+7; vào lại trận gửi toàn bộ trạng thái trận; pool mô phỏng = số nhân CPU (config); TLS: tự ký khi dev, Let's Encrypt khi chạy thật, chưa ghim chứng chỉ; deploy: một VPS Linux (Java + MariaDB + web PHP); dữ liệu cân bằng nhập qua trang admin nhập tay (cần thiết kế đăng nhập admin, version, nhật ký sửa).

**Cập nhật 2026-10-07 (cuối phiên):** server Java đã hoàn thành các mục OPEN (ảnh chụp dữ liệu người chơi, dữ liệu tĩnh, kho/cửa hàng/ghép đồ, trận 20 Hz có thưởng, kết nối lại giữa trận, migration tự động; 52 bài kiểm tra), thêm trang đăng ký web PHP (`BattleBallServer/web`, V023; xem `docs/architecture/BattleBallServer.md` mục 15.3). Bước kế tiếp: máy khách Unity kết nối/đăng ký/đăng nhập (cần sinh C# từ `battleball.proto` và Google.Protobuf: hỏi người dùng trước khi tải NuGet).

**Cập nhật 2026-10-07 (máy khách):** `BattleBall/Assets/BattleNet` đã có lớp mạng C# (kết nối TLS, đăng ký, đăng nhập, nhận snapshot); kiểm bằng .NET 8 với server thật, chưa kiểm trong Unity Editor/Android (xem `docs/architecture/BattleBallServer.md` 15.4). Bước kế tiếp: bấm `BattleBall/Verify Network` trong Editor; cảnh trận đấu nhận `MatchFrame`; UI thật.

**Cập nhật 2026-10-07 (UI đăng nhập):** `Assets/Scenes/Login.unity` (scene đầu của build) với `BbLoginUi`; xem `docs/architecture/BattleBallServer.md` 15.5. Cần người dùng bấm thử trong Play mode (bật server trước). Bước kế tiếp: màn chờ chọn nhân vật, tự đăng nhập bằng token, cảnh trận đấu.

**Cập nhật 2026-10-07 (tài nguyên từ server):** sprite trang phục tải từ server (gói zip qua HTTP từ `BattleBallServer/web/assets`, đóng gói bằng `tools/build_assets.py`, chạy thử bằng `tools/run_web.ps1` cổng 8081); `HelloAck` thêm `asset_base_url` và `asset_version`; máy khách `BbAssetUpdater` tải theo thang x2 đến x4; xem `docs/architecture/BattleBallServer.md` 15.6. Bước kế tiếp: thử trên Android thật, các nhóm tài nguyên còn lại, HTTPS cho bản thật.

**Cập nhật 2026-10-07 (cấu hình và Android):** địa chỉ server của máy khách nằm ở `Assets/BattleNet/Resources/BbConfig.asset`; server gửi tên máy chủ trong `HelloAck`; Android đã chuyển IL2CPP + ARM64/ARMv7 và build APK dev thành công (chưa chạy trên máy thật); xem `docs/architecture/BattleBallServer.md` 15.7. Còn mở: chạy thử APK trên điện thoại, mã gói và tên công ty thật, target SDK, `targetFrameRate`, giới hạn kết nối theo IP, hệ số EXP sự kiện.

**Cập nhật 2026-10-07 (menu chính):** scene `Menu.unity` theo bố cục bảng NRO với 3 tab Trái Đất, Xayda, Namếc; `BbSession` giữ kết nối giữa Login và Menu; xem `docs/architecture/BattleBallServer.md` 15.8. Bước kế tiếp: kiểm Play mode với server, hình vật phẩm trong ô, nối các nút (Vào trận, Kỹ năng, Cửa hàng, Trang phục), khóa hướng ngang cho Android.

**Cập nhật 2026-10-08 (kho chung, hòm thư, làng):** kho đồ chung cho ba nhân vật gồm cả vàng và đá quý (V026), hòm thư/thông báo/đổi mật khẩu (V027, trang admin tương ứng), popup cải trang (mặc skin; skin hợp thể chỉ cho nhân vật chủ), tab "Chức năng", làng tân thủ với bản đồ tạm, nhân vật chủ và hai AI, NPC, cần điều khiển, menu thu vào được; các nút VÀO TRẬN, Kỹ năng, Cửa hàng, Trang phục và nút Đăng xuất góc phải đã bỏ theo yêu cầu. Xem `docs/architecture/BattleBallServer.md` 15.9 và `docs/database/BattleBall.md` 10.36, 10.37. Bước kế tiếp: đường vào trận qua NPC "Cổng phó bản" (màn chọn phó bản, gửi cặp hợp thể ở `StartMatchRequest`), chunk map thật, hình vật phẩm trong ô, kiểm Play mode và Android.
