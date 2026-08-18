using System.Reflection;
using HarmonyLib;

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
    /// **The label is vanilla's own.** m_csSourceInfo already carries this kind of notice - the
    /// legacy-save warning and the cloud-saves-disabled line both land there - so the styling,
    /// position and wrapping are the game's problem rather than something to guess at. It is
    /// reassigned from scratch on every UpdateCharacterList, inside the branch that has a
    /// profile, which is the same branch this appends in. So the append cannot accumulate: our
    /// line is overwritten by vanilla's assignment before we add it again.
    ///
    /// Reached by reflection because the field is a TMP_Text, and taking a reference on
    /// Unity.TextMeshPro to assign one string is a build-time cost paid forever for a one-line
    /// win. Core's ConnectError makes the same trade for the same reason.
    /// </summary>
    internal static class CharacterNote
    {
        private static FieldInfo _sourceInfo;
        private static PropertyInfo _text;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FejdStartup), "UpdateCharacterList")]
        private static void Annotate(FejdStartup __instance)
        {
            // Off with the protection it describes. A binding that is not being enforced is
            // not a fact about the character, it is a leftover in a file, and saying it on the
            // menu would imply a rule that is not running.
            if (!DyrrConfig.ProtectCharacter.Value) return;

            var profile = MenuGuard.SelectedProfile(__instance);
            if (profile == null) return;

            var id = Home.IdOf(profile);
            if (id == 0L) return;

            var home = Home.GetBinding(id);

            var note = home.World == 0L
                ? "Not bound to a world yet. The first one it plays in becomes its home."
                : "Belongs to world " + Home.Describe(home) + ".";

            Append(__instance, note);
        }

        private static void Append(FejdStartup fejd, string note)
        {
            if (_sourceInfo == null)
                _sourceInfo = AccessTools.Field(typeof(FejdStartup), "m_csSourceInfo");

            var label = _sourceInfo != null ? _sourceInfo.GetValue(fejd) : null;
            if (label == null) return;

            if (_text == null) _text = AccessTools.Property(label.GetType(), "text");
            if (_text == null) return;

            var existing = _text.GetValue(label, null) as string;

            // Vanilla's own notices come first and keep their spacing. When there are none -
            // the ordinary case, since both of them are warnings - this is the whole label
            // rather than a blank line followed by one sentence.
            _text.SetValue(label,
                string.IsNullOrEmpty(existing) ? note : existing + "\n\n" + note, null);
        }
    }
}
