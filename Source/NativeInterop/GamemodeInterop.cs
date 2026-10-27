using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal interface IGamemodeClient
    {
        bool TryRequestStart(out string error);
        bool TryRequestEnd(out string error);
    }

    internal sealed class NativeGamemodeClient : IGamemodeClient
    {
        public bool TryRequestStart(out string error) => GamemodeInterop.TryRequestStart(out error);
        public bool TryRequestEnd(out string error) => GamemodeInterop.TryRequestEnd(out error);
    }

    internal static class GamemodeInterop
    {
        internal const string ShimFileName = "liblinuxmisc_gamemode.so";

        private static readonly object Sync = new object();
        private static bool loadAttempted;
        private static IntPtr libraryHandle;
        private static string loadError;
        private static OperationDelegate requestStart;
        private static OperationDelegate requestEnd;
        private static OperationDelegate queryStatus;
        private static ErrorCopyDelegate copyError;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int OperationDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate UIntPtr ErrorCopyDelegate(IntPtr buffer, UIntPtr bufferSize);

        internal static bool IsSupportedPlatform(out string reason)
        {
            if (!OperatingSystem.IsLinux())
            {
                reason = "GameMode integration is only available on Linux";
                return false;
            }

            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                reason = $"GameMode integration has no native shim for {RuntimeInformation.ProcessArchitecture}";
                return false;
            }

            reason = null;
            return true;
        }

        internal static bool IsLibraryAvailable()
        {
            lock (Sync)
            {
                return EnsureLoadedLocked(out _);
            }
        }

        internal static bool TryRequestStart(out string error)
        {
            lock (Sync)
            {
                if (!EnsureLoadedLocked(out error))
                {
                    return false;
                }
                return TryOperationLocked(requestStart, "start", requireZero: true, out _, out error);
            }
        }

        internal static bool TryRequestEnd(out string error)
        {
            lock (Sync)
            {
                if (!EnsureLoadedLocked(out error))
                {
                    return false;
                }
                return TryOperationLocked(requestEnd, "end", requireZero: true, out _, out error);
            }
        }

        internal static bool TryQueryStatus(out int status, out string error)
        {
            lock (Sync)
            {
                if (!EnsureLoadedLocked(out error))
                {
                    status = -1;
                    return false;
                }
                return TryOperationLocked(queryStatus, "query status", requireZero: false, out status, out error);
            }
        }

        private static bool TryOperationLocked(
            OperationDelegate operation,
            string operationName,
            bool requireZero,
            out int result,
            out string error)
        {
            result = -1;
            try
            {
                result = operation();
                if ((requireZero && result == 0) || (!requireZero && result >= 0))
                {
                    error = null;
                    return true;
                }

                error = ReadNativeErrorLocked();
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = $"GameMode {operationName} failed without an error message";
                }
                return false;
            }
            catch (Exception ex)
            {
                error = $"GameMode {operationName} call failed: {ex.Message}";
                return false;
            }
        }

        private static bool EnsureLoadedLocked(out string error)
        {
            if (libraryHandle != IntPtr.Zero)
            {
                error = null;
                return true;
            }

            if (loadAttempted)
            {
                error = loadError;
                return false;
            }

            loadAttempted = true;
            if (!IsSupportedPlatform(out loadError))
            {
                error = loadError;
                return false;
            }

            string assemblyLocation = typeof(GamemodeInterop).GetTypeInfo().Assembly.Location;
            string assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrEmpty(assemblyDirectory))
            {
                loadError = $"Cannot determine the mod assembly directory for {ShimFileName}";
                error = loadError;
                return false;
            }

            string shimPath = Path.Combine(assemblyDirectory, ShimFileName);
            try
            {
                libraryHandle = NativeLibrary.Load(shimPath);
                requestStart = LoadExport<OperationDelegate>(libraryHandle, "linuxmisc_gamemode_start");
                requestEnd = LoadExport<OperationDelegate>(libraryHandle, "linuxmisc_gamemode_end");
                queryStatus = LoadExport<OperationDelegate>(libraryHandle, "linuxmisc_gamemode_status");
                copyError = LoadExport<ErrorCopyDelegate>(libraryHandle, "linuxmisc_gamemode_error_copy");
                loadError = null;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                if (libraryHandle != IntPtr.Zero)
                {
                    try
                    {
                        NativeLibrary.Free(libraryHandle);
                    }
                    catch
                    {
                        // Preserve the original loader failure for startup diagnostics.
                    }
                    libraryHandle = IntPtr.Zero;
                }

                requestStart = null;
                requestEnd = null;
                queryStatus = null;
                copyError = null;
                loadError = $"Unable to load GameMode shim '{shimPath}': {ex.Message}";
                error = loadError;
                return false;
            }
        }

        private static T LoadExport<T>(IntPtr handle, string name) where T : Delegate
        {
            IntPtr address = NativeLibrary.GetExport(handle, name);
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        private static string ReadNativeErrorLocked()
        {
            UIntPtr required = copyError(IntPtr.Zero, UIntPtr.Zero);
            const ulong maximumErrorBytes = 16UL * 1024UL;
            ulong requiredBytes = required.ToUInt64();
            ulong requested = requiredBytes >= maximumErrorBytes
                ? maximumErrorBytes
                : requiredBytes + 1UL;

            IntPtr buffer = Marshal.AllocHGlobal(checked((int)requested));
            try
            {
                copyError(buffer, new UIntPtr(requested));
                return Marshal.PtrToStringUTF8(buffer) ?? string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
