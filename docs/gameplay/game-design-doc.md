# Tài liệu thiết kế game (Game Design Document)
### Thể loại: Hack & slash — đánh quái dồn dập, boss theo khu vực, hệ thống trang bị chuyên sâu

> *Cập nhật 2026-10-07 theo yêu cầu của chủ dự án: tài liệu đã được chỉnh cho khớp phương án hiện tại. Mục 3, 4 và 5 dùng **bộ skill của NRO** (bỏ cơ chế skill cũ; Ki giảm 10 lần; nâng cấp skill chỉ bằng vàng; tuyệt kỹ tối đa cấp 9). Mục 2 (hợp thể) cập nhật theo luật đã chốt. Bản cũ của các mục này không còn trong file. Các quyết định kỹ thuật và dữ liệu chi tiết nằm ở `docs\database\BattleBall.md` và `docs\protocol\BattleBall.md`.*

---

## 1. Tổng quan gameplay

- **Chơi đơn, cần kết nối mạng liên tục:** không có người chơi khác, không kết bạn, không PvP; server lưu dữ liệu và tính kết quả trận. Mỗi tài khoản có **3 nhân vật** (Xayda, Namec, Trái Đất), cấp tối đa **100**.
- Chiến đấu 2D, di chuyển 8 hướng, **bản đồ chiến đấu không trọng lực** (nhân vật mặc định bay); chạy và nhảy chỉ có ở làng tân thủ. Mỗi skill tấn công có hai chế độ trúng: **theo hướng ngắm** (dễ né) và **theo mục tiêu chọn** (đuổi theo); đòn thường tự đấm khi có mục tiêu trong tầm.
- Quái nhỏ dồn ra ồ ạt từ 1 hướng khi người chơi tiến vào khu vực.
- Bản đồ có nhiều khu vực boss riêng biệt, được đánh dấu chấm đỏ trên minimap.
- Minimap tự động lưu địa hình mỗi khi người chơi load chunk mới.
- Boss khi vào trạng thái tấn công,nếu người chơi ra khỏi biên một đoạn, sẽ đuổi thêm 1 đoạn sau đó quay lại vị trí cũ và hồi máu.
- Lên cấp → tăng: Máu, Ki (năng lượng dùng kỹ năng), Giáp, Tấn công, khả năng hồi máu (phụ thuộc Ki — hết Ki phải dùng vật phẩm hỗ trợ; máu cũng hồi được bằng vật phẩm). **Ki hồi rất nhanh: 5% Ki tối đa mỗi giây** (cố định, đề xuất 2026-10-07 theo yêu cầu "hồi nhiều Ki mỗi giây"); hồi máu 0,5% HP tối đa mỗi giây chỉ chạy khi còn Ki.


---

## 2. Đội hình

### Đội hình 3 người
- 1 người chơi điều khiển trực tiếp + 2 nhân vật AI tự động đánh và dùng kỹ năng.
- **Cách AI dùng skill (chủ dự án chốt: "hồi chiêu nào thì dùng chiêu đấy theo thứ tự"):** không có cấu hình chiến thuật; mỗi lượt quyết định AI duyệt danh sách skill của mình **theo thứ tự cố định** và dùng **skill đầu tiên đã hết hồi chiêu, đủ Ki và thỏa điều kiện**. Thứ tự (đề xuất): từ tuyệt kỹ, khiên, các skill hỗ trợ/khống chế, Quả cầu/Makankosappo, chưởng, rồi đòn đấm cơ bản ở cuối (theo `slot` của NRO giảm dần: 8, 7, 6, 5, 4, 3, 2, 1, 0), nên đòn đấm luôn là phương án dự phòng. Điều kiện (đề xuất): skill tấn công/khống chế cần có mục tiêu trong tầm (ưu tiên mục tiêu người chơi đang chọn, nếu không có thì mục tiêu gần nhất); Trị thương khi có đồng đội dưới 50% HP hoặc đã chết; buff (Biến hình, Huýt sáo, Tái tạo năng lượng, Khiên) chỉ khi chưa bật; Kaioken chỉ khi HP trên 50%; Đẻ trứng khi chưa có thú; **AI không dùng Tự phát nổ** (tự sát).
- Cần cơ chế aggro/threat rõ ràng để tránh mob dồn hết vào AI hoặc dồn hết vào người chơi.
- Nhân vật AI khi "ngất" nên có cơ chế hồi hoặc giảm hiệu quả tạm thời thay vì loại khỏi trận, tránh cảm giác thua vì AI yếu.

