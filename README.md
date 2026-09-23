# Tài liệu thiết kế game (Game Design Document)
### Thể loại: Hack & slash — đánh quái dồn dập, boss theo khu vực, hệ thống trang bị chuyên sâu

---

## 1. Tổng quan gameplay

- Quái nhỏ dồn ra ồ ạt từ 1 hướng khi người chơi tiến vào khu vực.
- Bản đồ có nhiều khu vực boss riêng biệt, được đánh dấu chấm đỏ trên minimap.
- Minimap tự động lưu địa hình mỗi khi người chơi load chunk mới.
- Boss khi vào trạng thái tấn công,nếu người chơi ra khỏi biên một đoạn, sẽ đuổi thêm 1 đoạn sau đó quay lại vị trí cũ và hồi máu.
- Đánh quái nhỏ nhận EXP → lên cấp → tăng: Máu, Ki (năng lượng dùng kỹ năng), Giáp, Tấn công, khả năng hồi máu (phụ thuộc Ki — hết Ki phải dùng vật phẩm hỗ trợ; máu cũng hồi được bằng vật phẩm).


---

## 2. Đội hình

### Đội hình 3 người
- 1 người chơi điều khiển trực tiếp + 2 nhân vật AI tự động đánh và dùng kỹ năng.
- Cần cơ chế đặt "chiến thuật" cho AI (ưu tiên DPS / CC / hỗ trợ) để người chơi vẫn kiểm soát được nhịp trận đấu.
- Cần cơ chế aggro/threat rõ ràng để tránh mob dồn hết vào AI hoặc dồn hết vào người chơi.
- Nhân vật AI khi "ngất" nên có cơ chế hồi hoặc giảm hiệu quả tạm thời thay vì loại khỏi trận, tránh cảm giác thua vì AI yếu.

### Đội hình hợp thể (2 người)
- 1 người ở trạng thái hợp thể, mang tổng chỉ số của cả 2 hành tinh.
- Bộ kỹ năng tính theo nhân vật được chọn làm chủ.
- **Đề xuất cân bằng:** cộng theo % (không phải 100% tuyệt đối) chỉ số của nhân vật phụ, giới hạn thời gian duy trì + tiêu hao Ki liên tục, và mất một phần skill hỗ trợ của nhân vật bị gộp — để đội 3 người (nhiều skill/linh hoạt hơn) và đội hợp thể (tanky/burst cao hơn) đều có vai trò riêng, không cái nào áp đảo tuyệt đối.

---

## 3. Ba chủng tộc

### Bảng vai trò tổng quan

| Chủng tộc | Đấm cơ bản | Vai trò chính |
|---|---|---|
| Xayda | 110% ATK | Burst tập trung cực cao + biến hình toàn diện |
| Namec | 100% ATK | DPS đơn mục tiêu liên tục, chuyên trị boss |
| Trái Đất | 90% ATK | AoE xuyên đường thẳng + CC — dọn mob & khống chế |

---

### 3.1 Trái Đất

**Kamehame** (xuyên đường thẳng, gây dame trên đường đi)
| Cấp | Sát thương | Hồi chiêu | Chi phí Ki |
|---|---|---|---|
| 1 | 500% ATK | 10s | 30% |
| 3 | 620% ATK | 8.5s | 28% |
| 5 | 740% ATK | 7s | 25% |
| 7 | 850% ATK | 6s | 22% |

**Flash** (choáng + bất động diện rộng)
| Cấp | Thời gian CC | Hồi chiêu | Chi phí Ki |
|---|---|---|---|
| 1 | 1.5s | 25s | 25% |
| 3 | 2s | 22s | 23% |
| 5 | 2.5s | 19s | 20% |
| 7 | 3s | 16s | 18% |

*Hồi chiêu luôn giữ dài dù max cấp — tránh spam-stun liên tục khiến boss/mob không thể phản kháng.*

**Quả cầu khênh ki** (burst nuke, có thời gian tụ lực 1.5–2s, có thể bị ngắt nếu trúng CC)
| Cấp | Dame mục tiêu chính | Dame AoE xung quanh | Hồi chiêu | Chi phí Ki |
|---|---|---|---|---|
| 1 | 2000% ATK | 1000% ATK | 70s | 70% |
| 4 | 2400% ATK | 1150% ATK | 55s | 65% |
| 7 | 2800% ATK | 1300% ATK | 45s | 60% |

