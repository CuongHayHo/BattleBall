-- BattleBallServer: one shared inventory per account instead of one per character. Owner decision 2026-10-08: "3 kho do dung chung",
-- including gold and gems. What moves from the character to the account: the wallet (gold, gems), material stacks, owned skins, gear in the bag
-- and support items. What stays with the character: level and EXP, skills, what it WEARS (character_equipment), daily counters, achievements,
-- first clears. A worn piece of gear or support item is used by one character at a time (character_equipment keeps a unique key on both).
-- Existing rows are merged: wallets and material stacks are summed over the account's characters, skins are the union.

-- 1) wallet: character_wallet becomes account_wallet
CREATE TABLE account_wallet (
  account_id BIGINT UNSIGNED NOT NULL,
  gold       BIGINT UNSIGNED NOT NULL DEFAULT 0,
  gems       BIGINT UNSIGNED NOT NULL DEFAULT 0,
  version    INT UNSIGNED    NOT NULL DEFAULT 1,
  updated_at TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (account_id),
  CONSTRAINT fk_awallet_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
INSERT INTO account_wallet (account_id, gold, gems)
SELECT c.account_id, SUM(w.gold), SUM(w.gems) FROM character_wallet w JOIN game_character c USING (character_id) GROUP BY c.account_id;
INSERT IGNORE INTO account_wallet (account_id) SELECT account_id FROM account;
DROP TABLE character_wallet;

-- 2) material stacks
CREATE TABLE material_stack_new (
  account_id  BIGINT UNSIGNED NOT NULL,
  template_id INT UNSIGNED    NOT NULL,
  quantity    INT UNSIGNED    NOT NULL DEFAULT 0,
  PRIMARY KEY (account_id, template_id),
  CONSTRAINT fk_mstack_account  FOREIGN KEY (account_id)  REFERENCES account (account_id) ON DELETE CASCADE,
  CONSTRAINT fk_mstack_template FOREIGN KEY (template_id) REFERENCES material_template (template_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
INSERT INTO material_stack_new (account_id, template_id, quantity)
SELECT c.account_id, m.template_id, SUM(m.quantity) FROM material_stack m JOIN game_character c USING (character_id) GROUP BY c.account_id, m.template_id;
DROP TABLE material_stack;
RENAME TABLE material_stack_new TO material_stack;

-- 3) owned skins (the equipment table no longer has a foreign key to them: the server checks ownership when a skin is worn)
ALTER TABLE character_equipment DROP FOREIGN KEY fk_equip_skin;
ALTER TABLE character_equipment DROP INDEX fk_equip_skin;
CREATE TABLE owned_skin_new (
  account_id  BIGINT UNSIGNED NOT NULL,
  costume_id  INT UNSIGNED    NOT NULL,
  source      ENUM('PURCHASE','DEFAULT','REWARD','ADMIN') NOT NULL,
  acquired_at TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (account_id, costume_id),
  CONSTRAINT fk_oskin_account  FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE,
  CONSTRAINT fk_oskin_template FOREIGN KEY (costume_id) REFERENCES skin_template (costume_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
INSERT INTO owned_skin_new (account_id, costume_id, source, acquired_at)
SELECT c.account_id, o.costume_id, MIN(o.source), MIN(o.acquired_at) FROM owned_skin o JOIN game_character c USING (character_id) GROUP BY c.account_id, o.costume_id;
DROP TABLE owned_skin;
RENAME TABLE owned_skin_new TO owned_skin;

-- 4) gear: owned by the account, worn by whoever character_equipment says
ALTER TABLE item_instance ADD COLUMN account_id BIGINT UNSIGNED NULL AFTER item_id;
UPDATE item_instance i JOIN game_character c ON c.character_id = i.character_id SET i.account_id = c.account_id;
ALTER TABLE item_instance MODIFY account_id BIGINT UNSIGNED NOT NULL;
ALTER TABLE item_instance DROP FOREIGN KEY fk_item_char;
ALTER TABLE item_instance DROP INDEX idx_item_char_loc;
ALTER TABLE item_instance DROP COLUMN character_id;
ALTER TABLE item_instance ADD KEY idx_item_account_loc (account_id, location, item_id);
ALTER TABLE item_instance ADD CONSTRAINT fk_item_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE;

-- 5) support items (pets, training armor, flags/wings/weapons, skill books)
ALTER TABLE character_support_item ADD COLUMN account_id BIGINT UNSIGNED NULL AFTER instance_id;
UPDATE character_support_item i JOIN game_character c ON c.character_id = i.character_id SET i.account_id = c.account_id;
ALTER TABLE character_support_item MODIFY account_id BIGINT UNSIGNED NOT NULL;
ALTER TABLE character_support_item DROP FOREIGN KEY fk_csi_char;
ALTER TABLE character_support_item DROP INDEX idx_csi_char;
ALTER TABLE character_support_item DROP COLUMN character_id;
ALTER TABLE character_support_item ADD KEY idx_csi_account (account_id);
ALTER TABLE character_support_item ADD CONSTRAINT fk_csi_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE;
