using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal interface IAffinitySysfsReader
    {
        bool FileExists(string path);
        string ReadAllText(string path);
    }

    internal sealed class SystemAffinitySysfsReader : IAffinitySysfsReader
    {
        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }
    }

    internal sealed class LinuxAffinityPlatform : IAffinityPlatform
    {
        private const string ProcessTaskPath = "/proc/self/task";
        private const string DefaultCpuSysfsPath = "/sys/devices/system/cpu";
        private readonly string cpuSysfsPath;
        private readonly IAffinitySysfsReader sysfsReader;

        public LinuxAffinityPlatform()
            : this(DefaultCpuSysfsPath, new SystemAffinitySysfsReader())
        {
        }

        internal LinuxAffinityPlatform(string cpuSysfsPath, IAffinitySysfsReader sysfsReader)
        {
            if (string.IsNullOrWhiteSpace(cpuSysfsPath))
                throw new ArgumentException("A CPU sysfs root is required", nameof(cpuSysfsPath));

            this.cpuSysfsPath = cpuSysfsPath;
            this.sysfsReader = sysfsReader ?? throw new ArgumentNullException(nameof(sysfsReader));
        }

        public IReadOnlyList<int> GetThreadIds()
        {
            var tids = new List<int>();
            try
            {
                foreach (string directory in Directory.EnumerateDirectories(ProcessTaskPath))
                {
                    string name = Path.GetFileName(directory);
                    if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int tid) && tid > 0)
                        tids.Add(tid);
                }
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return tids;
        }

        public int GetCurrentThreadId()
        {
            try
            {
                return LinuxSyscalls.GetCurrentThreadId();
            }
            catch (EntryPointNotFoundException)
            {
                return 0;
            }
        }

        public CpuCoreType GetCoreType(int cpuId)
        {
            if (cpuId < 0)
                return CpuCoreType.Unknown;

            string path = Path.Combine(cpuSysfsPath, $"cpu{cpuId}", "topology", "core_type");
            try
            {
                if (!sysfsReader.FileExists(path))
                    return CpuCoreType.Unknown;

                string value = sysfsReader.ReadAllText(path).Trim();
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int coreType))
                    return CpuCoreType.Unknown;

                return coreType switch
                {
                    1 => CpuCoreType.Efficiency,
                    2 => CpuCoreType.Performance,
                    _ => CpuCoreType.Unknown,
                };
            }
            catch (IOException)
            {
                return CpuCoreType.Unknown;
            }
            catch (UnauthorizedAccessException)
            {
                return CpuCoreType.Unknown;
            }
        }

        public bool TryGetAffinity(int tid, out CpuAffinityMask mask, out int errno)
        {
            mask = CpuAffinityMask.Empty;
            try
            {
                if (!LinuxSyscalls.TryGetCPUAffinity(tid, out byte[] bytes, out errno))
                    return false;

                mask = CpuAffinityMask.FromBytes(bytes);
                return !mask.IsEmpty;
            }
            catch (DllNotFoundException)
            {
                errno = 0;
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                errno = 0;
                return false;
            }
        }

        public bool TrySetAffinity(int tid, CpuAffinityMask mask, out int errno)
        {
            if (mask == null || mask.IsEmpty)
            {
                errno = AffinityErrno.InvalidArgument;
                return false;
            }

            try
            {
                return LinuxSyscalls.TrySetCPUAffinity(tid, mask.ToBytes(), out errno);
            }
            catch (DllNotFoundException)
            {
                errno = 0;
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                errno = 0;
                return false;
            }
        }
    }
}
