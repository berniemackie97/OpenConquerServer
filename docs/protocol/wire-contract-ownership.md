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
