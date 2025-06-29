using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Diplomacy.Extensions;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

using WarAndAiTweaks.AI.Goals;
using WarAndAiTweaks.DiplomaticAction;

namespace WarAndAiTweaks.AI
{
    /// <summary>
    /// Centralized CSV logger for AI computations.
    /// </summary>
    public static class AIComputationLogger
    {
        private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai_computation.log");
        private static readonly object _sync = new object();

        // Add this property to control logging detail
        public static bool EnableDetailedPartyLogging { get; set; } = false;
        public static float MinimumScoreChangeToLog { get; set; } = 100f; // Only log significant changes

        /// <summary>
        /// Clears the entire log file.
        /// </summary>
        public static void ClearLog()
        {
            try
            {
                lock (_sync)
                {
                    File.WriteAllText(LogFile, string.Empty);
                }
            }
            catch
            {
                // Suppress IO errors
            }
        }

        public static void WriteLine(string line)
        {
            try
            {
                lock (_sync)
                {
                    File.AppendAllText(LogFile, line + Environment.NewLine);
                }
            }
            catch
            {
                // Suppress IO errors
            }
        }

        /// <summary>
        /// Formats an ExplainedNumber instance into a semicolon-separated string of key:value pairs.
        /// </summary>
        private static string FormatExplainedNumber(ExplainedNumber explainedNumber)
        {
            var sb = new StringBuilder();
            var lines = explainedNumber.GetLines();

            if (lines == null) return "";

            foreach (var (name, value) in lines)
            {
                // Sanitize for CSV and key-value format
                var sanitizedName = name.Replace(":", "").Replace(";", "").Replace(",", "").Trim();
                sb.Append($"{sanitizedName}:{value:F2};");
            }
            // Remove trailing semicolon
            if (sb.Length > 0)
            {
                sb.Length--;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Logs the diplomatic overview for a kingdom at the start of its turn.
        /// </summary>
        public static void LogDiplomaticOverview(Kingdom kingdom, StrategicState state, IEnumerable<Kingdom> wars, IEnumerable<Kingdom> alliances, IEnumerable<DiplomaticAgreement> pacts, IEnumerable<Kingdom> bordering)
        {
            var warsStr = string.Join(";", wars.Select(k => k.Name.ToString().Replace(";", "")));
            var alliancesStr = string.Join(";", alliances.Select(k => k.Name.ToString().Replace(";", "")));
            var pactsStr = string.Join(";", pacts.Select(p => p.GetOtherKingdom(kingdom).Name.ToString().Replace(";", "")));
            var borderingStr = string.Join(";", bordering.Select(k => k.Name.ToString().Replace(";", "")));

            WriteLine($"{DateTime.UtcNow:o},DIPLOMATIC_OVERVIEW,{kingdom.StringId},{state},Wars:{warsStr},Alliances:{alliancesStr},NAPs:{pactsStr},Borders:{borderingStr}");
        }


        /// <summary>
        /// Logs the priority of all potential goals for a kingdom before a decision is made.
        /// </summary>
        public static void LogGoalEvaluation(Kingdom kingdom, IEnumerable<AIGoal> goals)
        {
            var sb = new StringBuilder();
            foreach (var goal in goals)
            {
                sb.Append($"{goal.Type}:{goal.Priority:F2};");
            }
            if (sb.Length > 0) sb.Length--; // Remove trailing semicolon

            WriteLine($"{DateTime.UtcNow:o},GOAL_EVALUATION,{kingdom.StringId},\"{sb.ToString()}\"");
        }

        // --- War logging ---
        public static void LogAIGoal(Kingdom kingdom, AIGoal goal, StrategicState state)
        {
            string goalDetails = "";
            if (goal is ExpandGoal expandGoal && expandGoal.Target != null)
            {
                goalDetails = $", Target: {expandGoal.Target.Name}";
            }
            // The check for SurviveGoal.PeaceCandidate has been removed.

            WriteLine($"{DateTime.UtcNow:o},AI_STATE_AND_GOAL,{kingdom.StringId},{state},{goal.Type},{goal.Priority:F2}{goalDetails}");
        }

        public static void LogBetrayalDecision(Kingdom owner, Kingdom target, float score, string reason)
        {
            WriteLine($"{DateTime.UtcNow:o},BETRAYAL_DECISION,{owner.StringId},{target.StringId},{score:F2},\"{reason}\"");
        }

        /// <summary>
        /// MODIFIED: Logs the detailed score breakdown for a potential war declaration.
        /// </summary>
        public static void LogWarCandidate(Kingdom owner, Kingdom target, ExplainedNumber explainedScore)
        {
            var details = FormatExplainedNumber(explainedScore);
            WriteLine($"{DateTime.UtcNow:o},WAR_CANDIDATE,{owner.StringId},{target.StringId},{explainedScore.ResultNumber:F2},\"{details}\"");
        }

        public static void LogWarDecision(Kingdom owner, Kingdom target, float chosenScore)
        {
            var tid = target != null ? target.StringId : "<none>";
            WriteLine($"{DateTime.UtcNow:o},WAR_DECISION,{owner.StringId},{tid},{chosenScore:F2}");
        }

        // --- Peace logging ---
        public static void LogPeaceCandidate(Kingdom owner, Kingdom target, float finalPeaceScore, ExplainedNumber explainedBaseScore)
        {
            var details = FormatExplainedNumber(explainedBaseScore);
            WriteLine($"{DateTime.UtcNow:o},PEACE_CANDIDATE,{owner.StringId},{target.StringId},{finalPeaceScore:F2},\"{details}\"");
        }

        public static void LogPeaceDecision(Kingdom owner, Kingdom target, float peaceScore)
        {
            WriteLine($"{DateTime.UtcNow:o},PEACE_DECISION,{owner.StringId},{target.StringId},{peaceScore:F2}");
        }

        /// <summary>
        /// Logs the result of comparing the war score against the threshold.
        /// </summary>
        public static void LogWarThresholdCheck(Kingdom owner, Kingdom target, float score, float threshold, bool declared)
        {
            WriteLine($"{DateTime.UtcNow:o},WAR_THRESHOLD_CHECK,{owner.StringId},{target.StringId},{score:F2},{threshold:F2},{(declared ? 1 : 0)}");
        }

        // --- Alliance logging ---
        public static void LogAllianceCandidate(Kingdom owner, Kingdom target, ExplainedNumber allianceScore)
        {
            var details = FormatExplainedNumber(allianceScore);
            WriteLine($"{DateTime.UtcNow:o},ALLIANCE_CANDIDATE,{owner.StringId},{target.StringId},{allianceScore.ResultNumber:F2},\"{details}\"");
        }
        public static void LogAllianceDecision(Kingdom owner, Kingdom target, bool decided, float allianceScore, string reason)
        {
            WriteLine($"{DateTime.UtcNow:o},ALLIANCE_DECISION,{owner.StringId},{target.StringId},{(decided ? 1 : 0)},{allianceScore:F2},\"{reason}\"");
        }

        // --- Non-Aggression Pact logging ---
        public static void LogPactCandidate(Kingdom owner, Kingdom target, ExplainedNumber pactScore)
        {
            var details = FormatExplainedNumber(pactScore);
            WriteLine($"{DateTime.UtcNow:o},NAP_CANDIDATE,{owner.StringId},{target.StringId},{pactScore.ResultNumber:F2},\"{details}\"");
        }

        public static void LogPactDecision(Kingdom owner, Kingdom target, bool decided, float pactScore, string reason)
        {
            WriteLine($"{DateTime.UtcNow:o},NAP_DECISION,{owner.StringId},{target.StringId},{(decided ? 1 : 0)},{pactScore:F2},\"{reason}\"");
        }

        // --- Party AI Behavior Logging ---
        public static void LogDefensiveBehaviorAnalysis(MobileParty party, int threatenedSettlements, int alliedThreatenedSettlements, float totalDefenseBonus)
        {
            if (!EnableDetailedPartyLogging) return; // Add this check

            var heroName = party.LeaderHero?.Name?.ToString() ?? "Unknown";
            var kingdomId = party.LeaderHero?.Clan?.Kingdom?.StringId ?? "None";

            WriteLine($"{DateTime.UtcNow:o},DEFENSIVE_ANALYSIS,{kingdomId},{heroName},{threatenedSettlements},{alliedThreatenedSettlements},{totalDefenseBonus:F2}");
        }

        public static void LogOffensiveBehaviorAnalysis(MobileParty party, int viableTargets, int weakEnemyParties, float totalOffenseBonus)
        {
            if (!EnableDetailedPartyLogging) return; // Add this check

            var heroName = party.LeaderHero?.Name?.ToString() ?? "Unknown";
            var kingdomId = party.LeaderHero?.Clan?.Kingdom?.StringId ?? "None";

            WriteLine($"{DateTime.UtcNow:o},OFFENSIVE_ANALYSIS,{kingdomId},{heroName},{viableTargets},{weakEnemyParties},{totalOffenseBonus:F2}");
        }

        public static void LogStrategicPositioning(MobileParty party, bool isMultiFrontWar, bool nearBorder, bool withAllies, float positioningBonus)
        {
            if (!EnableDetailedPartyLogging) return; // Add this check

            var heroName = party.LeaderHero?.Name?.ToString() ?? "Unknown";
            var kingdomId = party.LeaderHero?.Clan?.Kingdom?.StringId ?? "None";

            WriteLine($"{DateTime.UtcNow:o},STRATEGIC_POSITIONING,{kingdomId},{heroName},{(isMultiFrontWar ? 1 : 0)},{(nearBorder ? 1 : 0)},{(withAllies ? 1 : 0)},{positioningBonus:F2}");
        }

        public static void LogFeastBehaviorImpact(MobileParty party, bool attendingFeast, bool kingdomFeasting, bool enemyFeasting, float feastImpact)
        {
            // DISABLED: Feast logging disabled for performance
            return;
        }

        public static void LogSeasonalBehaviorImpact(MobileParty party, string season, bool isOffensive, bool isDefensive, float seasonalBonus)
        {
            if (!EnableDetailedPartyLogging) return; // Add this check

            var heroName = party.LeaderHero?.Name?.ToString() ?? "Unknown";
            var kingdomId = party.LeaderHero?.Clan?.Kingdom?.StringId ?? "None";

            WriteLine($"{DateTime.UtcNow:o},SEASONAL_BEHAVIOR,{kingdomId},{heroName},{season},{(isOffensive ? 1 : 0)},{(isDefensive ? 1 : 0)},{seasonalBonus:F2}");
        }

        public static void LogSettlementThreatAssessment(Settlement settlement, Kingdom kingdom, float threatLevel, int nearbyEnemies)
        {
            if (!EnableDetailedPartyLogging) return; // Add this check

            var kingdomId = kingdom?.StringId ?? "None";
            var settlementName = settlement?.Name?.ToString() ?? "Unknown";

            WriteLine($"{DateTime.UtcNow:o},SETTLEMENT_THREAT,{kingdomId},{settlementName},{threatLevel:F2},{nearbyEnemies}");
        }
    }
}