### Đội hình hợp thể (2 người)
- 1 người ở trạng thái hợp thể, mang **tổng chỉ số của cả 2 nhân vật** (cộng chỉ số cuối của hai nhân vật rồi mới nhân các buff).
- **Skill:** hợp thể dùng **toàn bộ bộ skill của nhân vật chủ** (nhân vật đang điều khiển, bộ skill NRO ở mục 3); **nhân vật phụ mất toàn bộ skill của mình** trong thời gian hợp thể. Ví dụ Trái Đất (chủ) hợp thể với Xayda: có skill của Trái Đất, mất skill của Xayda. Đội khi đó là **1 hợp thể + 1 nhân vật AI**.
- **Điều kiện:** cần đạo cụ **Bông tai Potara** (ghép từ 100 mảnh; mỗi tài khoản chỉ có 1 cái; không bị tiêu hao) và **cả 2 nhân vật đều đạt cấp 50**.
- **Thời gian:** cặp hợp thể được cố định khi bắt đầu trận và kéo dài **đến hết trận**; không có giới hạn thời gian, không tiêu hao Ki liên tục, không tách được giữa trận.
- **Cân bằng (đề xuất 2026-10-07):** **không thêm bonus % nào, không tự nâng cấp nhân vật phụ.** Chỉ số hợp thể = tổng chỉ số cuối của hai nhân vật (mỗi nhân vật dùng cấp, đồ và EXP riêng của mình, cấp tối thiểu 50), rồi mới nhân buff. Lý do: cùng trang bị, đội 3 nhân vật có tổng ATK 3A và tổng HP 3H trên 3 thân, 3 nhân vật cùng tung skill; đội hợp thể (hợp thể + 1 AI) có tổng ATK (2A + A) = 3A và tổng HP 3H, nhưng chỉ 2 nhân vật tung skill (hợp thể chỉ dùng bộ skill của chủ) và dồn HP vào 2 thân. Hai bên ngang nhau về tổng chỉ số, hợp thể đổi sự linh hoạt (mất skill nhân vật phụ) lấy độ trâu tập trung nên **không cần buff thêm**. Rủi ro còn lại (hợp thể quá mạnh khi cấp nhân vật phụ cao) được giảm bằng ràng buộc đạo cụ (1 cái mỗi tài khoản, 100 mảnh). Đề xuất cũ "cộng theo %, giới hạn thời gian, tiêu hao Ki liên tục, mất một phần skill hỗ trợ" **không còn áp dụng**.

---

## 3. Ba chủng tộc

> **Cập nhật 2026-10-07 (theo yêu cầu của chủ dự án):** bộ skill của BattleBall nay dùng **bộ skill của NRO** (Teamobi2026) và **bỏ cơ chế skill cũ**. Mọi con số dưới đây lấy từ file dữ liệu `BattleBall\SourceData\tables\seed\skills_seed.json` (nguồn: bảng `skill_template` của NRO và công thức trong mã server Teamobi2026). Điều chỉnh so với NRO: (1) **chi phí Ki của mọi skill giảm 10 lần**; skill NRO tính Ki tuyệt đối được **đổi sang % Ki tối đa** để không phụ thuộc cấp nhân vật; (2) **nâng cấp skill chỉ dùng vàng** (không dùng điều kiện sức mạnh của NRO); (3) **tuyệt kỹ tối đa cấp 9**; (4) Ma phong ba không còn khả năng làm người dùng chết. Các con số do trợ lý đề xuất (giá vàng ở các cấp NRO không có giá, % Ki quy đổi, vùng trúng, tốc độ đạn) nằm trong seed và có thể chỉnh. Chi tiết kỹ thuật: `docs\database\BattleBall.md` mục 10.6 đến 10.8.

### Bảng vai trò tổng quan

| Chủng tộc | Đòn đấm cơ bản (cấp 1) | Bộ kỹ năng nổi bật (NRO) |
|---|---|---|
| Xayda | 110% ATK (Galick) | Antomic, Biến hình (khỉ), Tự phát nổ, Trói, Huýt sáo, Tái tạo năng lượng; tuyệt kỹ Cađíc liên hoàn chưởng |
| Namec | 95% ATK (Demon) | Masenko, Trị thương (hồi sinh đồng đội), Makankosappo, Đẻ trứng, Liên hoàn, Biến Sôcôla; tuyệt kỹ Ma phong ba |
| Trái Đất | 100% ATK (Dragon) | Kamejoko, Kaioken, Quả cầu kênh khi, Thái Dương Hạ San, Dịch chuyển tức thời, Thôi miên; tuyệt kỹ Super Kamejoko |

*Ghi chú:* "% ATK" là phần trăm sát thương cơ bản (100% = đúng ATK của nhân vật). Chi phí Ki luôn tính theo **% Ki tối đa** (trừ Makankosappo tiêu toàn bộ Ki). Giá vàng ghi cho **mỗi cấp** nâng lên (từ cấp 1 đến cấp tối đa); cấp 1 = giá học skill. Mỗi skill tấn công có hai chế độ trúng: **theo hướng ngắm** (vùng/đường thẳng, dễ né) và **theo mục tiêu chọn** (đuổi theo); skill tự dùng/hồi chỉ ở chế độ tự thân.

---

### 3.1 Trái Đất

