using UnityEngine;
using Verse;

namespace RimWorldLinuxMisc
{
    public class LinuxMiscMod : Mod
    {
        private LinuxMiscSettings settings;

        public LinuxMiscMod(ModContentPack content) : base(content)
        {
            this.settings = GetSettings<LinuxMiscSettings>();
        }

        public override string SettingsCategory()
        {
            return "Linux Performance Optimizations";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            // CPU Affinity Section
            listingStandard.Label("CPU Affinity Configuration");
            listingStandard.GapLine();

            listingStandard.CheckboxLabeled(
                "Enable CPU affinity optimization",
                ref settings.enableCPUAffinity,
                "Pin game to specific CPU cores for better cache locality and reduced context switches"
            );

            if (settings.enableCPUAffinity)
            {
                listingStandard.Gap();
                listingStandard.Label($"Reserved cores for OS: {settings.reservedCoresForOS}");
                settings.reservedCoresForOS = (int)listingStandard.Slider(settings.reservedCoresForOS, 0, 4);
                listingStandard.Label("Number of CPU cores to leave available for system processes (0-4)");

                listingStandard.Gap();
                listingStandard.CheckboxLabeled(
                    "Prefer performance cores (hybrid CPUs)",
                    ref settings.preferPerformanceCores,
                    "On hybrid CPUs (Intel 12th gen+), pin only to P-cores instead of E-cores"
                );

                listingStandard.Gap();
                listingStandard.CheckboxLabeled(
                    "Apply CPU affinity with gamemode (Advanced)",
                    ref settings.applyCPUAffinityWithGamemode,
                    "Override gamemode's CPU management. WARNING: May conflict with gamemode's optimizations."
                );
            }

            listingStandard.Gap();
            listingStandard.Gap();

            // Memory Optimization Section
            listingStandard.Label("Memory Optimization Configuration");
            listingStandard.GapLine();

            listingStandard.CheckboxLabeled(
                "Enable Transparent Huge Pages (THP)",
                ref settings.enablePeriodicMadvise,
                "Apply THP to improve memory performance (5-15% TPS boost on large colonies)"
            );

            if (settings.enablePeriodicMadvise)
            {
                listingStandard.Gap();
                listingStandard.Label("Periodic Interval:");
                listingStandard.Label("Run memory optimizations every N seconds. Set to 0 for startup only.");

                listingStandard.Gap(4f);
                int intervalSeconds = settings.periodicMadviseInterval / 1000;
                listingStandard.Label($"Interval: {intervalSeconds} seconds ({settings.periodicMadviseInterval}ms)");
                intervalSeconds = (int)listingStandard.Slider(intervalSeconds, 0, 300);
                settings.periodicMadviseInterval = intervalSeconds * 1000;

                listingStandard.Gap();
                listingStandard.CheckboxLabeled(
                    "Verbose periodic logging",
                    ref settings.verbosePeriodicLogging,
                    "Show log messages each time periodic optimizations run (disable to reduce log spam)"
                );
            }

            listingStandard.Gap();
            listingStandard.CheckboxLabeled(
                "Enable memory prefaulting",
                ref settings.enableMemoryPrefaulting,
                "Pre-fault memory pages to reduce runtime stuttering (requires kernel 5.14+)"
            );

            listingStandard.Gap();
            listingStandard.CheckboxLabeled(
                "Enable file I/O hints",
                ref settings.enableFileIOHints,
                "Optimize file-backed memory for faster save/load operations"
            );

            listingStandard.Gap();
            listingStandard.Gap();

            // Gamemode Section
            listingStandard.Label("Gamemode Configuration");
            listingStandard.GapLine();

            listingStandard.CheckboxLabeled(
                "Enable gamemode integration",
                ref settings.enableGamemode,
                "Activate gamemode performance optimizations when available"
            );

            listingStandard.Gap();
            listingStandard.Label("Note: Requires gamemode to be installed on your system.");

            listingStandard.Gap();
            listingStandard.Gap();
            listingStandard.GapLine();
            listingStandard.Label("Note: Changes take effect after restarting the game.");

            if (listingStandard.ButtonText("Reset to Defaults"))
            {
                settings.enableCPUAffinity = true;
                settings.reservedCoresForOS = 1;
                settings.preferPerformanceCores = true;
                settings.applyCPUAffinityWithGamemode = false;
                settings.enablePeriodicMadvise = true;
                settings.periodicMadviseInterval = 30000;
                settings.verbosePeriodicLogging = true;
                settings.enableMemoryPrefaulting = true;
                settings.enableFileIOHints = true;
                settings.enableGamemode = true;
            }

            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public static LinuxMiscSettings GetSettings()
        {
            return LoadedModManager.GetMod<LinuxMiscMod>().GetSettings<LinuxMiscSettings>();
        }
    }
}