**Kaioken** (buff tiêu hao theo thời gian, không theo đòn đánh — tính trên HP/Ki tối đa)
| Cấp | Tăng dame tối đa | Tiêu hao HP/Ki mỗi giây |
|---|---|---|
| 1 | 30% | 8% mỗi loại |
| 4 | 40% | 6% mỗi loại |
| 7 | 50% | 5% mỗi loại |

---

### 3.2 Xayda

**Atomic** (xuyên đường thẳng)
| Cấp | Sát thương | Hồi chiêu | Chi phí Ki |
|---|---|---|---|
| 1 | 250% ATK | 6s | 20% |
| 4 | 310% ATK | 5s | 18% |
| 7 | 370% ATK | 4s | 16% |

**Tái tạo năng lượng** (hồi HP/Ki liên tục trong 10s)
| Cấp | Hồi mỗi giây | Hồi chiêu |
|---|---|---|
| 1 | 3% HP + 5% Ki | 50s |
| 4 | 4% HP + 6.5% Ki | 40s |
| 7 | 5% HP + 8% Ki | 32s |

**Biến khỉ** (buff toàn diện, giới hạn thời gian)
| Cấp | Chí mạng / tốc đánh / di chuyển | HP / Giáp / ATK | Thời lượng | Hồi chiêu |
|---|---|---|---|---|
| 1 | +20% | +15% | 90s | 150s |
| 4 | +30% | +22% | 105s | 130s |
| 7 | +40% | +30% | 120s | 110s |

*Ngay cả cấp 7, hồi chiêu (110s) vẫn dài hơn thời lượng (120s) một chút — tránh trạng thái "luôn bật" tuyệt đối.*

**Trói** (bất động mục tiêu)
| Cấp | Thời gian gốc | Hồi chiêu | Chi phí Ki |
|---|---|---|---|
| 1 | 30s (boss: ~8–10s do kháng CC) | 45s | 30% |
| 4 | 30s | 36s | 27% |
| 7 | 30s | 28s | 24% |

*Boss nên có % kháng CC, chỉ chịu khoảng 25–35% thời gian gốc để không phá vỡ nhịp trận.*

**Tự hủy** (ultimate rủi ro cao)
- Sát thương = (%giảm thương × máu còn lại hiện tại) + máu tối đa hiện tại, nổ diện rộng.
- Người dùng chết ngay sau kích hoạt (đề xuất thêm 1–1.5s trễ trước khi nổ để tạo cơ hội combo/né).
- Cần Namec hồi sinh, hoặc dùng vàng để hồi sinh (tối đa 2 lần/trận hoặc /ngày).

---

### 3.3 Namec

**Chiêu liên hoàn** (x2 tốc đánh, tự dịch chuyển bám mục tiêu gần nhất, vẫn bám mục tiêu kể cả khi boss đẩy lùi người chơi ra biên)
| Cấp | Sát thương/đòn | Hồi Ki/đòn | Thời lượng | Hồi chiêu |
|---|---|---|---|---|
| 1 | 200% ATK | 4% Ki | 6s | 14s |
| 4 | 245% ATK | 5% Ki | 7s | 11s |
| 7 | 290% ATK | 6% Ki | 8s | 8s |

*Cấp 7: hồi chiêu ≈ thời lượng → gần như duy trì liên tục, đúng vai trò "carry boss chuyên biệt".*

**Chữa trị**
| Cấp | Hồi sinh / Hồi máu toàn đội | Hồi chiêu |
|---|---|---|
| 1 | 40% HP / 25% HP tối đa | 100s |
| 4 | 55% HP / 35% | 85s |
| 7 | 70% HP / 45% | 70s |

**Để trứng** (triệu hồi thú đồng hành, tốc độ = tốc skill đang chọn)
| Cấp | Sát thương thú | Ki tiêu hao/đòn |
|---|---|---|
| 1 | 100% ATK | 10% |
| 4 | 130% ATK | 8% |
| 7 | 160% ATK | 6% |

---

## 4. Kỹ năng chung — Khiên năng lượng

| Cấp | Chặn tấn công | Thiêu đốt (%HP tối đa người dùng/giây) | Hồi chiêu | Chi phí (mỗi người) |
|---|---|---|---|---|
| 1 | 30s, giảm 60% dame nhận | 50%/giây | 150s | 40% Ki |
| 4 | 30s, giảm 70% | 55%/giây | 120s | 35% Ki |
| 7 | 30s, giảm 80% (không miễn nhiễm tuyệt đối) | 60%/giây | 100s | 30% Ki |

