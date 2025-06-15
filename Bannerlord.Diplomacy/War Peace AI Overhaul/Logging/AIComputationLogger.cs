using System;
using System.IO;

using TaleWorlds.CampaignSystem;

namespace WarAndAiTweaks.AI
{
    /// <summary>
    /// Centralized CSV logger for AI computations.
    /// </summary>
    public static class AIComputationLogger
    {
        private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai_computation.log");
        private static readonly object _sync = new object();

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

        // --- War logging ---
        public static void LogWarCandidate(Kingdom owner, Kingdom target, float baseScore, float peaceBonus, float totalScore)
        {
            WriteLine($"{DateTime.UtcNow:o},WAR_CANDIDATE,{owner.StringId},{target.StringId},{baseScore:F2},{peaceBonus:F2},{totalScore:F2}");
        }
        public static void LogWarDecision(Kingdom owner, Kingdom target, float chosenScore)
        {
            var tid = target != null ? target.StringId : "<none>";
            WriteLine($"{DateTime.UtcNow:o},WAR_DECISION,{owner.StringId},{tid},{chosenScore:F2}");
        }

        // --- Peace logging ---
        public static void LogPeaceCandidate(Kingdom owner, Kingdom target, float peaceScore)
        {
            WriteLine($"{DateTime.UtcNow:o},PEACE_CANDIDATE,{owner.StringId},{target.StringId},{peaceScore:F2}");
        }
        public static void LogPeaceDecision(Kingdom owner, Kingdom target, float peaceScore)
        {
            WriteLine($"{DateTime.UtcNow:o},PEACE_DECISION,{owner.StringId},{target.StringId},{peaceScore:F2}");
        }

        // --- Alliance logging ---
        public static void LogAllianceCandidate(Kingdom owner, Kingdom target, float allianceScore)
        {
            WriteLine($"{DateTime.UtcNow:o},ALLIANCE_CANDIDATE,{owner.StringId},{target.StringId},{allianceScore:F2}");
        }
        public static void LogAllianceDecision(Kingdom owner, Kingdom target, bool decided, float allianceScore)
        {
            WriteLine($"{DateTime.UtcNow:o},ALLIANCE_DECISION,{owner.StringId},{target.StringId},{(decided ? 1 : 0)},{allianceScore:F2}");
        }

        // --- Non-Aggression Pact logging ---
        public static void LogPactCandidate(Kingdom owner, Kingdom target, float pactScore)
        {
            WriteLine($"{DateTime.UtcNow:o},NAP_CANDIDATE,{owner.StringId},{target.StringId},{pactScore:F2}");
        }
        public static void LogPactDecision(Kingdom owner, Kingdom target, bool decided, float pactScore)
        {
            WriteLine($"{DateTime.UtcNow:o},NAP_DECISION,{owner.StringId},{target.StringId},{(decided ? 1 : 0)},{pactScore:F2}");
        }
    }
}
