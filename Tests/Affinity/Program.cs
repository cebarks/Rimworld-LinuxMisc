using RimWorldLinuxMisc.NativeInterop;

var tests = new (string Name, Action Body)[]
{
    ("Linux platform detection uses the net10 OS predicate", LinuxPlatformDetection),
    ("dynamic masks probe past 64 CPUs and preserve sparse high IDs", DynamicMasksProbeHighCpuIds),
    ("policy reserves actual allowed CPU IDs", PolicyUsesActualAllowedIds),
    ("authoritative core types are used and unknown topology falls back", TopologySignalAndFallback),
    ("Linux core_type adapter handles recognized and fallback inputs", LinuxCoreTypeAdapter),
    ("all current TIDs are intersected without widening narrower masks", PerTidIntersectionPreservesNarrowMasks),
    ("ESRCH races, partial failures, no-overlap and readback mismatches are reported", PartialFailuresAreAccurate),
    ("reconciliation applies newly observed TIDs and excludes its worker", ReconciliationAppliesNewThreads),
    ("reconciliation is non-overlapping and does not rearm after shutdown", ReconciliationLifecycle),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL {test.Name}: {ex.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

if (failures.Count != 0)
{
    Console.Error.WriteLine($"{failures.Count} test(s) failed");
    return 1;
}

Console.WriteLine($"PASS all {tests.Length} affinity regression tests");
return 0;

static void LinuxPlatformDetection()
{
    Assert.Equal(OperatingSystem.IsLinux(), LinuxSyscalls.IsLinux());
}

static void DynamicMasksProbeHighCpuIds()
{
    var native = new FakeNativeAffinityCalls(
        requiredSize: 32,
        mask: CpuAffinityMask.FromCpuIds([2, 130, 255], 32).ToBytes());

    Assert.True(AffinitySyscalls.TryGetAffinity(native, 77, out var bytes, out var errno));
    Assert.Equal(0, errno);
    Assert.SequenceEqual([8, 16, 32], native.GetSizes);
    Assert.SetEqual([2, 130, 255], CpuAffinityMask.FromBytes(bytes).CpuIds);

    var fatal = new FakeNativeAffinityCalls(requiredSize: 32, mask: bytes, undersizeErrno: 1);
    Assert.False(AffinitySyscalls.TryGetAffinity(fatal, 77, out _, out errno));
    Assert.Equal(1, errno);
    Assert.SequenceEqual([8], fatal.GetSizes);
}

static void PolicyUsesActualAllowedIds()
{
    var allowed = CpuAffinityMask.FromCpuIds([2, 64, 130], 32);
    var policy = AffinityPolicy.Build(allowed, reservedLogicalCpus: 1, preferPerformanceCores: false, _ => CpuCoreType.Unknown);

    Assert.SetEqual([2, 64], policy.Mask.CpuIds);
    Assert.False(policy.UsedPerformanceCoreTopology);
}

static void TopologySignalAndFallback()
{
    var allowed = CpuAffinityMask.FromCpuIds([0, 2, 8, 65], 16);
    var types = new Dictionary<int, CpuCoreType>
    {
        [0] = CpuCoreType.Efficiency,
        [2] = CpuCoreType.Performance,
        [8] = CpuCoreType.Efficiency,
        [65] = CpuCoreType.Performance,
    };

    var performance = AffinityPolicy.Build(allowed, 1, true, cpu => types[cpu]);
    Assert.True(performance.UsedPerformanceCoreTopology);
    Assert.SetEqual([2], performance.Mask.CpuIds);

    var clampedPerformance = AffinityPolicy.Build(allowed, 20, true, cpu => types[cpu]);
    Assert.True(clampedPerformance.UsedPerformanceCoreTopology);
    Assert.SetEqual([2], clampedPerformance.Mask.CpuIds);

    types[65] = CpuCoreType.Unknown;
    var fallback = AffinityPolicy.Build(allowed, 1, true, cpu => types[cpu]);
    Assert.False(fallback.UsedPerformanceCoreTopology);
    Assert.SetEqual([0, 2, 8], fallback.Mask.CpuIds);

    var noAllowedPerformance = AffinityPolicy.Build(
        CpuAffinityMask.FromCpuIds([0, 8], 16),
        0,
        true,
        _ => CpuCoreType.Efficiency);
    Assert.False(noAllowedPerformance.UsedPerformanceCoreTopology);
    Assert.SetEqual([0, 8], noAllowedPerformance.Mask.CpuIds);
}

static void LinuxCoreTypeAdapter()
{
    var reader = new FakeAffinitySysfsReader("/fake-cpu-root");
    reader.SetCoreType(0, "1\n");
    reader.SetCoreType(1, " 2 ");
    reader.SetCoreType(2, "2");
    reader.SetCoreType(4, "performance");
    reader.SetCoreType(5, "7");
    reader.SetReadFailure(6, new IOException("simulated I/O race"));
    reader.SetReadFailure(7, new UnauthorizedAccessException("simulated access failure"));

    var platform = new LinuxAffinityPlatform(reader.RootPath, reader);

    Assert.Equal(CpuCoreType.Efficiency, platform.GetCoreType(0));
    Assert.Equal(CpuCoreType.Performance, platform.GetCoreType(1));
    Assert.Equal(CpuCoreType.Unknown, platform.GetCoreType(3));
    Assert.Equal(CpuCoreType.Unknown, platform.GetCoreType(4));
    Assert.Equal(CpuCoreType.Unknown, platform.GetCoreType(5));
    Assert.Equal(CpuCoreType.Unknown, platform.GetCoreType(6));
    Assert.Equal(CpuCoreType.Unknown, platform.GetCoreType(7));

    var policy = AffinityPolicy.Build(
        CpuAffinityMask.FromCpuIds([0, 1, 2], 8),
        reservedLogicalCpus: 1,
        preferPerformanceCores: true,
        platform.GetCoreType);
    Assert.True(policy.UsedPerformanceCoreTopology);
    Assert.SetEqual([1], policy.Mask.CpuIds);
}

static void PerTidIntersectionPreservesNarrowMasks()
{
    var platform = new FakeAffinityPlatform
    {
        ThreadIds = [101, 102, 103],
        CurrentTid = 999,
    };
    platform.SetInitial(101, [2, 64, 130]);
    platform.SetInitial(102, [64]);
    platform.SetInitial(103, [1, 64, 130]);

    var controller = new AffinityController(platform);
    var policy = CpuAffinityMask.FromCpuIds([2, 64, 130], 32);
    var summary = controller.ApplyToCurrentThreads(policy, excludedTid: null);

    Assert.Equal(3, summary.EnumeratedThreads);
    Assert.Equal(3, summary.VerifiedThreads);
    Assert.Equal(2, summary.AlreadyCompliantThreads);
    Assert.Equal(1, summary.ChangedThreads);
    Assert.SetEqual([64], platform.Affinities[102].CpuIds);
    Assert.SetEqual([64, 130], platform.Affinities[103].CpuIds);
    Assert.SequenceEqual([103], platform.SetCalls.Select(call => call.Tid));
}

static void PartialFailuresAreAccurate()
{
    var platform = new FakeAffinityPlatform
    {
        ThreadIds = [1, 2, 3, 4, 5, 6],
        CurrentTid = 99,
    };
    platform.SetInitial(1, [0, 2]);
    platform.GetFailures[2] = AffinityErrno.NoSuchProcess;
    platform.SetInitial(3, [0, 2]);
    platform.SetFailures[3] = 1;
    platform.SetInitial(4, [8]);
    platform.SetInitial(5, [0, 2]);
    platform.ReadbackOverrides[5] = CpuAffinityMask.FromCpuIds([0], 8);
    platform.SetInitial(6, [0]);

    var controller = new AffinityController(platform);
    var summary = controller.ApplyToCurrentThreads(CpuAffinityMask.FromCpuIds([2], 8), excludedTid: 6);

    Assert.Equal(5, summary.EnumeratedThreads);
    Assert.Equal(1, summary.ChangedThreads);
    Assert.Equal(1, summary.RacedThreads);
    Assert.Equal(1, summary.FailedThreads);
    Assert.Equal(1, summary.NoOverlapThreads);
    Assert.Equal(1, summary.ReadbackMismatchThreads);
    Assert.Equal(1, summary.VerifiedThreads);
}

static void ReconciliationAppliesNewThreads()
{
    var platform = new FakeAffinityPlatform
    {
        ThreadIds = [10, 999],
        CurrentTid = 999,
    };
    platform.SetInitial(10, [1, 2]);
    platform.SetInitial(999, [1, 2]);

    var scheduler = new FakeScheduler();
    using var reconciler = new AffinityReconciler(
        new AffinityController(platform),
        CpuAffinityMask.FromCpuIds([1], 8),
        scheduler,
        TimeSpan.FromSeconds(30));

    reconciler.Start();
    scheduler.FireCurrentCallback();

    Assert.SetEqual([1], platform.Affinities[10].CpuIds);
    Assert.SetEqual([1, 2], platform.Affinities[999].CpuIds);
    Assert.SequenceEqual([10], platform.SetCalls.Select(call => call.Tid));
    Assert.Equal(2, scheduler.ScheduleCount);
}

static void ReconciliationLifecycle()
{
    var platform = new BlockingAffinityPlatform();
    var controller = new AffinityController(platform);
    var scheduler = new FakeScheduler();
    using var reconciler = new AffinityReconciler(
        controller,
        CpuAffinityMask.FromCpuIds([1], 8),
        scheduler,
        TimeSpan.FromSeconds(30));

    reconciler.Start();
    Assert.Equal(1, scheduler.ScheduleCount);
    Assert.Equal(TimeSpan.FromSeconds(30), scheduler.Delays.Single());

    var running = Task.Run(() => reconciler.TryReconcileNow());
    Assert.True(platform.Entered.Wait(TimeSpan.FromSeconds(5)), "first reconciliation did not start");
    Assert.False(reconciler.TryReconcileNow());

    reconciler.Shutdown();
    platform.Release.Set();
    Assert.True(running.GetAwaiter().GetResult());
    Assert.Equal(1, scheduler.ScheduleCount);

    scheduler.FireDisposedCallback();
    Assert.Equal(1, scheduler.ScheduleCount);
    Assert.Equal(1, platform.EnumerationCount);
}

internal sealed class FakeAffinitySysfsReader : IAffinitySysfsReader
{
    private readonly Dictionary<string, string> contents = [];
    private readonly Dictionary<string, Exception> failures = [];

    public FakeAffinitySysfsReader(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public void SetCoreType(int cpuId, string value)
    {
        contents[PathFor(cpuId)] = value;
    }

    public void SetReadFailure(int cpuId, Exception exception)
    {
        string path = PathFor(cpuId);
        contents[path] = string.Empty;
        failures[path] = exception;
    }

    public bool FileExists(string path) => contents.ContainsKey(path);

    public string ReadAllText(string path)
    {
        if (failures.TryGetValue(path, out Exception? failure))
            throw failure;
        return contents[path];
    }

    private string PathFor(int cpuId) => Path.Combine(RootPath, $"cpu{cpuId}", "topology", "core_type");
}

internal sealed class FakeNativeAffinityCalls : IAffinityNativeCalls
{
    private readonly int requiredSize;
    private readonly byte[] mask;
    private readonly int undersizeErrno;

    public FakeNativeAffinityCalls(int requiredSize, byte[] mask, int undersizeErrno = AffinityErrno.InvalidArgument)
    {
        this.requiredSize = requiredSize;
        this.mask = mask;
        this.undersizeErrno = undersizeErrno;
    }

    public List<int> GetSizes { get; } = [];

    public int GetAffinity(int tid, byte[] destination, out int errno)
    {
        GetSizes.Add(destination.Length);
        if (destination.Length < requiredSize)
        {
            errno = undersizeErrno;
            return -1;
        }

        Array.Copy(mask, destination, mask.Length);
        errno = 0;
        return 0;
    }

    public int SetAffinity(int tid, byte[] mask, out int errno)
    {
        errno = 0;
        return 0;
    }
}

internal class FakeAffinityPlatform : IAffinityPlatform
{
    private int? readbackTid;

    public IReadOnlyList<int> ThreadIds { get; set; } = [];
    public int CurrentTid { get; set; }
    public Dictionary<int, CpuAffinityMask> Affinities { get; } = [];
    public Dictionary<int, int> GetFailures { get; } = [];
    public Dictionary<int, int> SetFailures { get; } = [];
    public Dictionary<int, CpuAffinityMask> ReadbackOverrides { get; } = [];
    public List<(int Tid, CpuAffinityMask Mask)> SetCalls { get; } = [];

    public void SetInitial(int tid, IEnumerable<int> cpus) => Affinities[tid] = CpuAffinityMask.FromCpuIds(cpus, 32);

    public virtual IReadOnlyList<int> GetThreadIds() => ThreadIds;
    public virtual int GetCurrentThreadId() => CurrentTid;
    public virtual CpuCoreType GetCoreType(int cpuId) => CpuCoreType.Unknown;

    public virtual bool TryGetAffinity(int tid, out CpuAffinityMask mask, out int errno)
    {
        if (readbackTid == tid && ReadbackOverrides.TryGetValue(tid, out var readback))
        {
            readbackTid = null;
            mask = readback;
            errno = 0;
            return true;
        }

        if (GetFailures.TryGetValue(tid, out errno))
        {
            mask = CpuAffinityMask.Empty;
            return false;
        }

        mask = Affinities[tid];
        errno = 0;
        return true;
    }

    public virtual bool TrySetAffinity(int tid, CpuAffinityMask mask, out int errno)
    {
        SetCalls.Add((tid, mask));
        if (SetFailures.TryGetValue(tid, out errno))
            return false;

        Affinities[tid] = mask;
        readbackTid = tid;
        errno = 0;
        return true;
    }
}

internal sealed class BlockingAffinityPlatform : FakeAffinityPlatform
{
    public ManualResetEventSlim Entered { get; } = new(false);
    public ManualResetEventSlim Release { get; } = new(false);
    public int EnumerationCount { get; private set; }

    public override IReadOnlyList<int> GetThreadIds()
    {
        EnumerationCount++;
        Entered.Set();
        Release.Wait(TimeSpan.FromSeconds(5));
        return [];
    }
}

internal sealed class FakeScheduler : IAffinityScheduler
{
    private Action? lastCallback;
    private bool disposed;

    public int ScheduleCount { get; private set; }
    public List<TimeSpan> Delays { get; } = [];

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        ScheduleCount++;
        Delays.Add(delay);
        lastCallback = callback;
        disposed = false;
        return new CallbackDisposable(() => disposed = true);
    }

    public void FireCurrentCallback()
    {
        Assert.False(disposed, "scheduled callback was already disposed");
        disposed = true;
        lastCallback?.Invoke();
    }

    public void FireDisposedCallback()
    {
        Assert.True(disposed, "scheduled callback was not disposed");
        lastCallback?.Invoke();
    }

    private sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

internal static class Assert
{
    public static void True(bool value, string? message = null)
    {
        if (!value)
            throw new InvalidOperationException(message ?? "expected true");
    }

    public static void False(bool value, string? message = null) => True(!value, message ?? "expected false");

    public static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected}, got {actual}");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }

    public static void SetEqual(IEnumerable<int> expected, IEnumerable<int> actual)
    {
        var expectedSet = expected.Order().ToArray();
        var actualSet = actual.Order().ToArray();
        SequenceEqual(expectedSet, actualSet);
    }
}
