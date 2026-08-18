# Changelog

Notable changes to Dyrr. Format follows [Keep a Changelog](https://keepachangelog.com),
and the mod uses [semantic versioning](https://semver.org).

## [1.1.0] - 2026-08-18

The preventive half now covers servers, which is what it was always supposed to do.

**Built, not yet run in game.** Everything below compiles and nothing here has been tested
against a real server.

### Added

- **The client refuses a join into the wrong world, whatever the server does.** Until now the
  only thing standing between a character and a server that would ruin it was that server
  choosing to enforce, because a server's world identity is not known in the menu and there was
  nothing to check. There is one moment on the join path where it is known and nothing has been
  written yet - the client reads the world name, seed and uid inside `ZNet.RPC_PeerInfo`, and
  the permanent record this mod exists to prevent is only ever written by
  `PlayerProfile.GetWorldData`, which needs a spawned player. So the check goes there, and the
  connection is dropped the way vanilla drops a kicked one.

  This closes the hole the previous README described and left open: a non-enforcing server
  ruining a character that a different server would then refuse forever. It works whether or
  not the server runs this mod at all. New setting `ProtectOnServers`, on by default, under
  `ProtectCharacter`.

- **A `dyrr` console command.** `dyrr` prints what the door is doing here: the world, whether
  Enforce is on, which checks are live, how many connections have been refused this session,
  and the standing verdict for every player currently connected. That last part is the point of
  Enforce being off - it is meant to be sat in while deciding, and until now it answered "who
  would stop being able to play?" one line at a time into a log, at the moment each player
  connected. On a server the report is written to the BepInEx log as well, because a console
  scrolls and a log file does not.

  `dyrr home` lists this machine's character bindings, and `dyrr forget <id>` unbinds one
  without going and finding the file. Neither is a cheat command and neither is admin-gated:
  the report is the server's own state to whoever is already at its console, and the bindings
  are this machine's own file.

- **`dyrr-home.txt` now carries the character and world names** beside the two ids, and the
  refusal popup names the world rather than quoting a bare 19-digit number. Nothing is ever
  matched on the names - two fields still load, four still load on an older build - but the one
  action this mod asks of a player is "delete the line starting with your character's id", and
  that is much harder when every line looks the same.

### Fixed

- **A hand edit to `dyrr-home.txt` was silently undone.** The file was read once per process
  and the whole set written back on every bind, so deleting a line - the documented fix for a
  wrong binding, and what the refusal popup tells you to do - was reverted by the next
  character that bound. It is now re-read whenever it changes on disk, so the file the popup
  points at is the file that is actually read, and the edit can be made without restarting.

- `Home.Forget` existed and nothing called it. It is `dyrr forget` now.

## [1.0.0] - 2026-08-18

First published release.

The number means published and nothing else. It sat at 0.9.0 while the repo was public and
Thunderstore had nothing on it, because a 1.0 with no package behind it claims a release that
does not exist, and because a Thunderstore version can never be reissued once uploaded - so the
number had to be spent on the build that actually ships rather than on one that drifted away
from it on disk.

Everything below this line was already true at 0.9.0. Nothing about the mod's behaviour changed
to get here.

### Renamed from Threshold, 2026-08-18

*Dyrr* is Old Norse for the doorway itself, which is both what the mod guards and a better fit
beside Vaettir, Nidling and Rist than an English abstract noun was.

Renaming before publishing rather than after is the entire reason it was cheap. Nothing was on
Thunderstore to leave stranded, and this mod registers no prefabs, so there are no saved ZDOs
keyed on a name that would stop resolving. Two things did have to be carried over by hand, and
both fail silently rather than loudly if forgotten:

- **The config file.** BepInEx names it after the plugin GUID, so a rename hands everyone a
  file of defaults. That is not merely lost preferences: `Enforce` defaults to off, so a server
  that had the door on would quietly have it off with nothing said. The plugin now copies
  `ezomic.valheim.threshold.cfg` to its new name on first run, before binding anything, and
  leaves the old file alone.
- **The character bindings.** `threshold-home.txt` is adopted the same way `boon-home.txt`
  already was, and the fallback is now an ordered chain rather than a single name, so a machine
  that skipped a release cannot fall between the two. Losing these would hand every character
  one free trip to another world, silently and exactly once, and that trip has no undo.

Both were then confirmed on a real run rather than left to reasoning: all seven settings and
every binding line came across, and both original files were still in place afterwards. The
adoption also correctly does not repeat on the next launch.

### Fixed since the split

- **The dedicated server no longer binds a character it does not have.** A server has a
  `PlayerProfile` object with nobody behind it, so the binding ran there too and minted a
  fresh phantom id on every startup, one junk line per restart, forever. Binding now requires
  a local player, which is also the more correct moment: a character has not played anywhere
  until it spawns.
- **Bindings are adopted from Rist's `rist-home.txt`** on first run if `dyrr-home.txt`
  does not exist. Without that, moving the feature between mods would have silently unbound
  every character on the machine and handed everyone one free trip to another world.

### Verified against a real dedicated server

Both branches, which matters more than it sounds. It refuses a character that has been
elsewhere, with the reason on the client's own screen and in its own log, and it **admits** a
clean character on an enforcing server. Until the second one happened, "works" and "refuses
everybody" were indistinguishable, because every test until then used a character that had
genuinely travelled.

Also confirmed: the menu guard's binding, the adoption of bindings from Rist's old
`rist-home.txt`, and restoring a character backup as the documented recovery. A refused
character came back from backup and was admitted.

### Every server must enforce

Found by running two servers, one enforcing and one not. The menu guard covers local worlds
only; the door covers servers only where `Enforce` is on. A non-enforcing server is therefore a
hole a bound character walks into, and all the mod can do afterwards is log *"too late to stop
it - that world is now written into the character"*. The lenient server is the one that ruins
the character.

Enforce on every server, and keep a separate character for each.

### Known limits

- `RefuseCheats` is untested; it needs a character deliberately flagged by `devcommands`.

## [0.1.0] - 2026-08-16

Split out of [Rist](https://github.com/Ezomic/valheim-rist), where it never belonged.

### The line this sits on

> **Who comes through the door is server policy. It is not a levelling mod's decision.**

Rist awards character levels for skill gains, so it wanted to know whether a character's skills
were earned here, and answered by refusing the connection. That put an XP system in charge of
who is allowed to play, and when it fired the player got Valheim's generic kick screen with the
reason written only to the server's log, which on someone else's server they can never read.
The first time it happened, the person affected blamed an unrelated mod.

### Added

- **The door.** Connections are refused for characters that have spawned in another world
  (`RefuseOtherWorlds`), that the game has flagged for cheats (`RefuseCheats`), or that
  answered nothing at all (`RefuseUnreported`).
- **The menu guard, which is the half that actually protects you.** Refusing at the door is
  the lesser half: by then the harm is done, because loading a character into any world writes
  that world into its profile permanently. So the client also refuses to start a *local* world
  with a character belonging to a different one, at `FejdStartup.OnWorldStart`, the last
  moment anything can be done about it.
- **The refusal is shown on screen**, on both machines, under the stock kicked line, not
  written to a log the person who needs it cannot read.
- **A home file per character**, `BepInEx/config/dyrr-home.txt`, binding each character to
  the first world that accepted it. Plain text on purpose: it is protection rather than
  enforcement, and editing it can only damage your own character.

### Enforce is off by default, and that is deliberate

This is the one setting in the family that can lock people out of a server, including you.
**The game never removes entries from a character's world list**, so one visit anywhere else is
permanent for that character file, and restoring a backup from before the trip is the only way
back in. The cheat flag behaves the same way: set by `devcommands`, never cleared.

Turning it on should be an admin deciding, having read that paragraph, not something that
happens because a mod got installed.

### Verified

Refused a real connection on a dedicated server, logged the reason on both machines, and showed
it on the refusal panel. Not yet verified: admitting a genuinely clean character, and
`RefuseCheats`.
