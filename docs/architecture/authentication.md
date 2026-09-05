# Account authentication

`AccountAuthenticator` owns the use case. `AccountCredentialPolicy` in Domain defines the legacy
credential invariants; Application applies them before invoking any repository or expensive verifier.
Protocol decodes wire fields and Infrastructure implements password storage, without duplicating
account rules in either layer.

## Credential contract

- Trim surrounding username whitespace, then require 1–32 .NET characters.
- Preserve case, internal whitespace, and the legacy unrestricted character set. Persistence owns
  account matching/collation; this use case does not lowercase identifiers.
- Require 1–128 password characters. Empty/default password memory is invalid. Never trim or
  normalize a password; an all-space nonempty password satisfies the legacy length policy.
- Return `InvalidCredentials` for invalid supplied values before dependency calls. Null account-name
  and remote-address arguments remain explicit caller contract errors.
- Keep caller-owned password memory valid and unchanged until authentication completes. The
  authenticator and verifier do not retain it. The login request owner clears its buffer on disposal.

The native packet has a 128-byte account field and transforms only 32 credential bytes on the
standard path. Those wire dimensions are distinct from account policy. In particular, keypad decoding
must use the original account bytes **before** trimming the account for lookup.

## Authorization and concurrency

Account misses perform decoy verification. Resolved accounts acquire an attempt lease before
password verification; denied admission does not perform password work. The password is verified
before exposing banned status. A valid password for a denied/banned account is recorded as accepted
credentials, but cannot authorize login or trigger migration. Unknown verifier statuses fail closed.
Cancellation and exceptions abandon the lease; completed outcomes are reported exactly once.

Obsolete hashes are replaced through compare-and-swap against the exact original stored hash.
When replacement loses a race, authentication re-reads the account once and re-verifies its password
and access. A concurrent successful migration can still authenticate. A reset to a different password,
account deletion/recreation, or changed access cannot authorize the obsolete snapshot. Revalidation
never attempts a second migration, so contention cannot cause an unbounded retry loop.

Authentication is a point-in-time credential decision, not a persistent session grant. The repository
contract does not atomically lock password/access state through subsequent ticket issuance.
Game-session authorization and revocation remain separate, unimplemented boundaries on `main`.

## Password formats and migration

New hashes use `$openconquer$pbkdf2-sha256$v=1$`, PBKDF2-HMAC-SHA256 with 600,000 iterations,
a random 16-byte salt, and a 32-byte derived key. The existing format remains unchanged.

The verifier also accepts `$openconquer$identity-v3$` with the exact profile generated throughout
OpenConquerPublic's hasher history: Identity marker 1, PRF 2 (HMAC-SHA512), 220,000 iterations,
16-byte salt, and 32-byte subkey. The three header integers are big-endian. This layout is verified
against the [Microsoft Identity implementation](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Extensions.Core/src/PasswordHasher.cs),
a fixed independent vector, and hashes produced by the actual framework hasher in tests.

Only a matching password returns `SuccessRehashNeeded`. Application then creates a current hash
and requests conditional replacement; wrong passwords never migrate. This preserves existing
OpenConquerPublic accounts without an offline password reset or plaintext export.

The fixed-profile allowlist is intentional: it preserves what the legacy writer actually emitted,
while rejecting persisted iteration/length/PRF values that could drive unbounded CPU or allocation.
Unprefixed values, Identity V2, custom Identity profiles, malformed or extended records fail closed.
They were not emitted by this legacy writer. Older plaintext/custom-salt support was explicitly
removed from OpenConquerPublic in commit `6c66e2a`; this correction does not re-enable it.

## Verification work and sensitive memory

Every verification path performs one 600,000-round SHA256 derivation and one 220,000-round SHA512
derivation, each producing 32 bytes followed by a fixed-time comparison. The applicable format uses
its decoded salt/key; the other uses decoy material. Account misses and malformed records use
decoys for both. A decoy match can never authenticate because a valid decoded scheme is also
required. The shared verifier has no mutable per-request fields.

This deliberately adds the legacy KDF cost to current-hash verification to avoid distinguishing
account misses, malformed storage, and legacy/current accounts by hashing work during migration.
It does not claim identical total request latency: lookup, admission rejection, successful migration,
and scheduling have different costs. The host must enforce request and concurrency budgets before
exposing authentication to untrusted traffic.

Salts, decoded records, and derived-key buffers use bounded stack storage and are cleared in
`finally` blocks. Passwords enter the platform PBKDF2 API as spans. Tests exercise concurrent mixed
operations, real legacy migration, invalid metadata, wrong passwords, and warmed/rotated median
verification timings. Timing tests are isolated from other tests in the same test process.

No database adapter or registration service is implemented on `main`. Migration tests validate the
real authenticator and cryptography with a test repository; they do not assert durable SQL behavior.
