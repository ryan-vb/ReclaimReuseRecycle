using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace DoctorVanGogh.ReclaimReuseRecycle {
    public static class ThingDefGenerator_Reclaimed {

        public static readonly string NonSterileDefNameFormat = @"NonSterile_{0}";

        public static readonly string MangledDefNameFormat = @"Mangled_{0}";

        private static readonly Type[] validHediffs = new[] { typeof(Hediff_AddedPart), typeof(Hediff_Implant) };

        public static Dictionary<ThingDef, PackedThingDef[]> LookupCache;

        [DebuggerHidden]
        public static IEnumerable<ThingDef> ImpliedReclaimableDefs() {
            // can't go ThingDef => isBodyPartOrImplant=true because vanilla "WoodLog" counts as BodyPart.... hrmpf
            var thingDefs = DefDatabase<HediffDef>.AllDefs
                                                  .Where(d => validHediffs.Contains(d.hediffClass)
                                                              && d.spawnThingOnRemoved != null
                                                              && true != d.spawnThingOnRemoved.thingCategories?.Contains(DefReferences.ThingCategory_BodyPartsNatural))
                                                  .Select(d => d.spawnThingOnRemoved)
                                                  .Distinct();
            // prepare recipes/research to have researchproject's techlevel as fallback values
            var recipeThings = DefDatabase<RecipeDef>.AllDefs
                                                     .SelectMany(
                                                         rcd => rcd.products
                                                                   ?.Select(tcc => tcc.thingDef)
                                                                   .Select(td => new {ResearchTechLevel = rcd.researchPrerequisite?.techLevel, Def = td}))
                                                     .GroupBy(t => t.Def)
                                                     .Select(g => new {Def = g.Key, MinResearchTechLevel = g.Where(t => t.ResearchTechLevel != TechLevel.Undefined).Min(t => t.ResearchTechLevel)});        // 'undefined' level doesn't help - strip it out                                                          

            // left outer join hediff things onto research/recipe things
            var candidates = from td in thingDefs
                             join rt in recipeThings
                             on td equals rt.Def into comb
                             from x in comb.DefaultIfEmpty()
                             select new {
                                        Def = td,
                                        x?.MinResearchTechLevel
                                    };
          

            foreach (var t in candidates.ToList()) {
                yield return GenerateImpliedNonSterileDef(t.Def, t.MinResearchTechLevel);
                yield return GenerateImpliedMangledDef(t.Def, t.MinResearchTechLevel);
            }
        }

        private static PackedThingDef GenerateImpliedNonSterileDef(ThingDef t, TechLevel? researchTechLevel = null) {
            return GenerateImpliedPackedDef(
                t,
                NonSterileDefNameFormat,
                LanguageKeys.r3.R3_NonSterile_Label,
                LanguageKeys.r3.R3_NonSterile_Description,
                "Things/Item/BodyPart/NonSterile",
                ReclamationType.NonSterile,
                researchTechLevel);
        }

        private static PackedThingDef GenerateImpliedMangledDef(ThingDef t, TechLevel? researchTechLevel = null) {
            return GenerateImpliedPackedDef(
                t,
                MangledDefNameFormat,
                LanguageKeys.r3.R3_Mangled_Label,
                LanguageKeys.r3.R3_Mangled_Description,
                "Things/Item/BodyPart/Mangled",
                ReclamationType.Mangled,
                researchTechLevel);
        }

        private static PackedThingDef GenerateImpliedPackedDef(ThingDef t, string defFormat, string labelKey, string descriptionKey, string texPath,
                                                               ReclamationType type, TechLevel? researchTechlevel = null) {
            PackedThingDef d = new PackedThingDef {
                                   thingClass = typeof(PackedThing),
                                   defName = String.Format(CultureInfo.InvariantCulture, defFormat, t.defName),
                                   label = labelKey.Translate(t.LabelCap),
                                   description = descriptionKey.Translate(t.LabelCap),
                                   graphicData = new GraphicData {
                                                     texPath = texPath,
                                                     graphicClass = typeof(Graphic_Single)
                                                 },
                                   category = ThingCategory.Item,
                                   useHitPoints = true,
                                   selectable = true,
                                   altitudeLayer = AltitudeLayer.Item,
                                   tickerType = TickerType.Never,
                                   alwaysHaulable = true,
                                   isBodyPartOrImplant = false,
                                   tradeTags = new List<string>(),
                                   comps = new List<CompProperties> {
                                               new CompProperties_Forbiddable()
                                           },
                                   thingCategories = new List<ThingCategoryDef>(),
                                   pathCost = 10,
                                   techHediffsTags = t.techHediffsTags != null ? new List<string>(t.techHediffsTags) : null,
                                   statBases = new List<StatModifier>(),
                                   SpawnOnUnpack = t,
                                   ReclamationType = type,                                  
                               };

            d.statBases.Add(new StatModifier {stat = DefReferences.Stat_MaxHitPoints, value = 50f});
            d.statBases.Add(new StatModifier {stat = DefReferences.Stat_DeteriorationRate, value = 2f});
            d.statBases.Add(new StatModifier {stat = DefReferences.Stat_Beauty, value = -8f});
            var value = t.statBases.FirstOrDefault(sb => sb.stat == DefReferences.Stat_MarketValue)?.value;
            if (value != null)
                d.statBases.Add(new StatModifier {stat = DefReferences.Stat_MarketValue, value = value.Value});
            d.statBases.Add(new StatModifier {stat = DefReferences.Stat_Mass, value = t.statBases.FirstOrDefault(sb => sb.stat == DefReferences.Stat_Mass)?.value ?? 0.2f});

            d.Complexity = GetComplexity(d, value, d.techLevel, researchTechlevel);

            DirectXmlCrossRefLoader.RegisterListWantsCrossRef(d.thingCategories, GetThingCategoryDef(t, d.Complexity, type).defName);                 // because.... "magic"

            return d;
        }

        private static ThingCategoryDef GetThingCategoryDef(ThingDef t, Complexity complexity, ReclamationType type) {
            switch (type) {
                case ReclamationType.NonSterile:
                    switch (complexity) {
                        case Complexity.Primitive:
                            return DefReferences.ThingCategory_BodyPartsNonSterile_Primitive;
                        case Complexity.Advanced:
                            return DefReferences.ThingCategory_BodyPartsNonSterile_Advanced;
                        case Complexity.Glittertech:
                            return DefReferences.ThingCategory_BodyPartsNonSterile_Glittertech;
                        default:
                            Util.Warning($"Unknown complexity {complexity} used for {t.LabelCap}.");
                            return DefReferences.ThingCategory_BodyPartsNonSterile;
                    }
                case ReclamationType.Mangled:
                    switch (complexity) {
                        case Complexity.Primitive:
                            return DefReferences.ThingCategory_BodyPartsMangled_Primitive;
                        case Complexity.Advanced:
                            return DefReferences.ThingCategory_BodyPartsMangled_Advanced;
                        case Complexity.Glittertech:
                            return DefReferences.ThingCategory_BodyPartsMangled_Glittertech;
                        default:
                            Util.Warning($"Unknown complexity {complexity} used for {t.LabelCap}.");
                            return DefReferences.ThingCategory_BodyPartsMangled;
                    }
                default:
                    Util.Warning($"Unknown reclamation type {type} used for {t.LabelCap}.");
                    return DefReferences.ThingCategory_BodyPartsReclaimed;
            }

        }

        private static Complexity GetComplexity(ThingDef d, float? value, params TechLevel?[] techLevels) {
            if (d.techHediffsTags?.Contains("Advanced") == true)            // ex: vanilla bionics
                return Complexity.Glittertech;
            if (d.techHediffsTags?.Contains("Simple") == true)              // ex: vanilla 'simple' prosthetics
                return Complexity.Advanced;
            if (d.techHediffsTags?.Contains("Poor") == true)                // ex: vanilla peg legs/denture
                return Complexity.Primitive;

            var tech = (TechLevel?) techLevels.Where(tl => tl != TechLevel.Undefined).Cast<byte?>().Min();          // min non 'undefined' techlevel

            switch (tech) {
                case TechLevel.Animal:
                case TechLevel.Neolithic:
                case TechLevel.Medieval:
                    return Complexity.Primitive;
                case TechLevel.Industrial:
                case TechLevel.Spacer:
                    return Complexity.Advanced;
                case TechLevel.Ultra:
                case TechLevel.Transcendent:
                    return Complexity.Glittertech;
                case TechLevel.Undefined:
                case null:
                default:
                    if (value == null) {
                        Util.Warning($"{d.LabelCap} has no discernable or undefined techlevel, no techHediffsTags and no market value - defaulting to max Complexity.");
                        return Complexity.Glittertech;
                    }
                    if (value >= 1500)              // vanilla power claw price
                        return Complexity.Glittertech;
                    if (value >= 400)               // vanilla simple prosthetics price
                        return Complexity.Advanced;
                    return Complexity.Primitive;
            }

        }

        internal static PackedThingDef GetExtractableDef(ThingDef def, float hitpointsFactor) {
            ReclamationType? t = null;

            if (Settings.NonSterileRange.IncludesEpsilon(hitpointsFactor))
                t = ReclamationType.NonSterile;
            else if (Settings.MangledRange.IncludesEpsilon(hitpointsFactor))
                t = ReclamationType.Mangled;


            if (t == null)
                return null;

            PackedThingDef[] elements;
            return LookupCache.TryGetValue(def, out elements) 
                ? elements.FirstOrDefault(p => p.ReclamationType == t.Value) 
                : null;
        }
    }
}