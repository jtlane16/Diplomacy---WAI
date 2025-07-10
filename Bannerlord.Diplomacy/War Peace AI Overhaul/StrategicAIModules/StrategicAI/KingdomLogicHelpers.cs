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

using static TaleWorlds.CampaignSystem.CampaignTime;

using MathF = TaleWorlds.Library.MathF;

namespace Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI
{
    internal class KingdomLogicHelpers
    {
        private static float? _cachedMaxKingdomDistance = null;

        // ========== STILL NEEDED - CORE UTILITY FUNCTIONS ==========

        public static List<Kingdom> GetEnemyKingdoms(Kingdom kingdom)
        {
            if (kingdom == null)
                return new List<Kingdom>();

            return Kingdom.All
                .Where(k => k != null && k != kingdom && !k.IsEliminated && !k.IsMinorFaction && k.Leader != null && kingdom.IsAtWarWith(k))
                .ToList();
        }

        public static bool AreBordering(Kingdom kingdomA, Kingdom kingdomB)
        {
            if (kingdomA == null || kingdomB == null || kingdomA == kingdomB)
                return false;

            var settlementsA = kingdomA.Settlements.Where(s => s.IsFortification || s.IsVillage).ToList();
            var settlementsB = kingdomB.Settlements.Where(s => s.IsFortification || s.IsVillage).ToList();

            foreach (var settlement in settlementsA)
            {
                var nearest = Settlement.All
                    .Where(s => s != settlement)
                    .OrderBy(s => s.Position2D.DistanceSquared(settlement.Position2D))
                    .Take(5);

                if (nearest.Any(s => s.OwnerClan?.Kingdom == kingdomB))
                    return true;
            }

            foreach (var settlement in settlementsB)
            {
                var nearest = Settlement.All
                    .Where(s => s != settlement)
                    .OrderBy(s => s.Position2D.DistanceSquared(settlement.Position2D))
                    .Take(5);

                if (nearest.Any(s => s.OwnerClan?.Kingdom == kingdomA))
                    return true;
            }

            return false;
        }

        // ========== STILL NEEDED - PLAYER INTERACTION ==========

        public static void SendAIRequestToPlayerKingdom(Kingdom aiKingdom, Kingdom playerKingdom, string requestType, float score)
        {
            if (playerKingdom == null || playerKingdom.Leader != Hero.MainHero)
                return;

            if (!CanSendPeaceRequest(aiKingdom, playerKingdom))
                return;

            // Calculate daily tribute (from player to AI)
            int dailyTribute = GetPeaceTribute(playerKingdom.Leader.Clan, aiKingdom.Leader.Clan, playerKingdom, aiKingdom);

            string tributeText = dailyTribute > 0
                ? $"\n\nYou will pay {dailyTribute} gold per day in tribute."
                : dailyTribute < 0
                    ? $"\n\nYou will receive {Math.Abs(dailyTribute)} gold per day in tribute."
                    : "\n\nNo tribute will be paid.";

            // Get AI reasoning based on strategy
            var controller = Campaign.Current.GetCampaignBehavior<KingdomLogicController>();
            float stance = controller?.GetKingdomStance(aiKingdom, playerKingdom) ?? 50f;
            string stanceDescription = controller?.GetKingdomStrategy(aiKingdom)?.GetStanceDescription(playerKingdom) ?? "Unknown";

            string reasonText = $"Our strategic assessment indicates: {stanceDescription.ToLower()} (stance: {stance:F0}%)";
            string reasonSection = $"\n\nReason for peace:\n{reasonText}";

            string message = $"[AI Diplomacy] {aiKingdom.Name} wants to make {requestType} with your kingdom.{tributeText}{reasonSection}";

            InformationManager.ShowInquiry(new InquiryData(
                "Diplomacy Request",
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
                    InformationManager.DisplayMessage(new InformationMessage($"You Rejected peace with {aiKingdom.Name}."));
                }
            ));

            SetPeaceRequestCooldown(aiKingdom, playerKingdom);
        }

        public static bool CanSendPeaceRequest(Kingdom aiKingdom, Kingdom playerKingdom)
        {
            var key = (aiKingdom.StringId, playerKingdom.StringId);
            if (WarAndAiTweaks.WarPeaceAI.KingdomLogicController.PeaceRequestCooldowns.TryGetValue(key, out var lastRequestTime))
            {
                if ((CampaignTime.Now - lastRequestTime).ToDays < 10)
                    return false;
            }
            return true;
        }

        public static void SetPeaceRequestCooldown(Kingdom aiKingdom, Kingdom playerKingdom)
        {
            WarAndAiTweaks.WarPeaceAI.KingdomLogicController.PeaceRequestCooldowns[(aiKingdom.StringId, playerKingdom.StringId)] = CampaignTime.Now;
        }

        // ========== STILL NEEDED - STRATEGIC ANALYSIS ==========

