using Verse;

namespace RimWorldLinuxMisc
{
    public class LinuxMiscSettings : ModSettings
    {
        public bool enablePeriodicMadvise = true;
        public int periodicMadviseInterval = 30000;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enablePeriodicMadvise, "enablePeriodicMadvise", true);
            Scribe_Values.Look(ref periodicMadviseInterval, "periodicMadviseInterval", 30000);
            base.ExposeData();
        }

        public int GetPeriodicInterval()
        {
            if (!enablePeriodicMadvise || periodicMadviseInterval <= 0)
            {
                return 0;
            }

            return periodicMadviseInterval;
        }
    }
}
