using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

using WarAndAiTweaks.WarPeaceAI;

namespace Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI
{
    internal class KingdomLogicHelpers
    {
        /// <summary>
        /// Core utility: Get all kingdoms this kingdom is at war with
        /// </summary>
        public static List<Kingdom> GetEnemyKingdoms(Kingdom kingdom)
        {
            if (kingdom == null) return new List<Kingdom>();

            return Kingdom.All
                .Where(k => k != null && k != kingdom && !k.IsEliminated && !k.IsMinorFaction
                           && k.Leader != null && kingdom.IsAtWarWith(k))
                .ToList();
        }

        /// <summary>
        /// Geographic utility: Check if two kingdoms border each other
        /// </summary>
        public static bool AreBordering(Kingdom kingdomA, Kingdom kingdomB)
        {
            if (kingdomA == null || kingdomB == null || kingdomA == kingdomB)
                return false;

            // Simple check: Are any settlements of A close to any settlements of B?
            var settlementsA = kingdomA.Settlements.Where(s => s.IsFortification || s.IsVillage).ToList();
            var settlementsB = kingdomB.Settlements.Where(s => s.IsFortification || s.IsVillage).ToList();

            foreach (var settlementA in settlementsA)
            {
                var nearestToA = Settlement.All
                    .Where(s => s != settlementA)
                    .OrderBy(s => s.Position2D.DistanceSquared(settlementA.Position2D))
                    .Take(3); // Check only 3 nearest

                if (nearestToA.Any(s => s.OwnerClan?.Kingdom == kingdomB))
                    return true;
            }

            return false;
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
}