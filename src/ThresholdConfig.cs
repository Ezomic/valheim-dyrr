using BepInEx.Configuration;

namespace Threshold
{
    /// <summary>
    /// Everything tunable.
    ///
    /// Note the standing BepInEx trap: every entry is written to disk on first run and the
    /// saved value beats a new default in code. Changing a default here does nothing on a
    /// machine that has already run the plugin - edit the cfg as part of the same change.
    /// </summary>
    internal static class ThresholdConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Enforce;
        internal static ConfigEntry<bool> RefuseOtherWorlds;
        internal static ConfigEntry<bool> RefuseCheats;
        internal static ConfigEntry<bool> RefuseUnreported;
        internal static ConfigEntry<string> RefusedMessage;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Door", "Enabled", true,
                "Off leaves the plugin loaded and checking nothing.");

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
