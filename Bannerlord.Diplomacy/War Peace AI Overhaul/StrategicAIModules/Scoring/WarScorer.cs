using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

using WarAndAiTweaks.Strategic;

using MathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.Strategic.Scoring
{
    // NEW: Simple war record structure instead of nested dictionary
    public class WarRecord
    {
        [SaveableField(1)]
        public Kingdom Attacker;

        [SaveableField(2)]
        public Kingdom Target;

        [SaveableField(3)]
        public CampaignTime WarStartTime;

        public WarRecord() { }

        public WarRecord(Kingdom attacker, Kingdom target, CampaignTime startTime)
        {
            Attacker = attacker;
            Target = target;
            WarStartTime = startTime;
        }
    }

    public class WarScorer
    {
        private RunawayFactionAnalyzer _runawayAnalyzer;

        // REPLACED: Nested dictionary with simple list
        private List<WarRecord> _warRecords = new List<WarRecord>();

        public WarScorer(RunawayFactionAnalyzer runawayAnalyzer)
        {
            _runawayAnalyzer = runawayAnalyzer;
        }

        // Helper method to get war start times (replaces the old property)
        public Dictionary<Kingdom, Dictionary<Kingdom, CampaignTime>> GetWarStartTimes()
        {
            var result = new Dictionary<Kingdom, Dictionary<Kingdom, CampaignTime>>();

            foreach (var record in _warRecords)
            {
                if (!result.TryGetValue(record.Attacker, out var targets))
                {
                    targets = new Dictionary<Kingdom, CampaignTime>();
                    result[record.Attacker] = targets;
                }
                targets[record.Target] = record.WarStartTime;
            }

            return result;
        }

        public float CalculateWarPriority(Kingdom kingdom, Kingdom target, ConquestStrategy strategy)
        {
            float priority = 0f;

            // Apply runaway faction modifier first (can override normal strength requirements)
            float runawayModifier = _runawayAnalyzer.CalculateWarPriorityModifier(kingdom, target);
            priority += runawayModifier;

            // If this is a high-priority runaway target, reduce strength requirements
            if (runawayModifier > 50f)
            {
                float strengthRatio = kingdom.TotalStrength / target.TotalStrength;
                if (strengthRatio > 0.3f) // Only need 30% strength vs runaway threats
                {
                    return MathF.Clamp(priority, 50f, 100f); // Ensure high priority
                }
            }

            // Normal war priority calculations
            float normalStrengthRatio = kingdom.TotalStrength / target.TotalStrength;
            if (normalStrengthRatio < 1.2f) return 0f;

            // Base strength advantage scoring
            priority += CalculateStrengthAdvantageScore(normalStrengthRatio);

            // Opportunistic scoring
            priority += CalculateOpportunisticScore(kingdom, target);

            // Strategic value scoring
            priority += CalculateStrategicValueScore(kingdom, target, strategy);

            // Geographical scoring
            priority += CalculateGeographicalScore(kingdom, target, strategy);

            // Elimination priority scoring
            priority += CalculateEliminationScore(target);

            // Recent peace treaty penalty
            priority += CalculateRecentPeacePenalty(kingdom, target);

            return MathF.Clamp(priority, 0f, 100f);
        }

        private float CalculateStrengthAdvantageScore(float strengthRatio)
        {
            if (strengthRatio >= 2.0f) return 35f;
            if (strengthRatio >= 1.5f) return 25f;
            if (strengthRatio >= 1.2f) return 15f;
            return 0f;
        }

        private float CalculateOpportunisticScore(Kingdom kingdom, Kingdom target)
        {
            float score = 0f;

            int targetWars = FactionManager.GetEnemyKingdoms(target).Count();
            if (targetWars >= 3) score += 25f;
            else if (targetWars >= 2) score += 15f;
            else if (targetWars >= 1) score += 8f;

            if (targetWars >= 2)
            {
                float combinedEnemyStrength = FactionManager.GetEnemyKingdoms(target)
                    .Sum(enemy => enemy.TotalStrength) + kingdom.TotalStrength;
                float coalitionRatio = combinedEnemyStrength / target.TotalStrength;

                if (coalitionRatio > 2.0f) score += 20f;
                else if (coalitionRatio > 1.5f) score += 12f;
            }

            return score;
        }

        private float CalculateStrategicValueScore(Kingdom kingdom, Kingdom target, ConquestStrategy strategy)
        {
            float score = 0f;

            score += strategy.CalculateStrategicValue(target) * 0.3f;

            if (strategy.WouldCreateStrategicAdvantage(target))
                score += 15f;

            if (target.Settlements.Contains(target.FactionMidSettlement))
                score += 10f;

            return score;
        }

        private float CalculateGeographicalScore(Kingdom kingdom, Kingdom target, ConquestStrategy strategy)
        {
            float score = 0f;

            if (strategy.IsBorderingKingdom(target))
                score += 25f;

            if (WouldConsolidateTerritory(kingdom, target))
                score += 15f;

            return score;
        }

        private float CalculateEliminationScore(Kingdom target)
        {
            float score = 0f;

            if (target.Fiefs.Count <= 1) score += 50f;
            else if (target.Fiefs.Count <= 2) score += 35f;
            else if (target.Fiefs.Count <= 3) score += 20f;
            else if (target.Fiefs.Count <= 5) score += 10f;

            return score;
        }

        private float CalculateRecentPeacePenalty(Kingdom kingdom, Kingdom target)
        {
            var stance = kingdom.GetStanceWith(target);
            float daysSincePeace = stance.PeaceDeclarationDate.ElapsedDaysUntilNow;

            if (daysSincePeace < 30f) return -40f;
            if (daysSincePeace < 60f) return -20f;
            if (daysSincePeace < 90f) return -10f;

            return 0f;
        }

        private bool WouldConsolidateTerritory(Kingdom kingdom, Kingdom target)
        {
            return kingdom.Settlements.Any(ourSettlement =>
                target.Settlements.Any(theirSettlement =>
                    ourSettlement.Position2D.Distance(theirSettlement.Position2D) < 150f));
        }

        public void RecordWarStart(Kingdom kingdom, Kingdom target)
        {
            // Remove any existing record for this war
            _warRecords.RemoveAll(r => r.Attacker == kingdom && r.Target == target);

            // Add new record
            _warRecords.Add(new WarRecord(kingdom, target, CampaignTime.Now));
        }

        public void SyncData(IDataStore dataStore)
        {
            // FIXED: Now saving simple list instead of nested dictionary
            dataStore.SyncData("_warRecords", ref _warRecords);
        }
    }
}