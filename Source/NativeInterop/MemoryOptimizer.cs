using System;
using Verse;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal static class MemoryOptimizer
    {
        private static readonly object LifecycleLock = new object();
        private static MemoryOptimizationService service;
        private static bool verbosePeriodicLogging;
        private static long publishedPeriodicPasses;
        private static bool cleanupRegistered;

        public static bool EnableTHP()
        {
            try
            {
                LinuxMiscSettings settings = LinuxMiscMod.GetSettings();
                MemoryOptimizationOptions options = MemoryOptimizationOptions.FromSettings(settings);
                MemoryOptimizationService replacement = new MemoryOptimizationService(
                    options,
                    new MemoryAdviceInterop(),
                    new SystemMemoryTimerFactory());

                MemoryPassResult startup = replacement.Start();
                lock (LifecycleLock)
                {
                    service?.Shutdown();
                    service = replacement;
                    verbosePeriodicLogging = options.VerbosePeriodicLogging;
                    publishedPeriodicPasses = 0;
                    RegisterCleanupHandler();
                }

                LogStartupResult(options, startup, replacement.IsPeriodicScheduled);
                return startup.AnySuccessful || replacement.IsPeriodicScheduled;
            }
            catch (Exception exception)
            {
                Log.Error($"[LinuxMisc] Memory optimization initialization failed: {exception}");
                return false;
            }
        }

        internal static MemoryPassResult LatestPeriodicResult
        {
            get
            {
                lock (LifecycleLock)
                {
                    return service?.LatestPeriodicResult ?? MemoryPassResult.Empty();
                }
            }
        }

        internal static void PublishPeriodicStatus()
        {
            MemoryPassResult result;
            long completed;
            lock (LifecycleLock)
            {
                if (!verbosePeriodicLogging || service == null)
                {
                    return;
                }

                MemoryPeriodicSnapshot snapshot = service.LatestPeriodicSnapshot;
                completed = snapshot.Sequence;
                if (completed <= publishedPeriodicPasses)
                {
                    return;
                }

                publishedPeriodicPasses = completed;
                result = snapshot.Result;
            }

            Log.Message($"[LinuxMisc] Periodic memory advice pass {completed}: THP {Format(result.TransparentHugePages)}; prefault {Format(result.Prefault)}; file {Format(result.FileHints)}");
            if (!string.IsNullOrEmpty(result.Error))
            {
                Log.Warning("[LinuxMisc] " + result.Error);
            }
        }

        internal static void Shutdown()
        {
            MemoryOptimizationService current;
            lock (LifecycleLock)
            {
                current = service;
                service = null;
                verbosePeriodicLogging = false;
            }
            current?.Shutdown();
        }

        private static void LogStartupResult(MemoryOptimizationOptions options, MemoryPassResult result, bool periodicScheduled)
        {
            if (!options.EnableStartupThp && !options.EnableMemoryPrefaulting && !options.EnableFileIOHints && !periodicScheduled)
            {
                Log.Message("[LinuxMisc] Memory optimizations disabled in settings");
                return;
            }

            if (options.EnableStartupThp)
            {
                if (result.HostThpMode == TransparentHugePageMode.Never || result.HostThpMode == TransparentHugePageMode.Unavailable)
                {
                    Log.Warning($"[LinuxMisc] Startup THP request skipped because host THP mode is {result.HostThpMode.ToString().ToLowerInvariant()}");
                }
                else
                {
                    Log.Message($"[LinuxMisc] Startup THP advice: {Format(result.TransparentHugePages)}");
                }
            }

            if (options.EnableMemoryPrefaulting)
            {
                string limit = options.AllowUnboundedPrefaulting
                    ? "unbounded opt-in"
                    : $"cap {options.PrefaultBudgetBytes / (1024 * 1024)} MiB attempted/pass";
                Log.Message($"[LinuxMisc] Startup prefault advice ({limit}): {Format(result.Prefault)}");
            }

            if (options.EnableFileIOHints)
            {
                Log.Message($"[LinuxMisc] Startup file advice: {Format(result.FileHints)}");
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                Log.Warning("[LinuxMisc] " + result.Error);
            }

            if (periodicScheduled)
            {
                Log.Message($"[LinuxMisc] Periodic memory advice scheduled every {options.PeriodicIntervalMilliseconds}ms; results are requests, not measured performance gains");
            }
        }

        private static string Format(MemoryFeatureResult result)
        {
            string errno = result.LastErrno.HasValue ? $", last errno {result.LastErrno.Value}" : string.Empty;
            return $"{result.SuccessfulCalls}/{result.CallsAttempted} requests accepted, {result.BytesAttempted} bytes attempted{errno}";
        }

        private static void RegisterCleanupHandler()
        {
            if (cleanupRegistered)
            {
                return;
            }

            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
            cleanupRegistered = true;
        }
    }
}
