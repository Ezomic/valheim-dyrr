# Dyrr

Dyrr is a join policy for Valheim servers. A character that has played on other worlds, that
has used cheats, or that arrives with mods the server does not permit can be refused when it
connects. Nothing is refused until an admin turns `Enforce` on.

It also kicks idle players on dedicated servers, reports when a character comes back carrying
something it did not leave with, and stops your own client from taking a character into a world
it does not belong to.

## Features

- Six checks at the join: the character has spawned in another world, the game has flagged it
  for cheats, it has run a console command the game classes as a cheat, its own records
  disagree with each other, the client is running mods the server does not permit, or it did
  not answer at all. Each can be switched off on its own.
- `Enforce` is off by default. Every connection is still judged and the verdict is written to
  the log, so you can see who would be turned away before anyone actually is.
- A refused player is told which rule they broke, on their own screen if they have Longhouse
  Core, and in their own log either way.
- Character protection on the client: a character is bound to the first world it plays in, and
  the game refuses to start it anywhere else. This works whether or not the server runs Dyrr.
- Idle kick on dedicated servers, with a chat warning first.
- Inventory watch: a log line when a character's inventory changed while it was offline. This
  only ever reports. It never refuses or kicks.
- A `dyrr` console command that prints the standing verdict for everyone currently connected.

## How the join check works

