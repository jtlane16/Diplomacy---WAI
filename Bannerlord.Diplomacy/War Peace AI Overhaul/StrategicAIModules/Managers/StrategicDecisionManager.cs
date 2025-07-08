using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;

using WarAndAiTweaks.Strategic; // Add this for ConquestStrategy
using WarAndAiTweaks.Strategic.Diplomacy;
using WarAndAiTweaks.Strategic.Scoring;

namespace WarAndAiTweaks.Strategic.Decision
{
    public class StrategicDecisionManager
    {
        private WarScorer _warScorer;
        private PeaceScorer _peaceScorer;
        private PeaceNegotiationManager _peaceManager;
        private RunawayFactionAnalyzer _runawayAnalyzer;

        public StrategicDecisionManager(WarScorer warScorer, PeaceScorer peaceScorer,
            PeaceNegotiationManager peaceManager, RunawayFactionAnalyzer runawayAnalyzer)
        {
            _warScorer = warScorer;
            _peaceScorer = peaceScorer;
            _peaceManager = peaceManager;
            _runawayAnalyzer = runawayAnalyzer;
        }

        public bool ShouldConsiderPeace(Kingdom kingdom, ConquestStrategy strategy)
        {
            try
            {
                // Add null safety checks
                if (kingdom == null || kingdom.IsEliminated || strategy == null)
                    return false;

                var currentEnemies = FactionManager.GetEnemyKingdoms(kingdom);
                if (currentEnemies == null)
                    return false;

                var enemyCount = currentEnemies.Where(k => k != null && !k.IsEliminated).Count();
                if (enemyCount == 0)
                    return false;

                var recommendedWarLimit = _runawayAnalyzer.GetRecommendedWarLimit(kingdom);
                if (enemyCount > recommendedWarLimit)
                    return true;

                var validEnemies = currentEnemies.Where(k => k != null && !k.IsEliminated).ToList();
                var totalEnemyStrength = validEnemies.Sum(k => k.TotalStrength);
                var kingdomStrength = kingdom.TotalStrength;

                if (totalEnemyStrength > kingdomStrength * 1.5f)
                    return true;

                if (strategy.HasBetterExpansionTargets())
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error in ShouldConsiderPeace for {kingdom?.Name}: {ex.Message}",
                    Colors.Yellow));
                return false;
            }
        }

        public bool ShouldConsiderWar(Kingdom kingdom, ConquestStrategy strategy)
        {
            var activeWars = FactionManager.GetEnemyKingdoms(kingdom).Count();
            int warLimit = _runawayAnalyzer.GetRecommendedWarLimit(kingdom);

            if (activeWars >= warLimit) return false;

            // Emergency war conditions (always allow these)
            if (HasRunawayThreatTargets(kingdom))
                return true; // Always allow anti-runaway wars

            if (kingdom.Fiefs.Count <= 1)
                return true; // Desperate survival wars

            // Minimum viability check (much more lenient)
            if (!HasMinimumWarCapability(kingdom))
                return false;

            // Normal strategic war consideration
            return strategy.HasSuitableWarTargets();
        }

        private bool HasMinimumWarCapability(Kingdom kingdom)
        {
            // Much more lenient requirements that scale with kingdom size
            float minimumStrength = Math.Max(1000f, kingdom.Fiefs.Count * 800f); // 1k base + 800 per fief
            float minimumInfluence = Math.Max(50f, kingdom.Fiefs.Count * 40f);   // 50 base + 40 per fief

            // Allow war if kingdom meets EITHER strength OR influence requirement
            // OR if they have specific strategic opportunities
            if (kingdom.TotalStrength >= minimumStrength || kingdom.RulingClan.Influence >= minimumInfluence)
                return true;

            // Special exceptions for small kingdoms
            return HasSpecialWarConditions(kingdom);
        }

        private bool HasSpecialWarConditions(Kingdom kingdom)
        {
            // Allow wars for very small kingdoms in specific situations
            if (kingdom.Fiefs.Count <= 2)
            {
                // Desperate expansion against even smaller targets
                var viableTargets = Kingdom.All.Where(k => 
                    k != kingdom && !k.IsEliminated && !kingdom.IsAtWarWith(k) &&
                    k.Fiefs.Count <= kingdom.Fiefs.Count && // Target same size or smaller
                    kingdom.TotalStrength > k.TotalStrength * 0.6f); // Only need 60% strength

                if (viableTargets.Any())
                    return true;
            }

            // Coalition opportunities
            var potentialTargets = Kingdom.All.Where(k => 
                k != kingdom && !k.IsEliminated && !kingdom.IsAtWarWith(k));
            
            foreach (var target in potentialTargets)
            {
                int targetEnemies = FactionManager.GetEnemyKingdoms(target).Count();
                if (targetEnemies >= 2) // Target is already fighting multiple wars
                {
                    float combinedStrength = FactionManager.GetEnemyKingdoms(target).Sum(e => e.TotalStrength) + kingdom.TotalStrength;
                    if (combinedStrength > target.TotalStrength * 1.2f) // Coalition has advantage
                        return true;
                }
            }

            return false;
        }

        public void ConsiderPeaceDecisions(Kingdom kingdom, ConquestStrategy strategy)
        {
            // FIXED: Add comprehensive null safety checks
            if (kingdom == null || kingdom.IsEliminated || strategy == null)
                return;

            var enemies = FactionManager.GetEnemyKingdoms(kingdom)?.ToList();
            if (enemies == null || !enemies.Any()) return;

            _peaceManager.CheckForPendingPeaceProposals(kingdom, strategy);

            var peaceCandidates = new List<(Kingdom Kingdom, float Priority)>();

            // FIXED: Use safe iteration with explicit null checks
            foreach (var enemy in enemies)
            {
                // PERF: Comprehensive validation before processing
                if (enemy == null || enemy.IsEliminated || enemy == kingdom ||
                    _peaceManager.HasActivePeaceProposal(kingdom, enemy) ||
                    _peaceManager.HasRecentPeaceOffer(kingdom, enemy))
                    continue;

                try
                {
                    float priority = _peaceScorer.CalculatePeacePriority(kingdom, enemy, strategy);
                    peaceCandidates.Add((enemy, priority));
                }
                catch (Exception ex)
                {
                    // PERF: Log and continue instead of crashing
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"[Debug] Peace calculation error for {kingdom?.Name} vs {enemy?.Name}: {ex.Message}",
                        Colors.Yellow));
                    continue;
                }
            }

            if (!peaceCandidates.Any()) return;

            var bestPeaceTarget = peaceCandidates.OrderByDescending(x => x.Priority).FirstOrDefault();
            float peaceThreshold = _peaceScorer.CalculatePeaceThreshold(kingdom);

            if (bestPeaceTarget.Priority > peaceThreshold)
            {
                int tributeAmount = CalculatePeaceTribute(kingdom, bestPeaceTarget.Kingdom);
                _peaceManager.InitiatePeaceProposal(kingdom, bestPeaceTarget.Kingdom, tributeAmount);
            }
        }

        public void ConsiderWarDecisions(Kingdom kingdom, ConquestStrategy strategy)
        {
            var potentialTargets = Kingdom.All
                .Where(k => k != kingdom && !k.IsEliminated &&
                       !k.IsAtWarWith(kingdom) && k.IsMapFaction)
                .ToList();

            if (!potentialTargets.Any()) return;

            var warCandidates = potentialTargets
                .Select(target => new
                {
                    Kingdom = target,
                    Priority = _warScorer.CalculateWarPriority(kingdom, target, strategy)
                })
                .OrderByDescending(x => x.Priority)
                .ToList();

            var bestWarTarget = warCandidates.FirstOrDefault();
            float requiredPriority = HasRunawayThreatTargets(kingdom) ? 50f : 70f;

            if (bestWarTarget?.Priority > requiredPriority)
            {
                InitiateWarDeclaration(kingdom, bestWarTarget.Kingdom);
            }
        }

        private bool HasRunawayThreatTargets(Kingdom kingdom)
        {
            var threats = _runawayAnalyzer.GetAllThreats(kingdom);
            return threats.Any(t => !kingdom.IsAtWarWith(t));
        }

        private void InitiateWarDeclaration(Kingdom kingdom, Kingdom target)
        {
            var rulingClan = kingdom.RulingClan;

            if (kingdom.UnresolvedDecisions.Any(d => d is DeclareWarDecision war &&
                war.FactionToDeclareWarOn == target))
                return;

            var warDecision = new DeclareWarDecision(rulingClan, target);

            if (warDecision.CalculateSupport(rulingClan) > 50f)
            {
                kingdom.AddDecision(warDecision, true);

                string warType = _runawayAnalyzer.IsRunawayThreat(target)
                    ? "[Anti-Runaway]" : "[Strategic AI]";

                InformationManager.DisplayMessage(new InformationMessage(
                    $"{warType} {kingdom.Name} declares war on {target.Name}",
                    Colors.Red));
            }
        }

        public int CalculatePeaceTribute(Kingdom kingdom, Kingdom enemy)
        {
            if (kingdom == null || enemy == null)
                return 0;

            var clan = kingdom.RulingClan;
            var otherClan = enemy.RulingClan;

            // Use PeaceBarterable to get the value for the other faction
            int value = -new PeaceBarterable(clan.Leader, kingdom, enemy, CampaignTime.Years(1f)).GetValueForFaction(enemy);

            // Clamp value to 0 if between -5000 and 5000 (as in base game)
            if (value > -5000 && value < 5000)
                value = 0;

            // Get daily tribute for the value
            int dailyTribute = Campaign.Current.Models.DiplomacyModel.GetDailyTributeForValue(value);

            // Optionally, multiply by 10 for a lump sum
            int lumpSum = dailyTribute * 10;

            // Clamp to a reasonable range
            return Clamp(lumpSum, -5000, 5000);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}