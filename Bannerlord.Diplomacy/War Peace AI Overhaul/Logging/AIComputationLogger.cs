using System;
using System.IO;
using System.Text;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace WarAndAiTweaks.AI
{
    /// <summary>
    /// Centralized CSV logger for AI computations.
    /// </summary>
    public static class AIComputationLogger
    {
        private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai_computation.log");
        private static readonly object _sync = new object();

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

        private static void WriteLine(string line)
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

        // --- War logging ---
        /// <summary>
        /// Logs a potential war declaration, including a detailed breakdown of the score.
        /// </summary>
        // Add this new method to the class
        public static void LogAIGoal(Kingdom kingdom, WarAndAiTweaks.AI.Goals.AIGoal goal)
        {
            string goalDetails = "";
            if (goal is WarAndAiTweaks.AI.Goals.ExpandGoal expandGoal && expandGoal.Target != null)
            {
                goalDetails = $", Target: {expandGoal.Target.Name}";
            }
            else if (goal is WarAndAiTweaks.AI.Goals.SurviveGoal surviveGoal && surviveGoal.PeaceCandidate != null)
            {
                goalDetails = $", Peace Candidate: {surviveGoal.PeaceCandidate.Name}";
            }

            WriteLine($"{DateTime.UtcNow:o},AI_GOAL,{kingdom.StringId},{goal.Type},{goal.Priority:F2}{goalDetails}");
        }

        // Replace the old LogWarCandidate method with this new one
        public static void LogWarCandidate(Kingdom owner, Kingdom target, float baseScore, float warDesire, float peaceDesire, float recentPeacePenalty, float totalScore, ExplainedNumber explainedScore)
        {
            var details = FormatExplainedNumber(explainedScore);
            WriteLine($"{DateTime.UtcNow:o},WAR_CANDIDATE,{owner.StringId},{target.StringId},{baseScore:F2},{warDesire:F2},{peaceDesire:F2},{recentPeacePenalty:F2},{totalScore:F2},\"{details}\"");
        }

        public static void LogWarDecision(Kingdom owner, Kingdom target, float chosenScore)
        {
            var tid = target != null ? target.StringId : "<none>";
            WriteLine($"{DateTime.UtcNow:o},WAR_DECISION,{owner.StringId},{tid},{chosenScore:F2}");
        }

        // --- Peace logging ---
        /// <summary>
        /// Logs a potential peace proposal, including a detailed score breakdown and the ramp-up factor.
        /// </summary>
        public static void LogPeaceCandidate(Kingdom owner, Kingdom target, float finalPeaceScore, ExplainedNumber explainedBaseScore, float ramp)
        {
            var details = FormatExplainedNumber(explainedBaseScore);
            WriteLine($"{DateTime.UtcNow:o},PEACE_CANDIDATE,{owner.StringId},{target.StringId},{finalPeaceScore:F2},{ramp:F2},\"{details}\"");
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
        /// <summary>
        /// Logs a potential alliance, including a detailed breakdown of the score.
        /// </summary>
        public static void LogAllianceCandidate(Kingdom owner, Kingdom target, ExplainedNumber allianceScore)
        {
            var details = FormatExplainedNumber(allianceScore);
            WriteLine($"{DateTime.UtcNow:o},ALLIANCE_CANDIDATE,{owner.StringId},{target.StringId},{allianceScore.ResultNumber:F2},\"{details}\"");
        }
        public static void LogAllianceDecision(Kingdom owner, Kingdom target, bool decided, float allianceScore)
        {
            WriteLine($"{DateTime.UtcNow:o},ALLIANCE_DECISION,{owner.StringId},{target.StringId},{(decided ? 1 : 0)},{allianceScore:F2}");
        }

        // --- Non-Aggression Pact logging ---
        /// <summary>
        /// Logs a potential non-aggression pact, including a detailed breakdown of the score.
        /// </summary>
        public static void LogPactCandidate(Kingdom owner, Kingdom target, ExplainedNumber pactScore)
        {
            var details = FormatExplainedNumber(pactScore);
            WriteLine($"{DateTime.UtcNow:o},NAP_CANDIDATE,{owner.StringId},{target.StringId},{pactScore.ResultNumber:F2},\"{details}\"");
        }

        public static void LogPactDecision(Kingdom owner, Kingdom target, bool decided, float pactScore)
        {
            WriteLine($"{DateTime.UtcNow:o},NAP_DECISION,{owner.StringId},{target.StringId},{(decided ? 1 : 0)},{pactScore:F2}");
        }
    }
}