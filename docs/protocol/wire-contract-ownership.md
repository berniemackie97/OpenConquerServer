# CP3.1 — Server wire-contract ownership

[Canonical decision and comparison](https://github.com/berniemackie97/OpenConquerClient/blob/main/docs/networking/wire-contract-ownership.md) |
[Decision issue #247](https://github.com/berniemackie97/OpenConquerClient/issues/247)

## Recommendation

Keep the server wire implementation in `OpenConquer.Protocol`, the socket and
backpressure mechanisms in `OpenConquer.Transport`, and session admission,
authorization and shutdown in their owning hosts. Do not depend on a
client/server-shared runtime codec package.

The client and server implementations remain separate, but protocol acceptance
requires a common, independently sourced set of byte vectors. The decision
takes effect only after both linked PRs are reviewed and merged.

## Server-specific boundaries

- `GameCast5Cfb64Cipher` shares a wire algorithm with the client but has its
  own direction, disposal and session-lifetime contract.
- `GameSecuredFrameDecoder` receives client-signed frames; the client's
  decoder receives server-signed frames. Both use the 1,024-byte packet limit.
- `LoginStreamCipher` performs the complementary account transform, not
  the client's transform under a different class name.
- `GameWalkPacket.TryParse` rejects entity ID zero at this server boundary;
  the client may retain zero before deciding how to dispatch it.
- The 16-byte field in packet 1060 and the accepted realm-name policy have
  separate owners. Sharing the field layout must not merge authorization
  rules with client serialization.

## Change gate

The source-client evidence and independently retained captures determine
wire behavior, not agreement between these two implementations. CP3.2
[#248](https://github.com/berniemackie97/OpenConquerClient/issues/248)
will define the canonical test-only corpus, mirrored locally under
`tests/Fixtures/WireGolden/` with a pinned source commit and identical
payload hash. Linked client/server PRs must review byte changes and
invalid-length, signature, order, direction and fragmentation cases;
each repository runs its own checked-in fixtures in CI.

No codec, runtime dependency, cryptography, transport or test fixture is
changed by this decision slice. See the canonical document for alternative
evaluation, fixture metadata and version/update workflow.

## Frozen test corpus manifest

The test-only `tests/Fixtures/WireGolden/manifest.json` pins the canonical
client source revision `e13bf5724975ec825e64a3609719afedd9b6af41`,
the SHA-256 of each included fixture file, and an explicit list of every
registered case. Both test suites require the same manifest SHA-256
`0f07a8940e848680316bfcce433b4d994c51110ea7c3774d47698e34412517a7`,
verify every listed file and reject unlisted fixture JSON files. The corpus
has two static-derived cases and one project-defined registration case; it contains no running-client capture.
A corpus revision requires coordinated review of the source evidence,
fixture bytes, manifest and pinned hash in both repositories. No runtime
package or build-time cross-repository dependency is introduced.

## Account registration F001/F002 project vectors

The corpus includes `F001-F002-registration.project-defined.json`, based on the
in-client registration specification, not on the original 5517 executable.
It pins a 260-byte request with a synthetic account and seeded credential
block and the five valid eight-byte result frames (codes 0–4). Client tests
serialize F001 and parse all F002 results; server tests independently decode
F001 and serialize all F002 results. Each frame has its own SHA-256, and the
manifest pins the fixture-file SHA-256. This is specification-based evidence
and cannot satisfy the running-client capture requirement for other messages.
