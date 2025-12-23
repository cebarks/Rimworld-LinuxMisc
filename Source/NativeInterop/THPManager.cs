using System;
using System.IO;
using System.Linq;
using System.Threading;
using Verse;

namespace RimWorldLinuxMisc.NativeInterop
{
    internal static class THPManager
    {
        private const string THP_ENABLED_PATH = "/sys/kernel/mm/transparent_hugepage/enabled";
        private const string PROC_SELF_MAPS = "/proc/self/maps";
        private static Timer periodicTimer = null;

        public static bool EnableTHP()
        {
            try
            {
                if (!LinuxSyscalls.IsLinux())
                {
                    Log.Message("[LinuxMisc] Not running on Linux, skipping THP enablement");
                    return false;
                }

                if (!IsThpAvailable())
                {
                    Log.Warning("[LinuxMisc] Transparent Huge Pages not available on this system");
                    return false;
                }

                bool inMadviseMode = IsThpInMadviseMode();

                if (inMadviseMode)
                {
                    Log.Message("[LinuxMisc] THP is in madvise mode, using madvise approach");

                    if (TryEnableViaMadvise())
                    {
                        Log.Message("[LinuxMisc] Successfully enabled THP via madvise at startup");
                        SchedulePeriodicMadvise();
                        return true;
                    }
                    else
                    {
                        Log.Warning("[LinuxMisc] Failed to enable THP via madvise at startup");
                        SchedulePeriodicMadvise();
                        return false;
                    }
                }

                if (LinuxSyscalls.TryEnableTHPViaPrctl())
                {
                    Log.Message("[LinuxMisc] Successfully enabled THP via prctl");
                    return true;
                }

                int prctlErrno = LinuxSyscalls.GetLastErrno();
                Log.Warning($"[LinuxMisc] prctl(PR_SET_THP_DISABLE) failed with errno={prctlErrno}, trying madvise fallback");

                if (TryEnableViaMadvise())
                {
                    Log.Message("[LinuxMisc] Successfully enabled THP via madvise");
                    return true;
                }

                Log.Warning("[LinuxMisc] Failed to enable THP via both prctl and madvise");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[LinuxMisc] Exception while enabling THP: {ex}");
                return false;
            }
        }

        private static bool IsThpAvailable()
        {
            try
            {
                if (!File.Exists(THP_ENABLED_PATH))
                {
                    return false;
                }

                string content = File.ReadAllText(THP_ENABLED_PATH).Trim();

                if (content.Contains("[never]"))
                {
                    return false;
                }

                if (content.Contains("[always]") || content.Contains("[madvise]"))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Warning($"[LinuxMisc] Failed to check THP availability: {ex.Message}");
                return false;
            }
        }

        private static bool IsThpInMadviseMode()
        {
            try
            {
                if (!File.Exists(THP_ENABLED_PATH))
                {
                    return false;
                }

                string content = File.ReadAllText(THP_ENABLED_PATH).Trim();
                return content.Contains("[madvise]");
            }
            catch
            {
                return false;
            }
        }

        private static bool TryEnableViaMadvise(bool isPeriodicExecution = false)
        {
            try
            {
                if (!File.Exists(PROC_SELF_MAPS))
                {
                    return false;
                }

                var maps = File.ReadAllLines(PROC_SELF_MAPS);
                int successCount = 0;
                int totalRegions = 0;

                foreach (var line in maps)
                {
                    if (!ShouldApplyMadviseToRegion(line))
                        continue;

                    totalRegions++;

                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 1)
                        continue;

                    var addressRange = parts[0];
                    var addresses = addressRange.Split('-');
                    if (addresses.Length != 2)
                        continue;

                    if (!TryParseHex(addresses[0], out ulong startAddr) ||
                        !TryParseHex(addresses[1], out ulong endAddr))
                        continue;

                    ulong pageSize = 4096;
                    ulong alignedStart = (startAddr / pageSize) * pageSize;
                    ulong length = endAddr - alignedStart;

                    if (length == 0)
                        continue;

                    IntPtr addr = new IntPtr((long)alignedStart);
                    UIntPtr len = new UIntPtr(length);

                    if (LinuxSyscalls.TryEnableTHPViaMadvise(addr, len))
                    {
                        successCount++;
                    }
                }

                if (successCount > 0)
                {
                    // Only log if not periodic execution, or if verbose logging is enabled
                    if (!isPeriodicExecution || LinuxMiscMod.GetSettings().verbosePeriodicLogging)
                    {
                        Log.Message($"[LinuxMisc] Applied madvise(MADV_HUGEPAGE) to {successCount}/{totalRegions} memory regions");
                    }
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Warning($"[LinuxMisc] madvise approach failed: {ex.Message}");
                return false;
            }
        }

        private static bool ShouldApplyMadviseToRegion(string mapLine)
        {
            if (string.IsNullOrWhiteSpace(mapLine))
                return false;

            var parts = mapLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return false;

            string permissions = parts[1];

            if (!permissions.Contains("w"))
                return false;

            bool isAnonymous = parts.Length < 6 || string.IsNullOrWhiteSpace(parts[5]);

            if (parts.Length >= 6)
            {
                string pathname = parts[5];
                if (pathname == "[heap]" || pathname == "[stack]")
                    return true;
            }

            return isAnonymous;
        }

        private static bool TryParseHex(string hex, out ulong value)
        {
            try
            {
                value = Convert.ToUInt64(hex, 16);
                return true;
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private static void SchedulePeriodicMadvise()
        {
            try
            {
                var settings = LinuxMiscMod.GetSettings();
                int interval = settings.GetPeriodicInterval();

                if (interval <= 0)
                {
                    Log.Message("[LinuxMisc] Periodic madvise disabled in settings");
                    return;
                }

                Log.Message($"[LinuxMisc] Scheduled periodic madvise every {interval}ms");

                periodicTimer?.Dispose();
                periodicTimer = new Timer(_ =>
                {
                    try
                    {
                        if (LinuxMiscMod.GetSettings().verbosePeriodicLogging)
                        {
                            Log.Message("[LinuxMisc] Applying madvise to memory regions (periodic execution)");
                        }
                        TryEnableViaMadvise(isPeriodicExecution: true);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[LinuxMisc] Periodic madvise failed: {ex.Message}");
                    }
                }, null, interval, interval);
            }
            catch (Exception ex)
            {
                Log.Warning($"[LinuxMisc] Failed to schedule periodic madvise: {ex.Message}");
            }
        }
    }
}
