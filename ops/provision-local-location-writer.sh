#!/usr/bin/env bash
# Development operator action. Run once AFTER reviewing PR and when upgrading a local realm.
# Does not run as part of source installation or CI.
set -euo pipefail
umask 077

cd "/Volumes/EnderChest/Development/Repos/DotNet/Conquer/OpenConquerServer"
CREDENTIAL_DIR="$HOME/.config/openconquer"
CREDENTIAL_FILE="$CREDENTIAL_DIR/local-location-writer.env"
BACKUP_DIR="$CREDENTIAL_DIR/secret-backups"

if [[ -e "$CREDENTIAL_FILE" ]]; then
  echo 'ERROR: Existing location-writer credential file; inspect manually rather than resetting credentials.' >&2
  exit 1
fi

if lsof -nP -iTCP:9958 -iTCP:5816 -sTCP:LISTEN; then
  echo 'ERROR: Stop both servers before provisioning a writer.' >&2
  exit 1
fi

users="$(docker exec -i openconquer-mysql /bin/sh -c \
  'MYSQL_PWD="$(/bin/cat /run/secrets/mysql-root-password)" exec mysql --user=root --batch --skip-column-names' <<'SQL'
SELECT COUNT(*) FROM mysql.user WHERE User = 'oc_location_writer';
SQL
)"
if [[ "$users" != '0' ]]; then
  echo 'ERROR: A location-writer account already exists. Do not recreate its password.' >&2
  exit 1
fi

schema="$(docker exec -i openconquer-mysql /bin/sh -c \
  'MYSQL_PWD="$(/bin/cat /run/secrets/mysql-root-password)" exec mysql --user=root --batch --skip-column-names' <<'SQL'
SELECT CONCAT(schema_version, ':', migration_id)
FROM openconquer_game.schema_compatibility
WHERE component_name = 'game';
SQL
)"
if [[ "$schema" != '13:20261019010000_AddCharacterLocationWriterFence' ]]; then
  echo 'ERROR: Game schema is not v13.' >&2
  exit 1
fi

mkdir -p "$CREDENTIAL_DIR" "$BACKUP_DIR"
chmod 700 "$CREDENTIAL_DIR" "$BACKUP_DIR"
password="$(openssl rand -hex 32)"
printf 'OC_LOCATION_WRITER_PASSWORD=%s\n' "$password" > "$CREDENTIAL_FILE"
chmod 600 "$CREDENTIAL_FILE"

# MySQL GRANT statements are independent DDL changes. Preserve the generated credential
# if anything fails so that an operator can inspect partial provisioning safely.
docker exec -i openconquer-mysql /bin/sh -c \
  'MYSQL_PWD="$(/bin/cat /run/secrets/mysql-root-password)" exec mysql --user=root --show-warnings' <<SQL
CREATE USER 'oc_location_writer'@'%' IDENTIFIED WITH caching_sha2_password BY '$password';
GRANT SELECT (
  account_id, character_id, map_id, position_x, position_y,
  location_revision, last_location_operation_id, location_writer_token
) ON openconquer_game.characters TO 'oc_location_writer'@'%';
GRANT UPDATE (
  map_id, position_x, position_y, location_revision,
  last_location_operation_id, location_writer_token
) ON openconquer_game.characters TO 'oc_location_writer'@'%';
SQL

printf '%s\n' "$password" | docker exec -i openconquer-mysql /bin/sh -c '
  IFS= read -r password
  MYSQL_PWD="$password" exec mysql --protocol=TCP --host=127.0.0.1 \
    --user=oc_location_writer --database=openconquer_game --batch --skip-column-names \
    --execute="SELECT character_id FROM characters WHERE character_id = 0 LIMIT 1;"
'

secrets="$HOME/.microsoft/usersecrets/OpenConquer.GameServer/secrets.json"
if [[ ! -f "$secrets" ]]; then
  echo 'ERROR: GameServer User Secrets missing; database user was created. Preserve credential file and configure manually.' >&2
  exit 1
fi

cp -p "$secrets" "$BACKUP_DIR/OpenConquer.GameServer.pre-location-writer.$(date +%Y%m%d_%H%M%S).json"
chmod 600 "$BACKUP_DIR"/OpenConquer.GameServer.pre-location-writer.*.json

# The key is sent through standard input, not a shell command-line argument.
python3 - "$CREDENTIAL_FILE" <<'PY' | dotnet user-secrets set --project src/OpenConquer.GameServer
import json, sys
from pathlib import Path
lines = Path(sys.argv[1]).read_text().splitlines()
assert len(lines) == 1 and lines[0].startswith('OC_LOCATION_WRITER_PASSWORD=')
password = lines[0].split('=', 1)[1]
assert len(password) == 64 and all(c in '0123456789abcdef' for c in password)
json.dump({
    'ConnectionStrings:GameLocationWriter': (
        'Server=127.0.0.1;Port=3308;Database=openconquer_game;'
        'User ID=oc_location_writer;Password=' + password + ';SslMode=Preferred'
    )
}, sys.stdout)
PY

# Show the effective grant scope without printing credentials.
docker exec -i openconquer-mysql /bin/sh -c \
  'MYSQL_PWD="$(/bin/cat /run/secrets/mysql-root-password)" exec mysql --user=root --batch --table' <<'SQL'
SHOW GRANTS FOR 'oc_location_writer'@'%';
SQL

echo 'SUCCESS: Dedicated v13 column-scoped location writer provisioned. Review the displayed grants.'
