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
        internal static ConfigEntry<bool> RefuseCheatCommands;
        internal static ConfigEntry<bool> RefuseTampered;
        // A string rather than a bool, and that is a change from 1.2.0 rather than a preference.
        // The only way to watch the mod rule without acting on it used to be Enforce = false,
        // which is one gate over the whole verdict - so "let us see who this would turn away
        // before we switch it on" also stopped refusing cheats and altered records for as long
        // as it ran. Nothing said so. Off/Notice/Refuse gives the mod rule its own tier, and
        // the legacy true and false still parse and still mean what they meant.
        internal static ConfigEntry<string> RefuseMods;
        internal static ConfigEntry<bool> WatchInventories;
        internal static ConfigEntry<float> InventoryInterval;
        internal static ConfigEntry<int> InventoryDetail;
        internal static ConfigEntry<bool> InventoryNamesIds;
        internal static ConfigEntry<bool> KickIdle;
        internal static ConfigEntry<int> IdleMinutes;
        internal static ConfigEntry<int> IdleWarnMinutes;
        internal static ConfigEntry<string> ModPolicy;
        internal static ConfigEntry<string> AllowedMods;
        internal static ConfigEntry<string> DeniedMods;
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


            RefuseCheatCommands = cfg.Bind("Door", "RefuseCheatCommands", true,
                "Refuse a character that has run a console command the game marks as a cheat."
                + "\n"
                + "Separate from RefuseCheats because it reads a different record. The game "
                + "keeps the name of every command a character has ever run, in "
                + "m_knownCommands, written a few lines after the cheats flag and outside the "
                + "branch that sets it - so a mod that clears the flag leaves this behind. "
                + "Which command was run is named in the log and in the refusal."
                + "\n"
                + "The classification is done here, from the server's own command table, so a "
                + "cheat command added by another mod on this server counts too.");

            RefuseTampered = cfg.Bind("Door", "RefuseTampered", true,
                "Refuse a character whose own records disagree with each other."
                + "\n"
                + "The game writes the same facts in more than one place, at different moments, "
                + "from different code: the cheats flag has a counter beside it, and the list "
                + "of worlds a character has spawned in has a second list of world names "
                + "written at save time. Clearing one is easy; clearing all of them so they "
                + "still agree is different work, and a mod written to switch devcommands on "
                + "has not done it."
                + "\n"
                + "This is the only check here that does not depend on the client being honest, "
                + "only on it being consistent. A false positive would need a character whose "
                + "profile the game itself wrote inconsistently, which has not been seen."
                + "\n"
                + "The world half only runs when the client could actually read its own list "
                + "of worlds. A game update that moves or renames that field leaves the list "
                + "empty rather than wrong, which used to read as every character having wiped "
                + "it - so the whole server was refused, and told so. When that happens now, "
                + "the server logs that it is judging without the travel rules and lets people "
                + "in.");

            WatchInventories = cfg.Bind("Inventory", "WatchInventories", true,
                "Say so when a character comes back carrying something it did not leave "
                + "with. The client reports a tally of its own inventory every "
                + "InventoryInterval seconds; the server keeps the last one and compares "
                + "the first report of the next session against it, so what is reported is "
                + "a change made WHILE AWAY rather than ordinary play. "
                + "Self-reported, and honest about it: a player's inventory is never sent "
                + "to a server by the game - it lives in their own .fch - so a client "
                + "built to lie can lie. This catches the ordinary case, which is somebody "
                + "closing the game, editing the file and coming back richer. "
                + "A crash can also trip it: the last snapshot may be a minute stale, and "
                + "the game may itself roll a character back. The line says what moved so "
                + "forty iron can be told from one arrow.");

            InventoryInterval = cfg.Bind("Inventory", "InventoryInterval", 60f,
                "Seconds between a client's inventory reports. Lower means a crash loses "
                + "less and so raises fewer false alarms; it is a small package either way. "
                + "Floored at 5.");

            InventoryDetail = cfg.Bind("Inventory", "InventoryDetail", 6,
                "How many changed items to name before the rest are counted. A line naming "
                + "sixty items is a line nobody reads.");

            InventoryNamesIds = cfg.Bind("Inventory", "InventoryNamesIds", false,
                "Put the character's player id beside the name. Off by default because the "
                + "line goes to Discord and a name is enough to ask somebody about it.");

            KickIdle = cfg.Bind("Idle", "KickIdle", true,
                "Kick players who have been genuinely still - no movement, no camera - for "
                + "IdleMinutes, after a warning. Dedicated servers only: an AFK body holds "
                + "a slot, keeps its zones simulated and blocks the night from being "
                + "skipped, and the door works in both directions.");

            IdleMinutes = cfg.Bind("Idle", "IdleMinutes", 5,
                "Minutes of complete stillness before the kick. Moving, fighting, turning "
                + "the camera or sorting a chest all reset it; only a hands-off body does "
                + "not.");

            IdleWarnMinutes = cfg.Bind("Idle", "IdleWarnMinutes", 2,
                "Minutes of warning before the kick, said once in the player's chat. 0 "
                + "kicks without warning.");

            RefuseMods = cfg.Bind("Mods", "RefuseMods", Mods.Refuse,
                "Judge what the joining client has loaded, by BepInEx plugin GUID."
                + "\n"
                + "Off - do not look. Notice - look, write what was found to the log, and let "
                + "everybody in anyway. Refuse - turn them away, which still needs Enforce on. "
                + "The old true and false still work and mean Refuse and Off."
                + "\n"
                + "Notice exists because turning Enforce off is not a way to trial this rule: "
                + "Enforce is one switch over every rule at the door, so trialling the mod list "
                + "that way also stops refusing cheats and altered records for as long as the "
                + "trial runs. Use Notice for a week, read the log, then set Refuse."
                + "\n"
                + "This is the check that actually reaches cheating on a dedicated server. "
                + "Console.IsCheatsEnabled returns ZNet.instance.IsServer(), so a client's own "
                + "devcommands does nothing on somebody else's server - which means anyone "
                + "cheating there is running a mod that patched around it. What the character "
                + "did in the past is a weaker question than what the client is running now."
                + "\n"
                + "Self-reported like everything else here, so a purpose-built client can lie. "
                + "What it catches is a cheat mod installed from Thunderstore by somebody who "
                + "did not think about it.");

            ModPolicy = cfg.Bind("Mods", "ModPolicy", Mods.Allow,
                "Allow: only the mods this server runs, plus AllowedMods, may come in."
                + "\n"
                + "Deny: anything may come in except what is named in DeniedMods."
                + "\n"
                + "Allow is the default because a list of things to permit is knowable in "
                + "advance and a list of every cheat mod that will ever exist is not. Deny "
                + "suits a server that does not mind what people run as long as it is not that."
                + "\n"
                + "Whichever is set, nothing is refused while Enforce is off - it is reported, "
                + "and 'dyrr' prints the standing answer for everyone connected. Turn Enforce "
                + "on after reading that, not before.");

            AllowedMods = cfg.Bind("Mods", "AllowedMods", "",
                "Extra plugin GUIDs a client may run, separated by commas. Case is ignored."
                + "\n"
                + "The plugins this server itself runs are always allowed and do not need "
                + "listing - otherwise adding a mod to the server would refuse everybody, "
                + "including you. This is for the client-only ones: a map mod, an equipment "
                + "bar, and whatever you personally develop with."
                + "\n"
                + "Only read when ModPolicy is Allow, and added to whatever is in "
                + "BepInEx/config/dyrr-mods.txt. Prefer that file: this entry only takes effect "
                + "when the server restarts, and letting one friend keep their map mod is not "
                + "worth dropping everybody who is online.");

            DeniedMods = cfg.Bind("Mods", "DeniedMods", "",
                "Plugin GUIDs no client may run, separated by commas. Case is ignored."
                + "\n"
                + "Shipped empty on purpose. A list of cheat mod GUIDs written by me would be "
                + "out of date the week after it shipped and would read as complete when it "
                + "was not. Build it from what actually turns up: every plugin a client brings "
                + "that this server does not run is written to the log when it connects."
                + "\n"
                + "Only read when ModPolicy is Deny.");

            RefuseUnreported = cfg.Bind("Door", "RefuseUnreported", true,
                "Refuse a connection that answers nothing, or whose profile could not be " +
                "read. A door that opens when the question goes unanswered is not a door - " +
                "but Core's version gate should already have turned away a client without " +
                "this plugin, so in practice this is a backstop.");

            RefusedMessage = cfg.Bind("Door", "RefusedMessage",
                "This server refused this connection.",
                "Sent to the refused client so it lands in their own log, and on their " +
                "screen when they have Core. Valheim's refusal screen carries no text of its " +
                "own, and a player on somebody else's server can never read that server's " +
                "log - so without this, being turned away is indistinguishable from a crash.\n" +
                "The specific reason is appended to whatever this says, as a sentence starting " +
                "\"It\". So keep this one general. It used to read \"only accepts characters " +
                "that have never played anywhere else\", which was true when travel was the " +
                "only thing that could refuse anybody and became a lie the moment it was not: " +
                "a client turned away for running a mod was told, on screen, that its " +
                "character had played somewhere else. A fixed sentence must not claim which " +
                "of six checks fired.");
        }
    }
}
