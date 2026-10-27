using System;

namespace RimWorldLinuxMisc
{
    internal readonly record struct OptimizationStartupResult(bool GamemodeEnabled, bool CpuAffinityEnabled, bool MemoryEnabled);

    internal static class OptimizationStartup
    {
        public static OptimizationStartupResult Initialize(
            Func<bool> enableGamemode,
            Func<bool, bool> enableCpuAffinity,
            Func<bool> enableMemory)
        {
            bool gamemode = TryInvoke(enableGamemode);
            bool affinity = TryInvoke(() => enableCpuAffinity(gamemode));
            bool memory = TryInvoke(enableMemory);
            return new OptimizationStartupResult(gamemode, affinity, memory);
        }

        private static bool TryInvoke(Func<bool> action)
        {
            try
            {
                return action();
            }
            catch
            {
                return false;
            }
        }
    }
}
