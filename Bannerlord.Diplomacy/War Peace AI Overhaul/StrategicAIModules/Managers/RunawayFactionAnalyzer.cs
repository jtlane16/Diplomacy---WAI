using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

using MathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.Strategic
{
    public class RunawayFactionAnalyzer
    {
        private Dictionary<Kingdom, RunawayThreatData> _threatData = new Dictionary<Kingdom, RunawayThreatData>();
        private CampaignTime _lastAnalysis = CampaignTime.Zero;

        // Configuration constants
        private const float HIGH_THREAT_THRESHOLD = 70f;
        private const int ANALYSIS_INTERVAL_DAYS = 3;
        private const int MINIMUM_KINGDOMS_FOR_ANALYSIS = 3;

        public void UpdateAnalysis()
        {
            if (_lastAnalysis.ElapsedDaysUntilNow < ANALYSIS_INTERVAL_DAYS)
                return;

            var activeKingdoms = Kingdom.All.Where(k => !k.IsEliminated).ToList();
            if (activeKingdoms.Count < MINIMUM_KINGDOMS_FOR_ANALYSIS)
                return;

            AnalyzeRunawayThreats(activeKingdoms);
            _lastAnalysis = CampaignTime.Now;
        }

        public bool IsRunawayThreat(Kingdom kingdom)
        {
            return _threatData.TryGetValue(kingdom, out var data) && data.IsHighThreat;
        }

        public float GetThreatLevel(Kingdom kingdom)
        {
            return _threatData.TryGetValue(kingdom, out var data) ? data.CurrentThreatLevel : 0f;
        }

        public bool HasBeenThreatFor(Kingdom kingdom, int minimumDays)
        {
            return _threatData.TryGetValue(kingdom, out var data) &&
                   data.IsHighThreat &&
                   data.DaysAsHighThreat >= minimumDays;
        }

        public Kingdom GetBiggestThreat(Kingdom excludeKingdom = null)
        {
            return _threatData.Values
                .Where(t => t.Kingdom != excludeKingdom && t.IsHighThreat && !t.Kingdom.IsEliminated)
                .OrderByDescending(t => t.CurrentThreatLevel)
                .FirstOrDefault()?.Kingdom;
        }

        public List<Kingdom> GetAllThreats(Kingdom excludeKingdom = null)
        {
            return _threatData.Values
                .Where(t => t.Kingdom != excludeKingdom && t.IsHighThreat && !t.Kingdom.IsEliminated)
                .OrderByDescending(t => t.CurrentThreatLevel)
                .Select(t => t.Kingdom)
                .ToList();
        }

        public RunawayThreatData GetThreatData(Kingdom kingdom)
        {
            return _threatData.TryGetValue(kingdom, out var data) ? data : null;
        }

        // Calculate war priority modifier based on runaway threat analysis
        public float CalculateWarPriorityModifier(Kingdom attacker, Kingdom target)
        {
            float modifier = 0f;

            // Massive bonus for attacking runaway threats
            if (IsRunawayThreat(target))
            {
                modifier += 80f;

                // Additional bonus based on threat level
                modifier += GetThreatLevel(target) * 0.5f;

                // Coalition bonus - safer to attack when others are also fighting them
                int targetEnemies = FactionManager.GetEnemyKingdoms(target).Count();
                modifier += targetEnemies * 15f;

                // Emergency override - even weak kingdoms should consider attacking runaway threats
                float strengthRatio = attacker.TotalStrength / target.TotalStrength;
                if (strengthRatio > 0.3f) // Only need 30% strength vs runaway threats
                {
                    modifier += 20f; // Emergency action bonus
                }
            }

            return modifier;
        }

        // Calculate peace priority modifier based on runaway threat analysis
        public float CalculatePeacePriorityModifier(Kingdom peacemaker, Kingdom enemy)
        {
            float modifier = 0f;

            // Strong penalty for making peace with runaway threats
            if (IsRunawayThreat(enemy))
            {
                modifier -= 40f;
            }

            // Bonus for making peace to focus on bigger threats
            var biggestThreat = GetBiggestThreat(peacemaker);
            if (biggestThreat != null && biggestThreat != enemy)
            {
                modifier += 30f;
            }

            return modifier;
        }

        // Check if a kingdom should prioritize anti-runaway actions
        public bool ShouldPrioritizeAntiRunawayActions(Kingdom kingdom)
        {
            var biggestThreat = GetBiggestThreat(kingdom);
            if (biggestThreat == null) return false;

            // Prioritize if threat has been dominant for several days
            return HasBeenThreatFor(biggestThreat, 5) &&
                   !kingdom.IsAtWarWith(biggestThreat) &&
                   kingdom.TotalStrength > biggestThreat.TotalStrength * 0.3f;
        }

        // Check war limits based on runaway status
        public int GetRecommendedWarLimit(Kingdom kingdom)
        {
            if (IsRunawayThreat(kingdom))
            {
                // Runaway factions should be more defensive
                return 1;
            }

            // Normal kingdoms
            return 2;
        }

        public void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_threatData", ref _threatData);
            dataStore.SyncData("_lastAnalysis", ref _lastAnalysis);
        }

        private void AnalyzeRunawayThreats(List<Kingdom> kingdoms)
        {
            var metrics = kingdoms.Select(k => new KingdomMetrics(k)).ToList();
            float avgStrength = metrics.Average(m => m.TotalStrength);
            float avgFiefs = (float) metrics.Average(m => m.FiefCount);

            foreach (var metric in metrics)
            {
                float threatLevel = CalculateThreatLevel(metric, avgStrength, avgFiefs);

                if (!_threatData.TryGetValue(metric.Kingdom, out var data))
                {
                    data = new RunawayThreatData(metric.Kingdom);
                    _threatData[metric.Kingdom] = data;
                }

                data.UpdateThreatLevel(threatLevel);

                // Log new high threats
                if (data.IsHighThreat && data.DaysAsHighThreat == 1)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"[Runaway Detection] {metric.Kingdom.Name} identified as dominant threat!",
                        Colors.Red));
                }
            }
        }

        private float CalculateThreatLevel(KingdomMetrics metric, float avgStrength, float avgFiefs)
        {
            float threatLevel = 0f;

            // Strength dominance (0-40 points)
            float strengthRatio = metric.TotalStrength / avgStrength;
            if (strengthRatio > 2.0f) threatLevel += 40f;
            else if (strengthRatio > 1.5f) threatLevel += 25f;
            else if (strengthRatio > 1.3f) threatLevel += 15f;

            // Territorial dominance (0-30 points)
            float fiefRatio = metric.FiefCount / avgFiefs;
            if (fiefRatio > 2.0f) threatLevel += 30f;
            else if (fiefRatio > 1.5f) threatLevel += 20f;
            else if (fiefRatio > 1.3f) threatLevel += 10f;

            // Growth rate (0-20 points)
            if (_threatData.TryGetValue(metric.Kingdom, out var existingData))
            {
                float growthRate = existingData.EstimatedGrowthRate;
                if (growthRate > 0.2f) threatLevel += 20f;
                else if (growthRate > 0.1f) threatLevel += 10f;
                else if (growthRate > 0.05f) threatLevel += 5f;
            }

            // Market share (0-10 points)
            float totalFiefs = Kingdom.All.Where(k => !k.IsEliminated).Sum(k => k.Fiefs.Count);
            float marketShare = totalFiefs > 0 ? metric.FiefCount / totalFiefs : 0f;
            if (marketShare > 0.4f) threatLevel += 10f;
            else if (marketShare > 0.3f) threatLevel += 5f;

            return MathF.Clamp(threatLevel, 0f, 100f);
        }

        private class KingdomMetrics
        {
            public Kingdom Kingdom { get; }
            public float TotalStrength { get; }
            public int FiefCount { get; }

            public KingdomMetrics(Kingdom kingdom)
            {
                Kingdom = kingdom;
                TotalStrength = kingdom.TotalStrength;
                FiefCount = kingdom.Fiefs.Count;
            }
        }
    }

    // MOVED INSIDE NAMESPACE - This was the main issue!
    public class RunawayThreatData
    {
        [SaveableField(1)]
        public Kingdom Kingdom;

        [SaveableField(2)]
        public float CurrentThreatLevel;

        [SaveableField(3)]
        public int DaysAsHighThreat;

        [SaveableField(4)]
        public float EstimatedGrowthRate;

        [SaveableField(5)]
        public CampaignTime LastUpdate;

        [SaveableField(6)]
        public int PreviousFiefCount;

        public bool IsHighThreat => CurrentThreatLevel >= 70f;

        public RunawayThreatData(Kingdom kingdom)
        {
            Kingdom = kingdom;
            CurrentThreatLevel = 0f;
            DaysAsHighThreat = 0;
            EstimatedGrowthRate = 0f;
            LastUpdate = CampaignTime.Now;
            PreviousFiefCount = kingdom.Fiefs.Count;
        }

        // Parameterless constructor for SaveSystem
        public RunawayThreatData()
        {
        }

        public void UpdateThreatLevel(float newThreatLevel)
        {
            bool wasHighThreat = IsHighThreat;
            CurrentThreatLevel = newThreatLevel;

            // Track consecutive days as high threat
            if (IsHighThreat)
            {
                if (wasHighThreat)
                    DaysAsHighThreat++;
                else
                    DaysAsHighThreat = 1;
            }
            else
            {
                DaysAsHighThreat = 0;
            }

            // Update growth rate estimation
            int currentFiefs = Kingdom.Fiefs.Count;
            if (currentFiefs > PreviousFiefCount)
            {
                float growthRate = (float) (currentFiefs - PreviousFiefCount) / (float) Math.Max(PreviousFiefCount, 1);
                EstimatedGrowthRate = (EstimatedGrowthRate + growthRate) / 2f; // Moving average
            }
            PreviousFiefCount = currentFiefs;

            LastUpdate = CampaignTime.Now;
        }
    }
}