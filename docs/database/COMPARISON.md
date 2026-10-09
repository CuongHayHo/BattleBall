# Database: Teamobi2026 vs NroUnity

> BattleBall analysis has not started yet.
> Chi tiết: `Teamobi2026.md`, `NroUnity.md` cùng thư mục.

## Tóm tắt (CONFIRMED)
- **Teamobi2026 có database** (MySQL/MariaDB, JDBC + HikariCP, không ORM). **NroUnity không có database**, chỉ có `PlayerPrefs` qua `Rms`.
- Toàn bộ dữ liệu bền vững của người chơi nằm ở server; client giữ bản sao template và tài khoản/mật khẩu đã lưu.

## So sánh
| Khía cạnh | Teamobi2026 | NroUnity |
|---|---|---|
| Lưu trữ | MySQL (`team2026`) + file `data\` | `PlayerPrefs` (`Rms`) + `Resources` |
| Truy cập | `LocalManager`, DAO tĩnh, SQL thủ công | `Rms.saveRMS/loadRMS` |
| Schema | Dump SQL (41–53 bảng), không migration | Không có schema |
| Transaction | Hầu như không | Không áp dụng |
| Dữ liệu người chơi | Bảng `player` (JSON theo vị trí) + `account` | Không lưu cục bộ |
| Template | Bảng `*_template`, nạp vào RAM khi khởi động | `NRmap`, `NRskill`, `NRitem*` do server gửi |
| Tài khoản | `account.password` (dạng rõ theo code) | `acc`/`pass` trong `PlayerPrefs` (dạng rõ) |

## Tương ứng dữ liệu
- Template item/skill/map/mob/NPC: bảng `*_template` (server) ↔ các class `*Template` (client). INFERRED từ tên và luồng đã trace (`-28/6,7,8`); chưa so sánh từng trường nhị phân.
- Người chơi: `player` (server) ↔ `Char` (client, nhận qua gói tin). Clan: `clan` ↔ `Clan`.

## Chỉ có ở một phía
- Chỉ server: DB, bảng `web_*`, nạp thẻ, ngân hàng, lưu người chơi, web portal.
- Chỉ client: `PlayerPrefs`, tài nguyên nhúng.

## Dòng chảy dữ liệu
```
MySQL ──load──► RAM (Manager) ──gói tin / file update_data──► Client ──Rms──► PlayerPrefs
Player (RAM) ──PlayerDAO.updatePlayer (2 phút / khi ngắt)──► MySQL
```

## UNKNOWN
- Định dạng nhị phân của dữ liệu template gửi cho client.
- Dump SQL đang dùng thật.
