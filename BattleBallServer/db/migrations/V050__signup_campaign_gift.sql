-- BattleBallServer: limited-time gifts for new accounts, and the second request of the owner (docs 15.35). Written by tools/build_signup_campaign_sql.py.
-- Owner requests 2026-10-12: new accounts also get the gift of V049; add 100 Potara earring shards and 100 of every gear fragment.
-- A campaign is one mail that an account gets when it is created while the campaign is on (enabled and ends_at in the future); the mail expires when the campaign ends.
-- Both ways of creating an account read these tables in the same transaction as the account: the game server (AccountRepository.sendCampaignMails) and the web page (web/lib.php).
-- This migration also sends to every account that exists now the mails of the campaigns it does not have yet (sent_by 'campaign:N'; the mail of V049 counts as campaign 1).
SET NAMES utf8mb4;

CREATE TABLE signup_campaign (
  campaign_id TINYINT UNSIGNED NOT NULL,
  enabled     TINYINT(1)       NOT NULL DEFAULT 1,
  title       VARCHAR(80)      NOT NULL,
  body        VARCHAR(1000)    NOT NULL DEFAULT '',
  ends_at     TIMESTAMP        NOT NULL,
  updated_at  TIMESTAMP        NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  updated_by  VARCHAR(64)      NULL,
  PRIMARY KEY (campaign_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- same kinds and meaning as mail_attachment (V027): at most five lines make sense, the popup of a mail shows five gifts
CREATE TABLE signup_campaign_gift (
  campaign_id TINYINT UNSIGNED NOT NULL,
  line_no     TINYINT UNSIGNED NOT NULL,
  kind        ENUM('GOLD','GEMS','SKIN','MATERIAL','ITEM','EARRING') NOT NULL,
  ref_id      INT UNSIGNED     NOT NULL DEFAULT 0,
  quantity    BIGINT UNSIGNED  NOT NULL DEFAULT 1,
  PRIMARY KEY (campaign_id, line_no),
  CONSTRAINT fk_signup_campaign_gift FOREIGN KEY (campaign_id) REFERENCES signup_campaign (campaign_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO signup_campaign (campaign_id, enabled, title, body, ends_at, updated_by) VALUES
  (1, 1, 'Quà tặng: 99 thẻ nâng cấp và túi kinh nghiệm',
   'Tặng bạn 99 Thẻ nâng cấp kỹ năng và 1 Túi kinh nghiệm (dùng cho một nhân vật, đủ để lên cấp 100). Nhớ nhận quà trước hết thứ Bảy 17/10, quá hạn sẽ mất quà.',
   '2026-10-17 23:59:59', 'V050'),
  (2, 1, 'Quà tặng: 100 mảnh Bông tai Potara',
   'Tặng bạn 100 mảnh Bông tai Potara, vừa đủ để ghép một Bông tai (mỗi tài khoản chỉ có một Bông tai, mảnh dư sẽ không cộng thêm). Nhớ nhận quà trước hết thứ Bảy 17/10, quá hạn sẽ mất quà.',
   '2026-10-17 23:59:59', 'V050'),
  (3, 1, 'Quà tặng: 100 mảnh đồ Thần Linh',
   'Tặng bạn 100 mảnh của mỗi loại đồ Thần Linh (áo, quần, găng, giày, nhẫn). Ghép đồ ở Bunma. Nhớ nhận quà trước hết thứ Bảy 17/10, quá hạn sẽ mất quà.',
   '2026-10-17 23:59:59', 'V050'),
  (4, 1, 'Quà tặng: 100 mảnh đồ Hủy Diệt',
   'Tặng bạn 100 mảnh của mỗi loại đồ Hủy Diệt (áo, quần, găng, giày, nhẫn). Ghép đồ ở Bunma. Nhớ nhận quà trước hết thứ Bảy 17/10, quá hạn sẽ mất quà.',
   '2026-10-17 23:59:59', 'V050'),
  (5, 1, 'Quà tặng: 100 mảnh đồ Thiên Sứ',
   'Tặng bạn 100 mảnh của mỗi loại đồ Thiên Sứ (áo, quần, găng, giày, nhẫn). Ghép đồ ở Bunma. Nhớ nhận quà trước hết thứ Bảy 17/10, quá hạn sẽ mất quà.',
   '2026-10-17 23:59:59', 'V050');

INSERT INTO signup_campaign_gift (campaign_id, line_no, kind, ref_id, quantity) VALUES
  (1, 1, 'MATERIAL', 206, 99),
  (1, 2, 'MATERIAL', 207, 1),
  (2, 1, 'MATERIAL', 1820, 100),
  (3, 1, 'MATERIAL', 101, 100),
  (3, 2, 'MATERIAL', 102, 100),
  (3, 3, 'MATERIAL', 103, 100),
  (3, 4, 'MATERIAL', 104, 100),
  (3, 5, 'MATERIAL', 105, 100),
  (4, 1, 'MATERIAL', 106, 100),
  (4, 2, 'MATERIAL', 107, 100),
  (4, 3, 'MATERIAL', 108, 100),
  (4, 4, 'MATERIAL', 109, 100),
  (4, 5, 'MATERIAL', 110, 100),
  (5, 1, 'MATERIAL', 111, 100),
  (5, 2, 'MATERIAL', 112, 100),
  (5, 3, 'MATERIAL', 113, 100),
  (5, 4, 'MATERIAL', 114, 100),
  (5, 5, 'MATERIAL', 115, 100);

-- every account that exists now gets the mails of the campaigns it does not have yet
INSERT INTO mail (account_id, title, body, sent_by, expires_at)
  SELECT a.account_id, c.title, c.body, CONCAT('campaign:', c.campaign_id), c.ends_at
  FROM account a JOIN signup_campaign c
  WHERE c.enabled = 1 AND c.ends_at > NOW()
    AND NOT EXISTS (SELECT 1 FROM mail m WHERE m.account_id = a.account_id
                    AND (m.sent_by = CONCAT('campaign:', c.campaign_id) OR (c.campaign_id = 1 AND m.sent_by = 'V049')));

INSERT INTO mail_attachment (mail_id, line_no, kind, ref_id, quantity)
  SELECT m.mail_id, g.line_no, g.kind, g.ref_id, g.quantity
  FROM mail m JOIN signup_campaign_gift g ON m.sent_by = CONCAT('campaign:', g.campaign_id);
