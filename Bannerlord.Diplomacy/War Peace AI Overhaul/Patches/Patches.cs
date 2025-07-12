using HarmonyLib;

using Helpers;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;
using TaleWorlds.Library;

using WarAndAiTweaks;
using WarAndAiTweaks.WarPeaceAI;

using MathF = TaleWorlds.Library.MathF;

namespace Diplomacy.War_Peace_AI_Overhaul
{
    internal class Patches
    {
        [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnDeclarePeace")]
        public class KingdomPlayerPeacePatch
        {
            // Get the Strategic AI instance to evaluate AI willingness for peace
            public static bool Prefix(KingdomWarItemVM item)
            {
                return true;
            }
        }

        [HarmonyPatch(typeof(Building), "GetBuildingEffectAmount")]
        public class MilitiaPatch
        {
            public static void Postfix(Building __instance, BuildingEffectEnum effect, ref float __result)
            {
                if (effect == BuildingEffectEnum.Militia && __instance.Name.ToString() == "Militia Grounds")
                {
                    if (__instance.Town.IsCastle) { __result = __result + 5; }
                    if (__instance.Town.IsTown) { __result = __result + 10; }
                }
                return;
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

        [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculatePartyWage")]
        public class CalculatePartyWagePatch
        {
            public static void Postfix(MobileParty mobileParty, ref int __result)
            {
                if (mobileParty.IsGarrison)
                {
                    __result = (int) (__result * 0.5f); // Reduce garrison wages by 50%
                }
            }
        }

        [HarmonyPatch(typeof(DefaultSettlementFoodModel), "NumberOfMenOnGarrisonToEatOneFood", MethodType.Getter)]
        public class GarrisonFoodConsumptionPatch
        {
            [HarmonyPostfix]
            public static void Postfix(ref int __result)
            {
                // Original: 20 men eat 1 food
                // New: 40 men eat 1 food (50% reduction in food consumption)
                __result = 40;

                // Or make it configurable:
                // __result = (int)(__result * GarrisonConfig.FoodConsumptionEfficiency);
            }
        }

        // Main garrison calculation patch
        [HarmonyPatch(typeof(FactionHelper), "FindIdealGarrisonStrengthPerWalledCenter")]
        public class FindIdealGarrisonStrengthPatch
        {
            [HarmonyPostfix]
            public static void Postfix(ref float __result)
            {
                // Apply base multiplier to final result
                __result *= 2.0f;

                // Ensure minimum garrison size
                __result = MathF.Max(__result, 50f);
            }
        }
        [HarmonyPatch(typeof(DefaultArmyManagementCalculationModel), "GetMobilePartiesToCallToArmy")]
        public class Patch_RemovePlayerCompanionsFromArmyCall
        {
            public static void Postfix(MobileParty leaderParty, ref List<MobileParty> __result)
            {
                if (__result == null)
                    return;

                // Remove all player companions (not main hero, not player clan lords)
                __result.RemoveAll(mp =>
                    mp != null &&
                    mp.LeaderHero != null &&
                    mp.LeaderHero.Clan != null &&
                    mp.LeaderHero.Clan == Hero.MainHero.Clan &&
                    mp.LeaderHero.Clan == Clan.PlayerClan
                );
            }
        }
    }
}