using HarmonyLib;

using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Library;

using WarAndAiTweaks.Strategic;
using WarAndAiTweaks.Strategic.Scoring;

namespace Diplomacy.War_Peace_AI_Overhaul
{
    internal class Patches
    {
        [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomWarDecision")]
        public class Patch_DisableRandomWar { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

        [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomPeaceDecision")]
        public class Patch_DisableRandomPeace { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

        [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnDeclarePeace")]
        public class KingdomPlayerPeacePatch
        {
            // Get the Strategic AI instance to evaluate AI willingness for peace
            private static StrategicConquestAI GetStrategicAI()
            {
                // Look for the StrategicConquestAI behavior in the campaign
                return Campaign.Current?.GetCampaignBehavior<StrategicConquestAI>();
            }

            // Create temporary scoring components for evaluation
            // Create temporary scoring components for evaluation
            private static bool WantsPeace(Kingdom aiKingdom, Kingdom playerKingdom)
            {
                try
                {
                    var strategicAI = GetStrategicAI();
                    if (strategicAI == null)
                    {
                        // Fallback to simplified logic if Strategic AI is not available
                        return GetFallbackPeaceWillingness(aiKingdom, playerKingdom);
                    }

                    // FIXED: Create temporary components using proper constructor pattern
                    var runawayAnalyzer = new RunawayFactionAnalyzer();
                    var warScorer = new WarScorer(runawayAnalyzer);

                    // Try to get war start time from stance and record it
                    var stance = aiKingdom.GetStanceWith(playerKingdom);
                    if (stance != null && stance.IsAtWar)
                    {
                        var warTime = stance.WarStartDate;
                        warScorer.RecordWarStart(aiKingdom, playerKingdom);
                    }

                    // FIXED: Pass WarScorer instead of nested dictionary
                    var peaceScorer = new PeaceScorer(runawayAnalyzer, warScorer);
                    var strategy = new ConquestStrategy(aiKingdom);

                    // Calculate peace priority using the actual AI logic
                    float peacePriority = peaceScorer.CalculatePeacePriority(aiKingdom, playerKingdom, strategy);
                    float peaceThreshold = peaceScorer.CalculatePeaceThreshold(aiKingdom);

                    // AI wants peace if priority exceeds threshold
                    bool wantsPeace = peacePriority > peaceThreshold;

                    // Log the decision for debugging
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"[Peace Evaluation] {aiKingdom.Name}: Priority={peacePriority:F1}, Threshold={peaceThreshold:F1}, Wants Peace={wantsPeace}",
                        wantsPeace ? Colors.Green : Colors.Red));

                    return wantsPeace;
                }
                catch (System.Exception ex)
                {
                    // Fallback on any errors
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"[Peace Evaluation Error] {ex.Message} - Using fallback logic", Colors.Yellow));
                    return GetFallbackPeaceWillingness(aiKingdom, playerKingdom);
                }
            }

            // Fallback logic if Strategic AI components aren't available
            private static bool GetFallbackPeaceWillingness(Kingdom aiKingdom, Kingdom playerKingdom)
            {
                var stance = aiKingdom.GetStanceWith(playerKingdom);
                if (stance == null) return true;

                // Basic factors for fallback
                float strengthRatio = playerKingdom.TotalStrength / aiKingdom.TotalStrength;
                int aiActiveWars = FactionManager.GetEnemyKingdoms(aiKingdom).Count();
                int aiTerritories = aiKingdom.Fiefs.Count;
                float daysSinceWar = stance.WarStartDate.ElapsedDaysUntilNow;

                // AI more likely to want peace if:
                // - Player is much stronger (2x+)
                // - AI is fighting multiple wars (3+)
                // - AI has very few territories (≤2)
                // - War has lasted a long time (100+ days)
                bool wantsPeace = strengthRatio > 2.0f ||
                                 aiActiveWars >= 3 ||
                                 aiTerritories <= 2 ||
                                 daysSinceWar > 100f;

                return wantsPeace;
            }

            // Main patch method
            // Main patch method
            public static bool Prefix(KingdomWarItemVM item)
            {
                var playerKingdom = Hero.MainHero.Clan.Kingdom;
                var targetKingdom = item.Faction2 as Kingdom;

                if (playerKingdom == null || targetKingdom == null)
                    return true; // Allow if we can't determine kingdoms

                // FIXED: Only allow peace negotiations if player is the kingdom ruler
                if (playerKingdom.RulingClan?.Leader != Hero.MainHero)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Only the kingdom ruler can negotiate peace agreements.",
                        Colors.Red));
                    return false; // Block peace attempt for vassals
                }

                // Special case: If player kingdom is the target, check if the other faction wants peace
                var aiKingdom = (item.Faction1 as Kingdom == playerKingdom) ? targetKingdom : (item.Faction1 as Kingdom);

                if (aiKingdom == null || aiKingdom == playerKingdom)
                    return true; // Allow if both are player kingdoms or can't determine

                // Check if the AI kingdom wants peace with the player
                if (!WantsPeace(aiKingdom, playerKingdom))
                {
                    // Determine why AI doesn't want peace for better messaging
                    string reason = GetPeaceRejectionReason(aiKingdom, playerKingdom);

                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{aiKingdom.Name} is not interested in peace at this time. {reason}",
                        Colors.Red));

                    return false; // Block the peace attempt
                }

                // AI wants peace - allow the player action
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{aiKingdom.Name} is willing to negotiate peace.",
                    Colors.Green));

                return true; // Allow peace negotiation
            }

            // Provide specific reasons why AI rejected peace
            private static string GetPeaceRejectionReason(Kingdom aiKingdom, Kingdom playerKingdom)
            {
                try
                {
                    var stance = aiKingdom.GetStanceWith(playerKingdom);
                    if (stance == null) return "";

                    float strengthRatio = playerKingdom.TotalStrength / aiKingdom.TotalStrength;
                    int aiActiveWars = FactionManager.GetEnemyKingdoms(aiKingdom).Count();
                    int aiTerritories = aiKingdom.Fiefs.Count;
                    float daysSinceWar = stance.WarStartDate.ElapsedDaysUntilNow;

                    // Determine primary reason for rejection
                    if (daysSinceWar < 20f)
                        return "The war is too recent - they need more time to consider peace.";

                    if (strengthRatio < 0.8f)
                        return "They believe they have a military advantage.";

                    if (aiTerritories <= 1)
                        return "They are fighting for survival and won't give up easily.";

                    if (aiActiveWars <= 1)
                        return "They can focus their full attention on this war.";

                    return "They are determined to continue the conflict.";
                }
                catch
                {
                    return "They are not ready for peace negotiations.";
                }
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
    }
}