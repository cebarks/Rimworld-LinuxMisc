using System;
using System.Globalization;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal enum MemoryMapKind
    {
        Anonymous,
        File,
        Special,
        Unknown
    }

    internal readonly record struct MemoryMapRegion(
        ulong StartAddress,
        ulong EndAddress,
        bool Readable,
        bool Writable,
        bool Private,
        string Pathname,
        MemoryMapKind Kind)
    {
        public ulong Length => EndAddress - StartAddress;
    }

    internal static class MemoryMapParser
    {
        public static bool TryParse(string line, out MemoryMapRegion region)
        {
            region = default;
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            ReadOnlySpan<char> remaining = line.AsSpan().Trim();
            if (!TryTakeField(ref remaining, out ReadOnlySpan<char> addresses) ||
                !TryTakeField(ref remaining, out ReadOnlySpan<char> permissions) ||
                !TryTakeField(ref remaining, out _) ||
                !TryTakeField(ref remaining, out _) ||
                !TryTakeField(ref remaining, out _))
            {
                return false;
            }

            int separator = addresses.IndexOf('-');
            if (separator <= 0 || separator == addresses.Length - 1 ||
                !ulong.TryParse(addresses[..separator], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong start) ||
                !ulong.TryParse(addresses[(separator + 1)..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong end) ||
                start >= end || start > long.MaxValue || end > long.MaxValue)
            {
                return false;
            }

            if (permissions.Length < 4)
            {
                return false;
            }

            string pathname = remaining.Trim().ToString();
            MemoryMapKind kind = Classify(pathname);
            region = new MemoryMapRegion(
                start,
                end,
                permissions[0] == 'r',
                permissions[1] == 'w',
                permissions[3] == 'p',
                pathname,
                kind);
            return true;
        }

        private static MemoryMapKind Classify(string pathname)
        {
            if (pathname.Length == 0 ||
                pathname == "[heap]" ||
                pathname == "[stack]" ||
                pathname.StartsWith("[stack:", StringComparison.Ordinal) ||
                pathname.StartsWith("[anon:", StringComparison.Ordinal) ||
                pathname.StartsWith("[anon_shmem:", StringComparison.Ordinal))
            {
                return MemoryMapKind.Anonymous;
            }

            if (pathname[0] == '[' && pathname[^1] == ']')
            {
                return MemoryMapKind.Special;
            }

            if (pathname[0] == '/')
            {
                return MemoryMapKind.File;
            }

            return MemoryMapKind.Unknown;
        }

        private static bool TryTakeField(ref ReadOnlySpan<char> remaining, out ReadOnlySpan<char> field)
        {
            remaining = remaining.TrimStart();
            if (remaining.IsEmpty)
            {
                field = default;
                return false;
            }

            int index = remaining.IndexOfAny(' ', '\t');
            if (index < 0)
            {
                field = remaining;
                remaining = default;
                return true;
            }

            field = remaining[..index];
            remaining = remaining[(index + 1)..];
            return true;
        }
    }
}
