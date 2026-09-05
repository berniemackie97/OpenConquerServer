-- Fresh-database baseline for the OpenConquer AccountServer schema contract.
-- This script is intentionally not idempotent. Apply it only to an empty
-- database so an existing or partially provisioned schema fails closed.

CREATE TABLE player_permission_types (
    permission_code INT UNSIGNED NOT NULL,
    name VARCHAR(32) NOT NULL,
    PRIMARY KEY (permission_code)
) ENGINE = InnoDB
  DEFAULT CHARACTER SET = utf8mb4
  COLLATE = utf8mb4_0900_ai_ci;

INSERT INTO player_permission_types (permission_code, name)
VALUES
    (0, 'Error'),
    (1, 'Player'),
    (2, 'Helper'),
    (3, 'Moderator'),
    (4, 'PM'),
    (5, 'GM'),
    (255, 'Banned');

CREATE TABLE accounts (
    uid INT UNSIGNED NOT NULL AUTO_INCREMENT,
    username VARCHAR(32) NOT NULL,
    password VARCHAR(255) NOT NULL,
    email VARCHAR(255) NOT NULL,
    email_ver VARCHAR(255) NOT NULL,
    email_status TINYINT UNSIGNED NOT NULL DEFAULT 0,
    security_answer VARCHAR(128) NOT NULL,
    security_question VARCHAR(128) NOT NULL,
    permission INT UNSIGNED NOT NULL DEFAULT 0,
    timestamp_token INT UNSIGNED NOT NULL DEFAULT 0,
    registration_operation_id CHAR(64)
        CHARACTER SET ascii
        COLLATE ascii_bin
        NULL,
    PRIMARY KEY (uid),
    UNIQUE KEY UX_accounts_username (username),
    UNIQUE KEY UX_accounts_registration_operation_id
        (registration_operation_id),
    KEY IX_accounts_permission (permission),
    CONSTRAINT FK_accounts_permission_types
        FOREIGN KEY (permission)
        REFERENCES player_permission_types (permission_code)
        ON DELETE RESTRICT
        ON UPDATE RESTRICT
) ENGINE = InnoDB
  DEFAULT CHARACTER SET = utf8mb4
  COLLATE = utf8mb4_0900_ai_ci;

CREATE TABLE login_tickets (
    ticket_id INT UNSIGNED NOT NULL,
    hash INT UNSIGNED NOT NULL,
    account_id INT UNSIGNED NOT NULL,
    username VARCHAR(32) NOT NULL,
    issued_at_utc DATETIME(6) NOT NULL,
    expires_at_utc DATETIME(6) NOT NULL,
    session_uid INT UNSIGNED NOT NULL,
    PRIMARY KEY (ticket_id),
    KEY IX_login_tickets_account (account_id),
    KEY IX_login_tickets_expires (expires_at_utc),
    KEY IX_login_tickets_username (username),
    KEY IX_login_tickets_session_uid (session_uid),
    CONSTRAINT FK_login_tickets_accounts
        FOREIGN KEY (account_id)
        REFERENCES accounts (uid)
        ON DELETE CASCADE
        ON UPDATE RESTRICT,
    CONSTRAINT chk_login_tickets_times
        CHECK (expires_at_utc >= issued_at_utc)
) ENGINE = InnoDB
  DEFAULT CHARACTER SET = utf8mb4
  COLLATE = utf8mb4_0900_ai_ci;

CREATE TABLE openconquer_schema_versions (
    component VARCHAR(64)
        CHARACTER SET ascii
        COLLATE ascii_bin
        NOT NULL,
    version INT UNSIGNED NOT NULL,
    migration_id VARCHAR(128)
        CHARACTER SET ascii
        COLLATE ascii_bin
        NOT NULL,
    applied_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (component),
    CONSTRAINT chk_openconquer_schema_versions_version
        CHECK (version > 0)
) ENGINE = InnoDB
  DEFAULT CHARACTER SET = utf8mb4
  COLLATE = utf8mb4_0900_ai_ci;

INSERT INTO openconquer_schema_versions (
    component,
    version,
    migration_id,
    applied_at_utc)
VALUES (
    'account-server',
    2,
    '20260812_02_remove_legacy_hash_token',
    UTC_TIMESTAMP(6));
