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
        private static extern int sched_setaffinity(int pid, UIntPtr cpusetsize, [In] byte[] mask);

        [DllImport("libc", SetLastError = true)]
        private static extern int sched_getaffinity(int pid, UIntPtr cpusetsize, [Out] byte[] mask);

        [DllImport("libc", SetLastError = true)]
        private static extern int gettid();

        [DllImport("libc", SetLastError = true)]
        private static extern int uname(IntPtr buf);

        private sealed class LibcAffinityNativeCalls : IAffinityNativeCalls
        {
            public int GetAffinity(int tid, byte[] destination, out int errno)
            {
                int result = sched_getaffinity(tid, new UIntPtr((uint)destination.Length), destination);
                errno = result == 0 ? 0 : Marshal.GetLastWin32Error();
                return result;
            }

            public int SetAffinity(int tid, byte[] mask, out int errno)
            {
                int result = sched_setaffinity(tid, new UIntPtr((uint)mask.Length), mask);
                errno = result == 0 ? 0 : Marshal.GetLastWin32Error();
                return result;
            }
        }

        private static readonly IAffinityNativeCalls affinityNativeCalls = new LibcAffinityNativeCalls();

        public static bool IsLinux()
        {
            return OperatingSystem.IsLinux();
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

        public static bool TrySetCPUAffinity(int tid, byte[] affinityMask, out int errno)
        {
            if (!IsLinux())
            {
                errno = 0;
                return false;
            }

            return AffinitySyscalls.TrySetAffinity(affinityNativeCalls, tid, affinityMask, out errno);
        }

        public static bool TryGetCPUAffinity(int tid, out byte[] affinityMask, out int errno)
        {
            affinityMask = Array.Empty<byte>();
            errno = 0;

            if (!IsLinux())
                return false;

            return AffinitySyscalls.TryGetAffinity(affinityNativeCalls, tid, out affinityMask, out errno);
        }

        public static int GetCurrentThreadId()
        {
            if (!IsLinux())
                return 0;

            return gettid();
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
