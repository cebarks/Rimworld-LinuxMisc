using System;
using System.Collections.Generic;
using System.Linq;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal enum CpuCoreType
    {
        Unknown = 0,
        Efficiency = 1,
        Performance = 2,
    }

    internal sealed class AffinityPolicy
    {
        private AffinityPolicy(CpuAffinityMask mask, bool usedPerformanceCoreTopology)
        {
            Mask = mask;
            UsedPerformanceCoreTopology = usedPerformanceCoreTopology;
        }

        public CpuAffinityMask Mask { get; }
        public bool UsedPerformanceCoreTopology { get; }

        public static AffinityPolicy Build(
            CpuAffinityMask allowedMask,
            int reservedLogicalCpus,
            bool preferPerformanceCores,
            Func<int, CpuCoreType> getCoreType)
        {
            if (allowedMask == null)
                throw new ArgumentNullException(nameof(allowedMask));
            if (getCoreType == null)
                throw new ArgumentNullException(nameof(getCoreType));
            if (allowedMask.IsEmpty)
                throw new ArgumentException("The allowed CPU mask cannot be empty", nameof(allowedMask));

            int[] allowedIds = allowedMask.CpuIds.OrderBy(id => id).ToArray();
            IReadOnlyList<int> candidateIds = allowedIds;
            bool usedPerformanceCoreTopology = false;

            if (preferPerformanceCores)
            {
                var performanceIds = new List<int>();
                bool topologyComplete = true;
                foreach (int cpuId in allowedIds)
                {
                    CpuCoreType coreType = getCoreType(cpuId);
                    if (coreType == CpuCoreType.Unknown)
                    {
                        topologyComplete = false;
                        break;
                    }

                    if (coreType == CpuCoreType.Performance)
                        performanceIds.Add(cpuId);
                }

                // Only restrict to performance cores when the kernel supplied a complete,
                // authoritative core_type classification for every CPU we may use.
                if (topologyComplete && performanceIds.Count > 0)
                {
                    candidateIds = performanceIds;
                    usedPerformanceCoreTopology = true;
                }
            }

            int reserveCount = Math.Max(0, Math.Min(reservedLogicalCpus, candidateIds.Count - 1));
            int keepCount = candidateIds.Count - reserveCount;
            return new AffinityPolicy(
                CpuAffinityMask.FromCpuIds(candidateIds.Take(keepCount), allowedMask.ByteLength),
                usedPerformanceCoreTopology);
        }
    }
}
