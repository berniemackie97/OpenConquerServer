# Durable-fenced existing-character admission (Game v13)

## Authority and ordering

A ticket redemption authenticates an account; the character login resolver supplies the server-owned character identity, not a client-supplied coordinate. Presence registration occurs before the Accounts-side `TryEnterAsync`, preserving the existing kick/replacement ordering. A denied account-session entry never rotates the Game database's writer token.

After account-session ownership succeeds, the GameServer rotates the matching character's `location_writer_token` via its dedicated location-writer identity. The rotation is a single InnoDB row update and is not assumed to be cancellable after SQL dispatch. A timeout or exception fails login; there is no speculative reattempt after an uncertain result. A subsequent normal login can establish a new generation after ownership arbitration.

The GameServer then reloads the complete persisted profile with its normal read-only database identity, checks the authenticated account and character identity again, and verifies an independent scoped observation against the acquired token, revision, preceding operation ID and coordinates. The character's canonical loaded map and packet 1006 come from this rehydrated profile. No acceptance packet precedes these checks. An unavailable map fails closed rather than changing spawn locations.

The authority remains attached to the authenticated character connection and world session. On world admission, the server observes the generation before and after acquiring partition ownership. If it differs, or the connection was revoked, the existing cleanup path releases presence, the Accounts lease, transport and any acquired world reservation. A reserved source generation is not silently promoted into world ownership.

## Security boundary

`ConnectionStrings:Game` remains a SELECT-only Game database identity. `ConnectionStrings:GameLocationWriter` must be a separate, non-root identity on the same endpoint/database, also distinct from character creation when configured. The writer can select exactly identity/location/revision/marker/token columns and update only six location/fence columns in `characters`. It does not own accounts, passwords, profiles, inventory, health or schema DDL.

Network-enabled GameServer startup refuses a missing or invalid writer configuration. After normal Accounts and Game schema/readiness checks and before network ingress, a no-row SELECT/UPDATE permission probe verifies the configured writer's effective access. The probe is not a comprehensive MySQL grant audit: an operator must inspect `SHOW GRANTS` to detect excessive privileges.

## Concurrent and failure semantics

The durable writer token is a predicate for later location CAS operations. It is **not** a distributed world-session lock. Another process can supersede the token after the final point-in-time observation. Cross-realm account-session state and presence revocation are separately responsible for excluding or disconnecting competing live players. Future portal-transfer code must carry, test and invalidate this generation while maintaining immutable session/partition ownership until a complete transition is proven.

A failed rotation may already have committed. This does not authorize bootstrap, retries, world entry or a fabricated operation marker. A successful read that disagrees with the acquired generation is treated as supersession, not automatically reconciled. Network failures and timeouts must not leak a packet or move the character on an unconfirmed write.

## Known exclusions

This milestone does not implement connected action-85 portal transfer, mutable map residency, visibility teardown/reenrollment, durable walking, cross-process world leadership or native 5517 transfer packet choreography. It must be audited alongside existing world-session lifetime tests and live 5517 client traces before those milestones are implemented.
