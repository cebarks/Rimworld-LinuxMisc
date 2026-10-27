using RimWorldLinuxMisc.NativeInterop;
using Verse;

namespace RimWorldLinuxMisc
{
    [StaticConstructorOnStartup]
    public static class ModInitializer
    {
        static ModInitializer()
        {
            Log.Message("[LinuxMisc] Linux optimization controls initializing (settings changes require restart)...");

            OptimizationStartupResult result = OptimizationStartup.Initialize(
                GamemodeManager.EnableGamemode,
                CPUOptimizer.EnableCPUAffinity,
                MemoryOptimizer.EnableTHP);

            var enabled = new System.Collections.Generic.List<string>();
            if (result.GamemodeEnabled) enabled.Add("GameMode active");
            if (result.CpuAffinityEnabled) enabled.Add("best-effort CPU affinity active");
            if (result.MemoryEnabled) enabled.Add("memory advice active or scheduled");

            if (enabled.Count == 0)
            {
                Log.Message("[LinuxMisc] Initialization complete - no optional optimization was activated");
            }
            else
            {
                Log.Message($"[LinuxMisc] Initialization complete - {string.Join(", ", enabled)}");
            }
        }
    }
}
