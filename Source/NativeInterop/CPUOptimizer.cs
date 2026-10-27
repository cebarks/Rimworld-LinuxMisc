#nullable enable

using System;
using Verse;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal static class CPUOptimizer
    {
        private static readonly object lifecycleLock = new object();
        private static AffinityReconciler? reconciler;
        private static bool shutdownHooksRegistered;

        public static bool EnableCPUAffinity(bool gamemodeActive = false)
        {
            lock (lifecycleLock)
            {
                return EnableCPUAffinityLocked(gamemodeActive);
            }
        }

        private static bool EnableCPUAffinityLocked(bool gamemodeActive)
        {
            StopReconciliation();

            try
            {
                if (!LinuxSyscalls.IsLinux())
                {
                    Log.Message("[LinuxMisc] Not running on Linux, skipping CPU affinity");
                    return false;
                }

                LinuxMiscSettings settings = LinuxMiscMod.GetSettings();
                bool enabled = settings.enableCPUAffinity;
                int reservedLogicalCpus = settings.reservedCoresForOS;
                bool preferPerformanceCores = settings.preferPerformanceCores;
                bool applyWithGamemode = settings.applyCPUAffinityWithGamemode;

                if (!enabled)
                {
                    Log.Message("[LinuxMisc] CPU affinity disabled in settings");
                    return false;
                }

                if (gamemodeActive && !applyWithGamemode)
                {
                    Log.Message("[LinuxMisc] Skipping CPU affinity because GameMode activated and coexistence was not enabled");
                    return false;
                }

                if (gamemodeActive && applyWithGamemode)
                    Log.Warning("[LinuxMisc] Applying CPU affinity alongside active GameMode by user request");

                var platform = new LinuxAffinityPlatform();
                int currentTid = platform.GetCurrentThreadId();
                if (!platform.TryGetAffinity(currentTid > 0 ? currentTid : 0, out CpuAffinityMask allowedMask, out int errno))
                {
                    Log.Warning($"[LinuxMisc] Failed to read the current thread's allowed CPU IDs (errno={errno})");
                    return false;
                }

                AffinityPolicy policy = AffinityPolicy.Build(
                    allowedMask,
                    reservedLogicalCpus,
                    preferPerformanceCores,
                    platform.GetCoreType);
                if (policy.Mask.IsEmpty)
                {
                    Log.Warning("[LinuxMisc] Refusing to apply an empty CPU affinity policy");
                    return false;
                }

                var controller = new AffinityController(platform);
                AffinityApplySummary summary = controller.ApplyToCurrentThreads(policy.Mask, excludedTid: null);
                LogInitialSummary(summary, allowedMask, policy);

                if (summary.VerifiedThreads == 0)
                {
                    Log.Warning("[LinuxMisc] CPU affinity was not verified for any current thread; reconciliation was not started");
                    return false;
                }

                var newReconciler = new AffinityReconciler(
                    controller,
                    policy.Mask,
                    new TimerAffinityScheduler(),
                    TimeSpan.FromSeconds(30));
                reconciler = newReconciler;
                RegisterShutdownHooksLocked();
                newReconciler.Start();

                Log.Message("[LinuxMisc] CPU affinity enabled for current threads with 30-second best-effort new-thread reconciliation");
                return true;
            }
            catch (Exception ex)
            {
                StopReconciliation();
                Log.Error($"[LinuxMisc] Exception while enabling CPU affinity: {ex}");
                return false;
            }
        }

        internal static void ShutdownCPUAffinity()
        {
            StopReconciliation();
        }

        private static void LogInitialSummary(
            AffinityApplySummary summary,
            CpuAffinityMask allowedMask,
            AffinityPolicy policy)
        {
            string selection = policy.UsedPerformanceCoreTopology
                ? "authoritative kernel core_type performance CPUs"
                : "allowed CPU IDs with the configured logical-CPU exclusion";

            Log.Message(
                $"[LinuxMisc] CPU affinity policy selected {policy.Mask.Count}/{allowedMask.Count} allowed logical CPUs using {selection}");
            Log.Message(
                $"[LinuxMisc] CPU affinity initial pass: enumerated={summary.EnumeratedThreads}, " +
                $"verified={summary.VerifiedThreads}, changed={summary.ChangedThreads}, " +
                $"alreadyCompliant={summary.AlreadyCompliantThreads}, raced={summary.RacedThreads}, " +
                $"failed={summary.FailedThreads}, noOverlapPreserved={summary.NoOverlapThreads}, " +
                $"readbackMismatch={summary.ReadbackMismatchThreads}");
        }

        private static void StopReconciliation()
        {
            AffinityReconciler? previous;
            lock (lifecycleLock)
            {
                previous = reconciler;
                reconciler = null;
            }

            previous?.Shutdown();
        }

        private static void RegisterShutdownHooksLocked()
        {
            if (shutdownHooksRegistered)
                return;

            AppDomain.CurrentDomain.ProcessExit += OnProcessShutdown;
            AppDomain.CurrentDomain.DomainUnload += OnProcessShutdown;
            shutdownHooksRegistered = true;
        }

        private static void OnProcessShutdown(object? sender, EventArgs args)
        {
            StopReconciliation();
        }
    }
}
