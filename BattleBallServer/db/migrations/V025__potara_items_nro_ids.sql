-- BattleBallServer: the Potara items use the Nro ids, as the gem (#674) and gold (#190) already do. Owner decision 2026-10-07: "only the level 3 earring":
--   shard    #1820 "Manh vo bong tai cap 3" (icon 15613)  replaces our own #900 "Manh bong tai Potara"
--   earring  #1819 "Bong tai Porata" (icon 15614)         replaces #454 of V024 (the Nro level 1 earring; #921 and #454 are not used)
-- Everything that pointed at #900 (drop rules, dungeon multipliers, recipes, stacks, the daily purchase counter) is moved to #1820, then #900 is removed.
-- the key is unique: free it for the new row (the old row is deleted below)
UPDATE material_template SET material_key = 'potara_shard_old' WHERE template_id = 900;
INSERT INTO material_template (template_id, material_key, kind, name_vi, item_template_id, buy_currency, price_wins, price_fixed, max_stack, icon_id, enabled, note, updated_by)
SELECT 1820, 'potara_shard', kind, 'Mảnh vỡ bông tai cấp 3', item_template_id, buy_currency, price_wins, price_fixed, max_stack, 15613, enabled,
       'Nro #1820. Tài khoản cần 100 mảnh; mua tối đa 20 mảnh mỗi ngày (balance_config potara); ngừng rơi khi đã có bông tai', 'V025'
  FROM material_template WHERE template_id = 900;

UPDATE drop_rule           SET material_template_id = 1820 WHERE material_template_id = 900;
UPDATE dungeon_drop_mult   SET material_template_id = 1820 WHERE material_template_id = 900;
UPDATE craft_recipe        SET boost_material_id    = 1820 WHERE boost_material_id    = 900;
UPDATE craft_recipe_input  SET material_template_id = 1820 WHERE material_template_id = 900;
UPDATE material_stack      SET template_id          = 1820 WHERE template_id          = 900;
UPDATE daily_purchase_counter SET shop_item_id      = 1820 WHERE shop_item_id         = 900;
DELETE FROM material_template WHERE template_id = 900;

UPDATE balance_config
   SET value_json = JSON_SET(value_json, '$.item_id', 1819, '$.icon_id', 15614, '$.shard_item_id', 1820, '$.shard_icon_id', 15613),
       updated_by = 'V025'
 WHERE config_key = 'potara';
