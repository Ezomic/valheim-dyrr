using BepInEx.Configuration;

namespace Dyrr
{
    /// <summary>
    /// Everything tunable.
    ///
    /// Note the standing BepInEx trap: every entry is written to disk on first run and the
    /// saved value beats a new default in code. Changing a default here does nothing on a
    /// machine that has already run the plugin - edit the cfg as part of the same change.
    /// </summary>
    internal static class DyrrConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Enforce;
        internal static ConfigEntry<bool> RefuseOtherWorlds;
        internal static ConfigEntry<bool> RefuseCheats;
        internal static ConfigEntry<bool> RefuseUnreported;
        internal static ConfigEntry<string> RefusedMessage;
        internal static ConfigEntry<bool> ProtectCharacter;
        internal static ConfigEntry<bool> ProtectOnServers;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Door", "Enabled", true,
                "Off leaves the plugin loaded and checking nothing. Only affects the server "
                + "side; character protection has its own switch below.");

            // Client-side, and on by default even though Enforce is not. Refusing a connection
            // is a policy somebody should choose; refusing to let a player irreversibly ruin
            // their own character is just not standing by while it happens.
            ProtectCharacter = cfg.Bind("Protect", "ProtectCharacter", true,
                "Refuse to start a local world with a character that belongs to a different "
                + "one, and remember which world each character belongs to.\n"
                + "This is the only half of this mod that can prevent anything. By the time "
                + "the door refuses a character the damage is already permanent - the game "
                + "recorded the world it visited and never removes that record. The binding "
                + "lives in BepInEx/config/dyrr-home.txt, which you can edit; it only "
                + "protects your own characters, so there is nothing there worth defending "
                + "against you.\n"
                + "It refuses rather than asking, because a confirm dialog on an irreversible "
                + "action is just a button for doing the unfixable thing.");

            // On by default, and it is a behaviour change for anyone upgrading, so it is worth
            // being plain about why it is not opt-in. Until this existed, ProtectCharacter
            // covered local worlds only and a character could still be ruined by joining any
            // server that was not enforcing - which is not a hypothetical, it is how a
            // character was ruined here by the lenient one of two servers running side by
            // side. Leaving the hole open by default would mean the protective half of the mod
            // still did not protect the ordinary case.
            ProtectOnServers = cfg.Bind("Protect", "ProtectOnServers", true,
                "Extend the check above to servers: leave the connection before spawning if "
                + "the server's world is not the one this character belongs to.\n"
                + "This works whatever the server does, and whether or not the server runs "
                + "this mod at all. It is the same rule the menu applies to local worlds, "
                + "moved to the one moment on a join where the world is known and nothing has "
                + "been written yet.\n"
                + "Turn it off if you deliberately take one character between several worlds. "
                + "Note that doing so is exactly what the door refuses people for.");

            Enforce = cfg.Bind("Door", "Enforce", false,
                "On refuses the connection. Off only logs what would have been refused.\n" +
                "Off by default, and deliberately so. This is the one setting in the family " +
                "that can lock people out of a server, including you, so it should be a thing " +
                "somebody turns on having read what it does - not something that happens " +
                "because a mod was installed.");

            RefuseOtherWorlds = cfg.Bind("Door", "RefuseOtherWorlds", true,
                "Refuse a character that has spawned in any world but this one.\n" +
                "Read this before enabling Enforce: the game never removes entries from a " +
                "character's world list, so a single visit anywhere else is permanent for " +
                "that character file. Restoring a backup taken before the trip is the only " +
                "way back in. That is the intended severity - it is what makes skill levels " +
                "on this server mean something - but it has no undo.");

            RefuseCheats = cfg.Bind("Door", "RefuseCheats", true,
                "Refuse a character the game has flagged as having used cheats. Also " +
                "permanent, and set by devcommands rather than by anything subtle.");

            RefuseUnreported = cfg.Bind("Door", "RefuseUnreported", true,
                "Refuse a connection that answers nothing, or whose profile could not be " +
                "read. A door that opens when the question goes unanswered is not a door - " +
                "but Core's version gate should already have turned away a client without " +
                "this plugin, so in practice this is a backstop.");

            RefusedMessage = cfg.Bind("Door", "RefusedMessage",
                "This server only accepts characters that have never played anywhere else.",
                "Sent to the refused client so it lands in their own log. Valheim's refusal " +
                "screen carries no text of its own, and a player on somebody else's server " +
                "can never read that server's log - so without this, being turned away is " +
                "indistinguishable from a crash.");
        }
    }
}
