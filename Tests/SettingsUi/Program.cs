using System;
using System.Collections.Generic;
using System.Linq;
using RimWorldLinuxMisc;
using UnityEngine;
using Verse;

internal static class Program
{
    private static int failures;
    private static readonly Rect FixedSettingsPanel = new Rect(10f, 20f, 700f, 700f);

    private static int Main()
    {
        Run(nameof(CollapsedControlsAndResetAreInsideBalancedScrollView), CollapsedControlsAndResetAreInsideBalancedScrollView);
        Run(nameof(AllExpandedControlsFitFirstFrameScrollContent), AllExpandedControlsFitFirstFrameScrollContent);
        Run(nameof(ContentHeightIsMeasuredAfterFirstFrame), ContentHeightIsMeasuredAfterFirstFrame);
        Run(nameof(ScrollPositionPersistsPerModInstance), ScrollPositionPersistsPerModInstance);
        Run(nameof(BeginEndCallsRemainBalancedWhenDrawingFails), BeginEndCallsRemainBalancedWhenDrawingFails);
        Run(nameof(ResetButtonStillRestoresApprovedDefaults), ResetButtonStillRestoresApprovedDefaults);

        Console.WriteLine(failures == 0 ? "PASS: 6 settings UI tests" : $"FAIL: {failures} settings UI tests");
        return failures == 0 ? 0 : 1;
    }

    private static void CollapsedControlsAndResetAreInsideBalancedScrollView()
    {
        LinuxMiscSettings settings = FreshSettings();
        LinuxMiscMod mod = CreateMod(settings);
        UiTrace.Reset();

        mod.DoSettingsWindowContents(FixedSettingsPanel);

        Equal(1, UiTrace.ScrollBeginCount, "scroll view begins once");
        Equal(1, UiTrace.ScrollEndCount, "scroll view ends once");
        Equal(0, UiTrace.ScrollDepth, "scroll begin/end are balanced");
        Equal(1, UiTrace.ListingBeginCount, "listing begins once");
        Equal(1, UiTrace.ListingEndCount, "listing ends once");
        True(UiTrace.ListingBeginInsideScroll.Single(), "listing begins inside scroll view");
        True(UiTrace.Controls.All(control => control.InsideScrollView), "every collapsed control is scroll content");
        HasControl("button", "Reset to Fresh-Install Defaults");
        HasControl("label", "All changes require restarting the game. Existing saved choices are preserved and may differ from fresh-install defaults.");
        Equal(1, UiTrace.BaseCalls, "base UI invoked once");
    }

    private static void AllExpandedControlsFitFirstFrameScrollContent()
    {
        LinuxMiscSettings settings = ExpandedSettings();
        LinuxMiscMod mod = CreateMod(settings);
        UiTrace.Reset();

        mod.DoSettingsWindowContents(FixedSettingsPanel);

        float measuredHeight = UiTrace.MeasuredListingHeights.Single();
        Rect firstFrameView = UiTrace.ViewRects.Single();
        True(measuredHeight > FixedSettingsPanel.height, $"expanded content demonstrates clipping risk without scrolling ({measuredHeight} > {FixedSettingsPanel.height})");
        Console.WriteLine($"EVIDENCE expanded content {measuredHeight}px exceeds fixed panel {FixedSettingsPanel.height}px; first-frame view is {firstFrameView.height}px");
        True(firstFrameView.height >= measuredHeight, "first-frame scroll content is tall enough for measured expanded controls");
        True(firstFrameView.width < FixedSettingsPanel.width, "content reserves scrollbar width");
        True(UiTrace.Controls.All(control => control.InsideScrollView), "every expanded control is scroll content");
        HasControl("label", "Logical CPUs excluded from the selected set: 1");
        HasControl("checkbox", "Apply CPU affinity while GameMode is active (advanced)");
        HasControl("label", "Periodic interval: 90 seconds");
        HasControl("checkbox", "Verbose periodic result logging");
        HasControl("label", "Prefault attempt cap per pass: 64 MiB");
        HasControl("checkbox", "Allow unbounded prefaulting (advanced)");
        HasControl("button", "Reset to Fresh-Install Defaults");
        Equal(UiTrace.ScrollBeginCount, UiTrace.ScrollEndCount, "expanded scroll calls balanced");
        Equal(UiTrace.ListingBeginCount, UiTrace.ListingEndCount, "expanded listing calls balanced");
    }

