using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

using MathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.WarPeaceAI
{
    /// <summary>
    /// Optimized coalition system with caching to prevent recalculation
    /// </summary>
    public static class CoalitionSystem
    {
        private const float STRENGTH_SNOWBALL_THRESHOLD = 1.4f;    // 40% stronger than average
        private const float TERRITORY_SNOWBALL_THRESHOLD = 1.3f;   // 30% more territory than average
        private const float COALITION_PRESSURE = 8f;               // Stance adjustment strength

        // Performance caches - static for global access
        private static Kingdom _cachedBiggestThreat = null;
        private static float _lastThreatCalculationDay = -1f;
        private static float _cachedAvgStrength = 0f;
        private static float _cachedAvgTerritory = 0f;

        /// <summary>
        /// Find the biggest threat that needs coalition response (cached)
        /// </summary>
        public static Kingdom GetBiggestThreat()
        {
            float currentDay = (float) CampaignTime.Now.ToDays;

            // Cache threat calculation for 1 day to avoid expensive recalculation
            if (_cachedBiggestThreat != null && currentDay - _lastThreatCalculationDay < 1f)
            {
                // Verify cached threat is still valid
                if (!_cachedBiggestThreat.IsEliminated && _cachedBiggestThreat.Leader != null)
                    return _cachedBiggestThreat;
            }

            // Recalculate threat
            var kingdoms = Kingdom.All
                .Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null)
                .ToList();

            if (kingdoms.Count < 3)
            {
                _cachedBiggestThreat = null;
                return null;
            }

            // Cache averages for reuse
            _cachedAvgStrength = (float) kingdoms.Average(k => k.TotalStrength);
            _cachedAvgTerritory = (float) kingdoms.Average(k => k.Settlements.Count);

            // Find kingdoms that meet threat criteria
            var threats = kingdoms.Where(k =>
                k.TotalStrength >= _cachedAvgStrength * STRENGTH_SNOWBALL_THRESHOLD ||
                (k.Settlements.Count >= _cachedAvgTerritory * TERRITORY_SNOWBALL_THRESHOLD &&
                 k.TotalStrength >= _cachedAvgStrength * 1.2f) // Minimum strength requirement
            ).ToList();

            _cachedBiggestThreat = threats.OrderByDescending(k => k.TotalStrength).FirstOrDefault();
            _lastThreatCalculationDay = currentDay;

            return _cachedBiggestThreat;
        }

        /// <summary>
        /// Coalition stance adjustment: Unite against the threat (optimized)
        /// </summary>
        public static float GetCoalitionStanceAdjustment(Kingdom self, Kingdom target)
        {
            var biggestThreat = GetBiggestThreat(); // Now cached
            if (biggestThreat == null || self == biggestThreat) return 0f;

            // Use cached average strength if available
            float avgStrength = _cachedAvgStrength;
            if (avgStrength <= 0f)
            {
                var kingdoms = Kingdom.All.Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null).ToList();
                if (kingdoms.Count == 0) return 0f;
                avgStrength = (float) kingdoms.Average(k => k.TotalStrength);
            }

            float threatRatio = biggestThreat.TotalStrength / avgStrength;

            // Gradual coalition buildup (1.3x = 0%, 1.8x = 100%)
            float coalitionIntensity = MathF.Clamp((threatRatio - 1.3f) * 2f, 0f, 1f);

            if (target == biggestThreat)
                return COALITION_PRESSURE * coalitionIntensity; // More aggressive toward threat
            else
                return -COALITION_PRESSURE * coalitionIntensity; // More peaceful with others
        }

        /// <summary>
        /// Emergency peace: Should these kingdoms stop fighting due to bigger threat? (optimized)
        /// </summary>
        public static bool ShouldForceEmergencyPeace(Kingdom kingdom1, Kingdom kingdom2)
        {
            var biggestThreat = GetBiggestThreat(); // Now cached
            if (biggestThreat == null) return false;

            // Neither kingdom should be the threat
            if (kingdom1 == biggestThreat || kingdom2 == biggestThreat) return false;

            // They must be at war
            if (!kingdom1.IsAtWarWith(kingdom2)) return false;

            // Use cached average strength if available
            float avgStrength = _cachedAvgStrength;
            if (avgStrength <= 0f)
            {
                var kingdoms = Kingdom.All.Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null).ToList();
                if (kingdoms.Count == 0) return false;
                avgStrength = (float) kingdoms.Average(k => k.TotalStrength);
            }

            return biggestThreat.TotalStrength >= avgStrength * 2.0f; // 2x stronger = extreme threat
        }

        /// <summary>
        /// Check if a kingdom is the current snowball threat (optimized)
        /// </summary>
        public static bool IsSnowballThreat(Kingdom kingdom)
        {
            return GetBiggestThreat() == kingdom; // Now cached
        }

        /// <summary>
        /// Force cache invalidation (call when kingdoms are eliminated or major changes occur)
        /// </summary>
        public static void InvalidateCache()
        {
            _cachedBiggestThreat = null;
            _lastThreatCalculationDay = -1f;
            _cachedAvgStrength = 0f;
            _cachedAvgTerritory = 0f;
        }
    }
}