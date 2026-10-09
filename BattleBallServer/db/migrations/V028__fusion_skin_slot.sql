-- BattleBallServer: the fusion skin has a slot of its own. Owner decision 2026-10-08: a fusion skin (kind FUSION_FORM) is worn by the FUSION FORM
-- (the main character while merged with the Potara earring) and changes how the fused form looks and adds its stats; the main and the partner can still wear
-- ordinary skins that only change their own stats (added to the fusion total) and never the look of the fusion. The slot is kept per character: it is the one
-- of the character that is the main of a fusion.
ALTER TABLE character_equipment MODIFY slot
  ENUM('ARMOR','PANTS','GLOVES','BOOTS','RING','SKIN','FUSION_SKIN','SUPPORT_1','SUPPORT_2','SUPPORT_3','SUPPORT_4') NOT NULL;

-- a fusion skin that was worn in the ordinary SKIN slot moves to the new slot and the character wears its race's default skin again
INSERT INTO character_equipment (character_id, slot, costume_id)
SELECT e.character_id, 'FUSION_SKIN', e.costume_id
  FROM character_equipment e
  JOIN skin_template s ON s.costume_id = e.costume_id
  JOIN skin_kind k ON k.kind_key = s.kind_key
 WHERE e.slot = 'SKIN' AND k.requires_fusion_main = 1;

UPDATE character_equipment e
  JOIN game_character c ON c.character_id = e.character_id
  JOIN race_default_skin d ON d.race = c.race AND d.is_equipped = 1
  JOIN skin_template s ON s.costume_id = e.costume_id
  JOIN skin_kind k ON k.kind_key = s.kind_key
   SET e.costume_id = d.costume_id
 WHERE e.slot = 'SKIN' AND k.requires_fusion_main = 1;
