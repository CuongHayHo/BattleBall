# Gameplay: Teamobi2026 vs NroUnity

> BattleBall analysis has not started yet.
> Chi tiết: `Teamobi2026.md`, `NroUnity.md` cùng thư mục.

## Phân chia trách nhiệm (CONFIRMED)
| | Teamobi2026 (server) | NroUnity (client) |
|---|---|---|
| Quyết định | HP, sát thương, cooldown, mana, drop, chết, hồi sinh, map, nhiệm vụ | Vật lý và vị trí nhân vật của mình, chọn mục tiêu, hiệu ứng, nội suy, UI |
| Vòng lặp | Nhiều thread, khoảng 1 giây | 1 tick 50 Hz (`FixedUpdate`); gói mạng xử lý trong `OnGUI` |
| Thực thể | `Player` đa dụng bằng cờ, `Mob`, `ItemMap`, `Npc`; danh sách trong `Zone` | `Char`, `Mob`, `Npc`, `ItemMap`; `MyVector` tĩnh trong `GameScr` |
| Trạng thái | Field công khai, ít đồng bộ giữa thread | Field công khai, một thread chính |
| Đồng bộ | Gửi sự kiện ngay theo hành động | Áp dụng sự kiện khi nhận; nội suy cho người khác |
| Mob | Đứng yên, chỉ tấn công/chết/hồi sinh | Tự đi lại trong phạm vi (hình ảnh) |
| Vị trí | Gán thẳng vị trí client báo | Tự tính vật lý rồi báo |

## Giống nhau
- Khái niệm map → zone, mob, char, skill, item, template theo id.
- Mô hình gửi sự kiện, không snapshot.
- Trạng thái là field công khai; singleton tĩnh `gI()`.

## Khác nhau
- Server có cooldown, mana, sát thương thật; client có vật lý và va chạm thật.
- Server không mô phỏng chuyển động mob; client có.
- Server đa thread; client một thread.

## Chỉ có ở một phía
- Chỉ server: boss state machine, bot, AI mob tấn công, drop, nhiệm vụ, PvP, phó bản, tự lưu.
- Chỉ client: vật lý tile cho nhân vật, animation theo `statusMe`, `SkillPaint`, camera, nội suy, mod tự chơi.

## Điểm lệch gameplay/protocol (CONFIRMED)
1. Server không có `case -4`/`67`; đòn client gửi khi trong tầm có cả mob và người bị bỏ qua.
2. Sát thương `int` ở server; client `readLong()` nhưng `ModFunc.isReadInt = true` làm nó đọc `int`.
3. `-17`: server ghi `(byte) pl.id`, client đọc vào `cPk`.

## Rủi ro thiết kế đã quan sát (CONFIRMED trừ khi ghi khác)
- Vị trí và mục tiêu do client quyết định; chưởng không kiểm tra khoảng cách.
- `Player.update` có hai đường gọi mỗi giây; `Mob.injured` không đồng bộ.
- Luật đặc biệt gắn cứng theo `mapId`/`skillId`.

## UNKNOWN
- `Player.update()` gọi trùng có hệ quả gì; chi tiết `getDameAttack`/`injured` và PvP; tầm đánh phía client; payload `-9/-10/-11`.
