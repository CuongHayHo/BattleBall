-- BattleBallServer: mailbox (gifts from the admin) and notices. Owner decision 2026-10-08: the main menu gets a "Chuc nang" tab with
-- Hom thu (to receive gifts from the admin), Thong bao and Tai khoan (log out, change password). This REPLACES the older decision
-- "the mailbox is dropped" (docs/database/BattleBall.md 10.x): from now on the only mail is the one the admin sends.
-- A mail belongs to one account. "Send to everyone" is done by the admin page as one row per existing account (so every player owns, reads,
-- claims and deletes their own copy). A claim pays every attachment in one transaction and sets claimed_at, so a gift can never be taken twice.

CREATE TABLE mail (
  mail_id    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  account_id BIGINT UNSIGNED NOT NULL,
  title      VARCHAR(80)     NOT NULL,
  body       VARCHAR(1000)   NOT NULL DEFAULT '',
  sent_at    TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  read_at    TIMESTAMP       NULL,
  claimed_at TIMESTAMP       NULL,
  sent_by    VARCHAR(64)     NULL,
  PRIMARY KEY (mail_id),
  KEY idx_mail_account (account_id, mail_id),
  CONSTRAINT fk_mail_account FOREIGN KEY (account_id) REFERENCES account (account_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- kind: GOLD, GEMS (quantity = amount), SKIN (ref_id = costume id), MATERIAL (ref_id = material template, includes the Potara shard #1820),
-- ITEM (ref_id = gear template, quantity pieces go to the shared bag), EARRING (the Potara earring #1819, one per account)
CREATE TABLE mail_attachment (
  mail_id  BIGINT UNSIGNED NOT NULL,
  line_no  TINYINT UNSIGNED NOT NULL,
  kind     ENUM('GOLD','GEMS','SKIN','MATERIAL','ITEM','EARRING') NOT NULL,
  ref_id   INT UNSIGNED    NOT NULL DEFAULT 0,
  quantity BIGINT UNSIGNED NOT NULL DEFAULT 1,
  PRIMARY KEY (mail_id, line_no),
  CONSTRAINT fk_mail_attachment_mail FOREIGN KEY (mail_id) REFERENCES mail (mail_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Notices shown to every player (maintenance, events, news). Written on the admin page.
CREATE TABLE notice (
  notice_id  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  title      VARCHAR(80)     NOT NULL,
  body       VARCHAR(2000)   NOT NULL DEFAULT '',
  created_at TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  expires_at TIMESTAMP       NULL,
  enabled    TINYINT(1)      NOT NULL DEFAULT 1,
  created_by VARCHAR(64)     NULL,
  PRIMARY KEY (notice_id),
  KEY idx_notice_active (enabled, notice_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- item_log: a mail gift is logged as REWARD (the ENUM already has it)