        public static List<Kingdom> GetSnowballingKingdoms()
        {
            var majorKingdoms = Kingdom.All
                .Where(k => !k.IsEliminated && !k.IsMinorFaction)
                .ToList();

            if (majorKingdoms.Count == 0)
                return new List<Kingdom>();

            float avgStrength = (float) majorKingdoms.Average(k => k.TotalStrength);
            float avgSettlements = (float) majorKingdoms.Average(k => k.Settlements.Count);

            return majorKingdoms
                .Where(k =>
                    k.TotalStrength >= 1.5f * avgStrength &&
                    k.Settlements.Count >= 1.5f * avgSettlements)
                .ToList();
        }

        public static float GetMultipleWarsPenalty(Kingdom kingdom, float perWarPenalty = 10f)
        {
            int wars = GetEnemyKingdoms(kingdom).Count;
            return wars > 1 ? (wars - 1) * perWarPenalty : 0f;
        }

        // ========== STILL NEEDED - ECONOMIC CALCULATIONS ==========

        public static int GetPeaceTribute(Clan clan, Clan otherClan, Kingdom kingdom, IFaction otherFaction)
        {
            // 1. Influence gate (same early-out as vanilla)
            int influenceCost = Campaign.Current.Models.DiplomacyModel.GetInfluenceCostOfProposingPeace(clan);
            if (clan.Influence < influenceCost)
                return 0;

            // 2. Baseline barter value (negative => we owe, positive => they owe)
            int baseValue = new PeaceBarterable(
                clan.Leader, kingdom, otherFaction, CampaignTime.Years(1f)
            ).GetValueForFaction(otherFaction);

            int adjustedValue;

            // 3. Special case: player kingdom making peace with another kingdom
            if (clan.MapFaction == Hero.MainHero.MapFaction && otherFaction is Kingdom otherKingdom)
            {
                int minValue = baseValue;
                foreach (Clan kClan in otherKingdom.Clans)
                {
                    if (kClan.Leader != kClan.MapFaction.Leader)
                    {
                        int v = new PeaceBarterable(
                            kClan.Leader, kingdom, otherFaction, CampaignTime.Years(1f)
                        ).GetValueForFaction(kClan);
                        if (v < minValue)
                            minValue = v;
                    }
                }
                adjustedValue = -minValue;
            }
            else
            {
                // 4. AI-vs-AI or player clan vs clan → extra 30,000 leniency
                adjustedValue = -baseValue + 30000;
            }

            // 5. Clan-vs-clan war can never demand payment from the weaker clan
            if (otherFaction is Clan && adjustedValue < 0)
                adjustedValue = 0;

            // 6. If the other side is the player kingdom, make the offer a bit sweeter
            if (otherFaction == Hero.MainHero.MapFaction)
            {
                PeaceBarterable pb = new PeaceBarterable(
                    clan.MapFaction.Leader, kingdom, otherFaction, CampaignTime.Years(1f)
                );

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

                if (blended > adjustedValue)
                    adjustedValue = blended; // friendlier offer
            }

            // 7. Snap tiny numbers to zero so we don't spam 1-dinár payments
            if (adjustedValue > -5000 && adjustedValue < 5000)
                adjustedValue = 0;

            // 8. Convert lump-sum value into the daily tribute the game actually tracks
            return Campaign.Current.Models.DiplomacyModel.GetDailyTributeForValue(adjustedValue);
        }

        // ========== STILL NEEDED - BACKWARD COMPATIBILITY ==========
        // These provide traditional scoring for validation purposes

        public static DecisionReason GetPeaceDecisionReason(Kingdom self, Kingdom enemy)
        {
            var reason = new DecisionReason();

            if (self == null || enemy == null || self.Leader == null || enemy.Leader == null)
            {
                reason.Reasons = new List<string> { "No valid kingdoms or leaders to evaluate peace reasons." };
                reason.Score = 0;
                return reason;
            }

            // Get strategic stance for the main reason
            var controller = Campaign.Current.GetCampaignBehavior<KingdomLogicController>();
            float stance = controller?.GetKingdomStance(self, enemy) ?? 50f;
            string stanceDesc = controller?.GetKingdomStrategy(self)?.GetStanceDescription(enemy) ?? "Unknown";

            string selfName = self.Name?.ToString() ?? "Unknown Kingdom";
            string enemyName = enemy.Name?.ToString() ?? "Unknown Kingdom";

            // Use stance-based reasoning
            if (stance <= 15f)
                reason.Reasons = new List<string> { $"{selfName} desperately seeks peace with {enemyName}, as their strategic position has become untenable." };
            else if (stance <= 30f)
                reason.Reasons = new List<string> { $"{selfName} strongly desires peace with {enemyName}, viewing continued conflict as counterproductive." };
            else if (stance <= 45f)
                reason.Reasons = new List<string> { $"{selfName} is open to peace with {enemyName}, though they remain cautious about the terms." };
            else
                reason.Reasons = new List<string> { $"{selfName} shows little interest in peace with {enemyName} at this time." };

            // The score is now just the stance value converted to old scoring scale
            reason.Score = stance * 1.5f; // Convert 0-100 stance to approximate old score range
            return reason;
        }

