using System;
using RimWorldLinuxMisc.NativeInterop;

internal static class Program
{
    private sealed class FakeClient : IGamemodeClient
    {
        internal bool StartSucceeds = true;
        internal bool EndSucceeds = true;
        internal int StartCalls;
        internal int EndCalls;

        public bool TryRequestStart(out string error)
        {
            StartCalls++;
            error = StartSucceeds ? null : "start failed";
            return StartSucceeds;
        }

        public bool TryRequestEnd(out string error)
        {
            EndCalls++;
            error = EndSucceeds ? null : "end failed";
            return EndSucceeds;
        }
    }

    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Expected one test mode");
            return 2;
        }

        try
        {
            switch (args[0])
            {
                case "session":
                    TestSessionLifecycle();
                    break;
                case "missing":
                    Expect(!GamemodeInterop.TryRequestStart(out string missingError), "missing shim unexpectedly started");
                    ExpectContains(missingError, "liblinuxmisc_gamemode.so");
                    break;
                case "partial":
                    Expect(!GamemodeInterop.TryRequestStart(out string partialError), "partial shim unexpectedly started");
                    ExpectContains(partialError, "linuxmisc_gamemode_end");
                    break;
                case "native-success":
                    Expect(GamemodeInterop.TryRequestStart(out string startError), startError);
                    Expect(GamemodeInterop.TryQueryStatus(out int status, out string statusError), statusError);
                    Expect(status == 2, $"expected status 2, got {status}");
                    Expect(GamemodeInterop.TryRequestEnd(out string endError), endError);
                    break;
                case "native-start-failure":
                    Expect(!GamemodeInterop.TryRequestStart(out string failureError), "failing fake host unexpectedly started");
                    ExpectContains(failureError, "fake start failure");
                    break;
                case "native-old-status":
                    Expect(GamemodeInterop.TryRequestStart(out string oldStartError), oldStartError);
                    Expect(!GamemodeInterop.TryQueryStatus(out _, out string oldStatusError), "old fake host unexpectedly reported status");
                    ExpectContains(oldStatusError, "gamemode_query_status missing");
                    Expect(GamemodeInterop.TryRequestEnd(out string oldEndError), oldEndError);
                    break;
                default:
                    throw new InvalidOperationException("Unknown test mode: " + args[0]);
            }

            Console.WriteLine($"PASS: {args[0]}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void TestSessionLifecycle()
    {
        var client = new FakeClient();
        var session = new GamemodeSession(client);

        Expect(session.TryActivate(out string firstError), firstError);
        Expect(session.TryActivate(out string duplicateError), duplicateError);
        Expect(client.StartCalls == 1, "duplicate activation called native start more than once");
        Expect(session.IsActive, "session did not record active state");

        client.EndSucceeds = false;
        Expect(!session.TryDeactivate(out string failedEndError), "failed end unexpectedly succeeded");
        ExpectContains(failedEndError, "end failed");
        Expect(session.IsActive, "failed end discarded active state and prevented retry");

        client.EndSucceeds = true;
        Expect(session.TryDeactivate(out string endError), endError);
        Expect(session.TryDeactivate(out string duplicateEndError), duplicateEndError);
        Expect(client.EndCalls == 2, "idempotent end made an unexpected native call");
        Expect(!session.IsActive, "session remained active after successful end");

        client.StartSucceeds = false;
        Expect(!session.TryActivate(out string failedStartError), "failed start unexpectedly succeeded");
        ExpectContains(failedStartError, "start failed");
        Expect(!session.IsActive, "failed start marked session active");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message ?? "expectation failed");
        }
    }

    private static void ExpectContains(string actual, string expected)
    {
        if (actual == null || actual.IndexOf(expected, StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'");
        }
    }
}
