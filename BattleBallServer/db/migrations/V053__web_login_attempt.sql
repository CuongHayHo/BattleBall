-- BattleBallServer: failed logins of the web login page (web/login.php) and of the admin login page (admin/pages/auth.php), for the brute-force limit
-- (owner request 2026-10-13: the admin page no longer answers only to fixed IP addresses, so the password is the only door and wrong guesses are limited).
-- Only failures are recorded: at most 10 per IP and 30 per user name in one hour. Rows older than two days are deleted by the pages as they write.
SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS web_login_attempt (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  ip         VARCHAR(45)  NOT NULL,
  username   VARCHAR(64)  NOT NULL DEFAULT '',
  created_at TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  KEY idx_wla_ip_time (ip, created_at),
  KEY idx_wla_user_time (username, created_at),
  KEY idx_wla_time (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
