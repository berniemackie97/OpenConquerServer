# Character creation: control messages and repeated login proof

This document covers two GameServer building blocks for the 5517 create-role flow. Each can be
tested on its own. The first is the pair of control messages that drive the client's create-role
transitions. The second checks the login proof that the client sends a second time after a character
is created. Neither is connected yet: the character-creation route stays disabled, no new network
frames are sent, no database write privileges are granted, and world admission is unchanged.

## Control messages

The client uses the existing chat packet `1004` layout for create-role transitions:

| Stage | Channel | Text | Client effect |
| --- | --- | --- | --- |
| Authenticated new-character routing | `0x835` | `NEW_ROLE` | Enter the create-role UI |
| Successful durable character creation | `0x834` | `ANSWER_OK` | Send another post-DH `1052` on the existing socket |

`GameCharacterCreationControlPackets` provides immutable packet values, serialized the same way as
the server's other `GameChatPacket` messages. The success channel here is not the one the
existing-character bootstrap uses for its `ANSWER_OK` (`0x835`).

When the runtime processor is added, it must send the creation-channel success packet only after
`ICharacterCreationStore` reports a newly committed character. A duplicate account or name, an
unknown write outcome, a canceled operation, or a failed creation must never produce a success
packet.

The retail wording for failure messages is not known, so none is defined here.

## Repeated login proof

Analysis of the 5517 client shows that after a successful creation it sends the standard 28-byte
`1052` again on the same encrypted GameServer session. The login ticket was already redeemed and
deleted when the first `1052` authenticated the socket. Redeeming it again would either reject a
legitimate create-role continuation or weaken the single-use rule for tickets.

After the ticket is redeemed, `GameConnectionAuthenticator` sets up a private verifier for this
connection from the validated first `1052`. The original authentication key is not exposed as a
public property of the connection.

The verifier:

- Requires the repeated packet to have passed `GameLoginProof.TryParse` before it is supplied.
- Requires the same authenticated session UID and authentication key as the original `1052`.
- Requires the standard `1052` mode.
- Allows one attempt: the first verification uses it up, whether it matches or not.
- Never reads, recreates, or reissues the consumed login ticket in the database.
- Clears the stored key when the attempt is used, when it is discarded, or when the connection is
  disposed.
- Rejects connections where it was never set up, was already used, was discarded, or was disposed.
- Serializes concurrent verification attempts so at most one can succeed.

`AuthenticatedGameConnection` records the locale, hardware-address, and resource-version fields of
the first proof. When the verifier is set up, these fields must match the initial session. The check
on the repeated proof uses only the session UID and the original key, because the other fields have
not been shown to stay the same across the client's UI modes.

When character resolution finds an existing character, the handoff processor discards the
repeated-proof material immediately, so it does not stay in memory for the life of the world
session. On the creation route, the key stays private until the creation processor, which is not
written yet, uses it or closes the connection.

This is a one-time proof check local to the connection. It is not a second authentication and it
does not replay the ticket. The session processor that will use it must close the connection when a
repeated `1052` is malformed, missing, or rejected. That processor is not installed yet.

## Remaining security and lifecycle work

Before the creation path is enabled, the GameServer must still:

1. Provision and validate the separate creation database identity, with only the privileges it
   needs.
2. Settle on an approved, walkable creation spawn point and trusted attribute allotments.
3. Keep exclusive ownership of the authenticated connection throughout the create-role transition.
4. Send `NEW_ROLE` only for the resolved character-creation route.
5. Limit incoming `1001` request attempts, and all waiting, by the existing connection timeout.
6. Treat the session UID that `1001` reports only as an untrusted consistency check, and its MAC
   only as untrusted telemetry. Neither one authorizes an account.
7. Call `CharacterCreationService` with the authenticated account identity and nothing else as the
   account source.
8. Send `ANSWER_OK` on the creation channel only after the insert is confirmed committed.
9. Parse and verify exactly one `1052` after the success message, on the original encrypted
   connection.
10. Resolve and validate the newly saved character again, then reuse the existing-character
    bootstrap with ownership, cancellation, and error cleanup intact.

Until then, the control packets and the repeated-proof check are not connected to the creation
route, and character creation stays disabled.

## Sources

- `20_notes/Network/13_connection_lifecycle.md`: the client's `NEW_ROLE` handling and the
  create-role continuation.
- `docs/protocol/character-creation.md`: the packet `1001` layout and the client flow.
- `ExistingCharacterBootstrapPacketFactory`: the `ANSWER_OK` channel used for existing characters.
- `GameCharacterCreationSessionProcessor` in the earlier OpenConquerPublic project, for comparison.
