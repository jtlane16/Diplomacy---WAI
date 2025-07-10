using Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace WarAndAiTweaks.WarPeaceAI
{
    /// <summary>
    /// Simple coalition system: "Is someone too strong? Stop fighting each other and gang up on them!"
    /// </summary>
    public static class CoalitionSystem
    {
        // Simple thresholds - adjust these to tune when coalitions form
        private const float STRENGTH_SNOWBALL_THRESHOLD = 1.4f;    // 40% stronger than average
        private const float TERRITORY_SNOWBALL_THRESHOLD = 1.3f;   // 30% more territory than average

        // How much to adjust stances when coalition forms
        private const float COALITION_PEACE_PRESSURE = 8f;         // Push toward peace with non-threats
        private const float COALITION_WAR_PRESSURE = 10f;          // Push toward war with threats

        /// <summary>
        /// Simple check: Who's the biggest threat right now?
        /// </summary>
        public static Kingdom GetBiggestThreat()
        {
            var kingdoms = Kingdom.All
                .Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null)
                .ToList();

            if (kingdoms.Count < 3) return null; // Need at least 3 kingdoms for coalitions

            // Calculate the averages
            float avgStrength = (float) kingdoms.Average(k => k.TotalStrength);
            float avgTerritory = (float) kingdoms.Average(k => k.Settlements.Count);

            // Find kingdoms that meet BOTH criteria
            var threats = kingdoms.Where(k =>
                k.TotalStrength >= avgStrength * STRENGTH_SNOWBALL_THRESHOLD &&
                k.Settlements.Count >= avgTerritory * TERRITORY_SNOWBALL_THRESHOLD
            ).ToList();

            // Return the strongest threat (or null if none qualify)
            return threats.OrderByDescending(k => k.TotalStrength).FirstOrDefault();
        }

        /// <summary>
        /// Should this kingdom adjust its stance due to coalition pressure?
        /// </summary>
        public static float GetCoalitionStanceAdjustment(Kingdom self, Kingdom target)
        {
            var biggestThreat = GetBiggestThreat();
            if (biggestThreat == null) return 0f; // No coalition needed

            // If I AM the biggest threat, no coalition adjustments for me
            if (self == biggestThreat) return 0f;

            // If my TARGET is the biggest threat, be more aggressive toward them
            if (target == biggestThreat)
            {
                return COALITION_WAR_PRESSURE; // Push toward war with the threat
            }

            // If my target is NOT the threat, be more peaceful (focus on real threat)
            if (target != biggestThreat)
            {
                return -COALITION_PEACE_PRESSURE; // Push toward peace with non-threats
            }

            return 0f;
        }

        /// <summary>
        /// Emergency peace logic: Should these two kingdoms make peace immediately due to bigger threat?
        /// </summary>
        public static bool ShouldForceEmergencyPeace(Kingdom kingdom1, Kingdom kingdom2)
        {
            var biggestThreat = GetBiggestThreat();
            if (biggestThreat == null) return false;

            // Both kingdoms must NOT be the biggest threat
            if (kingdom1 == biggestThreat || kingdom2 == biggestThreat) return false;

            // They must currently be at war
            if (!kingdom1.IsAtWarWith(kingdom2)) return false;

            // The threat must be REALLY threatening (2x average strength)
            var kingdoms = Kingdom.All.Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null).ToList();
            float avgStrength = (float) kingdoms.Average(k => k.TotalStrength);

            bool extremeThreat = biggestThreat.TotalStrength >= avgStrength * 2.0f; // 2x stronger than average

            return extremeThreat;
        }

        /// <summary>
        /// Get a description of the current coalition situation for logging
        /// </summary>
        public static string GetCoalitionStatus()
        {
            var biggestThreat = GetBiggestThreat();
            if (biggestThreat == null)
                return "No coalition threats detected.";

            var kingdoms = Kingdom.All.Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null).ToList();
            float avgStrength = (float) kingdoms.Average(k => k.TotalStrength);
            float avgTerritory = (float) kingdoms.Average(k => k.Settlements.Count);

            float strengthRatio = biggestThreat.TotalStrength / avgStrength;
            float territoryRatio = biggestThreat.Settlements.Count / avgTerritory;

            return $"COALITION TARGET: {biggestThreat.Name} " +
                   $"(Strength: {strengthRatio:F1}x avg, Territory: {territoryRatio:F1}x avg) " +
                   $"- Other kingdoms should unite against this threat!";
        }

        /// <summary>
        /// Check if a kingdom qualifies as a snowball threat (for external use)
        /// </summary>
        public static bool IsSnowballThreat(Kingdom kingdom)
        {
            return GetBiggestThreat() == kingdom;
        }

        /// <summary>
        /// Get coalition adjustment reasoning for logging
        /// </summary>
        public static string GetCoalitionReasoning(Kingdom self, Kingdom target, float adjustment)
        {
            if (adjustment == 0f) return "";

            var biggestThreat = GetBiggestThreat();
            if (biggestThreat == null) return "";

            if (target == biggestThreat)
                return $"Coalition pressure: {biggestThreat.Name} is the biggest threat - increase aggression!";
            else
                return $"Coalition pressure: Focus on real threat ({biggestThreat.Name}) - reduce conflict with {target.Name}";
        }
    }
}