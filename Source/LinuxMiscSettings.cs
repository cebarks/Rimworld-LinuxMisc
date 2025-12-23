using Verse;

namespace RimWorldLinuxMisc
{
    public class LinuxMiscSettings : ModSettings
    {
        // THP settings
        public bool enablePeriodicMadvise = true;
        public int periodicMadviseInterval = 30000;
        public bool verbosePeriodicLogging = true;

        // CPU Affinity settings
        public bool enableCPUAffinity = true;
        public int reservedCoresForOS = 1;
        public bool preferPerformanceCores = true;
        public bool applyCPUAffinityWithGamemode = false; // Advanced: override gamemode's CPU management

        // Memory Prefaulting settings
        public bool enableMemoryPrefaulting = true;

        // File I/O Hints settings
        public bool enableFileIOHints = true;

        // Gamemode settings
        public bool enableGamemode = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enablePeriodicMadvise, "enablePeriodicMadvise", true);
            Scribe_Values.Look(ref periodicMadviseInterval, "periodicMadviseInterval", 30000);
            Scribe_Values.Look(ref verbosePeriodicLogging, "verbosePeriodicLogging", true);
            Scribe_Values.Look(ref enableCPUAffinity, "enableCPUAffinity", true);
            Scribe_Values.Look(ref reservedCoresForOS, "reservedCoresForOS", 1);
            Scribe_Values.Look(ref preferPerformanceCores, "preferPerformanceCores", true);
            Scribe_Values.Look(ref applyCPUAffinityWithGamemode, "applyCPUAffinityWithGamemode", false);
            Scribe_Values.Look(ref enableMemoryPrefaulting, "enableMemoryPrefaulting", true);
            Scribe_Values.Look(ref enableFileIOHints, "enableFileIOHints", true);
            Scribe_Values.Look(ref enableGamemode, "enableGamemode", true);
            base.ExposeData();
        }

        public int GetPeriodicInterval()
        {
            if (!enablePeriodicMadvise || periodicMadviseInterval <= 0)
            {
                return 0;
            }

            return periodicMadviseInterval;
        }
    }
}
