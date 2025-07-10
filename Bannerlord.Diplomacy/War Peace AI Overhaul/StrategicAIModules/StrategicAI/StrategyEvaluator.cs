using Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

using MathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.WarPeaceAI
{
    /// <summary>
    /// Calculates daily stance changes based on various factors
    /// </summary>
    public static class StrategyEvaluator
    {
        /// <summary>
        /// Calculates the daily stance change for one kingdom toward another
        /// </summary>
        public static float CalculateStanceChange(Kingdom self, Kingdom target)
        {
            if (self == null || target == null || self == target) return 0f;

            float change = 0f;
            bool atWar = self.IsAtWarWith(target);

            // 1. MILITARY FACTORS (±3 points)
            change += EvaluateMilitaryFactors(self, target);

            // 2. GEOGRAPHIC FACTORS (±2 points)  
            change += EvaluateGeographicFactors(self, target);

            // 3. DIPLOMATIC FACTORS (±2 points)
            change += EvaluateDiplomaticFactors(self, target, atWar);

            // 4. ECONOMIC FACTORS (±1.5 points)
            change += EvaluateEconomicFactors(self, target);

            // 5. PERSONALITY/RANDOM FACTORS (±1 point)
            change += EvaluatePersonalityFactors(self, target);

            // 6. WAR/PEACE MOMENTUM (±2 points)
            change += EvaluateWarPeaceMomentum(self, target, atWar);

            // 7. STRATEGIC SAFEGUARDS (up to -30 points)
            change += StrategicSafeguards.GetSafeguardStanceAdjustment(self, target);

            // 8. DECISION COMMITMENT (±8 points) ⬅️ ADD THIS
            change += EvaluateDecisionCommitment(self, target, atWar);

            return change;
        }

        private static float EvaluateMilitaryFactors(Kingdom self, Kingdom target)
        {
            float change = 0f;

            // Relative military strength using the power evaluator
            float relativeStrength = KingdomPowerEvaluator.GetRelativePower(self, target);

            if (relativeStrength > 1.3f)
                change += 1.5f; // Feeling strong, more aggressive
            else if (relativeStrength < 0.7f)
                change -= 1.5f; // Feeling weak, less aggressive

            // Check if either kingdom has significantly stronger armies
            if (IsStrongerArmy(self, target))
                change += 1.0f;
            else if (IsStrongerArmy(target, self))
                change -= 1.0f;

            return MathF.Clamp(change, -3f, 3f);
        }

        private static float EvaluateGeographicFactors(Kingdom self, Kingdom target)
        {
            float change = 0f;

            // Bordering kingdoms are more likely to have tensions
            if (KingdomLogicHelpers.AreBordering(self, target))
            {
                change += 0.8f; // Slight tendency toward conflict
            }

            // Distance penalty - far kingdoms are less interesting
            float distancePenalty = KingdomLogicHelpers.GetBorderDistancePenalty(self, target, 1.5f, 100f);
            change -= distancePenalty;

            return MathF.Clamp(change, -2f, 2f);
        }

        private static float EvaluateDiplomaticFactors(Kingdom self, Kingdom target, bool atWar)
        {
            float change = 0f;

            // Multiple wars penalty for the self kingdom
            int selfWars = KingdomLogicHelpers.GetEnemyKingdoms(self).Count;
            if (selfWars > 1)
                change -= 1.5f; // Strong penalty for multiple wars

            // If target is in multiple wars, they're a tempting target
            int targetWars = KingdomLogicHelpers.GetEnemyKingdoms(target).Count;
            if (targetWars > 2)
                change += 0.8f; // Target is distracted

            // COALITION SYSTEM: Check for snowball threats
            float coalitionAdjustment = CoalitionSystem.GetCoalitionStanceAdjustment(self, target);
            change += coalitionAdjustment;

            return MathF.Clamp(change, -2f, 2f);
        }

        private static float EvaluateEconomicFactors(Kingdom self, Kingdom target)
        {
            float change = 0f;

            // Wealthy targets are more tempting
            float targetWealth = GetKingdomWealth(target);
            float selfWealth = GetKingdomWealth(self);
            float avgWealth = GetAverageKingdomWealth();

            if (targetWealth > avgWealth * 1.4f && selfWealth < avgWealth)
                change += 1.2f; // Poor kingdom eyeing rich kingdom

            // Economic pressure on self makes more aggressive
            if (selfWealth < avgWealth * 0.6f)
                change += 0.8f; // Need resources, more aggressive

            // Rich kingdoms might be more cautious
            if (selfWealth > avgWealth * 1.4f)
                change -= 0.3f; // Less need for aggression

            return MathF.Clamp(change, -1.5f, 1.5f);
        }

        private static float EvaluatePersonalityFactors(Kingdom self, Kingdom target)
        {
            float change = 0f;

            // Cultural bias (deterministic based on culture relationships)
            float culturalBias = GetCulturalBias(self, target);
            change += culturalBias;

            // War stress bias (deterministic based on current state)
            int currentWars = KingdomLogicHelpers.GetEnemyKingdoms(self).Count;
            float warStressBias = currentWars > 0 ? -0.3f : 0.1f;
            change += warStressBias;

            // Leadership stability (deterministic based on kingdom traits)
            float leadershipFactor = GetLeadershipFactor(self, target);
            change += leadershipFactor;

            return MathF.Clamp(change, -1f, 1f);
        }

        private static float GetLeadershipFactor(Kingdom self, Kingdom target)
        {
            if (self?.Leader == null || target?.Leader == null) return 0f;

            // Deterministic "personality" based on kingdom characteristics
            float factor = 0f;

            // Kingdom size influences approach (larger = more cautious)
            float sizeRatio = (float) self.Settlements.Count / Math.Max(target.Settlements.Count, 1);
            if (sizeRatio > 1.2f)
                factor -= 0.2f; // Larger kingdoms more cautious
            else if (sizeRatio < 0.8f)
                factor += 0.2f; // Smaller kingdoms more aggressive

            // Economic position influences approach
            float selfWealth = GetKingdomWealth(self);
            float targetWealth = GetKingdomWealth(target);
            float wealthRatio = selfWealth / Math.Max(targetWealth, 1f);

            if (wealthRatio > 1.3f)
                factor -= 0.1f; // Rich kingdoms less aggressive
            else if (wealthRatio < 0.7f)
                factor += 0.1f; // Poor kingdoms more desperate

            return factor;
        }

        private static float EvaluateWarPeaceMomentum(Kingdom self, Kingdom target, bool atWar)
        {
            float change = 0f;

            if (atWar)
            {
                // War duration effects
                var warDuration = GetWarDuration(self, target);

                if (warDuration < 5) // Very short wars - momentum to continue
                    change += 1.0f;
                else if (warDuration < 15) // Short wars - slight pressure to continue
                    change += 0.3f;
                else if (warDuration > 30) // Long wars - war weariness begins
                    change -= 1.0f;
                else if (warDuration > 60) // Very long wars - strong peace pressure
                    change -= 2.0f;

                // Short war peace penalty (don't make peace too quickly)
                change += KingdomLogicHelpers.GetShortWarPeacePenalty(self, target, 10, 1.5f);
            }
            else
            {
                // Peace duration effects
                var peaceDuration = GetPeaceDuration(self, target);

                if (peaceDuration < 5) // Very recent peace - strong stability bonus
                    change -= 1.5f;
                else if (peaceDuration < 15) // Recent peace - moderate stability bonus
                    change -= 0.8f;
                else if (peaceDuration > 40) // Long peace - building tensions
                    change += 0.5f;
                else if (peaceDuration > 80) // Very long peace - significant tensions
                    change += 0.8f;

                // Short peace war penalty (don't declare war too quickly after peace)
                change += KingdomLogicHelpers.GetShortPeaceWarPenalty(self, target, 10, 1.5f);
            }

            return MathF.Clamp(change, -2f, 2f);
        }

        // Helper method implementations using existing game systems
        private static bool IsStrongerArmy(Kingdom self, Kingdom target)
        {
            if (target == null || self == null || target.TotalStrength <= 0)
                return false;
            return self.TotalStrength >= 1.2f * target.TotalStrength;
        }

        private static float GetKingdomWealth(Kingdom kingdom)
        {
            if (kingdom == null) return 0f;

            // Calculate total economic power from settlement prosperity only
            float totalProsperity = kingdom.Settlements
                .Where(s => s?.Town != null) // Only towns and castles have prosperity
                .Sum(s => s.Town.Prosperity);

            return totalProsperity;
        }

        private static float GetAverageKingdomWealth()
        {
            var kingdoms = Kingdom.All
                .Where(k => k != null && !k.IsEliminated && !k.IsMinorFaction)
                .ToList();

            if (kingdoms.Count == 0) return 100000f;

            return (float) kingdoms.Average(k => GetKingdomWealth(k));
        }

        private static float GetCulturalBias(Kingdom self, Kingdom target)
        {
            // Different cultures might have natural tensions or affinities
            // This is a simplified implementation - could be much more sophisticated

            if (self?.Culture == null || target?.Culture == null)
                return 0f;

            // Same culture = slight peace bias
            if (self.Culture == target.Culture)
                return -0.2f;

            // Different cultures = slight tension bias
            return 0.1f;
        }

        private static float GetWarDuration(Kingdom self, Kingdom target)
        {
            var stance = self.GetStanceWith(target);
            if (stance == null || !stance.IsAtWar)
                return 0f;

            return (float) (CampaignTime.Now - stance.WarStartDate).ToDays;
        }

        private static float GetPeaceDuration(Kingdom self, Kingdom target)
        {
            var stance = self.GetStanceWith(target);
            if (stance == null || stance.IsAtWar)
                return 0f;

            return (float) (CampaignTime.Now - stance.PeaceDeclarationDate).ToDays;
        }

        private static float EvaluateDecisionCommitment(Kingdom self, Kingdom target, bool atWar)
        {
            float change = 0f;

            if (atWar)
            {
                var stance = self.GetStanceWith(target);
                if (stance?.IsAtWar == true)
                {
                    float warDuration = (float) (CampaignTime.Now - stance.WarStartDate).ToDays;

                    // Strong commitment bonus for first 15 days of war
                    if (warDuration < 15)
                        change += 8f; // "We just declared war, we're committed!"
                                      // Moderate commitment for next 15 days  
                    else if (warDuration < 30)
                        change += 4f; // "Still building momentum"
                }
            }
            else
            {
                var stance = self.GetStanceWith(target);
                if (stance?.IsAtWar == false)
                {
                    float peaceDuration = (float) (CampaignTime.Now - stance.PeaceDeclarationDate).ToDays;

                    // Strong peace commitment for first 15 days
                    if (peaceDuration < 15)
                        change -= 8f; // "We just made peace, let's stick with it!"
                                      // Moderate peace commitment for next 15 days
                    else if (peaceDuration < 30)
                        change -= 4f; // "Still enjoying peace"
                }
            }

            return change;
        }
    }

    /// <summary>
    /// Evaluates relative power between kingdoms (moved from WarPeaceScoring.cs)
    /// </summary>
    internal static class KingdomPowerEvaluator
    {
        private static float GetOverallPowerScore(Kingdom kingdom)
        {
            // TotalStrength
            float strength = kingdom.TotalStrength;

            // Average prosperity across all settlements
            var settlements = kingdom.Settlements.Where(s => s != null && (s.IsTown || s.IsCastle)).ToList();
            float avgProsperity = settlements.Count > 0
                ? (float) settlements.Average(s => s.Town.Prosperity)
                : 0f;

            // Average wealth of all lords in the kingdom
            var lords = kingdom.Clans
                .Where(c => !c.IsEliminated && c.Leader != null)
                .Select(c => c.Leader)
                .Where(h => h.IsLord)
                .ToList();

            float avgWealth = lords.Count > 0
                ? (float) lords.Average(l => l.Gold)
                : 0f;

            // Combine the metrics (weights can be adjusted as needed)
            return strength + avgProsperity + avgWealth;
        }

        public static float GetRelativePower(Kingdom kingdomA, Kingdom kingdomB)
        {
            if (kingdomA == null || kingdomB == null)
                return 1f;
            float powerA = GetOverallPowerScore(kingdomA);
            float powerB = GetOverallPowerScore(kingdomB);
            if (powerB <= 0.01f) return 1f;
            return powerA / powerB;
        }
    }

    /// <summary>
    /// Extension methods for easier strategy access
    /// </summary>
    public static class StrategyExtensions
    {
        /// <summary>
        /// Gets the strategic stance of this kingdom toward another
        /// </summary>
        public static float GetStrategicStance(this Kingdom self, Kingdom target)
        {
            var controller = Campaign.Current?.GetCampaignBehavior<KingdomLogicController>();
            return controller?.GetKingdomStance(self, target) ?? 50f;
        }

        /// <summary>
        /// Gets a readable description of this kingdom's stance toward another
        /// </summary>
        public static string GetStanceDescription(this Kingdom self, Kingdom target)
        {
            var controller = Campaign.Current?.GetCampaignBehavior<KingdomLogicController>();
            var strategy = controller?.GetKingdomStrategy(self);
            return strategy?.GetStanceDescription(target) ?? "Unknown";
        }

        /// <summary>
        /// Checks if this kingdom should consider war against another based on strategy
        /// </summary>
        public static bool ShouldConsiderWar(this Kingdom self, Kingdom target)
        {
            var controller = Campaign.Current?.GetCampaignBehavior<KingdomLogicController>();
            var strategy = controller?.GetKingdomStrategy(self);
            return strategy?.ShouldConsiderWar(target) ?? false;
        }

        /// <summary>
        /// Checks if this kingdom should consider peace with another based on strategy
        /// </summary>
        public static bool ShouldConsiderPeace(this Kingdom self, Kingdom target)
        {
            var controller = Campaign.Current?.GetCampaignBehavior<KingdomLogicController>();
            var strategy = controller?.GetKingdomStrategy(self);
            return strategy?.ShouldConsiderPeace(target) ?? false;
        }
    }
}