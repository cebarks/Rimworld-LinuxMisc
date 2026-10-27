using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Rect
    {
        public float x;
        public float y;
        public float width;
        public float height;

        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }
}

namespace RimWorldLinuxMisc
{
    public sealed class LinuxMiscSettings : Verse.ModSettings
    {
        public bool enableTHP = true;
        public bool enablePeriodicMadvise;
        public int periodicMadviseInterval = 90000;
        public bool verbosePeriodicLogging;
        public bool enableMemoryPrefaulting;
        public bool enableFileIOHints;
        public int prefaultBudgetMiB = 64;
        public bool allowUnboundedPrefaulting;
        public bool enableCPUAffinity;
        public int reservedCoresForOS = 1;
        public bool preferPerformanceCores = true;
        public bool applyCPUAffinityWithGamemode;
        public bool enableGamemode = true;
    }
}

namespace Verse
{
    using UnityEngine;

    public class ModSettings { }
    public class ModContentPack { }

    public class Mod
    {
        public Mod(ModContentPack content) { }
        protected T GetSettings<T>() where T : ModSettings, new() => (T)LoadedModManager.Settings;
        public virtual string SettingsCategory() => string.Empty;
        public virtual void DoSettingsWindowContents(Rect inRect) => UiTrace.BaseCalls++;
    }

    public static class LoadedModManager
    {
        public static ModSettings Settings;
        public static T GetMod<T>() where T : Mod => (T)Activator.CreateInstance(typeof(T), new ModContentPack());
    }

    public static class Widgets
    {
        public static void BeginScrollView(Rect outRect, ref Vector2 scrollPosition, Rect viewRect)
        {
            UiTrace.ScrollBeginCount++;
            UiTrace.ScrollDepth++;
            UiTrace.ViewportRects.Add(outRect);
            UiTrace.ViewRects.Add(viewRect);
            UiTrace.IncomingScrollPositions.Add(scrollPosition);
            if (UiTrace.ReplaceScrollPositionOnBegin.HasValue)
            {
                scrollPosition = UiTrace.ReplaceScrollPositionOnBegin.Value;
            }
        }

        public static void EndScrollView()
        {
            UiTrace.ScrollEndCount++;
            UiTrace.ScrollDepth--;
        }
    }

    public sealed class Listing_Standard
    {
        private const float LineHeight = 30f;
        private float curHeight;

        public float CurHeight => curHeight;

        public void Begin(Rect rect)
        {
            UiTrace.ListingBeginCount++;
            UiTrace.ListingBeginInsideScroll.Add(UiTrace.ScrollDepth > 0);
            UiTrace.ListingRects.Add(rect);
            curHeight = 0f;
        }

        public void End()
        {
            UiTrace.ListingEndCount++;
            UiTrace.MeasuredListingHeights.Add(curHeight);
        }

        public void Label(string text)
        {
            Record("label", text);
            curHeight += LineHeight;
        }

        public void GapLine()
        {
            Record("gapline", string.Empty);
            curHeight += 12f;
        }

        public void Gap(float gap = 12f)
        {
            Record("gap", string.Empty);
            curHeight += gap;
        }

        public void CheckboxLabeled(string label, ref bool value, string tooltip = null)
        {
            Record("checkbox", label);
            curHeight += LineHeight;
        }

        public float Slider(float value, float min, float max)
        {
            Record("slider", $"{min}-{max}");
            curHeight += LineHeight;
            return value;
        }

        public bool ButtonText(string label)
        {
            Record("button", label);
            curHeight += LineHeight;
            return UiTrace.ClickButtonLabel == label;
        }

        private static void Record(string kind, string text)
        {
            UiTrace.Controls.Add(new UiControl(kind, text, UiTrace.ScrollDepth > 0));
            if (UiTrace.ThrowOnControlText == text)
            {
                throw new InvalidOperationException("Injected UI failure at " + text);
            }
        }
    }

    public readonly record struct UiControl(string Kind, string Text, bool InsideScrollView);

    public static class UiTrace
    {
        public static int ScrollBeginCount;
        public static int ScrollEndCount;
        public static int ScrollDepth;
        public static int ListingBeginCount;
        public static int ListingEndCount;
        public static int BaseCalls;
        public static string ClickButtonLabel;
        public static string ThrowOnControlText;
        public static Vector2? ReplaceScrollPositionOnBegin;
        public static readonly List<Rect> ViewportRects = new List<Rect>();
        public static readonly List<Rect> ViewRects = new List<Rect>();
        public static readonly List<Rect> ListingRects = new List<Rect>();
        public static readonly List<Vector2> IncomingScrollPositions = new List<Vector2>();
        public static readonly List<bool> ListingBeginInsideScroll = new List<bool>();
        public static readonly List<float> MeasuredListingHeights = new List<float>();
        public static readonly List<UiControl> Controls = new List<UiControl>();

        public static void Reset()
        {
            ScrollBeginCount = 0;
            ScrollEndCount = 0;
            ScrollDepth = 0;
            ListingBeginCount = 0;
            ListingEndCount = 0;
            BaseCalls = 0;
            ClickButtonLabel = null;
            ThrowOnControlText = null;
            ReplaceScrollPositionOnBegin = null;
            ViewportRects.Clear();
            ViewRects.Clear();
            ListingRects.Clear();
            IncomingScrollPositions.Clear();
            ListingBeginInsideScroll.Clear();
            MeasuredListingHeights.Clear();
            Controls.Clear();
        }
    }
}
