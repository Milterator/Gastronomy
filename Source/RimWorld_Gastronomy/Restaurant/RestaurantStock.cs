using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gastronomy.Dining;
using JetBrains.Annotations;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Gastronomy.Restaurant
{
    public class RestaurantStock : IExposable
    {
        public class Stock
        {
            public ThingDef def;
            public int ordered;
            [NotNull] public readonly List<Thing> items = new List<Thing>();
        }

        private const float MinOptimality = 100f;
        private const int JoyOptimalityWeight = 300;

        private class ConsumeOptimality
        {
            public Pawn pawn;
            public Thing thing;
            public float value;
        }

        //[NotNull] private readonly List<Thing> stockCache = new List<Thing>();
        [NotNull] private readonly List<ConsumeOptimality> eatOptimalityCache = new List<ConsumeOptimality>();
        [NotNull] private readonly List<ConsumeOptimality> joyOptimalityCache = new List<ConsumeOptimality>();
        [NotNull] private Map Map => Restaurant.map;
        [NotNull] private RestaurantMenu Menu => Restaurant.Menu;
        [NotNull] private RestaurantController Restaurant { get; }
        [NotNull] private readonly Dictionary<ThingDef, Stock> stockCache = new Dictionary<ThingDef, Stock>();
        [NotNull] public IReadOnlyDictionary<ThingDef, Stock> AllStock => stockCache;

        public RestaurantStock([NotNull] RestaurantController restaurant)
        {
            Restaurant = restaurant;
        }

        public void ExposeData() { }

        public bool HasAnyFoodFor([NotNull] Pawn pawn, bool allowDrug)
        {
            //Log.Message($"{pawn.NameShortColored}: HasFoodFor: Defs: {stockCache.Select(item=>item.Value).Count(stock => WillConsume(pawn, allowDrug, stock.def))}");
            return stockCache.Keys.Any(def => WillConsume(pawn, allowDrug, def));
        }

        public class FoodOptimality
        {
            public Thing thing { get; }
            public float optimality { get; }

            public FoodOptimality(Thing thing, float optimality)
            {
                this.thing = thing;
                this.optimality = optimality;
            }
        }

        public Thing GetBestMealFor([NotNull] Pawn pawn, bool allowDrug, bool includeEat = true, bool includeJoy = true)
        {
            var options = GetMealOptions(pawn, allowDrug, includeEat, includeJoy);
            //Log.Message($"{pawn.NameShortColored}: Meal options: {options.GroupBy(o => o.thing.def).Select(o => $"{o.Key.label} ({o.FirstOrDefault()?.optimality:F2})").ToCommaList()}");
            if (options
                .TryMaxBy(def => def.optimality, out var best))
            {
                //Log.Message($"{pawn.NameShortColored}: GetBestMealFor: {best?.thing.LabelCap} with optimality {best?.optimality:F2}");
                return best.thing;
            }
            return null;
        }

        public Thing GetRandomMealFor([NotNull] Pawn pawn, bool allowDrug, bool includeEat = true, bool includeJoy = true)
        {
            if (GetMealOptions(pawn, allowDrug, includeEat, includeJoy)
                .TryRandomElementByWeight(def => def.optimality, out var random))
            {
                //Log.Message($"{pawn.NameShortColored} picked {random.def.label} with a score of {random.optimality}");
                return random.thing;
            }
            return null;
        }

        private IEnumerable<FoodOptimality> GetMealOptions([NotNull] Pawn pawn, bool allowDrug, bool includeEat, bool includeJoy)
        {
            return stockCache.Values
                .Where(stock => WillConsume(pawn, allowDrug, stock.def))
                .Where(stock => CanAfford(pawn, stock.def))
                .SelectMany(stock => stock.items)
                .Where(consumable => Restaurant.Orders.CanBeOrdered(consumable))
                .Select(consumable => new FoodOptimality(consumable, GetMealOptimalityScore(pawn, consumable, includeEat, includeJoy)))
                .Where(def => def.optimality >= MinOptimality);
        }

        private bool CanAfford(Pawn pawn, ThingDef def)
        {
            if (Restaurant.guestPricePercentage <= 0) return true;
            if (!pawn.IsGuest()) return true;
            return pawn.GetSilver() >= def.GetPrice(Restaurant);
        }

        private float GetMealOptimalityScore([NotNull] Pawn pawn, Thing thing, bool includeEat = true, bool includeJoy = true)
        {
            if (thing == null) return 0;
            if (!IsAllowedIfDrug(pawn, thing.def!))
            {
                //Log.Message($"{pawn.NameShortColored}: {thing.LabelCap} Not allowed (drug)");
                return 0;
            }
            //var debugMessage = new StringBuilder($"{pawn.NameShortColored}: {thing.LabelCap} ");

            float score = 0;
            if (includeEat && pawn.needs.food != null)
            {
                var optimality = GetCachedOptimality(pawn, thing, eatOptimalityCache, CalcEatOptimality);
                var factor = NutritionVsNeedFactor(pawn, thing.def);
                score += optimality * factor;
                //debugMessage.Append($"EAT = {optimality:F0} * {factor:F2} ");
            }

            if (includeJoy && pawn.needs.joy != null)
            {
                var optimality = GetCachedOptimality(pawn, thing, joyOptimalityCache, CalcJoyOptimality);
                var factor = JoyVsNeedFactor(pawn, thing.def);
                score += optimality * factor;
                //debugMessage.Append($"JOY = {optimality:F0} * {factor:F2} ");
            }

            //debugMessage.Append($"= {score:F0}");
            //Log.Message(debugMessage.ToString());
            return score;
        }

        private static float CalcEatOptimality([NotNull] Pawn pawn, [NotNull]Thing thing)
        {
            return Mathf.Max(0, FoodUtility.FoodOptimality(pawn, thing, thing.def, IntVec3Utility.ManhattanDistanceFlat(pawn.Position, thing.Position) * 0.5f));
        }

        private static float CalcJoyOptimality([NotNull] Pawn pawn, [NotNull] Thing thing)
        {
            var def = thing.def;
            var toleranceFactor = pawn.needs.joy.tolerances.JoyFactorFromTolerance(def.ingestible.JoyKind);
            var drugCategoryFactor = GetDrugCategoryFactor(def);
            return toleranceFactor * drugCategoryFactor * JoyOptimalityWeight;
        }

        private static float GetDrugCategoryFactor(ThingDef def)
        {
            return def.ingestible.drugCategory switch
            {
                DrugCategory.None => 3.5f,
                DrugCategory.Social => 3.0f,
                DrugCategory.Medical => 1.5f,
                _ => 1.0f
            };
        }

        private static bool IsAllowedIfDrug([NotNull] Pawn pawn, [NotNull] ThingDef def)
        {
            if (!def.IsDrug) return true;
            if (pawn.drugs == null) return true;
            if (pawn.InMentalState) return true;
            if (pawn.IsTeetotaler()) return false;
            if (pawn.story?.traits.DegreeOfTrait(TraitDefOf.DrugDesire) > 0) return true; // Doesn't care about schedule no matter the schedule
            var drugPolicyEntry = pawn.GetPolicyFor(def);
            //Log.Message($"{pawn.NameShortColored} vs {def.label} as drug: for joy = {drugPolicyEntry?.allowedForJoy}");
            if (drugPolicyEntry?.allowedForJoy == false) return false;
            return true;
        }

        private static float GetCachedOptimality(Pawn pawn, [NotNull]Thing thing, [NotNull] List<ConsumeOptimality> optimalityCache, [NotNull] Func<Pawn, Thing, float> calcFunction)
        {
            // Expensive, must be cached
            var optimality = optimalityCache.FirstOrDefault(o => o.pawn == pawn && o.thing == thing);
            if (optimality == null)
            {
                // Optimality can be negative
                optimality = new ConsumeOptimality {pawn = pawn, thing = thing, value = calcFunction(pawn, thing)};
                optimalityCache.Add(optimality);
            }
            // From 0 to 300-400ish
            return optimality.value;
        }

        private static float NutritionVsNeedFactor(Pawn pawn, ThingDef def)
        {
            var need = pawn.needs.food?.NutritionWanted ?? 0;
            if (need < 0.1f) return 0;
            var provided = def.ingestible.CachedNutrition;
            if (provided < 0.01f) return 0;
            var similarity = 1 - Mathf.Abs(need - provided) / need;
            var score = Mathf.Max(0, need * similarity);
            //Log.Message($"{pawn.NameShortColored}: {def.LabelCap} EAT Need = {need:F2} Provided = {provided:F2} Similarity = {similarity:F2} Score = {score:F2}");
            return score;
        }

        private static float JoyVsNeedFactor(Pawn pawn, ThingDef def)
        {
            var need = 1 - pawn.needs.joy?.CurLevelPercentage ?? 0;
            if (def.ingestible.joyKind == null) return 0;
            var score = def.ingestible.joy * need;
            //Log.Message($"{pawn.NameShortColored}: {def.LabelCap} JOY Need = {need:F2} Provided = {def.ingestible.joy:F2} Score = {score:F2}");
            return score;
        }

        private static bool WillConsume(Pawn pawn, bool allowDrug, ThingDef def)
        {
            var result = def != null && (allowDrug || !def.IsDrug) && pawn.WillEat(def);
            //Log.Message($"{pawn.NameShortColored} will consume {def.label}? will eat = {pawn.WillEat(def)}, preferability = {def.ingestible?.preferability}, allowDrug = {allowDrug}, result = {result}");
            return result;
        }

        public Thing GetServableThing(Order order, Pawn pawn)
        {
            if (stockCache.TryGetValue(order.consumableDef, out var stock))
            {
                return stock.items.OrderBy(o => pawn.Position.DistanceToSquared(o.Position))
                    .FirstOrDefault(o => pawn.CanReserveAndReach(o, PathEndMode.Touch, JobUtility.MaxDangerServing, o.stackCount, 1));
            }
            return null;
        }

        public void RareTick()
        {
            // Refresh entire stock
            foreach (var stock in stockCache)
            {
                stock.Value.items.Clear();
                stock.Value.ordered = 0;
            }

            foreach (var thing in Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSource)
                .Where(t => t.def.IsIngestible && !t.def.IsCorpse && Menu.IsOnMenu(t) && !t.IsForbidden(Faction.OfPlayer)))
            {
                if (thing?.def == null) continue;
                if (!stockCache.TryGetValue(thing.def, out var stock))
                {
                    stock = new Stock {def = thing.def};
                    stockCache.Add(thing.def, stock);
                }

                stock.items.Add(thing);
            }

            // Slowly empty optimality caches again
            if (eatOptimalityCache.Count > 0) eatOptimalityCache.RemoveAt(0);
            if (joyOptimalityCache.Count > 0) joyOptimalityCache.RemoveAt(0);
        }

        [NotNull]
        public IReadOnlyCollection<Thing> GetAllStockOfDef(ThingDef def)
        {
            if (!stockCache.TryGetValue(def, out var stock)) return Array.Empty<Thing>();
            return stock.items;
        }

        public bool IsAvailable([NotNull] Thing consumable)
        {
            return stockCache.TryGetValue(consumable.def)?.items.Contains(consumable) == true;
        }
    }
}
