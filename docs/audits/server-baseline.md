# Server baseline review

Reviewed base: OpenConquerServer `a34296c50f89e123c26917090ca0d61265848f0d` and the
uncommitted account persistence slice on `bernie/account_authentication_persistence`.
Behavioral reference: OpenConquerPublic `11fa7ee1db02dc147afb9581bd456bedad79ed71`.
Native references are the maintained 5517 evidence listed below.

## Findings and decisions

The initial corrective pass was insufficient. It retained an unjustified persistence divergence,
used misleading implementation names, and incorrectly classified two responsibilities of the
already-ported authenticator as work for a later feature. This review replaces that assessment.

| Finding | Final correction | Why it is justified |
| --- | --- | --- |
| Direct MySqlConnector commands replaced EF Core account operations without evidence of a benefit. | `AccountDbContext`, explicit account mapping, pooled context factory, and `AccountAuthenticationRepository` use EF Core queries and updates. | Retains OpenConquerPublic's persistence model and retry behavior; removes handwritten application queries and keeps provider details in Infrastructure. Raw SQL is not inherently unsafe, but the extra maintenance here had no demonstrated payoff. |
| Authentication omitted successful-login timestamp persistence and the stored username. | Every successful authentication conditionally writes `timestamp_token` and returns the canonical persisted username. | Both are existing `EfLoginAuthenticator` responsibilities. The lack of a current timestamp reader did not justify discarding its write contract. |
| Password migration could authenticate an obsolete snapshot. | One conditional update checks ID, exact username, exact password hash, and current allowed permission; it writes the timestamp and optional hash together. | Improves on the original tracked update by preventing a concurrent reset, ban, denial, deletion, or rename from completing against stale state. |
| A failed conditional write did not establish a durable successful login. | Re-read and verify once, then require a successful conditional update. Reject repeated contention. | A harmless concurrent migration or an uncertain update acknowledgement can recover; an unbounded KDF retry loop cannot occur. |
| Username/password invariants were missing. | Domain owns the policy; Application validates before dependencies. | Reuses one account policy without mixing it into wire decoding or SQL. Preserves trimming, case, character set, and exact length semantics. |
| OpenConquerPublic hashes were unverifiable. | Accept the writer's exact Identity V3 profile and request migration to the existing version-1 format. | Existing accounts remain usable. Untrusted metadata cannot select work factors or allocation sizes. |
| Concrete names described origin or only one of two supported algorithms. | `AccountPasswordHasher`, `IAccountPasswordHasher`, `PasswordHashMigrationTests`, `LoginStreamCipher`; Identity V3 variables name their actual format. | Names describe the code's present responsibility. Managed identifiers contain no historical-project label. |
| Login session races could return on a pump fault without awaiting the pending frame operation. | Await framed I/O directly through pipeline completion; cancel and observe the seed write when opening fails. | Preserves exception propagation while keeping frame memory and operation lifetime under the session's ownership. |
| Documentation overstated implementation coverage and retained stale names/layout. | Root, Protocol, framing, Networking, and authentication references describe actual components and composition. | A buildable library slice must not be presented as a running, operationally protected server. |

## Complete authentication inventory used for placement

Paths in this section are relative to OpenConquerPublic `src/` unless stated otherwise.

- `Domain/Auth`: account constraints, login credentials/result/status, permission policy,
  registration models, and tickets; `Domain/Characters/Enums/PlayerPermission`.
- `Application/Auth/Login`, `Auth/Registration`, and `Auth/Tickets`: authentication protection,
  registration operation identity, and ticket interfaces.
- `Infrastructure/Persistence/Auth/Login/EfLoginAuthenticator`: normalization, lookup, decoy,
  per-account lease, password-before-permission ordering, migration, timestamp save, canonical name.
- `Infrastructure/Persistence/Auth/Registration/EfAccountRegistrationService`: input validation,
  uniqueness, hashing, operation IDs, transaction verification, and duplicate/uncertain-commit handling.
- `Infrastructure/Persistence/Auth/Tickets/EfLoginTicketRepository`: durable issuance, expiry,
  bounded cleanup, and single-use conditional deletion.
- `Infrastructure/Persistence/AuthDbContext`, `Entities/EfAccount`, authentication registration,
  MySQL configuration, and the account schema/readiness validator. The original uses handwritten
  SQL for `information_schema` validation, not account authentication queries.
- `Infrastructure/Security/AccountPasswordHasher` and its writer history: the Identity V3 profile
  uses SHA512, 220,000 rounds, 16-byte salt, and 32-byte subkey. The removed plaintext/token path
  is not restored. Framework-generated hashes and an independent vector verify compatibility.
- `AccountServer/Login/Handshake`, admission/protection, queues, hosted services, options,
  ticket issuer, and `Program`: request/IP budgets, phase deadlines, worker ownership,
  unsupported variants, wire failure mapping, ticket handoff, and correlated reports.
- Protocol login readers/writers/frames, stream cipher, RC5/keypad/seed decoder, socket adapter,
  serialization, and GameServer handshake consumers.
- The complete source account baseline `database/baseline/account-server-v2.sql`; account
  authentication, password hashing, registration commit, protection, lifecycle, and ticket tests.

This inventory establishes boundaries; it does not make registration, ticket issuance, or a host
implementation part of the current authentication component merely because their callers were read.

## Architecture and production behavior

The authentication use case depends on contracts and `TimeProvider`, not EF entities, connections,
or hashing algorithms. Infrastructure projects only authentication fields with `AsNoTracking` and
`SingleOrDefaultAsync`; duplicate names in a damaged schema fail closed. A context is created and
disposed for each database operation, so no database connection or tracked entity spans password
derivation. The singleton repository shares a context factory, never a `DbContext`.

