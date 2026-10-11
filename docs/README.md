# Documentation

| Area           | Document                                                     | Covers                                                    |
| -------------- | ------------------------------------------------------------ | --------------------------------------------------------- |
| Status         | [Implementation status](status.md)                           | What is and is not built                                  |
| Architecture   | [Overview](architecture/README.md)                           | Projects, dependency rules, ownership, failure handling   |
|                | [Networking](architecture/networking.md)                     | Connection handling on both servers, login flows          |
|                | [Authentication](architecture/authentication.md)             | Credentials, rate limiting, passwords, GameServer tickets |
|                | [Persistence](architecture/persistence.md)                   | Databases, schema contracts, read and write rules         |
|                | [World execution](architecture/world-execution.md)           | Partitions, scheduling, and world ownership               |
| Protocol       | [Overview](protocol/README.md)                               | Packet catalog and the login and bootstrap sequences      |
|                | [Framing](protocol/framing.md)                               | Frame header, limits, encoder and decoder behavior        |
|                | [Text encoding](protocol/text-encoding.md)                   | String fields and supported encodings                     |
| Content        | [Content](content/README.md)                                 | Item and map content, the content tool                    |
|                | [Base terrain format](content/base-terrain-format.md)        | The `.ocbt` file format                                   |
|                | [Collision format](content/collision-format.md)              | The `.ocmc` file format                                   |
| Operations     | [AccountServer](operations/account-server.md)                | Configuration, secrets, running, troubleshooting          |
|                | [GameServer](operations/game-server.md)                      | Configuration, content, startup and shutdown              |
|                | [Database migrations](operations/database-migrations.md)     | Applying, recovering, and downgrading schema migrations   |

Architecture documents describe how the server is built. Protocol documents describe what goes
over the wire. Operations documents describe how to run it.
