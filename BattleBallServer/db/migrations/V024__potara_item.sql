-- BattleBallServer: the Potara earring as an item. It lives in the bag of the client as the FIRST slot (CONFIRMED by the user), is per account and never
-- consumed (V003). The item id is the Nro one: ConstItem.BONG_TAI_PORATA = 454 in Teamobi2026 (the base earring, "Bong tai Porata", icon 3896; the Nro
-- upgrade tiers 921 (icon 7993) and 1819 (icon 15614) are not used: BattleBall has one earring). The server sends item_id, icon_id, the number of shards
-- and the minimum fusion level in PotaraInfo so that the client needs no table of its own. Everything stays editable in the admin page (balance_config potara).
UPDATE balance_config
   SET value_json = JSON_SET(value_json, '$.item_id', 454, '$.icon_id', 3896),
       updated_by = 'V024'
 WHERE config_key = 'potara';
