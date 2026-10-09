-- BattleBallServer: attempts at the web registration page, for the per-IP limit (CONFIRMED: registration over the web is limited per IP and has a captcha).
-- The PHP page web/register.php reads and writes this table; the game server does not use it.

SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS web_register_attempt (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  ip         VARCHAR(45)  NOT NULL,
  success    TINYINT(1)   NOT NULL DEFAULT 0,
  created_at TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  KEY idx_wra_ip_time (ip, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
