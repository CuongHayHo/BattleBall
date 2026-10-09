# BattleBall: Data Model (Design)

> **Trạng thái:** DESIGN ONLY. Chưa có code, chưa có schema SQL, chưa có opcode. `E:\PRJNRO\BattleBallServer` vẫn trống.
> Đây là mô hình dữ liệu **logic**, độc lập công nghệ database.
> Nguồn: GDD `docs\gameplay\game-design-doc.md` (đang ở giai đoạn thiết kế, chưa phải sản phẩm hoàn chỉnh), `docs\database\Teamobi2026.md`, client BattleBall hiện tại.
> Nhãn: **CONFIRMED** (có trong GDD hoặc reference đã xác minh), **PROPOSED** (đề xuất mới), **OPEN** (cần chốt), **UNKNOWN**.

---

## 1. Bối cảnh và phạm vi

| Điểm | Nội dung | Nhãn |
|---|---|---|
| Mô hình chơi | **Chơi đơn** (một người chơi mỗi trận, không đồng bộ nhiều người cùng map); server lưu dữ liệu **và mô phỏng trận** theo mô hình Teamobi2026 (xem dòng Combat) | CONFIRMED (người dùng xác nhận) |
| Vị trí dữ liệu | Item và dữ liệu người dùng lưu trên **server** | CONFIRMED (người dùng xác nhận) |
| Combat | "Học theo cơ chế Teamobi2026" (người dùng xác nhận). Hiểu là: **server tính** sát thương, cooldown, mana, HP, drop và chạy AI quái/boss/đồng đội; client báo vị trí và mục tiêu (`docs\gameplay\Teamobi2026.md`). Việc server kiểm tra vị trí do client báo: OPEN | CONFIRMED về ý định, OPEN về chi tiết |
| Kết nối | **Cần mạng liên tục** (người dùng xác nhận): mọi thao tác item và phần thưởng đi qua server ngay | CONFIRMED |
| Tài liệu này không bao gồm | Protocol, networking, công thức combat chi tiết | |

Hệ quả cho server (cập nhật so với `docs\architecture\BattleBallServer.md`): combat, skill, item, inventory, boss/mob trở thành **REQUIRED** theo GDD; đồng bộ nhiều người chơi **NOT REQUIRED** (chơi đơn).

---

## 2. Phân loại dữ liệu

| Loại | Chủ sở hữu | Bền vững | Ví dụ | Nhãn |
|---|---|---|---|---|
| **Static** (template, config) | Server | Có (file/bảng có `version`) | skill + bảng cấp 1–7, item template, boss/mob/zone template, drop table, bảng đá, set GEN, `Max_ATK_Total` | PROPOSED |
| **Player persistent** | Server | Có | account, character, cấp/EXP, cấp skill, item, vật liệu, tiền tệ, tiến trình | CONFIRMED (GDD/người dùng nêu rõ item và user lưu server) |
| **Run/session (tạm)** | Server (token) + Client (trạng thái trận) | Không, trừ kết quả | phiên đánh, kill streak, buff đang chạy, HP/Ki hiện tại | PROPOSED |
| **Client-local** | Client | Có/không | cài đặt UI, cache sprite | PROPOSED |
| **Minimap (địa hình đã khám phá)** | **Server, theo tài khoản** | Có | bitmask theo chunk, theo map/vùng; cập nhật khi người chơi load chunk mới | CONFIRMED (người dùng chốt) |

Dữ liệu dùng chung client ↔ server (SHARED) hiện có: tập costume/part của client (`CostumeCatalog.json`, `PartDatabase.json`). Liên hệ với item/skin: OPEN (xem mục 3.8).

---

## 3. Entity logic (PROPOSED trừ khi ghi khác)

```
Account 1──N Character 1──N CharacterSkill
   │             │
   │             └──N EquippedItem (10 ô mỗi nhân vật) ──► ItemInstance 1──N ItemStatLine
   │                                                  │
   │                                                  └──(0..1)── GenLine
   ├──1 Wallet (vàng, đá quý) ── CurrencyLedger
   ├──N MaterialStack (mảnh đồ theo template, đá nâng cấp, đá chỉ số, đá pha lê)
   ├──N ItemInstance (túi đồ) · N TimedItem · N OwnedSkin
   ├──1 Squad/Loadout (nhân vật điều khiển, 2 AI, chiến thuật)
   └──1 Progress (vùng, boss, phó bản, hồi sinh) ── RunSession (tạm)
Static: DungeonTemplate(độ khó, EXP thưởng), FusionRule, SkillTemplate, SkillLevel, ItemTemplate, StatDefinition, ZoneTemplate, MobTemplate, BossTemplate,
        DropTable, StoneRule, GenDefinition, BalanceConfig(Max_ATK_Total, version)
```

### 3.1 Account
- Trường logic: `accountId`, `username`, **`passwordHash`** (thuật toán băm chuyên dụng), `createdAt`, `lastLoginAt`, `banned`, `role`.
- Khác reference: mật khẩu băm (reference lưu dạng rõ: CONFIRMED trong `docs\database\Teamobi2026.md`).
- **`AuthToken` (CONFIRMED hướng: token đăng nhập dài hạn ~30 ngày; PROPOSED về bảng):** `accountId`, `tokenHash` (băm của token; không lưu token gốc), `deviceInfo`, `createdAt`, `expiresAt`, `revokedAt?`; thu hồi khi đổi mật khẩu hoặc đăng xuất. **CONFIRMED (2026-10-07):** nhiều token mỗi tài khoản (mỗi thiết bị một), hạn trượt 30 ngày (mỗi lần dùng gia hạn thêm).
- **Đăng ký (CONFIRMED, người dùng chốt):** không cần email; đăng ký cả trong game và qua **web riêng ghi trực tiếp vào DB** (như Teamobi2026). Khi tạo tài khoản, **tự tạo 3 nhân vật (Xayda, Namec, Trái Đất) ở cấp 1** và dữ liệu mặc định (ví dụ ví tiền, `Squad`). Vì có hai cửa ghi vào DB, **thuật toán băm, quy tắc tên/mật khẩu và dữ liệu mặc định phải dùng chung một đặc tả** và có ràng buộc `UNIQUE` ở DB (PROPOSED). **Quy tắc đã chốt (CONFIRMED):** tên tài khoản 4 đến 20 ký tự (a-z, 0-9, `_`), mật khẩu từ 8 ký tự; web đăng ký dùng **PHP**, chống tạo hàng loạt bằng **giới hạn theo IP và CAPTCHA**. Cột tên tài khoản đủ cho 20 ký tự (reference: `varchar(20)`). **Thuật toán băm (CONFIRMED, người dùng chọn): bcrypt cost 12, mật khẩu tối đa 64 byte** (dùng chung web PHP và server Java).

### 3.2 Character
- GDD có 3 chủng tộc: Xayda, Namec, Trái Đất (CONFIRMED). Đội hình: 1 người điều khiển + 2 nhân vật AI (CONFIRMED).
- Trường logic: `characterId`, `accountId`, `race`, `level`, `exp`, chỉ số gốc theo cấp (máu, Ki, giáp, tấn công, hồi máu: GDD mục 1), `createdAt`.
- **CONFIRMED (người dùng xác nhận):** mỗi tài khoản có **3 nhân vật, đủ 3 chủng tộc**; mỗi nhân vật có cấp, EXP, skill và trang bị riêng. Số nhân vật mỗi chủng tộc: 1 (suy ra từ "3 nhân vật"; nếu muốn nhiều hơn thì sửa).
- **EXP và cấp:** xem mục 4.1. Tóm tắt (CONFIRMED, người dùng chốt): EXP **tính riêng cho từng nhân vật trong 3 nhân vật**; **thua vẫn nhận EXP theo số boss đã hạ**, **thắng trận cộng thêm 20%**; **cấp tối đa 100**. Bảng EXP mỗi cấp là **đề xuất của tôi (PROPOSED)**, chờ bạn duyệt.

### 3.3 CharacterSkill
- `characterId`, `skillId`, `level` (1–7 theo GDD). Điều kiện mở khóa và cách nâng cấp skill: UNKNOWN (GDD không nêu).
- Bảng skill tĩnh (`SkillTemplate`, `SkillLevel`) giữ sát thương %ATK, hồi chiêu, chi phí Ki, thời lượng từ GDD mục 3–4.

### 3.4 Squad / Loadout
- Đội hình = 3 nhân vật của tài khoản. **Chế độ thường:** 1 nhân vật điều khiển + 2 AI. **Chế độ hợp thể (CONFIRMED, người dùng xác nhận):** 2 nhân vật gộp thành 1, đội còn **1 hợp thể + 1 AI**.
- **Điều kiện hợp thể (CONFIRMED, người dùng chốt; bản cập nhật mới nhất):**
  - Đạo cụ **Bông tai Potara**, tạo bằng **100 mảnh bông tai**; **không bị tiêu hao**. **Mỗi tài khoản chỉ có 1 bông tai** (CONFIRMED, người dùng chốt) và **bông tai không nằm trong 10 ô của nhân vật** (mục 3.5a). Hệ quả (PROPOSED): bông tai là dữ liệu **cấp tài khoản** (`Account.potaraEarringOwned`, hoặc một `ItemInstance` đặc biệt duy nhất không tính vào 2.000 món, không bán/hủy được); chỉ có **một cặp hợp thể tại một thời điểm** cho cả tài khoản; sau khi ghép xong thì mảnh bông tai thừa vô dụng, nên server **không cho mua thêm mảnh và không rơi thêm mảnh** khi đã có bông tai (CONFIRMED, người dùng chốt). Server kiểm tra điều kiện này khi mua và khi sinh drop.
  - Chỉ áp dụng khi **cả 2 nhân vật được chọn cùng đạt cấp 50**; đạt đủ thì hợp thể ngay.
  - **Cặp hợp thể được cố định tại thời điểm bắt đầu trận** và **kéo dài đến hết trận** (CONFIRMED: mỗi trận là một combat liên tục, không có bước tách giữa trận); không tiêu hao (không bị trừ). **Đang combat không tách được.** Ngoài trận, người chơi chọn nhân vật dùng bông tai cùng nhân vật điều khiển.
  - **Thay thế các điều đã ghi trước đó:** giới hạn thời gian, tiêu hao Ki liên tục và mất skill hỗ trợ theo GDD **không còn là quyết định đã xác nhận** (người dùng nay chốt "đến hết combat, không tiêu hao"). **Skill khi hợp thể (CONFIRMED, người dùng chốt):** nhân vật chủ **không mất skill**; **nhân vật phụ mất toàn bộ skill của mình khi hợp thể**. Ví dụ Trái Đất (chủ) hợp thể với Xayda: hợp thể có skill của Trái Đất, mất skill của Xayda. Hợp thể chỉ dùng bộ skill của nhân vật chủ.
  - **Mảnh bông tai** rơi từ **boss một phó bản nhất định** hoặc **mua** (**mua bằng vàng** như mảnh đồ GDD mục 6, **giá cố định theo mảnh, có giới hạn số mảnh mua mỗi ngày**: CONFIRMED, người dùng chốt; giá cụ thể, số mảnh tối đa mỗi ngày, mốc đổi ngày (múi giờ) và phó bản rơi mảnh: UNKNOWN/OPEN). Cần bảng `DailyPurchaseCounter(accountId, shopItemId, dayKey, count)` kiểm tra và tăng trong cùng transaction với việc mua (PROPOSED).
  - **Hai luật từng ở trạng thái "đang chỉnh lại" ("+1% mỗi cấp cho mọi chỉ số cơ bản" và "nhân vật phụ tự tăng cấp theo nhân vật điều khiển") đã được tối ưu (PROPOSED, 2026-10-07, người dùng giao trợ lý "tối ưu, cân bằng"):** **không thêm bonus nào cho hợp thể và không tự nâng cấp nhân vật phụ**; mỗi nhân vật giữ cấp/đồ/EXP riêng (tối thiểu cấp 50); xem phân tích cân bằng ở mục 10.9. Mức +1% mỗi cấp ở mục 4.2 là công thức chỉ số theo cấp của từng nhân vật (đã chốt), không phải bonus hợp thể.
  - Hệ quả dữ liệu (PROPOSED): `Squad.fusionPair` (2 `characterId`, nhân vật chủ = nhân vật điều khiển) được **chốt khi vào trận** và giữ trong dữ liệu trận đến hết trận; `FusionRule` tĩnh gồm cấp 50, id bông tai, số mảnh 100; nguồn rơi mảnh nằm trong `DropTable` của boss phó bản tương ứng. Server kiểm tra (cả 2 cấp ≥ 50, có bông tai) khi chốt trận; không trừ bông tai.
  **Quy tắc hợp thể (CONFIRMED, người dùng chốt):** chỉ số hợp thể = **cộng chỉ số của 2 nhân vật**; **kỹ năng và hiệu ứng kỹ năng dùng của nhân vật chủ**. Lưu ý: đây là cộng đầy đủ, không phải cộng % chỉ số phụ như GDD mục 2 đề xuất; GDD cũng đề xuất thêm giới hạn thời gian, tiêu hao Ki liên tục và mất skill hỗ trợ, **các điểm này chưa được xác nhận (OPEN)**. Rủi ro cân bằng đã được giảm một phần nhờ ràng buộc đạo cụ và cấp tối thiểu (INFERRED); cách tiêu hao đạo cụ vẫn cần chốt. Cần lưu: nhân vật điều khiển, nhân vật chủ/phụ khi hợp thể, chiến thuật AI (DPS / CC / hỗ trợ). 
- AI đồng đội do **server điều khiển** (PROPOSED, INFERRED từ reference: `Pet`, `Boss`, `Bot` đều là `Player` do server cập nhật, `docs\gameplay\Teamobi2026.md`).

### 3.5 ItemTemplate (tĩnh)
- 5 slot: Áo, Quần, Găng, Giày, Nhẫn (CONFIRMED). Chỉ số chính và base: Áo = Giáp 2.500, Quần = Máu 200.000, Găng = ATK 20.000, Giày = Ki 200.000, Nhẫn = +25% sát thương chí mạng (CONFIRMED, GDD mục 6).
- Nhẫn có base dạng % còn bốn món kia là giá trị tuyệt đối; cần `StatDefinition` phân biệt kiểu giá trị (tuyệt đối / phần trăm), vì GDD tự lưu ý về trần chí mạng.

### 3.5a Ô trang bị của nhân vật (CONFIRMED, người dùng chốt)
- **Mỗi nhân vật có 10 ô** (tổng 30 ô cho 3 nhân vật), gồm **5 món trang bị** (Áo, Quần, Găng, Giày, Nhẫn), **skin** và **các vật phẩm hỗ trợ vĩnh viễn khác**.
- **CONFIRMED (người dùng chốt) cách chia 10 ô:** 5 ô trang bị + 1 ô skin + **4 ô vật phẩm hỗ trợ vĩnh viễn** (vật phẩm trang trí hoặc hỗ trợ vĩnh viễn; **không gồm Bông tai Potara**: CONFIRMED). Loại và hiệu ứng của vật phẩm hỗ trợ: UNKNOWN.
- Mô hình (PROPOSED): `EquipSlot` là enum 10 giá trị (`ARMOR`, `PANTS`, `GLOVES`, `BOOTS`, `RING`, `SKIN`, `SUPPORT_1..4`) và bảng gán `CharacterEquipment(characterId, slot, itemInstanceId | costumeId)`. Slot trang bị chứa `ItemInstance` (`location = EQUIPPED`); slot `SKIN` tham chiếu `OwnedSkin` (costume id sở hữu theo tài khoản, khóa theo chủng tộc), không phải `ItemInstance`.
- Giới hạn 2.000 món chỉ tính `location = BAG`; số ô đang mặc tối đa 30 (3 nhân vật × 10) và không tính vào 2.000.

### 3.6 ItemInstance và ItemStatLine
- `ItemInstance`: `itemId`, `ownerAccountId`, `templateId`, `slot`, `upgradePercent` (do đá nâng cấp, tăng % chỉ số cơ bản vĩnh viễn), `equippedByCharacterId?`, `createdAt`, **`version`** (khóa lạc quan).
- `ItemStatLine`: mỗi dòng chỉ số là **một bản ghi có kiểu**: `itemId`, `statType`, `value`, `source` (BASE, UPGRADE_STONE, STAT_STONE, CRYSTAL_STONE, GEN). **Không dùng JSON theo vị trí** như `items_bag`/`data_point` của reference.
- Mỗi món gán thêm được 4 chỉ số khác (GDD mục 6): tối đa 4 dòng phụ ngoài chỉ số chính (CONFIRMED).
- Luật nghiệp vụ kiểm ở server khi áp đá (CONFIRMED trong GDD, PROPOSED về nơi thi hành): đá chỉ số không trùng chỉ số trên cùng món; tối đa 25% mỗi chỉ số (tỷ lệ chí mạng 5%); mỗi lần thành công +1,5–2% (hoặc +0,1–0,5% với chỉ số tối đa 5%); xác suất thành công giảm dần khi gần tối đa; đá pha lê bắt đầu 10%, tối đa 50% trong 3 chỉ số còn lại, có thể trùng loại.
- `GenLine`: một thuộc tính GEN riêng từng món; đủ 5 món cùng thuộc tính thì kích hoạt hiệu ứng bộ (+100% một loại sát thương/chỉ số). **Bonus bộ tính từ trạng thái, không lưu** (CONFIRMED về luật, PROPOSED về cách lưu).

### 3.7 Vật liệu, tiền tệ
| Dữ liệu | Mô hình | Ghi chú |
|---|---|---|
| Vàng, đá quý | `Wallet` (số dư) + `CurrencyLedger` (mọi biến động có lý do, trước/sau) | Vàng mua mảnh đồ, đá, item dùng 1 lần có thời hạn. Đá quý đổi skin, item trang trí vĩnh viễn, đổi đá pha lê (CONFIRMED, GDD) |
| Mảnh đồ | `MaterialStack(templateId, quantity)`; đủ số lượng thì ghép thành 1 `ItemInstance` | Số mảnh cần: UNKNOWN |
| Đá nâng cấp, đá chỉ số, đá pha lê | `MaterialStack` theo loại | Đá pha lê siêu hiếm: nguồn rơi và số lượng: UNKNOWN |

### 3.7a Túi đồ (CONFIRMED, người dùng chốt)
- **Không giới hạn số ô:** thêm đồ thì thêm ô.
- **Vật liệu có tiêu hao** (mảnh, đá, v.v.) **xếp chồng** và hiển thị bằng **số lượng** (`MaterialStack`). **Món đồ không tiêu hao** (trang bị) là `ItemInstance` riêng lẻ, mỗi món một ô.
- **Quản lý số lượng (CONFIRMED, người dùng chốt):** **giữ hết đồ, chỉ bán/hủy thủ công** (không có bộ lọc tự bán). **Giới hạn 2.000 món** `ItemInstance` riêng lẻ (chỉ tính trang bị/đồ riêng lẻ, **không tính vật liệu xếp chồng**). **Khi đạt 2.000 thì không nhặt (nhận) thêm đồ riêng lẻ nữa và hiện thông báo để người chơi dọn hành trang** (CONFIRMED, người dùng chốt). Vì vậy 2.000 hoạt động như **giới hạn cứng đối với việc nhận đồ riêng lẻ**, không phải chỉ cảnh báo.
- Hệ quả dữ liệu (PROPOSED): truy vấn túi phải **phân trang**, chỉ mục theo `accountId`. **Đồ rơi khi túi đã đủ 2.000 món:** không được cấp; **mất hẳn** (CONFIRMED, người dùng chốt; hệ thống **không có hòm thư**), server gửi thông báo "túi đầy, hãy dọn hành trang" cho client; số lần bị từ chối nên ghi vào `RunProgress` để biết người chơi đã bỏ lỡ gì. Vàng và vật liệu xếp chồng (mảnh, đá, đá quý) vẫn nhận bình thường vì không chiếm ô (INFERRED). Kiểm tra giới hạn và tạo `ItemInstance` nằm **trong cùng transaction** để không vượt 2.000 khi nhiều mob chết cùng lúc (PROPOSED). **Cảnh báo (CONFIRMED, người dùng chọn):** cảnh báo khi túi **gần đầy** và **trước khi vào trận**. Ngưỡng "gần đầy": đề xuất **1.800 món** (90%), chỉnh được.
- **Bán/hủy thủ công hàng loạt (CONFIRMED, người dùng chốt):** vì túi đầy là chặn nhận đồ nên người chơi dọn thường xuyên; **chọn nhiều món một lần** để bán hoặc hủy. **Mỗi lần là một transaction có ghi nhật ký** (`SELL`, `DESTROY` trong `ItemLog` và `CurrencyLedger`); **hủy cần xác nhận**.
- **Giá bán thấp (CONFIRMED, người dùng chốt):** bán đồ chỉ nhận **vàng ít** (`sellPriceGold` thấp, không phải nguồn vàng chính); con số: UNKNOWN.
- **Đồ đang trang bị không nằm trong kho đồ (CONFIRMED, người dùng chốt):** đồ đã mặc ở slot trang bị, **không xuất hiện trong danh sách kho nên không thể chọn để bán hay hủy**. Do đó không có tình huống "lô chứa đồ đang trang bị" như tôi từng lo. Mô hình dữ liệu (PROPOSED): `ItemInstance.location` = `BAG` hoặc `EQUIPPED`; giới hạn 2.000 món **chỉ tính `BAG`** (INFERRED; số ô đang mặc tối đa 30, xem mục 3.5a). **Server kiểm tra lại khi nhận lệnh bán/hủy (món thuộc tài khoản và `location = BAG`), phòng client gửi sai hoặc gian lận: CONFIRMED, người dùng chốt.** **Không có khái niệm khóa đồ (CONFIRMED, người dùng chốt):** hủy có xác nhận là đủ; không cần trường `locked`.
- PROPOSED: mỗi lô có idempotency key; giới hạn số món mỗi lô để transaction không quá lớn; món không hợp lệ trong lô (do client gửi sai): bỏ qua và báo lại món bị bỏ; **không có tự bán khi nhặt** (đã chốt giữ hết).
- Dung lượng DB tăng theo số item rơi (mọi mob và boss đều rơi đồ tự động); người chơi chủ động bán/hủy (không có hòm thư hay cơ chế tự hết hạn).

### 3.8 Vật phẩm có thời hạn, skin, trang trí
- `TimedItem` (mua bằng vàng): có `expiresAt` tính theo **giờ server**.
- `OwnedSkin` / vật phẩm trang trí vĩnh viễn (mua bằng đá quý): skin tăng mạnh chỉ số; trang trí yếu hơn (CONFIRMED về mức độ tương đối).
- **CONFIRMED (người dùng chốt):** **skin = costume id của client** (`NroCostumeEntry.id`), **sở hữu theo tài khoản** (`OwnedSkin(accountId, costumeId)`). Mua bằng đá quý. Client đã có 414 costume với trường `gender` (0 Trái Đất, 1 Namếc, 2 Xayda, 3 mọi giới); **skin bị khóa theo chủng tộc của costume** (CONFIRMED, người dùng chốt): `gender` 0 chỉ Trái Đất, 1 chỉ Namếc, 2 chỉ Xayda, 3 dùng chung; sở hữu theo tài khoản nhưng chỉ nhân vật đúng chủng tộc mới mặc được. Item trang trí vĩnh viễn khác skin (yếu hơn): có phải costume không: OPEN.
- Hệ quả (PROPOSED): server cần danh sách costume hợp lệ (shared với client, nguồn dữ liệu chính thức: OPEN) để kiểm tra `costumeId` khi bán/mặc.

### 3.9 Progress và hồi sinh
- Vùng đã mở, boss đã hạ, tiến độ phó bản (tầng đã qua), số lần hồi sinh bằng vàng: **tối đa 2 lần mỗi trận** (CONFIRMED, người dùng chốt). Bộ đếm này chỉ nằm trong dữ liệu trận (runtime), **không cần lưu bền vững** và không cần mốc đổi ngày.
- Kill streak chỉ tồn tại trong trận (runtime), không lưu.
- **`RunProgress` (PROPOSED theo yêu cầu người dùng):** thống kê tiến độ trận tại từng mốc thời gian, mỗi bản ghi: `runId`, `timestamp`, tiến độ (số mob/boss đã hạ, tầng/khu vực, sát thương gây ra, vật phẩm đã nhặt). Dùng để biết trận đã tới đâu khi bị hủy và để đối chiếu phần thưởng.

### 3.10 Nhật ký (học từ reference)
- `CurrencyLedger`, `ItemLog` (cấp, ghép, nâng cấp, hủy): tương tự `history_transaction` của reference (CONFIRMED có ở Teamobi2026, nhưng chỉ chụp túi đồ trước/sau).
- **`AnomalyLog` (CONFIRMED về cách chống gian lận: chỉ ghi log và xử lý thủ công; PROPOSED về bảng):** ghi các sự kiện bất thường (thời gian trận quá ngắn, số boss quá nhanh, thưởng mỗi giờ vượt ngưỡng, sát thương vượt ngưỡng) kèm `accountId`, `runId`, thời điểm, chỉ số liên quan; không chặn tự động.
- Mỗi thao tác tiền/đồ mang **idempotency key** để client gửi lại không bị trừ hai lần. PROPOSED.

---

## 4. Chỉ số và tính toán
- **Mốc cân bằng:** `Max_ATK_Total` là hằng số do thiết kế chốt (GDD mục 7: ví dụ 500.000), lưu trong `BalanceConfig` có `version`. HP/ATK/giáp của mob và boss là **số tuyệt đối** tính một lần từ % trong bảng GDD, lưu trong `MobTemplate`/`BossTemplate`. Đổi `Max_ATK_Total` là một đợt "rebalance pass" (đổi version bảng, không đổi dữ liệu người chơi). CONFIRMED về ý định, PROPOSED về cách lưu.
- **Chỉ số tổng của nhân vật** = chỉ số gốc theo cấp + base item × (1 + % nâng cấp) + dòng chỉ số + GEN/bonus bộ + skill/buff đang chạy. **Trần tổng chí mạng: KHÔNG giới hạn (CONFIRMED, người dùng chốt)** cho sát thương chí mạng và tỷ lệ chí mạng cộng dồn từ mọi nguồn (nhân vật theo cấp, đồ, đá chỉ số, đá pha lê, GEN). Lưu ý: GDD mục 6 ("Lưu ý khi hoàn thiện chi tiết") vẫn ghi cần đặt trần cứng; quyết định của người dùng thay thế dòng đó (tôi không sửa GDD). **Cách xử lý (CONFIRMED, người dùng chốt):** **kẹp tỷ lệ chí mạng ≤ 100%** (giới hạn toán học, không phải trần thiết kế); **sát thương chí mạng không trần**. Server dùng kiểu số đủ rộng (64 bit hoặc số thực) để không tràn số khi cộng dồn (PROPOSED). Rủi ro cân bằng cần theo dõi sau: sát thương chí mạng cộng dồn sẽ tăng tuyến tính.
- Server tính lại chỉ số khi trang bị hoặc đá thay đổi; client chỉ hiển thị.
- **Thứ tự tính chỉ số (CONFIRMED, người dùng chốt):** `chỉ_số_nhân_vật_theo_cấp + chỉ_số_đồ` rồi nhân `(1 + tổng % cộng dồn)` (các % từ đá chỉ số, GEN và bonus bộ cộng với nhau, **không nhân lồng nhau**), **sau đó** nhân buff/hợp thể riêng. Phần theo cấp (+1%/cấp) chỉ áp lên chỉ số cơ bản của nhân vật, không nhân vào đồ (mục 4.2).
- **Áp dụng hợp thể (CONFIRMED, người dùng chốt):** tính chỉ số cuối của **từng nhân vật** theo công thức trên, **cộng hai kết quả** để ra chỉ số hợp thể, **rồi mới nhân buff** (Kaioken, Biến khỉ...). Hợp thể là cộng, buff là nhân. Sát thương và tỷ lệ chí mạng là chỉ số cộng, không nhân.
- Công thức damage và Tự hủy `(%giảm thương × máu còn lại) + máu tối đa`: GDD có mô tả, nhưng thứ tự áp giáp, chí mạng: UNKNOWN.

### 4.2 Chỉ số nhân vật theo cấp (người dùng đã chốt)
**CONFIRMED (người dùng chốt):**
- Chỉ số **riêng của nhân vật** ở **cấp 100**: **HP = 575.000, Ki = 575.000, ATK = 26.000, Giáp = 22.000** (chưa tính trang bị).
- Mức tăng **1% mỗi cấp tính trên chỉ số cơ bản của nhân vật**, **chưa tính chỉ số cộng thêm của đồ** (trang bị không bị nhân thêm theo cấp; trang bị cộng riêng bên ngoài).
- **Chỉ số không khác nhau theo chủng tộc** (cả 3 chủng tộc dùng chung bảng).
- **Sát thương chí mạng:** cơ bản ban đầu **0%**, đến **cấp 100 cộng thêm 100%**. **Tỷ lệ chí mạng:** **10% là mức cơ bản ở cấp 100**.

**Điều chỉnh so với bản trước (INFERRED của tôi đã sai):** 100% và 10% **không phải trần tổng**; đó là giá trị cơ bản của nhân vật ở cấp 100. Vì vậy **trần tổng chí mạng (đồ + đá + GEN) lại là OPEN** (xem mục 4 và câu hỏi mở).

**Cách tính cụ thể (CONFIRMED, người dùng chốt: tuyến tính; chí mạng 1% × cấp và 0,1% × cấp):**
- `chỉ_số(L) = chỉ_số_cấp_1 × (1 + 0,01 × (L − 1))`, tuyến tính theo 1% chỉ số cấp 1 mỗi cấp; cấp 100 gấp 1,99 lần cấp 1. Chỉ số cấp 1 = giá trị cấp 100 ÷ 1,99. (Người dùng đã chốt tuyến tính, không lãi kép.)
- Sát thương chí mạng cơ bản = `1% × L` (cấp 1: 1%, cấp 100: 100%); tỷ lệ chí mạng cơ bản = `0,1% × L` (cấp 100: 10%). **Đã chốt (CONFIRMED):** công thức tuyến tính, chí mạng = `1% × L` và `0,1% × L` (cấp 1: 1% và 0,1%).
- Chỉ số tổng = chỉ số nhân vật theo cấp + chỉ số trang bị và đá/GEN (mục 4 trên): phần theo cấp không nhân vào trang bị.

