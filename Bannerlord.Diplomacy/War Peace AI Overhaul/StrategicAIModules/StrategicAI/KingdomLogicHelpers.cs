using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;
using TaleWorlds.Library;

using WarAndAiTweaks.WarPeaceAI;

namespace Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI
{
    internal class KingdomLogicHelpers
    {
        /// <summary>
        /// Core utility: Get all kingdoms this kingdom is at war with (optimized with caching)
        /// </summary>
        public static List<Kingdom> GetEnemyKingdoms(Kingdom kingdom)
        {
            if (kingdom == null) return new List<Kingdom>();

            // Use cached version from controller if available
            var controller = Campaign.Current.GetCampaignBehavior<KingdomLogicController>();
            if (controller != null)
            {
                return controller.GetEnemyKingdomsCached(kingdom);
            }

            // Fallback to direct calculation
            return Kingdom.All
                .Where(k => k != null && k != kingdom && !k.IsEliminated && !k.IsMinorFaction
                           && k.Leader != null && kingdom.IsAtWarWith(k))
                .ToList();
        }

        /// <summary>
        /// Geographic utility: Check if two kingdoms border each other (optimized with caching)
        /// </summary>
        public static bool AreBordering(Kingdom kingdomA, Kingdom kingdomB)
        {
            if (kingdomA == null || kingdomB == null || kingdomA == kingdomB)
                return false;

            // Use cached version from controller if available
            var controller = Campaign.Current.GetCampaignBehavior<KingdomLogicController>();
            if (controller != null)
            {
                return controller.AreBorderingCached(kingdomA, kingdomB);
            }

            // Fallback to optimized direct calculation
            return AreBorderingOptimized(kingdomA, kingdomB);
        }

        /// <summary>
        /// Optimized bordering check for fallback use
        /// </summary>
        private static bool AreBorderingOptimized(Kingdom kingdomA, Kingdom kingdomB)
        {
            // Get the closest settlements between the two kingdoms
            var settlementsA = kingdomA.Settlements.Where(s => s.IsFortification || s.IsVillage).Take(8);
            var settlementsB = kingdomB.Settlements.Where(s => s.IsFortification || s.IsVillage).Take(8);

            if (!settlementsA.Any() || !settlementsB.Any()) return false;

            // Find minimum distance between any settlements of the two kingdoms
            float minDistanceSquared = float.MaxValue;
            foreach (var settlementA in settlementsA)
            {
                foreach (var settlementB in settlementsB)
                {
                    float distSquared = settlementA.Position2D.DistanceSquared(settlementB.Position2D);
                    if (distSquared < minDistanceSquared)
                        minDistanceSquared = distSquared;
                }
            }

            // Compare against map-relative threshold
            float borderingThresholdSquared = GetBorderingThresholdSquared();
            return minDistanceSquared <= borderingThresholdSquared;
        }

        // Cache for bordering threshold
        private static float _cachedBorderingThresholdSquared = -1f;
        private static float _lastBorderingThresholdCalculation = -1f;

        private static float GetBorderingThresholdSquared()
        {
            float currentDay = (float) CampaignTime.Now.ToDays;

            // Recalculate threshold every 3 days
            if (_cachedBorderingThresholdSquared < 0 || currentDay - _lastBorderingThresholdCalculation > 3f)
            {
                _cachedBorderingThresholdSquared = CalculateBorderingThresholdSquared();
                _lastBorderingThresholdCalculation = currentDay;
            }

            return _cachedBorderingThresholdSquared;
        }

        private static float CalculateBorderingThresholdSquared()
        {
            var kingdoms = Kingdom.All.Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null).ToList();

            if (kingdoms.Count < 2) return 1000000f; // 1000^2 fallback

            // Find the minimum distance between any two settlements of different kingdoms
            // This gives us the "closest neighbors" distance on this map
            float minGlobalDistanceSquared = float.MaxValue;
            int sampleCount = 0;
            const int maxSamples = 50; // Limit samples for performance

            foreach (var kingdom in kingdoms.Take(10)) // Sample from first 10 kingdoms only
            {
                var settlements = kingdom.Settlements.Where(s => s.IsFortification || s.IsVillage).Take(3);

                foreach (var settlement in settlements)
                {
                    foreach (var otherKingdom in kingdoms.Where(k => k != kingdom).Take(5)) // Check against 5 other kingdoms
                    {
                        var otherSettlements = otherKingdom.Settlements.Where(s => s.IsFortification || s.IsVillage).Take(3);

                        foreach (var otherSettlement in otherSettlements)
                        {
                            float distSquared = settlement.Position2D.DistanceSquared(otherSettlement.Position2D);
                            if (distSquared < minGlobalDistanceSquared)
                                minGlobalDistanceSquared = distSquared;

                            sampleCount++;
                            if (sampleCount >= maxSamples) break;
                        }
                        if (sampleCount >= maxSamples) break;
                    }
                    if (sampleCount >= maxSamples) break;
                }
                if (sampleCount >= maxSamples) break;
            }

            // Use 1.5x the minimum distance as the bordering threshold
            // This means settlements need to be closer than 1.5x the tightest spacing on the map
            return minGlobalDistanceSquared * 2.25f; // 1.5^2 = 2.25
        }