- Có thể bị phá vỡ bởi một số kỹ năng của boss; một số boss có true damage xuyên khiên để tránh cảm giác bất khả chiến bại hoàn toàn.
- Thiêu đốt tính theo máu tối đa của **người dùng chiêu** (không phải mục tiêu) — giữ định hướng "máu trâu vẫn gây sát thương tốt" như một lối chơi chính đáng.

---

## 5. Cơ chế sustain (Kaioken + hút máu/ki)

- Định hướng đã chốt: **ưu tiên cảm giác thỏa mãn/sảng khoái hơn cân bằng cứng nhắc.**
- Vật phẩm hút máu/ki khi tấn công sẽ bù cho các skill tốn máu/ki (Kaioken, Tự hủy...), cho phép sustain gần như vô hạn ở endgame khi build đủ đồ — đây là phần thưởng progression hợp lý.
- Kaioken tiêu hao tính theo %HP/Ki **tối đa** mỗi **giây** (không theo đòn đánh) để độc lập với tốc độ đánh và dễ kiểm soát.
- Build "máu trâu gây dame" (qua Thiêu đốt) là một lối chơi hợp lệ song song với build DPS/chí mạng — không cần triệt tiêu, chỉ cần đảm bảo không phải là con đường duy nhất tối ưu.

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
| Vàng | Mua mảnh đồ, đá nâng cấp, đá chỉ số, item dùng 1 lần có thời hạn |
| Đá quý | Đổi skin (tăng mạnh chỉ số), item trang trí vĩnh viễn (chỉ số đặc biệt/hiệu ứng, yếu hơn skin), đổi đá pha lê |
| Đá nâng cấp | Tăng % chỉ số cơ bản vĩnh viễn cho 1 trong 5 món |
| Đá chỉ số | Tăng % chỉ số tổng (%tấn công,%máu,%giáp,%ki,%tỷ lệ chí mạng(max 5%),%sát thương chí mạng)(không phải chỉ số của đồ), tối đa 25% (riêng tỷ lệ chí mạng tối đa 5%); tỷ lệ thành công giảm dần khi gần max; mỗi lần thành công +1.5–2% (hoặc +0.1–0.5% với chỉ số max 5%); không trùng chỉ số trên cùng món; có thể đổi ngẫu nhiên giữa các loại chỉ số |
| Đá pha lê (siêu hiếm) | Chỉ số đỏ đặc biệt, tối đa 50% trong 3 chỉ số còn lại của đồ, có thể trùng loại, bắt đầu 10% và nâng dần bằng đá chỉ số |
| GEN | Dòng thuộc tính đặc biệt riêng từng món; đủ 5 món cùng thuộc tính → kích hoạt hiệu ứng bộ (+100% một loại sát thương/chỉ số tương ứng) |

**Lưu ý khi hoàn thiện chi tiết:**
- Đặt trần tổng (hard cap) cho tỷ lệ/sát thương chí mạng toàn bộ nguồn cộng dồn (đồ + đá chỉ số + đá pha lê + GEN).
- Cân nhắc đánh đổi khi kích hoạt bộ GEN 5 món để tránh trở thành lựa chọn duy nhất luôn tối ưu.

---

## 7. Chỉ số Mob / Boss — tính theo % Tổng ATK tối đa lý thuyết của 3 người chơi

**Định nghĩa mốc quy chiếu:**
> `Max_ATK_Total` = Tổng ATK tối đa mà 3 người chơi có thể đạt được khi full trang bị (đồ max chỉ số + đá pha lê max + GEN full set + skill cấp 7 + biến hình/buff tối đa), **không** phụ thuộc vào ATK hiện tại của người chơi đang chơi.

Cách tính này giúp độ khó mob/boss là **hằng số thiết kế cố định theo từng vùng/tầng**, không tự động dễ hơn hay khó hơn theo tiến trình cày đồ thực tế của từng người chơi — độ khó do đội ngũ thiết kế chủ động phân bổ theo vùng.

### 7.1 Quái nhỏ (trash mob)