| Skill | Cách hoạt động | Giá trị chính (cấp 1 → tối đa) | Hồi chiêu | Chi phí Ki | Vàng mỗi cấp (cấp 1 → tối đa) | Cấp tối đa |
|---|---|---|---|---|---|---|
| Chiêu đấm Dragon (id 0) | Đòn đấm cơ bản tầm gần (đánh tự động khi mục tiêu trong tầm). | Sát thương: 100% ATK → 160% ATK | 0,5 s | 0,01% Ki → 1% Ki | 0 → 2.000 | 7 |
| Chiêu Kamejoko (id 1) | Chưởng bay thẳng theo hướng ngắm (tầm 300 px) hoặc đuổi theo mục tiêu. | Sát thương: 150% ATK → 450% ATK | 2 → 5 s | 0,12% Ki → 5% Ki | 500 → 32.000 | 7 |
| Kaioken (id 9) | Đòn đấm tăng sát thương; mỗi lần dùng tốn 10% HP tối đa (cần HP lớn hơn 10%). | Sát thương: 160% ATK → 220% ATK | 0,5 s | 1,67% Ki → 5% Ki | 1.000 → 64.000 | 7 |
| Thái Dương Hạ San (id 6) | Làm choáng mọi kẻ địch trong bán kính (120 + 30 x cấp) px quanh người dùng. | Choáng: 3 s → 9 s | 60 → 30 s | 4,5% Ki → 1,5% Ki | 500 → 32.000 | 7 |
| Quả cầu kênh khi (id 10) | Tụ lực 4 s rồi ném; sát thương = 10% HP của mob/người trong phạm vi (350 + 30 x cấp) px + 10 x ATK nền; sát thương diện rộng. | Phạm vi: 380 px → 560 px | 360 → 300 s | 5% Ki → 8% Ki | 2.000 → 128.000 | 7 |
| Dịch chuyển tức thời (id 20) | Dịch chuyển tới mục tiêu, đánh một đòn luôn chí mạng và làm choáng. | Choáng: 1 s → 4 s | 20 → 14 s | 1,33% Ki → 8% Ki | 1.500 → 96.000 | 7 |
| Thôi miên (id 22) | Ru ngủ mục tiêu (trong tầm 200 px). | Ngủ: 5 s → 11 s | 30 → 42 s | 6% Ki | 1.500 → 96.000 | 7 |
| Super Kamejoko (id 24) | Tuyệt kỹ: gồng 2 s rồi gây sát thương lên mọi kẻ địch trong vùng chữ nhật phía trước. | Sát thương: 550% ATK → 950% ATK | 170 → 90 s | 8% Ki → 4% Ki | 5.000 → 128.100 | 9 |

---

### 3.2 Xayda

| Skill | Cách hoạt động | Giá trị chính (cấp 1 → tối đa) | Hồi chiêu | Chi phí Ki | Vàng mỗi cấp (cấp 1 → tối đa) | Cấp tối đa |
|---|---|---|---|---|---|---|
| Chiêu đấm Galick (id 4) | Đòn đấm cơ bản tầm gần (đánh tự động khi mục tiêu trong tầm). | Sát thương: 110% ATK → 170% ATK | 0,5 s | 0,01% Ki → 1% Ki | 0 → 4.000 | 7 |
| Chiêu Antomic (id 5) | Chưởng bay thẳng theo hướng ngắm (tầm 300 px) hoặc đuổi theo mục tiêu. | Sát thương: 110% ATK → 290% ATK | 1 → 2,2 s | 0,09% Ki → 5% Ki | 500 → 32.000 | 7 |
| Tái tạo năng lượng (id 8) | Tự hồi HP và Ki mỗi giây. | Hồi mỗi giây: 4% (HP và Ki) → 10% (HP và Ki) | 55 → 25 s | 0% Ki | 500 → 32.000 | 7 |
| Biến hình (id 13) | Biến khỉ: tăng HP, tồn tại có hạn. | HP cộng thêm / thời lượng: +40% HP, 60 s → +100% HP, 120 s | 300 → 360 s | 1% Ki | 1.000 → 64.000 | 7 |
| Tự phát nổ (id 14) | Gồng 2 s rồi nổ: sát thương = **HP hiện tại** của người dùng, diện rộng (400 + 30 x cấp) px; người dùng chết sau khi nổ. | Phạm vi nổ: 430 px → 610 px | 120 s | 5% Ki | 2.000 → 128.000 | 7 |
| Huýt sáo (id 21) | Tăng HP tạm thời cho đồng đội xung quanh (Namec bị trừ 10% HP). | HP cộng thêm: +40% → +100% | 210 → 180 s | 10% Ki → 4% Ki | 1.500 → 96.000 | 7 |
| Trói (id 23) | Trói bất động mục tiêu (một số boss bị giới hạn 5 s). | Trói: 5 s → 35 s | 15 → 45 s | 1,25% Ki → 8% Ki | 1.500 → 96.000 | 7 |
| Cađíc liên hoàn chưởng (id 25) | Tuyệt kỹ: gồng 2 s rồi gây sát thương lên mọi kẻ địch trong vùng chữ nhật phía trước. | Sát thương: 550% ATK → 950% ATK | 170 → 90 s | 8% Ki → 4% Ki | 5.000 → 128.100 | 9 |

---

### 3.3 Namec

