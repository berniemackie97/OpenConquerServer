# Server responsibility review — CP1.5

Reviewed against OCS `7407c71` after merged CP1.4. Size prompted review; it is not a
reason to split a cohesive authority or a target line limit. This slice separates
existing responsibilities while preserving wire behavior, gameplay and resource lifetime.

## Decisions

| Reviewed type | Decision | Responsibility after review |
| --- | --- | --- |
| `WorldRuntime` (1,170 lines) | Retain | Authoritative cross-map residency, generations, reservations/seals and associated session lifetimes. |
| `WorldSession` (932 → 654 lines) | Extract `WorldSessionGameplay` (364 lines) | Session owns connection, ordered delivery, world binding and teardown; gameplay component owns ingress policy, pacing and position reconciliation. |
| `GameConnectionSession` (707 → 646 lines) | Extract `GameHandshakeProgress` (100 lines) | Session owns secure I/O, codecs/cipher, operation admission/drain and disposal; progress component owns legal handshake state transitions. |
| `AccountMutationStore` (645 lines) | Retain | Atomic persistence of account-security changes, credential/ticket effects and audit evidence. |

### World residency authority

`WorldRuntime` delegates execution to `WorldScheduler`/`WorldPartition`, spatial state
to `MapInstance`, and external portal persistence to existing coordinators. Owner
registry, destination occupancy, connected-session generation key and durable transfer
seal change under the same gate during cutover. They constitute one residency
transaction. The authority contains no socket I/O, packet decoding, SQL or content-file
loading. Its entire implementation remains unchanged, including the CP1.4
[gate/command/worker decision](world-command-load.md).

### Admitted session and gameplay policy

The `WorldSession` facade remains the entry point for the connection worker and
visibility/portal components. `WorldSessionGameplay` owns walk pacing, jump ingress,
avatar/chat limits, reconciliation history and their request handlers. It reads live
ownership/map/profile through the session so a portal cannot leave a cached generation.

Walk, jump, ghost, chat, pose/facing, position-report, trade-quit and client-state
handlers retain existing guards, callbacks, timers, cancellation and response order.
Responses still use the session's FIFO/direct-write path. Portal rebind resets
reconciliation and walk pacing at the same point before volatile location publication;
other limiter state is retained as before.

Existing behavior authority remains `20_notes/Server/movement-eligibility-5517.md`,
`Server/death-ghost-revive-5517.md`, `Server/chat.md`, and the
[walking](ground-walking.md), [jump](native-jumping.md),
[avatar-action](native-avatar-actions.md) and [visibility](player-visibility.md) docs.
This is an extraction of already implemented behavior.

### Connection owner and handshake progression

`GameHandshakeProgress` owns bootstrap/challenge/response/secured/terminal flags and
guards. Every access remains under the session's existing protocol gate. Disposal
checks stay on the owner at the same call sites. Cancellation resets a response read
only under the original bootstrap/active/not-disposing condition. Codecs and cipher
ownership are installed before the secured-state write, in the original order.

Pipes, pumps, active-operation draining, observed pump failures, resource cleanup order
and exception aggregation remain on `GameConnectionSession`. Existing protocol
context is [networking](networking.md) and
`20_notes/Network/13_connection_lifecycle.md`; wire bytes and tolerance rules are unchanged.

### Account transaction boundary

The five mutations lock account state, check expected revisions, apply credential/
ticket effects and append audit evidence within one MySQL transaction. They share
lock order, one database UTC instant and explicit commit/rollback policy. Hashing,
transport, hosting and logging are elsewhere. The store remains the atomic
[persistence](persistence.md) boundary; its entire file is unchanged.

## Behavior-preservation evidence

- Existing test sources and fixtures are unchanged. Focused suites pass: GameServer
  873, Protocol 622, Transport 71, Application 422, AccountServer 218, Domain 343,
  content tool 222, world load tool 11 (**2,782 total**).
- Locked restore, zero-warning Release build and full format verification pass.
  CI runs database and remaining integration gates.
- The [source comparison](../reviews/verify-server-responsibilities.py) checks **78**
  preservation conditions against the base: ten moved gameplay bodies, progression
  guards and locks, owner methods, constants/initializers, codec-install order,
  cancellation/disposal ordering, constructor and portal-rebind bindings, and both
  retained files. It preserves string/comment payloads, ignores code whitespace and
  permits only the documented owner-reference rebinding.
- [Recorded check digests](../reviews/evidence/server-responsibilities-2026-11-05/equivalence.json)
  make the extraction auditable. These are mechanical source checks; existing
  runtime/fixture tests remain the behavioral check. No new native observation is claimed.

```bash
python3 docs/reviews/verify-server-responsibilities.py
# To revisit the extraction from a later checkout:
python3 docs/reviews/verify-server-responsibilities.py --head <CP1.5-commit>
```

The new components own state and behavior through composition. Catch review,
clock-policy cleanup and broader doc anchoring remain separate. CP1.5 stays current
until CI and Bernie review close the slice. No native symbol edit or BNDB save is involved.
