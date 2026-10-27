using System;
using System.Collections.Generic;

namespace Verse
{
    public static class Log
    {
        public static readonly List<string> Messages = new List<string>();

        public static void Message(string message) => Messages.Add("message:" + message);
        public static void Warning(string message) => Messages.Add("warning:" + message);
        public static void Error(string message) => Messages.Add("error:" + message);
    }
}

namespace RimWorldLinuxMisc
{
    internal sealed class LinuxMiscSettings
    {
        internal bool enableGamemode = true;
    }

    internal static class LinuxMiscMod
    {
        internal static LinuxMiscSettings Settings = new LinuxMiscSettings();
        internal static LinuxMiscSettings GetSettings() => Settings;
    }
}

namespace RimWorldLinuxMisc.NativeInterop
{
    internal static class LinuxSyscalls
    {
        internal static bool IsLinux() => OperatingSystem.IsLinux();
    }
}
