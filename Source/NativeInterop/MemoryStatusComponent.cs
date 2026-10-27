using Verse;

namespace RimWorldLinuxMisc.NativeInterop
{
    public sealed class MemoryStatusComponent : GameComponent
    {
        private int ticksUntilPublish;

        public MemoryStatusComponent(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (--ticksUntilPublish > 0)
            {
                return;
            }

            ticksUntilPublish = 60;
            MemoryOptimizer.PublishPeriodicStatus();
        }
    }
}
