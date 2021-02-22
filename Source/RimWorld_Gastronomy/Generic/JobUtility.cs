using Gastronomy.Dining;
using Gastronomy.Restaurant;
using Verse;
using Verse.AI;

namespace Gastronomy
{
    internal static class JobUtility
    {
        public static T FailOnDangerous<T>(this T f) where T : IJobEndable
        {
            JobCondition OnRegionDangerous()
            {
                Pawn pawn = f.GetActor();
                var check = RestaurantUtility.IsRegionDangerous(pawn, pawn.GetRegion());
                if (!check) return JobCondition.Ongoing;
                Log.Message($"{pawn.NameShortColored} failed {pawn.CurJobDef.label} because of danger ({pawn.GetRegion().DangerFor(pawn)})");
                return JobCondition.Incompletable;
            }

            f.AddEndCondition(OnRegionDangerous);
            return f;
        }

        public static T FailOnDurationExpired<T>(this T f) where T : IJobEndable
        {
            JobCondition OnDurationExpired()
            {
                var pawn = f.GetActor();
                if (pawn.jobs.curDriver.ticksLeftThisToil > 0) return JobCondition.Ongoing;
                Log.Message($"{pawn.NameShortColored} failed {pawn.CurJobDef?.label} because of timeout.");
                return JobCondition.Incompletable;
            }

            f.AddEndCondition(OnDurationExpired);
            return f;
        }

        public static T GetDriver<T>(this Pawn patron) where T : JobDriver
        {
            // Current
            return patron?.jobs?.curDriver as T;
            // It's not possible to get a driver from queue (only jobs are queued)
        }
    }
}
