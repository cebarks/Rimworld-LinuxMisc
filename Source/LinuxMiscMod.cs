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

            listingStandard.Label("Transparent Huge Pages (THP) Configuration");
            listingStandard.GapLine();

            listingStandard.CheckboxLabeled(
                "Enable periodic madvise THP applications",
                ref settings.enablePeriodicMadvise,
                "Continuously apply madvise at regular intervals to catch new memory allocations"
            );

            if (settings.enablePeriodicMadvise)
            {
                listingStandard.Gap();
                listingStandard.Label("Periodic Interval:");
                listingStandard.Label("Run madvise every N seconds. Set to 0 to disable periodic execution (startup only).");

                listingStandard.Gap(4f);
                int intervalSeconds = settings.periodicMadviseInterval / 1000;
                listingStandard.Label($"Interval: {intervalSeconds} seconds ({settings.periodicMadviseInterval}ms)");
                intervalSeconds = (int)listingStandard.Slider(intervalSeconds, 0, 300);
                settings.periodicMadviseInterval = intervalSeconds * 1000;
            }

            listingStandard.Gap();
            listingStandard.GapLine();
            listingStandard.Label("Note: Changes take effect after restarting the game.");

            if (listingStandard.ButtonText("Reset to Defaults"))
            {
                settings.enablePeriodicMadvise = true;
                settings.periodicMadviseInterval = 30000;
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