| Skill | Cách hoạt động | Giá trị chính (cấp 1 → tối đa) | Hồi chiêu | Chi phí Ki | Vàng mỗi cấp (cấp 1 → tối đa) | Cấp tối đa |
|---|---|---|---|---|---|---|
| Chiêu đấm Demon (id 2) | Đòn đấm cơ bản tầm gần (đánh tự động khi mục tiêu trong tầm). | Sát thương: 95% ATK → 155% ATK | 0,4 s | 0,01% Ki → 1% Ki | 0 → 4.000 | 7 |
| Chiêu Masenko (id 3) | Chưởng bay thẳng theo hướng ngắm (tầm 300 px) hoặc đuổi theo mục tiêu. | Sát thương: 100% ATK → 160% ATK | 0,8 → 0,7 s | 0,08% Ki → 5% Ki | 500 → 32.000 | 7 |
| Trị thương (id 7) | Hồi HP và Ki cho đồng đội và cả người dùng, **hồi sinh được đồng đội đã chết**; từ cấp 2 lan tới đồng đội trong 300 px. | Hồi: 50% HP và Ki tối đa → 80% HP và Ki tối đa | 30 → 44 s | 4% Ki → 1% Ki | 500 → 32.000 | 7 |
| Makankosappo (id 11) | Tụ lực 3 s rồi bắn tia laser xuyên thẳng; sát thương = % Ki tối đa của người dùng; **tiêu hết Ki**. | Sát thương: 70% Ki tối đa → 130% Ki tối đa | 360 → 300 s | toàn bộ Ki | 1.000 → 64.000 | 7 |
| Đẻ trứng (id 12) | Triệu hồi thú đồng hành; sát thương thú = % sát thương của người dùng; thú tồn tại có hạn. | Sát thương thú / tồn tại: 30% ATK, 120 s → 90% ATK, 240 s | 360 → 540 s | 2% Ki → 8% Ki | 2.000 → 128.000 | 7 |
| Liên hoàn (id 17) | Chuỗi đòn đấm nhanh. | Sát thương: 160% ATK → 190% ATK | 0,35 → 0,33 s | 0,43% Ki → 3% Ki | 1.500 → 96.000 | 7 |
| Biến Sôcôla (id 18) | Biến mục tiêu thành sôcôla (mất khả năng hành động) 30 s. | Thời gian: 30 s | 30 → 24 s | 2,2% Ki → 1% Ki | 1.500 → 96.000 | 7 |
| Ma phong ba (id 26) | Tuyệt kỹ: gồng 2 s rồi nhốt mọi kẻ địch trong 500 px phía trước vào bình 4 s (chỉ khống chế, không gây sát thương). | Nhốt: 4 s | 170 → 90 s | 8% Ki → 4% Ki | 5.000 → 128.100 | 9 |

---

## 4. Kỹ năng chung: Khiên năng lượng

| Skill | Cách hoạt động | Giá trị chính (cấp 1 → tối đa) | Hồi chiêu | Chi phí Ki | Vàng mỗi cấp (cấp 1 → tối đa) | Cấp tối đa |
|---|---|---|---|---|---|---|
| Khiên năng lượng (id 19) | Khiên chặn đòn tấn công trong một khoảng thời gian. | Thời gian khiên: 15 s → 45 s | 75 → 105 s | 5,1% Ki → 3,3% Ki | 1.000 → 64.000 | 7 |

- Khiên chặn các đòn tấn công trong thời gian ghi trên; thời gian = (cấp + 2) x 5 s.
- Mô tả của NRO: "Vô hiệu các đòn tấn công" (**không còn** cơ chế giảm % sát thương và thiêu đốt HP của người dùng như bản cũ).
- Có thể bị phá vỡ bởi một số kỹ năng của boss; một số boss có true damage xuyên khiên (giữ nguyên ý tưởng cũ; chi tiết chưa chốt).

### 4.1 Nâng cấp kỹ năng (chỉ dùng vàng)

- Nâng cấp skill **chỉ tiêu vàng**; **không** dùng điều kiện "sức mạnh" của NRO. Cấp tối đa: **7** với skill thường, **9** với tuyệt kỹ (Super Kamejoko, Ma phong ba, Cađíc liên hoàn chưởng).
- Giá vàng lấy từ NRO ở những cấp NRO có giá (ví dụ Kamejoko 500, 1.000, 2.000, 4.000, 8.000 cho cấp 1 đến 5). Các cấp NRO không có giá (giá giữ chỗ 9999) được **đề xuất theo quy tắc**: tiếp tục nhân đôi mỗi cấp từ giá NRO cuối cùng; skill hoàn toàn không có giá NRO: giá cấp 1 theo nhóm (1.000 đến 2.000), nhân đôi mỗi cấp; tuyệt kỹ: 5.000 ở cấp 1, nhân 1,5 mỗi cấp. Có thể chỉnh trong dữ liệu skill (admin).

---

## 5. Cơ chế sustain (Kaioken + hút máu/ki)

- Định hướng đã chốt: **ưu tiên cảm giác thỏa mãn/sảng khoái hơn cân bằng cứng nhắc.**
- Vật phẩm hút máu/ki khi tấn công sẽ bù cho các skill tốn máu/ki (Kaioken, Tự phát nổ...), cho phép sustain gần như vô hạn ở endgame khi build đủ đồ — đây là phần thưởng progression hợp lý.
- **Kaioken (theo NRO):** mỗi lần dùng tốn **10% HP tối đa** (không tiêu hao theo thời gian) và đổi lại cho đòn đấm mạnh hơn; phải còn HP lớn hơn 10% mới dùng được. *(Cập nhật 2026-10-07: thay cho cơ chế cũ "tiêu hao HP/Ki mỗi giây".)*
- **Tự phát nổ (theo NRO):** sát thương bằng **HP hiện tại** của người dùng, người dùng chết sau khi nổ. *(Cơ chế "thiêu đốt HP của khiên" cũ đã bỏ cùng skill cũ; build "máu trâu gây dame" không còn đi qua Khiên năng lượng. Có thể giữ qua Tự phát nổ nếu cần: chưa quyết định.)*

