using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Dyrr
{
    /// <summary>
    /// Says which world a character belongs to, on the screen where you pick the character.
    ///
    /// This is the same fact the console command reports, moved to where the decision is
    /// actually made. The console was the cheapest surface to write and it is the wrong one:
    /// it is off until somebody enables it, it is a developer's tool, and reading a report
    /// requires knowing to go and ask for it. The information matters at exactly one moment -
    /// while choosing which character to take somewhere - and that moment has a screen.
    ///
    /// It is also the difference between this mod's two halves in miniature. MenuGuard refuses
    /// at the commit point, which works but is a dead end by design; a dead end is a much
    /// better thing to have arrived at knowingly. Nothing here prevents anything, and that is
    /// the point: it is the sentence that stops the refusal being a surprise.
    ///
    /// **The first attempt wrote into m_csSourceInfo and it landed in the wrong place.** That
    /// field sounds like the line under the character's name and is not - it is the notice
    /// column against the right edge of the screen, where the legacy-save and
    /// cloud-saves-disabled warnings go, half a screen away from the character it was
    /// describing and wrapping mid-sentence. The line under the name is m_csFileSource, the one
    /// reading "Cloud save".
    ///
    /// So this clones that label rather than appending to it. Appending would have been fewer
    /// lines, but m_csFileSource carries the storage icons as children, laid out beside a
    /// two-word string; growing it into a sentence moves them. A clone inherits the font, size,
    /// colour and alignment - which is the whole reason for cloning vanilla UI - and owns its
    /// own width, so nothing of the game's moves when this appears.
    ///
    /// Text via reflection because the component is a TMP_Text, and taking a reference on
    /// Unity.TextMeshPro to assign one string is a build-time cost paid forever for a one-line
    /// win. Core's ConnectError makes the same trade for the same reason. Everything else is
    /// reached as a plain Component, which needs nothing beyond UnityEngine.CoreModule.
    /// </summary>
    internal static class CharacterNote
    {
        private static FieldInfo _fileSource, _name;
        private static PropertyInfo _text;

        private static GameObject _line;
        private static Component _lineText;

        /// <summary>
        /// Breathing room between this line and whatever it sits against, as a fraction of its
        /// own height. Measured off the labels rather than set in pixels so it follows a font
        /// size change or a UI scale.
        /// </summary>
        private const float Margin = 0.35f;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FejdStartup), "UpdateCharacterList")]
        private static void Annotate(FejdStartup __instance)
        {
            // Off with the protection it describes. A binding that is not being enforced is
            // not a fact about the character, it is a leftover in a file, and saying it on the
            // menu would imply a rule that is not running.
            if (!DyrrConfig.ProtectCharacter.Value) return;

            var label = Line(__instance);
            if (label == null) return;

            var profile = MenuGuard.SelectedProfile(__instance);
            var id = profile != null ? Home.IdOf(profile) : 0L;

            if (id == 0L)
            {
                // No character selected, or one whose id could not be read. Hide rather than
                // leave the last character's world sitting under somebody else's name.
                _line.SetActive(false);
                return;
            }

            var note = Note(profile, id);

            _text.SetValue(label, note, null);
            _line.SetActive(true);
        }

        /// <summary>
        /// What to say about this character.
        ///
        /// The binding file answers first, because that is the thing MenuGuard enforces. When
        /// it has nothing, **the character does**. PlayerProfile.m_worldData is one entry per
        /// world this character has spawned in, and a character with exactly one of them has a
        /// home whether or not this mod has ever watched it play - which was the hole here. A
        /// binding is only written when a character spawns while Dyrr is running, so every
        /// character that last played before the mod arrived reported "not bound to a world
        /// yet" while its own save file said otherwise.
        ///
        /// That single world is bound on the spot rather than only displayed. It is not a
        /// guess: the character has been in exactly one world and nothing else can be its home.
        /// Leaving it unbound would mean MenuGuard protecting nothing until the character
        /// happened to play again, which is the same shape of hole in a different place.
        ///
        /// More than one world is left unbound deliberately. There is no single home to defend,
        /// the harm has already happened, and inventing one would refuse the character
        /// everywhere except a world picked arbitrarily out of its history. Saying how many is
        /// the useful thing: those are exactly the characters an enforcing door turns away.
        /// </summary>
        private static string Note(PlayerProfile profile, long id)
        {
            var home = Home.GetBinding(id);

            if (home.World == 0L)
            {
                var worlds = Facts.WorldsOf(profile);

                if (worlds.Count > 1) return "Has played in " + Listed(Names(profile, worlds));
                if (worlds.Count == 0) return "Not bound to a world yet";

                Home.Bind(id, profile.GetName(), worlds[0], WorldName(worlds[0], profile));
                home = Home.GetBinding(id);

                if (home.World == 0L) return "Not bound to a world yet";
            }

            // The uid is in the log, the popup and 'dyrr home' - three places it is useful and
            // this is not one of them. A menu line is read in passing and a 19-digit number in
            // it is noise standing exactly where the answer should be.
            // A bare uid was the first version of this line and it is worthless on a menu:
            // nineteen digits nobody can match to anything, standing where the answer goes. A
            // binding with no name is a world this machine has never had on disk and cannot
            // name, and saying that is the useful thing - it is almost always a server.
            if (!string.IsNullOrEmpty(home.WorldName)) return "Belongs to " + home.WorldName;

            // One last try before giving up on a name. A binding written before names were
            // recorded, or by a build that could not resolve one, can often be named now.
            var late = WorldName(home.World, profile);
            if (!string.IsNullOrEmpty(late))
            {
                Home.Name(id, late);
                return "Belongs to " + late;
            }

            return "Belongs to a world that is not on this PC";
        }

        /// <summary>
        /// The names of every world a character has been in, best effort.
        ///
        /// A count was the first version of this and it answers the wrong question. "Has played
        /// in 3 worlds" tells somebody they have a problem without telling them what it is;
        /// the names tell them whether the third one was a friend's server they visited once,
        /// which is the thing they actually want to remember.
        ///
        /// Two sources, because neither is complete on its own. m_worldData holds uids, which
        /// only name a world that is on this disk. m_knownWorlds holds names with no uids, so
        /// it cannot be matched up entry by entry - but it covers the worlds this machine has
        /// never had, which is exactly where the first source runs out. Taking the local
        /// matches first and topping up from the names keeps the list as long as the truth and
        /// never invents an entry.
        /// </summary>
        private static List<string> Names(PlayerProfile profile, List<long> worlds)
        {
            var names = new List<string>();

            try
            {
                var local = SaveSystem.GetWorldList();

                if (local != null)
                    foreach (var uid in worlds)
                        foreach (var world in local)
                            if (world != null && world.m_uid == uid && !names.Contains(world.m_name))
                            {
                                names.Add(world.m_name);
                                break;
                            }

                if (names.Count < worlds.Count && profile != null && profile.m_knownWorlds != null)
                    foreach (var known in profile.m_knownWorlds)
                    {
                        if (names.Count >= worlds.Count) break;
                        if (!names.Contains(known.Key)) names.Add(known.Key);
                    }
            }
            catch (System.Exception e)
            {
                DyrrPlugin.Log.LogWarning("Could not name a character's worlds: " + e.Message);
            }

            // Whatever could not be named still has to be counted, or a character that has been
            // somewhere unnameable reads as having been to fewer places than it has - which is
            // the one thing this line must never understate.
            for (var i = names.Count; i < worlds.Count; i++) names.Add("");

            return names;
        }

        /// <summary>
        /// "A and B", or "A, B and 2 more" past three. Anything longer than that stops being a
        /// sentence and starts being a wall on a line that is read in passing.
        /// </summary>
        private static string Listed(List<string> names)
        {
            var known = new List<string>();
            var nameless = 0;

            foreach (var name in names)
                if (string.IsNullOrEmpty(name)) nameless++;
                else known.Add(name);

            if (known.Count == 0) return names.Count + " worlds, none of them on this PC";

            var shown = System.Math.Min(known.Count, 3);
            var text = "";

            for (var i = 0; i < shown; i++)
            {
                if (i > 0) text += i == shown - 1 && known.Count == shown && nameless == 0
                    ? " and " : ", ";
                text += known[i];
            }

            var rest = known.Count - shown + nameless;
            if (rest > 0) text += " and " + rest + " more";

            return text;
        }

        /// <summary>
        /// The name of a local world, or empty for one that is not on this machine.
        ///
        /// A character bound to a server's world has a uid here and no name, and that is
        /// correct rather than a gap - the world is not on this disk to be named. Read fresh
        /// rather than cached because worlds are created and deleted from the menu this runs
        /// in, and a stale list would name a world that is gone.
        /// </summary>
        private static string WorldName(long uid, PlayerProfile profile)
        {
            try
            {
                var worlds = SaveSystem.GetWorldList();

                if (worlds != null)
                    foreach (var world in worlds)
                        if (world != null && world.m_uid == uid) return world.m_name;
            }
            catch (System.Exception e)
            {
                DyrrPlugin.Log.LogWarning("Could not read the world list: " + e.Message);
            }

            // Not on this disk, so ask the character. m_knownWorlds is the game's own record of
            // where it has played, keyed by world *name* where m_worldData is keyed by uid -
            // the same pair the door cross-checks for tampering, used here for the opposite
            // purpose. When the character has been in one world and knows one name, they are
            // the same world, and this names a server's world that nothing local could.
            //
            // Only when both counts are one. Two names and one uid means the game wrote a name
            // for a world that left no spawn record, and picking one of them would be a guess
            // presented as a fact.
            try
            {
                if (profile != null && profile.m_knownWorlds != null && profile.m_knownWorlds.Count == 1
                    && Facts.WorldsOf(profile).Count == 1)
                    foreach (var known in profile.m_knownWorlds) return known.Key;
            }
            catch (System.Exception e)
            {
                DyrrPlugin.Log.LogWarning("Could not read a character's world names: " + e.Message);
            }

            return "";
        }

        /// <summary>
        /// Our label, built the first time it is needed.
        ///
        /// Rebuilt whenever it has gone: FejdStartup is destroyed and recreated every time the
        /// game returns to the menu, taking the clone with it, while these static fields
        /// survive the whole process. Compared with == null rather than ?. on purpose - Unity
        /// overloads == so a destroyed object compares equal to null, and the null-propagating
        /// operators bypass that overload entirely.
        /// </summary>
        private static Component Line(FejdStartup fejd)
        {
            if (_line != null && _lineText != null) return _lineText;

            if (_fileSource == null)
                _fileSource = AccessTools.Field(typeof(FejdStartup), "m_csFileSource");

            var donor = _fileSource != null ? _fileSource.GetValue(fejd) as Component : null;
            if (donor == null) return null;

            if (_text == null) _text = AccessTools.Property(donor.GetType(), "text");
            if (_text == null) return null;

            _line = Object.Instantiate(donor.gameObject, donor.transform.parent);
            _line.name = "DyrrHome";

            // The clone came with the storage icons - a cloud, a folder, a legacy marker -
            // which belong to the line that says where the save lives and mean nothing here.
            // Collected before destroying because removing children while walking them is a
            // way to visit half of them.
            var icons = new List<GameObject>();
            foreach (Transform child in _line.transform) icons.Add(child.gameObject);
            foreach (var icon in icons) Object.Destroy(icon);

            Place(fejd, donor);

            _lineText = _line.GetComponent(donor.GetType());
            if (_lineText == null)
            {
                Object.Destroy(_line);
                _line = null;
                return null;
            }

            return _lineText;
        }

        /// <summary>
        /// Put it above the character's name.
        ///
        /// It was below the storage line first, which is where it reads most naturally - name,
        /// then where the save lives, then where the character belongs - and there is no room
        /// there. The button bar starts immediately under it, so the line came out sitting on
        /// the panel's top edge. Above the name is empty, so that is where it goes.
        ///
        /// Positioned against m_csName rather than the donor because that is the thing it has
        /// to clear, and only when the two share a parent - anchoredPosition is relative to
        /// that parent, so comparing across two of them would be arithmetic on unrelated
        /// numbers. If they ever stop sharing one, the fallback puts it above the donor
        /// instead, which is wrong by a line rather than wrong by a screen.
        /// </summary>
        private static void Place(FejdStartup fejd, Component donor)
        {
            var mine = _line.transform as RectTransform;
            var theirs = donor.transform as RectTransform;
            if (mine == null || theirs == null) return;

            if (_name == null) _name = AccessTools.Field(typeof(FejdStartup), "m_csName");

            var label = _name != null ? _name.GetValue(fejd) as Component : null;
            var above = label != null ? label.transform as RectTransform : null;

            if (above == null || above.parent != theirs.parent) above = theirs;

            // Half of each box plus the margin - anchoredPosition is the pivot, and both of
            // these labels are centred, so clearing the other one means clearing half of it
            // and half of this.
            mine.anchoredPosition = new Vector2(
                above.anchoredPosition.x,
                above.anchoredPosition.y + above.rect.height * 0.5f
                    + mine.rect.height * (0.5f + Margin));
        }
    }
}
