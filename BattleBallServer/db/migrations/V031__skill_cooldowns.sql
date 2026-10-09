-- BattleBallServer: skill cooldowns that run on the real clock of the server (owner request 2026-10-09: logging out must not reset a cooldown).
-- One row per character and skill while it is cooling down; ready_at_ms is a Unix time in milliseconds. A session writes the rows when it ends (training or match,
-- also when the connection drops), the next session and the player snapshot read them. Rows already past are deleted when they are written again.
SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS character_skill_cooldown (
  character_id BIGINT UNSIGNED   NOT NULL,
  skill_id     SMALLINT UNSIGNED NOT NULL,
  ready_at_ms  BIGINT UNSIGNED   NOT NULL,              -- Unix ms (System.currentTimeMillis of the server) when the skill can be used again
  PRIMARY KEY (character_id, skill_id),
  CONSTRAINT fk_cscd_char  FOREIGN KEY (character_id) REFERENCES game_character (character_id) ON DELETE CASCADE,
  CONSTRAINT fk_cscd_skill FOREIGN KEY (skill_id)     REFERENCES skill_template (skill_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
