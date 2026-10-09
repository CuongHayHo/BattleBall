# Database: NroUnity

> BattleBall analysis has not started yet.
> Nhãn: **CONFIRMED** / **INFERRED** / **UNKNOWN**.

`C:` = `E:\PRJNRO\NroUnity\Assets\Scripts\Assembly-CSharp\Game1\`

## Technology
- CONFIRMED: **NroUnity không dùng database.** Không có SQLite, ORM, `ScriptableObject`, `JsonUtility`, Newtonsoft, `XmlSerializer`, `BinaryFormatter`. Grep trên mọi `Assets\**\*.cs` chỉ khớp `PlayerPrefs` trong `Rms.cs`.
- Dữ liệu game đến từ server qua mạng (xem `docs\protocol\NroUnity.md`).

## Local persistence: `Rms` trên `PlayerPrefs`
Source: `C:Rms.cs`.

- CONFIRMED: `Rms.saveRMS(name, sbyte[])` đổi byte thành chuỗi rồi `PlayerPrefs.SetString(filename + TabType.Tab1, …)` (Game1, `Rms.cs:206`). Bản Game2 dùng khoá `filename + TabType.Tab2`. Hai tab có kho cục bộ **tách biệt**.
- CONFIRMED: `Rms.update()` (gọi từ `Main.FixedUpdate`, `C:Main.cs:296`) mới thực sự ghi/đọc; `_saveRMS/_loadRMS` đặt `status` rồi chờ tối đa 500 × 5 ms, nên thread gọi bị chặn tới 2,5 giây.
- CONFIRMED: `Rms.clearAll()` gọi `PlayerPrefs.DeleteAll()`.
- UNKNOWN: vị trí vật lý của `PlayerPrefs` trên máy người dùng.

### Khoá RMS đã thấy (CONFIRMED qua `Controller.messageNotMap`, `LoginScr.cs`, `GameMidlet.cs`)
| Khoá | Nội dung |
|---|---|
| `NRmap`, `NRmapVersion`, `NRskill`, `NRskillVersion`, `NRitem0/1/2/100` | Dữ liệu template do server gửi và phiên bản |
| `ResVersion`, `version` | Phiên bản tài nguyên và game |
| `acc`, `pass`, `userAo<n>` | Tên đăng nhập và mật khẩu (**lưu dạng rõ**) |
| `AdminLink`, `clienttype`, `check` | Cờ cấu hình |
| `<zoom>bgItem<id>` | Ảnh nền đã tải |
Không có dữ liệu người chơi (túi đồ, chỉ số) được lưu cục bộ ngoài tài khoản và mật khẩu.

## Client-side models
Class C# thuần, không serialize tự động (CONFIRMED qua các file `C:*.cs`):

| Model | Trường chính |
|---|---|
| `Char` | `charID`, `cName`, `cgender`, `cHP/cHPFull`, `cMP`, `cPower`, `cTiemNang`, `ctaskId`, `clanID`, `xu`, `luong`, `cStamina`, `arrItemBag/Box/Body`, `vSkill` |
| `Item` / `ItemTemplate` / `ItemOption` | `template`, `itemOption[]`, `quantity`, `indexUI` / `id`, `type`, `gender`, `name`, `iconID`, `part` / `param`, `optionTemplate` |
| `Skill` / `SkillTemplate` | `skillId`, `point`, `coolDown`, `manaUse`, `damage` / `id`, `classId`, `name`, `maxPoint` |
| `MobTemplate`, `NpcTemplate`, `Part`/`PartImage`, `Clan`, `Task` | tương ứng template phía server |

## Embedded resources (CONFIRMED)
`E:\PRJNRO\NroUnity\Assets\Resources\`: 427 PNG, 38 WAV, 30 `.bytes`, 9 TTF, 4 OGG, 1 JSON (`BillingMode.json`, do Unity tạo).
- `.bytes`: font (`myfont\*`), 4 bản đồ mẫu (`mymap\0, 39, 40, 41` và `mapTable39/40/41`), dữ liệu hiệu ứng (`x4\effectdata\<id>\data.bytes`).
- Đọc bằng `Resources.Load(filename, typeof(TextAsset))` (`C:DataInputStream.cs:18`).
- INFERRED: chỉ 4 bản đồ được nhúng; phần còn lại do server gửi (server có `DataGame.sendMapTemp`).

## Persistence flow (client)
```
Server (DB) ──gói −28/6,7,8, −87, −74…──► Controller.messageNotMap ──► Rms.saveRMS("NRmap"/"NRskill"/"NRitem*" + Version) ──► PlayerPrefs
Lần chạy sau: Rms.loadRMS ──► DataInputStream ──► template (ItemTemplates, SkillTemplates, …)
So khớp phiên bản (−28/4, −74): khác thì tải lại.
```
Bước cuối (đọc vào template) là INFERRED từ đoạn `new DataInputStream(Rms.loadRMS("NRmap"))`.

## Data ownership
- Server (Teamobi2026) sở hữu dữ liệu người chơi và template gốc (`docs\database\Teamobi2026.md`).
- Client chỉ giữ bản sao template và tài khoản/mật khẩu đã lưu.

## Important files
| File | Vai trò |
|---|---|
| `C:Rms.cs` | Lưu cục bộ trên `PlayerPrefs` |
| `C:Controller.cs` (`messageNotMap`, khoảng dòng 5063) | Nhận và lưu dữ liệu template |
| `C:LoginScr.cs` | Lưu tài khoản/mật khẩu |
| `C:DataInputStream.cs` | Đọc `.bytes` từ `Resources` và byte từ `Rms` |
| `E:\PRJNRO\NroUnity\Assets\Resources\res\` | Tài nguyên nhúng |

## UNKNOWN
- Định dạng nhị phân chính xác của `NRmap`, `NRskill`, `NRitem*` (chưa đối chiếu với `DataGame.updateMap/updateSkill`, `ItemData.updateItem` phía server).
- Vị trí vật lý của `PlayerPrefs`.
