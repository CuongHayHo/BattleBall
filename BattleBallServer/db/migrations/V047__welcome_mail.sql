-- BattleBallServer: welcome mail. Owner request 2026-10-11: every new account gets one mail with a gift as soon as the account is created,
-- and the admin can switch it off (after that, new accounts get no welcome mail; mails already sent stay in the mailboxes).
-- Both ways of creating an account (the game server, AccountRepository.createAccount, and the web page, web/lib.php create_account) read these two tables
-- in the same transaction as the account. Editable on the admin page "Quà tân thủ".
-- The gift of the first version (OWNER-given numbers): 1,000,000 gold, 10,000 gems and 200 of each of the five stones (#201 to #205).

CREATE TABLE welcome_mail_setting (
  setting_id TINYINT UNSIGNED NOT NULL,
  enabled    TINYINT(1)       NOT NULL DEFAULT 1,
  title      VARCHAR(80)      NOT NULL,
  body       VARCHAR(1000)    NOT NULL DEFAULT '',
  updated_at TIMESTAMP        NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by VARCHAR(64)      NULL,
  PRIMARY KEY (setting_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- same kinds and meaning as mail_attachment (V027)
CREATE TABLE welcome_gift (
  line_no  TINYINT UNSIGNED NOT NULL,
  kind     ENUM('GOLD','GEMS','SKIN','MATERIAL','ITEM','EARRING') NOT NULL,
  ref_id   INT UNSIGNED     NOT NULL DEFAULT 0,
  quantity BIGINT UNSIGNED  NOT NULL DEFAULT 1,
  PRIMARY KEY (line_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO welcome_mail_setting (setting_id, enabled, title, body, updated_by) VALUES
  (1, 1, 'Quà tân thủ', 'Chào mừng bạn đến với BattleBall! Hãy nhận quà tân thủ này để bắt đầu hành trình.', 'V047');

INSERT INTO welcome_gift (line_no, kind, ref_id, quantity) VALUES
  (1, 'GOLD', 0, 1000000),
  (2, 'GEMS', 0, 10000),
  (3, 'MATERIAL', 201, 200),
  (4, 'MATERIAL', 202, 200),
  (5, 'MATERIAL', 203, 200),
  (6, 'MATERIAL', 204, 200),
  (7, 'MATERIAL', 205, 200);
