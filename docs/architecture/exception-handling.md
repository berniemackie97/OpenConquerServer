# Exception handling

Rules every `catch` in `src` follows, and the CP1.6 audit (2026-11-06) that checked all of them.

## Rules

1. A `catch` rethrows, records the failure for an aggregate, forwards it to the operation that is waiting for it,
   or maps it to a typed failure.
2. A `catch` that handles an exception and continues states why when the reason is not obvious from its filter.
   Expected shutdown (`OperationCanceledException` when the caller's token is canceled, `ObjectDisposedException`
   once the owner is stopping) is obvious from the filter and needs no comment.
3. Nothing is thrown from a `finally` block. Cleanup that can fail runs after the operation and keeps both failures
   (`GameCharacterCreationLoginProcessor`, `WorldPartition.ExecuteTurn`).
4. Database driver messages never reach a diagnostic. They can contain SQL parameter values, usernames or connection
   details. Readiness and persistence failures carry `DatabaseFailureCode.Describe` (MySQL error number and
   SQLSTATE) instead of the driver exception. Every exception passed to a logger first goes through
   `DatabaseFailureCode.RedactForDiagnostics`, which replaces any chain containing a `DbException` with a code-only
   failure.
5. Validation of untrusted input does not use exceptions for control flow when a non-throwing check exists
   (`ProtocolServerName.TryCreate`). Encoding checks that rely on exception fallbacks
   (`EncoderFallbackException`, `DecoderFallbackException`) are the platform's validation mechanism and stay.

## Audit

260 `catch` clauses in `src` at `cfcd95e`, classified by body:

| Class | Count | Meaning |
|---|---|---|
| Rethrow | 71 | Cleanup or recording, then `throw;` |
| Map | 53 | Typed failure that keeps the original as inner, except database exceptions (rule 4) |
| Aggregate | 48 | Added to a failure list combined later |
| Capture for later | 20 | Stored in a local and rethrown or aggregated after cleanup |
| Forward | 21 | Completes the waiting operation with the failure |
| Expected handling with return | 20 | Typed translation (duplicate key, timeout, parse failure) or a filtered shutdown path |
| Filtered, empty | 20 | Expected shutdown or best-effort writes, each with a filter or a comment |
| Capture via `ExceptionDispatchInfo` | 4 | Rethrown with the original stack |
| Log at a boundary | 3 | Worker/session boundaries that end the connection |

## Changes made by the audit

- Five sites forwarded a raw `MySqlException` or `DbException` as the inner exception: the three readiness
  verifiers, `CharacterLocationWriteStore` and `CharacterCreationStore`. They now carry the failure code only.
- Six realm services logged the raw `DbException`. They now log the failure code.
- Fifteen boundary log calls pass their exception through `DatabaseFailureCode.RedactForDiagnostics`, because most
  stores let driver exceptions propagate.
- `LoginAuthenticationHandler` used an `ArgumentException` to reject an invalid protocol server name; it now uses
  `ProtocolServerName.TryCreate`.
- `WorldRuntime.ReleaseCharacterAsync` states why a removal refused during shutdown is not an error.