| Cấp | HP và Ki | ATK | Giáp | Sát thương chí mạng | Tỷ lệ chí mạng |
|---|---|---|---|---|---|
| 1 | 288.945 | 13.065 | 11.055 | 1% | 0.1% |
| 25 | 358.291 | 16.201 | 13.709 | 25% | 2.5% |
| 50 | 430.528 | 19.467 | 16.472 | 50% | 5% |
| 75 | 502.764 | 22.734 | 19.236 | 75% | 7.5% |
| 100 | 575.000 | 26.000 | 22.000 | 100% | 10% |

Hằng số nằm trong dữ liệu tĩnh (`BalanceConfig`). **Chỉ số hồi máu (CONFIRMED, người dùng chốt):** hồi **0,5% HP tối đa mỗi giây, cố định theo cấp** (không tăng khi lên cấp), **phụ thuộc Ki** (hết Ki thì không hồi), đúng GDD mục 1; vật phẩm hỗ trợ hồi HP và Ki. Tức hồi theo HP tối đa nên số HP hồi tăng theo HP; **không tiêu Ki** (CONFIRMED): chỉ cần Ki lớn hơn 0 thì hồi, hết Ki thì dừng hồi. Server cập nhật chỉ số khi lên cấp **sau trận** (mục 4.1).

### 4.1 EXP và cấp (quy tắc đã chốt + đề xuất bảng)
**CONFIRMED (người dùng chốt):**
- EXP tính riêng cho từng nhân vật; đủ mốc thì lên 1 cấp.
- **Thua vẫn nhận EXP, tính theo số boss đã đánh bại.** **Thắng trận cộng thêm 20%.**
- **Cấp tối đa 100** (cũng là ý nghĩa của mốc "50/100": hợp thể mở ở cấp 50, 100 là cấp tối đa; INFERRED).
- Lượng EXP phụ thuộc độ khó phó bản (`DungeonTemplate`).

**Đã duyệt (CONFIRMED, người dùng duyệt):** bảng EXP cần để lên cấp (`100 × L²`, L = 1…99, cấp 100 không cần EXP) và cách tính EXP mỗi boss từ độ khó phó bản (mặc định `M = 3`, chỉnh được). **Người dùng yêu cầu thêm:** hệ số giảm EXP khi nhân vật vượt cấp phó bản, và kill streak có tăng EXP. **Người dùng đã duyệt nguyên các con số đề xuất cho hệ số vượt cấp và kill streak (CONFIRMED)**, và chốt thêm: **mỗi nhân vật nhận đủ phần EXP của phó bản** (không chia nhỏ), **thưởng nhẹ EXP khi nhân vật thấp cấp hơn khá nhiều** (xem mục 6). Chi tiết (con số thưởng thấp cấp là PROPOSED):
1. **Công thức EXP của một trận** (cho mỗi nhân vật):
   `EXP_mỗi_boss_hiệu_dụng = EXP_mỗi_boss(phó bản) × hệ_số_vượt_cấp(nhân vật) × (1 + thưởng_kill_streak lúc boss chết)`; `EXP_trận = Σ EXP_mỗi_boss_hiệu_dụng` của các boss đã hạ; nếu thắng trận: cộng thêm 20% của tổng đó (`× 1,2`).
   Hiểu "cộng 20%" là cộng thêm 20% trên EXP đã tích lũy trong trận (OPEN: hoặc 20% EXP cố định của phó bản).
2. **EXP mỗi boss** lấy từ `DungeonTemplate`, tính từ cấp khuyến nghị `R` và số boss `B` của phó bản: `EXP_mỗi_boss = EXP_cần_lên_cấp(R) ÷ (1,2 × B × M)`, với `M` = số trận thắng toàn bộ boss để lên 1 cấp khi chơi đúng cấp khuyến nghị (đề xuất `M = 3`; chỉnh được). Ví dụ phó bản `R = 50`, `B = 5`: EXP mỗi boss ≈ 13.889; thắng trọn gói ≈ 83.333 (1/3 mốc cấp 50). Thua sau 2 boss: ≈ 27.778 (không cộng 20%).
3. **Bảng EXP cần để lên cấp kế tiếp:** `EXP_cần_lên_cấp(L) = 100 × L²` (L = 1…99). Cấp 100 không cần EXP. Tổng EXP từ cấp 1 đến cấp 50 ≈ 4.042.500 ; đến cấp 100 ≈ 32.835.000 (đoạn 50→100 gấp ≈ 8 lần đoạn 1→50, giữ nhịp chậm dần ở cuối).

| Cấp L | EXP cần để lên cấp L+1 | Tổng EXP để đạt cấp L+1 |
|---|---|---|
| 1 | 100 | 100 |
| 10 | 10.000 | 38.500 |
| 20 | 40.000 | 287.000 |
| 30 | 90.000 | 945.500 |
| 40 | 160.000 | 2.214.000 |
| 49 | 240.100 | 4.042.500 |
| 50 | 250.000 | 4.292.500 |
| 60 | 360.000 | 7.381.000 |
| 70 | 490.000 | 11.679.500 |
| 80 | 640.000 | 17.388.000 |
| 90 | 810.000 | 24.706.500 |
| 99 | 980.100 | 32.835.000 |

4. **Áp dụng lên cấp sau trận:** EXP ghi ngay khi boss chết (không mất nếu rớt mạng), nhưng việc lên cấp và đổi chỉ số chỉ áp dụng khi trận kết thúc, để không đổi chỉ số giữa trận. Dư EXP khi lên cấp được giữ cho cấp kế tiếp.
5. **Cấp 100:** EXP dư bị bỏ (hoặc tích lũy cho hệ thống khác: OPEN).
6. **Hệ số giảm EXP khi quá cấp phó bản (CONFIRMED về việc áp dụng; con số PROPOSED):** tính riêng cho từng nhân vật với `chênh = cấp nhân vật − R` (R = cấp khuyến nghị của phó bản). `hệ_số_vượt_cấp = 1,0` nếu `chênh ≤ 5`; ngược lại `max(0,1 ; 1 − 0,05 × (chênh − 5))`. Ví dụ: chênh 10 → 0,75; chênh 15 → 0,5; từ chênh 23 trở lên → 0,1 (sàn). **Thưởng thấp cấp (CONFIRMED về việc có; con số PROPOSED):** nếu `chênh < −10` thì `hệ_số = 1 + min(0,2 ; 0,02 × (−chênh − 10))` (ví dụ chênh −20 → +20%, tối đa +20%); từ chênh −10 trở lên: 1,0. Áp dụng cho EXP mỗi boss trước khi nhân thưởng kill streak và trước khi cộng 20% thắng trận. Server tính theo cấp nhân vật **lúc vào trận**, không đổi giữa trận.
7. **Hợp thể và EXP:** mỗi nhân vật trong cặp vẫn nhận EXP riêng (như trên). Hợp thể cần cả 2 nhân vật cấp 50.
8. **Kill streak tăng EXP (CONFIRMED về việc có tăng EXP; con số PROPOSED, chờ bạn xác nhận):** chuỗi tăng 1 mỗi lần hạ mob hoặc boss nếu cách lần hạ trước không quá `T` giây (đề xuất `T = 5 s`), quá hạn thì về 0. `thưởng_kill_streak` theo mốc chuỗi: ≥10: +5%; ≥25: +10%; ≥50: +15%; ≥100: +20%; trần **+20%** (đề xuất). Server giữ bộ đếm trong trạng thái trận (runtime), không lưu. Thưởng áp dụng vào **EXP của boss tại lúc boss chết**; mob thường không cho EXP (EXP chỉ tính theo boss), nên chuỗi mob chủ yếu "nuôi" thưởng cho boss. GDD mục 8 còn nói tăng % vàng: vàng rơi cũng nhân thưởng này (PROPOSED, OPEN có áp cho item không). Trần +20% để không phá nhịp bảng EXP (bảng tính với chuỗi 0; chuỗi tối đa làm EXP nhanh hơn khoảng 17% so với tính toán `M`).
9. Mọi con số (hệ số 100, lũy thừa 2, `M = 3`) là **dữ liệu tĩnh có version**, đổi được khi cân bằng (`BalanceConfig`); hiện không có dữ liệu nhịp chơi (độ dài trận, số boss mỗi phó bản) để hiệu chỉnh, nên chỉ là điểm khởi đầu.

---

## 5. Persistence flow (PROPOSED)

```
Đăng nhập → load Account, Characters, Items, Materials, Wallet, Progress (async, ngoài vòng chơi)
Vào trận → server tạo RunSession (token, vùng/phó bản, thời điểm bắt đầu, snapshot chỉ số) và một instance Zone cho trận
Trong trận → server mô phỏng (zone-like). **Boss chết → phần thưởng item/vàng tự động vào túi ngay lúc boss chết** (server ghi ngay, 1 transaction, kèm RunProgress tại mốc đó). **Mob thường cũng rơi item/vàng tự động vào túi khi mob chết** (CONFIRMED, người dùng chốt, như boss); mỗi lần nhận là một bản ghi cấp thưởng (PROPOSED: ghi bền vững ngay nhưng gộp các lần nhận trong cùng một khoảng ngắn thành một transaction để không ghi DB theo từng mob; túi đầy thì xử lý thế nào: OPEN). **EXP tính theo số boss đã hạ, ghi ngay khi mỗi boss chết** (thua hay mất kết nối vẫn giữ EXP đã ghi; thắng trận cộng thêm 20%, xem mục 4.1)
Kết thúc trận → thắng: cộng thêm 20% EXP của trận cho từng nhân vật; áp dụng lên cấp (nếu đủ mốc) **sau trận**, không đổi chỉ số giữa trận (PROPOSED); thua/hủy/mất kết nối: không cộng 20% nhưng EXP của các boss đã hạ vẫn giữ
Thao tác kinh tế (ghép mảnh, nâng cấp, đá, đổi đá quý, mua) → 1 transaction:
    kiểm tra sở hữu + version → trừ vật liệu/tiền → RNG ở server → cập nhật item → ghi ledger/log → commit
Lưu: ngay sau thay đổi quan trọng + định kỳ + khi thoát (không lưu toàn bộ mỗi 2 phút như reference)
```
- **Transaction boundary:** mỗi thao tác kinh tế là một transaction; reference hầu như không có transaction (CONFIRMED), đây là điểm cải tiến.
- **Concurrency:** `ItemInstance.version` và `Wallet` dùng khóa lạc quan; hai thiết bị cùng đăng nhập: chính sách OPEN.
- **Thứ tự lưu:** ledger ghi cùng transaction để không mất hoặc nhân đôi đồ khi sập giữa chừng.

---

## 6. Chống gian lận cho mô hình chơi đơn (PROPOSED)
Combat theo mô hình Teamobi2026 (server tính sát thương, HP, cooldown, drop và chạy AI), nên phần lớn kết quả trận do server tạo ra. Server vẫn phải coi mọi gói từ client là **không tin cậy**:
- Server cấp `RunSession` (token) khi vào trận và là nơi giữ trạng thái trận (HP, Ki, cooldown, hợp thể, kill streak); phần thưởng chỉ cấp từ phiên do server giữ, một lần.
- Server chọn drop và EXP bằng RNG của server; client không gửi "tôi nhận món X".
- Client chỉ báo **vị trí và mục tiêu**. **Quyết định của người dùng (CONFIRMED): server KHÔNG kiểm tra vị trí và tầm đánh, giống Teamobi2026 (gán thẳng).** Hệ quả cần ghi nhận: không chặn được teleport hay đánh từ xa; vì game chơi đơn nên ảnh hưởng chủ yếu tới chính người chơi và tới **độ tin cậy của phần thưởng** (ví dụ né sát thương hoặc hạ boss nhanh bất thường). Server vẫn nên giới hạn phần thưởng theo thời gian và theo thống kê tiến độ (PROPOSED, giữ nguyên quyết định không kiểm vị trí).
- Chỉ số, item, tiền **không bao giờ** nhận từ client.
- **Mất kết nối giữa trận (CONFIRMED, người dùng chốt; cập nhật cho Android):** server **tạm dừng trận tối đa 30 giây, tối đa 2 lần mỗi trận**, cho vào lại bằng token; **quá hạn thì hủy trận nhưng giữ phần thưởng đã nhận** (item/vàng/EXP theo boss đã ghi ngay khi rơi). Đồ rơi trực tiếp khi đánh boss; khi người chơi nhận thì server **ghi ngay** cùng thống kê tiến độ (`RunProgress`). Chi tiết: `docs\protocol\BattleBall.md` mục 14.3.

---

## 7. Điểm khác reference (lý do)
| Teamobi2026 (CONFIRMED) | BattleBall (PROPOSED) | Lý do |
|---|---|---|
| `player` ~75 cột, JSON lồng chuỗi, theo vị trí | Bảng chuẩn hóa, `ItemStatLine` có kiểu | Truy vấn được, mở rộng được, không vỡ khi thêm trường |
| Mật khẩu dạng rõ | Băm | Bảo mật |
| Pool 1 kết nối | Pool cấu hình > 1 | |
| Không migration | Migration có version | |
| Gần như không transaction | Transaction cho thao tác kinh tế | Chống mất/nhân đôi |
| Lưu toàn bộ mỗi 2 phút | Lưu theo sự kiện + định kỳ + khi thoát | |
| Template nạp từ DB lúc khởi động | Template/config có version; cùng ý tưởng | Hỗ trợ rebalance pass |
| Web ↔ server qua bảng polling | Chưa có | Không có yêu cầu web |

---

## 8. Quyết định

### CONFIRMED
- Chơi đơn; server lưu item và dữ liệu người dùng.
- 3 chủng tộc, đội hình 1 người điều khiển + 2 AI, đội hợp thể 2 người.
- 5 slot trang bị, base stat từng món, mỗi món gán thêm 4 chỉ số; mảnh đồ ghép thành món; đá nâng cấp, đá chỉ số, đá pha lê, GEN với các luật và trần trong GDD.
- Tiền tệ: vàng và đá quý, công dụng như GDD.
- Đăng nhập chồng: kick phiên cũ, phiên mới vào (như Teamobi2026); mỗi trận tối đa 2 lần hồi sinh bằng vàng; server mô phỏng ở **20 Hz (50 ms)** (khác 1 giây của Teamobi2026).
- Mỗi tài khoản 3 nhân vật (đủ 3 chủng tộc); hợp thể 2 thành 1, đội còn 1 hợp thể + 1 AI; raid boss = 3 nhân vật của một người chơi; cần mạng liên tục; combat theo mô hình Teamobi2026 (server tính, client báo vị trí và mục tiêu); server **không** kiểm tra vị trí và tầm đánh; mỗi trận chạy như Zone của Teamobi2026 và **không có PvP**; mất kết nối thì hủy trận nhưng giữ phần thưởng đã nhặt (ghi ngay khi nhặt, kèm thống kê tiến độ); hợp thể = cộng chỉ số 2 nhân vật, skill và hiệu ứng skill theo nhân vật chủ, **cần Bông tai Potara (ghép từ 100 mảnh, không tiêu hao); cả 2 nhân vật được chọn phải cùng đạt cấp 50; cặp cố định từ đầu trận, hợp thể đến hết combat, không tiêu hao; EXP mỗi trận thắng tính riêng cho từng nhân vật**.
- `Max_ATK_Total` là hằng số thiết kế cố định; chỉ số boss là số tuyệt đối.
- Reference lưu player bằng JSON theo vị trí, mật khẩu dạng rõ, ít transaction (`docs\database\Teamobi2026.md`).

### PROPOSED
- Bảng chuẩn hóa: Account, Character, CharacterSkill, ItemInstance, ItemStatLine, MaterialStack, Wallet, Ledger, Progress, RunSession, và các bảng tĩnh có `version`.
- Mật khẩu băm; transaction cho thao tác kinh tế; khóa lạc quan bằng `version`; idempotency key.
- Server là nơi RNG (drop, đá, ghép) và nơi duy nhất cấp thưởng; client không gửi chỉ số hay item.
- Dữ liệu tĩnh (skill, boss, drop, đá, GEN, `Max_ATK_Total`) nằm ở server có version.

### UNKNOWN
- Số mảnh để ghép một món; nguồn rơi và tỷ lệ mọi item; số lượng đá.
- Điều kiện nâng cấp skill; cách tăng chỉ số khi lên cấp (bảng EXP).
- Công thức damage đầy đủ, thứ tự cộng %; công thức Tự hủy khi kết hợp giáp, chí mạng.
- Cấu trúc map, vùng, chunk, minimap.

### OPEN QUESTIONS
Đã trả lời: công thức cấp tuyến tính, chí mạng 1%/0,1% mỗi cấp, hồi HP không tiêu Ki, thứ tự tính chỉ số (hợp thể cộng rồi nhân buff), không khóa đồ; kick phiên cũ khi đăng nhập chồng; hồi sinh tối đa 2 lần mỗi trận; tick 20 Hz; túi không giới hạn ô, giữ hết, giới hạn 2.000 món (đạt thì mất đồ rơi, cảnh báo khi gần đầy và trước vào trận), bán thủ công giá thấp, đồ đang trang bị không nằm trong kho; hồi HP 0,5%/giây phụ thuộc Ki; chí mạng không trần (kẹp tỷ lệ ≤100%); chỉ số mục tiêu cấp 100 (mục 4.2); hợp thể (nhân vật phụ mất skill, chủ giữ); mảnh bông tai mua bằng vàng; **bỏ hẳn hòm thư**; bán/hủy hàng loạt; số nhân vật; hợp thể (cặp chốt đầu trận, kéo dài đến hết trận, không tiêu hao, cần cả 2 cấp 50, Bông tai Potara 100 mảnh); EXP riêng từng nhân vật, chỉ khi thắng trận, theo độ khó phó bản; raid boss; cần mạng liên tục; combat theo Teamobi2026; không kiểm vị trí; zone-like không PvP; mất kết nối (hủy trận, giữ item/vàng đã nhận); boss và mob thường rơi đồ tự động vào túi; minimap lưu server theo tài khoản; skin = costume id, sở hữu theo tài khoản, khóa theo chủng tộc.