The account mapping covers all eleven account columns, their lengths, unsigned fields, names,
collations, and indexes. The database remains externally provisioned. Tests apply a checked-in copy
of the OpenConquerPublic baseline, including permission and ticket foreign keys, rather than using
`EnsureCreated` from the model under test. Setup and deliberate schema damage may use SQL in tests;
application account reads and writes use EF Core.

Conditional completion uses `HEX` through the provider's EF translation for byte-exact username
and password comparisons. This preserves case and trailing-space distinctions despite the account
table's case-insensitive collation; the primary-key condition bounds the comparison to one row.
Matched-row semantics are configured explicitly so two successful logins in the same second both
succeed. An EF command interceptor verifies the case where migration commits but its acknowledgement
times out: provider retry loses the original comparison, then Application revalidates and records
the login against the new hash.

The EF/Pomelo version pairing follows the provider's published compatibility matrix: EF Core
9.0.18 with Pomelo 9.0.0 on the .NET 10 application target. MySqlConnector remains pinned at 2.6.2.
Using mismatched major versions or a preview provider solely to match the runtime number would
not be a defensible modernization. See the [provider compatibility table](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql#compatibility)
and [EF conditional update guidance](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete#concurrency-control-and-rows-affected).

Password work remains bounded to two fixed KDF profiles per verification, including misses and
malformed records. This adds CPU cost to current-format verification in exchange for hiding the
stored format from KDF timing. It does not equalize database or successful-migration latency.
The platform performs PBKDF2; managed code parses the fixed envelopes and clears bounded buffers.
No generic cryptography registry, custom KDF, or unused abstraction was introduced.

## Other implemented boundaries rechecked

| Boundary | Source/native contract and final assessment |
| --- | --- |
| Framing/text | Four-byte little-endian header, nonzero packet ID, exact payload writes, caller bounds, fixed/byte-prefixed text. Segmented decoding and failure cleanup are retained improvements. Generic limits remain distinct from game limits. |
| Transport | Complete sends, partial receives, no-delay sockets, owned disposal. Explicit single-reader/writer guards and bounded admission remain useful; they do not implement per-IP policy. |
| Admission/pumps | Ownership transfers only on acceptance; overload is rejected without waiting. Completion drains queued connections and reports disposal failures. Pipeline pumps preserve order, backpressure, and terminal errors. |
| Login I/O | Connection-local directional cipher state; encrypted seed precedes request handling. Exact reads, terminal state after partial failure, overlap rejection, plaintext clearing, and session cleanup remain enforced. Pending frame operations now finish before their caller exits. |
| Stream cipher | Generated tables, server transform order, independent 16-bit counter wrap. `LoginStreamCipher` is a semantic rename with unchanged transform bytes. |
| Credential envelope | Seed-derived MSVCRT key, RC5-32/12/16, signed original account bytes, keypad table and selection-sort ties. Raw account bytes are used before username normalization. |
| 1059/1060 | Eight-byte seed; standard 276-byte request with 128-byte account/credential fields and a 16-byte server field. Only the first 32 credential bytes are transformed. No character allowlist or new truncation rule was invented. |
| 1055 | 36-byte frame; nonzero UID selects success, separate key and additional field, UInt32 port, IPv4 C-string. The additional field remains independent because native consumers distinguish it. |
| 1100/1052 | 52/28-byte report forms, ordered consumption, session correlation, empty-or-uppercase-hex MAC, exact `res.dat`, signed version. Client reports remain telemetry rather than authorization. |

## Native evidence

Existing proof was sufficient; this review performs no new native identification or live native
execution and introduces no new wire rule. No Binary Ninja rename or note/reconstruction update
is claimed or required for a managed identifier rename.

Evidence root: `/Volumes/EnderChest/Development/Repos/GameDev/Conquer/client-5517`.

- `20_notes/Server/login-credential-envelope.md`: settled live seed+keypad result; seed handler
  `0x764BC6`, RC5 `0x749A97`/`0x749B67`, signed keypad seed `0x59F02D`, row table `0x936768`.
  The settled result takes precedence over contradictory older decoder comments.
- `20_notes/Network/13_connection_lifecycle.md`: standard/OEM producer split, 1084's 524-byte
  size, 1055 handoff, 1100 builder `0x767DA1`, AccountServer 1052 `0x7543CA`, and distinct
  post-DH GameServer 1052 `0x75443E`.
- `20_notes/Network/01_core_packets.md`: framing, receive fragmentation, and game trailer limits.
- `20_notes/Network/14_session_derived_legacy_cipher_mask.md`: standard GameServer mode 2 uses
  DH/CAST5-CFB64 and bypasses the rekeyed TQ tables; no account-stream mask was added.
- `10_work/docs/NATIVE_ACCOUNT_CREDENTIAL_FORMATS_5517.md` and existing session-mask evidence
  retain the reconstruction and Binary Ninja annotation references.

New proof must first be named/commented and saved in Binary Ninja, then propagated to `20_notes`
and applicable `10_work` material before changing the managed wire implementation.

## Verification boundary

The commit gate covers locked restore, format verification, Release build with warnings as errors,
all five suites including MySQL integration, dependency vulnerability checks, and review of the
complete working-tree diff. Adversarial checks must execute against the final code and restore each
mutation before the full gate. Test evidence is recorded below when that gate completes.

The two executable entry points are still empty. Registration, operational IP/account protection,
workers/deadlines, host schema-readiness composition, tickets, GameServer transport/gameplay, and
asset loading remain unimplemented. Component verification cannot certify that missing runtime.
