# Architecture: Teamobi2026 vs NroUnity

> Reference projects. BattleBall analysis has not started yet.
> Chi tiết và bằng chứng: `Teamobi2026.md`, `NroUnity.md` cùng thư mục.

## Quan hệ
- CONFIRMED: hai project là hai nửa của cùng một game NRO: server Java (`E:\PRJNRO\Teamobi2026`) và client Unity (`E:\PRJNRO\NroUnity`). Bằng chứng: cùng khung gói tin, cùng cơ chế XOR, cùng handshake `-27`, cùng cổng 14445 (`docs\protocol\COMPARISON.md`).
- Hai project **không có kiến trúc giống nhau**: một bên là server nhiều thread, một bên là client một vòng lặp.

## Bảng so sánh
| Khía cạnh | Teamobi2026 | NroUnity |
|---|---|---|
| Vai trò | Server, web, DB | Client |
| Ngôn ngữ | Java 17, PHP, SQL | C# (Unity 2022.3) |
| Build | Ant (NetBeans) | Unity Editor |
| Entry | `ServerManager.main` | Scene `NROL` → `Main : MonoBehaviour` |
| Mô hình chạy | Nhiều thread, singleton tĩnh | Một vòng lặp (`FixedUpdate`/`OnGUI`), singleton tĩnh |
| Module | Package trong một project Java | Một assembly, thư mục phẳng, hai namespace nhân bản |
| Mạng | NIO accept + 2 thread/session | `TcpClient` + sender/collector thread, hàng đợi sang main thread |
| Dữ liệu | MySQL + file `data\` | `PlayerPrefs` (`Rms`) + `Resources` |
| Cấu hình | `Config.properties` | Hằng số trong code (`Management.IpServer`) |
| Mod/tự chơi | Không | Có (`Mod\XMAP`, `ModFunc`, `AutoItem`…) |

## Tương đồng (CONFIRMED trừ khi ghi khác)
- Mô hình `Session` + `Message` + `Controller` (`switch` theo opcode `sbyte`) ở cả hai phía.
- Cùng tên ba router phụ `messageNotLogin`, `messageNotMap`, `messageSubCommand`.
- Dữ liệu game theo template + id (item, skill, map, mob, NPC).
- Trạng thái là field công khai, đồng bộ bằng gửi sự kiện (không snapshot).
- Singleton tĩnh `gI()` ở cả hai phía.

## Khác biệt
- Server giữ thẩm quyền về HP, sát thương, cooldown, mana, drop; client giữ vật lý và vị trí nhân vật của mình (`docs\gameplay\COMPARISON.md`).
- Server mở socket bằng NIO `Selector`; client kết nối đồng bộ trong thread riêng.
- Phía client có hàng đợi nhận chuyển sang main thread; phía server xử lý ngay trên thread nhận.
- Client có hệ thống mod; server không có tương đương.

## Chỉ tồn tại ở một phía
- Chỉ server: web portal, DB, boss, bot, phó bản, tự lưu, cầu nối DB↔web.
- Chỉ client: `Rms`/`PlayerPrefs`, animation theo `statusMe`, `SkillPaint`, mod tự chơi.

## Điểm lệch cần nhớ giữa hai phía (CONFIRMED)
- Danh sách cmd dùng size 3 byte: client có thêm `12`.
- Client đọc thêm `IP2/PORT2/isConnect2` trong gói key mà server không gửi.
- Server không xử lý opcode `-4` và `67` mà client có thể gửi.
Chi tiết: `docs\protocol\COMPARISON.md`.

## UNKNOWN
- Mức khớp từng opcode gameplay ngoài số đã kiểm.
- Hành vi khác nhau thực sự giữa `Game1` và `Game2`.
