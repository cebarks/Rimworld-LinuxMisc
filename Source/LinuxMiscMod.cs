using System;
using UnityEngine;
using Verse;

namespace RimWorldLinuxMisc
{
    public class LinuxMiscMod : Mod
    {
        private const float ScrollbarWidth = 16f;
        private const float ContentHeightPadding = 4f;
        // CurHeight is only known after drawing, so the first frame needs a conservative extent.
        private const float InitialSettingsContentHeight = 2000f;

        private readonly LinuxMiscSettings settings;
        private Vector2 settingsScrollPosition;
        private float settingsContentHeight;

        public LinuxMiscMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<LinuxMiscSettings>();
        }

        public override string SettingsCategory()
        {
            return "Linux Performance Optimizations";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            float contentHeight = settingsContentHeight > 0f
                ? Math.Max(inRect.height, settingsContentHeight)
                : Math.Max(inRect.height, InitialSettingsContentHeight);
            Rect viewRect = new Rect(0f, 0f, Math.Max(0f, inRect.width - ScrollbarWidth), contentHeight);

            Widgets.BeginScrollView(inRect, ref settingsScrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);
            try
            {
                listing.Label("CPU Affinity Configuration");
                listing.GapLine();
                listing.CheckboxLabeled(
                    "Enable best-effort CPU affinity",
                    ref settings.enableCPUAffinity,
                    "Off by default. Applies a best-effort policy to observed game threads; launcher, taskset, systemd, or cgroup policy is the reliable whole-game option.");

                if (settings.enableCPUAffinity)
                {
                    listing.Gap();
                    listing.Label($"Logical CPUs excluded from the selected set: {settings.reservedCoresForOS}");
                    settings.reservedCoresForOS = (int)listing.Slider(settings.reservedCoresForOS, 0, 4);
                    listing.Label("This does not reserve physical cores for the operating system.");

                    listing.Gap();
                    listing.CheckboxLabeled(
                        "Prefer performance cores when authoritative topology is available",
                        ref settings.preferPerformanceCores,
                        "Falls back conservatively when core type cannot be identified reliably.");

                    listing.Gap();
                    listing.CheckboxLabeled(
                        "Apply CPU affinity while GameMode is active (advanced)",
                        ref settings.applyCPUAffinityWithGamemode,
                        "By default, successful GameMode activation defers the in-mod affinity policy. Enable this only to opt into coexistence.");
                }

                listing.Gap();
                listing.Gap();
                listing.Label("Memory Advice Configuration");
                listing.GapLine();

                listing.CheckboxLabeled(
                    "Request Transparent Huge Pages at startup",
                    ref settings.enableTHP,
                    "On by default. Requests THP where supported; acceptance of advice is not a measured performance gain.");

                listing.Gap();
                listing.CheckboxLabeled(
                    "Enable periodic memory advice passes",
                    ref settings.enablePeriodicMadvise,
                    "Off by default. Repeats enabled mapping advice on a non-overlapping timer.");

                if (settings.enablePeriodicMadvise)
                {
                    listing.Gap();
                    int intervalSeconds = Math.Max(1, settings.periodicMadviseInterval / 1000);
                    listing.Label($"Periodic interval: {intervalSeconds} seconds");
                    intervalSeconds = (int)listing.Slider(intervalSeconds, 1, 300);
                    settings.periodicMadviseInterval = intervalSeconds * 1000;

                    listing.Gap();
                    listing.CheckboxLabeled(
                        "Verbose periodic result logging",
                        ref settings.verbosePeriodicLogging,
                        "Publishes completed request counts from the game thread; timer callbacks do not call game APIs.");
                }

                listing.Gap();
                listing.CheckboxLabeled(
                    "Enable memory prefaulting",
                    ref settings.enableMemoryPrefaulting,
                    "Off by default. Requests MADV_POPULATE_WRITE for writable anonymous ranges; attempted bytes can fault pages and are not a wall-time guarantee.");

                if (settings.enableMemoryPrefaulting)
                {
                    listing.Gap();
                    settings.prefaultBudgetMiB = Math.Max(1, settings.prefaultBudgetMiB);
                    listing.Label($"Prefault attempt cap per pass: {settings.prefaultBudgetMiB} MiB");
                    int displayedMaximum = Math.Max(1024, settings.prefaultBudgetMiB);
                    settings.prefaultBudgetMiB = (int)listing.Slider(settings.prefaultBudgetMiB, 1, displayedMaximum);
                    listing.CheckboxLabeled(
                        "Allow unbounded prefaulting (advanced)",
                        ref settings.allowUnboundedPrefaulting,
                        "Separate explicit opt-in. Ignores the byte cap and may cause long stalls or substantial memory pressure.");
                }

                listing.Gap();
                listing.CheckboxLabeled(
                    "Enable file-backed mapping advice",
                    ref settings.enableFileIOHints,
                    "Off by default. Requests sequential or will-need advice for recognized mapped file types; benefit is not measured.");

                listing.Gap();
                listing.Gap();
                listing.Label("GameMode Configuration");
                listing.GapLine();
                listing.CheckboxLabeled(
                    "Enable GameMode integration",
                    ref settings.enableGamemode,
                    "Requests GameMode activation when the integration and host service are available.");
                listing.Label("Missing GameMode support is handled as an optional unavailable feature.");

                listing.Gap();
                listing.GapLine();
                listing.Label("All changes require restarting the game. Existing saved choices are preserved and may differ from fresh-install defaults.");

                if (listing.ButtonText("Reset to Fresh-Install Defaults"))
                {
                    settings.enableCPUAffinity = false;
                    settings.reservedCoresForOS = 1;
                    settings.preferPerformanceCores = true;
                    settings.applyCPUAffinityWithGamemode = false;
                    settings.enableTHP = true;
                    settings.enablePeriodicMadvise = false;
                    settings.periodicMadviseInterval = 90000;
                    settings.verbosePeriodicLogging = false;
                    settings.enableMemoryPrefaulting = false;
                    settings.enableFileIOHints = false;
                    settings.prefaultBudgetMiB = 64;
                    settings.allowUnboundedPrefaulting = false;
                    settings.enableGamemode = true;
                }
            }
            finally
            {
                settingsContentHeight = Math.Max(inRect.height, listing.CurHeight + ContentHeightPadding);
                listing.End();
                Widgets.EndScrollView();
            }

            base.DoSettingsWindowContents(inRect);
        }

        public static LinuxMiscSettings GetSettings()
        {
            return LoadedModManager.GetMod<LinuxMiscMod>().GetSettings<LinuxMiscSettings>();
        }
    }
}