---

## 6. Hệ thống trang bị

**5 món đồ:** Áo (Giáp + % giảm thương) · Quần (Máu) · Găng (Tấn công) · Giày (Ki) · Nhẫn (Chí mạng) — mỗi món gán thêm được 4 chỉ số khác.

- Rơi mảnh đồ từ boss → ghép đủ số lượng thành 1 món hoàn chỉnh.

**Chỉ số cơ bản (base stat) của từng món ở cấp thường (chưa nâng cấp):**

| Món đồ | Chỉ số chính | Giá trị cơ bản |
|---|---|---|
| Áo | Giáp | 2.500 |
| Quần | Máu | 200.000 |
| Găng | Tấn công (ATK) | 20.000 |
| Giày | Ki | 200.000 |
| Nhẫn | % Sát thương chí mạng | +25% |

*Đây là base stat khi chưa qua đá nâng cấp/đá chỉ số/đá pha lê/GEN — các hệ thống này sẽ cộng thêm % hoặc giá trị tuyệt đối lên nền tảng này (xem mục Đá nâng cấp, Đá chỉ số, Đá pha lê bên dưới). Vì Nhẫn có base là % sát thương chí mạng (không phải giá trị tuyệt đối như 4 món kia), cần lưu ý khi tính tổng chỉ số toàn đội — % sát thương chí mạng cộng dồn cần có trần tổng riêng như đã đề xuất ở phần Đá pha lê/GEN.*

**Tiền tệ & vật liệu:**
| Loại | Công dụng |
|---|---|
| Vàng | Mua mảnh đồ, đá nâng cấp, đá chỉ số, item dùng 1 lần có thời hạn; **nâng cấp kỹ năng (mục 4.1)**; hồi sinh trong trận (tối đa 2 lần mỗi trận) |
| Đá quý | Đổi skin (tăng mạnh chỉ số), item trang trí vĩnh viễn (chỉ số đặc biệt/hiệu ứng, yếu hơn skin), đổi đá pha lê |
| Đá nâng cấp | Tăng % chỉ số cơ bản vĩnh viễn cho 1 trong 5 món; **tối đa +75% chỉ số cơ bản của món (15 bậc, mỗi bậc +5%)** (đề xuất 2026-10-07) |
| Đá chỉ số | Tăng % chỉ số tổng (%tấn công,%máu,%giáp,%ki,%tỷ lệ chí mạng(max 5%),%sát thương chí mạng)(không phải chỉ số của đồ), tối đa 25% (riêng tỷ lệ chí mạng tối đa 5%); tỷ lệ thành công giảm dần khi gần max; mỗi lần thành công +1.5–2% (hoặc +0.1–0.5% với chỉ số max 5%); không trùng chỉ số trên cùng món; có thể đổi ngẫu nhiên giữa các loại chỉ số |
| Đá pha lê (siêu hiếm) | Chỉ số đỏ đặc biệt, tối đa 50% trong 3 chỉ số còn lại của đồ, có thể trùng loại, bắt đầu 10% và nâng dần bằng đá chỉ số |
| GEN | Dòng thuộc tính đặc biệt riêng từng món; đủ 5 món cùng thuộc tính → kích hoạt hiệu ứng bộ (+100% một loại sát thương/chỉ số tương ứng) |