        public static DecisionReason GetWarDecisionReason(Kingdom self, Kingdom target)
        {
            var reason = new DecisionReason();

            if (self == null || target == null || self.Leader == null || target.Leader == null)
            {
                reason.Reasons = new List<string> { "No valid kingdoms or leaders to evaluate war reasons." };
                reason.Score = 0;
                return reason;
            }

            // Get strategic stance for the main reason
            var controller = Campaign.Current.GetCampaignBehavior<KingdomLogicController>();
            float stance = controller?.GetKingdomStance(self, target) ?? 50f;

            string selfName = self.Name?.ToString() ?? "Unknown Kingdom";
            string targetName = target.Name?.ToString() ?? "Unknown Kingdom";

            // Use stance-based reasoning
            if (stance >= 85f)
                reason.Reasons = new List<string> { $"{selfName} views {targetName} as a dire threat that must be crushed immediately." };
            else if (stance >= 70f)
                reason.Reasons = new List<string> { $"{selfName} sees an opportunity for conquest against {targetName} and mobilizes for war." };
            else if (stance >= 55f)
                reason.Reasons = new List<string> { $"{selfName} considers {targetName} a suitable target for expansion through military means." };
            else
                reason.Reasons = new List<string> { $"{selfName} shows little appetite for war against {targetName} at this time." };

            // The score is now just the stance value converted to old scoring scale
            reason.Score = stance * 1.5f; // Convert 0-100 stance to approximate old score range
            return reason;
        }

        // ========== STILL NEEDED - GEOGRAPHIC CALCULATIONS ==========

        public static float GetBorderDistancePenalty(Kingdom self, Kingdom target, float maxPenalty = 30f, float minDistance = 100f)
        {
            if (AreBordering(self, target))
                return 0f;

            var midA = self.FactionMidSettlement?.Position2D ?? Vec2.Zero;
            var midB = target.FactionMidSettlement?.Position2D ?? Vec2.Zero;
            float distance = midA.Distance(midB);

            float maxDistance = GetMaxKingdomMidpointDistance();
            float t = MathF.Clamp((distance - minDistance) / (maxDistance - minDistance), 0f, 1f);
            return t * maxPenalty;
        }

        public static float GetMaxKingdomMidpointDistance()
        {
            if (_cachedMaxKingdomDistance.HasValue)
                return _cachedMaxKingdomDistance.Value;

            var kingdoms = Kingdom.All
                .Where(k => k != null && !k.IsEliminated && !k.IsMinorFaction && k.FactionMidSettlement != null)
                .ToList();

            float maxDist = 0f;
            for (int i = 0; i < kingdoms.Count; i++)
            {
                var posA = kingdoms[i].FactionMidSettlement.Position2D;
                for (int j = i + 1; j < kingdoms.Count; j++)
                {
                    var posB = kingdoms[j].FactionMidSettlement.Position2D;
                    float dist = posA.Distance(posB);
                    if (dist > maxDist)
                        maxDist = dist;
                }
            }
            _cachedMaxKingdomDistance = maxDist > 0 ? maxDist : 1000f;
            return _cachedMaxKingdomDistance.Value;
        }

        // ========== STILL NEEDED - TIMING PENALTIES ==========

        public static float GetShortWarPeacePenalty(
            Kingdom self, Kingdom enemy,
            int minWarDays = 10,
            float maxPenalty = 75f)
        {
            var stance = self.GetStanceWith(enemy);
            if (stance == null || !stance.IsAtWar)
                return 0f;

            float warDays = (float) (CampaignTime.Now - stance.WarStartDate).ToDays;
            if (warDays >= minWarDays)
                return 0f;

            float t = 1f - (warDays / minWarDays);
            return -maxPenalty * t;
        }

        public static float GetShortPeaceWarPenalty(
            Kingdom self, Kingdom target,
            int minPeaceDays = 10,
            float maxPenalty = 75f)
        {
            var stance = self.GetStanceWith(target);
            if (stance == null || stance.IsAtWar)
                return 0f;

            float peaceDays = (float) (CampaignTime.Now - stance.PeaceDeclarationDate).ToDays;
            if (peaceDays >= minPeaceDays)
                return 0f;

            float t = 1f - (peaceDays / minPeaceDays);
            return -maxPenalty * t;
        }
    }

    // ========== STILL NEEDED - SUPPORT STRUCTURES ==========

    public struct DecisionReason
    {
        public float Score;
        public List<string> Reasons;

        public DecisionReason(float score)
        {
            Score = score;
            Reasons = new List<string>();
        }
    }

    internal static class WarPeaceLogger
    {
        private static readonly string LogFilePath = "WarPeaceAILog.txt";

        public static void Log(string message)
        {
            try
            {
                System.IO.File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { /* Ignore logging errors */ }
        }

        public static void Clear()
        {
            try
            {
                System.IO.File.WriteAllText(LogFilePath, string.Empty);
            }
            catch { /* Ignore logging errors */ }
        }
    }
}