# Changelog

Notable changes to Threshold. Format follows [Keep a Changelog](https://keepachangelog.com),
and the mod uses [semantic versioning](https://semver.org).

## [0.1.0] — 2026-08-16

Split out of [Boon](https://github.com/Ezomic/valheim-boon), where it never belonged.

### The line this sits on

> **Who comes through the door is server policy. It is not a levelling mod's decision.**

Boon awards character levels for skill gains, so it wanted to know whether a character's skills
were earned here — and answered by refusing the connection. That put an XP system in charge of
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
  with a character belonging to a different one, at `FejdStartup.OnWorldStart` — the last
  moment anything can be done about it.
- **The refusal is shown on screen**, on both machines, under the stock kicked line — not
  written to a log the person who needs it cannot read.
- **A home file per character**, `BepInEx/config/threshold-home.txt`, binding each character to
  the first world that accepted it. Plain text on purpose: it is protection rather than
  enforcement, and editing it can only damage your own character.

### Enforce is off by default, and that is deliberate

This is the one setting in the family that can lock people out of a server, including you.
**The game never removes entries from a character's world list**, so one visit anywhere else is
permanent for that character file, and restoring a backup from before the trip is the only way
back in. The cheat flag behaves the same way: set by `devcommands`, never cleared.

Turning it on should be an admin deciding, having read that paragraph — not something that
happens because a mod got installed.

### Verified

Refused a real connection on a dedicated server, logged the reason on both machines, and showed
it on the refusal panel. Not yet verified: admitting a genuinely clean character, and
`RefuseCheats`.