Còn mở:
1. Giá bán từng món (thấp bao nhiêu). *(Ngưỡng cảnh báo gần đầy: **CONFIRMED 1.800 món**, người dùng chốt 2026-10-07.)*
2. 4 ô vật phẩm hỗ trợ vĩnh viễn mỗi nhân vật (bạn sẽ liệt kê sau khi có danh sách vật phẩm), nguồn có được, và item trang trí vĩnh viễn có phải costume không.
3. Mảnh bông tai (chờ danh sách phó bản của bạn): phó bản rơi mảnh, giá vàng mỗi mảnh, số mảnh mua tối đa mỗi ngày. *(Mốc đổi ngày: **CONFIRMED 00:00 giờ Việt Nam, UTC+7**, 2026-10-07; `dayKey` của `DailyPurchaseCounter` tính theo UTC+7.)*
4. ~~Zone riêng cho mỗi người chơi mỗi trận~~ **CONFIRMED (2026-10-07):** game chơi đơn hoàn toàn, **không có kết bạn và không tồn tại người chơi khác** trong bất kỳ map nào (kể cả làng tân thủ); mỗi trận/làng chỉ có đúng một người chơi, nên không cần tầm nhìn/đồng bộ nhiều người.
5. **Công nghệ database: CONFIRMED MariaDB/MySQL (InnoDB), 2026-10-07.** **Deploy giai đoạn đầu: CONFIRMED một VPS Linux chạy server Java + MariaDB + web PHP** (tách DB khi cần). **Nhập dữ liệu cân bằng: CONFIRMED qua trang admin nhập tay** (PROPOSED hệ quả: cần đăng nhập admin riêng với phân quyền, ghi `version` mỗi lần sửa, nhật ký ai sửa gì, và server nạp lại dữ liệu tĩnh theo version mà không cần khởi động lại). Còn mở: nguồn dữ liệu costume chính thức. (Dòng cũ, đã chốt, giữ để truy vết: cách nhập dữ liệu cân bằng (bảng tính) vào bảng tĩnh; nguồn dữ liệu costume chính thức (client hiện đọc từ thư mục Teamobi2026).
6. Số mảnh ghép một món (ngoài bông tai), nguồn rơi và tỷ lệ mọi item (hiện UNKNOWN trong GDD).

---

## 9. Nguồn dữ liệu asset (người dùng chỉ ra 2026-10-07)

Ba vị trí sau do **người dùng xác nhận** là nơi chứa asset. Nội dung dưới đây do trợ lý **kiểm tra trực tiếp** thư mục/file (liệt kê và đếm), chưa đọc sâu từng file; phần "ý nghĩa" ghi rõ nhãn.

| Vị trí | Người dùng nói | Quan sát được (CONFIRMED: đã liệt kê) |
|---|---|---|
| `C:\Users\cuonghayho\Desktop\data` | Font chữ cho sát thương và hiệu ứng âm thanh | Thư mục `audio` (`music`: 6 file .ogg; `sound`: 79 file .wav, tên kiểu `14_chuong_kamehameha.wav`), `damage_font` (13 file: `atlas\number.bytes`, `tahoma_7.bytes`, `tahoma_7b.bytes`, `tahoma_8b.bytes`, `number_coords.json` với chuỗi ký tự ` 0123456789+-` và định dạng big-endian short `[x,y,w,h]`; 3 font `.ttf`: `barmeneb`, `chelthm`, `staccato`; `splash\sp0.png`; `demo_hien_thi_dame.png`; script `export_damage_font_assets.py`), `icon` (1x/2x/3x/4x, **93.904 file png** theo tên như `Cai_trang_*`), `ui_menu` (153 file: buttons, frames, icons, pointers, `NroDemoMenuOpener.cs`, `nro_menu_interactive.html`), script `mirror_icons_1x_2x_3x.py`. Tổng ~94.156 file (94.059 png) |
| `E:\PRJNRO\Teamobi2026\SRC\data` | Data về asset | Thư mục `effdata` (152 file `DataEffect_n`), `effect` (556 png `ImgEffect_n`, thang x1 đến x4), `icon` (64.315 png), `img_by_name` (1.154 png), `item_bg_temp` (1.816 png), `map` (`tile_map_data`, `eff_map`, `item_bg_map_data`, `tile_set_Info`; 361 file), `mob` (436 file, có `mob.rar`), `res` (4.308 file), `update_data` (6 file: `arrow`, `dart`, `effect`, `image`, `part`, `skill`). Hầu hết có thang độ phân giải x1 đến x4 |
| `E:\PRJNRO\Teamobi2026\SRC\sql\nro1.sql` | Nơi tham chiếu tọa độ/thành phần frame của từng loại skin, v.v. | Bảng `part` (~2.099 dòng: `id`, `TYPE`, `DATA` là danh sách bộ ba `[spriteId, dx, dy]`), `array_head_2_frames` (`id`, `data` danh sách frame đầu), `head_avatar` (`head_id`→`avatar_id`), `img_by_name` (`NAME`, `n_frame`), `skill_template` (27 dòng; cột `dam_info`, `skills` là JSON các cấp với `damage`, `dx`, `dy`, `mana_use`, `cool_down`, `power_require`, `price`...), cùng `item_template`, `map_template`, `mob_template`, `npc_template`, `bg_item_template`... |

**Ý nghĩa và hệ quả:**
- INFERRED: `part.DATA` mô tả mỗi part gồm các mảnh sprite kèm độ lệch (dx, dy); đây là "thành phần frame" mà người dùng nói và khớp với `NroMannequin` (984 part) trong BattleBall; chưa đối chiếu từng giá trị với `PartDatabase.json`.
- INFERRED: `skill_template.skills` cho thấy cấu trúc dữ liệu skill của NRO (sát thương theo %, tầm `dx`/`dy`, `cool_down` mili giây, `mana_use`); **đây là dữ liệu tham khảo của reference, không phải bảng skill BattleBall** (bảng skill chi tiết của BattleBall vẫn UNKNOWN, chờ người dùng). Ghi nhận: `dx`/`dy` tương ứng khái niệm tầm/hộp va chạm skill trong `docs\protocol\BattleBall.md` (OPEN 7).
- INFERRED: font số sát thương và âm thanh chưa nằm trong dự án BattleBall; cần chọn cách đưa vào client (sao chép hay tham chiếu).
- Tên file `.sql` là bản dump có dữ liệu của reference; mật khẩu/tài khoản trong đó không được dùng cho BattleBall.
- **Mục đích sản phẩm (CONFIRMED, người dùng nói 2026-10-07):** BattleBall là **sản phẩm cho báo cáo môn học, không có nhu cầu thương mại**. Asset là của game NRO (reference); việc dùng chúng nằm trong phạm vi học tập này. Nếu sau này phát hành hay thương mại hóa thì phải xem lại nguồn gốc/bản quyền asset (ghi nhận, không phải việc hiện tại).
- **Nguồn chính thức (CONFIRMED, người dùng chốt 2026-10-07):** asset **được sao chép sang dự án BattleBall** (bỏ phụ thuộc đường dẫn tuyệt đối của `NroSpriteResolver`/`NroCostumeCatalog`). **Việc sao chép thực tế chưa làm** (chưa có yêu cầu thực thi; đụng tới `E:\PRJNRO\BattleBall`). Còn mở: sao chép những gì (toàn bộ ~160.000 file hay chỉ tập cần dùng), vào thư mục nào, và dữ liệu `nro1.sql` (`part`, `skill_template`...) được xuất ra định dạng nào (JSON/`.proto`/bảng tĩnh MariaDB).
- **Chốt thêm (CONFIRMED, người dùng 2026-10-07):** (1) sao chép **toàn bộ** asset ở cả 3 nguồn; (2) tiêu chí chọn thang = **cả độ phân giải, RAM và DPI** của máy; (3) asset **tải từ server ở lần đầu** (như `DataGame` của Teamobi2026). Hai việc "chọn vị trí thư mục" và "định dạng xuất `nro1.sql`" người dùng **giao trợ lý chọn phương án tối ưu**: phương án dưới đây là **PROPOSED**, chờ người dùng xác nhận.
- **ĐÃ THỰC HIỆN (CONFIRMED, 2026-10-07, người dùng yêu cầu "làm đi"):** đã sao chép sang `E:\PRJNRO\BattleBall\SourceData\`: `data\` (73.105 file, ~560 MB, từ `Teamobi2026\SRC\data`), `ui_audio\` (94.156 file, ~358 MB, từ `Desktop\data`), `tables\nro1.sql` (bản y hệt, đã `cmp`). Robocopy báo 0 file lỗi. Chưa sửa `Assets\` hay code BattleBall; bản gốc không bị đổi. Chưa xuất `nro1.sql` ra JSON; chưa cấu hình Unity bỏ qua thư mục này (nằm ngoài `Assets\` nên Unity không nhập). Bố cục dưới đây vẫn là PROPOSED cho phần chưa làm.
- **ĐÃ SỬA CODE (2026-10-07, người dùng yêu cầu việc 1):** `Assets\NroMannequin\Runtime\NroSpriteResolver.cs`: hằng `DefaultServerRoot` (đường dẫn cứng `E:\PRJNRO\Teamobi2026\SRC`) được thay bằng **phương thức** `GetDefaultServerRoot()` trả `<thư mục dự án>\SourceData` (tính từ `Application.dataPath`); `_serverRoot` mặc định rỗng (rỗng = dùng mặc định). **Bài học (CONFIRMED qua thử nghiệm):** bản đầu dùng *thuộc tính tĩnh* `DefaultServerRoot => ...` làm Unity báo component `NroSpriteResolver` trong scene là "Missing script" (kiểm chứng thất bại `Resolver:False`); đổi sang phương thức thì hết lỗi, nguyên nhân gốc chưa tìm ra; 5 chỗ `Path.Combine(_serverRoot, ...)` đổi sang `ServerRootPath`. Scene `Assets\NroMannequin\Scenes\CostumeTester.unity` (dòng 7379) từng lưu cứng đường dẫn cũ, nay để rỗng. `NroCostumeCatalog` không chứa đường dẫn tuyệt đối (đã kiểm bằng tìm kiếm), nên không cần sửa. **Đã kiểm chứng (CONFIRMED, 2026-10-07):** chạy Unity 2022.3.62f2 batch `NroCostumeTesterVerification.RunVerification`: 8/8 mục PASS (414 costume x 4 thang zoom, 33 tư thế, 0 ngoại lệ). Sau đó đã **thêm mục 7b vào `NroCostumeTesterVerification.cs`** (kiểm tra đường dẫn): `ServerRootPath` phải bằng `<project>\SourceData` và không chứa `Teamobi2026`; tồn tại `data\update_data\image`, `data\res\x4\Big0`, `data\icon\x4`; sau `ClearAllCache()` nạp được atlas Big0 x4 (1020x1020) và icon rời 1 từ đĩa. **Kết quả: 9/9 PASS** (chạy lại 2026-10-07). Lưu ý còn lại: `image.bytes` vẫn nạp từ `Assets\NroMannequin\Data\image.bytes` (nhánh ưu tiên đầu của `ResolveImageBinaryPath`), bản trong `SourceData\data\update_data\image` chỉ là dự phòng; **đã đối chiếu (CONFIRMED, 2026-10-07): hai bản và bản gốc `Teamobi2026\SRC\data\update_data\image` giống hệt nhau từng byte** (26.975 byte, MD5 `8999cda247bbe9b28ecaa685d2390f00`), nên nhánh nào được chọn cũng cho cùng kết quả. Lưu ý: `Application.dataPath` chỉ đúng trong Editor/bản chạy máy tính; trên Android phải chuyển sang thư mục cache của bộ tải về (`Application.persistentDataPath`) khi làm phần tải asset.
- **ĐÃ XUẤT `nro1.sql` ra JSON (CONFIRMED, 2026-10-07, người dùng yêu cầu việc 2):** script `BattleBall\SourceData\tools\export_sql_to_json.py` (Python, chạy lại được: `python tools/export_sql_to_json.py [--version N]`) đọc `SourceData\tables\nro1.sql` và ghi `SourceData\tables\json\<bảng>.json` (khoảng 2,2 MB), mỗi file dạng `{table, version, source, columns, json_columns, row_count, rows[]}`. **Chỉ xuất bảng tĩnh theo danh sách cho phép (18 bảng)**: `part` (2.099 dòng), `array_head_2_frames` (52), `head_avatar` (464), `img_by_name` (146), `skill_template` (27), `item_template` (2.000), `item_option_template` (251), `map_template` (166), `mob_template` (119), `npc_template` (93), `bg_item_template` (597), `intrinsic` (208), `flag_bag` (156), `radar` (21), `shop` (32), `tab_shop` (58), `item_shop` (826), `item_shop_option` (1.420). **Cố ý bỏ** các bảng dữ liệu người dùng/web: `account`, `player`, `payments`, `bank_transfers`, `napthe`, `giftcode`, `history_transaction`, `adminpanel`, `comments`, `posts`, `phongchat`, `clan`, `super_rank`... Bản thân `nro1.sql` vẫn nằm nguyên trong `tables\` (chứa các bảng đó; nên xem xét không đưa vào bộ phát hành). Số dòng khớp với đếm trực tiếp trong SQL cho `part` (2.099) và `skill_template` (27); các bảng khác chỉ kiểm tra độ rộng hàng khớp số cột.
  - **Sửa lỗi dữ liệu khi xuất (CONFIRMED):** các cột JSON của NRO có định dạng hỏng nên script sửa rồi kiểm bằng `json.loads`: (a) `skill_template.skills` là mảng chuỗi chứa JSON lồng với dấu `"` không thoát (`["{"a":1}","{..}"]`) → mảng đối tượng thật; (b) `map_template.waypoints/mobs/npcs` cùng kiểu mảng-chuỗi-mảng → mảng lồng thật; (c) `part.DATA` và `array_head_2_frames.data` có dấu phẩy thừa trước `]` (ví dụ `[1705,1706,1707,]`) → bỏ dấu phẩy. Sau sửa: 0 ô JSON không phân tích được ở cả 18 bảng. Giá trị gốc không đổi ngoài các sửa cú pháp này.
  - **Đối chiếu `part.json` với `PartDatabase.json` (CONFIRMED, 2026-10-07, bằng script so từng phần tử):** `PartDatabase.json` (984 part) là **tập con khớp 100%** của bảng `part` (2.099 dòng): 984/984 part trùng `TYPE` và danh sách `[iconId, dx, dy]` theo `subIndex` (0 lệch); không có part nào chỉ nằm ở `PartDatabase`. Còn **1.115 part chỉ có trong SQL** (type 0: 504, type 1: 307, type 2: 304; id tới 2098, `PartDatabase` tới 1868). Đối chiếu thêm: 984 part này chính xác là tập part mà 414 costume trong `CostumeCatalog.json` dùng (`headPartId`, `bodyPartId`, `legPartId`, `headCyclePartIds`; 0 thừa, 0 thiếu). Kết luận: `PartDatabase.json` = `part` lọc theo 414 costume; 1.115 part còn lại thuộc costume/skin chưa nhập vào BattleBall (INFERRED: chưa kiểm xem chúng có ứng với costume nào khác trong `item_template` hay không).
  - **Chưa làm:** nạp JSON vào bảng tĩnh MariaDB; client đọc JSON bảng SQL thay cho `PartDatabase.json`.
- **PROPOSED (bố cục):** bản sao chép đặt trong BattleBall **ngoài `Assets\`**, ví dụ `E:\PRJNRO\BattleBall\SourceData\` (`data\` = `Teamobi2026\SRC\data`, `ui_audio\` = `Desktop\data`, `tables\` = dữ liệu xuất từ `nro1.sql`), để Unity **không nhập (import) ~160.000 file png** (làm editor rất chậm và phình `Library`); client chỉ nạp lúc chạy từ bộ đã tải. Do asset tải từ server nên **server là nơi lưu bản gốc phát hành** (`BattleBallServer\` hoặc thư mục tĩnh trên VPS), `SourceData` là bản nguồn để đóng gói.
- **PROPOSED (định dạng bảng):** từ `nro1.sql` xuất **JSON có version** (một file mỗi bảng: `part`, `array_head_2_frames`, `head_avatar`, `img_by_name`, `skill_template`...) làm định dạng trung gian: (a) nạp vào **bảng tĩnh MariaDB** cho server và trang admin đã chốt, (b) đóng gói trong bộ tải về cho client. Không dùng `.proto` cho dữ liệu tĩnh lớn (`.proto` chỉ cho message mạng). Cần sửa `skills` trong `skill_template` (JSON lồng chuỗi, dấu `"` không hợp lệ) khi xuất: chuyển thành mảng JSON thật.
- **PROPOSED (phân phối):** server giữ manifest `{version, scale, danh sách gói, băm}`; client gửi thông tin máy (độ phân giải, RAM, DPI) hoặc tự chọn thang rồi tải gói của thang đó qua **HTTPS** (khuyến nghị tách khỏi kênh game TCP; Teamobi2026 dùng chính TCP: CONFIRMED trong `docs\networking\Teamobi2026.md`), lưu cache trên máy, kiểm băm, chỉ tải lại khi version đổi. Cho phép đổi thang sau (OPEN).
- **Thang độ phân giải (CONFIRMED về nguyên tắc, người dùng chốt 2026-10-07):** chọn x1 đến x4 **tùy độ tương thích của thiết bị, áp dụng ở lần tải/nạp game đầu tiên** (client chọn thang phù hợp khi tải lần đầu hoặc khi nạp game). Còn mở (OPEN): tiêu chí chọn thang (độ phân giải/RAM/DPI của máy), có tải asset từ server (cần phân phối asset qua mạng, hiện `DataGame` đang DROP) hay đóng gói sẵn trong APK, và có cho đổi thang sau đó không.


---

## 10. Thiết kế bảng skill (PROPOSED, 2026-10-07)

> **CẬP NHẬT 2026-10-07 (CONFIRMED, người dùng quyết định): BattleBall dùng bộ skill của NRO và bỏ cơ chế skill cũ của BattleBall (13 skill trong GDD mục 3 và 4).** Hệ quả: các mục **10.2 đến 10.5 bên dưới (lược đồ và seed dựa trên GDD) là LỖI THỜI / SUPERSEDED**, giữ lại để truy vết; thiết kế hiện hành nằm ở **mục 10.6**. File seed GDD (16 skill, 88 cấp) đã bị thay bằng seed NRO. GDD (`docs\gameplay\game-design-doc.md`, file của người dùng) **chưa được sửa**: các mục 3, 4 và các câu liên quan skill (Kaioken, Khiên, Max_ATK_Total tính "skill cấp 7", hợp thể "mất skill") nay không còn khớp, cần người dùng quyết định cập nhật (OPEN, xem 10.6).

> Người dùng nói "bắt đầu" khi được hỏi 3 câu (dùng số NRO hay bảng trống; cách nâng cấp skill; số liệu hộp va chạm), nên đây là **bản đề xuất theo mặc định**, **chưa phải quyết định**. Nguồn: cấu trúc `skill_template` (CONFIRMED, đã xuất ra `SourceData\tables\json\skill_template.json`) và các bảng skill trong GDD (`docs\gameplay\game-design-doc.md` mục 3 và 4).

### 10.1 Quan sát về `skill_template` của NRO (CONFIRMED, đọc từ JSON đã xuất)
- 27 dòng (skill), mỗi dòng có: `nclass_id` (lớp nhân vật), `id`, `NAME`, `max_point` (mặc định 7), `mana_use_type`, `TYPE`, `icon_id`, `dam_info` (mẫu chuỗi mô tả), `slot`, `skills` (mảng các cấp).
- Mỗi cấp trong `skills` có: `id` (id toàn cục của cấp), `point` (số cấp 1..7), `power_require`, `damage` (100 = 100%), `dx`/`dy` (tầm theo pixel NRO), `price`, `max_fight`, `mana_use`, `cool_down` (mili giây), `info`.
- UNKNOWN: ý nghĩa của `mana_use_type`, `TYPE`, `max_fight`, `slot` (chưa đọc code dùng chúng); `damage` của skill buff/CC (ví dụ "Thái Dương Hạ San" có `damage` 3000 nhưng `dam_info` là "thời gian tác dụng: # mili giây", INFERRED: cột này mang nghĩa khác nhau tùy loại skill).
- **Khác biệt với BattleBall (CONFIRMED về GDD):** GDD chỉ định rõ giá trị mốc (cấp 1, 3, 4, 5, 7) chứ không phải đủ 7 cấp; sát thương theo % ATK; chi phí Ki theo % Ki tối đa; thêm nhiều hiệu ứng mới (khiên, Kaioken tiêu hao HP/Ki mỗi giây, Tự hủy, Để trứng...). Bảng của NRO không đủ cột cho các thứ này.

### 10.2 Lược đồ đề xuất (2 bảng + hiệu ứng JSON, đều là dữ liệu tĩnh có `version`, nhập qua trang admin đã chốt)

**`SkillTemplate`** (một dòng mỗi skill)

| Cột | Ý nghĩa |
|---|---|
| `skillId` (PK) | id skill |
| `race` | `EARTH` (Trái Đất), `XAYDA`, `NAMEC`, `COMMON` (khiên năng lượng chung) |
| `nameKey` / `name` | tên hiển thị (ví dụ "Kamehame") |
| `kind` | `ATTACK`, `CC`, `BUFF`, `HEAL`, `SUMMON`, `SELF_DESTRUCT`, `SHIELD`, `BASIC` (đòn đấm) |
| `castModes` | tập `DIRECTION` / `TARGET` được phép (đã chốt: mọi skill dùng cả hai chế độ trừ skill tự buff/hồi, tức `SELF`) |
| `hitShape` | `LINE_PIERCE` (xuyên đường thẳng, Kamehame/Atomic), `CIRCLE_AOE`, `SINGLE`, `SELF`, `TEAM` |
| `chargeMs`, `interruptible` | thời gian tụ lực và có bị CC ngắt không (Quả cầu khênh ki: 1,5 đến 2 giây, có thể bị ngắt) |
| `maxLevel` | 7 |
| `iconId` | id icon (có thể dùng `icon_id` của NRO, xem `SourceData\data\icon`) |
| `effectSoundId`, `effectId` | tham chiếu âm thanh/hiệu ứng (xem `SourceData\ui_audio\audio\sound`, `SourceData\data\effect`) |
| `sortOrder` | thứ tự hiển thị / slot |
| `version` | số phiên bản dữ liệu |

**`SkillLevel`** (một dòng mỗi skill mỗi cấp 1..7; khóa chính `(skillId, level)`)

| Cột | Ý nghĩa | Tương ứng NRO |
|---|---|---|
| `damagePctAtk` | sát thương mục tiêu chính, % ATK | `damage` (100 = 100%) |
| `aoeDamagePctAtk` | sát thương diện rộng xung quanh (nếu có) | không có |
| `cooldownMs` | hồi chiêu | `cool_down` (mili giây) |
| `kiCostPct` | tiêu Ki, % Ki tối đa | `mana_use` (cách tính: UNKNOWN) |
| `durationMs` | thời lượng buff/CC/hiệu ứng | trong `damage` (INFERRED) |
| `rangeX`, `rangeY` hoặc `length`, `width`, `radius` | vùng trúng (pixel NRO, `i32`) | `dx`, `dy` |
| `projectileSpeed`, `travelMs` | tốc độ và thời gian bay đạn (mục protocol 14/combat) | không có |
| `slackPx` | khoảng rộng dự phòng chống trễ mạng (đã đề xuất ở protocol) | không có |
| `requiredCharacterLevel` | cấp nhân vật tối thiểu để học/nâng cấp lên cấp này | `power_require` (nghĩa khác) |
| `goldCost` | giá vàng học/nâng cấp | `price` |
| `effects` | mảng JSON hiệu ứng riêng của skill, phần "thưa" | không có |

`effects` (JSON, mỗi phần tử `{type, value, unit}`) chứa các số không phải ai cũng có, để tránh nhiều cột rỗng. Các `type` đề xuất, lấy từ GDD: `STUN_MS`, `ROOT_MS`, `HEAL_HP_PCT`, `HEAL_KI_PCT`, `HEAL_HP_PCT_PER_SEC`, `REVIVE_HP_PCT`, `BUFF_STAT_PCT` (kèm danh sách chỉ số: chí mạng/tốc đánh/di chuyển/HP/Giáp/ATK), `DRAIN_HP_PCT_PER_SEC`, `DRAIN_KI_PCT_PER_SEC`, `DAMAGE_REDUCTION_PCT`, `BURN_SELF_HP_PCT_PER_SEC`, `SUMMON_DAMAGE_PCT_ATK`, `SUMMON_KI_PER_HIT_PCT`, `KI_REFUND_PER_HIT_PCT`, `SELF_DESTRUCT_FORMULA`, `BOSS_CC_RESIST_PCT`. Danh sách này **PROPOSED**, có thể thay đổi khi chốt công thức sát thương.

**Liên quan các bảng khác:** `CharacterSkill(characterId, skillId, level)` (mục 3.3) dùng `SkillLevel(skillId, level)`. `UseSkillRequest{skillId, mode, direction|targetEntityId}` (protocol) tra `SkillTemplate` + `SkillLevel` của cấp hiện tại; cooldown/Ki/hiệu ứng lấy từ đây; hợp thể dùng skill của nhân vật chủ (đã chốt). `CooldownUpdate`, `SkillCast` gửi `travelMs`, `origin`, `direction`.

### 10.3 Ví dụ dòng dữ liệu (số lấy từ GDD; CONFIRMED mốc, PROPOSED nội suy)

Kamehame (Trái Đất, `LINE_PIERCE`, `ATTACK`): GDD cho mốc cấp 1, 3, 5, 7:

| level | damagePctAtk | cooldownMs | kiCostPct | Ghi chú |
|---|---|---|---|---|
| 1 | 500 | 10.000 | 30 | CONFIRMED (GDD) |
| 2 | 560 | 9.250 | 29 | PROPOSED (nội suy tuyến tính) |
| 3 | 620 | 8.500 | 28 | CONFIRMED |
| 4 | 680 | 7.750 | 26,5 | PROPOSED |
| 5 | 740 | 7.000 | 25 | CONFIRMED |
| 6 | 795 | 6.500 | 23,5 | PROPOSED |
| 7 | 850 | 6.000 | 22 | CONFIRMED |

Flash (`CC`): `effects=[{"type":"STUN_MS","value":1500}]` cấp 1 đến 3000 cấp 7, hồi chiêu 25 s đến 16 s, Ki 25% đến 18% (GDD). Trói (Xayda, `CC`): `ROOT_MS` 30.000 ms, boss chịu 25 đến 35% thời gian gốc (kháng CC, `BOSS_CC_RESIST_PCT`), hồi chiêu 45 s, 36 s (cấp 4), 28 s (cấp 7). Khiên năng lượng (`COMMON`, `SHIELD`): 30 s, giảm 60/70/80% sát thương (cấp 1/4/7), thiêu đốt 50/55/60% HP tối đa/giây, hồi chiêu 150/120/100 s, Ki 40/35/30%.

### 10.4 Điểm GDD chưa đủ để điền bảng (UNKNOWN/OPEN)
- GDD chỉ cho **mốc cấp** (1/3/5/7 hoặc 1/4/7); các cấp còn lại cần nội suy hoặc bạn điền (OPEN: nội suy tuyến tính có được không).
- Đòn đấm cơ bản chỉ có % ATK mỗi chủng tộc (Xayda 110%, Namec 100%, Trái Đất 90%): lưu thành skill `BASIC` cấp 1 duy nhất hay trong dữ liệu chủng tộc (OPEN).
- Vùng trúng (`length`, `width`, `radius`, `rangeX/Y`), `travelMs`, `projectileSpeed`, `slackPx`, thời lượng frame đòn thường, tầm đấm: **UNKNOWN**, chờ người dùng (khớp OPEN 7 trong protocol). Có thể bắt đầu từ `dx`/`dy` của NRO (Kamejoko cấp 1: 160x160 px) rồi chỉnh.
- Điều kiện nâng cấp skill (`requiredCharacterLevel`, `goldCost`): UNKNOWN (GDD không nêu), mục 3.3.
- Tên và số lượng skill của Trái Đất/Xayda/Namec trong GDD (4 + 5 + 3 + 1 chung = khoảng 13 skill) ít hơn 27 dòng của NRO; chưa quyết định có giữ thêm skill nào khác của NRO hay không (OPEN).
- Công thức sát thương đầy đủ, thứ tự cộng %, Tự hủy kết hợp giáp/chí mạng: UNKNOWN (giữ nguyên ở mục UNKNOWN phía trên).

### 10.5 Phương án tối ưu đã chọn và file seed (người dùng giao trợ lý chọn, 2026-10-07; PROPOSED, chờ người dùng xác nhận)

Người dùng yêu cầu "đưa ra phương án tối ưu nhất" cho các câu mở ở 10.4. Quy tắc áp dụng (chọn để không bịa số cân bằng và dễ sửa sau):

1. **Cấp trung gian:** nội suy tuyến tính giữa hai mốc GDD gần nhất, làm tròn (thời gian về mili giây nguyên). Mỗi dòng gắn `status`: `GDD` (đủ giá trị lấy nguyên từ GDD), `INTERPOLATED` (có ít nhất một giá trị nội suy), `PLACEHOLDER` (GDD không có bảng). Người dùng sửa số nào thì chuyển `status` về `GDD` (hoặc `MANUAL`) khi nhập qua trang admin.
2. **Số GDD không có (vùng trúng `length/width/radius/rangeX/rangeY`, `projectileSpeed`, `travelMs`, `slackPx`, `requiredCharacterLevel`, `goldCost`, `iconId`, âm thanh/hiệu ứng): để `null`, không điền số giả.** Server phải coi `null` là "chưa cấu hình", không chạy skill đó cho tới khi có số (hoặc dùng mặc định cấu hình rõ ràng).
3. **Phạm vi skill:** chỉ 13 skill trong GDD (4 Trái Đất, 5 Xayda, 3 Namec, 1 khiên chung) cộng **3 đòn đấm cơ bản** làm skill `BASIC` một cấp (Xayda 110%, Namec 100%, Trái Đất 90% ATK). **Không** mang thêm skill nào của NRO (27 dòng) trừ khi người dùng yêu cầu.
4. **Tự hủy:** GDD không có bảng cấp, nên chỉ 1 dòng `PLACEHOLDER`, công thức UNKNOWN (ghi trong `note`).
5. **Số NRO làm tham chiếu, không làm giá trị:** `dx/dy` của `skill_template` chỉ để gợi ý khi chọn hộp va chạm.

**File seed (đã tạo):** `BattleBall\SourceData\tools\build_skill_seed.py` sinh `BattleBall\SourceData\tables\seed\skills_seed.json` (`SkillTemplate`: 16 dòng; `SkillLevel`: 88 dòng gồm 41 `GDD`, 46 `INTERPOLATED`, 1 `PLACEHOLDER`). `skillId` 1 đến 16 là id **mới của BattleBall** (không phải id NRO). Chạy lại bằng `python tools/build_skill_seed.py` sau khi sửa số trong script. Kiểm tra mẫu: Kamehame cấp 1/3/5/7 đúng 500/620/740/850, hồi chiêu 10.000/8.500/7.000/6.000 ms, Ki 30/28/25/22; cấp 2/4/6 nội suy (560/680/795). Chưa nạp vào MariaDB, chưa có code server/client đọc file này.


### 10.6 Bộ skill NRO làm skill của BattleBall (CONFIRMED về quyết định; chi tiết PROPOSED), 2026-10-07

**Quyết định (CONFIRMED, người dùng):** dùng bộ skill của NRO, bỏ cơ chế skill cũ của BattleBall (GDD). Giá trị lấy **nguyên bản (raw) từ NRO**, chưa cân bằng lại.

> **Xem thêm mục 10.7 (cập nhật cùng ngày):** Ki giảm 10 lần, nâng cấp chỉ bằng vàng, tuyệt kỹ tối đa cấp 9, kết quả đọc code cho 8 skill; số liệu seed ở 10.7 (25 skill, **181** dòng cấp) thay cho số 184 dưới đây, và Trị thương **có hồi sinh người chết** (CONFIRMED).

**Nguồn:** `SourceData\tables\json\skill_template.json` (từ `nro1.sql`). Script sinh seed: `BattleBall\SourceData\tools\build_skill_seed.py`; kết quả: `BattleBall\SourceData\tables\seed\skills_seed.json` (**25 skill, 184 dòng cấp**). `skillId` giữ **id của NRO** (không còn id mới 1 đến 16 của bản GDD).

**Ý nghĩa cột NRO đã xác minh trong code Teamobi2026 (CONFIRMED, `SkillService.java`, `Skill.java`):**
- `nclass_id`: 0 Trái Đất, 1 Namec, 2 Xayda (nhóm hằng số trong `Skill.java` và tên skill khớp). Skill id 19 (Khiên năng lượng) có mặt ở cả 3 lớp, giống hệt nhau: lưu một lần với `race=COMMON`.
- `TYPE` (rẽ nhánh trong `useSkill`): 1 = tấn công theo mục tiêu (`useSkillAttack`), 2 = buff lên người chơi khác (`useSkillBuffToPlayer`), 3 = skill tự dùng (`useSkillAlone`), 4 = skill đặc biệt/tuyệt kỹ không theo mục tiêu (`useNewSkillNotFocus`: Super Kamejoko, Liên hoàn chưởng, Ma phong ba).
- `mana_use_type`: 0 = tiêu Ki **tuyệt đối** (`mana_use`), 1 = tiêu **% Ki tối đa**, 2 = **tiêu hết Ki** (`canUseSkillWithMana`, `setMpAffterUseSkill`).
- Kaioken: trước khi dùng kiểm tra HP > 10% HP tối đa (có tiêu HP; `canUseSkillWithMana`).
- `max_fight`: chỉ thấy được gửi cho client (`DataGame`); **ý nghĩa UNKNOWN** (INFERRED: số mục tiêu tối đa).
- Hồi chiêu: server cho dùng lại khi đã qua `cool_down - 50` ms (`canUseSkillWithCooldown`).
- Ý nghĩa cột `damage` phụ thuộc skill (INFERRED từ `dam_info`): 12 skill là % sát thương (100 = 100%); Thái Dương Hạ San = thời gian (ms); Thôi miên = giây; Trị thương = % hồi HP/Ki; Tái tạo năng lượng = % hồi mỗi giây; Huýt sáo = % HP cộng thêm; 8 skill còn lại (Đẻ trứng, Biến hình, Tự phát nổ, Biến Sôcôla, Khiên năng lượng, Dịch chuyển tức thời, Trói, Ma phong ba): **UNKNOWN** (mô tả không có `#`), cần đọc code từng skill nếu muốn dùng đúng.

**Danh sách (25 skill; cấp tối đa 7, riêng 3 tuyệt kỹ type 4 có 10 dòng cấp):**

| Chủng tộc | Skill (id) |
|---|---|
| Trái Đất | Chiêu đấm Dragon (0), Chiêu Kamejoko (1), Thái Dương Hạ San (6), Kaioken (9), Quả cầu kênh khi (10), Dịch chuyển tức thời (20), Thôi miên (22), Super Kamejoko (24, tuyệt kỹ) |
| Namec | Chiêu đấm Demon (2), Chiêu Masenko (3), Trị thương (7), Makankosappo (11), Đẻ trứng (12), Liên hoàn (17), Biến Sôcôla (18), Ma phong ba (26, tuyệt kỹ) |
| Xayda | Chiêu đấm Galick (4), Chiêu Antomic (5), Tái tạo năng lượng (8), Biến hình (13), Tự phát nổ (14), Huýt sáo (21), Trói (23), Cađíc liên hoàn chưởng (25, tuyệt kỹ) |
| Dùng chung | Khiên năng lượng (19) |

**Lược đồ BattleBall (PROPOSED; thay thế 10.2):**
- `SkillTemplate`: `skillId`, `race`, `name`, `nroType` và `kind` (`ATTACK`/`ALLY_BUFF`/`SELF`/`SPECIAL`), `kiCostMode` (`ABSOLUTE`/`PERCENT_OF_MAX`/`ALL`), `damInfo`, `damageMeaning`, `castModes`, `iconId` (id icon NRO), `slot`, `nroMaxPoint`, `maxLevel`, `version`.
- `SkillLevel` (khóa `(skillId, level)`): `nroLevelId`, `damage` (raw), `dx`, `dy`, `kiCost`, `cooldownMs`, `maxFight`, `powerRequire`, `price`, `info`; cộng cột **BattleBall còn null**: `length`, `width`, `radius`, `projectileSpeed`, `travelMs`, `slackPx`; `status = NRO_RAW`.
- `castModes` (PROPOSED theo quyết định chiến đấu đã chốt: mọi skill dùng cả hai chế độ trúng trừ skill tự buff/hồi): type 1 và 4 = `DIRECTION` + `TARGET`; type 2 và 3 = `SELF`.

**Khác biệt cần xử lý (OPEN, chờ người dùng):**
1. **Thang số NRO không khớp thang BattleBall:** `mana_use` tuyệt đối (ví dụ Kamejoko cấp 7 tốn 1.280 Ki, Kaioken cấp 7 tốn 27.000 Ki), `power_require` (tới 60 tỷ), `price` và Ki tối đa của NRO khác thang đã chốt cho BattleBall (Ki cấp 100 = 575k). Cần quy đổi hoặc cân bằng lại; hiện giữ raw.
2. **Cách nâng cấp skill:** NRO dùng `power_require` (sức mạnh) và `price`; BattleBall có cấp nhân vật 1 đến 100 và EXP `100 x L^2`. Chưa quyết định điều kiện học/nâng cấp (OPEN), vì vậy `CharacterSkill` (mục 3.3) vẫn chưa có luật.
3. **Skill 8 loại UNKNOWN** (`damage` không rõ nghĩa) và 3 tuyệt kỹ type 4 cần quy tắc riêng (không theo mục tiêu, có thể là vùng/tia riêng); cần đọc code Teamobi2026 từng skill khi tới lượt.
4. **Tuyệt kỹ 10 dòng cấp nhưng `max_point` = 9:** chưa rõ dòng thứ 10 (INFERRED: dòng thừa hoặc cấp mở rộng); giữ cả 10 dòng.
5. **Các luật GDD gắn với skill cũ cần quyết định lại (GDD chưa sửa):** hợp thể "nhân vật phụ mất skill, chủ giữ" (vẫn dùng được: skill của nhân vật chủ); `Max_ATK_Total` tính "skill cấp 7"; cân bằng Kaioken/Khiên/Biến hình; "Chữa trị/hồi sinh" của Namec (bảng NRO chỉ có Trị thương, **không có skill hồi sinh riêng**; chưa kiểm tra code xem Trị thương có hồi sinh người chết không: UNKNOWN; hồi sinh bằng vàng đã chốt, tối đa 2 lần mỗi trận, vẫn dùng được).
6. Đòn đấm cơ bản NRO là skill id 0, 2, 4 (slot 0), `damage` 100/95/110% cấp 1 (khác 90/100/110% của GDD).
7. Hộp va chạm: NRO chỉ có `dx`/`dy`; hình dạng vùng, `travelMs`... vẫn UNKNOWN (OPEN 7 trong `docs\protocol\BattleBall.md`).


### 10.7 Quyết định bổ sung về skill (CONFIRMED, người dùng, 2026-10-07) và kết quả đọc code

**Quyết định của người dùng (CONFIRMED):**
1. **Chi phí Ki của tất cả skill giảm 10 lần** so với NRO. Seed lưu `kiCost` (đã chia 10, làm tròn 1 chữ số thập phân) và `kiCostNro` (gốc). Chế độ `ALL` (Makankosappo, tiêu hết Ki) giữ nguyên. Áp dụng cho cả chế độ `ABSOLUTE` (số Ki) và `PERCENT_OF_MAX` (% Ki tối đa). Hệ quả cần biết: Ki tuyệt đối của một số skill vẫn lớn (Kaioken cấp 7: 2.700 Ki; Dịch chuyển tức thời cấp 7: 3.000; Trói cấp 7: 3.200) so với Ki cấp 100 đã chốt (575k) mà vẫn chưa quy đổi (OPEN).
2. **Nâng cấp skill chỉ dùng vàng.** Bỏ hẳn điều kiện `power_require` (sức mạnh) của NRO khỏi seed. `goldCost` = giá `price` của NRO. NRO đặt `price = 9999` làm giá giữ chỗ ở 130 trong 181 dòng cấp (INFERRED: các cấp không mua bằng vàng trong NRO), nên các dòng đó có `goldCost = null`, `goldCostStatus = NEEDS_VALUE` (**cần người dùng quy định giá vàng**); 51 dòng có giá NRO (ví dụ Kamejoko: 500, 1.000, 2.000, 4.000, 8.000 cho cấp 1 đến 5).
3. **Tuyệt kỹ (type 4) có cấp tối đa 9.** Bỏ dòng cấp thứ 10 của NRO (3 skill: Super Kamejoko, Ma phong ba, Cađíc liên hoàn chưởng). Seed còn **181 dòng cấp** (trước 184); 22 skill cấp tối đa 7, 3 skill cấp tối đa 9.
4. **GDD đã được sửa** (người dùng yêu cầu): `docs\gameplay\game-design-doc.md` mục 3, 4 (thêm 4.1 nâng cấp bằng vàng) và 5 đã đổi sang bộ skill NRO; mục 2, 7 chỉnh các câu nhắc skill cũ. Bản cũ (13 skill) không còn trong file đó. Bảng skill trong GDD được **sinh từ `skills_seed.json`** (số liệu khớp seed).

**Kết quả đọc code Teamobi2026 để làm rõ 8 skill (CONFIRMED, trích từ `SkillService.java`, `SkillUtil.java`, `NPoint.java`, `MobMe.java`):** với nhiều skill, server **không dùng cột `damage`** mà dùng hàm theo cấp trong `SkillUtil`; do đó seed có thêm cột `params` (giá trị thật theo cấp, tính lại từ công thức) và `paramsSource`:

| Skill (id) | Điều code làm | Cột `damage` của NRO |
|---|---|---|
| Đẻ trứng (12) | triệu hồi `MobMe`; sát thương thú = sát thương của người dùng x {30,40,50,60,70,80,90}% theo cấp; thú tồn tại (cấp + 5) x 20 s; loại quái theo cấp {8,11,32,25,43,49,50} (id mob của NRO, BattleBall chưa có) | **không dùng** (50 đến 80) |
| Biến hình, tức Biến khỉ (13) | thời lượng (cấp + 5) x 10 s; HP cộng thêm (cấp + 3) x 10 %; sát thương cộng thêm (cấp + 3) (đơn vị INFERRED) | **không dùng** (100 không đổi) |
| Tự phát nổ (14) | gồng 2 s, phải giữ tối thiểu 1,5 s; **sát thương nổ = HP hiện tại của người dùng** (cộng 20% hoặc 50% HP tối đa nếu có bộ đồ đặc biệt); bán kính (400 + 30 x cấp) px; boss chỉ chịu một nửa; người dùng chết | **không dùng khi nổ** |
| Biến Sôcôla (18) | biến mục tiêu thành sôcôla cố định 30 s (mob và người) | **không dùng** (15 đến 27) |
| Khiên năng lượng (19) | khiên có thời gian (cấp + 2) x 5 s | **= thời gian khiên, đơn vị giây** (15 đến 45) |
| Dịch chuyển tức thời (20) | dịch chuyển tới mục tiêu, đánh một đòn luôn chí mạng (sát thương = ATK nền +/- 5%), làm choáng (cấp + 1) x 0,5 s | **= thời gian choáng, đơn vị mili giây** (1.000 đến 4.000) |
| Trói (23) | trói (cấp x 5) giây; một số boss bị giới hạn 5 s | **= thời gian trói, đơn vị giây** (5 đến 35) |
| Ma phong ba (26), type 4 | gồng 2 s; nhốt kẻ địch trong 500 px phía trước bằng bình 4 s; người chơi dùng có **1/50 khả năng kiệt sức chết** mỗi lần | chưa thấy dùng (UNKNOWN) |

Các skill khác đã đối chiếu khớp: Thái Dương Hạ San, Thôi miên, Trị thương, Huýt sáo, Tái tạo năng lượng có cột `damage` bằng đúng giá trị của hàm theo cấp (xem seed). **Makankosappo**: sát thương = % Ki tối đa của người dùng (`damage` là phần trăm), tụ lực 3 s, tiêu hết Ki. **Quả cầu kênh khi**: tụ lực 4 s, sát thương = 10% HP của mob/người trong phạm vi (350 + 30 x cấp) px + 10 x ATK nền (cột `damage` không dùng). **Trị thương hồi sinh được người đã chết** (code gọi `hsChar` khi mục tiêu đã chết): cập nhật cho mục 10.6 mục 5 (nay CONFIRMED, không còn UNKNOWN). Super Kamejoko và Cađíc liên hoàn chưởng: gồng 2 s rồi đánh vùng chữ nhật phía trước; cách tính sát thương chưa xác minh (UNKNOWN).

**Lựa chọn hợp lý và tối ưu được áp dụng (PROPOSED):** giữ cả `damage` thô lẫn `params` đã giải nghĩa trong seed; server BattleBall nên dùng `params` (khi có) cho hiệu ứng và dùng `damage` chỉ cho skill có `damageMeaning` là `DAMAGE_PCT` hoặc `DAMAGE_PCT_OF_MAX_KI`. Các skill có `damageMeaning` = `UNUSED_FORMULA_IN_CODE` hoặc `UNKNOWN` không dùng `damage`.

**Seed hiện tại:** `BattleBall\SourceData\tables\seed\skills_seed.json` = **25 skill, 181 dòng cấp**, trong đó 132 dòng có `params`. Chạy lại bằng `python tools/build_skill_seed.py` (tại `BattleBall\SourceData`).

**Còn mở (OPEN):** giá vàng cho 130 dòng cấp; quy đổi Ki tuyệt đối; cách tính sát thương Super Kamejoko/Cađíc liên hoàn chưởng/Ma phong ba; hộp va chạm và `travelMs`; các luật GDD khác nay nhắc tới skill (hợp thể "mất một phần skill hỗ trợ": cần chọn skill nào là "hỗ trợ"; hiện chọn tạm không có).


### 10.8 Năm điểm mở đã được trợ lý tối ưu theo yêu cầu "tự tối ưu lại" (PROPOSED, 2026-10-07)

Người dùng giao trợ lý tự chọn phương án tối ưu cho 5 điểm còn mở ở cuối 10.7 và yêu cầu chỉnh GDD cho khớp. Các quy tắc dưới đây được cài trong `BattleBall\SourceData\tools\build_skill_seed.py` (hằng số ở đầu phần quy tắc: `KI_PEAK_PCT`, `GOLD_BASE_BY_SLOT`, `HIT_RULES`, `PROJECTILE_SPEED`...) và có thể chỉnh rồi chạy lại. **Tất cả là PROPOSED (số do trợ lý chọn); người dùng chưa xác nhận từng con số.**

1. **Giá vàng cho 130 dòng cấp** (`goldCostStatus = PROPOSED_RULE`): nếu skill đã có giá NRO ở một số cấp đầu, tiếp tục **nhân đôi mỗi cấp** từ giá NRO cuối (khớp quy luật 500, 1.000, 2.000, 4.000, 8.000 của NRO; ví dụ Kamejoko cấp 6 = 16.000, cấp 7 = 32.000). Skill không có giá NRO nào: giá cấp 1 theo nhóm slot NRO (slot 3: 1.000; slot 4: 2.000; slot 5 và 6: 1.500; slot 7: 1.000) rồi nhân đôi mỗi cấp; tuyệt kỹ (slot 8): 5.000 ở cấp 1, nhân 1,5 mỗi cấp, làm tròn 100 (cấp 9: 128.100). 51 dòng giữ giá NRO (`goldCostStatus = NRO`). Nền kinh tế vàng tổng thể của BattleBall chưa được chốt, nên các con số này là **tương đối**.
2. **Quy đổi Ki tuyệt đối:** các skill NRO tính Ki tuyệt đối (12 skill: 3 đòn đấm, 3 chưởng, Kaioken, Liên hoàn, Dịch chuyển tức thời, Huýt sáo, Thôi miên, Trói) được đổi sang **% Ki tối đa**: `kiCostPct(cấp) = đỉnh% x kiNro(cấp) / kiNro lớn nhất`, với đỉnh% theo vai trò: đòn đấm 1%, chưởng 5%, Kaioken 5%, Liên hoàn 3%, Dịch chuyển 8%, Huýt sáo 10%, Thôi miên 6%, Trói 8%. Skill NRO vốn tính % Ki: lấy % của NRO chia 10 (quy tắc của người dùng). Makankosappo tiêu hết Ki (`ALL`). Mọi skill giờ dùng `kiCostPct` (không còn Ki tuyệt đối); `kiCostNro` giữ số gốc.
3. **Sát thương 3 tuyệt kỹ:** Super Kamejoko và Cađíc liên hoàn chưởng dùng cột `damage` làm **% ATK** (550% ở cấp 1 đến 950% ở cấp 9) lên mọi kẻ địch trong vùng chữ nhật phía trước sau khi gồng 2 s. (Đã xác minh: trong code NRO các skill này không có nhánh `percentDameSkill` trong `getDameAttack`, tức đòn thực tế chỉ dựa ATK nền; BattleBall chọn dùng `damage` làm % ATK để tuyệt kỹ thật sự mạnh hơn skill thường: đây là **thay đổi có chủ ý so với NRO**.) **Ma phong ba:** chỉ khống chế (nhốt 4 s mọi kẻ địch trong 500 px phía trước), không gây sát thương, **bỏ khả năng 1/50 làm người dùng chết** của NRO (người chơi đơn, hồi sinh có giới hạn).
4. **Hợp thể "mất một phần skill hỗ trợ": không cần chọn skill hỗ trợ.** Điểm này **đã được người dùng chốt** từ trước (mục 3.4): nhân vật chủ giữ toàn bộ skill, nhân vật phụ mất toàn bộ skill; GDD cũ ghi "mất một phần skill hỗ trợ" chỉ là đề xuất chưa xác nhận và đã bị thay thế. GDD đã được sửa cho khớp.
5. **Hộp va chạm và tốc độ đạn** (seed, cột `length/width/radius/projectileSpeed/travelMs/slackPx`):
   - Đòn đấm, Kaioken, Liên hoàn: `RECT_FRONT`, `length = dx`, `width = dy` của NRO, `travelMs = 0` (đánh tức thời), `slackPx = 12`.
   - Chưởng (Kamejoko, Masenko, Antomic): `RECT_FRONT`, `length = 300` (hằng `RANGE_ATTACK_CHIEU_CHUONG` của NRO, đã xác minh trong `Skill.java`), `width = dy`, `projectileSpeed = 480` px/s, `travelMs` = 625, `slackPx = 16`.
   - Makankosappo: tia laser `RECT_FRONT`, `length = 2.000` (NRO `dx = 20000` nghĩa là không giới hạn), `width = 48` (số của trợ lý), hiệu lực ngay sau khi tụ lực 3 s.
   - Quả cầu kênh khi, Thái Dương Hạ San, Tự phát nổ, Huýt sáo: `CIRCLE` với bán kính theo công thức trong code (`rangePx`) hoặc `dx`; Quả cầu có `projectileSpeed = 480` (thời gian bay tính lúc chạy theo khoảng cách).
   - Dịch chuyển tức thời, Thôi miên, Trói, Biến Sôcôla, Trị thương: `TARGET` (đơn mục tiêu trong `radius = dx`).
   - Super Kamejoko, Cađíc liên hoàn chưởng: `RECT_FRONT`, `length = dx`, `width = dy` (NRO), hiệu lực ngay sau gồng 2 s. Ma phong ba: `HALF_CIRCLE_FRONT`, bán kính 500.
   - Đẻ trứng, Biến hình, Tái tạo năng lượng, Khiên: không có hộp trúng (`NONE`).
   - `projectileSpeed = 480` px/s là số **của trợ lý** (đạn NRO trên client tối đa 8 px mỗi lần cập nhật, tốc độ thực phụ thuộc số lần cập nhật mỗi giây của client NRO: chưa xác minh); `slackPx` 12 hoặc 16 là số của trợ lý (nửa tile NRO 24 px đến hai phần ba tile). Cần chỉnh khi thử cảm giác chơi.

**GDD (`docs\gameplay\game-design-doc.md`) đã được chỉnh cho khớp phương án hiện tại (người dùng yêu cầu):** đầu file thêm ghi chú cập nhật; mục 1 thêm các điều đã chốt (chơi đơn cần mạng, 3 nhân vật, cấp 100, hai chế độ trúng, bản đồ không trọng lực); mục 2 viết lại luật hợp thể (cộng chỉ số, chủ giữ skill, phụ mất hết skill, Bông tai Potara, cấp 50, đến hết trận, không giới hạn thời gian/tiêu hao Ki); mục 3 và 4 sinh từ seed (có cột vàng mỗi cấp); mục 4.1 nâng cấp bằng vàng; mục 5 Kaioken/Tự phát nổ theo NRO; mục 6 vàng dùng thêm cho nâng cấp skill và hồi sinh; mục 7 `Max_ATK_Total` theo skill cấp tối đa; mục 8 kill streak chỉ tăng EXP. Nội dung GDD **vẫn là tài liệu thiết kế của người dùng**, mọi con số PROPOSED trong đó có thể được người dùng ghi đè.

**Còn mở (OPEN):** nền kinh tế vàng tổng thể (quy đổi với giá món/mảnh/Potara); thang HP/ATK so với `%` sát thương của NRO (Max_ATK_Total chưa tính lại theo skill mới); chọn skill của AI đồng đội (`aiTactic`) theo bộ skill NRO; các luật tăng EXP/chỉ số hợp thể đã người dùng ghi "đang chỉnh lại" (mục 3.4).


### 10.9 Kinh tế vàng, Max_ATK_Total, AI dùng skill và cân bằng hợp thể (2026-10-07)

> **Cập nhật cùng ngày (mục 10.10):** các số HP mob/boss ở 10.9 chưa tính giáp mục tiêu và đã được thay bằng bảng ở 10.10; `U` nay chốt 75% (khớp `Max_ATK_Total` = 500.000).

Người dùng giao ba việc: tối ưu nền kinh tế vàng và `Max_ATK_Total` theo skill mới ("tối ưu lại"), AI dùng skill theo quy tắc riêng, và tối ưu cân bằng luật chỉ số hợp thể. Mô hình số nằm trong `BattleBall\SourceData\tools\balance_model.py` (hằng số ở đầu file, chạy lại để ra bảng mới). **Mọi con số kinh tế và HP là PROPOSED** (không có dữ liệu nhịp chơi thật; dùng làm điểm khởi đầu). Quy tắc AI do người dùng chốt (CONFIRMED về nguyên tắc), chi tiết điều kiện là PROPOSED.

**1. AI đồng đội dùng skill (CONFIRMED về nguyên tắc: "hồi chiêu nào thì dùng chiêu đấy theo thứ tự").**
- Server điều khiển AI. Mỗi lượt quyết định, AI duyệt danh sách skill **theo thứ tự cố định** và dùng **skill đầu tiên đã hết hồi chiêu, đủ Ki và thỏa điều kiện**. Thứ tự (PROPOSED): `slot` của NRO giảm dần (8 tuyệt kỹ, 7 khiên, 6, 5, 4, 3, 2, 1 chưởng, 0 đòn đấm), nên đòn đấm là phương án dự phòng.
- Điều kiện (PROPOSED): skill tấn công/khống chế cần mục tiêu trong tầm (ưu tiên mục tiêu người chơi đang chọn, sau đó mục tiêu gần nhất); Trị thương khi có đồng đội dưới 50% HP hoặc đã chết; buff (Biến hình, Huýt sáo, Tái tạo năng lượng, Khiên) chỉ khi chưa bật; Kaioken chỉ khi HP trên 50%; Đẻ trứng khi chưa có thú; **AI không dùng Tự phát nổ** (tự sát).
- Hệ quả: **bỏ trường `aiTactic`** trong `SetSquadRequest` (`docs\protocol\BattleBall.md`); không còn "chiến thuật DPS/CC/hỗ trợ" của GDD cũ.

**2. Kinh tế vàng (PROPOSED).** Mốc là `Gm(R)` = vàng nhận từ một trận **thắng** ở cấp khuyến nghị `R` = **1.000 x R** (gồm vàng rơi mob và boss; đây là mục tiêu để thiết kế bảng rơi); trận thua ≈ một nửa.
- Thu nhập cấp 1 đến 100 (3 trận thắng mỗi cấp, đúng `M = 3` của EXP): ≈ **14,85 triệu vàng**.
- Nâng cấp mọi skill của cả 3 nhân vật lên tối đa (theo `skills_seed.json`): **4.189.180 vàng** ≈ 28% thu nhập; nếu dồn hết vàng cho skill thì đủ ở khoảng cấp 53, thực tế còn trang bị/đá nên kéo dài đến khoảng cấp 80 đến 90.
- **Bông tai Potara:** 100 mảnh x **15.000** = 1.500.000 vàng (≈ 30 trận thắng ở `R` = 50), mua tối đa **20 mảnh mỗi ngày** (mốc 00:00 giờ Việt Nam đã chốt), tối thiểu 5 ngày; có thể nhận thêm từ boss một phó bản. Đây là các con số còn UNKNOWN trước đó (giá và giới hạn mỗi ngày).
- **Mảnh đồ:** một món = 10 mảnh (số mảnh trước đó UNKNOWN); mua trọn món ≈ 6 trận thắng ở tier đó (giá mảnh ≈ 600 x `R`). `R` = 20: mảnh 12.000, món 120.000; `R` = 50: 30.000 và 300.000; `R` = 80: 48.000 và 480.000.
- **Giá bán đồ:** 5% giá mua cả món (thấp, như đã chốt); `R` = 50: 15.000.
- **Đá nâng cấp** ≈ 1,5 trận thắng, **đá chỉ số** ≈ 1, **item dùng một lần có thời hạn** ≈ 0,1; **hồi sinh** lần 1 ≈ 0,5, lần 2 ≈ 1 trận thắng (`R` = 50: 75.000, 50.000, 5.000, 25.000 và 50.000).
- Đá quý (đổi skin, trang trí, đá pha lê): nguồn nhận chưa quyết định (OPEN).

**3. `Max_ATK_Total` và HP mob/boss (PROPOSED).**
- **Giữ `Max_ATK_Total` = 500.000.** Kiểm tra: ATK mỗi nhân vật = (26.000 + 20.000 x (1 + U)) x (1 + 0,25 + 0,5 + 1,0) với U là % nâng cấp tối đa (chưa chốt): U = 100% cho 544.500 (cả đội), U = 50% cho 462.000; 500.000 nằm giữa. **Bộ skill NRO không có buff cộng ATK** (Kaioken là skill sát thương 220%, Biến hình chỉ cộng HP), nên `Max_ATK_Total` không cộng buff kỹ năng như định nghĩa cũ của GDD.
- **Phát hiện quan trọng:** bảng HP cũ của GDD (ví dụ boss vùng sớm 150% đến 250% của `Max_ATK_Total`, tức khoảng 1 triệu) sẽ quá thấp với skill NRO, vì đòn đấm của NRO đánh khoảng 2 lần mỗi giây với 155% đến 170% ATK (hồi chiêu 0,4 đến 0,5 s; đo từ seed: 3,2 / 3,88 / 3,4 ATK mỗi giây). Với đội đúng cấp, boss sớm sẽ chết trong vài giây.
- **Mô hình:** `Team_DPS_Max` = `Max_ATK_Total` x 3,5 (đòn đấm) x 0,5 (thời gian đánh trúng) x 1,1 (chí mạng) x 0,9 (hiệu suất AI) = **866.250/giây**. `HP = thời gian hạ x tỷ lệ ATK dự kiến theo vùng x Team_DPS_Max`, tỷ lệ ATK dự kiến: sớm 20%, giữa 45%, cuối 85%, endgame 100%.
- **Kết quả:** mob thường 0,4 s (HP 69.300 / 155.925 / 294.525 theo vùng sớm/giữa/cuối); mob elite 2 s (346.500 / 779.625 / 1.472.625); boss vùng sớm 45 s = **7.796.250**; giữa 75 s = **29.235.938**; cuối 120 s = **88.357.500**; mini-boss phó bản 40 s = 15.592.500 (vùng giữa); boss cuối phó bản 150 s = 110.446.875 (vùng cuối); raid 360 s = **311.850.000** (endgame). ATK và Giáp của mob/boss giữ nguyên bảng cũ của GDD (chưa kiểm chứng giáp vì công thức sát thương/giáp UNKNOWN).
- Hệ quả kỹ thuật: HP cỡ trăm triệu nên server và client cần số nguyên 64 bit cho HP và sát thương (khớp quyết định "kiểu đủ rộng, không ép 32-bit").
- Giả định chưa kiểm chứng: 50% thời gian đánh trúng, tốc độ đánh thực của đòn thường (thời lượng frame đòn thường vẫn UNKNOWN), sát thương skill đặc biệt coi là phần dự phòng.

**4. Cân bằng hợp thể (PROPOSED, thay cho "+1% mỗi cấp" và "nhân vật phụ tự tăng cấp").** **Không bonus, không tự nâng cấp.** Cùng trang bị (A = ATK, H = HP mỗi nhân vật): đội 3 nhân vật có tổng ATK 3A, tổng HP 3H trên 3 thân, 3 người tung skill; đội hợp thể (hợp thể + 1 AI) có tổng ATK 2A + A = 3A, tổng HP 3H trên 2 thân, và chỉ 2 người tung skill (nhân vật phụ mất hết skill). Hai bên ngang nhau về tổng chỉ số; hợp thể đổi sự linh hoạt lấy độ trâu tập trung. Rủi ro còn lại (nhân vật phụ cấp cao làm hợp thể quá mạnh) đã bị chặn bởi Bông tai Potara (1 cái mỗi tài khoản, 100 mảnh, 1,5 triệu vàng), điều kiện cả hai cấp 50 và cặp cố định đến hết trận.

**Còn mở (OPEN):** hiệu chỉnh bằng dữ liệu chơi thật (thời gian hạ boss, vàng mỗi trận, tỷ lệ ATK dự kiến); `U` (% nâng cấp tối đa); nguồn đá quý; công thức giáp/sát thương; điều kiện AI chi tiết có phù hợp cảm giác chơi không.


### 10.10 Giải quyết bốn điểm mở của 10.9: U, đá quý, công thức giáp/sát thương, hiệu chỉnh bằng dữ liệu thật (PROPOSED, 2026-10-07)

Người dùng nói "bắt đầu" cho bốn điểm còn mở cuối 10.9. Chưa có dữ liệu chơi thật (BattleBall chưa có gameplay), nên ba điểm đầu được chốt bằng **đề xuất của trợ lý**, điểm thứ tư là **quy trình hiệu chỉnh** để làm khi có dữ liệu. Tất cả PROPOSED, đã ghi vào GDD (mục 6, 7). Số liệu do `BattleBall\SourceData\tools\balance_model.py` tính (đã thêm `UPGRADE_MAX_PCT`, `ARMOR_K`, `TARGET_ARMOR`).

1. **U (đá nâng cấp tối đa): +75% chỉ số cơ bản của món, 15 bậc, mỗi bậc +5%.** Với U = 75%, ATK mỗi nhân vật full đồ = (26.000 + 20.000 x 1,75) x 2,75 = 167.750; cả đội 503.250, **khớp `Max_ATK_Total` = 500.000** (nên U = 75% thay cho khoảng 462.000 đến 544.500 ở 10.9).
2. **Nguồn đá quý:** không mua bằng vàng, không rơi từ mob. Nhận từ: phó bản giới hạn thời gian (10 mỗi lần, tối đa 5 lần/ngày), lần đầu hạ boss cuối phó bản (20), raid boss (100 lần đầu, sau đó 10 mỗi lần, tối đa 3 lần/ngày). Dùng để đổi skin (100 / 300 / 800 tùy nhóm costume), item trang trí vĩnh viễn (150), đá pha lê (500). Với 30 đá quý mỗi ngày: skin 300 mất khoảng 10 ngày, đá pha lê khoảng 17 ngày. Giá skin theo nhóm costume (Standard, Fusion, Broly, GreatApe, Chibi, Component) chưa gán chi tiết cho 414 costume: OPEN.
3. **Công thức giáp và sát thương:** `giảm_thương = Giáp / (Giáp + 50.000)` (cấp 1 đồ gốc 21,3%; cấp 100 đồ gốc 32,9%; cấp 100 full đồ 57,4%); Áo cộng thêm "% giảm thương" riêng, tổng kẹp 80%. Một đòn: `ATK x %skill` (hoặc % Ki tối đa / HP hiện tại cho skill đặc biệt) → x `(1 + sát thương chí mạng)` nếu chí mạng → x `(1 − giảm_thương_mục_tiêu)` → trừ khiên → tối thiểu 1, dao động ±5%. Tự phát nổ bỏ qua giáp (boss chịu một nửa). **Hệ quả đã sửa:** bảng HP ở 10.9 chưa tính giáp của mục tiêu; công thức đúng là `HP = thời gian hạ x tỷ lệ ATK x Team_DPS_Max x (1 − giảm thương mục tiêu)`; số mới: mob thường 64.103 / 144.231 / 272.436 (sớm/giữa/cuối); mob elite 285.863 / 643.191 / 1.214.916; boss vùng sớm **6.042.094**, giữa **19.295.719**, cuối **49.480.200**; mini-boss phó bản 10.524.938; boss cuối phó bản 56.880.141; raid **124.740.000**. **Các số HP ở mục 10.9 là bản cũ, dùng bảng này.** Kiểm tra: Kamejoko cấp 7 (450%) với ATK 46.000 lên boss vùng giữa gây 136.620; boss vùng cuối gây khoảng 192.950 mỗi đòn lên nhân vật cấp 100 đồ gốc (chưa tính khoảng cách giữa các đòn của boss, UNKNOWN).
4. **Hiệu chỉnh bằng dữ liệu chơi thật (quy trình PROPOSED; chỉ làm được sau khi có gameplay):**
   - **Đo (ghi vào `RunProgress` đã đề xuất ở mục 3.9, mỗi mốc và mỗi boss):** `bossId`, vùng/tier, thời điểm bắt đầu gây sát thương và lúc chết (thời gian hạ), tổng ATK và HP đội lúc vào trận, sát thương đã gây theo skill, tỷ lệ thời gian có gây sát thương (`uptime`), vàng nhận, số lần chết/hồi sinh.
   - **Chỉnh:** `HP_mới = HP_cũ x (thời gian hạ mục tiêu / thời gian hạ đo được, trung vị)`, mỗi lần tối đa ±30% rồi đo lại; `Gm` mới = `Gm` cũ x (vàng mục tiêu / vàng đo được mỗi trận thắng); `uptime` và hiệu suất AI lấy từ số liệu đo thay cho 0,5 và 0,9.
   - **Chấp nhận:** thời gian hạ trong ±20% mục tiêu; ≈ 3 trận thắng mỗi cấp (khớp `M = 3`); người chơi nâng hết skill vào khoảng cấp 80 đến 90.
   - **Công cụ:** bảng nhập dữ liệu qua trang admin đã chốt (đổi `BalanceConfig` có `version`), xem số liệu qua `AnomalyLog`/`RunProgress`. Chưa có dữ liệu nào, nên bước này **UNKNOWN cho tới khi có bản chơi thử**.

**Còn mở (OPEN):** gán giá skin cho 414 costume; khoảng cách giữa các đòn của boss (nhịp tấn công); công thức hồi Ki; kiểm chứng giáp và nhịp tấn công bằng dữ liệu chơi thật.


### 10.11 Hồi Ki và nhịp tấn công boss (2026-10-07)

**Hồi Ki (CONFIRMED về hướng: người dùng chốt "hồi nhiều Ki mỗi giây"; con số PROPOSED):** hồi **5% Ki tối đa mỗi giây**, cố định theo cấp, không phụ thuộc HP (kết hợp với hồi máu 0,5% HP tối đa mỗi giây chỉ chạy khi Ki lớn hơn 0 đã chốt ở mục 4.2). Kiểm tra với seed: các skill thường tốn 1% đến 10% Ki mỗi lần và hồi chiêu vài giây, nên một nhịp xoay skill gần như duy trì được; chỉ các chuỗi tốn Ki dày (Masenko cấp 7 khoảng 5% mỗi 0,7 s ≈ 7%/giây, Liên hoàn, Dịch chuyển, Thôi miên, Trói) làm hết Ki sau vài chục giây, khớp ý GDD "hết Ki phải dùng vật phẩm hỗ trợ". Kaioken bị giới hạn bởi HP (10% HP tối đa mỗi lần) hơn là bởi Ki. Skill Tái tạo năng lượng của Xayda hồi thêm (mục 10.6). Hằng số `KI_REGEN_PCT_PER_SEC = 5.0` trong `balance_model.py`; ghi vào `BalanceConfig`. Đã cập nhật GDD mục 1.

**Nhịp tấn công boss (CONFIRMED về bộ chiêu và quy tắc do người dùng mô tả; số liệu PROPOSED):**
- Bộ chiêu (người dùng): đòn đánh thường, choáng của Trái Đất (diện rộng), khiên, chưởng hoặc kỹ năng tối thượng; các skill **tự focus / không thể né** nhắm vào **đơn vị có nhiều máu nhất**; các skill còn lại tấn công **trong phạm vi/vùng** (né được); ra khỏi biên vùng thì boss **ngừng đuổi, về vị trí ban đầu và hồi máu**.
- Chọn chiêu: duyệt theo ưu tiên (tối thượng, khiên, choáng, focus, chưởng, đòn thường) và dùng chiêu đầu tiên đã hết hồi chiêu (cùng nguyên tắc AI đồng đội, mục 10.9). Hồi chiêu và hệ số (x ATK boss): tối thượng 60 s x3,0 (gồng 2 s báo trước); khiên 45 s (khoảng 10 s); choáng 25 s x0,5 + choáng 2 s; focus 15 s x1,0 (không né được); chưởng 6 s x1,5; đòn thường x1,0.
- Khoảng cách giữa các nhịp, tìm bằng mô phỏng (`balance_model.py`, phần BOSS RHYTHM) sao cho nếu cả đội không né, không hồi, không khiên thì boss gây **2 lần tổng HP đội** trong thời gian hạ mục tiêu: sớm ≈ 1,3 s, giữa ≈ 1,8 s, cuối ≈ 2,2 s, raid ≈ 5 s. Phần sát thương không thể né chiếm 8% đến 23%. Giả định: HP đội theo cấp 30/60/90/100 cộng đồ và % chỉ số (xem script), giáp người chơi 25% đến 50%.
- Vùng và quay về (PROPOSED): đuổi thêm khoảng 200 px sau biên rồi quay về, hồi 5% HP tối đa mỗi giây, quay về không bị khóa mục tiêu; pha enrage dưới 30% HP rút ngắn nhịp khoảng 30%. Đã ghi GDD mục 7.5.
- Hệ quả giao thức (PROPOSED): skill focus của boss là skill `TARGET` do server chọn mục tiêu (đơn vị có HP hiện tại cao nhất), client chỉ nhận `SkillCast` với `targetEntityId` và `CombatEvent`; vùng báo trước của kỹ năng tối thượng gửi qua `SkillCast` (`origin`, `direction`, `travelMs` = thời gian gồng) để client vẽ cảnh báo.

**Còn mở (OPEN):** giá skin cho 414 costume (đã giải thích cho người dùng, chờ chọn cách định giá); xác nhận các con số boss/hồi Ki bằng dữ liệu chơi thật.


### 10.12 Phân loại và định giá 414 skin (costume) (người dùng chốt hướng, số PROPOSED, 2026-10-07)

> **ĐÃ SỬA (mục 10.13):** costume 1788 "Cải trang baby khỉ" **không phải** dạng khỉ của skill (là vật phẩm sự kiện thường); số đúng là **SHOP 313, FUSION_FORM 6, COMPONENT 95, SKILL_FORM 0** (dạng khỉ của Biến hình dùng part 192 đến 200, nằm ngoài catalog). Dòng `SKILL_FORM (1)` bên dưới là bản cũ.

**Người dùng chốt (CONFIRMED):** (1) một số skin **được kích hoạt theo skill** nên **không tính là skin mua bán**; (2) một số skin **áp dụng cho Bông tai Potara** (tên theo chủng tộc: **td** Trái Đất, **xd** Xayda, **nm** Namếc); (3) **giá tính theo chỉ số cộng thêm, theo nhóm và độ hiếm**; (4) **giá có thể thay đổi trên trang quản trị admin**.

**Phân loại tìm được trong `CostumeCatalog.json` (CONFIRMED: đã đọc dữ liệu; phép gán là PROPOSED):**
- `SKILL_FORM` (1): `1788 "Cải trang baby khỉ"` (nhóm GreatApe), dạng khỉ do skill Biến hình; **không bán**. Các skill khác có thể có dạng riêng (ví dụ Sôcôla): chưa thấy costume tương ứng trong catalog; người dùng có thể chỉ thêm (OPEN).
- `FUSION_FORM` (6): `"Cải trang Hợp Thể"` id **601 (td), 602 (nm), 603 (xd)** và **639 (xd), 640 (td), 641 (nm)** (hai bộ ba, đều nhóm Fusion); **không bán**, server gán theo **chủng tộc nhân vật chủ** khi hợp thể bằng Bông tai Potara. Dùng bộ nào mặc định, bộ còn lại làm skin thay thế hay không: OPEN (chưa quyết định).
- `COMPONENT` (95): các "Avatar"/nón chỉ có đầu (`isHeadOnly`), **không phải skin đầy đủ**; không bán như skin (PROPOSED; người dùng chưa nói gì về nhóm này).
- `SHOP` (**312**): còn lại (Standard 176, Chibi 48, Fusion 47, Broly 41); bán bằng đá quý, **khóa theo chủng tộc** như đã chốt.

**Công thức định giá (PROPOSED, nằm trong dữ liệu có version, sửa được trên admin):**
- `giá_đá_quý = giá_cơ_sở[nhóm] x hệ_số[độ hiếm]`: giá cơ sở Standard 100, Chibi 150, Fusion 300, Broly 400; hệ số COMMON x1, RARE x2, VERY_RARE x4 (giá từ 100 đến 1.600).
- `chỉ_số_cộng_thêm` của skin theo độ hiếm: COMMON +1%, RARE +3%, VERY_RARE +5% **tất cả chỉ số cơ bản (cộng vào nhóm % cộng dồn)**. Tổng % cộng dồn tối đa tăng từ 275% lên 280%, nên `Max_ATK_Total` ≈ 512.400 (+2,5%); vẫn coi 500.000 là hằng số thiết kế (trong dung sai).
- **Độ hiếm ban đầu là phỏng đoán theo tên** (`rarityStatus = HEURISTIC`): tên chứa VIP / Siêu / Super / God / Gogeta / Rose / SSJ / Zamasu / Hatchiyack = VERY_RARE; tên khác "Cải trang" chung = RARE; "Cải trang"/"CT" trơn = COMMON. Kết quả: 81 COMMON, 199 RARE, 32 VERY_RARE; tổng giá mua cả 312 skin ≈ 115.550 đá quý (≈ 3.850 ngày với 30 đá quý mỗi ngày, nên không ai mua hết). Admin cần soát và sửa độ hiếm, giá và chỉ số từng skin.

**Dữ liệu và công cụ:** script `BattleBall\SourceData\tools\build_skin_pricing.py` sinh `BattleBall\SourceData\tables\seed\skin_pricing.json` (414 dòng: `costumeId`, `kind`, `rarity`, `gemPrice`, `statBonusPct`, `fusionRace`...). Bảng server tương ứng (PROPOSED): `SkinTemplate(costumeId, kind, rarity, gemPrice, statBonusPct, fusionRace?, version)`; `OwnedSkin(accountId, costumeId)` chỉ cho `kind = SHOP`.

**Còn mở (OPEN):** bộ Hợp Thể mặc định (601 đến 603 hay 639 đến 641); thêm dạng skin cho skill khác (Sôcôla, Biến hình của chủng tộc khác); xử lý nhóm Component (avatar) có bán không; soát độ hiếm 312 skin trên admin.


### 10.13 Skin của kỹ năng Biến khỉ (Biến hình) của Xayda (CONFIRMED: đọc từ code và dữ liệu, 2026-10-07)

Người dùng hỏi kỹ năng Biến khỉ của Xayda dùng skin nào ở cấp 1 đến 7, và cho biết frame của nó nằm từ `2200.png` đến `2243.png`, để lọc ra khỏi danh sách skin.

**Kết luận (CONFIRMED, từ `Player.getHead/getBody/getLeg` và `ConstPlayer.HEADMONKEY` của Teamobi2026, đối chiếu với bảng `part`):** khi skill đang chạy (`isMonkey`), hình dạng khỉ ghép từ **3 part**: đầu theo cấp, thân và chân cố định.

| Cấp Biến khỉ | Part đầu (`part.id`) | Part thân | Part chân | Icon (frame) của đầu |
|---|---|---|---|---|
| 1 | 192 | 193 | 194 | 2204, 2205 |
| 2 | 195 | 193 | 194 | 2232, 2233 |
| 3 | 196 | 193 | 194 | 2234, 2235 |
| 4 | 199 | 193 | 194 | 2240, 2241 |
| 5 | 197 | 193 | 194 | 2236, 2237 |
| 6 | 200 | 193 | 194 | 2242, 2243 |
| 7 | 198 | 193 | 194 | 2238, 2239 |

- Thân (part 193, 17 frame) dùng các icon 2200, 2201, 2208, 2212, 2214, 2216 đến 2219, 2224 đến 2226, 2228, 2229, cùng icon 16 chung; chân (part 194, 14 frame) dùng 2203, 2206, 2207, 2211, 2213, 2215, 2220 đến 2223, 2227, 2230, cùng icon 0 chung. Các icon 0, 16, 20 là frame trống/dùng chung.
- **Các frame 2200 đến 2243 (44 icon) đúng là bộ frame của dạng khỉ:** 40 icon được 9 part nói trên tham chiếu; 4 icon còn lại (**2202, 2209, 2210, 2231**) có file ảnh (đủ ở x1 đến x4) nhưng **không part nào tham chiếu** (cách dùng UNKNOWN; có thể là hiệu ứng/đuôi khỉ do code khác vẽ).
- **Không có skin nào trong `CostumeCatalog.json` (414) hay `PartDatabase.json` (984) dùng 9 part này hoặc các icon 2200 đến 2243** (đã kiểm tra: 0 costume, 0 part). Vì vậy dạng khỉ **nằm ngoài danh sách skin hiện có**; "lọc ra" nghĩa là **không được nhập 9 part này (192 đến 200) từ bảng `part` vào danh mục skin hay cửa hàng** khi mở rộng 984 lên các part còn lại (1.115 part chỉ có trong SQL).
- Biến khỉ NRO còn dùng quy tắc khác: HP nhân đôi lúc bật (`setHp(hp * 2)`), kéo dài `(cấp + 5) x 10 s` (mục 10.7). Code là của skill id 13; vật phẩm học "Hóa khỉ khổng lồ" cấp 1 đến 7 là item 314 đến 320 và 1336.

**Sửa một nhận định sai ở mục 10.12 (CONFIRMED):** tôi từng xếp costume `1788 "Cải trang baby khỉ"` (nhóm GreatApe) là dạng khỉ của skill. **Sai:** theo `item_template`, 1788 là **vật phẩm sự kiện** thường (TYPE 5, part 1721, 1722, 1723), không liên quan skill. Nay phân loại lại thành `SHOP`; không costume nào trong catalog là `SKILL_FORM`. Phân loại cập nhật: **SHOP 313**, **FUSION_FORM 6**, **COMPONENT 95**, **SKILL_FORM 0**; tổng giá mua cả 313 skin ≈ 116.350 đá quý.

**Dữ liệu và công cụ:** `BattleBall\SourceData\tools\build_skill_forms.py` sinh `BattleBall\SourceData\tables\seed\skill_forms.json` (skill 13, 7 cấp, part đầu/thân/chân, danh sách icon, `excludePartIdsFromSkinCatalog = [192..200]`, 4 icon chưa tham chiếu). `build_skin_pricing.py` đã sửa theo kết luận trên và sinh lại `skin_pricing.json` (GreatApe 1788 giá cơ sở 400).

**Còn mở (OPEN):** cách client vẽ dạng khỉ theo cấp (mapping vào hệ `NroMannequin`/`NroPartData`; chưa nhập 9 part này); công dụng của 4 icon không tham chiếu; dạng skin của các skill khác (Sôcôla dùng part 412 đến 414 trong code, Mafuba/đá/Halloween...) chưa kiểm tra.


### 10.14 Trang HTML duyệt và phân loại 414 skin ở trạng thái IDLE (2026-10-07)

**Yêu cầu của người dùng:** liệt kê và render 414 skin ở trạng thái IDLE trên HTML để tự phân loại độ hiếm/loại; trợ lý phân loại trước để người dùng đỡ chọn từng cái.

**Đã làm (CONFIRMED, đã kiểm thử trong trình duyệt của app: hình vẽ đúng, đổi độ hiếm cập nhật giá, lưu được):**
- `BattleBall\SourceData\skin_browser.html` (sinh bởi `tools\build_skin_browser.py`, 131 KB, dữ liệu nhúng sẵn). **Mở bằng cách nhấp đúp file trong trình duyệt.** *(Đã sửa cùng ngày: bản đầu lấy ảnh theo đường dẫn tương đối `data\icon\x2\<id>.png` nên người dùng báo "rất nhiều cái không có hình" khi mở ngoài thư mục gốc hoặc trong khung xem tĩnh; nay **mọi ảnh (916 icon x2) nhúng thẳng vào HTML dưới dạng base64**, file ≈ 3,6 MB, **không phụ thuộc thư mục hay máy chủ**; kiểm tra: 414/414 thẻ có hình, 0 yêu cầu ảnh ngoài, 0 ảnh lỗi.)*
- Vẽ tư thế IDLE (`cf = 0`, Stand_1) bằng đúng công thức của `NroMannequin.PlaceSlot`: đầu frame 0, chân và thân frame 1; offset đầu (-13, 34), chân (-8, 10), thân (-9, 16); thứ tự đầu, chân, thân. Costume thiếu thân/chân (avatar, nón) dùng part mặc định 57 và 58 như công cụ thử trong Unity.
- Mỗi thẻ có: hình IDLE, id, tên, chủng tộc, nhóm, **Loại** (SHOP, FUSION_FORM, COMPONENT, SKILL_FORM, HIDDEN), **Độ hiếm** (Phổ thông, Hiếm, Rất hiếm), **Giá đá quý**, **Chỉ số cộng thêm (%)**, và thông tin tham khảo của NRO (bán trong shop NRO hay vật phẩm sự kiện, % chỉ số NRO, giá ngọc NRO, mô tả).
- Tìm kiếm theo tên/id; lọc theo nhóm, loại, độ hiếm, chủng tộc, nguồn NRO, "đã sửa tay"; **áp dụng hàng loạt cho các skin đang lọc**; chỉnh cỡ thẻ; **tự lưu trong trình duyệt**; **Xuất JSON/CSV**, Nhập JSON, Đặt lại. Đổi độ hiếm thì giá và chỉ số tự tính lại (sửa tay được).
- Cách gửi lại kết quả: bấm **Xuất JSON** (file `skin_pricing_edited.json` trong thư mục Tải xuống), báo đường dẫn để trợ lý nhập vào `skin_pricing.json`/bảng `SkinTemplate`.

**Phân loại trước của trợ lý (PROPOSED, dựa trên dữ liệu NRO thay vì chỉ đoán tên; `build_skin_pricing.py` v2):**
- **Tín hiệu NRO (CONFIRMED: `item_template` có đủ 414 costume, TYPE 5, id costume = id item):** mô tả item ("Cải trang khi hợp thể" = 6 skin Hợp Thể Potara, "Dùng để thay đổi khuôn mặt", "VPSK"/"Vật phẩm sự kiện"/"giáng sinh" = sự kiện), tổng % chỉ số trong `item_shop_option` (sức đánh, HP, Ki, chí mạng, giảm sát thương), giá ngọc trong `item_shop`, có bán trong shop NRO hay không (153 costume có bán).
- **Loại:** FUSION_FORM 6 (601, 602, 603, 639, 640, 641), COMPONENT 95, SHOP 313, SKILL_FORM 0.
- **Độ hiếm (313 skin SHOP):** điểm = (% chỉ số NRO ≥ 60: +2; ≥ 20: +1) + (giá ngọc NRO ≥ 1.000: +2; ≥ 300: +1) + (vật phẩm sự kiện: +1) + (tên có VIP/Siêu/Super/God/Gogeta/...: +2); điểm ≥ 3 là Rất hiếm, ≥ 1 là Hiếm, còn lại Phổ thông. **Kết quả: Phổ thông 105, Hiếm 154, Rất hiếm 54.** Theo nhóm (phổ thông / hiếm / rất hiếm): Standard 64 / 92 / 20; Chibi 22 / 21 / 5; Fusion 11 / 18 / 18; Broly 8 / 22 / 11; GreatApe 0 / 1 / 0. Nguồn: 105 skin bán trong shop NRO, 122 vật phẩm sự kiện, 86 skin khác.
- **Giá:** giá cơ sở theo nhóm x hệ số (1, 2, 4); tổng mua hết 313 skin ≈ 120.300 đá quý. Đây là đề xuất; người dùng sửa trên trang HTML hoặc admin.
- Hạn chế: 39 costume có nhóm Component nhưng mô tả không phải "thay đổi khuôn mặt" (nón, phụ kiện); giữ nguyên là COMPONENT theo nhóm. Chưa xác minh `level` (254 costume `level > 0`) có ý nghĩa gì.

**Còn mở (OPEN):** kết quả phân loại do người dùng chốt trên trang HTML; bộ Hợp Thể mặc định (601 đến 603 hay 639 đến 641); có bán nhóm Component không.


### 10.15 Kết quả duyệt skin của người dùng, loại BOSS_MOB, luật skin hợp thể và danh sách chỉ số của skin (2026-10-07)

**Đã nhập file `Desktop\skin_pricing_edited.csv` (CONFIRMED):** bản sao lưu ở `BattleBall\SourceData\tables\seed\skin_user_edit_2026-10-07.csv`; `build_skin_pricing.py` v3 đọc file này làm đầu vào. Kết quả người dùng chọn: **SHOP 255, COMPONENT 55, FUSION_FORM 11, HIDDEN 93**.
- **Đã xóa 93 skin người dùng đánh dấu "Ẩn (không dùng)"** khỏi `skin_pricing.json` (danh sách id lưu ở `removedHiddenCostumeIds`) và khỏi `skin_browser.html`; còn **321** costume. **Chưa xóa khỏi `Assets\NroMannequin\Data\CostumeCatalog.json` / `PartDatabase.json`** (client Unity và bài kiểm chứng vẫn dùng đủ 414); việc xóa khỏi catalog client là một thay đổi riêng (OPEN, cần người dùng yêu cầu).
- Người dùng đã chuyển thêm vào **FUSION_FORM**: `885 "CT Lích Tên béo"` và 4 skin Broly `1815 "ct dep vcl"`, `1816 "td"`, `1817 "nm"`, `1818 "xd"` (cùng 6 skin "Cải trang Hợp Thể" 601, 602, 603, 639, 640, 641). Tổng **11 skin hợp thể**; `fusionRace` suy từ chủng tộc hoặc từ tên td/xd/nm (1815 và 885 chưa có chủng tộc: OPEN).
- Người dùng đặt tay 4 skin `1780`, `1781`, `1783`, `1784` (Broly) thành Rất hiếm 1.600.

**Loại mới `BOSS_MOB` (CONFIRMED, người dùng yêu cầu):** hình dạng dành cho **boss và quái nhỏ**, không bán, người chơi không mặc. Hiện chưa skin nào được gán (người dùng gán trên trang HTML). Danh sách loại nay: SHOP, FUSION_FORM, COMPONENT, SKILL_FORM, BOSS_MOB, **NPC**, HIDDEN. **Loại `NPC` (CONFIRMED, người dùng yêu cầu thêm 2026-10-07):** hình dạng dành cho NPC (cửa hàng, nhiệm vụ...), không bán, người chơi không mặc; chưa gán cho skin nào.

**Luật skin hợp thể (CONFIRMED, người dùng chốt):** skin hợp thể **có thể mua**, nhưng khi dùng **chỉ mặc được lên nhân vật chủ khi hợp thể bằng Bông tai Potara**; **AI và nhân vật phụ không mặc được skin hợp thể** (kể cả skin hợp thể khác). Hệ quả (PROPOSED): `SkinTemplate.fusionOnlyMainCharacter = true`, `aiCanWear = false`; server từ chối lệnh mặc `FUSION_FORM` lên AI hoặc khi chưa hợp thể; chỉ áp dụng skin khi cặp hợp thể đang hoạt động trong trận; khi chưa sở hữu skin hợp thể nào thì dùng bộ mặc định theo chủng tộc (601, 602, 603). Giá skin hợp thể mặc định = giá cơ sở theo nhóm x hệ số độ hiếm như SHOP (ví dụ 601: 1.200; chỉnh được). Seed ghi `fusionOnlyMainCharacter` và `aiCanWear`.

**Các chỉ số một skin có thể mang (CONFIRMED: đọc mã Teamobi2026 và dữ liệu, 2026-10-07):**
- **Nguồn:** `NPoint.addOption(ItemOption)` (`NPoint.java` khoảng dòng 472 đến 650) là hàm duy nhất đổi "option" của đồ đang mặc, kể cả cải trang, thành chỉ số nhân vật (`calPoint` lặp qua `itemsBody` rồi gọi `addOption`). Đối chiếu với `item_shop_option`/`item_option_template`: 414 costume dùng **63 loại option** (cả 63 đã được xếp loại; `tools\build_skin_stats.py` sinh `tables\seed\skin_stat_catalog.json`).
- **Kết quả: 49 khóa chỉ số**: **27 dùng trực tiếp được** trong BattleBall, **13 là hiệu ứng đặc biệt** (cần cơ chế riêng), **9 không áp dụng** (đã bỏ).
  - **% chỉ số chiến đấu:** Sức đánh %, HP %, Ki %, Giáp %, tỷ lệ chí mạng, sát thương chí mạng %, né đòn %, phản sát thương %, tốc độ di chuyển %, biến % sát thương thành HP / Ki, sức đánh % khi đánh quái nhỏ, biến % sát thương lên quái thành HP, xuyên giáp khi dùng chưởng / cận chiến, biến % sát thương chưởng nhận vào thành Ki, hồi HP/Ki % mỗi giây, hút HP/Ki xung quanh mỗi 5 giây.
  - **Số cộng thẳng:** Sức đánh, HP, Ki, Giáp, hồi HP / Ki cố định.
  - **Kinh tế:** EXP từ boss %, vàng nhận được %.
  - **Hiệu ứng (cần cơ chế riêng):** tàng hình, làm chậm / hóa đá / biến sôcôla xung quanh, miễn lạnh, kháng Thái Dương Hạ San (miễn choáng), dịch chuyển tức thời, phát nổ sau khi chết, hồi Ki khi bị đánh, nhược điểm giảm HP, cộng dồn sức đánh khi chỉ đấm, giảm sát thương khi Ki thấp, đổi sức đánh lấy EXP và vàng.
  - **Không áp dụng:** hạn sử dụng, không giao dịch, "chỉ có tác dụng khi hợp thể" (thay bằng loại FUSION_FORM), hiệu ứng khi ở gần người chơi khác (game chơi đơn), thưởng cho đệ tử, mảnh bộ, dò pha lê, may mắn, nhãn hiển thị.
- **Ví dụ một skin (`765 "Cải trang Gohan Bư"`):** NRO gắn Sức đánh +22%, HP +19%, Ki +19%, hồi HP +5%, hồi Ki +5% và hạn sử dụng 30 ngày. Trong BattleBall skin này (độ hiếm Hiếm) mặc định có Sức đánh, HP, Ki mỗi thứ +3%, và người dùng có thể thêm bất kỳ chỉ số trong danh sách trên.
- **Khoảng giá trị NRO đo được:** Sức đánh 8 đến 33%, HP 8 đến 30%, Ki 15 đến 30%, chí mạng 2 đến 15, sát thương chí mạng 10 đến 30%, giảm sát thương 5 đến 15%, tốc độ 20 đến 100%. **Các giá trị này lớn hơn nhiều giới hạn của BattleBall** (tổng % cộng dồn tối đa 280%; xem 10.12), nên BattleBall dùng **gợi ý tối đa mỗi chỉ số** (Sức đánh/HP/Ki/Giáp 10%, chí mạng 3, sát thương chí mạng 20%, tốc độ 10%...): giá trị vượt gợi ý bị cảnh báo trên trang HTML.
- **Lệch giữa dữ liệu và code (UNKNOWN nguyên nhân):** option id 156 trong DB tên "Cộng dồn 1% sức đánh khi chỉ dùng chiêu đấm" còn chú thích code ở `addOption` ghi 156 là "Giảm 50% sức đánh, HP, KI..." (DB ghi tên đó ở id 155). Seed theo DB và ghi chú lệch.
- Số costume có chỉ số NRO gắn: 115 trong 321 (còn lại không bán trong shop NRO nên không có option, ví dụ skin sự kiện); chỉ số mặc định của các skin có giá là Sức đánh/HP/Ki bằng nhau theo độ hiếm (1%, 3%, 5%).

**Cập nhật trang `BattleBall\SourceData\skin_browser.html` (3,2 MB, tự chứa ảnh, đã kiểm thử trong trình duyệt):** 321 costume; thêm loại BOSS_MOB; skin có giá (SHOP và FUSION_FORM) có **bộ sửa chỉ số** (thêm/xóa dòng, chọn trong danh sách chỉ số, nhập giá trị, cảnh báo vượt gợi ý) và dòng tham khảo "NRO gắn: ..."; nút **"Danh sách chỉ số"** hiển thị toàn bộ catalog 49 chỉ số với số costume NRO dùng và khoảng giá trị; ghi chú luật skin hợp thể trên thẻ FUSION_FORM; xuất JSON/CSV có cột `stats`, `fusionOnlyMainCharacter`. Dữ liệu mặc định của trang đã nạp sẵn các lựa chọn từ CSV của người dùng.

**Sửa lỗi xuất file (2026-10-07):** người dùng báo trang không xuất được JSON/CSV (và F5 không thấy bản mới). Đã sửa `skin_browser.html`: nút "Xuất JSON" và "Xuất CSV" nay luôn mở **hộp nội dung** có nút "Sao chép" và "Tải file" (ngoài việc thử tải file tự động), để dùng được cả khi trình duyệt/khung xem chặn tải file; thêm quy tắc CSS `[hidden]`. Đã kiểm thử: sinh đúng 321 dòng, JSON hợp lệ. Nếu F5 vẫn ra bản cũ: dùng Ctrl+F5 hoặc mở file bằng trình duyệt thật (khung xem file của app chỉ hiện bản chụp tĩnh).

**Còn mở (OPEN):** gán BOSS_MOB cho skin nào; chủng tộc của skin hợp thể 885 và 1815; xóa 93 skin khỏi catalog client hay không; bộ chỉ số cuối cùng cho từng skin (người dùng sửa trên trang HTML rồi xuất JSON).


### 10.16 Vòng duyệt skin thứ hai của người dùng (2026-10-07)

**Đã nhập file `Desktop\skin_pricing_edited.csv` (CONFIRMED):** bản sao lưu ở `BattleBall\SourceData\tables\seed\skin_user_edit_round2.csv` (`build_skin_pricing.py` đọc file này; costume vắng mặt khỏi file xuất mới nhất tính là đã xóa ở vòng trước). Máy chủ thử đã tắt theo yêu cầu.

**Kết quả người dùng chọn (321 dòng trong CSV):** SHOP 86, BOSS_MOB 64, COMPONENT 34, NPC 25, FUSION_FORM 12, **HIDDEN 100 (mới)**.
- **Đã xóa thêm 100 skin ẩn** khỏi `skin_pricing.json` (`removedHiddenCostumeIds`: nay **193**, gồm 93 vòng trước và 100 vòng này) và khỏi `skin_browser.html`. **Còn 221 costume.** Vẫn **chưa xóa** khỏi `CostumeCatalog.json`/`PartDatabase.json` của client (vẫn 414; việc xóa là thay đổi riêng, OPEN).
- **Loại sau cùng:** skin bán thường 86; skin hợp thể 12 (601, 602, 603, 639, 640, 641, 885, 1566, 1815, 1816, 1817, 1818); skin dành cho boss/quái nhỏ 64; NPC 25; avatar/đầu 34.
- **Skin có giá:** 98 (86 SHOP và 12 FUSION_FORM): độ hiếm Phổ thông 28, Hiếm 26, Rất hiếm 44; giá mua hết ≈ **67.250 đá quý**. Chỉ số của từng skin lấy đúng cột `stats` người dùng xuất (kiểm tra: 0 dòng lệch); mặc định vẫn là Sức đánh, HP, Ki bằng nhau theo độ hiếm.
- Skin hợp thể chưa có chủng tộc (`fusionRace` trống): 885, 1566 (cùng tên "CT Lích Tên béo") và 1815 ("ct dep vcl"); 1816, 1817, 1818 suy từ tên td/nm/xd. OPEN: người dùng chỉ rõ chủng tộc, hoặc để dùng cho mọi chủng tộc.
- Nhóm thân hình còn lại: Standard 81, Fusion 42, Broly 40, Component 34, Chibi 23, GreatApe 1 (1788 nay là BOSS_MOB).

**Cập nhật `skin_browser.html`:** 221 costume, 522 icon nhúng (2,3 MB); kiểm tra cú pháp JavaScript đạt.

**Còn mở (OPEN):** chủng tộc của 885, 1566, 1815; xóa 193 skin ẩn khỏi catalog client hay không; gán chỉ số cuối cho 98 skin có giá (người dùng sửa trên trang rồi xuất lại).


### 10.17 Chốt 3 việc: chủng tộc skin hợp thể, thu gọn catalog client, và thiết kế bảng MariaDB (2026-10-07)

**1. Chủng tộc 3 skin hợp thể (CONFIRMED về cách xử lý, người dùng "chốt"):** 885, 1566 ("CT Lích Tên béo") và 1815 ("ct dep vcl") không có chủng tộc trong dữ liệu (catalog ghi "Mọi Giới Tính"), nên được đặt `fusion_race = ANY`: **nhân vật chủ thuộc mọi chủng tộc đều mặc được khi hợp thể** (vẫn chỉ nhân vật chủ, AI không mặc). Các skin khác: 601, 640, 1816 là td; 602, 641, 1817 là nm; 603, 639, 1818 là xd.

**2. Thu gọn catalog client (CONFIRMED, đã làm và kiểm chứng):**
- `Assets\NroMannequin\Data\CostumeCatalog.json`: **414 → 221 costume**; `PartDatabase.json`: **984 → 569 part** (chỉ giữ part mà 221 costume dùng, gồm đầu, thân, chân, chu kỳ đầu). Công cụ: `BattleBall\SourceData\tools\prune_client_catalog.py` (an toàn chạy lại). **Bản đầy đủ lưu ở `SourceData\tables\backup\CostumeCatalog_full414.json` và `PartDatabase_full984.json`.**
- Đã sửa số 414 thành 221 trong `NroCostumeTesterVerification.cs`, `CostumeTesterSceneBuilder.cs` và nhãn "All (221)" trong scene `CostumeTester.unity`.
- **Kiểm chứng Unity batch (2022.3.62f2): 9/9 mục PASS** với 221 costume (221/221 x 4 thang zoom, 33 tư thế, 0 lỗi part, đường dẫn SourceData).
- Không có skin nào của skill (part 192 đến 200) bị ảnh hưởng (chúng vốn không có trong catalog).

**3. Thiết kế bảng MariaDB cho dữ liệu tĩnh (PROPOSED, yêu cầu người dùng: áp dụng tất cả chỉ số, chỉ khác nhau ở trạng thái kích hoạt/không kích hoạt, chỉnh được trên trang quản trị admin):**
- **File:** `BattleBallServer\db\migrations\V001__static_data_schema.sql` (13 bảng) và `V002__static_data_seed.sql` (124 KB, sinh bởi `BattleBall\SourceData\tools\build_static_seed_sql.py` từ các file seed). Đây là các file đầu tiên trong `BattleBallServer`.
- **Chỉ số: `stat_type` giữ đủ cả 49 loại**, cột **`enabled`** là công tắc kích hoạt (admin bật/tắt): mặc định **27 loại chỉ số thường = bật**, **22 loại (hiệu ứng đặc biệt chưa có cơ chế và loại không áp dụng) = tắt**. `skin_stat(costume_id, stat_key, value)` gắn chỉ số cho skin; server chỉ áp một dòng khi loại chỉ số đó đang `enabled = 1`.
- **Mọi thứ admin có thể muốn thêm đều là dữ liệu, không phải kiểu cột:** `skin_kind` (SHOP, FUSION_FORM, COMPONENT, SKILL_FORM, BOSS_MOB, NPC; cờ `sellable`, `player_wearable`, `requires_fusion_main`; admin thêm loại mới bằng một dòng), `skin_rarity` (hệ số giá và chỉ số mặc định), `skin_group_price` (giá cơ sở theo nhóm thân hình).
- **Bảng chính:** `skin_template` (221 dòng; `rarity_status` HEURISTIC/MANUAL, `gem_price`, `fusion_race` td/xd/nm/ANY, `ai_can_wear` luôn 0 cho skin hợp thể), `skin_nro_stat` (tham khảo NRO), `skill_template` (25) và `skill_level` (181; gồm `params` JSON, vùng trúng, giá vàng), `skill_form` (dạng khỉ của Biến hình), `balance_config` (21 tham số: `Max_ATK_Total`, giáp K, vàng mỗi trận, Ki hồi, Potara, nhịp boss, thứ tự skill AI...), `static_data_version` (số phiên bản từng nhóm để server/client nạp lại), `admin_audit_log` (ai sửa gì, trước/sau).
- **Dữ liệu seed:** `stat_type` 49, `skin_template` 221, `skin_stat` 294, `skin_nro_stat` 281, `skill_template` 25, `skill_level` 181, `skill_form` 7, `skin_kind` 6, `skin_rarity` 3, `skin_group_price` 6, `balance_config` 21, `static_data_version` 4.
- **Mức kiểm chứng (nói rõ giới hạn):** **chưa chạy trên MariaDB thật** (máy không cài MariaDB/MySQL/Docker). Đã kiểm bằng cách dịch sơ lược sang SQLite và nạp thử: tạo đủ 13 bảng, nạp seed với khóa ngoại bật, **0 vi phạm khóa ngoại**, số dòng đúng, không skin nào không bán mà có giá, số cấp skill khớp `max_level`. Cú pháp riêng của MariaDB (ENUM, SET, JSON, `ON UPDATE CURRENT_TIMESTAMP`) chưa được kiểm; cần chạy thử trên MariaDB 10.6 trở lên trước khi dùng.
- **Hạn chế đã biết:** các bảng người chơi (tài khoản, nhân vật, túi đồ...) chưa thiết kế ở dạng SQL (chỉ có mô tả ở mục 3); cột `COMPONENT.player_wearable = 1` là đoán (người dùng chưa nói cách có được avatar); mã hóa ký tự `\` trong chuỗi seed theo quy tắc mặc định của MariaDB (nhân đôi dấu gạch chéo ngược).

**Còn mở (OPEN):** chạy thử SQL trên MariaDB thật; thiết kế trang admin (sửa `enabled`, giá, chỉ số, ghi `admin_audit_log`, tăng `static_data_version`); cách có được skin COMPONENT; thiết kế bảng người chơi.

### 10.18 Hành tinh skin hợp thể, avatar nhận khi tạo nhân vật, và bảng người chơi V003 (2026-10-07)

**1. Hành tinh của skin hợp thể (CONFIRMED, người dùng chọn):** **giữ `ENUM('td','xd','nm','ANY')`** của `skin_template.fusion_race`, không tạo bảng tra cứu. Trang admin phải có **một ô chọn riêng "hành tinh"** cho skin loại FUSION_FORM để sửa (ví dụ đổi skin 885 từ `ANY` sang `xd`). Lưu ý (PROPOSED): thêm hành tinh mới sẽ cần sửa cấu trúc bảng (không phải chỉ thêm một dòng), và một skin chỉ có một giá trị (không chọn hai hành tinh cùng lúc).

**2. Avatar/đầu (kind COMPONENT) nhận khi tạo nhân vật (CONFIRMED, người dùng chọn):** không bán, không rơi. Bảng tĩnh admin sửa được **`race_default_skin(race, costume_id, is_equipped)`**; mỗi nhân vật mới nhận các avatar mặc định của chủng tộc mình (ghi vào `owned_skin` với `source = DEFAULT`) và mặc cái có `is_equipped = 1`. **Danh sách avatar mặc định là PROPOSED:** 412 đến 414 (Trái Đất), 415 đến 417 (Namếc), 418 đến 420 (Xayda), vì đó là các avatar duy nhất trong catalog mà NRO bán nhưng không có giá ngọc; mặc cái đầu tiên của mỗi chủng tộc. Các avatar COMPONENT còn lại (đang có giá cơ sở trong `skin_group_price` từ vòng phân loại trước) hiện **không có cách nhận** (OPEN: người dùng chưa nói; ví dụ phần thưởng boss hay thành tích).

**3. Phạm vi dữ liệu người chơi: mọi thứ riêng từng nhân vật (CONFIRMED, người dùng chọn):** vàng, ngọc, túi đồ, skin sở hữu, vật liệu, cấp skill đều thuộc `game_character`. **Thay đổi so với quyết định cũ** (đã được thay thế): skin sở hữu theo tài khoản (mục 3.8), ví theo tài khoản (mục 3.1/3.7), túi 2.000 món theo tài khoản (mục 3.7a). Nay giới hạn 2.000 món `BAG` tính **theo từng nhân vật**, và muốn dùng cùng một skin cho 3 nhân vật phải mua 3 lần (skin vẫn khóa theo chủng tộc nên thực tế mỗi skin chỉ mua được cho nhân vật đúng chủng tộc, riêng skin `gender_id = 3` dùng chung chủng tộc mua riêng cho từng nhân vật). **Ngoại lệ (PROPOSED, suy ra từ quyết định cũ "mỗi tài khoản 1 bông tai"):** Bông tai Potara, số mảnh bông tai và bộ đếm mua theo ngày nằm ở **cấp tài khoản** (`account.potara_owned`, `account.potara_fragments`, `daily_purchase_counter.account_id`), vì bông tai nối hai nhân vật của một tài khoản. Nếu bạn muốn bông tai cũng theo nhân vật, cần chốt lại luật "1 bông tai mỗi tài khoản".

**4. File `BattleBallServer\db\migrations\V003__player_data_schema.sql` (PROPOSED, 12 bảng + `race_default_skin`):** `account` (bcrypt, `role`, `banned`, bông tai), `auth_token` (băm SHA-256, hạn trượt 30 ngày), `game_character` (`UNIQUE(account_id, race)` nên mỗi tài khoản đúng 3 nhân vật, cấp 1 đến 100), `character_wallet` (vàng, ngọc, `version` khóa lạc quan), `currency_ledger` (mọi biến động, `idempotency_key` duy nhất theo nhân vật), `character_skill`, `owned_skin`, `item_instance` (`location` BAG/EQUIPPED), `item_stat_line` (mỗi dòng chỉ số một bản ghi, không JSON theo vị trí), `material_stack`, `character_equipment` (10 ô: 5 trang bị, SKIN, 4 hỗ trợ; ô SKIN chỉ cho skin nhân vật đó đã sở hữu nhờ khóa ngoại ghép), `daily_purchase_counter` (`day_key` mốc 00:00 UTC+7). Chưa thiết kế: `item_template`, `material_template` (nên `template_id` chưa có khóa ngoại), `RunProgress`, `AnomalyLog`, `ItemLog`, bản đồ nhỏ. Việc tự tạo 3 nhân vật, ví, avatar mặc định khi đăng ký do server/web PHP làm trong một transaction (cùng một đặc tả).

**5. Kiểm chứng (nói rõ giới hạn):** vẫn **chưa chạy trên MariaDB thật**. Đã dịch sang SQLite và nạp V001, V002, V003: 26 bảng, 0 vi phạm khóa ngoại; thử luồng tạo tài khoản, 3 nhân vật, ví, cấp avatar mặc định và mặc avatar thì đúng; mặc skin chưa sở hữu bị từ chối; tạo nhân vật thứ hai cùng chủng tộc bị từ chối; các avatar mặc định đều là COMPONENT và đúng chủng tộc. Cú pháp riêng của MariaDB (ENUM, JSON, `ON UPDATE CURRENT_TIMESTAMP`, `UNIQUE KEY`, khóa ngoại ghép) vẫn chưa được kiểm.

**Còn mở (OPEN):** chạy thử V001 đến V003 trên MariaDB 10.6+ thật; thiết kế `item_template`/`material_template`; cách nhận các avatar COMPONENT ngoài nhóm mặc định; xác nhận ngoại lệ bông tai theo tài khoản; thiết kế trang admin (gồm ô chọn hành tinh skin hợp thể, `enabled` của chỉ số, giá, `race_default_skin`, ghi `admin_audit_log`, tăng `static_data_version`).

### 10.19 Kết quả chạy thử V001 đến V003 trên MariaDB thật (CONFIRMED, 2026-10-07)
- **Môi trường:** máy người dùng có XAMPP tại `E:\xampp` đang chạy, **MariaDB 10.4.32** (thấp hơn mục tiêu 10.6+, nhưng đủ để chạy). Tôi tạo cơ sở dữ liệu tạm `battleball_dbtest`, chạy ba file theo thứ tự, kiểm tra rồi **xóa cơ sở dữ liệu tạm**; các cơ sở dữ liệu khác (`team2026`, `a`, `test`...) không bị động tới.
- **Kết quả:** cả ba file chạy **không lỗi**; 26 bảng, 26 khóa ngoại; số dòng đúng (skin 221, skin_stat 294, skin_nro_stat 281, skill_level 181, stat_type 49, race_default_skin 9); 132 chuỗi `params` đều là JSON hợp lệ (`JSON_VALID`); chữ tiếng Việt đúng (`Cải trang Hợp Thể`). Luồng tạo tài khoản, 3 nhân vật, ví, avatar mặc định chạy đúng; mặc skin chưa sở hữu bị từ chối (lỗi 1452); tạo nhân vật thứ hai cùng chủng tộc bị từ chối (lỗi 1062).
- **Lưu ý:** trên MariaDB, kiểu `JSON` chỉ là bí danh của `LONGTEXT` (kèm kiểm tra hợp lệ ở phiên bản này), không phải kiểu JSON riêng như MySQL. `static_data_version` không tự tăng khi sửa dữ liệu; trang admin/server phải tăng (đúng thiết kế). **Chưa thử trên 10.6 trở lên** (nên thử lại khi có bản mới).

### 10.20 Mẫu trang bị và vật liệu: V004 (PROPOSED, người dùng giao "tự đề xuất", 2026-10-07)
- **File:** `BattleBallServer\db\migrations\V004__item_material_templates.sql`. Tạo `item_template` và `material_template`, rồi thêm khóa ngoại `item_instance.template_id` và `material_stack.template_id` (V003 để trống đến lúc này).
- **`item_template` (5 dòng, mỗi ô một mẫu, theo GDD mục 6, một bậc duy nhất):** Áo `FLAT_ARMOR` 2.500; Quần `FLAT_HP` 200.000; Găng `FLAT_ATK` 20.000; Giày `FLAT_KI` 200.000; Nhẫn `CRIT_DMG_PCT` 25. Mỗi mẫu cần **10 mảnh** để ghép. Có cột `tier` (mặc định 1, duy nhất theo `slot` + `tier`) để sau này thêm bậc đồ cao hơn chỉ bằng thêm dòng. GDD không nêu nhiều bậc nên hiện chỉ có bậc 1.
- **`material_template` (8 dòng):** 5 loại mảnh đồ (mỗi loại ghép ra một mẫu), đá nâng cấp, đá chỉ số, đá pha lê. **Giá tính bằng "số trận thắng"** (đúng mục `item_economy_wins` trong `balance_config`): một trận thắng = 1.000 × R vàng với R là cấp khuyến nghị của vùng mua (ví dụ R = 50 thì 50.000 vàng). Mảnh 0,6 trận thắng (6 trận thắng cho cả món chia 10 mảnh), đá nâng cấp 1,5, đá chỉ số 1,0; đá pha lê chỉ đổi bằng 500 đá quý. Vì **vùng chơi chưa thiết kế**, server tính giá vàng cụ thể lúc mua; admin sửa `price_wins`. Giá bán đồ = 5% giá mua cả món (`sell_fraction` trong `balance_config`), tính khi bán, không lưu.
- **Không gồm:** bảng rơi đồ (cần mẫu vùng, mob, boss trước: OPEN), item dùng một lần có thời hạn, item trang trí vĩnh viễn (GDD chưa nói loại), Bông tai Potara và mảnh bông tai (ở cấp tài khoản, mục 10.18).
- **Kiểm chứng (CONFIRMED):** chạy V001 đến V004 liên tiếp trên MariaDB 10.4.32 thật (XAMPP của người dùng, cơ sở dữ liệu tạm đã xóa): 28 bảng, không lỗi; chèn đồ và mảnh với mẫu hợp lệ thành công, mẫu không tồn tại bị từ chối (lỗi 1452).
- **Còn mở (OPEN):** bảng rơi đồ; vùng/mob/boss template; item dùng một lần có thời hạn và item trang trí; nhiều bậc đồ; icon cho đồ.

### 10.21 Vật phẩm hỗ trợ vĩnh viễn: kết quả đọc source Teamobi2026 (CONFIRMED về dữ kiện đọc được, 2026-10-07)
Người dùng nêu bốn nhóm: linh thú/pet, giáp luyện tập, cờ/cánh/vũ khí, sách tuyệt kỹ 1 và 2. **Trùng khớp với 4 ô hỗ trợ của mục 3.5a**: trong NRO, `InventoryService.putItemBody` xếp vật phẩm vào các ô thân như sau: type 27 → ô 7 (pet), type 32 → ô 6 (giáp tập luyện), type 11 → ô 8 (cờ/cánh/vũ khí, "flag bag"), type 25 → ô 10 (sách tuyệt kỹ); type 23/24 (thú cưỡi, ván bay) → ô 9 (**không nằm trong 4 nhóm người dùng nêu**). Chỉ số của vật phẩm lấy từ `item_shop_option` (các dòng đã xuất ra `tables\json`), từ code, hoặc từ dòng option khi vật phẩm được tạo.

**1. Pet (type 27, tên bắt đầu "Pet"): 48 vật phẩm.** 31 là "VPSK" (thuần thẩm mỹ, không chỉ số); 17 có mô tả hiệu ứng: ví dụ "Tăng 5% chí mạng cho chủ nhân trong 5 giây, hồi sk..." (6 pet), "Tăng 5% giáp cho chủ nhân trong 5 giây..." (3 pet, đã thấy cả mức 15% trong 10 giây ở Voi Chín Ngà, Gà Chín Cựa, Ngựa Chín Hồng Mao), cộng vài pet đặc biệt (biến người xung quanh thành bánh bao, khí lạnh đóng băng, chống lạnh, +% chí mạng khi đi cùng Khủng Long Thơ Mộng). **Mô tả bị cắt ở 60 ký tự trong cơ sở dữ liệu NRO ("hồi sk...") nên không biết hết điều kiện hồi chiêu (UNKNOWN).** Trong code `Player.sendNewPet` và `PetService.Pet2` chỉ tạo pet đi theo (hình ảnh); **không tìm thấy code cộng chỉ số 5 giây** (UNKNOWN, có thể chưa được cài). Pet ở ô 7 không có dòng option trong `item_shop_option`, còn ô "linh thú đi theo" (ô 12, `sendPetFollow`) là hình đi theo của hiệu ứng chibi, **không phải một nhóm vật phẩm riêng**.

**2. Giáp tập luyện (type 32): 7 vật phẩm** (cấp 1, 2, 3 theo từng cặp id 529 đến 531 và 534 đến 536, thêm cấp 4 id 1716). Cơ chế (`NPoint.setDameTrainArmor`, `ItemService.getPercentTrainArmor`, `EffectSkin.updateTrainArmor`): **khi mặc, sức đánh giảm 10%, 20%, 30%, 40% theo cấp; mỗi phút đứng yên không đánh (30 giây không tấn công) cộng 1 phút "tích luỹ" (tối đa 1.000 phút, option id 9); khi cởi ra, sức đánh tăng đúng tỉ lệ đó (10/20/30/40%) cho tới khi hết số phút tích luỹ** (mỗi phút trừ 1). Yêu cầu sức mạnh 0, 1,5 triệu, 15 triệu. Hai cặp id chỉ khác cách bán (vàng/ngọc). Cơ chế này **dựa trên thời gian thực và treo máy**, không hợp trực tiếp với trận đấu (xem đề xuất bên dưới).

**3. Cờ/cánh/vũ khí (type 11): 190 vật phẩm** (gồm nhiều thứ không phải trang trí như ngọc rồng, đứa bé). Phần có chỉ số trong shop: **HP+% (params 3, 5, 10, 15, 18), Ki+% (3, 5, 10, 15), Sức đánh+% (3, 5, 10, 15, 18)**, mỗi vật phẩm thường mang cả ba cùng giá trị (ví dụ Cờ Hoa sen 5/5/5, Balo Capybara 10/10/10, Cánh Thiên thần 3: 3/3/3). Lẻ tẻ: sát thương chí mạng +9%, Ki +5 mỗi 30 giây, "Cute" +3% sức đánh cho người xung quanh, may mắn +15%, +30% kinh nghiệm đệ tử khi sư phụ mặc, không giao dịch, hạn dùng 7 ngày. 119 vật phẩm ghi "VPSK" và nhiều cái không có option (thuần thẩm mỹ). Có cả **cờ PK (type 28, 14 vật phẩm: Cờ xanh, đỏ, ...)** là cờ bật chế độ PK, **không áp dụng** cho chơi đơn.

**4. Sách tuyệt kỹ (type 25): sách 1 (id 1044, 1211, 1212) và sách 2 (id 1278, 1279, 1280).** Không có chỉ số cố định: khi **giám định** (`GiamDinhSach.java`) mỗi dòng "chưa giám định" (option 217) thành **một option ngẫu nhiên trong 12 loại**: HP%, Ki%, Sức đánh%, Né đòn%, Giảm sát thương (giáp)%, Chí mạng, Hồi HP/30 giây %, Hồi Ki/30 giây %, Giảm thời gian bị mù %, Sát thương chí mạng %, **Phạm vi tuyệt kỹ +%** (option 214), **Tuyệt kỹ +% sát thương** (option 216), **giá trị 1 đến 10** (hoặc 1 đến 5, vì giá trị = random(1, 10 / random(1, 2))). Số dòng: một dòng, **với "con dấu" có 20% thêm 1 đến 2 dòng**. Lấy sách: đổi **10 "cuốn sách cũ" + 1 "kìm bấm giấy"** với tỉ lệ thành công 20% (100% khi dùng con dấu). Có thể tẩy và nâng cấp sách (`TaySach`, `NangCapSachTuyetKy`, cần 10 kìm bấm giấy). **Khác biệt giữa sách 1 và sách 2: UNKNOWN** (id khác nhau, cùng mô tả "Vật phẩm hỗ trợ").

**Hệ quả so với 49 loại chỉ số của skin (mục 10.15):** các chỉ số trên đều có sẵn (ATK_PCT, HP_PCT, KI_PCT, DODGE_PCT, ARMOR_PCT, CRIT_RATE_PCT, HP_REGEN_PCT, KI_REGEN_PCT, CRIT_DMG_PCT, FLAT_KI_REGEN, GOLD/EXP...), **trừ ba loại chưa có: "Phạm vi tuyệt kỹ %", "Tuyệt kỹ + % sát thương" (số lượng mục tiêu không dùng) và "Giảm thời gian bị mù %"** (cần thêm vào `stat_type`; "bị mù" không áp dụng). **Chưa có bảng nào trong V001 đến V004 cho 4 nhóm này.**

**Đề xuất áp dụng vào BattleBall (PROPOSED, chờ người dùng duyệt):**
- Dùng đúng **4 ô hỗ trợ**: `SUPPORT_1` pet, `SUPPORT_2` giáp tập luyện, `SUPPORT_3` cờ/cánh/vũ khí, `SUPPORT_4` sách tuyệt kỹ (mỗi ô một nhóm; nhóm quyết định ô).
- **Pet:** bước đầu chỉ chọn trong 17 pet có hiệu ứng, hiệu ứng đơn giản: sau mỗi 15 giây cộng buff (+5% chí mạng hoặc +5%/+15% giáp) trong 5 đến 10 giây; pet thẩm mỹ không chỉ số. Thông số admin sửa được.
- **Giáp tập luyện:** đổi sang theo **trận**: mặc thì sức đánh giảm X% nhưng mỗi trận hoàn thành cộng điểm tích luỹ (không cần đứng yên); khi cởi ra, sức đánh +X% cho số trận bằng điểm tích luỹ. Số điểm/trận và trần do admin sửa.
- **Cờ/cánh/vũ khí:** chỉ số HP%, Ki%, Sức đánh% (3 đến 18%) làm dữ liệu giống `skin_stat` (bảng `support_item_stat`), loại bỏ cờ PK.
- **Sách tuyệt kỹ:** ngẫu nhiên khi nhận, 1 dòng, giá trị 1 đến 10 trong 12 loại đã nêu (bỏ "bị mù", thêm hai loại tuyệt kỹ); cách nhận: ghép vật liệu như NRO hoặc rơi từ boss (OPEN).
- **Quy mô dữ liệu (PROPOSED):** `support_item_template` (kind PET/TRAIN_ARMOR/FLAG_WING_WEAPON/SKILL_BOOK, ô, costume/icon, `enabled`, giá), `support_item_stat`, và bảng `character_support_item` (thể hiện của người chơi, kể cả dòng ngẫu nhiên của sách).

**OPEN:** xác nhận 4 nhóm, 4 ô; chọn tập vật phẩm cụ thể (bao nhiêu pet/cờ cánh vũ khí đưa vào; hiện NRO có 17 pet hiệu ứng, khoảng 25 vật phẩm type 11 có chỉ số); cách nhận từng nhóm (mua bằng đá quý, rơi từ boss, ghép); khác biệt sách 1 và sách 2; công thức giáp tập luyện theo trận.

### 10.22 Vật phẩm hỗ trợ vĩnh viễn: người dùng duyệt đề xuất, bảng V005 và dữ liệu V006 (2026-10-07)
**CONFIRMED (người dùng: "tôi duyệt" đề xuất ở 10.21):** mỗi nhóm một ô cố định (`SUPPORT_1` pet, `SUPPORT_2` giáp tập luyện, `SUPPORT_3` cờ/cánh/vũ khí, `SUPPORT_4` sách tuyệt kỹ); pet: chỉ lấy pet có hiệu ứng chỉ số, mỗi 15 giây cho buff 5 đến 10 giây, admin sửa số được; giáp tập luyện tính theo trận (mặc thì giảm sức đánh, mỗi trận hoàn thành cộng điểm tích luỹ, cởi ra thì tăng sức đánh cho số trận bằng điểm); cờ/cánh/vũ khí: chỉ lấy loại có chỉ số, bỏ cờ PK; sách tuyệt kỹ: ngẫu nhiên 1 dòng trong 11 loại (bỏ "bị mù").

**Số liệu thực tế khác con số tôi nêu ở 10.21 (đính chính):** có **10 pet** được đưa vào (không phải 17): trong 15 pet có mô tả hiệu ứng, 4 pet đặc biệt (Po, Shimo, Cerberus: hiệu ứng không có cơ chế; Giru: chỉ có tác dụng khi đi cùng Khủng Long Thơ Mộng) bị loại, và pet 1203 trùng tên 1202 nên bỏ. **23 vật phẩm cờ/cánh/vũ khí** (không phải 25). Với vật phẩm có nhiều phiên bản trong shop NRO tôi lấy phiên bản có Sức đánh% cao nhất; bỏ các dòng không áp dụng (Cute, may mắn, kinh nghiệm đệ tử, Ki mỗi 30 giây, hạn sử dụng).

**Bảng (file `V005__support_items_schema.sql`, PROPOSED):** `support_kind` (4 nhóm và ô), `support_item_template` (id = id vật phẩm NRO; `obtain` SHOP/CRAFT/DROP/NONE, `gem_price`), `support_item_stat` (chỉ số cố định), `support_pet_effect` (chỉ số, giá trị, `interval_s`, `duration_s`), `support_train_armor` (phạt và thưởng sức đánh %, `points_per_match`, `max_points`), `skill_book_template` (số dòng), `skill_book_pool` (11 loại chỉ số với khoảng giá trị và trọng số), `character_support_item` (đồ của nhân vật; `charge_points` cho giáp, `appraised` cho sách), `character_support_stat` (dòng ngẫu nhiên của sách), và thêm cột `character_equipment.support_instance_id`. **Cơ sở dữ liệu không tự kiểm "nhóm vật phẩm phải khớp ô"**: server phải kiểm (ví dụ pet chỉ vào `SUPPORT_1`); khóa duy nhất chỉ ngăn một món nằm ở hai ô.

**Dữ liệu (file `V006__support_items_seed.sql`, sinh bởi `BattleBall\SourceData\tools\build_support_seed_sql.py`):** 10 pet, 4 giáp tập luyện (cấp 1 đến 4: phạt/thưởng 10, 20, 30, 40%), 23 cờ/cánh/vũ khí (70 dòng chỉ số: Sức đánh, HP, Ki %, một vật phẩm thêm sát thương chí mạng 9%), 2 sách; thêm hai loại chỉ số mới vào `stat_type`: **`ULT_RANGE_PCT`** (phạm vi tuyệt kỹ) và **`ULT_DMG_PCT`** (sát thương tuyệt kỹ), đều bật; phạm vi giá trị của sách giới hạn theo trần chỉ số của BattleBall (ví dụ chí mạng 1 đến 3, hồi HP 0,1 đến 1) thay vì 1 đến 10 như NRO.

**Các số do tôi đặt mặc định (PROPOSED, admin sửa được):** giá **150 đá quý** cho pet, giáp tập luyện và cờ/cánh/vũ khí (theo mức "item trang trí vĩnh viễn" trong GDD mục 6); chu kỳ pet 15 giây; giáp tập luyện cộng **1 điểm mỗi trận**, tối đa **50 điểm**; sách tuyệt kỹ **không bán** (`obtain = CRAFT`, cách nhận OPEN) và sách 1, sách 2 tạm dùng cùng bảng chỉ số (khác biệt thật: UNKNOWN).

**Kiểm chứng (CONFIRMED):** chạy V001 đến V006 liên tiếp trên MariaDB 10.4.32 thật (XAMPP, cơ sở dữ liệu tạm đã xóa): 37 bảng, không lỗi; mặc pet vào `SUPPORT_1`, thêm dòng ngẫu nhiên cho sách chạy đúng; đặt cùng một món vào hai ô bị từ chối (lỗi 1062); chỉ số không tồn tại bị từ chối (lỗi 1452).

**OPEN:** cách nhận sách tuyệt kỹ (ghép vật liệu như NRO hay rơi từ boss); thông tin hồi chiêu của pet (mô tả NRO bị cắt); sách 1 khác sách 2 thế nào; giá riêng cho từng loại thay vì 150 chung; bảng rơi đồ.

### 10.23 Avatar nhận qua thành tích (V007) và trang quản trị admin (2026-10-07)
**1. Avatar ngoài nhóm mặc định: nhận qua thành tích (CONFIRMED, người dùng chọn phương án B).** File `V007__achievements.sql` (sinh bởi `BattleBall\SourceData\tools\build_achievement_seed_sql.py`) tạo `achievement_template` (điều kiện `LEVEL_REACH`, `WINS_TOTAL`, `FIRST_CLEAR_BOSS`; ngưỡng; skin thưởng và/hoặc đá quý; `enabled`) và `character_achievement`. **Nội dung seed là đề xuất của tôi (PROPOSED):** 25 avatar COMPONENT còn lại (Trái Đất 8, Namếc 6, Xayda 11) được gán cho các mốc **đạt cấp** từ 10 đến 100, cùng chủng tộc với nhân vật đạt cấp, avatar "VIP" ở cấp 100; ví dụ Xayda cấp 10 nhận avatar 196. Server ghi `owned_skin` với `source = 'REWARD'` khi đạt mốc (chưa viết). Admin sửa ngưỡng, skin thưởng, bật tắt. **OPEN:** có muốn thành tích kiểu khác (tổng số trận thắng, hạ boss lần đầu) cho avatar; hiện chỉ seed loại đạt cấp.

**2. Trang quản trị admin: đã tạo tại `E:\PRJNRO\BattleBallServer\admin\` (PROPOSED về công nghệ, người dùng chỉ yêu cầu tạo ở thư mục đó).** Chọn **PHP 8 thuần + PDO** (không cần cài thêm gì; XAMPP của người dùng có PHP 8.2.12; cùng công nghệ với web đăng ký đã chốt là PHP). Gồm `index.php` (định tuyến), `lib.php` (kết nối, phiên, CSRF, ghi nhật ký, tăng phiên bản, bảng sửa tổng quát), `pages.php` (các màn hình), `style.css` (sáng/tối, dùng được trên điện thoại), `tools\make_admin.php` (tạo tài khoản admin), `config.sample.php`, `.htaccess`, `.gitignore`.
- **Màn hình:** Tổng quan (phiên bản từng nhóm dữ liệu, số lượng); **Chỉ số** (bật/tắt 51 loại chỉ số + mức gợi ý tối đa); **Skin** (danh sách có lọc theo loại/tên/mã; trang sửa từng skin: loại, độ hiếm, giá đá quý, **ô chọn hành tinh cho skin hợp thể** (td/nm/xd/ANY), AI được mặc, bật/tắt, thêm/sửa/xóa chỉ số); **Loại skin**, **Độ hiếm**, **Giá theo nhóm**; **Đồ hỗ trợ** (danh sách và trang sửa: cách nhận, giá, bật/tắt; pet: chỉ số, giá trị, chu kỳ, thời lượng; giáp tập luyện: phạt/thưởng, điểm mỗi trận, tối đa; cờ/cánh/vũ khí: chỉ số; sách: số dòng); **Sách tuyệt kỹ** (bảng chỉ số ngẫu nhiên); **Thành tích**; **Avatar mặc định**; **Cân bằng** (`balance_config`, JSON); **Nhật ký** (200 thay đổi gần nhất).
- **Quy tắc ghi dữ liệu:** mọi lần lưu nằm trong **một transaction**, ghi `admin_audit_log` (trước/sau, người sửa) và **tăng `static_data_version`** của nhóm đó (STAT, SKIN, SUPPORT, ACHIEVEMENT, BALANCE) để server và client biết nạp lại; có lỗi thì hoàn tác cả lần lưu. Kiểm tra dữ liệu: loại bán được phải có độ hiếm và giá; loại không bán thì tự xóa độ hiếm/giá; skin hợp thể bắt buộc chọn hành tinh và luôn cấm AI mặc; mỗi chủng tộc đúng một avatar mặc ngay; sách có min ≤ max; vượt mức gợi ý chỉ **cảnh báo**.
- **Bảo mật:** đăng nhập bằng tài khoản `role = 'ADMIN'` của bảng `account` (mật khẩu bcrypt cost 12, như tài khoản game), chống đoán mật khẩu bằng độ trễ, phiên `HttpOnly` + `SameSite=Strict`, **token CSRF** cho mọi POST, truy vấn có tham số (chống SQL injection), thoát ký tự khi hiển thị (chống XSS), tiêu đề CSP, và **chỉ cho các địa chỉ IP trong danh sách** (mặc định chỉ máy này: `127.0.0.1`). Cấu hình kết nối qua biến môi trường `BB_DB_*` hoặc `config.php`; khi triển khai phải dùng một người dùng cơ sở dữ liệu riêng, không dùng `root`. **Chưa có:** HTTPS (cần đặt sau reverse proxy/Apache có TLS khi mở ra ngoài), phân quyền theo vai trò admin, thêm dòng mới (chỉ sửa dòng có sẵn; thêm loại skin, thêm đồ hỗ trợ bằng SQL), xem ảnh skin.
- **Cách chạy thử:** tạo cơ sở dữ liệu, chạy V001 đến V007, tạo admin bằng `php admin\tools\make_admin.php <tên> <mật khẩu>`, rồi `php -S 127.0.0.1:8080 -t admin` (đặt `BB_DB_NAME`, `BB_DB_USER`, `BB_DB_PASS`).
- **Kiểm chứng (CONFIRMED):** chạy thật trên MariaDB 10.4.32 (XAMPP) với PHP 8.2.12 và máy chủ PHP tích hợp (đã tắt, cơ sở dữ liệu tạm đã xóa): cả 17 trang trả về 200; đăng nhập đúng/sai, thiếu CSRF bị từ chối (400), chưa đăng nhập bị chuyển về trang đăng nhập (302), đăng xuất; đổi hành tinh skin 885 từ ANY sang td; skin hợp thể thiếu hành tinh bị từ chối; skin bán được thiếu giá bị từ chối, đủ giá thì lưu `rarity_status = MANUAL`; đổi loại sang NPC tự xóa giá và độ hiếm; thêm/xóa chỉ số của skin; bật/tắt chỉ số hàng loạt; sửa giá và chu kỳ pet; ràng buộc avatar mặc định, sách và khóa ngoại thành tích đều từ chối và hoàn tác; phiên bản dữ liệu tăng đúng nhóm; nhật ký ghi đúng; tên skin chứa `<script>` hiển thị đã thoát ký tự. **Chưa kiểm:** trình duyệt thật (giao diện chỉ kiểm bằng HTTP), MariaDB 10.6+, Apache có TLS, nhiều admin cùng lúc.

### 10.24 Trang admin: thêm dòng mới (2026-10-07)
Bước tiếp theo trong danh sách (sau 10.23): trang admin nay **thêm được dòng mới**, không chỉ sửa dòng có sẵn.
- **Form "Thêm dòng mới" dưới các bảng:** Loại skin (mã in hoa, 2 đến 24 ký tự), Độ hiếm, Giá theo nhóm, Thành tích, Avatar mặc định, Sách tuyệt kỹ (thêm loại chỉ số vào bảng ngẫu nhiên). Có kiểm tra định dạng mã, kiểu số, trùng khóa, khóa ngoại (ví dụ skin thưởng phải tồn tại); mỗi lần thêm ghi `admin_audit_log` (INSERT) và tăng `static_data_version`.
- **Trang "Thêm đồ hỗ trợ" (`?p=support_new&kind=...`):** thêm pet, giáp tập luyện, cờ/cánh/vũ khí hoặc sách, kèm bảng thông số riêng của nhóm (pet: chỉ số buff, giá trị, chu kỳ, thời lượng; giáp: phạt/thưởng, điểm; sách: số dòng); đồ bán trong shop bắt buộc có giá; mã trùng bị từ chối; cờ/cánh/vũ khí thêm chỉ số ở trang sửa sau khi tạo.
- **Vẫn không thêm được trên admin:** **skin mới** (mỗi skin phải có sẵn trong catalog client `CostumeCatalog.json` vì mã skin = mã costume, nên thêm skin là việc của dữ liệu client), **loại chỉ số mới** (cần cơ chế tính trong server), và xóa dòng (chỉ tắt bằng `enabled`).
- **Lỗi đã sửa trong lúc làm:** trong PHP, `"«$label»"` bị hiểu thành biến `$label»` (byte của ký tự `»` hợp lệ trong tên biến) nên các thông báo lỗi nhập liệu của trang admin thiếu tên trường; đã sửa thành `{$label}` và quét lại toàn bộ file PHP không còn chỗ tương tự (thông báo lỗi ở 10.23 trước đó bị thiếu tên trường, nay đã đúng).
- **Kiểm chứng (CONFIRMED):** trên MariaDB 10.4.32 (cơ sở dữ liệu tạm đã xóa): thêm loại skin, độ hiếm, thành tích, avatar mặc định, dòng sách, pet (kèm hiệu ứng), cờ, giáp tập luyện đều thành công; mã sai định dạng, trùng, thiếu giá, skin thưởng không tồn tại đều bị từ chối với thông báo đúng; 10 dòng INSERT trong nhật ký; phiên bản ACHIEVEMENT, SKIN, SUPPORT tăng đúng.

### 10.25 Sách tuyệt kỹ nhận bằng cách ghép nguyên liệu (V008) và trang chọn boss rơi nguyên liệu (2026-10-07)
**CONFIRMED (người dùng):** sách tuyệt kỹ **chỉ có được bằng cách ghép nguyên liệu** (như NRO); **nguyên liệu chỉ rơi từ boss với tỉ lệ thấp**; người dùng sẽ chọn loại boss rơi nguyên liệu qua trang HTML ở bước làm bảng rơi đồ tiếp theo.

**Cơ chế đọc từ source Teamobi2026 (CONFIRMED về dữ kiện, `combine\DoiSachTuyetKy.java`, `NangCapSachTuyetKy.java`, `TaySach.java`):**
- Sách có **3 phiên bản theo chủng tộc** (item `gender` 0, 1, 2): sách 1 = 1044 (Trái Đất), 1211 (Namếc), 1212 (Xayda); sách 2 = 1278, 1279, 1280. **Đính chính 10.21/10.22:** V006 chỉ nạp bản Trái Đất của hai sách, nay V008 bổ sung đủ 6 bản. Trong NRO, sách đổi ra là ngẫu nhiên một trong 3 bản (code bỏ qua chủng tộc); **BattleBall (PROPOSED) cho ra đúng bản của chủng tộc nhân vật ghép**. Khi mặc, server kiểm cột `support_item_template.race` (cột mới của V008).
- **Ghép sách 1:** 10 "Cuốn sách cũ" (id 1283) + 1 "Kìm bấm giấy" (id 1285), thành công **20%**; thất bại mất 5 cuốn sách cũ và 1 kìm. Có "Con dấu" (id 1794): **100% thành công** và **20% có thêm 1 đến 2 dòng chỉ số**.
- **Nâng cấp sách 1 lên sách 2** (đây là nguồn của sách 2; **trả lời câu "sách 1 khác sách 2 thế nào": sách 2 là bản nâng cấp của sách 1, cùng cách giám định**; mức chỉ số khác nhau thế nào vẫn UNKNOWN): cần **10 kìm bấm giấy**, **10%** thành công, **mất kìm dù thất bại, sách được giữ**.
- **Tẩy sách** (đổi lại dòng chỉ số, giới hạn 5 lần theo option 219 của NRO) **chưa đưa vào BattleBall (OPEN)**.

**V008 (`BattleBallServer\db\migrations\V008__skill_book_crafting.sql`, PROPOSED):** thêm kiểu `BOOK_MATERIAL` cho `material_template`; ba nguyên liệu **301 Cuốn sách cũ, 302 Kìm bấm giấy, 303 Con dấu** (không bán, chỉ rơi); cột `race` cho `support_item_template`; thêm bốn sách còn thiếu (Namếc/Xayda, sách 1 và sách 2); bảng **`craft_recipe`** (loại CRAFT/UPGRADE, sách đầu vào và kết quả, tỉ lệ, nguyên liệu phụ và tỉ lệ khi dùng, xác suất và số dòng thêm) và **`craft_recipe_input`** (số lượng và phần bị mất khi thất bại); 6 công thức: ba công thức ghép (theo chủng tộc) và ba công thức nâng cấp. Đã áp dụng V008 vào cơ sở dữ liệu `battleball` của máy bạn. **Admin có hai màn hình mới:** "Công thức ghép" (sửa tỉ lệ, nguyên liệu phụ) và "Nguyên liệu ghép" (số lượng, phần mất khi thất bại), và ô "Giới hạn chủng tộc" cho đồ hỗ trợ.

**Trang HTML chọn boss: `BattleBallServer\admin\drop_picker.html` (mở trực tiếp bằng trình duyệt hoặc qua trang admin ở địa chỉ `/drop_picker.html`, mục "Chọn boss rơi" trên thanh menu; tự chứa).** Bảng vật phẩm × 8 loại boss lấy từ GDD mục 7 (mob thường, mob elite, boss vùng sớm/giữa/cuối, mini-boss, boss cuối phó bản, raid boss, kèm thời gian hạ và HP). Mỗi ô: đánh dấu rơi, tỉ lệ rơi %, số lượng nhỏ nhất đến lớn nhất; hiển thị ước lượng "bao nhiêu lần hạ boss loại đó để đủ nguyên liệu cho một lần thử". **Các số đã điền sẵn là đề xuất của tôi (PROPOSED, thấp):** cuốn sách cũ rơi từ mob elite (1%) đến raid boss (12%, 3 đến 5 cái), kìm bấm giấy từ boss vùng sớm (2%) đến raid (10%), con dấu chỉ từ boss cuối phó bản (2%) và raid boss (5%). Có thêm nhóm tuỳ chọn (mảnh đồ, đá nâng cấp, đá chỉ số, đá pha lê, mảnh bông tai) để trống nếu muốn quyết định sau. Kết quả xuất ra JSON hoặc CSV (có ô sao chép, nút tải về) để dán lại cho tôi. **Bước kế tiếp:** dựa vào lựa chọn này, thiết kế bảng rơi đồ (`drop_rule`), cần thêm bảng vùng/mob/boss.

**OPEN:** lựa chọn của người dùng trên trang HTML; bảng rơi đồ và mẫu vùng/mob/boss; tẩy sách; mức chỉ số sách 2 khác sách 1 hay không; cách người chơi thực hiện ghép (giao diện, ở đâu).

### 10.26 Trang chọn nhóm boss cho từng phó bản, kèm hình IDLE (2026-10-07)
Người dùng: **mỗi phó bản có một nhóm boss khác nhau** (CONFIRMED), cần trang để chọn nhóm và **có hình boss ở trạng thái IDLE để phân biệt**.
- **Trang:** `BattleBallServer\admin\boss_group_picker.html` (tự chứa, mở trực tiếp hoặc qua admin ở `/boss_group_picker.html`, mục "Nhóm boss phó bản" trên menu); sinh bởi `BattleBall\SourceData\tools\build_boss_picker.py` (mẫu giao diện `boss_picker_template.html` cùng thư mục; chạy lại được).
- **Dữ liệu (CONFIRMED, đọc từ code boss của NRO, thư mục chỉ đọc):** **31 nhóm, 139 boss**, mỗi nhóm là một thư mục code boss của NRO, đọc `BossData` (tên, giới tính, outfit đầu/thân/chân, ATK, HP của NRO) từ `BossesData.java` và từng lớp boss (kể cả `boss_con_duong_ran_doc`). Hình boss ghép từ phần đầu, thân, chân của NRO ở tư thế đứng yên, **cùng cách tính với trang duyệt skin**, nên khớp với cách client vẽ; cả 139 boss đều có hình. Nhóm chia thẻ: **Phó bản NRO** (Doanh trại độc nhãn, Khí gas huỷ diệt, Bản đồ kho báu, Con đường rắn độc), **Nhóm boss** (Nappa, Cell, Android, Fide, Broly, Majin Bư, Yardrat, Bojack, Tiểu đội sát thủ, ...), **Cần hệ thống khác** (Đại hội võ thuật, Luyện tập), **Sự kiện** (ẩn mặc định). Một số tên trong code là biến hoặc để trống nên trang hiển thị tên lớp Java kèm chú thích "tên trong code".
- **Cách dùng:** cột trái là danh sách phó bản (mặc định 6, thêm/xóa/đổi tên, chọn cấp vùng sớm/giữa/cuối); chọn một phó bản rồi bấm "Chọn cho ..." ở nhóm boss. **Mỗi nhóm chỉ dùng cho một phó bản** (gán sang phó bản khác thì phó bản cũ mất nhóm); trong nhóm đã chọn có thể đặt vai trò từng boss (thường, mini-boss cuối tầng, boss cuối phó bản). Kết quả xuất JSON hoặc CSV (có sao chép và tải về), lựa chọn tự lưu trong trình duyệt. Đã kiểm chứng trong trình duyệt của app: hình hiển thị, gán nhóm, chuyển nhóm, đặt vai trò, xuất JSON và CSV đều đúng.
- **Giới hạn:** số phó bản (6) và cấp vùng của từng phó bản chỉ là mặc định để người dùng chỉnh (UNKNOWN: GDD chưa có danh sách phó bản); các boss "Rival", nhân bản của người chơi không có hình riêng nên nằm ở nhóm "Cần hệ thống khác"; HP, ATK của NRO chỉ để tham khảo vì BattleBall tính lại theo mục 7 GDD.
- **OPEN:** lựa chọn của người dùng; sau đó làm bảng mẫu phó bản (`dungeon_template`, `dungeon_boss`) và bảng rơi đồ (kết hợp với `drop_picker.html` ở mục 10.25).

**Cập nhật 10.26 (2026-10-07, theo yêu cầu người dùng):** thêm nhóm **"Hóa khỉ của Xayda (khỉ đột cấp 1 đến 7)"** vào trang chọn nhóm boss: 7 "boss" là 7 cấp của kỹ năng Biến khỉ, hình ghép từ các part ở mục 10.13 (đầu theo cấp 192, 195, 196, 199, 197, 200, 198; thân 193; chân 194). Trang nay có **32 nhóm, 146 boss**; cả 7 hình khỉ hiển thị đúng (từ tóc đen đến tóc bạc). Đây là dạng biến hình của skill chứ không phải boss trong NRO, nên **chỉ số (HP, ATK, chiêu) do BattleBall tự định nghĩa** khi người dùng chọn nhóm này cho một phó bản (OPEN: chiêu và cách dùng của các khỉ này trong trận).

### 10.27 Lựa chọn nhóm boss cho 13 phó bản và bảng phó bản/boss (V009), 2026-10-07
**CONFIRMED (người dùng gửi kết quả từ `boss_group_picker.html`; lưu ở `BattleBall\SourceData\tables\seed\dungeon_choice_2026-10-07.json`):** 13 phó bản, mỗi phó bản một nhóm boss khác nhau:
| # | Phó bản | Cấp vùng | Nhóm boss |
|---|---|---|---|
| 1 | Luyện tập | sớm | Luyện tập: Karin, Thượng đế, Tổ sư Kaio, Whis, Yajirô, Thần Vũ Trụ, Mr.PôPô, Khỉ Bubbles, Tàu Pảy Pảy (9 boss; người dùng chọn sau, cùng ngày) |
| 2 | Thi đấu | sớm | Đại hội võ thuật (12 boss) |
| 3 | **Farm Sách tuyệt kỹ** | giữa | Hóa khỉ của Xayda, khỉ đột cấp 1 đến 7 (7 boss) |
| 4 | Phó bản 4 | giữa | Con đường rắn độc (3) |
| 5 | Phó bản 5 | cuối | Khí gas huỷ diệt (2) |
| 6 | Phó bản 6 | sớm | Cell và Xên con (12) |
| 7 | Phó bản 7 | cuối | Cumber (2) |
| 8 | Phó bản 8 | cuối | Fide (Frieza) (3) |
| 9 | Phó bản 9 | cuối | Fide Vàng (2) |
| 10 | Phó bản 10 | giữa | Majin Bư nhóm 12h (6) |
| 11 | Phó bản 11 | cuối | Majin Bư nhóm 14h (5) |
| 12 | Phó bản 12 | sớm | Nhóm Nappa (3) |
| 13 | Phó bản 13 | giữa | Tiểu đội sát thủ (5) |
- **Gợi ý từ tên phó bản 3 (INFERRED, chưa được người dùng nói rõ):** "Farm Sách tuyệt kỹ" cho thấy phó bản này là nơi rơi nguyên liệu ghép sách (cuốn sách cũ, kìm bấm giấy, con dấu: mục 10.25). Cần xác nhận khi làm bảng rơi đồ.
- **Vai trò boss:** người dùng để mọi boss là NORMAL; chưa đánh dấu mini-boss hay boss cuối phó bản (OPEN: GDD mục 7.3 cần mini-boss cuối tầng và boss cuối phó bản).
- **Số tên đặt tạm:** "Phó bản 4 đến 13" là tên mặc định, người dùng đổi trên admin; cấp vùng 1 và 2 giữ mặc định của trang chọn (người dùng có thể đã đổi trong trang).

**V009 (`BattleBallServer\db\migrations\V009__dungeons.sql`, sinh bởi `BattleBall\SourceData\tools\build_dungeon_seed_sql.py` từ `boss_groups.json` và file lựa chọn; PROPOSED về cấu trúc):** `boss_group` (32 nhóm), `boss_template` (146 boss: tên, chủng tộc, part đầu/thân/chân để vẽ như skin, HP và sát thương của NRO chỉ để tham khảo), `dungeon_template` (13 phó bản, `UNIQUE(group_key)` bảo đảm mỗi nhóm boss chỉ dùng cho một phó bản), `dungeon_boss` (62 dòng, vai trò NORMAL/MINI/FINAL). **Chỉ số boss của BattleBall (HP, ATK, giáp, chiêu) chưa lưu ở đây (OPEN):** sẽ tính từ cấp vùng và vai trò theo GDD mục 7. Đã chạy V001 đến V009 trên MariaDB 10.4.32 thật và áp dụng V009 vào cơ sở dữ liệu `battleball`; admin có thêm màn hình "Phó bản" (đổi tên, cấp vùng, bật/tắt) và "Boss phó bản" (đặt vai trò); nhóm boss đã dùng cho phó bản khác bị chặn bằng khóa duy nhất.
- **Cập nhật:** người dùng chọn nhóm "Luyện tập (Karin, Thượng đế, ...)" cho phó bản 1; đã cập nhật file lựa chọn, V009 (nay 71 liên kết boss) và cơ sở dữ liệu `battleball` (9 boss, phiên bản DUNGEON tăng, ghi nhật ký). Nhóm này thuộc thẻ "Cần hệ thống khác" (trong NRO là các NPC huấn luyện), nên **cách chúng đánh trong trận là OPEN**.
- **OPEN:** boss nào là mini-boss/boss cuối; chỉ số và chiêu của từng boss; bảng rơi đồ (cần quyết nguyên liệu rơi ở phó bản nào, theo `drop_picker.html`).

**Cập nhật 10.27 (2026-10-07): quái nhỏ khác boss (CONFIRMED, người dùng).** "Tất cả đều là boss; **quái nhỏ ra ồ ạt khi chiến đấu là nhóm Sự kiện Halloween và đám Xên con**." Vì vậy:
- Nhóm Halloween (Bí ma, Dơi, Ma trơi) đổi thành nhóm **quái nhỏ** (`boss_group.tag = 'MOB'`, nhãn "Quái nhỏ Halloween (Bí ma, Dơi, Ma trơi)"), không còn thuộc "Sự kiện". **Chưa gán cho phó bản nào** (OPEN: quái nhỏ này ra ở những trận nào, mật độ, tần suất).
- Bảy "Xên con 1 đến 7" của phó bản 6 (nhóm Cell) có vai trò mới **`MINION`** (quái nhỏ ra ồ ạt trong trận) thay cho NORMAL; vai trò của `dungeon_boss` nay có 4 giá trị: NORMAL, MINI, FINAL, MINION. Phó bản 6 còn 5 boss thật (Xên Hoàn Thiện, Siêu Bọ Hung, Xên bọ hung, hai Xên hoàn thiện).
- Quái nhỏ theo GDD mục 7.1 (mob thường và mob elite: HP thấp, hạ trong 0,4 s đến 2 s), nên chỉ số của chúng tính theo bảng mob chứ không phải bảng boss (OPEN: chọn mob thường hay elite).
- Cài đặt: file `V010__minions_and_mob_groups.sql` (idempotent) và V009 được sinh lại có sẵn các giá trị mới; đã áp dụng V010 vào cơ sở dữ liệu `battleball` (7 dòng MINION, phiên bản DUNGEON tăng) và thử cài mới V001 đến V010 trên bản tạm (đã xóa). Trang chọn nhóm boss có thêm thẻ "Quái nhỏ (ra ồ ạt)", bộ lọc "Quái nhỏ" và vai trò "Quái nhỏ ra ồ ạt" (Xên con và nhóm Halloween được gợi ý mặc định); admin "Boss phó bản" có thêm giá trị MINION.

**Cập nhật 10.27 (2026-10-07): Xên con là mob elite (CONFIRMED, người dùng: "xên con cho thành nhóm Mob elite").** `dungeon_boss` có cột mới **`mob_tier`** (`MOB` hoặc `ELITE`, chỉ dùng cho vai trò MINION; file `V011__minion_tier.sql`, idempotent, đã áp vào cơ sở dữ liệu `battleball` và thử cài mới V001 đến V011). Bảy "Xên con 1 đến 7" có `mob_tier = ELITE`, nghĩa là **chỉ số theo bảng mob elite của GDD mục 7.1** (hạ trong 2 giây, ATK 8% đến 10% `Max_ATK_Total`, giáp 15% đến 20%) và **rơi theo cột "Mob elite"** ở trang chọn rơi đồ. Nhóm Halloween chưa phân loại mob thường hay elite (OPEN). Admin "Boss phó bản" có thêm cột "Loại quái nhỏ" và kiểm tra: chỉ MINION mới có loại, mọi MINION phải có loại. Trang chọn nhóm boss có hai vai trò mới "Quái nhỏ thường" và "Quái nhỏ elite" (Xên con mặc định elite).
**Rơi đồ của quái nhỏ (PROPOSED, chờ người dùng chốt):** chỉ rơi vàng và (mob elite) tỉ lệ rất thấp vật liệu xếp chồng như đá nâng cấp hoặc đá chỉ số; không rơi trang bị (túi 2.000 món), không rơi nguyên liệu sách (chỉ boss). Rủi ro cày vô hạn khi quái nhỏ sinh liên tục trong trận boss: cần giới hạn số con mỗi trận (OPEN).

**Cập nhật 10.27 (2026-10-07): nhóm Halloween là mob thường (CONFIRMED, người dùng: "mob thường").** `boss_group` có cột **`mob_tier`** (file `V012__mob_group_tier.sql`, idempotent, đã áp vào `battleball` và thử cài mới V001 đến V012): nhóm "Quái nhỏ Halloween (Bí ma, Dơi, Ma trơi)" = `MOB` (chỉ số theo bảng mob thường GDD mục 7.1: hạ trong 0,4 giây, ATK 3% đến 5%, giáp 5% đến 10%; rơi theo cột "Mob thường"). Hai nhóm quái nhỏ nay: Halloween (mob thường, chưa gán phó bản) và Xên con (mob elite, phó bản 6). Admin có thêm màn hình "Nhóm boss" (sửa tên, loại quái nhỏ, bật/tắt).

### 10.28 Bảng rơi đồ (V013) theo đề nghị đã duyệt, 2026-10-07
**CONFIRMED (người dùng: "làm theo đề nghị"):** quái nhỏ chỉ rơi vàng, mob elite (kể cả Xên con) thêm tỉ lệ thấp đá; nguyên liệu sách chỉ boss rơi, nhiều nhất ở phó bản 3 "Farm Sách tuyệt kỹ"; giới hạn Xên con mỗi trận; boss cuối mỗi phó bản là boss cuối danh sách. **Các con số cụ thể là PROPOSED, sửa được trên admin.**

**File `BattleBallServer\db\migrations\V013__drop_rules.sql` (PROPOSED):**
- **`drop_rule`** (nguồn, cấp vùng, nguyên liệu, tỉ lệ %, số lượng nhỏ nhất đến lớn nhất; khóa duy nhất theo nguồn + cấp + nguyên liệu). Nguồn của một lần hạ: boss vai trò NORMAL → `BOSS` theo cấp vùng của phó bản; MINI → `MINI`; FINAL → `FINAL`; quái nhỏ → `MOB` hoặc `ELITE` theo `mob_tier`.
- **Nguyên liệu sách (301 cuốn sách cũ, 302 kìm bấm giấy, 303 con dấu), chỉ boss:** cuốn sách cũ: boss sớm/giữa/cuối 4%/5%/6% (1 đến 2, 1 đến 2, 1 đến 3 cái), mini-boss 5% (1 đến 2), boss cuối 10% (2 đến 4); kìm bấm giấy: 2%/3%/4% (1 cái), mini-boss 3%, boss cuối 8% (1 đến 2); con dấu: chỉ boss cuối 2% (1 cái). **Mob elite:** 0,5% đá nâng cấp và 0,5% đá chỉ số (xếp chồng, không chiếm ô túi). **Mob thường: không rơi vật liệu** (chỉ vàng).
- **`dungeon_drop_mult`:** phó bản 3 nhân **3** cho cả ba nguyên liệu sách (phó bản khác nhân 1, tỉ lệ sau khi nhân tối đa 100%).
- **Vàng** nằm ở `balance_config`: `gold_split` = 40% vàng của trận chia cho quái nhỏ, 60% cho boss; mob elite nhiều gấp 5 lần mob thường. `minion_cap_per_match` = **20** Xên con mỗi trận (chống cày vô hạn). Số vàng cụ thể mỗi con chưa tính được vì chưa biết số quái mỗi trận (OPEN).
- **Boss cuối (vai trò FINAL) do tôi đề xuất, đánh dấu `updated_by = 'proposal'`:** boss cuối cùng của mỗi phó bản theo thứ tự trong dữ liệu NRO. **Có vài chỗ nhìn không hợp lý, cần người dùng chỉnh trên admin:** phó bản 1 "Luyện tập" → Yajirô, phó bản 2 "Thi đấu" → Yamcha, phó bản 4 → SAIBAMEN (rắn độc, trong khi có Cađích, Nađíc), phó bản 10 → Ya côn. Mini-boss chưa đánh dấu (OPEN).
- **Ước lượng độ khó nhận sách (không phải số đo, giả sử mỗi trận hạ mỗi boss một lần):** ở phó bản 3 (6 boss giữa + 1 boss cuối) trung bình mỗi trận được khoảng **2,25 cuốn sách cũ** và **0,9 kìm bấm giấy**; ghép thành công 20% nên trung bình 5 lần thử tốn khoảng 30 cuốn và 5 kìm, tức khoảng **13 trận** cho một cuốn sách 1 (không dùng con dấu), và khoảng **40 trận** nếu chỉ chơi phó bản khác (hệ số 1). Nâng sách 1 lên sách 2 cần 10 kìm, 10% thành công, tức khoảng 100 kìm (khoảng 110 trận ở phó bản 3).
- **Không có trong V013 (OPEN):** mảnh đồ (hiện chỉ mua bằng vàng, GDD nói có rơi từ boss nhưng chưa đặt tỉ lệ), đá pha lê, mảnh bông tai, rương thưởng phó bản, đá quý theo GDD mục 6 (nguồn đá quý là phó bản giới hạn thời gian, boss cuối lần đầu, raid boss: chưa có bảng riêng).
- **Admin:** màn hình mới "Bảng rơi đồ" (sửa tỉ lệ, số lượng, bật tắt, thêm dòng) và "Hệ số rơi phó bản" (sửa, thêm); kiểm tra tỉ lệ từ 0 đến 100 và số lượng nhỏ nhất không lớn hơn lớn nhất.
- **Kiểm chứng (CONFIRMED):** cài mới V001 đến V013 (13 file) trên MariaDB 10.4.32 thật (bản tạm đã xóa); 13 phó bản có 13 boss cuối; thêm dòng, trùng, tỉ lệ 150 bị từ chối, hệ số đều đúng; V013 đã áp vào `battleball`.

### 10.29 Chỉ số quái theo loại và cấp vùng (V014), người dùng giao "tự đề xuất", 2026-10-07
Trong ba việc đề xuất (mảnh đồ rơi từ boss, nguồn đá quý, chỉ số boss), tôi chọn **chỉ số quái** vì không có nó thì không dựng được trận; hai việc còn lại vẫn OPEN.
- **File `V014__monster_stats.sql` (PROPOSED, sinh bởi `BattleBall\SourceData\tools\build_monster_stats_sql.py` dùng hằng số của `balance_model.py`; chạy một lần, không phải idempotent):** bảng **`monster_stat_class`** (5 loại MOB, ELITE, BOSS, MINI, FINAL × 3 cấp vùng = 15 dòng: thời gian hạ, HP, ATK % nhỏ nhất và lớn nhất, giáp % nhỏ nhất và lớn nhất, giãn cách chiêu), hai cột **`boss_template.hp_mult` và `atk_mult`** (mặc định 1, chỉnh từng boss), và view **`v_dungeon_monster`** (loại quái lấy từ vai trò: NORMAL → BOSS, MINI, FINAL, MINION → `mob_tier`; cấp vùng lấy từ phó bản) cho ra HP, ATK mỗi đòn (`Max_ATK_Total` = 500.000) và giáp cuối cùng của 71 quái trong 13 phó bản.
- **HP đúng công thức GDD mục 7 (CONFIRMED trùng bảng GDD):** HP = thời gian hạ × sức mạnh đội dự kiến của cấp vùng × `Team_DPS_Max` (866.250) × (1 − giáp). Ví dụ: mob thường 64.103 / 144.231 / 272.436 (sớm/giữa/cuối); mob elite 285.863 / 643.191 / 1.214.916; boss 6.042.094 / 19.295.719 / 49.480.200; mini-boss giữa 10.524.938; boss cuối cuối 56.880.141. Thời gian hạ: mob 0,4 s, elite 2 s, boss 45/75/120 s, mini-boss 40 s, boss cuối 150 s. Giãn cách chiêu: 1,3 / 1,8 / 2,2 s theo cấp vùng (GDD 7.5), quái nhỏ không có.
- **Phần tôi đề xuất thêm (PROPOSED):** GDD chỉ cho mini-boss (vùng giữa) và boss cuối (vùng cuối) một dòng. Với các cấp vùng khác, **HP tính theo công thức** và **ATK giữ đúng tỉ lệ GDD so với boss thường cùng cấp vùng** (mini-boss = boss thường × khoảng 0,86 đến 0,89, boss cuối = boss thường × khoảng 1,08 đến 1,10), nên khớp GDD ở vùng giữa (mini-boss) và vùng cuối (boss cuối). Ví dụ boss cuối vùng sớm: HP 13,4 triệu, ATK 22% đến 30%; mini-boss vùng sớm HP 4,7 triệu. **Giáp mini-boss (30–35%) và boss cuối (45–52%) giữ như GDD ở mọi cấp vùng.**
- **Chưa có (OPEN):** chiêu cụ thể của từng boss (mới có chung `boss_actions` của `balance_config`); raid boss (chưa có phó bản raid); số boss phải hạ mỗi trận (phó bản "Thi đấu" 12 boss vùng sớm × 45 s ≈ 9 phút chỉ để đánh, cần xác nhận có phải hạ hết hay chọn một phần); vàng cụ thể mỗi quái; cách sinh quái nhỏ (mật độ, thời điểm).
- **Admin (kiểm chứng trong trình duyệt bằng HTTP):** màn hình mới "Quái phó bản" (xem kết quả cuối cùng theo từng phó bản), "Chỉ số quái" (sửa 15 dòng, có kiểm tra HP ≥ 1, nhỏ nhất ≤ lớn nhất, giáp < 100), "Hệ số boss" (sửa HP và ATK nhân của từng boss, hệ số phải > 0). Đã cài mới V001 đến V014 trên MariaDB 10.4.32 thật (bản tạm đã xóa) và áp V014 vào `battleball`.

**Cập nhật 10.29 (2026-10-07): số boss mỗi trận (người dùng: "tự đề xuất" → PROPOSED, chờ duyệt).** File `V015__bosses_per_match.sql` (idempotent; đã áp vào `battleball`) thêm cột **`dungeon_template.bosses_per_match`** (mặc định **5**, sửa được ở màn hình admin "Phó bản"). **Luật (PROPOSED):** mỗi trận luôn có **boss cuối** (và mini-boss nếu có); các boss còn lại **bốc ngẫu nhiên không lặp từ boss thường** cho đủ số; phó bản ít boss hơn thì dùng hết; quái nhỏ (MINION) không tính. Kết quả: phó bản 1 (9 boss), 2 (12), 3 (7), 10 (6) chỉ gặp 5 boss mỗi trận (4 thường ngẫu nhiên và 1 boss cuối); các phó bản còn lại (2 đến 5 boss) gặp hết. Trận "Thi đấu" dài khoảng 4 boss × 45 s + 1 boss cuối ≈ 5 phút thay vì 9 phút.
**Đính chính ước lượng nhận sách ở 10.28 (cùng giả sử mỗi boss hạ một lần, nay chỉ 4 boss thường + 1 boss cuối ở phó bản 3):** khoảng **1,8 cuốn sách cũ** và **0,72 kìm bấm giấy** mỗi trận ⇒ khoảng **17 trận** cho một sách 1 (30 cuốn, 5 kìm theo kỳ vọng ghép 20%), khoảng **50 trận** nếu chơi phó bản khác (hệ số 1); nâng sách 1 lên sách 2 (khoảng 100 kìm) cần khoảng **140 trận** ở phó bản 3. Nếu thấy lâu, tăng hệ số phó bản 3 hoặc tỉ lệ rơi ở màn hình "Hệ số rơi phó bản".

### 10.30 Rơi mảnh đồ, đá và mảnh bông tai (V016), người dùng giao "tự đề xuất cho #101 201 202 203 900", 2026-10-07
**PROPOSED (file `V016__drops_fragments_stones_potara.sql`, đã áp vào `battleball`, thử cài mới V001 đến V016 trên bản tạm; mọi số sửa được ở màn hình admin "Bảng rơi đồ"):** chỉ boss rơi (quái nhỏ giữ như 10.28).
| Vật phẩm | Boss thường (sớm / giữa / cuối) | Mini-boss | Boss cuối |
|---|---|---|---|
| Mảnh đồ #101 đến 105 (mỗi loại Áo, Quần, Găng, Giày, Nhẫn cùng tỉ lệ) | 1,0% / 1,2% / 1,4% | 1,5% | 3% (1 đến 2 mảnh) |
| Đá nâng cấp #201 | 0,5% / 0,7% / 1,0% | 1,5% | 3% |
| Đá chỉ số #202 | 0,7% / 1,0% / 1,3% | 2,0% | 4% |
| Đá pha lê #203 (siêu hiếm) | không | không | 0,10% / 0,20% / 0,40% (sớm / giữa / cuối) |
| Mảnh bông tai Potara #900 | không | không | **chỉ boss cuối phó bản 11 (Majin Bư 14h)**: 25%, 1 đến 3 mảnh |
- **Căn chỉnh theo kinh tế (kiểm tra bằng truy vấn trên cơ sở dữ liệu):** mỗi trận (4 boss thường + 1 boss cuối) ở phó bản 11 (vùng cuối) rơi trung bình khoảng 0,10 mảnh mỗi loại (0,50 mảnh tổng), 0,07 đá nâng cấp, 0,09 đá chỉ số, 0,004 đá pha lê, 0,5 mảnh bông tai. Quy đổi: mảnh ≈ 0,30 và đá ≈ 0,20 "trận thắng vàng" (mảnh 0,6 trận thắng, đá nâng cấp 1,5, đá chỉ số 1,0) ⇒ **khoảng 0,5 trận thắng vàng mỗi trận**, nghĩa là rơi đồ bù cỡ một nửa thu nhập vàng nhưng không thay thế được việc mua. Muốn rơi ít hơn, giảm tỉ lệ ở màn hình admin.
- **Thời gian nhận (kỳ vọng, giả sử hạ mỗi boss một lần mỗi trận):** một món đủ 10 mảnh của một ô (một loại mảnh) ≈ 100 trận (vùng cuối); đá pha lê ≈ 250 trận (cách còn lại: đổi 500 đá quý); mảnh bông tai ≈ 200 trận cho 100 mảnh (cách còn lại: mua 15.000 vàng mỗi mảnh, tối đa 20 mỗi ngày), nên mua nhanh hơn rất nhiều; phó bản 3 nhận mảnh đồ ít hơn một chút (vùng giữa).
- **Thay đổi cấu trúc:** `material_template` có thêm kiểu `POTARA_SHARD` và bản ghi **#900 "Mảnh bông tai Potara"** (mua 15.000 vàng); **mảnh này server cộng vào `account.potara_fragments` (cấp tài khoản) chứ không vào kho vật liệu của nhân vật**, và ngừng rơi khi tài khoản đã có bông tai (luật đã chốt ở mục 3.4). `drop_rule` có thêm cột **`dungeon_id`** (rỗng = mọi phó bản; khóa duy nhất gồm cả cột này) để giới hạn một dòng cho một phó bản; admin có ô "Chỉ phó bản".
- **Chưa rơi (OPEN):** mảnh đồ và đá không rơi từ quái nhỏ (giữ quyết định 10.28; mob elite 0,5% đá); đá quý chưa có nguồn (GDD: phó bản giới hạn thời gian, lần đầu hạ boss cuối, raid boss); rương thưởng phó bản của GDD mục 8 chưa làm.

### 10.31 Nguồn đá quý (V017), người dùng duyệt "làm", 2026-10-07
**File `V017__gem_rewards.sql` (PROPOSED theo GDD mục 6; đã áp vào `battleball`, thử cài mới V001 đến V017):**
- **Bảng `gem_reward_rule`** (nguồn, phó bản hoặc mọi phó bản, số đá quý, giới hạn mỗi ngày, bật/tắt): **lần đầu hạ boss cuối (thắng trận) = 20 đá quý**, một lần cho mỗi nhân vật và mỗi phó bản; **phó bản có giới hạn thời gian hoàn thành trong thời gian = 10 đá quý, tối đa 5 lần mỗi ngày** (theo từng nhân vật, ngày bắt đầu 00:00 giờ Việt Nam); **raid boss lần đầu 100, các lần sau 10, tối đa 3 lần mỗi ngày: đang TẮT vì chưa có phó bản raid.** Đá quý không mua bằng vàng, quái không rơi (giữ quyết định GDD).
- **Cột `dungeon_template.time_limit_s`** (rỗng = không giới hạn): phó bản 2 **"Thi đấu" = 480 giây (8 phút)** (đề xuất: 4 boss thường 45 s + boss cuối 150 s ≈ 330 s đánh). Quy tắc chỉ áp dụng cho phó bản có giới hạn thời gian; thêm dòng với phó bản cụ thể để đổi mức thưởng riêng.
- **Bảng người chơi mới:** `character_first_clear(character_id, dungeon_id)` (khóa chính chặn nhận trùng) và `character_daily_counter(character_id, counter_key, day_key, cnt)` (đếm số lần nhận mỗi ngày). Server ghi `currency_ledger` với lý do FIRST_CLEAR, TIMED_CLEAR, RAID (chưa viết).
- **Thu nhập (ước lượng):** lần đầu hạ boss cuối cả 13 phó bản = **260 đá quý mỗi nhân vật**; phó bản giới hạn thời gian tối đa **50 đá quý mỗi ngày** (5 lần). Với 50 đá quý mỗi ngày: một skin 300 đá quý mất khoảng 6 ngày, một đá pha lê (500 đá quý) khoảng 10 ngày. Con số 30 đá quý mỗi ngày ở 10.9 là bản cũ (chỉ tính một phần nguồn).
- **Admin:** màn hình mới "Nguồn đá quý" (sửa số đá quý, giới hạn, bật/tắt, thêm dòng cho phó bản riêng) và cột "Giới hạn thời gian (giây)" ở "Phó bản". Đã kiểm: thêm dòng, trùng nguồn, số đá 0 bị từ chối; chặn nhận lần đầu trùng bằng khóa chính.
- **OPEN:** phó bản giới hạn thời gian nào khác ngoài "Thi đấu"; cách tính thời gian (từ lúc vào trận đến khi hạ boss cuối); raid boss; rương thưởng phó bản; quy đổi thưởng bù khi thua (hiện không có).

### 10.32 Mã vật phẩm của đá quý: #674 (CONFIRMED, người dùng, 2026-10-07)
Người dùng: **"ID: #674 là id của đá quý".** Trong NRO, vật phẩm #674 là **"Đá ngũ sắc"** (`TYPE` 27, icon 6467, mô tả "loại đá quý hiếm nhất ... dùng để đục lỗ gắn Sao Pha Lê"), icon có sẵn ở `BattleBall\SourceData\data\icon\x2\6467.png`. File `V018__gem_item_id.sql` ghi **`balance_config.gem_item` = {item_id 674, tên "Đá quý", icon 6467}**; client và giao thức dùng mã này để gọi tên và vẽ tiền tệ đá quý (nhãn giá skin, thông báo phần thưởng). **Số dư vẫn nằm ở `character_wallet.gems`** (đá quý không phải `item_instance`). Đã áp vào cơ sở dữ liệu `battleball` và thử cài mới V001 đến V018.
Lưu ý (INFERRED): trong NRO, #674 cũng là vật liệu đục lỗ gắn Sao Pha Lê, tên gần giống "đá pha lê" của GDD (vật liệu #203 của BattleBall, khác đá quý); hai thứ này **khác nhau** trong BattleBall (#203 là vật liệu siêu hiếm đổi bằng 500 đá quý). Mã vàng tương ứng (NRO có 76, 188, 189, 190) chưa chốt: OPEN.

**Cập nhật 10.32 (2026-10-07): mã vàng #190 (CONFIRMED, người dùng: "#190 đi").** NRO #190 là "Vàng" (`TYPE` 9, icon 930, file `BattleBall\SourceData\data\icon\x2\930.png`). File `V019__gold_item_id.sql` ghi `balance_config.gold_item` = {item_id 190, tên "Vàng", icon 930}; số dư vẫn ở `character_wallet.gold`. Người dùng cũng **xác nhận (CONFIRMED)** rằng đá quý (#674, tiền tệ) và đá pha lê (#203, vật liệu siêu hiếm) là hai thứ khác nhau. Đã áp vào `battleball` và thử cài mới V001 đến V019.

### 10.33 Bảng ghi trận, nhật ký đồ và sự kiện bất thường (V020), người dùng giao "tự đề xuất", 2026-10-07
**PROPOSED (file `V020__match_runs_and_logs.sql`; đã áp vào `battleball`, cài mới V001 đến V020 không lỗi, thử luồng thắng trận bằng SQL):** phần dữ liệu người chơi còn thiếu theo mục 3.9 và 3.10, cần để server trả thưởng đúng một lần.
- **`match_run`** (một trận của một phó bản): tài khoản, phó bản, nhân vật điều khiển, nhân vật phụ khi hợp thể, trạng thái (ACTIVE, WON, LOST, ABANDONED, EXPIRED), thời điểm, thời lượng, số lần hồi sinh (tối đa 2 theo luật đã chốt), vàng và đá quý nhận được, cờ **`reward_applied`**. **`session_token_hash` duy nhất** (token dùng một lần, băm SHA-256) nên gửi "kết thúc trận" hai lần không trả thưởng hai lần; cờ `reward_applied` đặt cùng transaction với việc cộng ví, EXP và đồ rơi (thử: cập nhật lần hai không có tác dụng). 
- **`match_run_boss`** (các boss bốc cho trận theo luật V015, vai trò, lúc hạ), **`match_run_character`** (EXP và cấp trước/sau của từng nhân vật; EXP tính riêng từng nhân vật như đã chốt).
- **`item_log`** (mọi biến động đồ và vật liệu: rơi, mua, ghép mảnh, ghép sách, nâng cấp, bán, hủy, thưởng, mất vì túi đầy) và **`anomaly_log`** (thời gian trận quá ngắn, hạ boss quá nhanh, thưởng mỗi giờ vượt ngưỡng...; **chỉ ghi, không chặn**, có cờ "đã xem"). `anomaly_log` đúng quyết định chống gian lận đã chốt (chỉ ghi log và xử lý thủ công).
- **Không làm (OPEN):** bản đồ nhỏ/địa hình đã khám phá theo tài khoản (mục 2 từng chốt; hiện trò chơi chia theo phó bản, không có bản đồ thế giới: cần người dùng xác nhận còn dùng không); `CurrencyLedger` đã có (V003); bảng tiến độ "vùng, boss đã hạ" đã được thay bằng `character_first_clear` (V017) và `match_run`.
- **Admin:** hai màn hình mới "Các trận" (200 trận mới nhất, chỉ xem) và "Bất thường" (đánh dấu đã xem).

### 10.34 Bảng thử đăng ký web (V023), 2026-10-07
**CONFIRMED (file `V023__web_register_attempt.sql`; đã áp vào `battleball`):** bảng `web_register_attempt` (ip, success, created_at) dùng cho giới hạn tốc độ của trang đăng ký web `BattleBallServer/web` (xem `docs/architecture/BattleBallServer.md` mục 15.3). Không ảnh hưởng dữ liệu trò chơi.

### 10.35 Bông tai Potara là vật phẩm số 454 và nằm ở ô đầu tiên của hành trang (V024), 2026-10-07
**CONFIRMED (người dùng): bông tai nằm trong hành trang và luôn ở vị trí đầu tiên** (vẫn ở cấp tài khoản, không nằm trong 10 ô của nhân vật, không bị tiêu hao). **CONFIRMED (đọc mã nguồn Teamobi2026, người dùng nhờ tìm id): `ConstItem.BONG_TAI_PORATA = 454`** (`item_template` NRO: "Bông tai Porata", icon 3896, quả cầu vàng). NRO còn hai cấp nâng cấp 921 (xanh lá, icon 7993) và 1819 (xanh dương, icon 15614) cùng các vật phẩm nâng cấp 933, 934, 935, 1820; BattleBall chỉ có một bông tai nên chỉ dùng 454 (**PROPOSED**: dùng id 454 và icon 3896; đổi được trên admin). Mảnh bông tai vẫn là vật liệu #900 (cộng vào `account.potara_fragments`).
File `V024__potara_item.sql` thêm `item_id` = 454 và `icon_id` = 3896 vào `balance_config.potara` (đã áp vào `battleball`, `schema_version` 24). `PotaraInfo` trong giao thức có thêm `item_id`, `icon_id`, `shards_needed` (100) và `min_fusion_level` (50) để máy khách không giữ bảng riêng. `build_assets.py` đóng gói thêm icon 3896. Máy khách: ô đầu tiên của hành trang là bông tai (đủ màu và chữ "Dùng" khi đã có, bấm vào để hợp thể hoặc "Tách"; mờ kèm "n/100" khi chưa đủ mảnh); đã bỏ nút "Bông tai" bên phải. Maven 53 bài kiểm tra đạt.

**Cập nhật 10.35 (CONFIRMED, người dùng, 2026-10-08): chỉ dùng bông tai cấp 3 của NRO.** Bông tai Potara = **#1819** "Bông tai Porata" (icon 15614, xanh dương); mảnh = **#1820** "Mảnh vỡ bông tai cấp 3" (icon 15613). Không dùng #454, #921 và các vật phẩm nâng cấp khác. File `V025__potara_items_nro_ids.sql` (đã áp vào `battleball`, `schema_version` 25): chuyển mảnh từ id #900 sang **#1820** (tạo dòng mới trong `material_template`, chuyển mọi tham chiếu của bảng rơi, hệ số theo phó bản, công thức, kho và bộ đếm mua theo ngày, rồi xóa #900) và đặt trong `balance_config.potara`: `item_id` 1819, `icon_id` 15614, `shard_item_id` 1820, `shard_icon_id` 15613. Mọi chỗ ghi "#900" ở các mục 10.30 trở về trước nay là #1820 (id cũ chỉ còn trong lịch sử migration V016). `PotaraInfo` gửi thêm `shard_item_id` và `shard_icon_id`. Máy khách: ô đầu tiên của hành trang là mảnh (kèm "n/100") cho đến khi đủ, rồi là bông tai (chữ "Dùng"). Maven 53 bài kiểm tra đạt.
**Chọn hành tinh khi dùng bông tai (CONFIRMED yêu cầu của người dùng; cách hiểu "như thiết kế ban đầu" là INFERRED):** bấm bông tai luôn hiện hộp "Chọn hành tinh hợp thể với <nhân vật chủ>" liệt kê hai hành tinh còn lại; hành tinh có nhân vật dưới cấp tối thiểu vẫn hiện nhưng mờ và không chọn được ("cần cấp 50"). Nhân vật chủ là nhân vật ở tab đang mở.

**Cấp thủ công cho tài khoản thử `tdno01` (CONFIRMED, 2026-10-08, người dùng yêu cầu):** `account_id` 6: đặt `potara_owned = 1` (Bông tai Porata #1819, cấp theo tài khoản); thêm skin #1816 "td" (`kind_key` FUSION_FORM, `fusion_race` td, `gender_id` 3, icon 15622) vào `owned_skin` của nhân vật Trái Đất `character_id` 7 với `source = ADMIN`. Chọn nhân vật Trái Đất vì skin hợp thể "td" dành cho nhân vật chủ Trái Đất (INFERRED); hai nhân vật Namếc (8) và Xayda (9) không được cấp. Sửa trực tiếp bằng SQL trong DB thật, không qua migration; server đọc dữ liệu người chơi mỗi lần đăng nhập nên không cần khởi động lại.

**Cấp 100 và skin trong hành trang (CONFIRMED, 2026-10-08, người dùng yêu cầu):** ba nhân vật của `tdno01` (id 7, 8, 9) được đặt `level = 100`, `exp = 0` bằng SQL (cấp tối đa 100; chỉ số tính từ cấp nên không cần sửa gì khác). Người dùng không thấy skin #1816 vì menu chưa liệt kê skin sở hữu: các ô hành trang sau ô bông tai nay hiện các skin nhân vật đang có (`CharacterInfo.owned_skins`, hình lấy từ `iconId` trong `CostumeCatalog`, chữ "Mặc" trên skin đang mặc). Bấm một ô chỉ hiện tên skin; **đổi trang phục (gửi `EquipSkinRequest`) chưa làm**: nút "Trang phục" vẫn là "sắp có" (OPEN). Hành trang có 9 ô cho skin, nhiều hơn thì chưa có cách cuộn (OPEN). Ảnh kiểm tra: `BattleBall\Logs\menu_0.png` (dữ liệu giả, chưa thử với server thật trong chế độ Play).

### 10.36 Kho đồ chung của ba nhân vật (V026), người dùng chốt 2026-10-08: "cả 3 đều dùng 1 kho đồ chung", gồm cả vàng và đá quý

- **CONFIRMED (người dùng):** ba nhân vật của một tài khoản dùng chung **một ví (vàng, đá quý), nguyên liệu, skin sở hữu, đồ trang bị trong túi và đồ hỗ trợ**. Trước đó mọi thứ này lưu theo nhân vật (mục 3.8 và 10.5 là bản cũ).
- **Giữ theo nhân vật:** cấp và EXP, kỹ năng, **đồ đang mặc** (`character_equipment`), bộ đếm theo ngày (`character_daily_counter`), thành tích, lần hạ boss đầu (đá quý lần đầu vẫn tính theo nhân vật). Bông tai và mảnh bông tai vẫn theo tài khoản như trước.
- **Bảng (V026):** `character_wallet` thành `account_wallet(account_id)`; `material_stack` và `owned_skin` đổi khóa sang `account_id`; `item_instance` và `character_support_item` có `account_id` thay cho `character_id`. Dữ liệu cũ được gộp: ví và nguyên liệu cộng dồn, skin hợp nhất; khóa ngoại `fk_equip_skin` bị bỏ (server kiểm tra quyền sở hữu khi mặc).
- **Đồ đang mặc:** một món (đồ trang bị hay đồ hỗ trợ) chỉ một nhân vật mặc tại một thời điểm (khóa duy nhất của `character_equipment`); món đang được mặc không còn nằm trong túi, nhân vật khác phải tháo ra mới mặc được (lệnh mặc trả `NOT_FOUND`).
- **Giới hạn đã biết (INFERRED/OPEN):** giáp tập luyện là một món chung; khi nhiều nhân vật không mặc, chỉ món chưa ai mặc bị trừ điểm. Mua skin vẫn kiểm tra chủng tộc của nhân vật đang thực hiện.
- **Skin hợp thể (CONFIRMED, người dùng, 2026-10-08):** cải trang hợp thể **chỉ dùng được với nhân vật chủ** của một lần hợp thể. Server gửi `SkinInfo.fusion_main_only` và `fusion_race`; client chỉ cho bấm "Sử dụng" khi nhân vật đó đang là chủ của hợp thể; khi vào trận mà nhân vật không phải chủ hợp thể, server dùng skin mặc định của chủng tộc thay cho skin hợp thể (chỉ số cộng vốn đã chỉ tính cho chủ). Skin hợp thể của hành tinh khác không dùng được nếu nhân vật đó không phải chủ.
- **Cấp thủ công (CONFIRMED, 2026-10-08):** `tdno01` đã có cả ba nhân vật cấp 100, bông tai #1819 và skin #1816 (nay thuộc tài khoản).
- Kiểm tra: `EconomyServiceTest.theThreeCharactersShareOneInventory` và các kiểm tra ví/skin trong `ProtocolIntegrationTest`; 61 bài kiểm tra Maven đạt.

### 10.37 Hòm thư, thông báo, đổi mật khẩu (V027), người dùng chốt 2026-10-08

- **CONFIRMED (người dùng), THAY THẾ quyết định cũ "bỏ hẳn hòm thư":** tab "Chức năng" của menu có **Hòm thư** (nhận quà từ admin), **Thông báo** và **Tài khoản** (đăng xuất, đổi mật khẩu). Hòm thư chỉ chứa thư do admin gửi; người chơi không gửi thư cho nhau.
- **Quà trong thư:** vàng, đá quý, skin, nguyên liệu (kể cả mảnh bông tai #1820, giữ giới hạn 100 mảnh và không nhận khi đã có bông tai), đồ trang bị, bông tai Porata #1819. Nhận quà là một giao dịch: đủ chỗ trong túi (giới hạn 2.000) hoặc không nhận gì; không nhận hai lần (`claimed_at`). Quà vào kho chung của tài khoản.
- **Gửi:** trang admin "Hòm thư" gửi cho một tên tài khoản hoặc `*` (mọi tài khoản đang có, mỗi tài khoản một bản; tài khoản tạo sau không nhận). Có nhật ký gửi (`admin_audit_log`) và danh sách thư đã gửi. Xóa thư chỉ khi không có quà hoặc đã nhận quà.
- **Thông báo:** trang admin "Thông báo" (đăng, bật/tắt, xóa, hạn dùng tùy chọn); người chơi xem tối đa 50 thông báo mới nhất đang bật và chưa hết hạn. Chưa có trạng thái "đã đọc" cho thông báo (OPEN).
- **Đổi mật khẩu:** cần mật khẩu cũ đúng (5 lần sai trong 10 phút thì khóa tạm); mật khẩu mới 8 đến 64 byte; mọi refresh token cũ bị thu hồi và server cấp token mới cho máy đang đổi (client lưu lại). Phiên đang mở ở máy khác không bị đóng ngay (OPEN).
- Bảng: `mail`, `mail_attachment(kind GOLD/GEMS/SKIN/MATERIAL/ITEM/EARRING)`, `notice`. Kiểm tra: `MailServiceTest` (5 bài), `ProtocolIntegrationTest` (đổi mật khẩu và hòm thư qua socket), `BbNetSelfTest` bước 5 trên server thật (đạt).

### 10.38 Hợp thể: tab riêng, skin hợp thể gắn vào hình thái hợp thể (V028), người dùng chốt 2026-10-08

- **CONFIRMED (người dùng), SỬA mục 10.36 (đoạn "Skin hợp thể") và thay cách hiển thị "gộp 2 tab thành 1":** khi dùng Bông tai, **một tab mới "Hợp thể" được tạo** (tổng chỉ số của chủ và phụ); **ba tab hành tinh vẫn giữ nguyên** để mặc cải trang thường cho từng nhân vật. Chuyển sang tab hành tinh khác **không** tự tách hợp thể; chỉ bấm ô Bông tai **trên tab Hợp thể** mới tách ("Tách"). Bấm ô Bông tai trên tab hành tinh khác bắt đầu một hợp thể mới với nhân vật đó làm chủ (thay hợp thể đang có). Lỗi trước đây: bấm Bông tai ở tab khác thì tự tách.
- **Cải trang hợp thể** (kind `FUSION_FORM`, `requires_fusion_main`) gắn vào **hình thái hợp thể**: ô riêng `FUSION_SKIN` của nhân vật chủ (V028; lệnh `EquipSkinRequest.fusion = true`). Nó đổi ngoại hình của hình thái hợp thể (client hiện ở tab Hợp thể và ở làng; server dùng nó làm trang phục của chủ khi vào trận có hợp thể) và cộng chỉ số **chỉ khi hợp thể**. Chỉ nhân vật đúng chủng tộc của skin (`fusion_race`) mới giữ được; ô `SKIN` thường từ chối skin hợp thể (và ngược lại).
- **Cải trang thường** của chủ và phụ (ô `SKIN`) **không đổi ngoại hình hợp thể**, chỉ cộng chỉ số riêng của nhân vật đó; sau đó hai nhân vật cộng lại thành tổng hợp thể (thứ tự chỉ số của GDD không đổi).
- Giao thức: `CharacterInfo.fusion_stats` và `fusion_bonuses` là chỉ số và tổng cộng thêm của nhân vật khi là chủ hợp thể (đã tính skin hợp thể); client cộng với chỉ số của phụ để hiện tab Hợp thể. Cặp hợp thể vẫn chỉ nằm ở client (chưa lưu, chưa gửi ở `StartMatchRequest`: OPEN).
- Kiểm tra: `EconomyServiceTest.aFusionSkinIsWornByTheFusionFormAndCountsOnlyThere`; 62 bài kiểm tra Maven đạt; ảnh `Logs/menu_fused.png`, `menu_fused_other_tab.png`, `menu_skin_popup_fused.png`.

**Sửa 2026-10-08 (người dùng): làng giữ hình thái hợp thể khi xem tab chủ hoặc phụ (CONFIRMED).** Khi đang có hợp thể, nhân vật trong làng luôn là hình thái hợp thể (chủ mặc cải trang hợp thể, phụ nhập vào chủ) cạnh nhân vật còn lại là AI, **dù đang mở tab nào**; chuyển tab chủ hoặc phụ chỉ để xem và mặc đồ riêng, không tách hợp thể. Chỉ thao tác với Bông tai mới tách (ô "Tách" trên tab Hợp thể) hoặc thay bằng hợp thể mới. Ảnh: `BattleBall\Logs\menu_fused_partner_tab.png`.

**10.39 Aura (V029, 2026-10-08, CONFIRMED bởi người dùng):** bảng aura nằm trong `balance_config` khóa `auras` (JSON `{"items":[{id, name, level, enabled, layers:[{layer, frames}]}]}`), cột `account.equipped_aura_id` (NULL = không đeo, một aura mỗi tài khoản, dùng chung 3 nhân vật). Mở khóa: `MAX(game_character.level)` của tài khoản >= `level` của aura; không có bảng "đã mở" vì cấp không giảm. Aura chỉ là hiệu ứng hình ảnh. Sinh migration: `tools/make_aura_migration.py` từ `tools/aura_planner/aura_levels.json`.

**10.40 NPC luyện tập ở làng tân thủ (V030, 2026-10-08, CONFIRMED bởi người dùng):** `balance_config` khóa `training` = `{"dummies":[{key, name, hp, mob_template_id, costume_id, radius, armor_pct}], "unlearned_skill_level", "revive_after_s", "idle_end_s"}`. Mặc định: `moc_nhan` "Mộc nhân" (mob Nro 0, bán kính 24) và `saibamen` "Saibamen" (cải trang #647, bán kính 20), cả hai 2.000.000.000 HP, giáp 0; skill chưa học dùng ở cấp 1 khi luyện tập (PROPOSED, vì máy khách chưa có chỗ học skill; 0 = chỉ skill đã học); nhân vật chết khi luyện tập (Tự phát nổ) đứng dậy sau 2 s; phiên đóng sau 300 s không thao tác. Luyện tập **không ghi gì** vào DB (không `match_run`, không thưởng). Chỗ đứng của NPC nằm trong bản đồ làng (`chunk.json`, NPC `action = "TrainingDummy"`, `id` = `key`, `mob`), không nằm trong DB.

**10.41 Hồi chiêu theo giờ thật của server (V031, 2026-10-09, CONFIRMED bởi người dùng: "thời gian hồi chiêu tính vào thời gian thực của server, tránh người dùng đăng xuất để reset hồi chiêu"):** bảng `character_skill_cooldown(character_id, skill_id, ready_at_ms)`, `ready_at_ms` là Unix ms của đồng hồ server. Khi một phiên kết thúc (luyện tập hay trận, kể cả mất kết nối, rời, bị thay bằng phiên luyện tập mới) server ghi các skill còn đang hồi chiêu của mọi nhân vật trong đội (`MatchManager.saveCooldowns`); dòng đã qua giờ thì xóa. Phiên sau nạp lại (`PlayerRepository.load` đọc các dòng chưa qua giờ, `MatchStarter` gọi `MatchEngine.applyCooldowns`) nên đăng xuất rồi vào lại không làm mới hồi chiêu. Snapshot có `CharacterInfo.cooldowns` (còn lại bao nhiêu ms tại lúc gửi). Chỉ ghi khi phiên kết thúc: nếu server tắt đột ngột giữa phiên thì hồi chiêu của phiên đó mất (OPEN: có cần ghi định kỳ không).

**10.42 Sửa luyện tập (V032, 2026-10-09, người dùng yêu cầu):** `training.unlearned_skill_level` = **0** (CONFIRMED): skill chưa học **không** dùng được khi luyện tập nữa (trước đó PROPOSED là 1); nhân vật mới chỉ có đòn đấm. Bán kính trúng đòn của Mộc nhân giảm 24 → 14 và Saibamen 20 → 12 vì đòn đấm "khá xa": đòn đánh trúng khi khoảng cách ≤ chiều dài skill + bán kính mục tiêu (đấm lv1: 32 + 14 = 46 px Nro thay cho 56).

**10.43 Cửa hàng NPC (V033, 2026-10-09, CONFIRMED bởi người dùng: "thêm 1 npc urôn bán hàng (bán tất cả skin)"):** `balance_config` khóa `npc_shops` = `{"uron":{"name":"Urôn","tabs":[{"name":"Mọi tộc","gender":3},{"name":"Trái Đất","gender":0},{"name":"Xayda","gender":2},{"name":"Namếc","gender":1}]}}`: mỗi khóa là id NPC trên bản đồ, `tabs[].gender` là tộc của cải trang (0 Trái Đất, 1 Namếc, 2 Xayda, 3 mọi tộc). Cửa hàng bán mọi cải trang bán được (`skin_template.enabled`, `skin_kind.enabled` và `sellable`, `gem_price` khác NULL), giá bằng ngọc, sắp theo giá rồi mã; thêm NPC bán hàng khác chỉ cần thêm một khóa. Xem `docs/architecture/BattleBallServer.md` 15.15.
