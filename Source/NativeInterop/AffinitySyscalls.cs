using System;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal interface IAffinityNativeCalls
    {
        int GetAffinity(int tid, byte[] destination, out int errno);
        int SetAffinity(int tid, byte[] mask, out int errno);
    }

    internal static class AffinitySyscalls
    {
        private const int InitialMaskBytes = 8;
        private const int MaximumMaskBytes = 1024 * 1024;

        public static bool TryGetAffinity(
            IAffinityNativeCalls nativeCalls,
            int tid,
            out byte[] mask,
            out int errno)
        {
            if (nativeCalls == null)
                throw new ArgumentNullException(nameof(nativeCalls));

            for (int size = InitialMaskBytes; size <= MaximumMaskBytes; size *= 2)
            {
                var candidate = new byte[size];
                int result = nativeCalls.GetAffinity(tid, candidate, out errno);
                if (result == 0)
                {
                    errno = 0;
                    mask = candidate;
                    return true;
                }

                if (errno != AffinityErrno.InvalidArgument)
                {
                    mask = Array.Empty<byte>();
                    return false;
                }
            }

            mask = Array.Empty<byte>();
            errno = AffinityErrno.InvalidArgument;
            return false;
        }

        public static bool TrySetAffinity(
            IAffinityNativeCalls nativeCalls,
            int tid,
            byte[] mask,
            out int errno)
        {
            if (nativeCalls == null)
                throw new ArgumentNullException(nameof(nativeCalls));
            if (mask == null)
                throw new ArgumentNullException(nameof(mask));
            if (mask.Length == 0)
            {
                errno = AffinityErrno.InvalidArgument;
                return false;
            }

            int result = nativeCalls.SetAffinity(tid, mask, out errno);
            if (result == 0)
            {
                errno = 0;
                return true;
            }

            return false;
        }
    }
}
