using Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI;

using System;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

using MathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.WarPeaceAI
{
    /// <summary>
    /// Simple safeguards to prevent AI from making terrible decisions
    /// </summary>
    public static class StrategicSafeguards
    {
        private const int MAX_CURRENT_WARS = 1;           // Objective #9: No multiple wars
        private const int MAX_WAR_DURATION_DAYS = 120;    // Safety net for forever wars
        private const float MIN_POWER_RATIO_FOR_WAR = 0.7f; // Basic military viability

        /// <summary>
        /// Core safeguard: Is declaring war strategically wise? (Objective #9)
        /// </summary>
        public static bool IsWarDeclarationWise(Kingdom self, Kingdom target)
        {
            // NEW: Prevent any kingdom from declaring war if it has no viable targets
            var kingdoms = Kingdom.All.Where(k =>
                k != self && !k.IsEliminated && !k.IsMinorFaction && k.Leader != null && !self.IsAtWarWith(k)
            ).ToList();

            bool hasViableTarget = kingdoms.Any(k =>
                self.TotalStrength >= k.TotalStrength * MIN_POWER_RATIO_FOR_WAR
            );

            // If there are no viable targets, do not allow war declaration
            if (!hasViableTarget)
                return false;

            // Core rule: No multiple wars unless coalition emergency
            int currentWars = KingdomLogicHelpers.GetEnemyKingdoms(self).Count;
            if (currentWars >= MAX_CURRENT_WARS)
            {
                // Exception: Coalition against significant threat
                if (CoalitionSystem.IsSnowballThreat(target))
                {
                    var biggestThreat = CoalitionSystem.GetBiggestThreat();
                    if (biggestThreat != null && IsSignificantThreat(biggestThreat))
                        return true; // Coalition emergency override
                }
                return false;
            }

            // Basic military viability check
            float powerRatio = self.TotalStrength / Math.Max(target.TotalStrength, 1f);
            if (powerRatio < MIN_POWER_RATIO_FOR_WAR)
                return false; // Too weak to attack

            return true;
        }

        /// <summary>
        /// Decision timing safeguard (Objective #6)
        /// </summary>
        public static bool IsDecisionTooSoon(Kingdom self, Kingdom target, bool proposingWar)
        {
            var stance = self.GetStanceWith(target);
            if (stance == null) return false;

            if (proposingWar && !stance.IsAtWar)
            {
                float peaceDuration = (float) (CampaignTime.Now - stance.PeaceDeclarationDate).ToDays;
                return peaceDuration < 12f; // Minimum peace duration
            }
            else if (!proposingWar && stance.IsAtWar)
            {
                float warDuration = (float) (CampaignTime.Now - stance.WarStartDate).ToDays;
                return warDuration < 12f; // Minimum war duration
            }

            return false;
        }

        /// <summary>
        /// Forever war prevention
        /// </summary>
        public static bool ShouldForceWarEnd(Kingdom self, Kingdom target)
        {
            if (!self.IsAtWarWith(target)) return false;

            var stance = self.GetStanceWith(target);
            if (stance?.IsAtWar != true) return false;

            float warDuration = (float) (CampaignTime.Now - stance.WarStartDate).ToDays;

            // Force peace after maximum duration (safety net)
            return warDuration >= MAX_WAR_DURATION_DAYS;
        }

        /// <summary>
        /// Additional stance pressure to guide decisions
        /// </summary>
        public static float GetSafeguardStanceAdjustment(Kingdom self, Kingdom target)
        {
            float adjustment = 0f;

            // Strong penalty for multiple wars (supports objective #9)
            int currentWars = KingdomLogicHelpers.GetEnemyKingdoms(self).Count;
            if (currentWars > 0 && !self.IsAtWarWith(target))
            {
                adjustment -= 15f; // Strong penalty against new wars
            }

            // Escalating war weariness (supports natural end around 30 days)
            if (self.IsAtWarWith(target))
            {
                var stance = self.GetStanceWith(target);
                if (stance?.IsAtWar == true)
                {
                    float warDuration = (float) (CampaignTime.Now - stance.WarStartDate).ToDays;

                    if (warDuration > 50) adjustment -= 5f;   // was 30
                    if (warDuration > 80) adjustment -= 10f;  // was 60
                    if (warDuration > 110) adjustment -= 15f; // was 90
                    if (warDuration > 140) adjustment -= 25f; // was 120
                }
            }

            return MathF.Clamp(adjustment, -30f, 0f); // Only negative adjustments (safer decisions)
        }

        private static bool IsSignificantThreat(Kingdom kingdom)
        {
            var kingdoms = Kingdom.All.Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null).ToList();
            if (kingdoms.Count == 0) return false;

            float avgStrength = (float) kingdoms.Average(k => k.TotalStrength);
            return kingdom.TotalStrength >= avgStrength * 1.6f; // 60% above average = significant
        }
    }
}