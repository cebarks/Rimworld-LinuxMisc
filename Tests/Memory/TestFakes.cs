using System;
using System.Collections.Generic;
using System.Xml;

namespace UnityEngine
{
    public struct Vector2 { }
    public struct Rect
    {
        public float width;
        public float height;
        public Rect(float x, float y, float width, float height)
        {
            this.width = width;
            this.height = height;
        }
    }
}

namespace Verse
{
    public enum LoadSaveMode { Inactive, Saving, LoadingVars, ResolvingCrossRefs, PostLoadInit }

    public static class Scribe
    {
        public static LoadSaveMode mode;
        public static readonly FakeScribeLoader loader = new FakeScribeLoader();
    }

    public sealed class FakeScribeLoader
    {
        public XmlElement curXmlParent;
    }

    public static class Scribe_Values
    {
        public static readonly Dictionary<string, object> Values = new Dictionary<string, object>();

        public static void Look(ref bool value, string label, bool defaultValue = false, bool forceSave = false)
        {
            LookValue(ref value, label, defaultValue);
        }

        public static void Look(ref int value, string label, int defaultValue = 0, bool forceSave = false)
        {
            LookValue(ref value, label, defaultValue);
        }

        private static void LookValue<T>(ref T value, string label, T defaultValue)
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (Values.TryGetValue(label, out object stored)) value = (T)stored;
                else value = defaultValue;
            }
            else if (Scribe.mode == LoadSaveMode.Saving)
            {
                Values[label] = value;
            }
        }
    }

    public class ModSettings
    {
        public virtual void ExposeData() { }
    }

    public class ModContentPack { }

    public class Mod
    {
        public Mod(ModContentPack content) { }
        protected T GetSettings<T>() where T : ModSettings, new() => LoadedModManager.Settings as T ?? new T();
        public virtual string SettingsCategory() => string.Empty;
        public virtual void DoSettingsWindowContents(UnityEngine.Rect inRect) { }
    }

    public static class LoadedModManager
    {
        public static ModSettings Settings;
        public static T GetMod<T>() where T : Mod => (T)Activator.CreateInstance(typeof(T), new ModContentPack());
    }

    public static class Widgets
    {
        public static void BeginScrollView(UnityEngine.Rect outRect, ref UnityEngine.Vector2 scrollPosition, UnityEngine.Rect viewRect) { }
        public static void EndScrollView() { }
    }

    public sealed class Listing_Standard
    {
        public float CurHeight => 0f;
        public void Begin(UnityEngine.Rect rect) { }
        public void End() { }
        public void Label(string text) { }
        public void GapLine() { }
        public void Gap(float gap = 12f) { }
        public void CheckboxLabeled(string label, ref bool value, string tooltip = null) { }
        public float Slider(float value, float min, float max) => value;
        public bool ButtonText(string label) => false;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class StaticConstructorOnStartupAttribute : Attribute { }

    public static class Log
    {
        public static readonly List<string> Entries = new List<string>();
        public static void Message(string message) => Entries.Add("M:" + message);
        public static void Warning(string message) => Entries.Add("W:" + message);
        public static void Error(string message) => Entries.Add("E:" + message);
    }
}

namespace RimWorldLinuxMisc.NativeInterop
{
    internal static class LinuxSyscalls
    {
        public static bool IsLinux() => true;
        public static bool TryEnableTHPViaPrctl() => true;
        public static bool TryEnableTHPViaMadvise(IntPtr address, UIntPtr length) => true;
        public static bool TryApplyMemoryPrefaulting(IntPtr address, UIntPtr length) => true;
        public static bool TryApplySequentialHint(IntPtr address, UIntPtr length) => true;
        public static bool TryApplyWillNeedHint(IntPtr address, UIntPtr length) => true;
        public static int GetLastErrno() => 0;
    }

    internal static class GamemodeManager
    {
        public static readonly List<string> Calls = new List<string>();
        public static bool EnableGamemode() { Calls.Add("gamemode"); return true; }
    }

    internal static class CPUOptimizer
    {
        public static readonly List<string> Calls = GamemodeManager.Calls;
        public static bool EnableCPUAffinity(bool gamemodeActive) { Calls.Add("affinity:" + gamemodeActive); return true; }
    }
}