**Kinh tế vàng (đề xuất 2026-10-07, chỉnh bằng `BattleBall\SourceData\tools\balance_model.py`):**
- **Thu nhập:** một trận **thắng** ở cấp khuyến nghị `R` cho khoảng **1.000 x R vàng** (gồm vàng rơi từ mob và boss); thua cho khoảng một nửa. Cấp 1 đến 100 với 3 trận thắng mỗi cấp ≈ **14,85 triệu vàng**.
- **Nâng cấp kỹ năng (mục 4.1):** tối đa mọi skill của cả 3 nhân vật ≈ **4,19 triệu vàng** (≈ 28% tổng thu nhập), nên người chơi vẫn còn vàng cho trang bị và đá.
- **Bông tai Potara:** 100 mảnh x **15.000 vàng** = 1,5 triệu vàng (≈ 30 trận thắng ở `R` = 50); mua tối đa **20 mảnh mỗi ngày** (mốc đổi ngày 00:00 giờ Việt Nam), tức tối thiểu 5 ngày; có thể nhận mảnh từ boss của một phó bản.
- **Mảnh đồ:** một món ghép từ 10 mảnh; mua đủ một món bằng vàng ≈ 6 trận thắng ở cấp vùng đó (giá mảnh ≈ 600 x `R`). Ví dụ `R` = 50: mảnh 30.000, món 300.000.
- **Bán đồ:** giá bán thấp, bằng **5%** giá mua cả món (ví dụ 15.000 vàng cho món `R` = 50).
- **Đá nâng cấp** ≈ 1,5 trận thắng; **đá chỉ số** ≈ 1 trận thắng; **item dùng một lần có thời hạn** ≈ 0,1 trận thắng (ví dụ `R` = 50: 75.000, 50.000, 5.000).
- **Hồi sinh trong trận (tối đa 2 lần):** lần 1 ≈ 0,5 trận thắng, lần 2 ≈ 1 trận thắng (ví dụ `R` = 50: 25.000 và 50.000).
- **Đá quý (đề xuất 2026-10-07):** **không mua bằng vàng, không rơi từ mob.** Nguồn nhận: (1) hoàn thành **phó bản giới hạn thời gian** trong thời gian cho phép: 10 đá quý mỗi lần (tối đa 5 lần mỗi ngày); (2) **lần đầu hạ boss cuối của mỗi phó bản**: 20; (3) **raid boss**: 100 cho lần hạ đầu tiên, sau đó 10 mỗi lần (tối đa 3 lần mỗi ngày). Dùng để: đổi **skin** (giá 100 đến 1.600 tùy nhóm costume và độ hiếm; xem phân loại bên dưới), **item trang trí vĩnh viễn** (150), **đổi đá pha lê** (500). **Phân loại skin (chủ dự án chốt hướng 2026-10-07):** một số skin **kích hoạt theo skill** (ví dụ dạng khỉ của Biến hình ở Xayda, dùng part 192 đến 200, nằm ngoài danh sách skin) và **không bán**; một số skin **áp dụng cho Bông tai Potara** (skin hợp thể, theo chủng tộc nhân vật chủ: td Trái Đất, xd Xayda, nm Namếc) **có thể mua**, nhưng **chỉ nhân vật chủ khi dùng Bông tai mới mặc được; AI và nhân vật phụ không mặc được skin hợp thể**; một số skin dành cho **boss và quái nhỏ** (không bán, người chơi không mặc); các skin còn lại bán bằng đá quý. **Giá và chỉ số cộng thêm tính theo nhóm, độ hiếm** (mặc định skin phổ thông +1%, hiếm +3%, rất hiếm +5% Sức đánh, HP, Ki), và mỗi skin có thể mang bất kỳ chỉ số nào trong danh sách chỉ số của skin (49 loại, xem `docs\database\BattleBall.md` mục 10.15); **giá và chỉ số chỉnh được trên trang quản trị**. Sau khi người dùng ẩn các skin không dùng, còn 321 skin (255 bán thường, 11 hợp thể, 55 avatar/đầu). Với 30 đá quý mỗi ngày, một skin 300 mất khoảng 10 ngày, một đá pha lê khoảng 17 ngày.


**Lưu ý khi hoàn thiện chi tiết:**
- Đặt trần tổng (hard cap) cho tỷ lệ/sát thương chí mạng toàn bộ nguồn cộng dồn (đồ + đá chỉ số + đá pha lê + GEN).
- Cân nhắc đánh đổi khi kích hoạt bộ GEN 5 món để tránh trở thành lựa chọn duy nhất luôn tối ưu.

---

## 7. Chỉ số Mob / Boss — HP theo thời gian hạ mục tiêu, ATK theo % Tổng ATK tối đa

> *Cập nhật 2026-10-07: bảng HP cũ (tính bằng % của `Max_ATK_Total`, ví dụ boss vùng sớm 150% đến 250%) được thiết kế cho nhịp đánh chậm; với bộ skill NRO, đòn đấm đánh khoảng 2 lần mỗi giây nên sát thương mỗi giây của đội lớn hơn nhiều, bảng cũ sẽ khiến boss chết trong vài giây. HP nay tính từ **thời gian hạ mục tiêu** mong muốn. ATK và Giáp của mob/boss giữ nguyên bảng cũ.*

**Định nghĩa mốc quy chiếu:**
> `Max_ATK_Total` = Tổng ATK tối đa mà 3 nhân vật có thể đạt được khi full trang bị (đồ max chỉ số + đá pha lê max + GEN full set), **không** phụ thuộc vào ATK hiện tại của người chơi. **Giữ cố định 500.000.** Cách kiểm tra: ATK mỗi nhân vật = (ATK cấp 100 là 26.000 + ATK găng 20.000 x (1 + U)) x (1 + 25% đá chỉ số + 50% đá pha lê + 100% bộ GEN); với **U = 75%** (đá nâng cấp tối đa +75% chỉ số cơ bản của món, xem mục 6) ra 167.750 mỗi nhân vật, 503.250 cho cả đội, khớp 500.000. **Bộ skill NRO không có buff cộng ATK** (Kaioken là skill đánh mạnh hơn, Biến hình chỉ cộng HP), nên `Max_ATK_Total` không còn cộng buff kỹ năng như bản cũ.

> `Team_DPS_Max` = `Max_ATK_Total` x 1,73 = **866.250 sát thương/giây** (đòn đấm trung bình 3,5 ATK/giây x 50% thời gian đánh trúng x 1,1 chí mạng x 0,9 hiệu suất AI). Skill đặc biệt coi như phần dự phòng cộng thêm.

**HP = Thời gian hạ (giây) x Tỷ lệ ATK dự kiến theo vùng x `Team_DPS_Max` x (1 − giảm thương của mục tiêu).** Giảm thương lấy theo cột Giáp của bảng (giá trị giữa khoảng). Tỷ lệ ATK dự kiến (đội đúng cấp, so với `Max_ATK_Total`): vùng sớm 20%, giữa 45%, cuối 85%, endgame (raid) 100%.

Cách tính này giúp độ khó mob/boss là **hằng số thiết kế cố định theo từng vùng/tầng**, không tự động dễ hơn hay khó hơn theo tiến trình cày đồ thực tế của từng người chơi — độ khó do đội ngũ thiết kế chủ động phân bổ theo vùng. Mọi hằng số nằm ở đầu `BattleBall\SourceData\tools\balance_model.py`; đổi rồi chạy lại để ra bảng mới.

