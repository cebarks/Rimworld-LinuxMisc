#nullable enable

using System;
using System.Threading;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal interface IAffinityScheduler
    {
        IDisposable Schedule(TimeSpan delay, Action callback);
    }

    internal sealed class TimerAffinityScheduler : IAffinityScheduler
    {
        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            Timer? timer = null;
            timer = new Timer(
                _ =>
                {
                    timer?.Dispose();
                    callback();
                },
                null,
                delay,
                Timeout.InfiniteTimeSpan);
            return timer;
        }
    }

    internal sealed class AffinityReconciler : IDisposable
    {
        private readonly object stateLock = new object();
        private readonly AffinityController controller;
        private readonly CpuAffinityMask policyMask;
        private readonly IAffinityScheduler scheduler;
        private readonly TimeSpan interval;
        private IDisposable? scheduledCallback;
        private bool started;
        private bool shutdown;
        private int reconciliationRunning;

        public AffinityReconciler(
            AffinityController controller,
            CpuAffinityMask policyMask,
            IAffinityScheduler scheduler,
            TimeSpan interval)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.policyMask = policyMask ?? throw new ArgumentNullException(nameof(policyMask));
            this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            if (policyMask.IsEmpty)
                throw new ArgumentException("The policy mask cannot be empty", nameof(policyMask));
            if (interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(interval));
            this.interval = interval;
        }

        public void Start()
        {
            lock (stateLock)
            {
                if (started || shutdown)
                    return;

                started = true;
                ScheduleNextLocked();
            }
        }

        public bool TryReconcileNow()
        {
            if (Volatile.Read(ref shutdown))
                return false;
            if (Interlocked.CompareExchange(ref reconciliationRunning, 1, 0) != 0)
                return false;

            try
            {
                if (Volatile.Read(ref shutdown))
                    return false;

                int workerTid = controller.Platform.GetCurrentThreadId();
                controller.ApplyToCurrentThreads(policyMask, workerTid > 0 ? workerTid : null);
                return true;
            }
            catch
            {
                // Reconciliation is deliberately best effort. Native/procfs races must not
                // terminate a ThreadPool worker or access game logging from this callback.
                return true;
            }
            finally
            {
                Volatile.Write(ref reconciliationRunning, 0);
            }
        }

        public void Shutdown()
        {
            IDisposable? callback;
            lock (stateLock)
            {
                if (shutdown)
                    return;

                shutdown = true;
                callback = scheduledCallback;
                scheduledCallback = null;
            }

            callback?.Dispose();
        }

        public void Dispose()
        {
            Shutdown();
        }

        private void OnScheduledCallback()
        {
            lock (stateLock)
            {
                if (shutdown)
                    return;
                scheduledCallback = null;
            }

            try
            {
                TryReconcileNow();
            }
            finally
            {
                lock (stateLock)
                {
                    if (!shutdown)
                        ScheduleNextLocked();
                }
            }
        }

        private void ScheduleNextLocked()
        {
            scheduledCallback = scheduler.Schedule(interval, OnScheduledCallback);
        }
    }
}