| Loại | HP (%Max_ATK_Total) | ATK (%Max_ATK_Total) | Giáp (giảm thương) |
|---|---|---|---|
| Mob thường | 2% – 4% | 3% – 5% | 5% – 10% |
| Mob elite (ngẫu nhiên trong đàn) | 8% – 12% | 8% – 10% | 15% – 20% |

### 7.2 Boss theo khu vực bản đồ

| Cấp độ vùng | HP (%Max_ATK_Total) | ATK (%Max_ATK_Total) | Giáp (giảm thương) |
|---|---|---|---|
| Vùng sớm (early) | 150% – 250% | 20% – 28% | 20% – 25% |
| Vùng giữa (mid) | 400% – 650% | 35% – 45% | 30% – 38% |
| Vùng cuối (late) | 900% – 1300% | 50% – 65% | 40% – 48% |

### 7.3 Boss phó bản & Raid boss

| Loại | HP (%Max_ATK_Total) | ATK (%Max_ATK_Total) | Giáp (giảm thương) | Ghi chú |
|---|---|---|---|---|
| Mini-boss cuối tầng phó bản | 250% – 400% | 30% – 40% | 30% – 35% | Xuất hiện giữa/cuối mỗi tầng |
| Boss cuối phó bản | 1000% – 1500% | 55% – 70% | 45% – 52% | Rương thưởng lớn |
| Raid boss (cần phối hợp 3 chủng tộc) | 2000% – 3000% | 70% – 90% | 55% – 65% | Có pha enrage dưới 30% HP, triệu hồi thêm mob phụ giữa trận |

**Ví dụ minh họa cách tính `Max_ATK_Total` (chỉ mang tính tham khảo cho báo cáo):**
> `Max_ATK_Total` là **một con số cố định do nhà thiết kế chốt sẵn** (không phải giá trị tính động theo ATK thực tế của người chơi tại thời điểm chơi). Cách chốt: lấy tổng ATK lý thuyết cao nhất mà 3 người chơi có thể đạt được khi full trang bị max (Găng base +20.000 ATK/người → 3 người đã có 60.000 ATK nền, cộng thêm % tối đa từ đá chỉ số, GEN, đá pha lê, buff kỹ năng như Kaioken +50%, Biến khỉ +30%...). Sau khi tính ra một lần, con số này được **ghi cố định** trong bảng thiết kế (VD: 500.000) và dùng để quy đổi % ở bảng HP/ATK/Giáp mob-boss thành số tuyệt đối — hoàn toàn không thay đổi theo ATK hiện tại của người chơi đang chơi ở bất kỳ thời điểm nào trong game.

**Nguyên tắc áp dụng:**
- Mọi giá trị HP/ATK boss là **số tuyệt đối cố định**, được tính một lần từ `Max_ATK_Total` tại thời điểm thiết kế/cân bằng lại (không tính lại theo real-time build của người chơi).
- Khi thêm nội dung mới (đồ mới, GEN mới) làm `Max_ATK_Total` tăng lên, cần một đợt "cân bằng lại" (rebalance pass) để cập nhật HP/ATK boss theo mốc mới — tương tự cách các game live-service tăng độ khó nội dung mới theo mỗi mùa/season.
- Pha enrage (dưới 30% HP boss): tăng tốc độ đánh + mở thêm đòn AoE, tạo áp lực thời gian.
- Leash timer: boss hồi máu dần nếu người chơi rời vùng quá lâu, tránh chiến thuật kite vô hạn.

---

## 8. Ý tưởng bổ sung cho combat/dungeon

- **Kill streak:** giết mob liên tiếp trong thời gian ngắn → tăng % EXP/vàng nhận được, khuyến khích chơi AoE tích cực.
- **Phó bản theo tầng:** mob khó dần theo tầng, rương thưởng ngẫu nhiên (mảnh đồ/đá nâng cấp/đá chỉ số) mỗi tầng.
- **Phó bản giới hạn thời gian:** phần thưởng đá quý/đá pha lê hiếm, thưởng cho build DPS cao.
- **Minimap:** thêm đánh dấu khu vực đã farm hết mob (tạm thời) và khu vực còn rương/tài nguyên chưa nhặt, ngoài chấm đỏ boss.

---

## 9. Việc cần làm tiếp theo

- [ ] Rebalance pass định kỳ cho HP/ATK boss mỗi khi `Max_ATK_Total` thay đổi do nội dung mới.