### 7.1 Quái nhỏ (trash mob)

| Loại | Thời gian hạ (giây, cả đội) | HP vùng sớm / giữa / cuối | ATK (%Max_ATK_Total) | Giáp (giảm thương) |
|---|---|---|---|---|
| Mob thường | 0,4 | 64.103 / 144.231 / 272.436 | 3% – 5% | 5% – 10% |
| Mob elite (ngẫu nhiên trong đàn) | 2 | 285.863 / 643.191 / 1.214.916 | 8% – 10% | 15% – 20% |

### 7.2 Boss theo khu vực bản đồ

| Cấp độ vùng | Thời gian hạ (giây) | HP | ATK (%Max_ATK_Total) | Giáp (giảm thương) |
|---|---|---|---|---|
| Vùng sớm (early) | 45 | 6.042.094 | 20% – 28% | 20% – 25% |
| Vùng giữa (mid) | 75 | 19.295.719 | 35% – 45% | 30% – 38% |
| Vùng cuối (late) | 120 | 49.480.200 | 50% – 65% | 40% – 48% |

### 7.3 Boss phó bản & Raid boss

| Loại | Thời gian hạ (giây) | HP (ví dụ tại vùng) | ATK (%Max_ATK_Total) | Giáp (giảm thương) | Ghi chú |
|---|---|---|---|---|---|
| Mini-boss cuối tầng phó bản | 40 | 10.524.938 (giữa) | 30% – 40% | 30% – 35% | Xuất hiện giữa/cuối mỗi tầng |
| Boss cuối phó bản | 150 | 56.880.141 (cuối) | 55% – 70% | 45% – 52% | Rương thưởng lớn |
| Raid boss (cần phối hợp 3 chủng tộc) | 360 | 124.740.000 (endgame) | 70% – 90% | 55% – 65% | Có pha enrage dưới 30% HP, triệu hồi thêm mob phụ giữa trận |

*Lưu ý:* HP cỡ chục đến trăm triệu nên cần kiểu số 64 bit ở cả server và client (đã chốt: sát thương kiểu đủ rộng). Với cấp phó bản khác vùng, tính lại theo công thức HP ở trên bằng tỷ lệ ATK dự kiến của cấp khuyến nghị.

### 7.4 Công thức sát thương và giáp (đề xuất 2026-10-07)

- **Giảm thương của nhân vật** (từ chỉ số Giáp): `giảm_thương = Giáp / (Giáp + 50.000)`. Ví dụ: cấp 1 với đồ gốc 21,3%; cấp 100 với đồ gốc (Giáp 24.500) 32,9%; cấp 100 full đồ và % cộng (Giáp ≈ 67.375) 57,4%. Món Áo còn có thêm "% giảm thương" riêng (cộng trực tiếp vào `giảm_thương`, tổng kẹp tối đa 80%).
- **Giảm thương của mob/boss:** lấy trực tiếp theo cột Giáp (%) ở các bảng mục 7.
- **Thứ tự tính một đòn:** `sát_thương_nền = ATK x %skill` (hoặc `% Ki tối đa` với Makankosappo, hoặc `HP hiện tại` với Tự phát nổ) → nếu **chí mạng** thì nhân `(1 + sát_thương_chí_mạng)` → nhân `(1 − giảm_thương_mục_tiêu)` → trừ khiên/hiệu ứng của mục tiêu → làm tròn, tối thiểu 1. Mỗi đòn dao động ngẫu nhiên ±5% (như NRO dùng ±5% cho Dịch chuyển tức thời), do server tính.
- **Ngoại lệ:** Tự hủy (Tự phát nổ) **bỏ qua giáp** (sát thương chuẩn) nhưng boss chỉ chịu một nửa (như NRO); Khiên năng lượng vô hiệu đòn tấn công trong thời gian khiên.
- Ví dụ (cấp 100, ATK 46.000 gồm găng gốc): Kamejoko cấp 7 (450%) lên boss vùng giữa (34%) gây 136.620 (chí mạng, +100%: 273.240). Boss vùng cuối (ATK 57,5% x 500.000 = 287.500 mỗi đòn) lên nhân vật cấp 100 với đồ gốc gây khoảng 192.950 mỗi đòn.

### 7.5 Hành vi và nhịp tấn công của boss (chủ dự án mô tả, số liệu đề xuất 2026-10-07)

**Bộ chiêu (chủ dự án chốt):** chủ yếu là **đòn đánh thường**, **choáng của Trái Đất** (diện rộng, kiểu Thái Dương Hạ San), **Khiên năng lượng**, **chưởng** (đạn bay thẳng) hoặc **kỹ năng tối thượng** (gồng báo trước). Các kỹ năng **tự focus (khóa mục tiêu, người chơi không thể né)** luôn nhắm vào **đơn vị có nhiều máu nhất** trong đội; các kỹ năng còn lại tấn công **trong một phạm vi/vùng nhất định** nên né được bằng di chuyển hoặc đứng xa.

**Cách chọn chiêu (đề xuất, cùng nguyên tắc với AI đồng đội):** mỗi nhịp hành động boss duyệt theo thứ tự ưu tiên và dùng **chiêu đầu tiên đã hết hồi chiêu**; đòn đánh thường là phương án dự phòng.

