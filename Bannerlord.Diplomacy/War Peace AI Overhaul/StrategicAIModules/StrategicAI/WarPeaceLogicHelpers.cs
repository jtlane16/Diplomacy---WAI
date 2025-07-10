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
    internal class WarPeaceLogicHelpers
    {

        private static float? _cachedMaxKingdomDistance = null;

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

            // Get AI reasoning
            var reason = GetPeaceDecisionReason(aiKingdom, playerKingdom);
            string reasonText = string.Join("\n", reason.Reasons);
            string reasonSection = $"\n\nReason for peace:\n{reasonText}";

            string message = $"[AI Diplomacy] {aiKingdom.Name} wants to make {requestType} with your kingdom (score: {score}).{tributeText}{reasonSection}";

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

        // Returns a list of snowballing kingdoms (those with 1.5x the average strength and 1.5x the average settlements)
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

        public static bool CanSendPeaceRequest(Kingdom aiKingdom, Kingdom playerKingdom)
        {
            var key = (aiKingdom.StringId, playerKingdom.StringId);
            if (WarAndAiTweaks.WarPeaceAI.WarPeaceLogicController.PeaceRequestCooldowns.TryGetValue(key, out var lastRequestTime))
            {
                if ((CampaignTime.Now - lastRequestTime).ToDays < 10) // Use the same cooldown as in controller
                    return false;
            }
            return true;
        }

        public static void SetPeaceRequestCooldown(Kingdom aiKingdom, Kingdom playerKingdom)
        {
            WarAndAiTweaks.WarPeaceAI.WarPeaceLogicController.PeaceRequestCooldowns[(aiKingdom.StringId, playerKingdom.StringId)] = CampaignTime.Now;
        }

        public static float GetWarWeariness(Kingdom kingdom)
        {
            if (kingdom == null) return 0f;
            var controller = Campaign.Current.GetCampaignBehavior<WarAndAiTweaks.WarPeaceAI.WarPeaceLogicController>();
            if (controller?.WarWeariness == null) return 0f;
            controller.WarWeariness.TryGetValue(kingdom.StringId, out float value);
            return value;
        }

        public static float GetWarEagerness(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                var controller = Campaign.Current.GetCampaignBehavior<WarAndAiTweaks.WarPeaceAI.WarPeaceLogicController>();
                return controller != null ? controller.WarEagernessMax / 2f : 25f;
            }
            var ctrl = Campaign.Current.GetCampaignBehavior<WarAndAiTweaks.WarPeaceAI.WarPeaceLogicController>();
            if (ctrl?.WarEagerness == null) return 25f;
            ctrl.WarEagerness.TryGetValue(kingdom.StringId, out float value);
            return value;
        }

        public static float GetWarWearinessScore(Kingdom kingdom)
        {
            var controller = Campaign.Current.GetCampaignBehavior<WarPeaceLogicController>();
            if (controller == null || controller.WarWearinessMax <= 0f) return 0f;
            float weariness = GetWarWeariness(kingdom);
            return 40f * (weariness / controller.WarWearinessMax);  // Up from 20f to compensate for doubled max
        }

        public static float GetWarEagernessScore(Kingdom kingdom)
        {
            var controller = Campaign.Current.GetCampaignBehavior<WarPeaceLogicController>();
            if (controller == null || controller.WarEagernessMax <= 0f) return 0f;
            float eagerness = GetWarEagerness(kingdom);
            return 40f * (eagerness / controller.WarEagernessMax);  // Up from 20f to compensate for doubled max
        }

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

            // 7. Snap tiny numbers to zero so we don’t spam 1-dinár payments
            if (adjustedValue > -5000 && adjustedValue < 5000)
                adjustedValue = 0;

            // 8. Convert lump-sum value into the daily tribute the game actually tracks
            return Campaign.Current.Models.DiplomacyModel.GetDailyTributeForValue(adjustedValue);
        }

        public static DecisionReason GetPeaceDecisionReason(Kingdom self, Kingdom enemy)
        {
            var reason = new DecisionReason();
            float score = 0;

            if (self == null || enemy == null || self.Leader == null || enemy.Leader == null)
            {
                reason.Reasons = new List<string> { "No valid kingdoms or leaders to evaluate peace reasons." };
                reason.Score = 0;
                return reason;
            }

            string selfName = self.Name?.ToString() ?? "Unknown Kingdom";
            string enemyName = enemy.Name?.ToString() ?? "Unknown Kingdom";

            // List of (score, reason) tuples
            var reasonScores = new List<(float, string)>();

            if (PeaceScoring.IsStrongerArmy(enemy, self))
                reasonScores.Add((15, $"It is said that the armies of {enemyName} outnumber those of {selfName}, compelling {selfName} to seek peace."));

            float relPower = KingdomPowerEvaluator.GetRelativePower(self, enemy);
            float relPowerScore = MathF.Clamp((1f - relPower) * 60f, -30f, 30f);
            if (relPowerScore > 0)
                reasonScores.Add((relPowerScore, $"{selfName} is overshadowed by the might of {enemyName}, and thus desires peace."));
            else if (relPowerScore < 0)
                reasonScores.Add((Math.Abs(relPowerScore), $"{selfName} stands strong, yet chooses the path of peace with {enemyName}."));

            if (!AreBordering(self, enemy))
                reasonScores.Add((10, $"There is little to gain from distant wars, and so {selfName} seeks peace with {enemyName}."));

            float snowballBonus = PeaceScoring.GetSnowballingPeaceBonus(self, enemy);
            if (snowballBonus != 0)
                reasonScores.Add((snowballBonus, $"With greater threats looming, {selfName} turns to peace with {enemyName} to face other perils."));

            float multiWarPenalty = GetMultipleWarsPenalty(self, 15f);
            if (multiWarPenalty != 0)
                reasonScores.Add((multiWarPenalty, $"{selfName} is beset on many fronts and seeks to lighten its burdens by making peace with {enemyName}."));

            float wearinessScore = GetWarWearinessScore(self) * 2.5f;
            if (wearinessScore > 0)
                reasonScores.Add((wearinessScore, $"The people of {selfName} are weary of war and yearn for peace with {enemyName}."));
            else if (wearinessScore < 0)
                reasonScores.Add((Math.Abs(wearinessScore), $"Though the people of {selfName} are not yet tired of war, their leaders seek peace with {enemyName}."));

            float eagernessScore = GetWarEagernessScore(self);
            if (eagernessScore > 0)
                reasonScores.Add((eagernessScore, $"Some in {selfName} still hunger for battle, but the council has chosen peace with {enemyName}."));

            float tributeScore = PeaceScoring.GetPeaceTributeScore(self, enemy, 0.0015f, 25f);
            if (tributeScore > 0)
                reasonScores.Add((tributeScore, $"{selfName} is willing to pay tribute to secure peace with {enemyName}."));
            else if (tributeScore < 0)
                reasonScores.Add((Math.Abs(tributeScore), $"{selfName} expects tribute from {enemyName} as part of the peace."));

            // Pick the reason with the highest score (absolute value)
            var best = reasonScores.OrderByDescending(r => Math.Abs(r.Item1)).FirstOrDefault();
            if (!string.IsNullOrEmpty(best.Item2))
                reason.Reasons = new List<string> { best.Item2 };
            else
                reason.Reasons = new List<string> { "No compelling reason for peace could be found." };

            // The overall score is still the sum of all factors
            reason.Score = reasonScores.Sum(r => r.Item1);
            return reason;
        }

        public static DecisionReason GetWarDecisionReason(Kingdom self, Kingdom target)
        {
            var reason = new DecisionReason();
            float score = 0;

            if (self == null || target == null || self.Leader == null || target.Leader == null)
            {
                reason.Reasons = new List<string> { "No valid kingdoms or leaders to evaluate war reasons." };
                reason.Score = 0;
                return reason;
            }

            string selfName = self.Name?.ToString() ?? "Unknown Kingdom";
            string targetName = target.Name?.ToString() ?? "Unknown Kingdom";

            var reasonScores = new List<(float, string)>();

            if (WarScoring.IsStrongerArmy(self, target))
                reasonScores.Add((20, $"{selfName} believes its armies can overcome those of {targetName}, and so war is declared."));

            float relPower = KingdomPowerEvaluator.GetRelativePower(self, target);
            float relPowerScore = MathF.Clamp((relPower - 1f) * 60f, -30f, 30f);
            if (relPowerScore > 0)
                reasonScores.Add((relPowerScore, $"{selfName} sees itself as stronger than {targetName}, emboldening them to war."));
            else if (relPowerScore < 0)
                reasonScores.Add((Math.Abs(relPowerScore), $"{selfName} is undeterred by the might of {targetName}, choosing war regardless."));

            if (AreBordering(self, target))
                reasonScores.Add((10, $"The lands of {selfName} and {targetName} share a border, making conflict inevitable."));

            float snowballBonus = WarScoring.GetSnowballingWarBonus(self, target);
            if (snowballBonus != 0)
                reasonScores.Add((snowballBonus, $"{selfName} seeks to check the growing power of {targetName} through war."));

            float multiWarPenalty = GetMultipleWarsPenalty(self, 15f);
            if (multiWarPenalty != 0)
                reasonScores.Add((multiWarPenalty, $"{selfName} is already entangled in many wars, yet chooses to add {targetName} to its list of foes."));

            float eagernessScore = GetWarEagernessScore(self) * 2.5f;
            if (eagernessScore > 0)
                reasonScores.Add((eagernessScore, $"The lords of {selfName} are eager for conquest and press for war against {targetName}."));

            float wearinessScore = GetWarWearinessScore(self) * 1.5f;
            if (wearinessScore > 0)
                reasonScores.Add((wearinessScore, $"Despite growing weariness, {selfName} marches to war against {targetName}."));

            var best = reasonScores.OrderByDescending(r => Math.Abs(r.Item1)).FirstOrDefault();
            if (!string.IsNullOrEmpty(best.Item2))
                reason.Reasons = new List<string> { best.Item2 };
            else
                reason.Reasons = new List<string> { "No compelling reason for war could be found." };

            reason.Score = reasonScores.Sum(r => r.Item1);
            return reason;
        }
        public static void LogKingdomThinking(Kingdom kingdom, List<Kingdom> enemies)
        {
            if (kingdom == null) return;

            var weariness = GetWarWeariness(kingdom);
            var eagerness = GetWarEagerness(kingdom);
            var wearinessScore = GetWarWearinessScore(kingdom);
            var eagernessScore = GetWarEagernessScore(kingdom);
            var multiWarPenalty = GetMultipleWarsPenalty(kingdom, 15f);
            var snowballing = GetSnowballingKingdoms().Contains(kingdom);

            var log = new System.Text.StringBuilder();
            log.AppendLine($"AI Thinking: {kingdom.Name} (ID: {kingdom.StringId})");
            log.AppendLine($"  TotalStrength: {kingdom.TotalStrength}");
            log.AppendLine($"  Settlements: {kingdom.Settlements.Count}");
            log.AppendLine($"  WarWeariness: {weariness} (Score: {wearinessScore})");
            log.AppendLine($"  WarEagerness: {eagerness} (Score: {eagernessScore})");
            log.AppendLine($"  MultipleWarsPenalty: {multiWarPenalty}");
            log.AppendLine($"  IsSnowballing: {snowballing}");
            log.AppendLine($"  Enemies: {string.Join(", ", enemies.Select(e => e.Name.ToString()))}");

            foreach (var enemy in enemies)
            {
                // Get detailed scoring breakdowns
                var peaceBreakdown = PeaceScoring.GetPeaceScoreBreakdown(kingdom, enemy);
                var warBreakdown = WarScoring.GetWarScoreBreakdown(kingdom, enemy);

                log.AppendLine($"    vs {enemy.Name}:");
                log.AppendLine($"      PeaceScore = {peaceBreakdown.Total} (Threshold={WarPeaceLogicController.PeaceScoreThreshold})");
                foreach (var (label, value) in peaceBreakdown.Factors)
                    log.AppendLine($"        {label}: {value}");

                log.AppendLine($"      WarScore = {warBreakdown.Total} (Threshold={WarPeaceLogicController.WarScoreThreshold})");
                foreach (var (label, value) in warBreakdown.Factors)
                    log.AppendLine($"        {label}: {value}");

                var reason = GetPeaceDecisionReason(kingdom, enemy);
                log.AppendLine($"      PeaceReason: {reason.Reasons.FirstOrDefault()}");
                var warReason = GetWarDecisionReason(kingdom, enemy);
                log.AppendLine($"      WarReason: {warReason.Reasons.FirstOrDefault()}");
            }

            // If no enemies, log the best war target
            if (enemies.Count == 0)
            {
                var warTarget = WarAndAiTweaks.WarPeaceAI.WarScoring.GetBestWarTarget(kingdom);
                if (warTarget.kingdom != null)
                {
                    var warBreakdown = WarScoring.GetWarScoreBreakdown(kingdom, warTarget.kingdom);
                    log.AppendLine($"  Considering war on: {warTarget.kingdom.Name} (Score={warTarget.score}, Threshold={WarPeaceLogicController.WarScoreThreshold})");
                    log.AppendLine($"    WarScore breakdown:");
                    foreach (var (label, value) in warBreakdown.Factors)
                        log.AppendLine($"      {label}: {value}");
                    var warReason = GetWarDecisionReason(kingdom, warTarget.kingdom);
                    log.AppendLine($"    WarReason: {warReason.Reasons.FirstOrDefault()}");
                }
            }

            WarPeaceLogger.Log(log.ToString());
        }
        public static float GetBorderDistancePenalty(Kingdom self, Kingdom target, float maxPenalty = 30f, float minDistance = 100f)
        {
            if (AreBordering(self, target))
                return 0f;

            var midA = self.FactionMidSettlement?.Position2D ?? Vec2.Zero;
            var midB = target.FactionMidSettlement?.Position2D ?? Vec2.Zero;
            float distance = midA.Distance(midB);

            float maxDistance = GetMaxKingdomMidpointDistance();
            // Clamp and scale the penalty
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
            // Fallback to 1000 if only one kingdom or error
            _cachedMaxKingdomDistance = maxDist > 0 ? maxDist : 1000f;
            return _cachedMaxKingdomDistance.Value;
        }

        public static void UpdateAllKingdomWarStates(WarAndAiTweaks.WarPeaceAI.WarPeaceLogicController controller)
        {
            foreach (var kingdom in Kingdom.All.Where(k =>
                k != null && !k.IsEliminated && !k.IsMinorFaction && k.Leader != null && k.Leader != Hero.MainHero))
            {
                int enemyCount = GetEnemyKingdoms(kingdom).Count;

                // Update WarWeariness
                if (!controller.WarWeariness.TryGetValue(kingdom.StringId, out float weariness))
                    weariness = 0f;

                if (enemyCount > 0)
                {
                    // Increase weariness if at war, up to max
                    weariness = MathF.Min(controller.WarWearinessMax, weariness + controller.WarWearinessStep);
                }
                else
                {
                    // Decay weariness if at peace
                    weariness = MathF.Max(0f, weariness - controller.WarWearinessStep);
                }
                controller.WarWeariness[kingdom.StringId] = weariness;

                // Update WarEagerness
                if (!controller.WarEagerness.TryGetValue(kingdom.StringId, out float eagerness))
                    eagerness = controller.WarEagernessMax / 2f;

                if (enemyCount == 0)
                {
                    // Increase eagerness if at peace, up to max
                    eagerness = MathF.Min(controller.WarEagernessMax, eagerness + controller.WarEagernessStep);
                }
                else
                {
                    // Decay eagerness if at war
                    eagerness = MathF.Max(0f, eagerness - controller.WarEagernessStep);
                }
                controller.WarEagerness[kingdom.StringId] = eagerness;
            }
        }

        /// Penalty for declaring war again too soon after concluding peace.
        /// Returns –maxPenalty at 0 days and 0 once minPeaceDays have elapsed.
        /// Penalty for offering peace too soon after a war begins.
        /// Returns –maxPenalty at 0 days and 0 once minWarDays have elapsed.
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

            float t = 1f - (warDays / minWarDays);           // linear fade-out
            return -maxPenalty * t;
        }

        /// Penalty for declaring war again too soon after peace.
        /// Returns –maxPenalty at 0 days and 0 once minPeaceDays have elapsed.
        public static float GetShortPeaceWarPenalty(
            Kingdom self, Kingdom target,
            int minPeaceDays = 10,
            float maxPenalty = 75f)
        {
            var stance = self.GetStanceWith(target);
            if (stance == null || stance.IsAtWar)
                return 0f;                                   // already at war → no penalty

            // NB: property name is *PeaceDeclarationTime* in TaleWorlds code
            float peaceDays = (float) (CampaignTime.Now - stance.PeaceDeclarationDate).ToDays;
            if (peaceDays >= minPeaceDays)
                return 0f;

            float t = 1f - (peaceDays / minPeaceDays);
            return -maxPenalty * t;
        }


    }
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