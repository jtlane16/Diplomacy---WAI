using Diplomacy;
using Diplomacy.DiplomaticAction;
using Diplomacy.Extensions;

using Microsoft.Extensions.Logging;

using System.Text;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace WarAndAiTweaks
{
    /// <summary>
    /// A helper class to log detailed AI diplomatic decision-making for debugging purposes.
    /// </summary>
    internal class DiplomacyLogHelper
    {
        private static readonly ILogger _logger = LogFactory.Get<DiplomacyLogHelper>();

        /// <summary>
        /// Logs the result of the initial prerequisite checks for declaring war.
        /// </summary>
        public static void LogWarConditionCheck(Kingdom us, Kingdom them, bool passed, string reason)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("---------- AI WAR PRE-CONDITION CHECK ----------");
            sb.AppendLine($"Checking: {us.Name} vs {them.Name}");
            sb.AppendLine($"Result: {(passed ? "PASSED" : "FAILED")}");
            sb.AppendLine($"Reason: {reason}");
            sb.AppendLine("-------------------------------------------------");
            _logger.LogInformation(sb.ToString());
        }

        /// <summary>
        /// Logs a detailed breakdown of a kingdom's evaluation for declaring war against a target.
        /// </summary>
        public static void LogWarEvaluation(Kingdom us, WarScoreBreakdown breakdown, float warThreshold, float urgencyDiscount)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("========== AI WAR EVALUATION ==========");
            sb.AppendLine($"Evaluating: {us.Name} -> {breakdown.Target.Name}");
            sb.AppendLine($"Stance: {us.GetStanceWith(breakdown.Target).GetStanceName()}");
            sb.AppendLine($"---");
            sb.AppendLine($"Final Score: {breakdown.FinalScore:F2} / {warThreshold:F2} (Base: {warThreshold + urgencyDiscount:F2}, Urgency: -{urgencyDiscount:F2}) -> {(breakdown.FinalScore > warThreshold ? "Considering War" : "Not Considering War")}");
            sb.AppendLine($"--- Breakdown ---");
            sb.AppendLine($"Threat Score: {breakdown.ThreatScore:F2}");
            sb.AppendLine($"Power Balance Score: {breakdown.PowerBalanceScore:F2}");
            sb.AppendLine($"Existing Wars Penalty: {breakdown.MultiWarPenalty:F2}");
            sb.AppendLine($"Distance Penalty: {breakdown.DistancePenalty:F2}");
            sb.AppendLine($"Dogpile Bonus: {breakdown.DogpileBonus:F2}");
            sb.AppendLine($"Conquest Score: {breakdown.ConquestScore:F2}"); // NEW
            sb.AppendLine("=======================================");
            _logger.LogInformation(sb.ToString());
        }

        /// <summary>
        /// Logs a detailed breakdown of a kingdom's evaluation for making peace with an enemy.
        /// </summary>
        public static void LogPeaceEvaluation(Kingdom us, PeaceScoreBreakdown breakdown, float peaceThreshold, bool wantsPeace)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("========== AI PEACE EVALUATION ==========");
            sb.AppendLine($"Evaluating: {us.Name} -> {breakdown.Target.Name}");
            sb.AppendLine($"Stance: {us.GetStanceWith(breakdown.Target).GetStanceName()}");
            sb.AppendLine($"---");
            sb.AppendLine($"Final Score: {breakdown.FinalScore:F2} / {peaceThreshold:F2} -> {(wantsPeace ? "Wants Peace" : "Wants War")}");
            sb.AppendLine($"--- Breakdown ---");
            sb.AppendLine($"Danger Score (Their Strength): {breakdown.DangerScore:F2}");
            sb.AppendLine($"War Exhaustion Score: {breakdown.ExhaustionScore:F2}");
            sb.AppendLine($"Tribute Score (x{breakdown.TributeFactor:F2}): {breakdown.TributeAmount * breakdown.TributeFactor:F2} (Tribute: {breakdown.TributeAmount})");
            sb.AppendLine("=======================================");
            _logger.LogInformation(sb.ToString());
        }
    }
}