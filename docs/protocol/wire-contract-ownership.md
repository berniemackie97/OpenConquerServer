# CP3.1 — Wire-contract ownership

[Client decision issue #247](https://github.com/berniemackie97/OpenConquerClient/issues/247) |
[Client comparison and workflow](https://github.com/berniemackie97/OpenConquerClient/blob/main/docs/networking/wire-contract-ownership.md)

## Recommendation

Retain the server's protocol implementation in `OpenConquer.Protocol` and its
transport/session ownership in `OpenConquer.Transport` and the server hosts.
Do not adopt a client/server-shared production codec package at this checkpoint.
Use independent implementations tested against the same reviewed byte-vector
corpus. This becomes the accepted decision once both companion PRs are
reviewed and merged.

The client and server already each implement CAST5/CFB64, two-direction session
cipher state, secured frame encoding/decoding and overlapping packet layouts.
They have different responsibilities:

- `GameCast5Cfb64Cipher` is similar at the block-feedback level, but
  `GameSessionCipher` and its client counterpart own different session and
  disposal contracts. Server ingress validates the `TQClient` trailer;
  client ingress validates `TQServer`.
- `LoginStreamCipher` performs the server-side inverse of the client's
  account stream transform. Collapsing them into one unqualified transform
  risks confusing direction and credential handling.
- `GameWalkPacket.TryParse` rejects a zero entity ID at the server boundary,
  while the client can retain that value for dispatch. A shared decoder must
  not silently erase this difference.
- The 16-byte 1060 realm-identity field is a wire property. Validation of
  permitted realm names and authorization is a separate application/server
  policy; neither is established by the field width alone.

A shared package would reduce repeated cipher and packet routines but force
runtime version coordination and could make a single codec bug appear correct
on both sides. The alternative retains a modest duplication cost while
allowing each boundary to enforce its own safety and lifetime rules.
Agreement between two implementations is not independent evidence.

## Ownership and verification

The 5517 client and separately retained captures establish wire expectations.
For shared wire changes, review source evidence first; then update both
repositories' codecs only where necessary and verify against identical,
checked-in test vectors. This repository owns its parser, serializer,
authorization, admission, transport and cleanup behavior.

CP3.2 will define a canonical test-vector corpus in the client repository
under `tests/Fixtures/WireGolden/`. The server will retain a test-only copy
at the same relative path, with a manifest recording the pinned client commit
and matching corpus hash. Each entry must state direction, packet identity,
capture/derived/synthetic classification, evidence reference, expected bytes
or failure and schema revision. No secret-bearing captures, build-time
network fetch or production-package dependency.

Wire-affecting changes require linked client and server PRs, byte-level review,
negative framing/signature/ordering tests, stream-fragmentation tests and
separate CI in each repository. The hash and upstream reference must be
checked during coordinated review; independent CI validates the local corpus.
Fixture definition and those checks belong to CP3.2, not this decision spike.

## Acceptance boundary

This is a governance decision for the existing architecture, not a codec
migration or validation of new packet behavior. No runtime implementation,
cryptographic primitive or transport rule changes in this slice. The
cross-repository test corpus and independent captured vectors remain open
in [CP3.2](https://github.com/berniemackie97/OpenConquerClient/issues/248).
