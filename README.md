# Threshold

A door policy. Characters that have played on another world do not come in.

## Why this is its own mod

It used to live inside [Boon](../boon), and that was the wrong place for it.

Boon awards character levels for skill gains, so it needs to know whether a character's skills
were actually earned on this server. It answered that by **refusing the connection** — which
put a levelling mod in charge of who is allowed to play. The failure mode is exactly what you
would expect: a bug in an XP system locks people out of a server. And when it fired, the player
got Valheim's generic kick screen with the reason written only to the server's log, which on
somebody else's server they can never read. The first time it happened in practice, the person
affected assumed a completely unrelated mod had blocked them.

So the two halves were separated by what each is actually for:

- **Boon** keeps the question it has standing to ask — *do I pay for these levels?* — and
  answers it by withholding XP. Nobody is disconnected, nothing already earned is removed, and
  the player is told once, on screen.
- **Threshold** owns the question of who comes through the door. That is a server policy, it
  has nothing to do with levelling, and it is the whole of this mod.

The split also makes each honest about its own limits, which the fused version could not be.

## What it checks

| Check | Default | What it means |
| --- | --- | --- |
| `RefuseOtherWorlds` | on | The character has spawned in some world other than this one |
| `RefuseCheats` | on | The game has flagged the character as having used cheats |
| `RefuseUnreported` | on | The connection answered nothing, or its profile could not be read |

`Enforce` is **off by default**, and deliberately. This is the one setting in the family that
can lock people out of a server, including you. It should be something an admin turns on having
read what it does, not something that happens because a mod got installed.

## Read this before turning Enforce on

**The game never removes entries from a character's world list.** One visit anywhere else is
permanent for that character file. Restoring a backup taken before the trip is the only way
back in. The cheat flag is the same — set by `devcommands`, never cleared.

That severity is the point: it is what makes a skill level on this server mean something. But
it has no undo, and it applies to your own character exactly as it does to everyone else's.

## How it works

The shape is lifted from Core's version handshake, because the problem is identical. Both ends
register an RPC the moment the connection object exists, in `ZNet.OnNewConnection`, which
happens before either side sends `PeerInfo`. ZRpc delivers in order on one connection, so by
the time `RPC_PeerInfo` runs the answer has arrived and there is something to judge. Anything
later means deciding on data that has not turned up yet — and the symptom of getting that wrong
is a door that admits the first connection and works ever after.

Two deliberate differences from the version that lived in Boon:

**It refuses before the player is admitted.** Boon's ran after spawn, on a routed RPC, so a
refused player watched the world load and then got dropped — which reads far more like a crash
than like a rule.

**The client sends its raw world list, and the server does the arithmetic.** At handshake time
the client does not reliably know which world it is joining; the UID arrives later. Boon asked
the client to subtract the current world itself, which is part of why it had to run so late.
Sending the list lets the server — which certainly knows its own UID — work it out, and lets
the whole exchange finish before anyone is let in.

**The reason travels to the client.** Valheim's refusal screen carries no text of its own, so
the message is sent over the wire before the disconnect and logged on the client's own machine.
Being told which rule you broke is the difference between a door and a mystery.

## Honest limits

`PlayerProfile` lives on the client, so everything judged here is **self-reported**. A
purpose-built client can lie about all of it. What this catches is the ordinary case — an
unmodified player bringing a character that levelled somewhere else — because the game records
that itself and has no reason to misreport it.

This is a house rule with a lock on the door, not a security boundary. Core's version gate
makes it meaningful by refusing clients that do not have the plugin at all, but a client that
has it and has been modified is beyond what any of this can see.

## Scope

Registers with Core at `Requirement.Everyone`. Not because clients decide anything — only the
server does — but because the facts being judged live on the client and have to be reported,
so a client without the plugin answers nothing.

**Built and never run.** It compiles, and its handshake is the same shape as Core's, which is
proven. Nothing in it has been watched refusing or admitting a real connection.
