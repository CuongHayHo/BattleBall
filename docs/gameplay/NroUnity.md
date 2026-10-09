# Gameplay: NroUnity (client)

> BattleBall analysis has not started yet.
> Chỉ ghi hệ thống đã xác minh. Nhãn: **CONFIRMED** / **INFERRED** / **UNKNOWN**.

`C:` = `E:\PRJNRO\NroUnity\Assets\Scripts\Assembly-CSharp\Game1\`

## Game loop
CONFIRMED:
- Chỉ có **một MonoBehaviour điều khiển là `Main`** (cộng `CoroutineRunner`). Gameplay không dùng GameObject, Prefab hay Animator.
- `Main.FixedUpdate()` (`C:Main.cs:294`) là game tick: `Rms.update()`, `ipKeyboard.update()`, `GameMidlet.gameCanvas.update()`, `Image.update()`, `DataInputStream.update()`. Fixed timestep 0,02 s = 50 Hz (`E:\PRJNRO\NroUnity\ProjectSettings\TimeManager.asset`).
- `Main.OnGUI()` (`C:Main.cs:157`): `checkInput()`, `Session_ME.update()` (xử lý gói mạng), rồi vẽ `gameCanvas.paint(g)` khi `EventType.Repaint`.
- `GameCanvas.update()` (`C:GameCanvas.cs:519`) → `currentScreen.update()` + `updateKey()`; màn hình là `mScreen` (`GameScr`, `LoginScr`…).
- `GameScr.update()` (`C:GameScr.cs:4292`): mod (`AutoXmap`, `ModFunc`, `PickMob`) → camera → `vCharInMap[i].update()` → `Char.myCharz().update()` → `EffecMn` → `vMob[i].update()` → `vNpc[i].update()` → `vItemMap[i].update()` → các `Effect2`…

## Entity
CONFIRMED:
- Danh sách tĩnh trong `GameScr`: `vCharInMap`, `vMob`, `vNpc`, `vItemMap` (kiểu `MyVector`), `Teleport.vTeleport`, `Effect2.v*`.
- `Char` (`C:Char.cs`, khoảng 7.900 dòng) vừa là dữ liệu, vừa là state machine, vừa tự vẽ. Nhân vật local là `Char.myCharz()`, pet là `Char.myPetz()`.
- `Mob` (`C:Mob.cs`), `Npc`, `ItemMap`; template tĩnh (`ItemTemplates`, `GameScr.sks`).

## Player controller, input, animation
CONFIRMED:
- Input: `Main.checkInput` → `GameCanvas`/`GameScr`; phím tắt skill `doSelectSkill(keySkill[i])` (`C:GameScr.cs:2522–2578`); chạm và chuột.
- Animation/state: số nguyên `Char.statusMe` quyết định hàm cập nhật: `updateCharStand`, `updateCharRun`, `updateCharJump`, `updateCharFall`, `updateCharFly`, `updateCharAutoJump`, `updateSkillStand/Fall` (`C:Char.cs:2906–3831`). Khung hình qua `cf`, `cp3`, `Part`/`PartImage`. Hiệu ứng skill qua `setSkillPaint`/`SkillPaint`.
- Collision client: `TileMap.tileTypeAt/tileTypeAtPixel` (tile 24 px); client tự tính vật lý cho nhân vật local.

## World/Map
- CONFIRMED: `TileMap` (`C:TileMap.cs`), `MapTemplate`, danh sách thực thể trong `GameScr`. Tải map: `Controller.loadInfoMap`, `loadCurrMap`.
- INFERRED: map và dữ liệu thực thể do server gửi (xem `docs\protocol\NroUnity.md`); chỉ 4 bản đồ mẫu nhúng trong `Resources\res\mymap`.

## Combat và Skill
CONFIRMED:
- Client tự chọn mục tiêu trong tầm (`Char.setSkillPaint`, khoảng `C:Char.cs:4760–4790`), phát hiệu ứng ngay, rồi `Service.sendPlayerAttack(vMob, vChar, type)`: chỉ mob → `54`; chỉ người → `-60`; cả hai → `-4` hoặc `67`.
- Sát thương, HP và chết đến từ server: `-60`, `-12`, `-9`, `-10`, `-11`.
- Chết: `-17` → `Char.startDie` (`meDead`); `-16` → `liveFromDead`; người khác `-8` → `waitToDie`.
- Chọn skill: `Service.selectSkill` (`34`).

## Item
- CONFIRMED: dùng item gửi `-43` (`Service.cs`); túi đồ là `Char.arrItemBag/Body/Box`. Hiệu ứng item: UNKNOWN.

## State: prediction và interpolation
CONFIRMED:
- **Nhân vật local, client-authoritative về vị trí.** Client di chuyển ngay theo input rồi `Service.charMove()` (gọi từ khoảng 15 chỗ trong `Char.cs`) gửi `-7` khi `cx/cy` khác lần gửi trước; byte đầu là 1 nếu đang ở tile trống (bay) và client tự trừ MP bay. Không có reconcile hay rollback.
- **Nhân vật khác, nội suy đơn giản.** `-7` (`C:Controller.cs:4048`) → `Char.moveTo(x, y, 0)` (`C:Char.cs:5826`): lệch quá 100 px ngang hoặc 300 px dọc thì dịch chuyển tức thì (tạo bóng); ngược lại suy ra hướng và hành động, đẩy vào `vMovePoints` để đi dần.
- **Mob**: server không gửi chuyển động; client tự cho mob đi trong `±rangeMove` quanh `xFirst` (`Mob.updateMobWalk/updateMobStandWait`, `C:Mob.cs:1008–1152`). Chỉ là mô phỏng hình ảnh.
- Dự đoán combat: hiệu ứng phát ngay, số liệu từ server.

## Important gameplay flows
- **Di chuyển**: `Char.updateChar*` → `Service.charMove` (`-7`) → server `PlayerService.playerMove` → broadcast `-7` → `Char.moveTo` ở client khác.
- **Tấn công**: `doSelectSkill` → `Char.setSkillPaint` → `Service.sendPlayerAttack` → server → `-60`/`-12`/`-9` → thanh HP, `startDie`, chữ bay.
- **Chết/hồi sinh**: `-17` → `startDie`; người chơi gửi `-16` → server → `-16` → `liveFromDead`.

## Mod (không thuộc game gốc: INFERRED)
`Mod\XMAP\*` (`AutoXmap`, `XmapAlgorithm`, `XmapController`), `PickMob`, `Mod\ShowBoss`, `AutoItem`, `ModFunc`; gọi từ đầu `GameScr.update()`.

## Important files
`C:Main.cs`, `C:GameCanvas.cs`, `C:GameScr.cs`, `C:Char.cs`, `C:Mob.cs`, `C:TileMap.cs`, `C:Service.cs`, `C:Controller.cs`, `C:ModFunc.cs`.

## UNKNOWN
- Cách client tính tầm đánh trong `setSkillPaint` so với server.
- Payload chi tiết `-9`, `-10`, `-11`.
- Phần còn lại của `charMove` (cách ghi tọa độ ngoài byte đầu).
