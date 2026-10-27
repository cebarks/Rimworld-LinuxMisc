using System;
using System.Collections.Generic;
using System.IO;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal sealed class MemoryAdviceInterop : IMemoryOptimizationHost
    {
        private const string TransparentHugePageModePath = "/sys/kernel/mm/transparent_hugepage/enabled";
        private const string ProcessMapsPath = "/proc/self/maps";

        public bool IsLinux => LinuxSyscalls.IsLinux();

        public TransparentHugePageMode ReadTransparentHugePageMode()
        {
            if (!File.Exists(TransparentHugePageModePath))
            {
                return TransparentHugePageMode.Unavailable;
            }

            string value = File.ReadAllText(TransparentHugePageModePath);
            if (value.Contains("[always]", StringComparison.Ordinal))
            {
                return TransparentHugePageMode.Always;
            }
            if (value.Contains("[madvise]", StringComparison.Ordinal))
            {
                return TransparentHugePageMode.Madvise;
            }
            if (value.Contains("[never]", StringComparison.Ordinal))
            {
                return TransparentHugePageMode.Never;
            }
            return TransparentHugePageMode.Unavailable;
        }

        public IReadOnlyList<string> ReadProcessMaps()
        {
            return File.ReadAllLines(ProcessMapsPath);
        }

        public bool TryApply(MemoryAdvice advice, ulong address, ulong length, out int errno)
        {
            bool success;
            IntPtr pointer = address == 0 ? IntPtr.Zero : new IntPtr((long)address);
            UIntPtr byteLength = new UIntPtr(length);
            switch (advice)
            {
                case MemoryAdvice.EnableProcessThp:
                    success = LinuxSyscalls.TryEnableTHPViaPrctl();
                    break;
                case MemoryAdvice.HugePage:
                    success = LinuxSyscalls.TryEnableTHPViaMadvise(pointer, byteLength);
                    break;
                case MemoryAdvice.PopulateWrite:
                    success = LinuxSyscalls.TryApplyMemoryPrefaulting(pointer, byteLength);
                    break;
                case MemoryAdvice.Sequential:
                    success = LinuxSyscalls.TryApplySequentialHint(pointer, byteLength);
                    break;
                case MemoryAdvice.WillNeed:
                    success = LinuxSyscalls.TryApplyWillNeedHint(pointer, byteLength);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(advice), advice, null);
            }

            errno = success ? 0 : LinuxSyscalls.GetLastErrno();
            return success;
        }
    }
}
