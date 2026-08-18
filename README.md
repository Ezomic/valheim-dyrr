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

It used to live inside [Rist](https://thunderstore.io/c/valheim/p/Ezomic/Rist/), and that was the wrong place for it.

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

So the other half runs on the client, and **it refuses to take a character into a world that is
not its own** - a local world or somebody else's server, whether or not that server runs this
mod. For a local world the commit point is `FejdStartup.OnWorldStart`, in the menu. For a
server there is no equivalent in the menu, because a server's world identity is not known until
after connecting; the check goes at the one moment on the join path where the world is known
and nothing has been written yet, and leaves before the character spawns.

It refuses rather than asking. A confirm dialog was tried and rejected: the damage is
irreversible, so a prompt is just a button for doing the unfixable thing by clicking through
it. A dead end forces a wrong answer to be diagnosed instead of waved past, so the popup
carries everything needed to correct it: both ids and the file to edit.

Each character is bound to the first world it is accepted in, recorded in
`BepInEx/config/dyrr-home.txt`. That file is protection, not enforcement; editing it only
lets you damage your own character, which is why it is plain text you can open and fix. Nothing
that turns *other people* away is ever read from the client.

The server case is worth spelling out, because it is where the window is narrow. The client
learns which world it is joining inside `ZNet.RPC_PeerInfo`, which reads the world name, seed
and uid straight off the wire. The permanent record - the entry in
`PlayerProfile.m_worldData` - is only ever written by `PlayerProfile.GetWorldData`, reached
from the logout point, the map data and the spawn point, all of which need a player who has
spawned. Between those two facts there is a window, and this is the whole of it. Leaving is
what vanilla itself does when you are kicked: set the connection status, drop the peer. The
logout that follows saves nothing, because `Game.SavePlayerProfile` does nothing at all
without a local player and there is not one yet.

So the door and the guard now cover the same ground from both sides, which is exactly why they
belong in one mod. This used to live in Rist, warning about a lockout Rist had no part in.

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

## Asking the door what it is doing

`Enforce` off is not a disabled state. It is the state an admin is meant to sit in while
deciding: every connection is still judged and what would have happened is still reported. The
trouble was that it reported one line at a time into a log, at the moment each player
connected, so the question actually being asked - *if I turn this on, who stops being able to
play?* - could only be answered by reading back through a log for lines that scrolled past
while nobody was watching.

`dyrr` in the console answers it standing:

```
Dyrr 1.1.0
World: 'midgard' (-4881...)
Enforce is OFF - failures are reported here and refused to nobody.
Checks: other worlds on, cheats on, unreported on
Refused so far this session: 0

  Ragnar  admitted
  Sigrun  would refuse: has played on 2 other world(s)
```

On a server the same block goes to `BepInEx/LogOutput.log`, because a console scrolls and a log
file does not. On a client it reports what that machine knows instead: whether a server has
refused it this session, and why.

Two more:

- `dyrr home` - which world each character on this machine belongs to, by name as well as id.
- `dyrr forget <id>` - unbind a character, so the next world it plays in becomes its new home.
  This does not undo anywhere it has already been; the game's record of that is permanent and
  no mod can clear it.

Neither is a cheat command and neither is admin-gated, because neither reads or changes
anything that was not already open to whoever can run it: the report is the server's own state
to somebody already at its console, and the bindings are this machine's own text file.

The console needs Valheim's `-console` launch argument on a client. A dedicated server has one
already.

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
| `ProtectCharacter` | on | The client-side guard, and the binding that feeds it |
| `ProtectOnServers` | on | Extend that guard to servers: leave a join into the wrong world before spawning |

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

## Running more than one server

This used to be a warning, and it was the mod's worst hole. The guard covered local worlds
only, and the door covered servers only where `Enforce` was on - so a non-enforcing server was
a gap a character walked straight into. The game wrote that world into the profile, and the mod
could do nothing but log

> Character 'X' is bound to world A but is in world B. Too late to stop it - that world is now
> written into the character.

Which is exactly what happened the first time two servers ran here, one enforcing and one not.
The character was ruined by the server that was being lenient.

**As of 1.1 the client refuses that join itself**, before spawning, whatever the server does
and whether or not the server runs this mod. That is what `ProtectOnServers` is, and it is on
by default. The hole is closed from the side that has something to lose.

The advice has not changed, because a lock is not a reason to stop being careful: keep a
separate character per server. One character, one world, permanently. What has changed is that
forgetting to do so is no longer irreversible.

## Recovering a ruined character

Restoring a character backup taken **before** the trip clears its travel record and it is
admitted again. This is the only way back, and it has been done: a character refused for
having visited another world came back from backup and was let in.

Note the backup does not touch `dyrr-home.txt`, which lives beside the config rather than
with the character - `dyrr forget <id>`, or deleting that character's line, clears the binding
if it is now wrong. A restored character can therefore carry a stale home. That is harmless,
a home pointing at a server world matches no local world, so the menu guard simply refuses all
of them, which errs toward protection, but the world id quoted in the popup may be the old one.

## Status: v1.1

**Both branches have been run against a real dedicated server.** It refuses a character that
has been elsewhere, with the reason on the client's own screen and in its own log; and it
**admits** a clean character on an enforcing server. That second one mattered more than it
sounds: until it happened, "works" and "refuses everybody" were indistinguishable, because
every test until then involved a character that genuinely had travelled. The only arithmetic in
the mod is counting worlds that are not this one, and that is now confirmed in both directions.

Also confirmed: the menu guard's binding, the adoption of bindings from the old home file, and
the backup recovery above.

### New in 1.1, and untested

The client-side guard on servers, the `dyrr` command, and the named bindings all **compile and
have not been run in game**. The rule they apply is the one the menu guard has been applying
correctly for a while; what is new is the moment it is applied at, and that moment has been
read out of the game's own code rather than observed. Treat it as unproven until a character
has actually been turned back at somebody else's server.

### Known gaps

**`RefuseCheats` has never fired.** It needs a character deliberately flagged by `devcommands`,
and no test so far has produced one. The code path is three lines and shares the reporting and
refusal machinery that the other two checks have exercised thoroughly, so the risk is that it
never triggers rather than that it triggers wrongly. It is on by default. If you would rather
not run an untested check, set `RefuseCheats = false`; the other two are the ones doing the
work.

The migration from the Threshold-era files is **confirmed working**, on a real run: the config
came across with all seven values intact, the character bindings came across in full, and both
original files were left untouched, since it copies rather than moves.

## License

MIT. See [LICENSE](LICENSE).

## Reporting bugs

[The Discord](https://discord.gg/hJzAVaZ5wb) is the fastest route, and the right one if
you are not sure whether what you are seeing is a bug at all. Issues on
[the repo](https://github.com/Ezomic/valheim-dyrr) work too and suit anything long.

Bring `BepInEx\LogOutput.log` if you can, and say whether you were on a server or your
own world. The log is most of the difference between a fix and a guess, and it is written
every session whether or not anything went wrong.

## Part of the Longhouse pack

This is one of [the Longhouse pack](https://thunderstore.io/c/valheim/p/Ezomic/Longhouse/),
a pinned set of my mods that installs in one click and is what the Longhouse server runs. You
do not need the pack to use this on its own, and nothing here behaves differently outside it.

[The Discord](https://discord.gg/hJzAVaZ5wb) is where the server lives if you want to play on
it: small, EU, hard combat difficulty and everything else vanilla.

## Author

Robbin Thijssen / Thijssen Software.