Both ends register an RPC as soon as the connection object exists, before either side sends
PeerInfo. The client sends what it knows about its own character: the character name, the
cheats flag and its counter, the name of every console command the character has ever run, the
UIDs of every world it has spawned in, and the BepInEx plugin GUIDs loaded on that machine. The
server does the arithmetic in a prefix on `ZNet.RPC_PeerInfo` and refuses before the player
spawns, so a refused player never watches the world load first. All of it comes out of
`PlayerProfile`, which lives on the client's disk, so all of it is self-reported. See
[Known limits](#known-limits).

Which console commands count as cheats is decided on the server, from the server's own
`Terminal` command table, so a cheat command added by some other mod on the server counts for
free. A dedicated server has no guarantee of having built that table when the first player
knocks, so there is a fallback list of the 73 vanilla commands registered with `isCheat: true`.

**The mod check is the one that reaches cheating on a dedicated server.**
`Console.IsCheatsEnabled()` returns `ZNet.instance.IsServer()`, so a client's own `devcommands`
does nothing on someone else's server. Anyone cheating there is running a mod that patched
around that line, which makes "what is this client running" the more useful question.

**The tamper check does not need the client to be honest, only consistent.** The game writes
the same facts in more than one place: `m_usedCheats` is a bool, `PlayerStatType.Cheats` is a
counter incremented on the next line, and `m_knownWorlds` records worlds by name at save time
where `m_worldData` records them by UID at spawn. A flag that is clear beside a counter above
zero, or more world names than world UIDs, is an edited save. Neither inequality can be
produced by playing the game.

## Character protection

Refusing a character at the join arrives too late to help it. Loading a character into any world
writes that world into `PlayerProfile.m_worldData` permanently, and nothing in the game ever
removes the entry. So the client half refuses the trip instead.

Each character is bound to the first world it is seen in, recorded in
`BepInEx/config/dyrr-home.txt`. After that:

- Starting a **local world** with a character bound elsewhere is refused at
  `FejdStartup.OnWorldStart`, with a popup naming both worlds and the file to edit.
- **Joining a server** whose world is not the character's home is dropped inside
  `RPC_PeerInfo`, before the character spawns and before the game writes anything. This works
  whatever the server does, and whether or not the server runs Dyrr at all. That is
  `ProtectOnServers`.

Neither asks for confirmation. The damage has no undo, so a confirm dialog would just be a
button for doing the unfixable thing.

The character select screen carries the answer under the character's name, in a clone of
vanilla's own "Cloud save" label: `Belongs to <world>`, `Not bound to a world yet`,
`Has played in <world> and <world>` for a character that already has more than one, or
`Belongs to a world that is not on this PC` when the world is a server's. The line follows
`ProtectCharacter` and disappears with it.

A character with exactly one world in its own save is bound on the spot even if Dyrr has never
watched it play. A character with more than one is left unbound, since there is no single home
left to defend, and those are exactly the characters an enforcing server turns away.

## Idle kick

On dedicated servers only. A player whose character has not moved 5cm or turned one degree for
`IdleMinutes` is kicked, after a chat warning `IdleWarnMinutes` ahead. Position and facing are
read off the character's ZDO every five seconds. Walking, fighting, turning the camera or
sorting a chest all reset the clock.

The kick itself is vanilla's own `InternalKick`, the same path the console command takes, so the
player gets the ordinary kicked screen with a reason attached. A locally hosted world is never
watched.

## Inventory watch

A player's inventory never crosses the wire in vanilla: it lives in their own `.fch` on their
own disk. So the client sends a tally of what it is carrying every `InventoryInterval` seconds,
the server keeps the most recent one, and compares the first report of the **next** session
against it. What that catches is a change made while the character was away, rather than
ordinary play.

The line names what moved, biggest movement first. Item names are the game's own localisation
tokens, and anything above quality 1 carries its level:

```
Inventory changed while away: Balder, +40 $item_iron, +12 $item_silverore, +1 $item_sword_iron*4
```

Two honest sources of false alarms: a client that crashes stops sending, so the stored snapshot
can be up to one interval stale, and Valheim can itself roll a character back to its own last
local save. Both look identical to tampering at the moment the report arrives, which is why
this only ever writes a log line. Nothing here can refuse, kick or correct anybody.

## Installation

Through a mod manager, install
[Dyrr](https://thunderstore.io/c/valheim/p/Ezomic/Dyrr/) and it is done. By hand, put
`Dyrr.dll` in `BepInEx/plugins/Dyrr/`.

- Requires [BepInEx 5.4.2350](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).
  BepInEx 5 API only, not compatible with BepInEx 6.
- [Longhouse Core](https://thunderstore.io/c/valheim/p/Ezomic/Longhouse_Core/) is an optional
  soft dependency. See [Multiplayer](#multiplayer).
- Install it on the server **and** on every client. The facts being judged live on the client,
  so a client without Dyrr answers nothing, and `RefuseUnreported` is on by default.

Start the game or the server once and quit. That first run writes
`BepInEx/config/ezomic.valheim.dyrr.cfg`, which does not exist until the plugin has loaded.
This is the usual reason people think the mod is broken.

Built against Valheim 1.0.7. Version 1.4.0 does not run on pre-1.0 Valheim, and 1.3.0 does not
run on 1.0.

## Before you turn Enforce on

Read this part. `Enforce` is the one setting here that can lock people out of a server,
including you.

- **The game never removes a world from a character's record.** One visit anywhere else is
  permanent for that character file. Restoring a backup taken before the trip is the only way
  back in, and it does work: a character refused for having travelled came back from backup and
  was admitted.
- **The cheat flag is the same.** `devcommands` sets it and nothing clears it.
- **Being an admin does not exempt you.** Dyrr does not consult `adminlist.txt` anywhere. Your
  own character is judged by exactly the same rules as everybody else's.
- **With `Enforce` on, a client without Dyrr is refused** by `RefuseUnreported`. If you are
  running Core as well, its version check turns that client away first.
- **Every server you run must enforce.** A lenient server is a hole a bound character walks
  into, and the lenient one is what ruins it. Keep a separate character per server.

The order that works: leave `Enforce` off, let people connect, run `dyrr` and read the log, put
the innocent plugins in `dyrr-mods.txt`, then turn `Enforce` on.

`ProtectCharacter` is on while `Enforce` is off. Refusing other people is a policy somebody
should choose; refusing to let you ruin your own character is not.

## Configuration

`BepInEx/config/ezomic.valheim.dyrr.cfg`. Every entry carries its own comment in the file.

**Door** (server side)

| Setting | Default | Effect |
| --- | --- | --- |
| `Enabled` | `true` | Off leaves the plugin loaded and judging nothing. Server side only |
| `Enforce` | `false` | On refuses the connection. Off logs what would have been refused |
| `RefuseOtherWorlds` | `true` | Refuse a character that has spawned in any world but this one |
| `RefuseCheats` | `true` | Refuse a character the game has flagged for `devcommands` use |
| `RefuseCheatCommands` | `true` | Refuse a character that has run a command the game marks as a cheat. The command is named in the log and in the refusal |
| `RefuseTampered` | `true` | Refuse a character whose own records disagree with each other |
| `RefuseUnreported` | `true` | Refuse a connection that answers nothing, or whose profile could not be read |
| `RefusedMessage` | `This server refused this connection.` | Sent to the refused client. The specific reason is appended as a sentence starting "It", so keep this one general |

**Protect** (client side)

| Setting | Default | Effect |
| --- | --- | --- |
| `ProtectCharacter` | `true` | Refuse to start a local world with a character that belongs to a different one, and record which world each character belongs to |
| `ProtectOnServers` | `true` | Extend that to servers: leave the connection before spawning if the server's world is not this character's home |

**Mods** (server side)

| Setting | Default | Effect |
| --- | --- | --- |
| `RefuseMods` | `Refuse` | `Off`, `Notice` or `Refuse`. Notice logs what it would have done and lets everyone in. The legacy `true` and `false` still parse as Refuse and Off |
| `ModPolicy` | `Allow` | `Allow`: only what this server runs, plus the allowlist. `Deny`: anything except `DeniedMods` |
| `AllowedMods` | *empty* | Extra GUIDs a client may run, comma separated. Prefer `dyrr-mods.txt`, below |
| `DeniedMods` | *empty* | GUIDs no client may run, comma separated. Only read under `Deny` |

`Notice` exists because `Enforce` is a single switch over every rule at once. Trialling the mod
list by turning `Enforce` off also stops refusing cheats and altered records for as long as the
trial runs. Use `Notice` for a week, read the log, then set `Refuse`.

The plugins the server itself runs are always permitted and never need listing, so adding a mod
to the server does not refuse everybody the next day. A value that is none of the three words is
read as `Refuse` and logged, so a typo cannot be the thing that opens a server.

**Inventory**

| Setting | Default | Effect |
| --- | --- | --- |
| `WatchInventories` | `true` | Report when a character comes back carrying something it did not leave with |
| `InventoryInterval` | `60` | Seconds between a client's inventory reports. Floored at 5 |
| `InventoryDetail` | `6` | How many changed items to name before the rest are counted |
| `InventoryNamesIds` | `false` | Put the character's player id beside the name in the log line |

**Idle** (dedicated servers)

| Setting | Default | Effect |
| --- | --- | --- |
| `KickIdle` | `true` | Kick players who have been completely still for `IdleMinutes` |
| `IdleMinutes` | `5` | Minutes of stillness before the kick |
| `IdleWarnMinutes` | `2` | Minutes of warning first, said once in the player's chat. `0` kicks without warning |

BepInEx writes every entry to disk on the first run, and the saved value beats a new default in
code. If a setting appears to do nothing after an update, check the `.cfg` before anything else.

## Files Dyrr writes

All three live in `BepInEx/config/`, as plain text.

**`dyrr-home.txt`** (client). One line per character: `playerId|worldUid|character|world`. Only
the two numbers are read; the names are there so you can recognise the line. Delete a line to
unbind that character. The file is re-read whenever it changes, so you can edit it with the game
running. Editing it can only damage your own character, which is why it is not defended.

**`dyrr-mods.txt`** (server). The allowlist, one GUID per line, `#` for comments. Written with
its own explanation the first time Dyrr looks for it. It is re-read whenever it changes, so
letting one friend keep their map mod takes effect on the next connection rather than at the
next restart. `AllowedMods` in the `.cfg` still works and is added to this, but BepInEx never
reloads a `.cfg` on its own, so that entry costs a server restart and everybody online.

**`dyrr-inventories.txt`** (server). The last inventory snapshot per character. Safe to delete;
you lose the next comparison and nothing else.

## Console commands

The console needs Valheim's `-console` launch argument on a client. A dedicated server has one
already. None of these are cheat commands and none are admin gated: the report is the server's
own state to whoever is already at its console, and the bindings are the local machine's own
file.

| Command | Does |
| --- | --- |
| `dyrr` | What the join check is doing here |
| `dyrr home` | Which world each character on this machine belongs to |
| `dyrr forget <id>` | Unbind a character, so the next world it plays in becomes its new home |

`dyrr forget` does not undo anywhere the character has already been. The game's record of that
is permanent and no mod can clear it.

On a server, `dyrr` prints something like:

```
Dyrr 1.4.0
World: 'midgard' (-4881...)
Enforce is OFF - failures are reported here and refused to nobody.
Checks: other worlds on, cheats on, cheat commands on, tampered on, unreported on
Mods: Allow (Refuse) - 12 permitted here
Refused so far this session: 0

  Ragnar  admitted
  Sigrun  would refuse: has played on 2 other world(s)
```

The same block also goes to `BepInEx/LogOutput.log`, because a console scrolls and a log file
does not. On a client it reports what that machine knows instead: whether a server has refused
it this session, and why, plus the local bindings.

Every plugin a joining client brings that the server does not run or allow is logged as it
connects, admitted or refused. That line is what you build the allowlist from:

```
A client brought 2 plugin(s) this server does not run or allow: randyknapp.mods.equipmentandquickslots, ...
```

A refusal names who it was, by character name and by the platform id off the socket:

```
Refused a connection: Balder (76561198662440314) has played on 1 other world(s)
```

## Multiplayer

Install Dyrr on the server and on every client. The server decides everything; the client only
answers the question and protects itself.

Longhouse Core is optional and the join check works without it. Two things are lost when it is
not installed:

- **The version check.** Core compares each Ezomic mod's version and build id when a client
  connects and makes the server reject mismatches. That matters more here than elsewhere,
  because the facts being judged are reported by the client, and an old build of Dyrr answering
  an unfamiliar question is exactly what the version check would have caught. A report in a
  format this build cannot read is treated as unreported rather than guessed at.
- **The refusal screen.** Valheim's kick screen carries no text of its own. Core is what puts
  the reason on it. Without Core, a refused player gets a generic screen and the reason in their
  own `BepInEx/LogOutput.log`.

With Core installed, Dyrr registers at `Requirement.Everyone`, so Core requires the plugin on
both ends. Core also applies the host's config values on connected clients in memory, without
writing their config file.

**What a refused player sees.** With Core: `This server refused this connection. It has played
on 2 other world(s).` on the kick screen, and the same line in their log. Without Core: the
stock kicked screen, and the line in their log. A refusal by the client's own protection is
different again: a local world gets a vanilla popup naming both worlds and the file to edit,
while a stopped server join gets a plain disconnect screen, with the reason on it only when Core
is installed.

## Known limits

- **Everything the client reports is self-reported**, the plugin list included. A purpose-built
  client can lie about all of it. More records only raise what a liar has to keep straight.
- **A cheat that never touches the console leaves no mark.** Confirmed here rather than
  reasoned about: a god mode toggle that calls `Player.SetGodMode` directly writes none of the
  four records the character checks read, so those checks correctly find nothing. `RefuseMods`
  is what sees that client, by its plugin GUID.
- **`RefuseTampered` has never fired in practice.** Producing a profile whose own records
  disagree takes deliberate editing nobody here has done. It cannot be tripped by playing the
  game wrong, so the risk is that it never fires rather than that it fires wrongly.
- **The inventory watch can raise a false alarm after a crash.** See that section.
- If Dyrr cannot read `PlayerProfile.m_worldData` at all, it says so once at error level and
  judges nobody on travel, rather than reading an unreadable list as an empty one. Both travel
  rules go quiet together and the server logs that it is judging without them.

## Troubleshooting

**There is no config file.** The plugin has to load once before BepInEx writes it. Start the
game or the server, quit, look again.

**A setting has no effect.** BepInEx saved the old value on first run and it beats the new
default. Edit the `.cfg`, not just the docs.

**Nobody is being refused.** `Enforce` is off by default. Run `dyrr` to see what the checks are
set to and what the verdict on each connected player is.

**Your own client is refused for mods.** A server does not run the tools you develop with. Put
their GUIDs in `dyrr-mods.txt`.

**The character select line is missing.** It follows `ProtectCharacter`. With that off there is
nothing being enforced for it to describe.

**A restored backup carries a stale home.** Backups do not touch `dyrr-home.txt`, which lives
beside the config rather than with the character. Use `dyrr forget <id>` or delete that
character's line.

**Upgrading from Threshold.** Dyrr was called Threshold until 2026-08-18. On the first run it
copies `ezomic.valheim.threshold.cfg` to its new name and adopts `threshold-home.txt` (and
`boon-home.txt` before that) if `dyrr-home.txt` does not exist. Both originals are left in
place. Confirmed on a real run, config and bindings intact.

## Status

Run against a real dedicated server in both directions: it refuses a character that has been
elsewhere, with the reason on the client's own screen and in its own log, and it admits a clean
character on an enforcing server. Both cheat checks have fired on a deliberately flagged
character. Backup recovery, the menu guard's binding and the migration from the Threshold-era
files are all confirmed. `RefuseTampered` is the one check that has never seen the thing it
looks for.

1.4.0 is the Valheim 1.0 rebuild. The achievements system turned `PlayerProfile`'s single stat
record into an array of ten and moved the known-worlds and known-commands lists inside it, and
the cheat counter is now resolved by name rather than by the ordinal the compiler baked in.
Full history in [CHANGELOG.md](CHANGELOG.md).

## Bug reports

[The Discord](https://discord.gg/hJzAVaZ5wb) is the fastest route, and the right one if you are
not sure whether what you are seeing is a bug. Issues on
[the repo](https://github.com/Ezomic/valheim-dyrr) work too and suit anything long.

Bring `BepInEx\LogOutput.log` from whichever end saw the problem, and say whether you were on a
server or in single player. If a refusal is wrong, the `dyrr` output and the character's line
from `dyrr-home.txt` are the two things that settle it. If a vanilla mechanic broke, check
`AppData\LocalLow\IronGate\Valheim\Player.log` as well, because gameplay exceptions land there
rather than in the BepInEx log.

## Discord

[discord.gg/hJzAVaZ5wb](https://discord.gg/hJzAVaZ5wb) is where mod information, updates,
support, bug reports and compatibility questions go. There is also a small EU server running the
pack if you want somewhere to play. Details are in the Discord.

## Licence

MIT. See [LICENSE](LICENSE).

Robbin Thijssen / Thijssen Software.

## Part of Longhouse

Dyrr is one of the mods in [the Longhouse pack](https://thunderstore.io/c/valheim/p/Ezomic/Longhouse/),
which pins exact versions of its members. You do not need the pack to use this, and it behaves
the same on its own.
