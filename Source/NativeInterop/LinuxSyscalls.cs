using System;
using System.Runtime.InteropServices;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal static class LinuxSyscalls
    {
        // prctl constants
        private const int PR_SET_THP_DISABLE = 41;

        // madvise constants
        private const int MADV_SEQUENTIAL = 2;
        private const int MADV_WILLNEED = 3;
        private const int MADV_HUGEPAGE = 14;
        private const int MADV_POPULATE_WRITE = 23; // Linux 5.14+

        [DllImport("libc", SetLastError = true)]
        private static extern int prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

        [DllImport("libc", SetLastError = true)]
        private static extern int madvise(IntPtr addr, UIntPtr length, int advice);

        [DllImport("libc", SetLastError = true)]
        private static extern int sched_setaffinity(int pid, UIntPtr cpusetsize, ref ulong mask);

        [DllImport("libc", SetLastError = true)]
        private static extern int sched_getaffinity(int pid, UIntPtr cpusetsize, ref ulong mask);

        [DllImport("libc", SetLastError = true)]
        private static extern int uname(IntPtr buf);

        public static bool IsLinux()
        {
            return Environment.OSVersion.Platform == PlatformID.Unix;
        }

        public static bool TryEnableTHPViaPrctl()
        {
            if (!IsLinux())
                return false;

            int result = prctl(PR_SET_THP_DISABLE, 0, 0, 0, 0);

            if (result == 0)
                return true;

            int errno = Marshal.GetLastWin32Error();
            return false;
        }

        public static bool TryEnableTHPViaMadvise(IntPtr addr, UIntPtr length)
        {
            if (!IsLinux())
                return false;

            if (addr == IntPtr.Zero || length == UIntPtr.Zero)
                return false;

            int result = madvise(addr, length, MADV_HUGEPAGE);

            if (result == 0)
                return true;

            int errno = Marshal.GetLastWin32Error();
            return false;
        }

        public static int GetLastErrno()
        {
            return Marshal.GetLastWin32Error();
        }

        public static bool TrySetCPUAffinity(ulong affinityMask, out int errno)
        {
            if (!IsLinux())
            {
                errno = 0;
                return false;
            }

            int result = sched_setaffinity(0, new UIntPtr(8), ref affinityMask);
            errno = Marshal.GetLastWin32Error();
            return result == 0;
        }

        public static bool TryGetCPUAffinity(out ulong affinityMask, out int errno)
        {
            affinityMask = 0;
            errno = 0;

            if (!IsLinux())
                return false;

            int result = sched_getaffinity(0, new UIntPtr(8), ref affinityMask);
            errno = Marshal.GetLastWin32Error();
            return result == 0;
        }

        public static bool TryGetKernelVersion(out int major, out int minor, out int errno)
        {
            major = 0;
            minor = 0;
            errno = 0;

            if (!IsLinux())
                return false;

            try
            {
                // Allocate buffer for utsname struct (390 bytes on most systems)
                IntPtr buf = Marshal.AllocHGlobal(390);
                try
                {
                    int result = uname(buf);
                    if (result != 0)
                    {
                        errno = Marshal.GetLastWin32Error();
                        return false;
                    }

                    // release field is at offset 130 (after sysname and nodename)
                    string release = Marshal.PtrToStringAnsi(buf + 130);
                    if (string.IsNullOrEmpty(release))
                        return false;

                    // Parse version string like "6.17.12-300.fc43.x86_64"
                    var parts = release.Split('.');
                    if (parts.Length >= 2 &&
                        int.TryParse(parts[0], out major) &&
                        int.TryParse(parts[1], out minor))
                    {
                        return true;
                    }

                    return false;
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool TryApplyMadviseHint(IntPtr addr, UIntPtr length, int advice)
        {
            if (!IsLinux())
                return false;

            if (addr == IntPtr.Zero || length == UIntPtr.Zero)
                return false;

            int result = madvise(addr, length, advice);
            return result == 0;
        }

        public static bool TryApplyMemoryPrefaulting(IntPtr addr, UIntPtr length)
        {
            return TryApplyMadviseHint(addr, length, MADV_POPULATE_WRITE);
        }

        public static bool TryApplySequentialHint(IntPtr addr, UIntPtr length)
        {
            return TryApplyMadviseHint(addr, length, MADV_SEQUENTIAL);
        }

        public static bool TryApplyWillNeedHint(IntPtr addr, UIntPtr length)
        {
            return TryApplyMadviseHint(addr, length, MADV_WILLNEED);
        }
    }
}
