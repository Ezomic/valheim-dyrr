using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Ezomic.Core;
using HarmonyLib;

namespace Threshold
{
    /// <summary>
    /// Threshold. A door policy: characters that have played elsewhere do not come in.
    ///
    /// This exists because it was living inside Boon, where it did not belong. Boon awards
    /// levels for skill gains, so it wanted to know whether a character's skills were earned
    /// here - and answered that by refusing the connection outright. A levelling mod deciding
    /// who may play is the wrong power in the wrong place: the failure mode is that a bug in
    /// an XP system locks people out of a server, and when it fired the player got Valheim's
    /// generic kick screen with the reason only in a log they had no access to.
    ///
    /// Split along what each half is actually for. Boon keeps the question it has standing to
    /// ask - "do I pay for these levels?" - and answers it by withholding XP, never by
    /// disconnecting anyone. The question of who is allowed through the door is a server
    /// policy, has nothing to do with levelling, and is this.
    ///
    /// There is no BepInProcess attribute. A dedicated server runs valheim_server.exe, and
    /// refusing a connection only happens on the server side of RPC_PeerInfo, so this has to
    /// load there to do anything at all.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    // Soft, not hard. Threshold installs and runs on its own - a door policy is useful to
    // somebody running a server who wants none of the rest of this suite - and a hard
    // dependency that is absent does not degrade, the plugin simply never loads. Soft still
    // buys the load-order guarantee when Core is present, which is what registering needs.
    [BepInDependency(CoreGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class ThresholdPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.threshold";
        public const string PluginName = "Threshold";
        public const string PluginVersion = "1.1.0";
        public const string PluginAuthor = "Robbin Thijssen";

        /// <summary>Core's plugin GUID. Optional - see TryRegisterWithCore.</summary>
        internal const string CoreGuid = "ezomic.valheim.core";

        /// <summary>Whether Core answered at load. Read by Doorman's refusal path.</summary>
        internal static bool CorePresent;

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            ThresholdConfig.Bind(Config);

            TryRegisterWithCore();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Doorman));
            _harmony.PatchAll(typeof(MenuGuard));


            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " - ready.");
        }

        /// <summary>
        /// Remember which world this character belongs to, the first time it is seen in one.
        /// After that the menu refuses to start it anywhere else, which is the only point at
        /// which that can still be prevented.
        ///
        /// Polled rather than hooked because there is no single moment when both ZNet and the
        /// player profile exist and the world UID is known - the same shape the prefab
        /// registration recipes use.
        ///
        /// The guard is the character and world last handled, not a bare "already done" flag.
        /// It was a flag, reset by a ZNet.Shutdown postfix, and that reset did not fire on
        /// every way of leaving a world - so only the FIRST world of a game session ever
        /// bound. Seen exactly that way: a character joined the dev server and bound, then a
        /// second character joined the live server in the same session and was never recorded.
        ///
        /// The consequence is worse than a missing log line. An unbound character has no home,
        /// and MenuGuard only refuses when it knows a home and sees a different world - so the
        /// preventive half silently stopped protecting anyone who joined a second world without
        /// restarting the game, which is the ordinary case. The two halves of this mod are
        /// supposed to cover each other and that left a hole in the one that acts first.
        ///
        /// Comparing the pair costs two long compares a frame and cannot go stale, because it
        /// is derived from the thing it is guarding rather than from an event that has to be
        /// remembered to fire. Home.Bind ignores a repeat of the same pairing anyway, so this
        /// is only keeping the common case off the dictionary.
        /// </summary>
        private void Update()
        {
            if (!ThresholdConfig.ProtectCharacter.Value) return;
            if (ZNet.instance == null || Game.instance == null) return;

            // Only bind where there is a character actually playing. A dedicated server has a
            // player profile object but nobody behind it, and without this it bound a phantom
            // "Stranger" to its own world on every startup - one junk line per restart, in a
            // file that exists to protect characters the server does not have and guards a
            // menu it never shows.
            if (Player.m_localPlayer == null) return;

            var uid = ZNet.instance.GetWorldUID();
            if (uid == 0L) return;

            var profile = Game.instance.GetPlayerProfile();
            if (profile == null) return;

            var id = Home.IdOf(profile);
            if (id == 0L) return;
            if (id == _lastId && uid == _lastWorld) return;

            _lastId = id;
            _lastWorld = uid;
            Home.Bind(id, profile.GetName(), uid);
        }

        private static long _lastId;
        private static long _lastWorld;

        /// <summary>
        /// Joins Core's version gate when Core is installed, and does nothing when it is not.
        ///
        /// Threshold is the mod most likely to be wanted on its own - a door policy is useful
        /// to somebody running a server who wants none of the rest of this suite - and a hard
        /// dependency that is absent does not degrade, the plugin never loads at all.
        ///
        /// Nothing about the policy needs Core. Doorman carries its own handshake and does its
        /// own refusing on the server side of RPC_PeerInfo, so the door works standalone. Two
        /// things are given up. The version gate, so a client running a different build of
        /// Threshold is no longer reported - which matters more here than elsewhere, because
        /// the facts being judged are reported *by the client*, and an old build answering an
        /// unfamiliar question is exactly the case the gate would have caught. And the refusal
        /// screen: see OnRefused in Doorman, which falls back to the log alone.
        /// </summary>
        private void TryRegisterWithCore()
        {
            CorePresent = Chainloader.PluginInfos.ContainsKey(CoreGuid);

            if (!CorePresent)
            {
                Log.LogInfo("Core not installed - running standalone, without the version gate. "
                    + "Refused players will be told why in their log, not on the screen.");
                return;
            }

            RegisterWithCore();
        }

        /// <summary>
        /// Kept separate and never inlined on purpose. The JIT resolves the assemblies a method
        /// needs when it first compiles that method, so a Suite call sitting directly in Awake
        /// would drag Ezomic.Core in before the check above could prevent it - and the
        /// missing-assembly exception would land during plugin load, which is the failure this
        /// whole arrangement exists to avoid. Isolating it means the type is only ever resolved
        /// on a machine that has Core.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RegisterWithCore()
        {
            // Everyone, not HostOnly, even though only the server decides. The facts being
            // judged live on the client and have to be reported, so a client without this
            // plugin answers nothing - and a door that cannot ask its question is not a door.
            Suite.Register(PluginGuid, PluginName, PluginVersion, Config, Requirement.Everyone);

            // The host's rules are the rules. A client deciding locally that it does not
            // refuse itself would be meaningless anyway, but syncing keeps the log on both
            // ends agreeing about what was applied.
            Suite.Sync(
                ThresholdConfig.Enabled,
                ThresholdConfig.Enforce,
                ThresholdConfig.RefuseOtherWorlds,
                ThresholdConfig.RefuseCheats,
                ThresholdConfig.RefuseUnreported);
        }

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }
    }
}
