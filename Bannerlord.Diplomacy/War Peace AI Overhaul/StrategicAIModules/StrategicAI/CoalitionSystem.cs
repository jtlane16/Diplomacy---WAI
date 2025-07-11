using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

using MathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.WarPeaceAI
{
    /// <summary>
    /// Simple coalition system: Stop the snowballing kingdom
    /// </summary>
    public static class CoalitionSystem
    {
        private const float STRENGTH_SNOWBALL_THRESHOLD = 1.4f;    // 40% stronger than average
        private const float TERRITORY_SNOWBALL_THRESHOLD = 1.3f;   // 30% more territory than average
        private const float COALITION_PRESSURE = 8f;               // Stance adjustment strength

        /// <summary>
        /// Find the biggest threat that needs coalition response
        /// </summary>
        public static Kingdom GetBiggestThreat()
        {
            var kingdoms = Kingdom.All
                .Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null)
                .ToList();

            if (kingdoms.Count < 3) return null; // Need at least 3 kingdoms for coalitions

            float avgStrength = (float) kingdoms.Average(k => k.TotalStrength);
            float avgTerritory = (float) kingdoms.Average(k => k.Settlements.Count);

            // Find kingdoms that meet threat criteria
            var threats = kingdoms.Where(k =>
                k.TotalStrength >= avgStrength * STRENGTH_SNOWBALL_THRESHOLD ||
                (k.Settlements.Count >= avgTerritory * TERRITORY_SNOWBALL_THRESHOLD &&
                 k.TotalStrength >= avgStrength * 1.2f) // Minimum strength requirement
            ).ToList();

            return threats.OrderByDescending(k => k.TotalStrength).FirstOrDefault();
        }

        /// <summary>
        /// Coalition stance adjustment: Unite against the threat
        /// </summary>
        public static float GetCoalitionStanceAdjustment(Kingdom self, Kingdom target)
        {
            var biggestThreat = GetBiggestThreat();
            if (biggestThreat == null || self == biggestThreat) return 0f;

            var kingdoms = Kingdom.All.Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null).ToList();
            if (kingdoms.Count == 0) return 0f;

            float avgStrength = (float) kingdoms.Average(k => k.TotalStrength);
            float threatRatio = biggestThreat.TotalStrength / avgStrength;

            // Gradual coalition buildup (1.3x = 0%, 1.8x = 100%)
            float coalitionIntensity = MathF.Clamp((threatRatio - 1.3f) * 2f, 0f, 1f);

            if (target == biggestThreat)
                return COALITION_PRESSURE * coalitionIntensity; // More aggressive toward threat
            else
                return -COALITION_PRESSURE * coalitionIntensity; // More peaceful with others
        }

        /// <summary>
        /// Emergency peace: Should these kingdoms stop fighting due to bigger threat?
        /// </summary>
        public static bool ShouldForceEmergencyPeace(Kingdom kingdom1, Kingdom kingdom2)
        {
            var biggestThreat = GetBiggestThreat();
            if (biggestThreat == null) return false;

            // Neither kingdom should be the threat
            if (kingdom1 == biggestThreat || kingdom2 == biggestThreat) return false;

            // They must be at war
            if (!kingdom1.IsAtWarWith(kingdom2)) return false;

            // The threat must be extreme (2x average strength)
            var kingdoms = Kingdom.All.Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null).ToList();
            if (kingdoms.Count == 0) return false;

            float avgStrength = (float) kingdoms.Average(k => k.TotalStrength);
            return biggestThreat.TotalStrength >= avgStrength * 2.0f; // 2x stronger = extreme threat
        }

        /// <summary>
        /// Check if a kingdom is the current snowball threat
        /// </summary>
        public static bool IsSnowballThreat(Kingdom kingdom)
        {
            return GetBiggestThreat() == kingdom;
        }
    }
}