# Dyrr

A door policy. Characters that have played on another world do not come in.

*Dyrr* is Old Norse for the doorway itself. This mod was called **Threshold** until 2026-08-18;
nothing about it changed but the name. If you ran it under the old name, your settings and your
character bindings are carried over on the first run and you do not need to do anything.

## Installing

Needs BepInEx. Core is optional, see below. Through a mod manager it is one install. By hand,
put `Dyrr.dll` in `BepInEx/plugins/Dyrr/`.

Then start the game once and quit. That first run writes the config file, which does not exist
before the mod has loaded, and that is the usual reason people think it is broken.

**Nothing is refused until you turn `Enforce` on.** Out of the box the door only logs what it
would have refused, on purpose. The half that does work immediately is the menu guard on the
client, which stops you taking a character into a world it does not belong to.

## Why this is its own mod

It used to live inside [Rist](../rist), and that was the wrong place for it.

Rist awards character levels for skill gains, so it needs to know whether a character's skills
were actually earned on this server. It answered that by **refusing the connection**, which
put a levelling mod in charge of who is allowed to play. The failure mode is exactly what you
would expect: a bug in an XP system locks people out of a server. And when it fired, the player
got Valheim's generic kick screen with the reason written only to the server's log, which on
somebody else's server they can never read. The first time it happened in practice, the person
affected assumed a completely unrelated mod had blocked them.

So the two halves were separated by what each is actually for:

- **Rist** keeps the question it has standing to ask, *do I pay for these levels?*, and
  answers it by withholding XP. Nobody is disconnected, nothing already earned is removed, and
  the player is told once, on screen.
- **Dyrr** owns the question of who comes through the door. That is a server policy, it
  has nothing to do with levelling, and it is the whole of this mod.

The split also makes each honest about its own limits, which the fused version could not be.

## Two halves, and the important one is the preventive half

**Refusing at the door is the lesser half**, because by the time it fires the harm is already
done. Loading a character into any world writes that world into `PlayerProfile.m_worldData`
permanently. Refusing the connection afterwards tells you about a mistake you can no longer
undo.

So the other half runs on the client, in the menu: **it refuses to start a local world with a
character that belongs to a different one**. `FejdStartup.OnWorldStart` is the commit point and
the last moment anything can be done.

It refuses rather than asking. A confirm dialog was tried and rejected: the damage is
irreversible, so a prompt is just a button for doing the unfixable thing by clicking through
it. A dead end forces a wrong answer to be diagnosed instead of waved past, so the popup
carries everything needed to correct it: both ids and the file to edit.

Each character is bound to the first world it is accepted in, recorded in
`BepInEx/config/dyrr-home.txt`. That file is protection, not enforcement; editing it only
lets you damage your own character, which is why it is plain text you can open and fix. Nothing
that turns *other people* away is ever read from the client.

Only local worlds are covered by the menu guard, because a server's world identity is not known
until after connecting. That path is not unguarded: it is the door, which refuses before the
character ever spawns. The two halves cover each other, which is exactly why they belong in one
mod. This used to live in Rist, warning about a lockout Rist had no part in.

## What it checks

