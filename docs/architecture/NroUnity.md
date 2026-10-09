# NroUnity: Architecture

> Reference project. Not part of BattleBall. BattleBall analysis has not started yet.
> Nhãn: **CONFIRMED** / **INFERRED** / **UNKNOWN**. Source code > các file `*_TREE.md`.

`C:` = `E:\PRJNRO\NroUnity\Assets\Scripts\Assembly-CSharp\Game1\` (bản `Game2\` là bản nhân đôi theo namespace)

## Overview
- CONFIRMED: client game Unity **2022.3.62f2** (`E:\PRJNRO\NroUnity\ProjectSettings\ProjectVersion.txt`), productName "NRO QUEEN" (`ProjectSettings\ProjectSettings.asset`).
- CONFIRMED: `Packages\manifest.json` chỉ có module built-in của Unity, không có package bên thứ ba.
- CONFIRMED: một assembly `Assembly-CSharp`, không có `.asmdef`. 392 file `.cs` = `Game1\` (195) + `Game2\` (195) + `Management.cs` + `TabType.cs`.
- CONFIRMED: `Game1` và `Game2` cùng bộ 195 file, chỉ khác `namespace Game1`/`Game2` và vài dòng (đã so sánh `Message.cs`, `Session_ME.cs`, `Main.cs`).
- INFERRED: mã là client Java/J2ME của NRO được chuyển sang C# (tên `midlet`, `Rms`, `mGraphics`, `mFont`, `MyVector`, tự vẽ). Không có tài liệu trong source xác nhận.
- CONFIRMED: client dùng Unity chỉ như host cho vòng lặp và vẽ; gameplay không dùng GameObject, Prefab hay Animator.

## Entry points
| Entry | Source |
|---|---|
| Scene `NROL` (và `NROL1`) | `E:\PRJNRO\NroUnity\Assets\Scenes\NROL.unity` |
| `Main : MonoBehaviour` (`Awake`, `Start`) | `C:Main.cs` |
| `GameMidlet` | `C:GameMidlet.cs`: tạo `MotherCanvas`, gán `Controller` làm handler cho `Session_ME`/`Session_ME2`, tạo `GameCanvas`, đặt màn hình đầu là `SplashScr` |
| Chọn tab, nạp scene | `Assembly-CSharp\Management.cs`, `TabType.cs` |

Lưu ý (CONFIRMED): `ProjectSettings\EditorBuildSettings.asset` liệt kê scene `Nro1.unity` và `NRO2.unity`, không tồn tại; scene thật là `NROL`, `NROL1`; `Management.cs` nạp theo tên thật.

## Modules
Một thư mục phẳng, nhóm theo chức năng (CONFIRMED qua tên file):

| Nhóm | File tiêu biểu |
|---|---|
| Vòng lặp và canvas | `Main`, `GameMidlet`, `MotherCanvas`, `GameCanvas`, `mGraphics`, `mScreen` |
| Màn hình | `SplashScr`, `ServerListScreen`, `LoginScr`, `ChooseCharScr`, `CreateCharScr`, `GameScr`, `RadarScr` |
| Network | `Session_ME(2)`, `ISession`, `Message`, `Controller`, `Service`, `myReader/myWriter`, `DataInputStream/OutputStream` |
| Thực thể | `Char`, `Mob`, `Npc`, `ItemMap`, `BigBoss`, `Mabu`, `PetFollow` |
| Dữ liệu | `Item*`, `Skill*`, `MapTemplate`, `MobTemplate`, `NpcTemplate`, `Part*`, `EffectData`, `SmallImage`, `Task`, `Clan*` |
| UI | `Panel`, `Menu`, `Dialog`, `ChatPopup`, `InfoDlg`, `NewBagUI` |
| Mod (không thuộc game gốc: INFERRED) | `ModFunc`, `Mod\XMAP\*`, `Mod\ShowBoss`, `AutoItem*`, `AutoLogin` |

## Core systems
- CONFIRMED: vòng lặp. `Main.FixedUpdate()` (50 Hz theo `ProjectSettings\TimeManager.asset`) → `GameCanvas.update()` → `currentScreen.update()`. `Main.OnGUI()` xử lý gói mạng (`Session_ME.update()`) rồi vẽ (`gameCanvas.paint`). Xem `docs\gameplay\NroUnity.md`.
- CONFIRMED: `Controller.cs` (khoảng 6.350 dòng) là dispatcher gói đến; `Service.cs` là lớp gửi.
- CONFIRMED: `Management.IpServer` là hằng số chứa địa chỉ server (`C:` cùng thư mục cha, `Management.cs`); `ServerListScreen.ListIP` lấy từ đó.

## Dependencies
- CONFIRMED: không có package ngoài Unity. Không SQLite, không JSON/XML serialization (chỉ `PlayerPrefs` qua `Rms`).
- CONFIRMED: cần một server tương thích (Teamobi2026) cho dữ liệu game (xem `docs\networking\COMPARISON.md`).

## Data flow
```
Scene NROL ─► Main.Awake/Start ─► GameMidlet ─► GameCanvas ─► SplashScr ─► ServerListScreen(Management.IpServer) ─► LoginScr
Main.OnGUI ─► Session_ME.update ─► Controller.onMessage ─► GameScr/Char/TileMap/Panel ─► GameCanvas.paint
Input ─► Service.xxx() ─► Message ─► Session_ME (sender thread) ─► TCP
Local: Rms ─► PlayerPrefs (key + Tab1|Tab2)
Static assets: Resources.Load (png, wav, ttf, .bytes)
```

## Important files
| File | Vai trò |
|---|---|
| `C:Main.cs` | MonoBehaviour điều khiển, `FixedUpdate`/`OnGUI` |
| `C:GameCanvas.cs`, `C:GameScr.cs` | Màn hình, vòng cập nhật |
| `C:Session_ME.cs`, `C:Message.cs`, `C:Controller.cs`, `C:Service.cs` | Mạng |
| `C:Char.cs`, `C:Mob.cs`, `C:TileMap.cs` | Thực thể và map |
| `C:Rms.cs` | Lưu cục bộ |
| `Assembly-CSharp\Management.cs` | Chọn tab, địa chỉ server |
| `E:\PRJNRO\NroUnity\Assets\Resources\res\` | Tài nguyên nhúng |

## Confirmed / Inferred / Unknown
- UNKNOWN:
  - Vì sao tồn tại hai bản `Game1`/`Game2` và `Session_ME2`/`isConnect2` được dùng với server nào.
  - Build thực tế dùng scene nào (do `EditorBuildSettings` trỏ sai).
  - Cách `ServerListScreen` phân tích chuỗi `a:host:port:...`.
  - Vị trí vật lý của dữ liệu `PlayerPrefs` trên máy người dùng.

## Discrepancies với `NroUnity_TREE.md`
- `Mod\ShowBoss.cs` nằm ở `Mod\`, không phải `Mod\XMAP\`.
- Báo cáo không nói `Game1`/`Game2` là hai bản sao theo namespace.
- `DIRECTORY_TREE.md` ghi "Unity Editor 202x"; thực tế 2022.3.62f2.