        /// <summary>
        /// Player interaction: Send peace request to player kingdom
        /// </summary>
        public static void SendAIRequestToPlayerKingdom(Kingdom aiKingdom, Kingdom playerKingdom, string requestType, float stance)
        {
            if (playerKingdom?.Leader != Hero.MainHero) return;

            int dailyTribute = GetPeaceTribute(playerKingdom.Leader.Clan, aiKingdom.Leader.Clan, playerKingdom, aiKingdom);

            string tributeText = dailyTribute > 0
                ? $"\n\nYou will pay {dailyTribute} gold per day in tribute."
                : dailyTribute < 0
                    ? $"\n\nYou will receive {Math.Abs(dailyTribute)} gold per day in tribute."
                    : "\n\nNo tribute will be paid.";

            // Use only narrative reason for peace requests
            string reasonText = requestType == "peace"
                ? StrategyEvaluator.GetPeaceReason(aiKingdom, playerKingdom)
                : "";

            string message = $"{aiKingdom.Name} seeks {requestType} with your kingdom.{tributeText}\n\n{reasonText}";

            InformationManager.ShowInquiry(new InquiryData(
                "Diplomatic Request",
                message,
                true,
                true,
                "Accept",
                "Deny",
                () =>
                {
                    MakePeaceAction.Apply(playerKingdom, aiKingdom, dailyTribute);
                    InformationManager.DisplayMessage(new InformationMessage($"You accepted peace with {aiKingdom.Name}.{tributeText}"));
                },
                () =>
                {
                    string rejectionReason = StrategyEvaluator.GetPeaceRejectionReason(aiKingdom, playerKingdom);
                    InformationManager.DisplayMessage(new InformationMessage($"You rejected peace with {aiKingdom.Name}.\n{rejectionReason}"));
                }
            ));
        }

        /// <summary>
        /// Economic calculation: Determine peace tribute amount
        /// </summary>
        public static int GetPeaceTribute(Clan clan, Clan otherClan, Kingdom kingdom, IFaction otherFaction)
        {
            // Check influence requirement
            int influenceCost = Campaign.Current.Models.DiplomacyModel.GetInfluenceCostOfProposingPeace(clan);
            if (clan.Influence < influenceCost) return 0;

            // Get baseline barter value
            int baseValue = new PeaceBarterable(clan.Leader, kingdom, otherFaction, CampaignTime.Years(1f))
                .GetValueForFaction(otherFaction);

            int adjustedValue;

            if (clan.MapFaction == Hero.MainHero.MapFaction && otherFaction is Kingdom otherKingdom)
            {
                // Player kingdom making peace - get worst case from all clans
                int minValue = baseValue;
                foreach (Clan kClan in otherKingdom.Clans)
                {
                    if (kClan.Leader != kClan.MapFaction.Leader)
                    {
                        int v = new PeaceBarterable(kClan.Leader, kingdom, otherFaction, CampaignTime.Years(1f))
                            .GetValueForFaction(kClan);
                        if (v < minValue) minValue = v;
                    }
                }
                adjustedValue = -minValue;
            }
            else
            {
                // AI-vs-AI → Add leniency
                adjustedValue = -baseValue + 30000;
            }

            // Don't demand payment from weaker clans
            if (otherFaction is Clan && adjustedValue < 0)
                adjustedValue = 0;

            // Make offers to player sweeter
            if (otherFaction == Hero.MainHero.MapFaction)
            {
                var pb = new PeaceBarterable(clan.MapFaction.Leader, kingdom, otherFaction, CampaignTime.Years(1f));
                int worstCase = pb.GetValueForFaction(clan.MapFaction);
                int sum = 0, count = 1;

                if (clan.MapFaction is Kingdom ourKingdom)
                {
                    foreach (Clan kClan in ourKingdom.Clans)
                    {
                        if (kClan.Leader != kClan.MapFaction.Leader)
                        {
                            int v = pb.GetValueForFaction(kClan);
                            if (v < worstCase) worstCase = v;
                            sum += v; count++;
                        }
                    }
                }

                float avg = (float) sum / count;
                int blended = (int) (0.65f * avg + 0.35f * worstCase);
                if (blended > adjustedValue) adjustedValue = blended;
            }

            // Snap tiny amounts to zero
            if (adjustedValue > -5000 && adjustedValue < 5000)
                adjustedValue = 0;

            return Campaign.Current.Models.DiplomacyModel.GetDailyTributeForValue(adjustedValue);
        }
    }

    [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnDeclarePeace")]
    public class OnDeclarePeace
    {
        public static bool Prefix(KingdomWarItemVM item)
        {
            var playerKingdom = Hero.MainHero.Clan.Kingdom;
            var targetKingdom = item.Faction2 as Kingdom;
            if (playerKingdom == null || targetKingdom == null)
                return false;

            // Only allow peace if the AI is willing (optional, can be removed if not needed)
            var controller = Campaign.Current.GetCampaignBehavior<KingdomLogicController>();
            float stance = controller?.GetKingdomStance(targetKingdom, playerKingdom) ?? 50f;

            // Use the stance threshold for peace (lower stance = more willing for peace)
            if (stance > KingdomStrategy.PEACE_THRESHOLD)
            {
                InformationManager.DisplayMessage(new InformationMessage($"{targetKingdom.Name} is not interested in peace at this time.", Colors.Red));
                return false;
            }

            // Calculate daily tribute (from player to AI)
            int dailyTribute = KingdomLogicHelpers.GetPeaceTribute(
                playerKingdom.Leader.Clan,
                targetKingdom.Leader.Clan,
                playerKingdom,
                targetKingdom
            );

            // Apply peace with tribute
            MakePeaceAction.Apply(playerKingdom, targetKingdom, dailyTribute);

            // Show result to player
            string tributeText = dailyTribute > 0
                ? $"You will pay {dailyTribute} gold per day in tribute."
                : dailyTribute < 0
                    ? $"You will receive {Math.Abs(dailyTribute)} gold per day in tribute."
                    : "No tribute will be paid.";
            InformationManager.DisplayMessage(new InformationMessage(
                $"You declared peace with {targetKingdom.Name}. {tributeText}"
            ));

            return false; // Skip original method
        }
    }
}