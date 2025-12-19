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

            if (thpEnabled)
            {
                Log.Message("[LinuxMisc] Initialization complete - THP enabled successfully");
            }
            else
            {
                Log.Message("[LinuxMisc] Initialization complete - THP not enabled (see warnings above)");
            }
        }
    }
}
