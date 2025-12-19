using System;
using System.Runtime.InteropServices;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal static class LinuxSyscalls
    {
        private const int PR_SET_THP_DISABLE = 41;
        private const int MADV_HUGEPAGE = 14;

        [DllImport("libc", SetLastError = true)]
        private static extern int prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

        [DllImport("libc", SetLastError = true)]
        private static extern int madvise(IntPtr addr, UIntPtr length, int advice);

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
    }
}
