# Gameplay: Teamobi2026 (server)

> BattleBall analysis has not started yet.
> Chỉ ghi hệ thống đã xác minh. Không phân tích từng skill/item riêng lẻ.
> Nhãn: **CONFIRMED** / **INFERRED** / **UNKNOWN**.

`S:` = `E:\PRJNRO\Teamobi2026\SRC\src\nro\models\`

## Game loop
CONFIRMED: không có tick toàn cục; nhiều vòng lặp độc lập trên thread riêng.

| Vòng lặp | Chu kỳ | Source |
|---|---|---|
| `Zone.update()` = `udMob` → `udItem` → `udPlayer` → `udNonInteractiveNPC` | 1 giây (`scheduleAtFixedRate`); các zone chia nhóm 10 chạy song song trên `executor` | `S:server\Manager.java` `initMap`, `S:map\Zone.java:182` |
| `Player.run()` → `update()` | 1 giây, **một thread cho mỗi người chơi** | `S:player\Player.java:419–430`; `start()` từ `Controller.sendInfo` |
| `BossManager.run()` → `boss.update()` | khoảng 1,5 giây | `S:boss\Boss_Manager\BossManager.java:481–488` |
| Thread khác: `Client.run`, Super Rank, ĐHVT, Shenron, minigame, cầu nối web, tự lưu 2 phút, cập nhật top | thread riêng | `S:server\ServerManager.java` `run()` |
- CONFIRMED: `Player.update()` của người chơi thật có hai đường gọi mỗi giây (thread riêng và `Zone.udPlayer`, vốn duyệt `notBosses`). Hệ quả cụ thể: UNKNOWN (chưa đọc hết `update()`).
- CONFIRMED: thao tác người chơi chạy trên thread Collector của session, song song với các vòng lặp trên. Chỉ `Player.injured` có `synchronized`; `Mob.injured` thì không.
- CONFIRMED: không có event bus; "sự kiện" là các manager chạy thread riêng và các hàm `check…` gọi trực tiếp (ví dụ `TaskService.checkDoneTaskKillMob`).
- CONFIRMED: thời gian dùng `System.currentTimeMillis()` + `Util.canDoWithTime(last, interval)`.

## Entity
CONFIRMED:
- `Player` là lớp gốc cho mọi thực thể dạng người: người chơi, `Pet`, `NewPet`, Bot, `Boss extends Player implements IBoss` (`S:boss\Boss.java:65`), `NonInteractiveNPC`. Phân biệt bằng cờ (`isPl()`, `isPet`, `isBot`, `isBoss`, `isNewPet`). Không có ECS/Entity chung.
- `Mob` (`S:mob\Mob.java`): `point.hp/maxHp`, `location`, `effectSkill`, `temporaryEnemies`.
- `ItemMap` (`S:map\ItemMap.java`): vật phẩm rơi, có `update()`.
- NPC: `Npc`, `NpcFactory`, `npc_list\*`, gắn vào `Map` (`Map.initNpc`).
- **Không có entity đạn (projectile) ở server**: chưởng được tính ngay khi nhận lệnh.
- Quản lý: `Zone` giữ nhiều danh sách song song `players`, `humanoids`, `notBosses`, `bosses`, `pets`, `nonInteractiveNPCs`, `mobs`, `items`, `trapMaps`; `Zone.addPlayer/removePlayer` phân loại theo cờ. Online toàn server: `S:server\Client.java`.

## Player/Character
- Vòng đời: login (`MrBlue.loadPlayer`) → `zone.addPlayer` → `Client.put` → `Controller.sendInfo` → `player.start()` → … → `Client.remove(Player)` (rời map, huỷ giao dịch, lưu DB) + `dispose`. CONFIRMED.
- Chỉ số: `NPoint` (`S:player\NPoint.java`): `hp/hpMax`, `mp/mpMax`, `dame`, `def`, `crit`, `power`, `tiemNang`, `stamina`, các tỷ lệ né/giáp. `calPoint()` tính lại từ gốc, đồ và set đồ; `update()` lo hồi khi "charge" và hồi định kỳ 30 giây. CONFIRMED.
- Trạng thái: cờ rải rác trên `Player`, `EffectSkill`, `EffectSkin`, `ItemTime`, `Charms`; không có state machine tập trung (trừ Boss: `BossStatus` REST → RESPAWN → JOIN_MAP → CHAT_S → ACTIVE → CHAT_E → LEAVE_MAP/DIE, `Boss.changeStatus`/`update`). CONFIRMED.
- Buff/debuff: `EffectSkill.update()` hết hạn từng hiệu ứng theo thời gian; `EffectSkillService` bật hiệu ứng và gửi gói. CONFIRMED.
- Túi đồ/trang bị: `Inventory` (`itemsBody`, `itemsBag`, `itemsBox`, tiền); `InventoryService`, `ItemService`; `setClothes` tính bonus set. CONFIRMED về cấu trúc; thao tác mặc/tháo: UNKNOWN.

## World/Map
CONFIRMED:
- `Map` (template + `tileMap[y][x]` + waypoint + NPC) chứa N `Zone` (kênh); mỗi zone có mob và người chơi riêng (`Manager.initMap`).
- Spawn: mob tạo một lần từ `map_template` (`Map.initMob`); hồi sinh trong `Mob.update()` (3 giây ở map thường, phó bản có luật riêng); boss do `BossManager` (`Map.initBoss`, `Boss.joinMap`).
- Physics/va chạm: `Map.yPhysicInTop`, `tileTypeAt`, `LastY`, `moveXY` (khoảng 228 chỗ dùng), chủ yếu cho item rơi, boss, pet, bot. **Đường đi của người chơi không bị kiểm tra va chạm.**
- Đổi map: `ChangeMapService` (`changeMapWaypoint`, `changeMap`, `changeMapBySpaceShip`, `changeZone`, `goToMap` = `exitMap` + gán `zone` mới + `addPlayer`). Server xác minh người chơi đứng trong vùng waypoint (`MapService.getWaypointPlayerIn`), nhưng vị trí là vị trí do client báo.

## Combat và Skill
CONFIRMED:
- Lối vào: opcode `54` (đánh mob) → `Service.attackMob`; `-60` (đánh người) → `Service.attackPlayer`; `-45` (skill không mục tiêu). Tất cả đến `SkillService.useSkill` (`S:services\SkillService.java:47`).
- `useSkill` kiểm tra: cùng bang, mục tiêu vừa hồi sinh dưới 1,5 giây, đang bị khống chế, mana (`canUseSkillWithMana`), cooldown (`canUseSkillWithCooldown`: `lastTimeUseThisSkill`, `coolDown - 50`); rồi rẽ theo `template.type`: 1 → `useSkillAttack`, 2 → `useSkillBuffToPlayer`, 3 → `useSkillAlone`, 4 → `useNewSkillNotFocus`.
- Chọn mục tiêu: **do client chỉ định** (id mob hoặc id player); server chỉ tìm đối tượng trong zone theo id.
- Khoảng cách: chỉ skill cận chiến (Dragon, Demon, Galick, Liên hoàn) so `Util.getDistance` với `Skill.RANGE_ATTACK_CHIEU_DAM`, vượt thì `miss`; chưởng (Kamejoko, Masenko, Antomic) không kiểm tra; skill loại 4 dùng bán kính 500.
- Damage: `NPoint.getDameAttack()` (`S:player\NPoint.java:1350`) → `Player.injured(plAtt, dame, piercing, isMobAttack)` (`S:player\Player.java:1047`, `synchronized`) áp giáp, né, khiên, luật map; `Mob.injured` (`S:mob\Mob.java:117`) áp luật mob. Sát thương ép `int`.
- Hậu quả: trừ MP và đặt cooldown (`affterUseSkill`), hút máu (`hutHPMP`), phản sát thương (`phanSatThuong`), cộng sức mạnh/tiềm năng (`Service.addSMTN`), nhiệm vụ, thành tựu.
- Chết:
  - Mob: `setDie` → gói `-12` (kèm drop) → `mobReward`/`getItemMobReward` tạo `ItemMap` → `hutItem`.
  - Người chơi: `Player.setDie(plAtt)` (`Player.java:1233`) rơi vàng, có thể trừ sức mạnh theo map, xoá hiệu ứng, hp=mp=0, `Service.charDie` (`-17`), thêm kẻ thù, xử thua PvP, rơi ngọc rồng đen.
- Hồi sinh: người chơi gửi `-16` → `PlayerService.hoiSinh` (trả ngọc/vàng, chờ 1,5 giây) → `Service.hsChar` (`-16`); hoặc `-15` về nhà. Mob tự hồi trong `Mob.update`.
- AI mob: `Mob.update` → `attack()` → `getPlayerCanAttack` (ưu tiên `temporaryEnemies` trong 300 px, sau đó người gần trong 100 px với mob cấp cao) → `mobAttackPlayer`. **Mob đứng yên ở server.**
- Skill: `PlayerSkill` (`skills`, `skillSelect`, `skillShortCut`); chọn skill qua opcode `34` (`SkillService.selectSkill`).

## Item
- Mặt đất: `ItemMap.update()` (hết hạn, vệ tinh, ngọc rồng đen bám người chơi). CONFIRMED.
- Dùng item: opcode `-43` → `S:services_func\UseItem.doItem`: huỷ giao dịch, lấy `itemsBag[index]`, một số loại hỏi xác nhận (server gửi lại `-43`). Hiệu ứng từng item: UNKNOWN (không trace).

## State
CONFIRMED:
```
Gói tin (thread Collector) → Controller.onMessage (switch) → *Service sửa thẳng field của Player/NPoint/Mob/Zone
 → tạo Message tại chỗ → player.sendMessage / Service.sendMessAllPlayerInMap(zone)
 → DB chỉ khi tự lưu 2 phút hoặc ngắt kết nối
