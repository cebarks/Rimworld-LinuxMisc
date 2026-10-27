using System;
using Verse;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal sealed class GamemodeSession
    {
        private readonly object sync = new object();
        private readonly IGamemodeClient client;
        private bool isActive;

        internal GamemodeSession(IGamemodeClient client)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
        }

        internal bool IsActive
        {
            get
            {
                lock (sync)
                {
                    return isActive;
                }
            }
        }

        internal bool TryActivate(out string error)
        {
            lock (sync)
            {
                if (isActive)
                {
                    error = null;
                    return true;
                }

                if (!client.TryRequestStart(out error))
                {
                    return false;
                }

                isActive = true;
                return true;
            }
        }

        internal bool TryDeactivate(out string error)
        {
            lock (sync)
            {
                if (!isActive)
                {
                    error = null;
                    return true;
                }

                if (!client.TryRequestEnd(out error))
                {
                    return false;
                }

                isActive = false;
                return true;
            }
        }
    }

    internal static class GamemodeManager
    {
        private static readonly object CleanupSync = new object();
        private static readonly GamemodeSession Session = new GamemodeSession(new NativeGamemodeClient());
        private static bool cleanupRegistered;

        public static bool EnableGamemode()
        {
            try
            {
                if (!GamemodeInterop.IsSupportedPlatform(out string unsupportedReason))
                {
                    Log.Message($"[LinuxMisc] {unsupportedReason}; skipping GameMode activation");
                    return false;
                }

                LinuxMiscSettings settings = LinuxMiscMod.GetSettings();
                if (!settings.enableGamemode)
                {
                    Log.Message("[LinuxMisc] GameMode disabled in settings; skipping activation");
                    return false;
                }

                bool wasActive = Session.IsActive;
                if (!Session.TryActivate(out string error))
                {
                    Log.Warning($"[LinuxMisc] GameMode activation was not accepted: {error}");
                    return false;
                }

                RegisterCleanupHandler();
                if (wasActive)
                {
                    Log.Message("[LinuxMisc] GameMode already active; duplicate activation skipped");
                }
                else
                {
                    Log.Message("[LinuxMisc] GameMode request activated for this client");
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[LinuxMisc] Exception while enabling GameMode: {ex}");
                return false;
            }
        }

        private static void DisableGamemode()
        {
            try
            {
                if (!Session.IsActive)
                {
                    return;
                }

                if (Session.TryDeactivate(out string error))
                {
                    Log.Message("[LinuxMisc] GameMode request ended on shutdown");
                }
                else
                {
                    Log.Warning($"[LinuxMisc] Failed to end GameMode request: {error}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[LinuxMisc] Exception while ending GameMode request: {ex.Message}");
            }
        }

        private static void RegisterCleanupHandler()
        {
            lock (CleanupSync)
            {
                if (cleanupRegistered)
                {
                    return;
                }

                try
                {
                    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                    cleanupRegistered = true;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[LinuxMisc] Failed to register GameMode cleanup handler: {ex.Message}");
                }
            }
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            DisableGamemode();
        }
    }
}
