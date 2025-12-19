using System;
using System.Runtime.InteropServices;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal static class GamemodeInterop
    {
        private const string LIBGAMEMODE = "libgamemode.so.0";

        [DllImport(LIBGAMEMODE, EntryPoint = "real_gamemode_request_start")]
        private static extern int gamemode_request_start();

        [DllImport(LIBGAMEMODE, EntryPoint = "real_gamemode_request_end")]
        private static extern int gamemode_request_end();

        [DllImport(LIBGAMEMODE, EntryPoint = "real_gamemode_error_string", CharSet = CharSet.Ansi)]
        private static extern IntPtr gamemode_error_string();

        [DllImport(LIBGAMEMODE, EntryPoint = "real_gamemode_query_status")]
        private static extern int gamemode_query_status();

        public static bool TryRequestStart(out string error)
        {
            try
            {
                int result = gamemode_request_start();
                if (result == 0)
                {
                    error = null;
                    return true;
                }

                error = GetErrorString();
                return false;
            }
            catch (DllNotFoundException)
            {
                error = "libgamemode.so.0 not found";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Unexpected error: {ex.Message}";
                return false;
            }
        }

        public static bool TryRequestEnd(out string error)
        {
            try
            {
                int result = gamemode_request_end();
                if (result == 0)
                {
                    error = null;
                    return true;
                }

                error = GetErrorString();
                return false;
            }
            catch (DllNotFoundException)
            {
                error = "libgamemode.so.0 not found";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Unexpected error: {ex.Message}";
                return false;
            }
        }

        public static bool IsLibraryAvailable()
        {
            try
            {
                gamemode_query_status();
                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static string GetErrorString()
        {
            try
            {
                IntPtr errorPtr = gamemode_error_string();
                if (errorPtr == IntPtr.Zero)
                {
                    return "Unknown error";
                }

                return Marshal.PtrToStringAnsi(errorPtr) ?? "Unknown error";
            }
            catch
            {
                return "Unknown error";
            }
        }
    }
}
