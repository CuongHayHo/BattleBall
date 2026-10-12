"""Writes db/migrations/V050__signup_campaign_gift.sql: time-limited gifts for NEW accounts, and the gifts of the owner's second request.

Owner requests 2026-10-12: (1) "đã tặng cho cả những tài khoản mới tạo chưa, kiểu khi tạo tài khoản xong vẫn có phần thưởng này" (the gift of V049 must reach accounts made later too);
(2) "thêm cả phần quà 100 mảnh bông tai, kèm 100 mảnh đồ các loại" (100 Potara earring shards and 100 of every gear fragment, added to the gift).

A campaign is a mail that every new account gets while the campaign is on (enabled and ends_at in the future); the mail expires when the campaign ends. A mail shows at most five gifts
(the five picture cells of the popup), so the gift is cut into mails of at most five lines: 1 cards and EXP bag (the mail of V049), 2 the earring shards, 3 to 5 the fragments of the three sets
(Thần Linh 101-105, Hủy Diệt 106-110, Thiên Sứ 111-115, five slots each). The migration also sends, to every existing account, the mails of the campaigns it does not have yet.
The earring shards are the Potara shard 1820: the claim keeps them on the account, at most 100, and none once the earring exists (MailService.grantShards).

Run from anywhere:  python tools/build_signup_campaign_sql.py   (the result is a migration: once applied it is never edited)
"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'db', 'migrations', 'V050__signup_campaign_gift.sql')
ENDS = '2026-10-17 23:59:59'          # the end of Saturday of the week of the request (server time, Vietnam)
TAIL = ' Nhớ nhận quà trước hết thứ Bảy 17/10, quá hạn sẽ mất quà.'

CAMPAIGNS = [
    (1, 'Quà tặng: 99 thẻ nâng cấp và túi kinh nghiệm',
     'Tặng bạn 99 Thẻ nâng cấp kỹ năng và 1 Túi kinh nghiệm (dùng cho một nhân vật, đủ để lên cấp 100).' + TAIL,
     [(206, 99), (207, 1)]),
    (2, 'Quà tặng: 100 mảnh Bông tai Potara',
     'Tặng bạn 100 mảnh Bông tai Potara, vừa đủ để ghép một Bông tai (mỗi tài khoản chỉ có một Bông tai, mảnh dư sẽ không cộng thêm).' + TAIL,
     [(1820, 100)]),
    (3, 'Quà tặng: 100 mảnh đồ Thần Linh',
     'Tặng bạn 100 mảnh của mỗi loại đồ Thần Linh (áo, quần, găng, giày, nhẫn). Ghép đồ ở Bunma.' + TAIL,
     [(i, 100) for i in range(101, 106)]),
    (4, 'Quà tặng: 100 mảnh đồ Hủy Diệt',
     'Tặng bạn 100 mảnh của mỗi loại đồ Hủy Diệt (áo, quần, găng, giày, nhẫn). Ghép đồ ở Bunma.' + TAIL,
     [(i, 100) for i in range(106, 111)]),
    (5, 'Quà tặng: 100 mảnh đồ Thiên Sứ',
     'Tặng bạn 100 mảnh của mỗi loại đồ Thiên Sứ (áo, quần, găng, giày, nhẫn). Ghép đồ ở Bunma.' + TAIL,
     [(i, 100) for i in range(111, 116)]),
]


def q(text):
    return text.replace('\\', '\\\\').replace("'", "''")


def main():
    camp = ",\n".join("  (%d, 1, '%s',\n   '%s',\n   '%s', 'V050')" % (cid, q(title), q(body), ENDS) for cid, title, body, _ in CAMPAIGNS)
    lines = ",\n".join("  (%d, %d, 'MATERIAL', %d, %d)" % (cid, n + 1, ref, qty) for cid, _, _, gifts in CAMPAIGNS for n, (ref, qty) in enumerate(gifts))
    sql = """-- BattleBallServer: limited-time gifts for new accounts, and the second request of the owner (docs 15.35). Written by tools/build_signup_campaign_sql.py.
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
%s;

INSERT INTO signup_campaign_gift (campaign_id, line_no, kind, ref_id, quantity) VALUES
%s;

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
""" % (camp, lines)
    with open(OUT, 'w', encoding='utf-8', newline='') as f:
        f.write(sql)
    print('wrote', os.path.normpath(OUT))


main()
