using System.Linq;
using RimWorld;
using Verse;

namespace Restaurant.TableTops
{
    public class TableTop : Building
    {
        public Building Table { get; private set; } // Can't be saved with ExposeData, the reference gets lost

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            InitTable(map);
        }

        private void InitTable(Map map)
        {
            Table = map.thingGrid.ThingsAt(Position)?.OfType<Building>().FirstOrDefault(b => b.def.surfaceType == SurfaceType.Eat);
            if (Table == null) Log.Error($"TableTop has no table at {Position}!");
        }

        public override void PostMapInit()
        {
            base.PostMapInit();
            InitTable(Map);
        }

        public virtual void Notify_BuildingDespawned(Thing thing)
        {
            if (thing == Table)
            {
                Table = null;
                this.Uninstall();
            }
        }
    }
}
