using System;
using System.Collections.Generic;
using System.Threading;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal enum TransparentHugePageMode
    {
        Unavailable,
        Never,
        Madvise,
        Always
    }

    internal enum MemoryAdvice
    {
        EnableProcessThp,
        HugePage,
        PopulateWrite,
        Sequential,
        WillNeed
    }

    internal sealed class MemoryOptimizationOptions
    {
        private const ulong PageSize = 4096;
        private const ulong Mebibyte = 1024 * 1024;

        public MemoryOptimizationOptions(
            bool enableStartupThp,
            bool enablePeriodicMadvise,
            int periodicIntervalMilliseconds,
            bool verbosePeriodicLogging,
            bool enableMemoryPrefaulting,
            bool enableFileIOHints,
            ulong prefaultBudgetBytes,
            bool allowUnboundedPrefaulting)
        {
            EnableStartupThp = enableStartupThp;
            EnablePeriodicMadvise = enablePeriodicMadvise;
            PeriodicIntervalMilliseconds = Math.Max(0, periodicIntervalMilliseconds);
            VerbosePeriodicLogging = verbosePeriodicLogging;
            EnableMemoryPrefaulting = enableMemoryPrefaulting;
            EnableFileIOHints = enableFileIOHints;
            PrefaultBudgetBytes = NormalizeBudget(prefaultBudgetBytes);
            AllowUnboundedPrefaulting = allowUnboundedPrefaulting;
        }

        public bool EnableStartupThp { get; }
        public bool EnablePeriodicMadvise { get; }
        public int PeriodicIntervalMilliseconds { get; }
        public bool VerbosePeriodicLogging { get; }
        public bool EnableMemoryPrefaulting { get; }
        public bool EnableFileIOHints { get; }
        public ulong PrefaultBudgetBytes { get; }
        public bool AllowUnboundedPrefaulting { get; }

        public static MemoryOptimizationOptions FromSettings(LinuxMiscSettings settings)
        {
            ulong budgetMiB = (ulong)Math.Max(1, settings.prefaultBudgetMiB);
            return new MemoryOptimizationOptions(
                settings.enableTHP,
                settings.enablePeriodicMadvise,
                settings.GetPeriodicInterval(),
                settings.verbosePeriodicLogging,
                settings.enableMemoryPrefaulting,
                settings.enableFileIOHints,
                budgetMiB * Mebibyte,
                settings.allowUnboundedPrefaulting);
        }

        private static ulong NormalizeBudget(ulong value)
        {
            ulong clamped = Math.Max(PageSize, value);
            return clamped - (clamped % PageSize);
        }
    }

    internal sealed record MemoryFeatureResult(int CallsAttempted, int SuccessfulCalls, int FailedCalls, ulong BytesAttempted, int? LastErrno)
    {
        public static readonly MemoryFeatureResult Empty = new MemoryFeatureResult(0, 0, 0, 0, null);
    }

    internal sealed record MemoryPassResult(
        MemoryFeatureResult TransparentHugePages,
        MemoryFeatureResult Prefault,
        MemoryFeatureResult FileHints,
        TransparentHugePageMode HostThpMode,
        string Error)
    {
        public bool AnyAttempted => TransparentHugePages.CallsAttempted > 0 || Prefault.CallsAttempted > 0 || FileHints.CallsAttempted > 0;
        public bool AnySuccessful => TransparentHugePages.SuccessfulCalls > 0 || Prefault.SuccessfulCalls > 0 || FileHints.SuccessfulCalls > 0;

        public static MemoryPassResult Empty(string error = null) => new MemoryPassResult(
            MemoryFeatureResult.Empty,
            MemoryFeatureResult.Empty,
            MemoryFeatureResult.Empty,
            TransparentHugePageMode.Unavailable,
            error);
    }

    internal sealed record MemoryPeriodicSnapshot(long Sequence, MemoryPassResult Result);

    internal interface IMemoryOptimizationHost
    {
        bool IsLinux { get; }
        TransparentHugePageMode ReadTransparentHugePageMode();
        IReadOnlyList<string> ReadProcessMaps();
        bool TryApply(MemoryAdvice advice, ulong address, ulong length, out int errno);
    }

    internal interface IMemoryTimer : IDisposable
    {
        void Arm(int dueTimeMilliseconds);
    }

    internal interface IMemoryTimerFactory
    {
        IMemoryTimer Create(Action callback);
    }

    internal sealed class MemoryOptimizationService : IDisposable
    {
        private readonly MemoryOptimizationOptions options;
        private readonly IMemoryOptimizationHost host;
        private readonly IMemoryTimerFactory timerFactory;
        private readonly object lifecycleLock = new object();
        private IMemoryTimer timer;
        private bool started;
        private bool shutdown;
        private int callbackRunning;
        private MemoryPeriodicSnapshot periodicSnapshot = new MemoryPeriodicSnapshot(0, MemoryPassResult.Empty());

        public MemoryOptimizationService(MemoryOptimizationOptions options, IMemoryOptimizationHost host, IMemoryTimerFactory timerFactory)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.timerFactory = timerFactory ?? throw new ArgumentNullException(nameof(timerFactory));
        }

        public MemoryPeriodicSnapshot LatestPeriodicSnapshot => Volatile.Read(ref periodicSnapshot);
        public MemoryPassResult LatestPeriodicResult => LatestPeriodicSnapshot.Result;
        public long CompletedPeriodicPasses => LatestPeriodicSnapshot.Sequence;
        public bool IsPeriodicScheduled { get; private set; }

        public MemoryPassResult Start()
        {
            lock (lifecycleLock)
            {
                if (started)
                {
                    throw new InvalidOperationException("Memory optimization service has already been started.");
                }
                started = true;
            }

            if (!host.IsLinux)
            {
                return MemoryPassResult.Empty("Memory advice is only available on Linux.");
            }

            MemoryPassResult startup = RunPass(periodic: false);
            if (options.EnablePeriodicMadvise && options.PeriodicIntervalMilliseconds > 0)
            {
                lock (lifecycleLock)
                {
                    if (!shutdown)
                    {
                        timer = timerFactory.Create(OnTimer);
                        timer.Arm(options.PeriodicIntervalMilliseconds);
                        IsPeriodicScheduled = true;
                    }
                }
            }

            return startup;
        }

        public void Shutdown()
        {
            IMemoryTimer timerToDispose;
            lock (lifecycleLock)
            {
                if (shutdown)
                {
                    return;
                }

                shutdown = true;
                IsPeriodicScheduled = false;
                timerToDispose = timer;
                timer = null;
            }

            timerToDispose?.Dispose();
        }

        public void Dispose()
        {
            Shutdown();
        }

        private void OnTimer()
        {
            lock (lifecycleLock)
            {
                if (shutdown)
                {
                    return;
                }
            }

            if (Interlocked.CompareExchange(ref callbackRunning, 1, 0) != 0)
            {
                return;
            }

            try
            {
                MemoryPassResult result = RunPass(periodic: true);
                var snapshot = new MemoryPeriodicSnapshot(LatestPeriodicSnapshot.Sequence + 1, result);
                Volatile.Write(ref periodicSnapshot, snapshot);
            }
            finally
            {
                Volatile.Write(ref callbackRunning, 0);
                lock (lifecycleLock)
                {
                    if (!shutdown && timer != null)
                    {
                        timer.Arm(options.PeriodicIntervalMilliseconds);
                    }
                }
            }
        }

        private MemoryPassResult RunPass(bool periodic)
        {
            bool requestHugePages = periodic ? options.EnablePeriodicMadvise : options.EnableStartupThp;
            bool requestPrefault = options.EnableMemoryPrefaulting;
            bool requestFileHints = options.EnableFileIOHints;
            if (!requestHugePages && !requestPrefault && !requestFileHints)
            {
                return MemoryPassResult.Empty();
            }

            TransparentHugePageMode thpMode = TransparentHugePageMode.Unavailable;
            string error = null;
            if (requestHugePages)
            {
                try
                {
                    thpMode = host.ReadTransparentHugePageMode();
                }
                catch (Exception exception)
                {
                    error = "Failed to read THP mode: " + exception.Message;
                }
            }

            FeatureAccumulator thp = new FeatureAccumulator();
            FeatureAccumulator prefault = new FeatureAccumulator();
            FeatureAccumulator fileHints = new FeatureAccumulator();
            bool adviseHugePageMappings = requestHugePages && thpMode == TransparentHugePageMode.Madvise;

            if (requestHugePages && thpMode == TransparentHugePageMode.Always && !periodic)
            {
                bool processRequestSucceeded = TryApply(MemoryAdvice.EnableProcessThp, 0, 0, thp);
                adviseHugePageMappings = !processRequestSucceeded;
            }

            bool needMaps = adviseHugePageMappings || requestPrefault || requestFileHints;
            if (!needMaps)
            {
                return new MemoryPassResult(thp.ToResult(), prefault.ToResult(), fileHints.ToResult(), thpMode, error);
            }

            IReadOnlyList<string> lines;
            try
            {
                lines = host.ReadProcessMaps() ?? Array.Empty<string>();
            }
            catch (Exception exception)
            {
                string mapsError = "Failed to read process maps: " + exception.Message;
                error = error == null ? mapsError : error + " " + mapsError;
                return new MemoryPassResult(thp.ToResult(), prefault.ToResult(), fileHints.ToResult(), thpMode, error);
            }

            ulong prefaultRemaining = options.AllowUnboundedPrefaulting ? ulong.MaxValue : options.PrefaultBudgetBytes;
            foreach (string line in lines)
            {
                if (!MemoryMapParser.TryParse(line, out MemoryMapRegion region) ||
                    !TryGetAlignedRange(region, out ulong address, out ulong length))
                {
                    continue;
                }

                bool anonymousWritable = region.Kind == MemoryMapKind.Anonymous && region.Writable && region.Private;
                if (adviseHugePageMappings && anonymousWritable)
                {
                    TryApply(MemoryAdvice.HugePage, address, length, thp);
                }

                if (requestPrefault && anonymousWritable && prefaultRemaining >= 4096)
                {
                    ulong attemptedLength = options.AllowUnboundedPrefaulting ? length : Math.Min(length, prefaultRemaining);
                    attemptedLength -= attemptedLength % 4096;
                    if (attemptedLength >= 4096)
                    {
                        TryApply(MemoryAdvice.PopulateWrite, address, attemptedLength, prefault);
                        if (!options.AllowUnboundedPrefaulting)
                        {
                            prefaultRemaining -= attemptedLength;
                        }
                    }
                }

                if (requestFileHints && region.Kind == MemoryMapKind.File && region.Readable)
                {
                    MemoryAdvice advice = IsSequentialPath(region.Pathname) ? MemoryAdvice.Sequential : MemoryAdvice.WillNeed;
                    if (IsSupportedFileHintPath(region.Pathname))
                    {
                        TryApply(advice, address, length, fileHints);
                    }
                }
            }

            return new MemoryPassResult(thp.ToResult(), prefault.ToResult(), fileHints.ToResult(), thpMode, error);
        }

        private bool TryApply(MemoryAdvice advice, ulong address, ulong length, FeatureAccumulator result)
        {
            bool success = false;
            int errno = 0;
            try
            {
                success = host.TryApply(advice, address, length, out errno);
            }
            catch
            {
                success = false;
                errno = -1;
            }
            result.Record(length, success, errno);
            return success;
        }

        private static bool TryGetAlignedRange(MemoryMapRegion region, out ulong address, out ulong length)
        {
            const ulong pageSize = 4096;
            ulong alignedStart = region.StartAddress + ((pageSize - (region.StartAddress % pageSize)) % pageSize);
            ulong alignedEnd = region.EndAddress - (region.EndAddress % pageSize);
            if (alignedStart >= alignedEnd)
            {
                address = 0;
                length = 0;
                return false;
            }

            address = alignedStart;
            length = alignedEnd - alignedStart;
            return true;
        }

        private static bool IsSupportedFileHintPath(string pathname)
        {
            string path = pathname.EndsWith(" (deleted)", StringComparison.Ordinal) ? pathname[..^10] : pathname;
            return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains(".so.", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".rws", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSequentialPath(string pathname)
        {
            string path = pathname.EndsWith(" (deleted)", StringComparison.Ordinal) ? pathname[..^10] : pathname;
            return path.EndsWith(".rws", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class FeatureAccumulator
        {
            private int attempted;
            private int successful;
            private ulong bytes;
            private int? lastErrno;

            public void Record(ulong attemptedBytes, bool success, int errno)
            {
                attempted++;
                bytes += attemptedBytes;
                if (success)
                {
                    successful++;
                }
                else
                {
                    lastErrno = errno;
                }
            }

            public MemoryFeatureResult ToResult()
            {
                return new MemoryFeatureResult(attempted, successful, attempted - successful, bytes, lastErrno);
            }
        }
    }

    internal sealed class SystemMemoryTimerFactory : IMemoryTimerFactory
    {
        public IMemoryTimer Create(Action callback) => new SystemMemoryTimer(callback);
    }

    internal sealed class SystemMemoryTimer : IMemoryTimer
    {
        private readonly Timer timer;
        private bool disposed;

        public SystemMemoryTimer(Action callback)
        {
            timer = new Timer(_ => callback(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Arm(int dueTimeMilliseconds)
        {
            if (!disposed)
            {
                timer.Change(dueTimeMilliseconds, Timeout.Infinite);
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                timer.Dispose();
            }
        }
    }
}