```
Không có snapshot hay delta theo tick; mỗi thay đổi gửi ngay thành một sự kiện.

## Important gameplay flows
**Di chuyển**: `Controller case -7` (bỏ qua nếu chết hoặc bị khống chế; kiểm tra khoảng cách tối đa 500 chỉ ở map NRSĐ) → `PlayerService.playerMove` (gán `location`, xử lý rơi khỏi map ở một số map, `isFly` theo tile, trừ MP bay, pet bám theo) → `MapService.sendPlayerMove` (`-7`) cho người khác. CONFIRMED.

**Đánh mob**: `54` → `Service.attackMob` → `useSkill` → `useSkillAttack` (stamina, miss nếu cận chiến quá xa) → `playerAttackMob` → `getDameAttack(true)` → `Mob.injured` → còn sống `sendMobStillAliveAffterAttacked`, chết `setDie` + `-12` + nhiệm vụ → `addSMTN`. CONFIRMED.

**Đánh người và chết**: `-60` → `playerAttackPlayer` → `Player.injured` → broadcast `-60` → nếu HP ≤ 0 `Player.setDie` → `-17` → `-16` → `hoiSinh` → `hsChar`. CONFIRMED.

**Mob đánh người**: `Zone.update` → `Mob.update` → `attack()` → `mobAttackPlayer` → `Player.injured(…, isMobAttack=true)`. CONFIRMED.

**Đổi map**: `-23/-33` → `changeMapWaypoint` → `getWaypointPlayerIn` → `getMapCanJoin` → luật đặc biệt → `goToMap` → `mapInfo` (`-24`); client trả `-39` → `finishLoadMap`. CONFIRMED.

## Important files
`S:server\Manager.java`, `S:server\ServerManager.java`, `S:server\Controller.java`, `S:player\Player.java`, `S:player\NPoint.java`, `S:player\EffectSkill.java`, `S:player\Inventory.java`, `S:map\Zone.java`, `S:map\Map.java`, `S:map\ItemMap.java`, `S:mob\Mob.java`, `S:services\SkillService.java`, `S:services\PlayerService.java`, `S:services\Service.java`, `S:services\EffectSkillService.java`, `S:map\service\ChangeMapService.java`, `S:map\service\MapService.java`, `S:services_func\UseItem.java`, `S:boss\Boss.java`, `S:boss\Boss_Manager\BossManager.java`.

## UNKNOWN
- `Player.update()` có bị gọi trùng thật không và hệ quả.
- `Map.run()` có được khởi chạy ở đâu không.
- Chi tiết `getDameAttack`/`injured` (thứ tự crit, giáp, né) và luật PvP (`canAttackPlayer`, `typePk`).
- Mặc/tháo đồ, `setClothes`, `calPoint` đầy đủ; AI Bot và Pet.
- Payload của `-9`, `-10`, `-11`.
