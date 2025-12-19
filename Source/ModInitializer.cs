using RimWorldLinuxMisc.NativeInterop;
using Verse;

namespace RimWorldLinuxMisc
{
    [StaticConstructorOnStartup]
    public static class ModInitializer
    {
        static ModInitializer()
        {
            Log.Message("[LinuxMisc] Linux Performance Optimizations mod initializing...");

            bool thpEnabled = THPManager.EnableTHP();
            bool gamemodeEnabled = GamemodeManager.EnableGamemode();

            if (thpEnabled && gamemodeEnabled)
            {
                Log.Message("[LinuxMisc] Initialization complete - THP and gamemode enabled successfully");
            }
            else if (thpEnabled)
            {
                Log.Message("[LinuxMisc] Initialization complete - THP enabled, gamemode not available");
            }
            else if (gamemodeEnabled)
            {
                Log.Message("[LinuxMisc] Initialization complete - Gamemode enabled, THP not available");
            }
            else
            {
                Log.Message("[LinuxMisc] Initialization complete - neither THP nor gamemode enabled");
            }
        }
    }
}
