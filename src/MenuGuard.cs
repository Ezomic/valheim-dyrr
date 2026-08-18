using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Dyrr
{
    /// <summary>
    /// Refuses to start a world that would damage the selected character.
    ///
    /// This is the half of the door policy that matters most, because it is the only half that
    /// can still prevent anything. By the time the server refuses a character, the harm is
    /// already permanent: PlayerProfile.m_worldData gained an entry for the world it visited,
    /// nothing in the game ever removes that entry, and the door will refuse that character on
    /// its own server from then on. There is no undo short of a backup taken beforehand -
    /// which is precisely what nobody has when they need one.
    ///
    /// FejdStartup.OnWorldStart is the commit point for a local world and the last moment
    /// anything can be done.
    ///
    /// It refuses outright rather than asking. A confirm dialog was tried and rejected: the
    /// damage is irreversible, so a prompt is a button that lets you do the unfixable thing by
    /// clicking through it. A dead end forces a wrong answer to be diagnosed and fixed instead
    /// of waved past. The popup therefore has to carry everything needed to correct it - both
    /// ids and the file to edit.
    ///
    /// Only local worlds are covered, because a server's world identity is not known until
    /// after connecting. That path is not left unguarded though: it is the other half of this
    /// mod, which refuses at RPC_PeerInfo before the character ever spawns. The two halves
    /// cover each other, which is the reason they belong in one place - this used to live in
    /// Rist, where it warned about a lockout Rist no longer had anything to do with.
    /// </summary>
    internal static class MenuGuard
    {
        private static FieldInfo _world, _profiles, _profileIndex;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnWorldStart))]
        private static bool GuardWorldStart(FejdStartup __instance)
        {
            if (!DyrrConfig.ProtectCharacter.Value) return true;

            var world = SelectedWorld(__instance);
            var profile = SelectedProfile(__instance);
            if (world == null || profile == null) return true;

            var id = Home.IdOf(profile);
            var home = Home.Get(id);

            // Nothing recorded means nothing to protect yet - the character has not been
            // accepted anywhere, so any world is a legitimate first one.
            if (id == 0L || home == 0L || home == world.m_uid) return true;

            DyrrPlugin.Log.LogWarning("Character '" + profile.GetName() + "' (" + id +
                ") belongs to world " + home + " but is being started in world '" +
                world.m_name + "' (" + world.m_uid + "). Refused.");

            // Everything needed to diagnose or override this, since there is no way through it
            // from here: both ids, and the one file that decides.
            UnifiedPopup.Push(new WarningPopup(
                "Dyrr: wrong world for this character",
                "Character: " + profile.GetName() + "  (id " + id + ")\n" +
                "This world: " + world.m_name + "  (" + world.m_uid + ")\n" +
                "Belongs to world: " + home + "\n\n" +
                "Loading it here records this world permanently in the character and locks it " +
                "out of its own server. The only undo is restoring a backup, so this is refused " +
                "rather than confirmed.\n\n" +
                "If the binding is wrong, delete the line starting " + id + " from " +
                "BepInEx/config/dyrr-home.txt.",
                UnifiedPopup.Pop,
                localizeText: false));

            return false;
        }

        private static World SelectedWorld(FejdStartup fejd)
        {
            if (_world == null) _world = AccessTools.Field(typeof(FejdStartup), "m_world");
            return _world != null ? _world.GetValue(fejd) as World : null;
        }

        private static PlayerProfile SelectedProfile(FejdStartup fejd)
        {
            if (_profiles == null) _profiles = AccessTools.Field(typeof(FejdStartup), "m_profiles");
            if (_profileIndex == null) _profileIndex = AccessTools.Field(typeof(FejdStartup), "m_profileIndex");
            if (_profiles == null || _profileIndex == null) return null;

            if (!(_profiles.GetValue(fejd) is List<PlayerProfile> list)) return null;

            var index = (int)_profileIndex.GetValue(fejd);
            if (index < 0 || index >= list.Count) return null;

            return list[index];
        }
    }
}
