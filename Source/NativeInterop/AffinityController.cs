using System;
using System.Collections.Generic;
using System.Linq;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal interface IAffinityPlatform
    {
        IReadOnlyList<int> GetThreadIds();
        int GetCurrentThreadId();
        CpuCoreType GetCoreType(int cpuId);
        bool TryGetAffinity(int tid, out CpuAffinityMask mask, out int errno);
        bool TrySetAffinity(int tid, CpuAffinityMask mask, out int errno);
    }

    internal static class AffinityErrno
    {
        public const int NoSuchProcess = 3;
        public const int InvalidArgument = 22;
    }

    internal sealed class AffinityApplySummary
    {
        public int EnumeratedThreads { get; internal set; }
        public int VerifiedThreads { get; internal set; }
        public int ChangedThreads { get; internal set; }
        public int AlreadyCompliantThreads { get; internal set; }
        public int RacedThreads { get; internal set; }
        public int FailedThreads { get; internal set; }
        public int NoOverlapThreads { get; internal set; }
        public int ReadbackMismatchThreads { get; internal set; }
    }

    internal sealed class AffinityController
    {
        private readonly IAffinityPlatform platform;

        public AffinityController(IAffinityPlatform platform)
        {
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        }

        public IAffinityPlatform Platform => platform;

        public AffinityApplySummary ApplyToCurrentThreads(CpuAffinityMask policyMask, int? excludedTid)
        {
            if (policyMask == null)
                throw new ArgumentNullException(nameof(policyMask));
            if (policyMask.IsEmpty)
                throw new ArgumentException("The policy mask cannot be empty", nameof(policyMask));

            var summary = new AffinityApplySummary();
            IReadOnlyList<int> tids;
            try
            {
                tids = platform.GetThreadIds() ?? Array.Empty<int>();
            }
            catch
            {
                summary.FailedThreads++;
                return summary;
            }

            foreach (int tid in tids.Where(tid => tid > 0).Distinct().OrderBy(tid => tid))
            {
                if (excludedTid.HasValue && tid == excludedTid.Value)
                    continue;

                summary.EnumeratedThreads++;
                try
                {
                    ApplyToThread(tid, policyMask, summary);
                }
                catch
                {
                    // A single disappearing or otherwise malformed task must not prevent
                    // best-effort processing of the remaining current threads.
                    summary.FailedThreads++;
                }
            }

            return summary;
        }

        private void ApplyToThread(int tid, CpuAffinityMask policyMask, AffinityApplySummary summary)
        {
            if (!platform.TryGetAffinity(tid, out CpuAffinityMask currentMask, out int errno))
            {
                RecordFailure(errno, summary);
                return;
            }

            CpuAffinityMask targetMask = currentMask.Intersect(policyMask);
            if (targetMask.IsEmpty)
            {
                // Never replace a valid per-thread mask with an empty mask. A thread may
                // intentionally be confined outside the process-wide best-effort policy.
                summary.NoOverlapThreads++;
                return;
            }

            if (targetMask.Equals(currentMask))
            {
                summary.AlreadyCompliantThreads++;
                summary.VerifiedThreads++;
                return;
            }

            if (!platform.TrySetAffinity(tid, targetMask, out errno))
            {
                RecordFailure(errno, summary);
                return;
            }

            if (!platform.TryGetAffinity(tid, out CpuAffinityMask effectiveMask, out errno))
            {
                RecordFailure(errno, summary);
                return;
            }

            if (!effectiveMask.Equals(targetMask))
            {
                summary.ReadbackMismatchThreads++;
                return;
            }

            summary.ChangedThreads++;
            summary.VerifiedThreads++;
        }

        private static void RecordFailure(int errno, AffinityApplySummary summary)
        {
            if (errno == AffinityErrno.NoSuchProcess)
                summary.RacedThreads++;
            else
                summary.FailedThreads++;
        }
    }
}
