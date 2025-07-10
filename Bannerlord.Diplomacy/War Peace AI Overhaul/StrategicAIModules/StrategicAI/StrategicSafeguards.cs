using Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

using MathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.WarPeaceAI
{
    /// <summary>
    /// Strategic safeguards to prevent AI from making terrible decisions
    /// </summary>
    public static class StrategicSafeguards
    {
        // Safeguard thresholds
        private const float MIN_POWER_RATIO_FOR_WAR = 0.8f;        // Must be at least 80% as strong
        private const float VULNERABILITY_BONUS_THRESHOLD = 0.6f;   // Target in multiple wars = vulnerable
        private const int MAX_CURRENT_WARS = 1;                    // Almost never fight multiple wars
        private const int MAX_WAR_DURATION_DAYS = 120;             // Force peace consideration after 120 days
        private const int STALEMATE_DURATION_DAYS = 60;            // Detect stalemates after 60 days

        /// <summary>
        /// SAFEGUARD 1: Check if declaring war would be strategic suicide
        /// </summary>
        public static bool IsWarDeclarationWise(Kingdom self, Kingdom target)
        {
            // Never declare war if already in one (with rare exceptions)
            int currentWars = KingdomLogicHelpers.GetEnemyKingdoms(self).Count;
            if (currentWars >= MAX_CURRENT_WARS)
            {
                // Exception: Coalition against extreme snowball threat
                if (CoalitionSystem.IsSnowballThreat(target))
                {
                    var biggestThreat = CoalitionSystem.GetBiggestThreat();
                    if (biggestThreat != null && IsExtremeSnowballThreat(biggestThreat))
                    {
                        return true; // Coalition emergency override
                    }
                }
                return false; // Don't start multiple wars
            }

            // Check basic military viability
            if (!IsMilitarilyViable(self, target))
                return false;

            // Wait for target to become vulnerable unless we're much stronger
            if (!IsTargetVulnerable(self, target) && !IsDecisiveAdvantage(self, target))
                return false;

            return true;
        }

        public static bool IsDecisionTooSoon(Kingdom self, Kingdom target, bool proposingWar)
        {
            var stance = self.GetStanceWith(target);
            if (stance == null) return false;

            if (proposingWar && !stance.IsAtWar)
            {
                // Can't declare war if peace is less than 7 days old
                float peaceDuration = (float) (CampaignTime.Now - stance.PeaceDeclarationDate).ToDays;
                return peaceDuration < 7f;
            }
            else if (!proposingWar && stance.IsAtWar)
            {
                // Can't make peace if war is less than 7 days old
                float warDuration = (float) (CampaignTime.Now - stance.WarStartDate).ToDays;
                return warDuration < 7f;
            }

            return false;
        }

        /// <summary>
        /// SAFEGUARD 2: Check if we should force peace to prevent forever wars
        /// </summary>
        public static bool ShouldForceWarEnd(Kingdom self, Kingdom target)
        {
            if (!self.IsAtWarWith(target)) return false;

            var stance = self.GetStanceWith(target);
            if (stance?.IsAtWar != true) return false;

            float warDuration = (float) (CampaignTime.Now - stance.WarStartDate).ToDays;

            // Force peace after maximum duration
            if (warDuration >= MAX_WAR_DURATION_DAYS)
                return true;

            // Force peace if stalemate detected and we're losing
            if (warDuration >= STALEMATE_DURATION_DAYS && IsInStalemate(self, target))
                return true;

            // Force peace if economically unsustainable
            if (IsWarEconomicallyUnsustainable(self, warDuration))
                return true;

            return false;
        }

        /// <summary>
        /// SAFEGUARD 3: Get additional stance pressure to prevent bad decisions
        /// </summary>
        public static float GetSafeguardStanceAdjustment(Kingdom self, Kingdom target)
        {
            float adjustment = 0f;

            // Heavy penalty for multiple wars
            int currentWars = KingdomLogicHelpers.GetEnemyKingdoms(self).Count;
            if (currentWars > 0 && !self.IsAtWarWith(target))
            {
                adjustment -= 15f; // Strong penalty against new wars
            }

            // Forever war prevention - escalating peace pressure
            if (self.IsAtWarWith(target))
            {
                var stance = self.GetStanceWith(target);
                if (stance?.IsAtWar == true)
                {
                    float warDuration = (float) (CampaignTime.Now - stance.WarStartDate).ToDays;

                    // Escalating war weariness
                    if (warDuration > 30)
                        adjustment -= 5f;
                    if (warDuration > 60)
                        adjustment -= 10f;
                    if (warDuration > 90)
                        adjustment -= 15f;
                    if (warDuration > 120)
                        adjustment -= 25f; // Desperate for peace
                }
            }

            // Opportunity detection - only fight when target is vulnerable
            if (!self.IsAtWarWith(target) && !IsTargetVulnerable(self, target))
            {
                adjustment -= 5f; // Wait for better opportunity
            }

            return MathF.Clamp(adjustment, -30f, 0f); // Only negative adjustments (safer decisions)
        }

        // Helper methods for safeguard checks

        private static bool IsMilitarilyViable(Kingdom self, Kingdom target)
        {
            if (self?.TotalStrength == null || target?.TotalStrength == null) return false;

            float powerRatio = self.TotalStrength / Math.Max(target.TotalStrength, 1f);
            return powerRatio >= MIN_POWER_RATIO_FOR_WAR;
        }

        private static bool IsTargetVulnerable(Kingdom self, Kingdom target)
        {
            // Target is vulnerable if fighting multiple wars
            int targetWars = KingdomLogicHelpers.GetEnemyKingdoms(target).Count;
            if (targetWars >= 2) return true;

            // Target is vulnerable if fighting someone stronger
            var targetEnemies = KingdomLogicHelpers.GetEnemyKingdoms(target);
            foreach (var enemy in targetEnemies)
            {
                float enemyPower = enemy.TotalStrength / Math.Max(target.TotalStrength, 1f);
                if (enemyPower >= 1.2f) return true; // Fighting someone 20% stronger
            }

            // Target is vulnerable if economically weak
            if (IsKingdomEconomicallyWeak(target)) return true;

            return false;
        }

        private static bool IsDecisiveAdvantage(Kingdom self, Kingdom target)
        {
            float powerRatio = self.TotalStrength / Math.Max(target.TotalStrength, 1f);
            return powerRatio >= 1.5f; // 50% stronger = decisive advantage
        }

        private static bool IsExtremeSnowballThreat(Kingdom kingdom)
        {
            var kingdoms = Kingdom.All.Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null).ToList();
            if (kingdoms.Count == 0) return false;

            float avgStrength = (float) kingdoms.Average(k => k.TotalStrength);
            return kingdom.TotalStrength >= avgStrength * 2.0f; // 2x average = extreme
        }

        private static bool IsInStalemate(Kingdom self, Kingdom target)
        {
            // Simplified stalemate detection
            // In a full implementation, you'd track territory changes, battle outcomes, etc.

            // Check if both kingdoms are roughly equal in strength
            float powerRatio = self.TotalStrength / Math.Max(target.TotalStrength, 1f);
            bool roughlyEqual = powerRatio >= 0.8f && powerRatio <= 1.2f;

            // Check if we're the weaker party in a stalemate
            bool weAreWeaker = powerRatio < 1.0f;

            return roughlyEqual && weAreWeaker;
        }

        private static bool IsWarEconomicallyUnsustainable(Kingdom kingdom, float warDuration)
        {
            // Simple economic pressure model
            // Longer wars become more expensive

            if (warDuration < 30) return false; // Short wars are fine

            // Check if kingdom is economically weak
            if (IsKingdomEconomicallyWeak(kingdom) && warDuration > 45)
                return true;

            // Very long wars are always economically draining
            if (warDuration > 90)
                return true;

            return false;
        }

        private static bool IsKingdomEconomicallyWeak(Kingdom kingdom)
        {
            if (kingdom == null) return false;

            // Simple economic weakness detection
            float avgProsperity = (float) kingdom.Settlements
                .Where(s => s?.Town != null)
                .Select(s => s.Town.Prosperity)
                .DefaultIfEmpty(0)
                .Average();

            float avgGold = (float) kingdom.Clans
                .Where(c => c?.Leader != null)
                .Select(c => c.Leader.Gold)
                .DefaultIfEmpty(0)
                .Average();

            // Weak if below thresholds (adjust as needed)
            return avgProsperity < 3000f || avgGold < 5000f;
        }

        /// <summary>
        /// Get human-readable explanation for why a war declaration was blocked
        /// </summary>
        public static string GetWarBlockedReason(Kingdom self, Kingdom target)
        {
            int currentWars = KingdomLogicHelpers.GetEnemyKingdoms(self).Count;

            if (currentWars >= MAX_CURRENT_WARS)
                return "already engaged in conflict elsewhere";

            if (!IsMilitarilyViable(self, target))
                return "insufficient military strength for victory";

            if (!IsTargetVulnerable(self, target) && !IsDecisiveAdvantage(self, target))
                return "waiting for target to become vulnerable";

            return "strategic considerations advise caution";
        }

        /// <summary>
        /// Get human-readable explanation for why peace was forced
        /// </summary>
        public static string GetForcedPeaceReason(Kingdom self, Kingdom target)
        {
            if (!self.IsAtWarWith(target)) return "";

            var stance = self.GetStanceWith(target);
            if (stance?.IsAtWar != true) return "";

            float warDuration = (float) (CampaignTime.Now - stance.WarStartDate).ToDays;

            if (warDuration >= MAX_WAR_DURATION_DAYS)
                return "prolonged conflict has exhausted the realm";

            if (warDuration >= STALEMATE_DURATION_DAYS && IsInStalemate(self, target))
                return "stalemate has proven neither side can achieve victory";

            if (IsWarEconomicallyUnsustainable(self, warDuration))
                return "the economic cost of war has become unbearable";

            return "strategic wisdom demands an end to hostilities";
        }
    }
}