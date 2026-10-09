"""
Writes db/migrations/V029__auras.sql from tools/aura_planner/aura_levels.json (the table the owner made with the aura planner).

  python tools/make_aura_migration.py

The auras are kept in balance_config 'auras' (editable in the admin page "Cân bằng") as {"items":[{"id","name","level","enabled","layers":[{"layer","frames"}]}]}
and the account remembers the aura it wears (account.equipped_aura_id, NULL = none).
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "aura_planner", "aura_levels.json")
OUT = os.path.join(os.path.dirname(HERE), "db", "migrations", "V029__auras.sql")

data = json.load(open(SRC, encoding="utf-8"))
items = []
for a in sorted(data["auras"], key=lambda x: x["id"]):
    items.append({
        "id": a["id"], "name": a.get("name") or "", "level": a["level"], "enabled": bool(a.get("enabled", True)),
        "layers": [{"layer": l["layer"], "frames": l["frames"]} for l in sorted(a["layers"], key=lambda l: l["layer"])],
    })
payload = json.dumps({"items": items}, ensure_ascii=False, separators=(",", ":")).replace("'", "''")
sql = """-- BattleBallServer: auras (owner decision 2026-10-08). An aura is an achievement: the account unlocks it when its HIGHEST character reaches the level of the aura,
-- the player wears ONE aura at a time (a visual effect only, no stats) and it is shared by all three characters. The table of levels comes from the aura planner
-- (tools/aura_planner/aura_levels.json, made by tools/make_aura_migration.py): balance_config 'auras' = {"items":[{id, name, level, enabled, layers:[{layer, frames}]}]},
-- layer 0 is drawn behind the character and layer 1 in front of it, frames = equal parts of the picture (one on top of the other). Editable in the admin page.
SET NAMES utf8mb4;

INSERT INTO balance_config (config_key, value_json, description, updated_by) VALUES
  ('auras', '%s',
   'Aura: mở khi nhân vật cấp cao nhất của tài khoản đạt cấp; lớp 0 vẽ sau người, lớp 1 vẽ trước người; chỉ là hiệu ứng hình ảnh', 'V029')
  ON DUPLICATE KEY UPDATE value_json = VALUES(value_json), description = VALUES(description), updated_by = VALUES(updated_by);

UPDATE static_data_version SET version = version + 1, updated_by = 'V029' WHERE domain = 'BALANCE';

ALTER TABLE account ADD COLUMN equipped_aura_id INT UNSIGNED NULL;
""" % payload
with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    f.write(sql)
print("%d auras -> %s" % (len(items), OUT))
