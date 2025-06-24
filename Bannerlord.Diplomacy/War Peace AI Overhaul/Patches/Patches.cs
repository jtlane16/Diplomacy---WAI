using Diplomacy.Extensions;

using HarmonyLib;

using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Linq;
using System.Reflection;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Library;
using TaleWorlds.Localization;

using WarAndAiTweaks.AI;
using WarAndAiTweaks.AI.Behaviors;

namespace Diplomacy.War_Peace_AI_Overhaul
{
    internal class Patches
    {
        [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomWarDecision")]
        public class Patch_DisableRandomWar { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

        [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomPeaceDecision")]
        public class Patch_DisableRandomPeace { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

        [HarmonyPatch(typeof(Building), "GetBuildingEffectAmount")]
        public class MilitiaPatch
        {
            static void Postfix(Building __instance, BuildingEffectEnum effect, ref float __result)
            {
                if (effect == BuildingEffectEnum.Militia && __instance.Name.ToString() == "Militia Grounds")
                {
                    if (__instance.Town.IsCastle) { __result = __result + 5; }
                    if (__instance.Town.IsTown) { __result = __result + 10; }
                }
                return;
            }
        }
        [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnDeclarePeace")]
        public class KingdomPlayerPeacePatch
        {
            private static bool WantsPeace(Kingdom us, Kingdom them)
            {
                var peaceEvaluator = new StrategicAI.DefaultPeaceEvaluator();
                var peaceScore = peaceEvaluator.GetPeaceScore(us, them).ResultNumber;
                return peaceScore > 30f;
            }

            public static bool Prefix(KingdomWarItemVM item)
            {
                var playerKingdom = Hero.MainHero.Clan.Kingdom;
                var targetKingdom = item.Faction2 as Kingdom;
                if (playerKingdom == null || targetKingdom == null) return true;

                if (WantsPeace(targetKingdom, playerKingdom))
                {
                    return true;
                }

                InformationManager.DisplayMessage(new InformationMessage($"{targetKingdom.Name} is not interested in peace at this time.", Colors.Red));
                return false;
            }
        }
        [HarmonyPatch(typeof(MakePeaceAction), "ApplyInternal")]
        public class MakePeaceActionPatch
        {
            public static void Postfix(IFaction faction1, IFaction faction2, MakePeaceAction.MakePeaceDetail detail)
            {
                var strategicAIBehavior = Campaign.Current.GetCampaignBehavior<StrategicAICampaignBehavior>();
                strategicAIBehavior?.OnPeaceDeclared(faction1, faction2, detail);

                if (faction1 is Kingdom kingdom1 && faction2 is Kingdom kingdom2)
                {
                    var alliesOf1 = kingdom1.GetAlliedKingdoms().ToList();
                    var alliesOf2 = kingdom2.GetAlliedKingdoms().ToList();

                    foreach (var ally1 in alliesOf1)
                    {
                        if (ally1.IsAtWarWith(kingdom2))
                        {
                            MakePeaceAction.Apply(ally1, kingdom2);
                        }
                        foreach (var ally2 in alliesOf2)
                        {
                            if (ally1.IsAtWarWith(ally2))
                            {
                                MakePeaceAction.Apply(ally1, ally2);
                            }
                        }
                    }

                    foreach (var ally2 in alliesOf2)
                    {
                        if (ally2.IsAtWarWith(kingdom1))
                        {
                            MakePeaceAction.Apply(ally2, kingdom1);
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(DeclareWarAction), "ApplyByDefault")]
        public class DeclareWarAction_ApplyByDefault_Patch
        {
            private static MethodInfo _applyInternalMethod;

            public static bool Prepare()
            {
                _applyInternalMethod = AccessTools.Method(typeof(DeclareWarAction), "ApplyInternal");
                return _applyInternalMethod != null;
            }

            public static bool Prefix(IFaction faction1, IFaction faction2)
            {
                try
                {
                    var detail = (faction1 == Hero.MainHero.MapFaction || faction2 == Hero.MainHero.MapFaction)
                        ? DeclareWarAction.DeclareWarDetail.CausedByPlayerHostility
                        : DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision;

                    _applyInternalMethod.Invoke(null, new object[] { faction1, faction2, detail });
                }
                catch (Exception ex)
                {
                    InformationManager.DisplayMessage(new InformationMessage($"War & AI Tweaks Harmony Error: Could not call ApplyInternal. {ex.Message}", Colors.Red));
                    return true;
                }

                return false;
            }
        }
        [HarmonyPatch(typeof(DefaultPartySizeLimitModel), "CalculateBaseMemberSize")]
        public class DefaultPartySizeLimitModelPatch
        {
            public static void Postfix(Hero partyLeader, IFaction partyMapFaction, Clan actualClan, ref ExplainedNumber result)
            {
                float townbonus = 0f;
                float castlebonus = 0f;
                float villagebonus = 0f;

                // Ensure the variables are properly scoped and initialized
                if (partyLeader == null || partyMapFaction == null || actualClan == null)
                {
                    return;
                }

                foreach (Settlement settlement in actualClan.Settlements)
                {
                    if (settlement.IsVillage)
                    {
                        villagebonus += 1f;
                    }
                    else if (settlement.IsCastle)
                    {
                        castlebonus += 5f;
                    }
                    else if (settlement.IsTown)
                    {
                        townbonus += 10f;
                    }
                }

                // Add bonuses to the result
                if (villagebonus > 0f)
                {
                    result.Add(villagebonus, new TaleWorlds.Localization.TextObject("Villages bonus"));
                }
                if (castlebonus > 0f)
                {
                    result.Add(castlebonus, new TaleWorlds.Localization.TextObject("Castles bonus"));
                }
                if (townbonus > 0f)
                {
                    result.Add(townbonus, new TaleWorlds.Localization.TextObject("Towns bonus"));
                }
            }
        }
    }
}