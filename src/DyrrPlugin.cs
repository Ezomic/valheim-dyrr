using System;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Ezomic.Core;
using HarmonyLib;

namespace Dyrr
{
    /// <summary>
    /// Dyrr. A door policy: characters that have played elsewhere do not come in.
    ///
    /// This exists because it was living inside Rist, where it did not belong. Rist awards
    /// levels for skill gains, so it wanted to know whether a character's skills were earned
    /// here - and answered that by refusing the connection outright. A levelling mod deciding
    /// who may play is the wrong power in the wrong place: the failure mode is that a bug in
    /// an XP system locks people out of a server, and when it fired the player got Valheim's
    /// generic kick screen with the reason only in a log they had no access to.
    ///
    /// Split along what each half is actually for. Rist keeps the question it has standing to
    /// ask - "do I pay for these levels?" - and answers it by withholding XP, never by
    /// disconnecting anyone. The question of who is allowed through the door is a server
    /// policy, has nothing to do with levelling, and is this.
    ///
    /// There is no BepInProcess attribute. A dedicated server runs valheim_server.exe, and
    /// refusing a connection only happens on the server side of RPC_PeerInfo, so this has to
    /// load there to do anything at all.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    // Soft, not hard. Dyrr installs and runs on its own - a door policy is useful to
    // somebody running a server who wants none of the rest of this suite - and a hard
    // dependency that is absent does not degrade, the plugin simply never loads. Soft still
    // buys the load-order guarantee when Core is present, which is what registering needs.
    [BepInDependency(CoreGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class DyrrPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.dyrr";
        public const string PluginName = "Dyrr";
        public const string PluginVersion = "1.4.1";
        public const string PluginAuthor = "Robbin Thijssen";

        /// <summary>Core's plugin GUID. Optional - see TryRegisterWithCore.</summary>
        internal const string CoreGuid = "ezomic.valheim.core";

        /// <summary>
        /// GUIDs this plugin has been published under, newest first. BepInEx names the config
        /// file after the GUID, so a rename hands everybody a file of defaults and silently
        /// discards what they had set - including Enforce, which is the one setting here that
        /// decides whether anyone can connect at all.
        /// </summary>
        private static readonly string[] LegacyGuids = { "ezomic.valheim.threshold" };

        /// <summary>Whether Core answered at load. Read by Doorman's refusal path.</summary>
        internal static bool CorePresent;

        internal static ManualLogSource Log;

        private Harmony _harmony;

        /// <summary>
        /// Whether Awake ran all the way to its last statement.
        ///
        /// Unity keeps calling Update on a component whose Awake threw. Nothing in Awake was
        /// wrapped, so a config bind that failed, a Core registration that failed, or one
        /// patch that would not apply left this plugin with an unpatched door and a live
        /// Update - and Update runs the idle kicker. A server in that state throws players out
        /// for standing still while judging nobody at the door, which is every cost of this mod
        /// and none of its point, and the only sign of it is one exception in a log.
        ///
        /// Set as the LAST statement of Awake so it cannot be true for a half-built plugin, and
        /// read as the first line of Update.
        /// </summary>
        private static bool _awakeCompleted;

        /// <summary>
        /// Whether the door itself patched. The idle kick waits on this rather than on
        /// _awakeCompleted alone, because Doorman can fail on its own while the rest of Awake
        /// finishes - and kicking for idleness on a server that admits everybody unjudged is
        /// the same failure as above, arrived at a different way.
        /// </summary>
        private static bool _doorPatched;

        private void Awake()
        {
            Log = Logger;

            // Before Bind, and that ordering is the whole trick. BaseUnityPlugin builds Config
            // with saveOnInit false, so nothing has been written to the new path yet - dropping
            // the old file there and reloading lets Bind find every saved value as an orphaned
            // entry, exactly as if it had always been called that.
            AdoptOldConfig();
            DyrrConfig.Bind(Config);

            // Caught for the same reason the dependency is soft: the door is supposed to work
            // without Core, so an exception coming out of Core's API - a signature that moved,
            // a version of Core that answers differently - must cost the version gate and the
            // refusal screen, and not the door. Unwrapped it aborted Awake before a single
            // patch was applied, which is the loudest possible way to give up the thing the
            // whole plugin is for over an optional extra.
            try
            {
                TryRegisterWithCore();
            }
            catch (Exception e)
            {
                CorePresent = false;
                Log.LogError("Dyrr could not register with Core, so it runs standalone this "
                    + "session: no version gate, and refused players see the reason only in "
                    + "their own log. The door itself is unaffected. " + e);
            }

            _harmony = new Harmony(PluginGuid);

            // One try/catch per class, not one around the five. PatchAll throws on the first
            // method it cannot patch, so a single vanilla signature that moved in a game update
            // used to abort Awake there and leave every class after it unpatched - and the
            // order below is arbitrary, so which half of the mod survived a game update was
            // decided by nothing. Caught per class, a signature change costs exactly the
            // feature it belongs to and says which one that was.
            _doorPatched = Patch(typeof(Doorman));
            Patch(typeof(MenuGuard));
            Patch(typeof(CharacterNote));
            Patch(typeof(Warden));
            Patch(typeof(Commands));

            // Said plainly and at error level, because this is the state where the mod is
            // installed, logs a cheerful ready line and judges nobody. An admin reading the log
            // has to be able to tell that apart from a quiet day at the door.
            if (!_doorPatched)
                Log.LogError("Dyrr's door is NOT applied - the Doorman patches did not take, so "
                    + "no connection will be judged and nobody will be kicked for idling "
                    + "either. The line above names the method that would not patch.");

            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " - ready.");

            // The last statement in Awake, and it has to stay the last one. Update refuses to
            // do anything until this is true; see the field.
            _awakeCompleted = true;
        }

        /// <summary>
        /// Patch one class, and treat a failure as the loss of that class rather than of the
        /// plugin. Returns whether it took, which only the door is asked about.
        /// </summary>
        private bool Patch(Type type)
        {
            try
            {
                _harmony.PatchAll(type);
                return true;
            }
            catch (Exception e)
            {
                // The whole exception, not e.Message. A Harmony patch failure names the target
                // method inside the inner exception, and that name is the entire diagnosis when
                // a game update moved something - it is the difference between "Dyrr is broken"
                // and "ZNet.RPC_PeerInfo changed shape".
                Log.LogError("Dyrr could not patch " + type.Name + ", so that part of the mod "
                    + "is not running at all. " + e);
                return false;
            }
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
            // Nothing at all until Awake finished. Unity calls Update on a component whose
            // Awake threw, and everything below assumes an Awake that ran - the config entries
            // it reads are bound there, and the door it belongs to is patched there.
            if (!_awakeCompleted) return;

            // Before the character-protection early-outs: the idle watch is a server
            // duty and a dedicated server never has a local player.
            //
            // Gated on the door having patched, because the idle kick is the door's other half
            // and must never be the only half. A server whose Doorman patches failed admits
            // every character unjudged; kicking people off it for standing still is all of this
            // mod's teeth and none of its policy.
            if (_doorPatched) Idle.Tick();

            Inventories.Tick();

            if (!DyrrConfig.ProtectCharacter.Value) return;
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
            Home.Bind(id, profile.GetName(), uid, ZNet.instance.GetWorldName());
        }

        private static long _lastId;
        private static long _lastWorld;

        /// <summary>
        /// Copy the settings over from the config file this mod had before it was renamed.
        ///
        /// Only ever when the current file does not exist, and never a move - the old file is
        /// left where it is. A copy is recoverable by hand and a move is not, and the cost of
        /// leaving it is one stale file nothing reads.
        ///
        /// Doing nothing was not an option worth taking. The defaults here are not neutral:
        /// Enforce defaults to false, so a silent reset does not merely lose preferences, it
        /// turns the door off on a server that had it on, and nothing would say so.
        /// </summary>
        private void AdoptOldConfig()
        {
            var current = Path.Combine(Paths.ConfigPath, PluginGuid + ".cfg");
            if (File.Exists(current)) return;

            foreach (var guid in LegacyGuids)
            {
                var old = Path.Combine(Paths.ConfigPath, guid + ".cfg");
                if (!File.Exists(old)) continue;

                try
                {
                    File.Copy(old, current);
                    Config.Reload();
                    Log.LogInfo("Adopted settings from " + old + ". Edit " +
                        Path.GetFileName(current) + " from now on; the old file is left alone " +
                        "and is no longer read.");
                }
                catch (Exception e)
                {
                    Log.LogWarning("Could not adopt " + old + " (" + e.Message +
                        "), so this run uses defaults - including Enforce off. Copy it to " +
                        Path.GetFileName(current) + " by hand to keep your settings.");
                }

                return;
            }
        }

        /// <summary>
        /// Joins Core's version gate when Core is installed, and does nothing when it is not.
        ///
        /// Dyrr is the mod most likely to be wanted on its own - a door policy is useful
        /// to somebody running a server who wants none of the rest of this suite - and a hard
        /// dependency that is absent does not degrade, the plugin never loads at all.
        ///
        /// Nothing about the policy needs Core. Doorman carries its own handshake and does its
        /// own refusing on the server side of RPC_PeerInfo, so the door works standalone. Two
        /// things are given up. The version gate, so a client running a different build of
        /// Dyrr is no longer reported - which matters more here than elsewhere, because
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
        /// Put a reason on the refusal screen, when there is a Core installed to draw it.
        ///
        /// Both halves of the door need this - the server-side one to explain a refusal that
        /// came over the wire, the client-side one to explain a join it stopped itself - and
        /// the Core-present check has to sit here, outside the call, rather than in each of
        /// them. See RegisterWithCore for why the call itself is isolated.
        /// </summary>
        internal static void Explain(string why)
        {
            if (CorePresent) ExplainOnScreen(why);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ExplainOnScreen(string why)
        {
            Suite.ExplainRefusal(why);
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
                DyrrConfig.Enabled,
                DyrrConfig.Enforce,
                DyrrConfig.RefuseOtherWorlds,
                DyrrConfig.RefuseCheats,
                DyrrConfig.RefuseUnreported);
        }

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }
    }
}
