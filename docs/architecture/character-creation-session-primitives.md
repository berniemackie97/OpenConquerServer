# Character creation: native control talks and repeated login proof

This slice prepares two independently testable GameServer primitives for the verified native
5517 create-role continuation. It does **not** enable the character-creation route, send any
new network frames, grant database write privileges, or alter world admission.

## Native control messages

The client uses the existing packet `1004` chat layout to perform create-role transitions:

| Stage | Channel | Text | Client effect |
| --- | --- | --- | --- |
| Authenticated new-character routing | `0x835` | `NEW_ROLE` | Enter the native create-role UI |
| Successful durable character creation | `0x834` | `ANSWER_OK` | Send another post-DH `1052` on the existing socket |

`GameCharacterCreationControlPackets` supplies immutable packet values with the established
server-side `GameChatPacket` serialization policy. The second control channel is different
from the existing-character bootstrap's `ANSWER_OK` channel (`0x835`).

A future runtime processor must send the creation-channel success packet **only after**
`ICharacterCreationStore` reports a newly committed character. A duplicate account or name,
unknown write outcome, canceled operation, or failed creation must not result in success.

Non-success retail message wording has not been established and is not defined here.

## Bound repeated proof

Native analysis confirms that after successful creation the client sends the standard 28-byte
`1052` on the **same encrypted GameServer session**. The original login ticket was redeemed
and deleted when the first `1052` authenticated the socket. Repeating redemption would
incorrectly reject a legitimate create-role continuation or weaken single-use semantics.

After successful ticket redemption, `GameConnectionAuthenticator` arms a private, per-connection
repeated-proof verifier using the validated first `1052`. No original authentication key is
exposed as a public connection property.

The verifier:

- Requires the repeated packet to have passed `GameLoginProof.TryParse` before it is supplied.
- Requires the same authenticated session UID and authentication key as the original `1052`.
- Requires the established standard `1052` mode.
- Consumes the allowance on the first verification attempt, whether it matches or not.
- Never queries, recreates, or reissues the consumed database login ticket.
- Clears the stored key on consumption, explicit discard, or connection disposal.
- Rejects unarmed, already-consumed, discarded, and disposed connections.
- Serializes concurrent verification attempts so at most one can succeed.

The initial proof's locale, hardware-address, and resource-version fields are captured by
`AuthenticatedGameConnection`. When arming the verifier, these fields must match the initial
session; the repeated proof's authorization check uses only the session UID and original key.
Other repeated fields are not independently established as immutable across native UI modes.

After character resolution selects an **existing character**, the handoff processor immediately
discards the repeated-proof material. It is never retained unnecessarily into a long-lived
existing-character world session. On the creation route, the key remains private until the
future creation processor uses or closes the connection.

This is a one-time, connection-local proof check, **not** a second authentication or a ticket
replay mechanism. A future session processor must close the connection when a repeated `1052`
is malformed, missing, or rejected; this slice does not install that processor.

## Remaining security and lifecycle work

Before enabling the creation path, the GameServer must still:

1. Provision and validate the separate least-privilege creation database identity.
2. Establish an approved, walkable creation spawn and authoritative allotments.
3. Keep authenticated connection ownership exclusive throughout the create-role transition.
4. Send `NEW_ROLE` only for the resolved character-creation route.
5. Bound incoming `1001` request attempts and all waiting by the existing connection timeout.
6. Treat `1001`'s client-reported session UID as an untrusted consistency check and its MAC as
   untrusted telemetry, never as account authorization.
7. Call `CharacterCreationService` with the authenticated account identity exclusively.
8. Send `ANSWER_OK` on the creation channel only after confirmed durable insertion.
9. Parse and verify exactly one post-success `1052` on the original secured connection.
10. Re-resolve and validate the newly persisted character and reuse existing-character bootstrap,
    preserving ownership, cancellation, and error cleanup.

The wire control packets and repeated-proof primitive are currently **unconnected** to the hosted
creation route; character creation remains fail-closed.

## Evidence

- `20_notes/Network/13_connection_lifecycle.md`, native `NEW_ROLE` and create-role continuation.
- `docs/protocol/character-creation.md`, verified packet `1001` structure and native flow.
- `ExistingCharacterBootstrapPacketFactory`, existing-character `ANSWER_OK` channel policy.
- OpenConquerPublic's earlier `GameCharacterCreationSessionProcessor`, historical comparison.