| Ưu tiên | Chiêu | Hồi chiêu | Sát thương (x ATK boss) | Né được? | Ghi chú |
|---|---|---|---|---|---|
| 1 | Kỹ năng tối thượng | 60 s | 3,0 | Có (vùng báo trước, gồng 2 s) | Kiểu Super Kamejoko, vùng chữ nhật phía trước |
| 2 | Khiên năng lượng | 45 s | 0 | — | Boss được khiên khoảng 10 s (không nhận sát thương) |
| 3 | Choáng diện rộng | 25 s | 0,5 + choáng 2 s | Có (đứng ngoài bán kính) | Kiểu Thái Dương Hạ San |
| 4 | Skill focus (khóa mục tiêu) | 15 s | 1,0 | **Không** | Nhắm đơn vị có HP hiện tại cao nhất; có thể kèm Dịch chuyển tức thời/Trói |
| 5 | Chưởng | 6 s | 1,5 | Có (bay thẳng, né khi di chuyển) | Kiểu Kamejoko |
| 6 | Đòn đánh thường | không | 1,0 | Có (chỉ trúng khi ở tầm gần) | Dự phòng |

- **Khoảng cách giữa các nhịp** (đề xuất, chọn sao cho nếu cả đội không né, không hồi, không khiên thì boss gây gấp đôi tổng HP của đội trong thời gian hạ mục tiêu): vùng sớm ≈ **1,3 s**, giữa ≈ **1,8 s**, cuối ≈ **2,2 s**, raid ≈ **5 s** (mỗi nhịp một chiêu). Boss mạnh hơn thì đánh nặng hơn nhưng thưa hơn. Phần sát thương **không thể né** (skill focus) chiếm khoảng 8% đến 23% tổng sát thương tiềm năng.
- **Pha enrage dưới 30% HP:** rút ngắn khoảng cách nhịp khoảng 30% và thêm đòn diện rộng (như mục 7).
- **Vùng hoạt động và quay về (chủ dự án chốt):** mỗi boss gắn với một vùng (zone) nhất định. Khi mục tiêu **ra khỏi biên vùng**, boss đuổi thêm một đoạn ngắn (đề xuất 200 px) rồi **ngừng đuổi, quay về vị trí ban đầu và hồi máu** (đề xuất 5% HP tối đa mỗi giây, trong lúc quay về không bị khóa mục tiêu); đây cũng là cơ chế chống kite vô hạn đã có ở mục 1 và mục 7.
- Số liệu (hệ số sát thương, hồi chiêu, khoảng cách nhịp) sinh bởi mô hình `BattleBall\SourceData\tools\balance_model.py` (phần "BOSS RHYTHM"); là điểm khởi đầu để chỉnh khi thử chơi.

**Ví dụ minh họa cách tính `Max_ATK_Total` (chỉ mang tính tham khảo cho báo cáo):**
> `Max_ATK_Total` là **một con số cố định do nhà thiết kế chốt sẵn** (không phải giá trị tính động theo ATK thực tế của người chơi tại thời điểm chơi): 500.000, kiểm tra bằng công thức ở đầu mục. Con số này được **ghi cố định** trong bảng thiết kế và dùng để quy đổi ATK mob/boss thành số tuyệt đối và để tính `Team_DPS_Max`; hoàn toàn không thay đổi theo ATK hiện tại của người chơi ở bất kỳ thời điểm nào trong game.

**Nguyên tắc áp dụng:**
- Mọi giá trị HP/ATK boss là **số tuyệt đối cố định**, được tính một lần từ `Max_ATK_Total` và thời gian hạ mục tiêu tại thời điểm thiết kế/cân bằng lại (không tính lại theo real-time build của người chơi).
- Khi thêm nội dung mới (đồ mới, GEN mới) làm `Max_ATK_Total` tăng lên, cần một đợt "cân bằng lại" (rebalance pass) để cập nhật HP/ATK boss theo mốc mới — tương tự cách các game live-service tăng độ khó nội dung mới theo mỗi mùa/season.
- Pha enrage (dưới 30% HP boss): tăng tốc độ đánh + mở thêm đòn AoE, tạo áp lực thời gian.
- Leash timer: boss hồi máu dần nếu người chơi rời vùng quá lâu, tránh chiến thuật kite vô hạn.

---

## 8. Ý tưởng bổ sung cho combat/dungeon

- **Kill streak:** giết mob liên tiếp trong thời gian ngắn → tăng % EXP nhận được, khuyến khích chơi AoE tích cực.
- **Phó bản theo tầng:** mob khó dần theo tầng, rương thưởng ngẫu nhiên (mảnh đồ/đá nâng cấp/đá chỉ số) mỗi tầng.
- **Phó bản giới hạn thời gian:** phần thưởng đá quý/đá pha lê hiếm, thưởng cho build DPS cao.
- **Minimap:** thêm đánh dấu khu vực đã farm hết mob (tạm thời) và khu vực còn rương/tài nguyên chưa nhặt, ngoài chấm đỏ boss.

---

## 9. Việc cần làm tiếp theo

- [ ] Rebalance pass định kỳ cho HP/ATK boss mỗi khi `Max_ATK_Total` thay đổi do nội dung mới.
