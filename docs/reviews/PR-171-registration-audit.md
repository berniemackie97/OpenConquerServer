# PR #171 registration audit

Audited `bernie/account-registration` at `002c7e6666773a1553a9ce7b7ee40ab1133eba1b`
against base `e46067d00b23a3177ce6b3942813c6068e7e796b`. Corrections are local and uncommitted.

## Findings and corrections

| Finding | Correction |
| --- | --- |
| Authentication reader and handshake owned registration, persistence errors, realm admission and ticket issuance together. | A shared request reader and dispatcher route to separate authentication and registration handlers. Registration has no realm, session or ticket dependencies. |
| An expired in-flight source at the head of the limiter prevented all following sources from expiring; timestamps could also be queued out of order under concurrent admission. | Expire all elapsed windows while retaining outstanding leases; obtain timestamps under the lock. Tests cover expiry, IPv4-mapped addresses, parallel admission and repeated disposal. |
| Persistence ignored caller cancellation after the first insert and lacked a shared operation deadline. | Link connection and pre-commit writes to caller cancellation and a 15-second budget. Check cancellation after hashing. Tests cancel during the final insert and verify earlier rows roll back. |
| A missing recovery row was described as proof that a lost commit did not happen. | Only the expected operation/account match confirms success. Missing or unavailable evidence remains unknown, with no insert retry. Commit and recovery have separate 5-second cancellation budgets. |
| Raw database failures could expose SQL values or connection details to worker logs; the registration settings record also generated a connection-string-bearing `ToString()`. | Replace persistence failures with safe diagnostics, retain numeric error codes, discard raw inner exceptions, and use a configuration class without generated value diagnostics. |
| Failure-response delivery could replace the original registration fault. | Preserve both failures; wipe temporary credentials before sending a result. |
| The domain password policy checked only length, allowing non-wire callers to create credentials outside the login alphabet. | Validate the same ASCII alphabet produced by the existing keypad decoder. Packet formats and normal login decoding remain unchanged. |
| The original MySQL creation test queried a binary UUID using the driver's default UUID representation. | Use `Binary16` in the verification connection, matching the schema and production store. |

## Changed areas

- AccountServer: `LoginHandshakeProcessor`, new `LoginAuthenticationHandler` and
  `AccountRegistrationHandler`, the request reader/result/status moved from `Authentication` to
  `Requests`, dependency injection, and registration configuration diagnostics.
- Application/domain: cancellation before persistence, store outcome contract, registration password
  alphabet.
- Infrastructure: transactional cancellation and commit resolution, safe persistence exceptions,
  source-window expiry.
- Tests: dispatcher and ownership regressions, configuration privacy, application cancellation,
  password policy, rate-limit concurrency, restricted-identity MySQL writes and rollback,
  registration-to-authentication integration, and a TCP proxy for commit/recovery faults.
- Documentation: registration architecture and AccountServer operations.

## Compatibility and deployment boundaries

The `0xF001`/`0xF002` extension, result codes, disabled default, username rules, password length,
shared credential envelope, ordinary `1060`/`1055` login, and database schema are unchanged.
Registration still produces one response and no ticket; the worker closes the connection.

Quotas remain per process and reset on restart. Multi-instance deployments need source affinity
or a shared external limit. Registration grants must be verified with the dedicated identity;
the ordinary startup readiness check uses the AccountServer identity. Cancellation budgets request
cancellation; driver cancellation and disposal must complete before releasing owned resources.

The automated checks exercise the server and MySQL integration. An interactive OpenConquerClient
signup smoke test and validation of deployed database grants are deployment checks, not results
claimed by this audit.