Three questions, asked of every connection: has this character spawned in a world other than
this one, has the game flagged it for cheats, and did it answer at all. Each can be turned off
on its own; see [Settings](#settings) for the full list.

`Enforce` is **off by default**, and deliberately. This is the one setting in the family that
can lock people out of a server, including you. It should be something an admin turns on having
read what it does, not something that happens because a mod got installed.

## Read this before turning Enforce on

**The game never removes entries from a character's world list.** One visit anywhere else is
permanent for that character file. Restoring a backup taken before the trip is the only way
back in. The cheat flag is the same, set by `devcommands`, never cleared.

That severity is the point: it is what makes a skill level on this server mean something. But
it has no undo, and it applies to your own character exactly as it does to everyone else's.

## How it works

The shape is lifted from Core's version handshake, because the problem is identical. Both ends
register an RPC the moment the connection object exists, in `ZNet.OnNewConnection`, which
happens before either side sends `PeerInfo`. ZRpc delivers in order on one connection, so by
the time `RPC_PeerInfo` runs the answer has arrived and there is something to judge. Anything
later means deciding on data that has not turned up yet, and the symptom of getting that wrong
is a door that admits the first connection and works ever after.

Two deliberate differences from the version that lived in Rist:

**It refuses before the player is admitted.** Rist's ran after spawn, on a routed RPC, so a
refused player watched the world load and then got dropped, which reads far more like a crash
than like a rule.

**The client sends its raw world list, and the server does the arithmetic.** At handshake time
the client does not reliably know which world it is joining; the UID arrives later. Rist asked
the client to subtract the current world itself, which is part of why it had to run so late.
Sending the list lets the server, which certainly knows its own UID, work it out, and lets
the whole exchange finish before anyone is let in.

**The reason travels to the client.** Valheim's refusal screen carries no text of its own, so
the message is sent over the wire before the disconnect and logged on the client's own machine.
Being told which rule you broke is the difference between a door and a mystery.

## Core is optional

Dyrr installs and runs on its own, which is useful if you want a door policy and none of the rest
of this suite. Core is a **soft** dependency, and installing Dyrr no longer installs it.

**The door itself works standalone.** Doorman carries its own handshake and does its own
refusing on the server side of `RPC_PeerInfo`; none of that is Core's.

Two things are given up. The **version gate**, which matters more here than elsewhere: the
facts being judged are reported *by the client*, so an old build of Dyrr answering an
unfamiliar question is precisely the case the gate would have caught. And the **refusal
screen**: Core is what carries the reason through to Valheim's kick dialog. Without it a
refused player gets the reason in their own log and a generic screen, which is exactly the
failure that splitting this out of Rist was meant to fix. It still logs; it just cannot draw.

Install Core on the clients to put the reason back on the screen.

## Honest limits

`PlayerProfile` lives on the client, so everything judged here is **self-reported**. A
purpose-built client can lie about all of it. What this catches is the ordinary case, an
unmodified player bringing a character that levelled somewhere else, because the game records
that itself and has no reason to misreport it.

This is a house rule with a lock on the door, not a security boundary. Core's version gate
makes it meaningful by refusing clients that do not have the plugin at all, but a client that
has it and has been modified is beyond what any of this can see.

## Settings

The file is `BepInEx/config/ezomic.valheim.dyrr.cfg`. Every entry carries a comment explaining
itself, so the file is the reference; this is the map.

| Setting | Default | What it does |
| --- | --- | --- |
| `Enabled` | on | Off leaves the plugin loaded and checking nothing. Server side only |
| `Enforce` | **off** | On refuses the connection. Off only logs what would have been refused |
| `RefuseOtherWorlds` | on | Refuse a character that has spawned in any world but this one |
| `RefuseCheats` | on | Refuse a character the game has flagged for `devcommands` use |
| `RefuseUnreported` | on | Refuse a connection that answers nothing, or an unreadable profile |
| `RefusedMessage` | *a sentence* | Sent to the refused client so it lands in their own log |
| `ProtectCharacter` | on | The client-side menu guard, and the binding that feeds it |

Note the standing BepInEx behaviour: every entry is written to disk on the first run and the
saved value beats a new default in code. Changing a default in a later version does nothing on
a machine that has already run the mod.

`ProtectCharacter` is on while `Enforce` is off, and that asymmetry is deliberate. Refusing
other people is a policy somebody should choose; refusing to let you irreversibly ruin your own
character is just not standing by while it happens.

## Scope

Registers with Core at `Requirement.Everyone`. Not because clients decide anything, since only the
server does, but because the facts being judged live on the client and have to be reported,
so a client without the plugin answers nothing.

## Every server must enforce, or the halves stop covering each other

This is the one thing to understand before running more than one server.

The menu guard covers **local worlds only**; a server's world identity is not known until
after connecting. The door covers **servers**, but only where `Enforce` is on. So a
non-enforcing server is a hole: a character belonging to another world walks in, the game
writes that world into its profile, and the mod can then do nothing but log

> Character 'X' is bound to world A but is in world B. Too late to stop it - that world is now
> written into the character.

Which is exactly what happened the first time two servers ran here, one enforcing and one not.
The character was ruined by the server that was being lenient.

So: if you run a test server alongside a real one, **enforce on both**, and keep a separate
character for each. One character, one server, permanently.

If you ever need a genuinely non-enforcing server, the fix is a client-side check in
`ZNet.RPC_PeerInfo`, where the world uid is known, before the player spawns, so the character
could be protected whatever the server does. Not implemented, because enforcing everywhere is
simpler and was enough.

## Recovering a ruined character

Restoring a character backup taken **before** the trip clears its travel record and it is
admitted again. This is the only way back, and it has been done: a character refused for
having visited another world came back from backup and was let in.

Note the backup does not touch `dyrr-home.txt`, which lives beside the config rather than
with the character. A restored character can therefore carry a stale home. That is harmless,
a home pointing at a server world matches no local world, so the menu guard simply refuses all
of them, which errs toward protection, but the world id quoted in the popup may be the old one.

## Status: v1.0

**Both branches have been run against a real dedicated server.** It refuses a character that
has been elsewhere, with the reason on the client's own screen and in its own log; and it
**admits** a clean character on an enforcing server. That second one mattered more than it
sounds: until it happened, "works" and "refuses everybody" were indistinguishable, because
every test until then involved a character that genuinely had travelled. The only arithmetic in
the mod is counting worlds that are not this one, and that is now confirmed in both directions.

Also confirmed: the menu guard's binding, the adoption of bindings from the old home file, and
the backup recovery above.

### Known gaps

**`RefuseCheats` has never fired.** It needs a character deliberately flagged by `devcommands`,
and no test so far has produced one. The code path is three lines and shares the reporting and
refusal machinery that the other two checks have exercised thoroughly, so the risk is that it
never triggers rather than that it triggers wrongly. It is on by default. If you would rather
not run an untested check, set `RefuseCheats = false`; the other two are the ones doing the
work.

**The migration from the Threshold-era files has been reasoned about, not run.** The config and
the character bindings are copied over on first run, and both are copies rather than moves, so
the originals are still there if anything goes wrong.

## License

MIT. See [LICENSE](LICENSE).

## Author

Robbin Thijssen / Thijssen Software.