    private static void ContentHeightIsMeasuredAfterFirstFrame()
    {
        LinuxMiscMod mod = CreateMod(ExpandedSettings());
        UiTrace.Reset();
        mod.DoSettingsWindowContents(FixedSettingsPanel);
        float firstViewHeight = UiTrace.ViewRects.Single().height;
        float measuredHeight = UiTrace.MeasuredListingHeights.Single();

        UiTrace.Reset();
        mod.DoSettingsWindowContents(FixedSettingsPanel);
        float secondViewHeight = UiTrace.ViewRects.Single().height;

        True(secondViewHeight >= measuredHeight, "second frame keeps all measured content reachable");
        True(secondViewHeight < firstViewHeight, "second frame replaces conservative first-frame height with measured content height");
        Equal(1, UiTrace.ScrollBeginCount, "second frame scroll begins once");
        Equal(1, UiTrace.ScrollEndCount, "second frame scroll ends once");
    }

    private static void ScrollPositionPersistsPerModInstance()
    {
        LinuxMiscSettings settings = ExpandedSettings();
        LinuxMiscMod first = CreateMod(settings);
        UiTrace.Reset();
        UiTrace.ReplaceScrollPositionOnBegin = new Vector2(0f, 123f);
        first.DoSettingsWindowContents(FixedSettingsPanel);
        Equal(0f, UiTrace.IncomingScrollPositions.Single().y, "new instance starts at top");

        UiTrace.Reset();
        first.DoSettingsWindowContents(FixedSettingsPanel);
        Equal(123f, UiTrace.IncomingScrollPositions.Single().y, "same instance retains scroll position");

        LinuxMiscMod second = CreateMod(settings);
        UiTrace.Reset();
        second.DoSettingsWindowContents(FixedSettingsPanel);
        Equal(0f, UiTrace.IncomingScrollPositions.Single().y, "different instance has independent scroll position");
    }

    private static void BeginEndCallsRemainBalancedWhenDrawingFails()
    {
        LinuxMiscMod mod = CreateMod(FreshSettings());
        UiTrace.Reset();
        UiTrace.ThrowOnControlText = "Memory Advice Configuration";

        bool threw = false;
        try
        {
            mod.DoSettingsWindowContents(FixedSettingsPanel);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        True(threw, "fake drawing failure was injected");
        Equal(1, UiTrace.ScrollBeginCount, "failed draw began one scroll view");
        Equal(1, UiTrace.ScrollEndCount, "failed draw ended one scroll view");
        Equal(0, UiTrace.ScrollDepth, "failed draw leaves no open scroll view");
        Equal(1, UiTrace.ListingBeginCount, "failed draw began one listing");
        Equal(1, UiTrace.ListingEndCount, "failed draw ended one listing");
    }

    private static void ResetButtonStillRestoresApprovedDefaults()
    {
        LinuxMiscSettings settings = ExpandedSettings();
        settings.reservedCoresForOS = 4;
        settings.preferPerformanceCores = false;
        settings.periodicMadviseInterval = 123000;
        settings.verbosePeriodicLogging = true;
        settings.prefaultBudgetMiB = 999;
        settings.enableGamemode = false;
        LinuxMiscMod mod = CreateMod(settings);
        UiTrace.Reset();
        UiTrace.ClickButtonLabel = "Reset to Fresh-Install Defaults";

        mod.DoSettingsWindowContents(FixedSettingsPanel);

        Equal(false, settings.enableCPUAffinity, "CPU default");
        Equal(1, settings.reservedCoresForOS, "reserved CPU default");
        Equal(true, settings.preferPerformanceCores, "performance core default");
        Equal(false, settings.applyCPUAffinityWithGamemode, "affinity/GameMode default");
        Equal(true, settings.enableTHP, "startup THP default");
        Equal(false, settings.enablePeriodicMadvise, "periodic default");
        Equal(90000, settings.periodicMadviseInterval, "periodic interval default");
        Equal(false, settings.verbosePeriodicLogging, "verbose default");
        Equal(false, settings.enableMemoryPrefaulting, "prefault default");
        Equal(false, settings.enableFileIOHints, "file hints default");
        Equal(64, settings.prefaultBudgetMiB, "prefault cap default");
        Equal(false, settings.allowUnboundedPrefaulting, "unbounded default");
        Equal(true, settings.enableGamemode, "GameMode default");
        True(UiTrace.Controls.Single(control => control.Kind == "button").InsideScrollView, "reset button remains in scroll content");
    }

    private static LinuxMiscSettings FreshSettings() => new LinuxMiscSettings();

    private static LinuxMiscSettings ExpandedSettings() => new LinuxMiscSettings
    {
        enableCPUAffinity = true,
        enablePeriodicMadvise = true,
        enableMemoryPrefaulting = true
    };

    private static LinuxMiscMod CreateMod(LinuxMiscSettings settings)
    {
        LoadedModManager.Settings = settings;
        return new LinuxMiscMod(new ModContentPack());
    }

    private static void HasControl(string kind, string text)
    {
        True(UiTrace.Controls.Any(control => control.Kind == kind && control.Text == text), $"missing {kind}: {text}");
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
        }
    }
}
