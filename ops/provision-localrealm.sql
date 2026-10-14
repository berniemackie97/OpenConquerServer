-- Development-only one-time provisioning. Runs on a single MySQL instance
-- holding both openconquer_accounts and openconquer_game.
-- Execute as an administrative identity after stopping both servers and backing up.
-- Automatic fail-closed checks; a conflict aborts before committing either row.
DELIMITER $$
CREATE PROCEDURE `openconquer_accounts`.`provision_localrealm_once`()
BEGIN
    DECLARE accounts_version BIGINT UNSIGNED;
    DECLARE game_version BIGINT UNSIGNED;
    DECLARE existing_accounts INT DEFAULT 0;
    DECLARE existing_game INT DEFAULT 0;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        RESIGNAL;
    END;

    START TRANSACTION;
    SELECT schema_version INTO accounts_version
        FROM `openconquer_accounts`.`schema_compatibility`
        WHERE component_name = 'accounts' FOR UPDATE;
    SELECT schema_version INTO game_version
        FROM `openconquer_game`.`schema_compatibility`
        WHERE component_name = 'game' FOR UPDATE;
    -- realms arrives in Accounts v3 and realm_binding in Game v10.
    IF accounts_version IS NULL OR accounts_version < 3
        OR game_version IS NULL OR game_version < 10 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Accounts schema v3 and Game schema v10 or later are required';
    END IF;

    SELECT COUNT(*) INTO existing_accounts FROM `openconquer_accounts`.`realms`
        WHERE realm_id = 1 OR protocol_server_name = 'LocalRealm';
    SELECT COUNT(*) INTO existing_game FROM `openconquer_game`.`realm_binding`;
    IF existing_accounts <> 0 OR existing_game <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Realm ID/name or Game binding already exists';
    END IF;

    INSERT INTO `openconquer_accounts`.`realms`
        (`realm_id`, `protocol_server_name`, `public_game_address`, `public_game_port`, `availability`)
    VALUES (1, 'LocalRealm', INET6_ATON('127.0.0.1'), 5816, 1);
    INSERT INTO `openconquer_game`.`realm_binding` (`binding_key`, `realm_id`) VALUES (1, 1);
    COMMIT;
END$$
DELIMITER ;
CALL `openconquer_accounts`.`provision_localrealm_once`();
DROP PROCEDURE `openconquer_accounts`.`provision_localrealm_once`;

SELECT realm_id, protocol_server_name, INET6_NTOA(public_game_address) AS game_ip,
       public_game_port, availability FROM `openconquer_accounts`.`realms` WHERE realm_id = 1;
SELECT binding_key, realm_id FROM `openconquer_game`.`realm_binding`;
