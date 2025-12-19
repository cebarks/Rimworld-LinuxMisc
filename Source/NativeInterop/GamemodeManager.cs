using System;
using Verse;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal static class GamemodeManager
    {
        private static bool isActive = false;
        private static bool cleanupRegistered = false;

        public static bool EnableGamemode()
        {
            try
            {
                if (!LinuxSyscalls.IsLinux())
                {
                    Log.Message("[LinuxMisc] Not running on Linux, skipping gamemode activation");
                    return false;
                }

                var settings = LinuxMiscMod.GetSettings();
                if (!settings.enableGamemode)
                {
                    Log.Message("[LinuxMisc] Gamemode disabled in settings, skipping activation");
                    return false;
                }

                if (!GamemodeInterop.IsLibraryAvailable())
                {
                    Log.Message("[LinuxMisc] libgamemode not found, skipping gamemode activation");
                    return false;
                }

                if (isActive)
                {
                    Log.Message("[LinuxMisc] Gamemode already active, skipping duplicate activation");
                    return true;
                }

                if (GamemodeInterop.TryRequestStart(out string error))
                {
                    isActive = true;
                    RegisterCleanupHandler();
                    Log.Message("[LinuxMisc] Successfully activated gamemode");
                    return true;
                }

                Log.Warning($"[LinuxMisc] Failed to activate gamemode: {error}");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[LinuxMisc] Exception while enabling gamemode: {ex}");
                return false;
            }
        }

        private static void DisableGamemode()
        {
            try
            {
                if (!isActive)
                {
                    return;
                }

                if (GamemodeInterop.TryRequestEnd(out string error))
                {
                    Log.Message("[LinuxMisc] Successfully deactivated gamemode on shutdown");
                }
                else
                {
                    Log.Warning($"[LinuxMisc] Failed to deactivate gamemode: {error}");
                }

                isActive = false;
            }
            catch (Exception ex)
            {
                Log.Warning($"[LinuxMisc] Exception while disabling gamemode: {ex.Message}");
            }
        }

        private static void RegisterCleanupHandler()
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
                Log.Warning($"[LinuxMisc] Failed to register gamemode cleanup handler: {ex.Message}");
            }
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            DisableGamemode();
        }
    }
}
