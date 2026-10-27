using Verse;

namespace RimWorldLinuxMisc
{
    public class LinuxMiscSettings : ModSettings
    {
        // Memory settings. Startup THP and recurring scans are deliberately independent.
        public bool enableTHP = true;
        public bool enablePeriodicMadvise = false;
        public int periodicMadviseInterval = 90000;
        public bool verbosePeriodicLogging = false;
        public bool enableMemoryPrefaulting = false;
        public bool enableFileIOHints = false;
        public int prefaultBudgetMiB = 64;
        public bool allowUnboundedPrefaulting = false;

        // CPU affinity settings
        public bool enableCPUAffinity = false;
        public int reservedCoresForOS = 1;
        public bool preferPerformanceCores = true;
        public bool applyCPUAffinityWithGamemode = false;

        // GameMode settings
        public bool enableGamemode = true;

        public override void ExposeData()
        {
            bool loading = Scribe.mode == LoadSaveMode.LoadingVars;
            bool hasStartupThpKey = loading && HasSavedKey("enableTHP");
            bool hasLegacyThpKey = loading && HasSavedKey("enablePeriodicMadvise");

            Scribe_Values.Look(ref enableTHP, "enableTHP", true);
            Scribe_Values.Look(ref enablePeriodicMadvise, "enablePeriodicMadvise", false);
            Scribe_Values.Look(ref periodicMadviseInterval, "periodicMadviseInterval", 90000);
            Scribe_Values.Look(ref verbosePeriodicLogging, "verbosePeriodicLogging", false);
            Scribe_Values.Look(ref enableMemoryPrefaulting, "enableMemoryPrefaulting", false);
            Scribe_Values.Look(ref enableFileIOHints, "enableFileIOHints", false);
            Scribe_Values.Look(ref prefaultBudgetMiB, "prefaultBudgetMiB", 64);
            Scribe_Values.Look(ref allowUnboundedPrefaulting, "allowUnboundedPrefaulting", false);

            Scribe_Values.Look(ref enableCPUAffinity, "enableCPUAffinity", false);
            Scribe_Values.Look(ref reservedCoresForOS, "reservedCoresForOS", 1);
            Scribe_Values.Look(ref preferPerformanceCores, "preferPerformanceCores", true);
            Scribe_Values.Look(ref applyCPUAffinityWithGamemode, "applyCPUAffinityWithGamemode", false);
            Scribe_Values.Look(ref enableGamemode, "enableGamemode", true);

            // Before enableTHP existed, enablePeriodicMadvise was presented as the THP switch.
            // Only migrate an actually persisted legacy value; a fresh settings node keeps the
            // new startup-on/periodic-off defaults.
            if (loading && !hasStartupThpKey && hasLegacyThpKey)
            {
                enableTHP = enablePeriodicMadvise;
            }

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

        private static bool HasSavedKey(string key)
        {
            return Scribe.loader?.curXmlParent?[key] != null;
        }
    }
}
