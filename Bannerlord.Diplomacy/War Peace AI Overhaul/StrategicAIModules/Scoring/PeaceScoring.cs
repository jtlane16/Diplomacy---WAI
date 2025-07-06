using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

using WarAndAiTweaks.Strategic; // Add this line for ConquestStrategy

using MathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.Strategic.Scoring
{
    public class PeaceScorer
    {
        private RunawayFactionAnalyzer _runawayAnalyzer;
        private WarScorer _warScorer; // FIXED: Store WarScorer instead of nested dictionary

        private const int SOFT_COMMITMENT_DAYS = 20; // Soft commitment period

        public PeaceScorer(RunawayFactionAnalyzer runawayAnalyzer, WarScorer warScorer)
        {
            _runawayAnalyzer = runawayAnalyzer;
            _warScorer = warScorer; // FIXED: Store the WarScorer instead of the nested dictionary
        }

        public float CalculatePeacePriority(Kingdom kingdom, Kingdom enemy, ConquestStrategy strategy)
        {
            // FIXED: Add comprehensive null safety checks at the start
            if (kingdom == null || enemy == null || strategy == null ||
                kingdom.IsEliminated || enemy.IsEliminated)
                return 0f;

            float priority = 0f;

            try
            {
                // Apply runaway faction modifier
                priority += _runawayAnalyzer?.CalculatePeacePriorityModifier(kingdom, enemy) ?? 0f;

                // Strength disparity scoring
                priority += CalculateStrengthDisparityScore(kingdom, enemy);

                // Territorial loss scoring
                priority += CalculateTerritorialLossScore(kingdom, enemy, strategy);

                // Multi-war pressure scoring
                priority += CalculateMultiWarPressureScore(kingdom);

                // Strategic opportunity scoring
                priority += CalculateStrategicOpportunityScore(kingdom, strategy);

                // Elimination resistance scoring
                priority += CalculateEliminationResistanceScore(enemy);

                // Geographical positioning scoring
                priority += CalculateGeographicalPositioningScore(kingdom, enemy, strategy);

                // War commitment penalty (SOFT COMMITMENT)
                priority += CalculateWarCommitmentPenalty(kingdom, enemy);
            }
            catch (Exception ex)
            {
                // PERF: Log error and return safe default
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Peace priority calculation error: {ex.Message}",
                    Colors.Yellow));
                return 0f;
            }

            return MathF.Clamp(priority, 0f, 100f);
        }

        private float CalculateStrengthDisparityScore(Kingdom kingdom, Kingdom enemy)
        {
            if (kingdom == null || enemy == null) return 0f;

            float strengthRatio = enemy.TotalStrength / Math.Max(kingdom.TotalStrength, 1f);

            // Higher priority to make peace with much stronger enemies
            if (strengthRatio >= 2.5f) return 35f; // Overwhelming enemy
            if (strengthRatio >= 2.0f) return 25f; // Very strong enemy
            if (strengthRatio >= 1.5f) return 15f; // Strong enemy
            if (strengthRatio >= 1.2f) return 8f;  // Somewhat stronger

            return 0f;
        }

        private float CalculateTerritorialLossScore(Kingdom kingdom, Kingdom enemy, ConquestStrategy strategy)
        {
            if (kingdom == null || enemy == null || strategy == null)
                return 0f;

            float score = 0f;

            try
            {
                // Higher priority if we're losing territory to them
                if (strategy.IsLosingTerritory(enemy))
                    score += 30f;

                // REMOVED: General territorial pressure check
                // REMOVED: if (strategy.HasRecentTerritorialLosses()) score += 15f;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Territorial loss calculation error: {ex.Message}",
                    Colors.Yellow));
                return 0f;
            }

            return score;
        }

        private float CalculateMultiWarPressureScore(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated) return 0f;

            try
            {
                // FIXED: Add null safety to prevent "Value cannot be null" error
                var enemyKingdoms = FactionManager.GetEnemyKingdoms(kingdom);
                if (enemyKingdoms == null) return 0f;

                int activeWars = enemyKingdoms.Where(k => k != null && !k.IsEliminated).Count();

                // Exponential pressure from multiple wars
                if (activeWars >= 4) return 40f; // Overwhelming
                if (activeWars >= 3) return 25f; // Very high pressure
                if (activeWars >= 2) return 12f; // High pressure

                return 0f;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Multi-war pressure calculation error: {ex.Message}",
                    Colors.Yellow));
                return 0f;
            }
        }

        private float CalculateStrategicOpportunityScore(Kingdom kingdom, ConquestStrategy strategy)
        {
            if (kingdom == null || strategy == null) return 0f;

            float score = 0f;

            try
            {
                // Higher priority if better targets are available
                if (strategy.HasBetterExpansionTargets())
                    score += 20f;

                // Check for runaway threats we should focus on
                var biggestThreat = _runawayAnalyzer?.GetBiggestThreat(kingdom);
                if (biggestThreat != null)
                    score += 15f;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Strategic opportunity calculation error: {ex.Message}",
                    Colors.Yellow));
            }

            return score;
        }

        private float CalculateEliminationResistanceScore(Kingdom enemy)
        {
            if (enemy == null || enemy.IsEliminated) return 0f;

            // STRONG penalty for making peace with nearly defeated enemies
            if (enemy.Fiefs?.Count <= 1) return -60f; // Don't let them escape!
            if (enemy.Fiefs?.Count <= 2) return -40f; // Almost finished
            if (enemy.Fiefs?.Count <= 3) return -25f; // Very weak
            if (enemy.Fiefs?.Count <= 5) return -15f; // Weak

            return 0f;
        }

        private float CalculateGeographicalPositioningScore(Kingdom kingdom, Kingdom enemy, ConquestStrategy strategy)
        {
            if (kingdom == null || enemy == null || strategy == null) return 0f;

            float score = 0f;

            try
            {
                // Prefer peace with non-bordering enemies (less strategic value)
                if (!strategy.IsBorderingKingdom(enemy))
                    score += 15f;

                // Prefer peace if they're not blocking our expansion
                if (!IsBlockingExpansion(kingdom, enemy, strategy))
                    score += 10f;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Geographical positioning calculation error: {ex.Message}",
                    Colors.Yellow));
            }

            return score;
        }

        private float CalculateWarCommitmentPenalty(Kingdom kingdom, Kingdom enemy)
        {
            if (kingdom == null || enemy == null || _warScorer == null) return 0f;

            try
            {
                var warStartTimes = _warScorer.GetWarStartTimes(); // FIXED: Get the war times when needed
                if (warStartTimes == null) return 0f;

                if (!warStartTimes.TryGetValue(kingdom, out var wars) ||
                    wars == null || !wars.TryGetValue(enemy, out var warStartTime))
                    return 0f; // No war record

                float daysSinceWarStart = warStartTime.ElapsedDaysUntilNow;

                // SOFT COMMITMENT: Heavy penalty for early peace, but not impossible
                if (daysSinceWarStart < SOFT_COMMITMENT_DAYS)
                {
                    float commitmentPenalty = (SOFT_COMMITMENT_DAYS - daysSinceWarStart) * -2.5f; // Up to -50 points

                    // Emergency situations can override commitment
                    if (IsInEmergencyMode(kingdom))
                        commitmentPenalty *= 0.5f; // Halve the penalty in emergencies

                    return commitmentPenalty;
                }

                // Small bonus for honoring commitment period
                if (daysSinceWarStart >= SOFT_COMMITMENT_DAYS && daysSinceWarStart < SOFT_COMMITMENT_DAYS + 10)
                    return 5f;

                return 0f;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] War commitment penalty calculation error: {ex.Message}",
                    Colors.Yellow));
                return 0f;
            }
        }

        private bool IsBlockingExpansion(Kingdom kingdom, Kingdom enemy, ConquestStrategy strategy)
        {
            if (kingdom == null || enemy == null || strategy == null) return false;

            try
            {
                // FIXED: Add null safety checks for all collections
                var priorityTargets = strategy.PriorityTargets;
                if (priorityTargets == null || !priorityTargets.Any()) return false;

                var enemySettlements = enemy.Settlements;
                if (enemySettlements == null || !enemySettlements.Any()) return false;

                var kingdomSettlements = kingdom.Settlements;
                if (kingdomSettlements == null || !kingdomSettlements.Any()) return false;

                // Check if enemy settlements are positioned to block expansion to priority targets
                return priorityTargets.Any(target =>
                {
                    if (target?.Settlements == null) return false;

                    return enemySettlements.Any(enemySettlement =>
                    {
                        if (enemySettlement == null) return false;

                        return target.Settlements.Any(targetSettlement =>
                        {
                            if (targetSettlement == null) return false;

                            return kingdomSettlements.Any(ourSettlement =>
                            {
                                if (ourSettlement == null) return false;

                                return IsGeographicallyBetween(ourSettlement, targetSettlement, enemySettlement);
                            });
                        });
                    });
                });
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] IsBlockingExpansion calculation error: {ex.Message}",
                    Colors.Yellow));
                return false;
            }
        }

        private bool IsGeographicallyBetween(Settlement from, Settlement to, Settlement middle)
        {
            if (from == null || to == null || middle == null) return false;

            try
            {
                // Simplified geographical check
                float distanceFromTo = from.Position2D.Distance(to.Position2D);
                float distanceFromMiddle = from.Position2D.Distance(middle.Position2D);
                float distanceMiddleTo = middle.Position2D.Distance(to.Position2D);

                // If going through middle is roughly the same distance, they're "between"
                return Math.Abs((distanceFromMiddle + distanceMiddleTo) - distanceFromTo) < 50f;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Geographical calculation error: {ex.Message}",
                    Colors.Yellow));
                return false;
            }
        }

        private bool IsInEmergencyMode(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated) return false;

            try
            {
                // FIXED: Add null safety to prevent "Value cannot be null" error
                var enemyKingdoms = FactionManager.GetEnemyKingdoms(kingdom);
                int activeWars = enemyKingdoms?.Where(k => k != null && !k.IsEliminated).Count() ?? 0;
                int fiefCount = kingdom.Fiefs?.Count ?? 0;

                return fiefCount <= 2 || // Very few fiefs left
                       activeWars >= 3 || // Fighting too many wars
                       (fiefCount <= 4 && activeWars >= 2); // Small kingdom fighting multiple wars
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Emergency mode calculation error: {ex.Message}",
                    Colors.Yellow));
                return false;
            }
        }

        public float CalculatePeaceThreshold(Kingdom kingdom)
        {
            if (kingdom == null) return 60f;

            float threshold = 60f; // Higher base threshold than current 50f

            try
            {
                // Lower threshold in emergency situations
                if (IsInEmergencyMode(kingdom))
                    threshold = 45f;

                // Runaway factions get higher threshold (more defensive)
                if (_runawayAnalyzer?.IsRunawayThreat(kingdom) == true)
                    threshold = 70f;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Peace threshold calculation error: {ex.Message}",
                    Colors.Yellow));
            }

            return threshold;
        }
    }
}