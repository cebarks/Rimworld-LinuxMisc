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

            bool cpuAffinityEnabled = CPUOptimizer.EnableCPUAffinity();
            bool memoryOptsEnabled = MemoryOptimizer.EnableTHP();
            bool gamemodeEnabled = GamemodeManager.EnableGamemode();

            int enabledCount = (cpuAffinityEnabled ? 1 : 0) + (memoryOptsEnabled ? 1 : 0) + (gamemodeEnabled ? 1 : 0);

            if (enabledCount == 3)
            {
                Log.Message("[LinuxMisc] Initialization complete - All optimizations enabled (CPU affinity, memory optimizations, gamemode)");
            }
            else if (enabledCount > 0)
            {
                var enabled = new System.Collections.Generic.List<string>();
                if (cpuAffinityEnabled) enabled.Add("CPU affinity");
                if (memoryOptsEnabled) enabled.Add("memory optimizations");
                if (gamemodeEnabled) enabled.Add("gamemode");
                Log.Message($"[LinuxMisc] Initialization complete - {string.Join(", ", enabled)} enabled");
            }
            else
            {
                Log.Message("[LinuxMisc] Initialization complete - no optimizations enabled");
            }
        }
    }
}
