using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal static class CPUOptimizer
    {
        private const string CPU_TOPOLOGY_PATH = "/sys/devices/system/cpu";

        private struct CpuTopology
        {
            public int TotalCores;
            public List<int> PerformanceCores;
            public List<int> EfficiencyCores;
            public bool IsHybrid;
        }

        public static bool EnableCPUAffinity()
        {
            try
            {
                if (!LinuxSyscalls.IsLinux())
                {
                    Log.Message("[LinuxMisc] Not running on Linux, skipping CPU affinity");
                    return false;
                }

                var settings = LinuxMiscMod.GetSettings();
                if (!settings.enableCPUAffinity)
                {
                    Log.Message("[LinuxMisc] CPU affinity disabled in settings");
                    return false;
                }

                // Check for gamemode conflict
                bool gamemodeEnabled = settings.enableGamemode && GamemodeInterop.IsLibraryAvailable();
                if (gamemodeEnabled && !settings.applyCPUAffinityWithGamemode)
                {
                    Log.Message("[LinuxMisc] Skipping CPU affinity - gamemode will handle CPU management (enable 'Apply CPU affinity with gamemode' to override)");
                    return false;
                }

                if (gamemodeEnabled && settings.applyCPUAffinityWithGamemode)
                {
                    Log.Warning("[LinuxMisc] Applying CPU affinity alongside gamemode - this may conflict with gamemode's CPU management");
                }

                // Get current affinity to see available cores
                if (!LinuxSyscalls.TryGetCPUAffinity(out ulong currentMask, out int errno))
                {
                    Log.Warning($"[LinuxMisc] Failed to get current CPU affinity (errno={errno})");
                    return false;
                }

                int availableCores = CountSetBits(currentMask);
                Log.Message($"[LinuxMisc] Available CPU cores: {availableCores}");

                // Detect CPU topology
                var topology = DetectCpuTopology(availableCores);

                // Build affinity mask based on topology and settings
                ulong newMask = BuildAffinityMask(topology, currentMask, settings);

                if (newMask == 0)
                {
                    Log.Warning("[LinuxMisc] Failed to build valid CPU affinity mask");
                    return false;
                }

                // Apply the new affinity
                if (LinuxSyscalls.TrySetCPUAffinity(newMask, out errno))
                {
                    int pinnedCores = CountSetBits(newMask);
                    if (topology.IsHybrid)
                    {
                        Log.Message($"[LinuxMisc] CPU affinity set to {pinnedCores} performance cores (hybrid CPU detected)");
                    }
                    else
                    {
                        Log.Message($"[LinuxMisc] CPU affinity set to {pinnedCores}/{availableCores} cores ({settings.reservedCoresForOS} reserved for OS)");
                    }
                    return true;
                }
                else
                {
                    Log.Warning($"[LinuxMisc] Failed to set CPU affinity (errno={errno})");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[LinuxMisc] Exception while setting CPU affinity: {ex}");
                return false;
            }
        }

        private static CpuTopology DetectCpuTopology(int totalCores)
        {
            var topology = new CpuTopology
            {
                TotalCores = totalCores,
                PerformanceCores = new List<int>(),
                EfficiencyCores = new List<int>(),
                IsHybrid = false
            };

            try
            {
                if (!Directory.Exists(CPU_TOPOLOGY_PATH))
                    return topology;

                // Try to detect hybrid CPU by checking core types
                // Intel hybrid CPUs have different max frequencies for P/E cores
                var coreFrequencies = new Dictionary<int, int>();

                for (int i = 0; i < totalCores; i++)
                {
                    string maxFreqPath = $"{CPU_TOPOLOGY_PATH}/cpu{i}/cpufreq/cpuinfo_max_freq";
                    if (File.Exists(maxFreqPath))
                    {
                        try
                        {
                            string freqStr = File.ReadAllText(maxFreqPath).Trim();
                            if (int.TryParse(freqStr, out int freq))
                            {
                                coreFrequencies[i] = freq;
                            }
                        }
                        catch
                        {
                            // Ignore errors reading individual core frequencies
                        }
                    }
                }

                if (coreFrequencies.Count > 0)
                {
                    // Group cores by frequency
                    var frequencyGroups = coreFrequencies.GroupBy(kvp => kvp.Value)
                        .OrderByDescending(g => g.Key)
                        .ToList();

                    // If we have at least 2 distinct frequency groups, it's likely a hybrid CPU
                    if (frequencyGroups.Count >= 2)
                    {
                        topology.IsHybrid = true;
                        // Highest frequency cores are P-cores
                        topology.PerformanceCores = frequencyGroups[0].Select(kvp => kvp.Key).ToList();
                        // Lower frequency cores are E-cores
                        for (int i = 1; i < frequencyGroups.Count; i++)
                        {
                            topology.EfficiencyCores.AddRange(frequencyGroups[i].Select(kvp => kvp.Key));
                        }

                        Log.Message($"[LinuxMisc] Hybrid CPU detected: {topology.PerformanceCores.Count} P-cores, {topology.EfficiencyCores.Count} E-cores");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[LinuxMisc] Failed to detect CPU topology: {ex.Message}");
            }

            return topology;
        }

        private static ulong BuildAffinityMask(CpuTopology topology, ulong currentMask, LinuxMiscSettings settings)
        {
            ulong newMask = 0;

            if (topology.IsHybrid && settings.preferPerformanceCores && topology.PerformanceCores.Count > 0)
            {
                // Hybrid CPU: pin to performance cores only
                foreach (int core in topology.PerformanceCores)
                {
                    if (core < 64) // CPU affinity mask is 64 bits
                    {
                        newMask |= (1UL << core);
                    }
                }
            }
            else
            {
                // Non-hybrid or user doesn't prefer P-cores: use all cores except reserved
                int totalCores = CountSetBits(currentMask);
                int reservedCores = Math.Max(0, Math.Min(settings.reservedCoresForOS, totalCores - 1));
                int targetCores = totalCores - reservedCores;

                int coresAdded = 0;
                for (int i = 0; i < 64 && coresAdded < targetCores; i++)
                {
                    if ((currentMask & (1UL << i)) != 0)
                    {
                        newMask |= (1UL << i);
                        coresAdded++;
                    }
                }
            }

            return newMask;
        }

        private static int CountSetBits(ulong value)
        {
            int count = 0;
            while (value != 0)
            {
                count++;
                value &= value - 1;
            }
            return count;
        }
    }
}
