// ╔══════════════════════════════════════════════════════════════════════════════════════════════════════════════════╗
// ║                                           STRATEGIC ENGINE                                                       ║
// ║                                    Complete Strategic Logic & Scoring                                           ║
// ╚══════════════════════════════════════════════════════════════════════════════════════════════════════════════════╝
//
//  This file contains ALL strategic logic for the Bannerlord AI system:
//  • Configuration constants (ScoringConfig)
//  • War and Peace scoring algorithms  
//  • Strategic analysis and opportunity assessment
//  • Target prioritization and geographical analysis
//  • Conquest strategy state management
//
//  The StrategicConquestAI file simply orchestrates this engine.
//
// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

using WarAndAiTweaks.Strategic.Decision;

using MathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.Strategic.Scoring
{
    #region 🎛️ GLOBAL CONFIGURATION - All Strategic Behavior Tuning

    /// <summary>
    /// Master configuration for all strategic AI behavior.
    /// Modify these constants to rebalance war/peace decisions, target prioritization, and strategic analysis.
    /// </summary>
    public static class ScoringConfig
    {
        // ═══════════════ WAR SCORING ═══════════════
        public const float War_StrengthRatioHigh = 2.0f;      // ≥ this ratio → High war score
        public const float War_StrengthRatioMed = 1.5f;       // Medium strength advantage
        public const float War_StrengthRatioLow = 1.2f;       // Minimum strength to consider war
        public const float War_StrengthScoreHigh = 35f;       // Score for high strength advantage
        public const float War_StrengthScoreMed = 25f;        // Score for medium advantage
        public const float War_StrengthScoreLow = 15f;        // Score for low advantage

        // Opportunistic warfare (target already fighting others)
        public const float War_TargetEngaged_3Wars = 25f;     // Bonus if target fighting 3+ wars
        public const float War_TargetEngaged_2Wars = 15f;     // Bonus if target fighting 2 wars
        public const float War_TargetEngaged_1War = 8f;       // Bonus if target fighting 1 war
        public const float War_CoalitionRatioHigh = 2.0f;     // Coalition strength / target strength
        public const float War_CoalitionBonusHigh = 20f;      // High coalition bonus
        public const float War_CoalitionBonusMed = 12f;       // Medium coalition bonus

        // Geographic and strategic bonuses
        public const float War_BorderBonus = 25f;             // Bonus for attacking neighbors
        public const float War_ConsolidationBonus = 15f;      // Bonus for territory consolidation
        public const float War_CapitalBonus = 10f;            // Bonus for targeting capitals
        public const float War_Elimination_1Fief = 50f;       // Bonus for eliminating 1-fief kingdoms
        public const float War_Elimination_2Fiefs = 35f;      // Bonus for eliminating 2-fief kingdoms
        public const float War_Elimination_3Fiefs = 20f;      // Bonus for eliminating 3-fief kingdoms
        public const float War_Elimination_5Fiefs = 10f;      // Bonus for eliminating 5-fief kingdoms

        // Peace treaty cooldowns (penalties for recent peace)
        public const float War_PeaceCooldown30 = -40f;        // Penalty for war within 30 days of peace
        public const float War_PeaceCooldown60 = -20f;        // Penalty for war within 60 days of peace
        public const float War_PeaceCooldown90 = -10f;        // Penalty for war within 90 days of peace

        // ═══════════════ PEACE SCORING ═══════════════
        public const float Peace_StrengthDisparity_Max = 40f; // Max score for strength disadvantage

        // War exhaustion thresholds
        public const float Peace_WarExhaustion_30d = 10f;     // Base exhaustion after 30 days
        public const float Peace_WarExhaustion_60d = 25f;     // Exhaustion after 60 days
        public const float Peace_WarExhaustion_90d = 80f;     // High exhaustion after 90 days
        public const float Peace_WarExhaustion_120d = 160f;   // Extreme exhaustion after 120 days

        public const float Peace_TerritoryLoss = 30f;         // Score for losing territory
        public const float Peace_StrategicOpportunity_BetterTargets = 20f;  // Score for having better expansion targets
        public const float Peace_StrategicOpportunity_RunawayThreat = 15f;  // Score for runaway faction threat
        public const float Peace_NonBorderBonus = 15f;        // Bonus for peace with non-neighbors
        public const float Peace_NotBlockingExpansionBonus = 10f; // Bonus if enemy not blocking expansion

        // War weariness system
        public const float Peace_WarWeariness_Threshold = 30f; // Days before weariness starts
        public const float Peace_WarWeariness_60d = 25f;      // Weariness score at 60 days
        public const float Peace_WarWeariness_90d = 80f;      // Weariness score at 90 days
        public const float Peace_WarWeariness_120d = 160f;    // Weariness score at 120 days
        public const float Peace_WarWeariness_Max = 160f;     // Maximum weariness

        // Multi-war pressure system
        public const float Peace_MultiWar_Base = 12f;         // Base score per additional war
        public const float Peace_MultiWar_Cap = 50f;          // Maximum multi-war penalty
        public const float Peace_MultiWar_ExpGrowth = 1.4f;   // Exponential growth for 3+ wars

        // War commitment system (prevents immediate peace after declaring war)
        public const int Peace_SoftCommitmentDays = 20;       // Days of commitment after declaring war
        public const float Peace_CommitmentPenaltyPerDay = -2.5f; // Daily penalty for early peace
        public const float Peace_CommitmentEarlyPenaltyCap = -50f; // Maximum early peace penalty
        public const float Peace_CommitmentHonourBonus = 5f;  // Bonus for honoring commitment

        // Elimination resistance (negative scores make peace harder)
        public const float Peace_EliminationPenaltyMax = -60f; // Maximum penalty for eliminating kingdoms

        // ═══════════════ STRATEGIC ANALYSIS ═══════════════
        // Target assessment and prioritization
        public const int Strategy_MaxPriorityTargets = 5;     // Maximum targets to track
        public const int Strategy_MaxBorderingTargets = 3;    // Maximum neighboring targets
        public const float Strategy_NeighborDetectionRange = 150f; // Distance for neighbor detection
        public const float Strategy_ConsolidationRange = 150f;     // Distance for consolidation detection

        // Opportunity assessment thresholds
        public const float Strategy_OpportunisticMinRatio = 0.4f;  // Minimum strength ratio for opportunistic attacks
        public const float Strategy_DesperateExpansionThreshold = 2f; // Fief count for desperate expansion
        public const float Strategy_CoalitionJoinThreshold = 1.5f;   // Coalition/target ratio to join wars

        // Strategic value calculation
        public const float Strategy_TownProsperityWeight = 0.01f;   // Value per prosperity point
        public const float Strategy_VillageHearthWeight = 0.005f;  // Value per hearth point
        public const float Strategy_CapitalBonus = 50f;            // Bonus for targeting capitals

        // Decision making parameters
        public const float Strategy_MinInfluencePerFief = 25f;     // Required influence per fief
        public const float Strategy_BaseMinInfluence = 50f;       // Base minimum influence
        public const int Strategy_DecisionCooldownDays = 7;       // Days between strategic decisions

        // Performance optimization
        public const float Strategy_CacheRefreshDays = 1.0f;      // Days between cache refreshes
    }

    #endregion

    #region 📊 DATA STRUCTURES

    /// <summary>War record for tracking conflict history</summary>
    public class WarRecord
    {
        [SaveableField(1)] public Kingdom Attacker;
        [SaveableField(2)] public Kingdom Target;
        [SaveableField(3)] public CampaignTime WarStartTime;

        public WarRecord() { }
        public WarRecord(Kingdom attacker, Kingdom target, CampaignTime startTime)
        {
            Attacker = attacker;
            Target = target;
            WarStartTime = startTime;
        }
    }

    /// <summary>War weariness tracking between kingdom pairs</summary>
    public class WarWearinessRecord
    {
        [SaveableField(1)] public Kingdom KingdomA;
        [SaveableField(2)] public Kingdom KingdomB;
        [SaveableField(3)] public float Weariness;
        [SaveableField(4)] public CampaignTime LastUpdated;

        public WarWearinessRecord() { }
        public WarWearinessRecord(Kingdom a, Kingdom b, float weariness, CampaignTime lastUpdated)
        {
            KingdomA = a;
            KingdomB = b;
            Weariness = weariness;
            LastUpdated = lastUpdated;
        }
    }

    /// <summary>Comprehensive opportunity assessment for a kingdom</summary>
    public class OpportunityAssessment
    {
        public List<Kingdom> OpportunisticTargets { get; set; } = new List<Kingdom>();
        public List<Kingdom> TraditionalTargets { get; set; } = new List<Kingdom>();
        public List<Kingdom> CoalitionTargets { get; set; } = new List<Kingdom>();
        public List<Kingdom> DesperationTargets { get; set; } = new List<Kingdom>();

        public bool HasAnyTargets => OpportunisticTargets.Any() || TraditionalTargets.Any() ||
                                    CoalitionTargets.Any() || DesperationTargets.Any();
    }

    /// <summary>Geographical relationship analysis</summary>
    public class GeographicalAnalysis
    {
        public List<Kingdom> BorderingKingdoms { get; set; } = new List<Kingdom>();
        public List<Kingdom> ConsolidationTargets { get; set; } = new List<Kingdom>();
        public List<Kingdom> IsolatedKingdoms { get; set; } = new List<Kingdom>();
        public float AverageDistanceToEnemies { get; set; }
    }

    /// <summary>Overall strategic position assessment</summary>
    public class StrategicPosition
    {
        public int ActiveWars { get; set; }
        public float StrengthRelativeToStrongest { get; set; }
        public float InfluenceRatio { get; set; }
        public bool IsInSurvivalMode { get; set; }
        public bool HasRunawayThreat { get; set; }
        public List<Kingdom> PotentialAllies { get; set; } = new List<Kingdom>();
    }

    /// <summary>Peace proposal for bilateral negotiation</summary>
    public class PeaceProposal
    {
        public Kingdom Proposer { get; set; }
        public Kingdom Target { get; set; }
        public int TributeAmount { get; set; } // Positive = proposer pays, Negative = proposer receives
        public CampaignTime ProposalTime { get; set; }
        public bool IsPlayerInvolved { get; set; }
    }

    #endregion

    #region ⚔️ WAR SCORING ENGINE

    /// <summary>
    /// Calculates war priority scores for kingdom pairs.
    /// Considers strength, opportunities, geography, and strategic value.
    /// </summary>
    public class WarScorer
    {
        private readonly RunawayFactionAnalyzer _runawayAnalyzer;
        private List<WarRecord> _warRecords = new();
        private Dictionary<(Kingdom, Kingdom), WarWearinessRecord> _warWeariness = new();

        public WarScorer(RunawayFactionAnalyzer runawayAnalyzer) => _runawayAnalyzer = runawayAnalyzer;

        /// <summary>Get war start times for peace commitment calculations</summary>
        public Dictionary<Kingdom, Dictionary<Kingdom, CampaignTime>> GetWarStartTimes()
        {
            var dict = new Dictionary<Kingdom, Dictionary<Kingdom, CampaignTime>>();
            foreach (var r in _warRecords)
            {
                if (!dict.TryGetValue(r.Attacker, out var targets))
                    dict[r.Attacker] = targets = new Dictionary<Kingdom, CampaignTime>();
                targets[r.Target] = r.WarStartTime;
            }
            return dict;
        }

        private (Kingdom, Kingdom) GetPairKey(Kingdom a, Kingdom b) =>
            a.GetHashCode() < b.GetHashCode() ? (a, b) : (b, a);

        /// <summary>Update war weariness between two kingdoms</summary>
        public void UpdateWarWeariness(Kingdom a, Kingdom b, bool atWar)
        {
            var key = GetPairKey(a, b);
            if (!_warWeariness.TryGetValue(key, out var record))
                record = new WarWearinessRecord(a, b, 0f, CampaignTime.Now);

            float daysPassed = (float) (CampaignTime.Now - record.LastUpdated).ToDays;
            if (daysPassed <= 0) daysPassed = 0.01f;

            if (atWar)
            {
                // Progressive weariness increase
                float prevWeariness = record.Weariness;
                float newWeariness = prevWeariness;
                if (prevWeariness < ScoringConfig.Peace_WarWeariness_60d)
                    newWeariness = MathF.Min(prevWeariness + daysPassed * 1.0f, ScoringConfig.Peace_WarWeariness_60d);
                else if (prevWeariness < ScoringConfig.Peace_WarWeariness_90d)
                    newWeariness = MathF.Min(prevWeariness + daysPassed * 2.0f, ScoringConfig.Peace_WarWeariness_90d);
                else if (prevWeariness < ScoringConfig.Peace_WarWeariness_120d)
                    newWeariness = MathF.Min(prevWeariness + daysPassed * 3.0f, ScoringConfig.Peace_WarWeariness_120d);
                else
                    newWeariness = MathF.Min(prevWeariness + daysPassed * 4.0f, ScoringConfig.Peace_WarWeariness_Max);

                record.Weariness = newWeariness;
            }
            else
            {
                // Gradual weariness decay during peace
                float decayPerDay = ScoringConfig.Peace_WarWeariness_Max / ScoringConfig.Peace_WarWeariness_Threshold;
                record.Weariness = MathF.Max(0f, record.Weariness - decayPerDay * daysPassed);
            }
            record.LastUpdated = CampaignTime.Now;
            _warWeariness[key] = record;
        }

        /// <summary>Get current war weariness between two kingdoms</summary>
        public float GetWarWeariness(Kingdom a, Kingdom b)
        {
            var key = GetPairKey(a, b);
            return _warWeariness.TryGetValue(key, out var record) ? record.Weariness : 0f;
        }

        /// <summary>Calculate comprehensive war priority score</summary>
        public float CalculateWarPriority(Kingdom kingdom, Kingdom target, ConquestStrategy strategy)
        {
            if (kingdom == null || target == null || strategy == null) return 0f;

            float priority = _runawayAnalyzer?.CalculateWarPriorityModifier(kingdom, target) ?? 0f;

            // Early exit for runaway threat scenarios
            if (priority > 50f)
            {
                float ratio = kingdom.TotalStrength / MathF.Max(target.TotalStrength, 1f);
                if (ratio > 0.3f)
                    return MathF.Clamp(priority, 50f, 100f);
            }

            float strengthRatio = kingdom.TotalStrength / MathF.Max(target.TotalStrength, 1f);
            if (strengthRatio < ScoringConfig.War_StrengthRatioLow) return 0f;

            // Core scoring components
            priority += CalculateStrengthScore(strengthRatio);
            priority += CalculateOpportunisticScore(kingdom, target);
            priority += CalculateStrategicValueScore(kingdom, target, strategy);
            priority += CalculateGeographicalScore(kingdom, target, strategy);
            priority += CalculateEliminationScore(target);
            priority += CalculateRecentPeacePenalty(kingdom, target);

            // War weariness penalty
            float weariness = GetWarWeariness(kingdom, target);
            priority -= MathF.Min(MathF.Pow(weariness / ScoringConfig.Peace_WarWeariness_Max, 2) * 100f, 80f);

            // Expansionist bonus for long peace periods
            var stance = kingdom.GetStanceWith(target);
            if (stance != null && !stance.IsAtWar && stance.PeaceDeclarationDate.ElapsedDaysUntilNow > 60f && strengthRatio > 1.5f)
                priority += MathF.Min(10f + 5f * (strengthRatio - 1.5f), 20f);

            // Internal stability penalty
            if (kingdom.RulingClan != null && kingdom.RulingClan.Influence < Math.Max(ScoringConfig.Strategy_BaseMinInfluence, kingdom.Fiefs.Count * ScoringConfig.Strategy_MinInfluencePerFief))
                priority -= 15f;

            return MathF.Clamp(priority, 0f, 100f);
        }

        private float CalculateStrengthScore(float ratio)
        {
            if (ratio >= ScoringConfig.War_StrengthRatioHigh) return ScoringConfig.War_StrengthScoreHigh;
            if (ratio >= ScoringConfig.War_StrengthRatioMed) return ScoringConfig.War_StrengthScoreMed;
            return ScoringConfig.War_StrengthScoreLow;
        }

        private float CalculateOpportunisticScore(Kingdom kingdom, Kingdom target)
        {
            int targetWars = FactionManager.GetEnemyKingdoms(target).Count();
            float warBonus = MathF.Min(
                ScoringConfig.War_TargetEngaged_1War * (1f - MathF.Pow(0.5f, MathF.Max(0, targetWars - 1))) * 3f,
                ScoringConfig.War_TargetEngaged_3Wars);

            float score = warBonus;

            if (targetWars >= 2)
            {
                float coalitionRatio = (FactionManager.GetEnemyKingdoms(target).Sum(e => e.TotalStrength) + kingdom.TotalStrength)
                                        / MathF.Max(target.TotalStrength, 1f);

                int ownWars = FactionManager.GetEnemyKingdoms(kingdom).Count();
                float coalitionBonus = coalitionRatio > ScoringConfig.War_CoalitionRatioHigh ? 15f :
                                     coalitionRatio > 1.5f ? 8f : 0f;

                // Reduce bonus if already fighting multiple wars
                if (ownWars >= 2) coalitionBonus *= 0.5f;
                score += coalitionBonus;
            }
            return score;
        }

        private float CalculateStrategicValueScore(Kingdom kingdom, Kingdom target, ConquestStrategy strategy)
        {
            float value = strategy.CalculateStrategicValue(target) * 0.3f;
            if (strategy.WouldCreateStrategicAdvantage(target)) value += 15f;
            if (target.Settlements.Contains(target.FactionMidSettlement)) value += ScoringConfig.War_CapitalBonus;
            return value;
        }

        private float CalculateGeographicalScore(Kingdom kingdom, Kingdom target, ConquestStrategy strategy)
        {
            float score = 0f;
            if (strategy.IsBorderingKingdom(target)) score += ScoringConfig.War_BorderBonus;
            if (WouldConsolidate(kingdom, target)) score += ScoringConfig.War_ConsolidationBonus;
            return score;
        }

        private static bool WouldConsolidate(Kingdom kingdom, Kingdom target) =>
            kingdom.Settlements.Any(our => target.Settlements.Any(their =>
                our.Position2D.Distance(their.Position2D) < ScoringConfig.Strategy_ConsolidationRange));

        private static float CalculateEliminationScore(Kingdom target)
        {
            int fiefs = target.Fiefs.Count;
            return MathF.Max(0f, 50f - (fiefs * 10f));
        }

        private static float CalculateRecentPeacePenalty(Kingdom kingdom, Kingdom target)
        {
            var stance = kingdom.GetStanceWith(target);
            float days = stance?.PeaceDeclarationDate.ElapsedDaysUntilNow ?? 999f;
            return days switch
            {
                < 30f => ScoringConfig.War_PeaceCooldown30,
                < 60f => ScoringConfig.War_PeaceCooldown60,
                < 90f => ScoringConfig.War_PeaceCooldown90,
                _ => 0f
            };
        }

        public void RecordWarStart(Kingdom attacker, Kingdom target)
        {
            _warRecords.RemoveAll(r => r.Attacker == attacker && r.Target == target);
            _warRecords.Add(new WarRecord(attacker, target, CampaignTime.Now));
        }

        public void SyncData(IDataStore store)
        {
            store.SyncData("_warRecords", ref _warRecords);
            store.SyncData("_warWeariness", ref _warWeariness);
        }
    }

    #endregion

    #region 🕊️ PEACE SCORING ENGINE

    /// <summary>
    /// Calculates peace priority scores considering multiple factors:
    /// strength disparity, war weariness, strategic opportunities, etc.
    /// </summary>
    public class PeaceScorer
    {
        private readonly RunawayFactionAnalyzer _runawayAnalyzer;
        private readonly WarScorer _warScorer;

        public PeaceScorer(RunawayFactionAnalyzer runaway, WarScorer warScorer)
        {
            _runawayAnalyzer = runaway;
            _warScorer = warScorer;
        }

        /// <summary>Calculate comprehensive peace priority score</summary>
        public float CalculatePeacePriority(Kingdom kingdom, Kingdom enemy, ConquestStrategy strategy)
        {
            if (kingdom == null || enemy == null || strategy == null || kingdom.IsEliminated || enemy.IsEliminated)
                return 0f;

            float score = 0f;
            score += _runawayAnalyzer?.CalculatePeacePriorityModifier(kingdom, enemy) ?? 0f;
            score += CalculateStrengthDisparityScore(kingdom, enemy);
            score += CalculateWarWearinessScore(kingdom, enemy);
            score += CalculateTerritoryLossScore(kingdom, enemy, strategy);
            score += CalculateMultiWarPressureScore(kingdom);
            score += CalculateStrategicOpportunityScore(kingdom, strategy);
            score += CalculateEliminationResistanceScore(enemy);
            score += CalculateGeographicalPositioningScore(kingdom, enemy, strategy);
            score += CalculateWarCommitmentPenalty(kingdom, enemy);
            score += CalculateTributeScore(kingdom, enemy);

            // Strength advantage penalty
            float strengthRatio = kingdom.TotalStrength / MathF.Max(enemy.TotalStrength, 1f);
            if (strengthRatio > 1.5f && !strategy.IsLosingTerritory(enemy))
                score -= MathF.Min(10f + 10f * (strengthRatio - 1.5f), 30f);

            // Diplomatic isolation bonus
            int enemyWars = FactionManager.GetEnemyKingdoms(kingdom)?.Count() ?? 0;
            int allyCount = kingdom.Clans?.Count(c => c != null && c != kingdom.RulingClan) ?? 0;
            if (enemyWars > allyCount)
                score += MathF.Min(10f + 5f * (enemyWars - allyCount), 30f);

            return MathF.Clamp(score, 0f, 100f);
        }

        private static float CalculateStrengthDisparityScore(Kingdom kingdom, Kingdom enemy)
        {
            float ratio = enemy.TotalStrength / MathF.Max(kingdom.TotalStrength, 1f);
            if (ratio <= 1.1f) return 0f;
            float scaled = MathF.Min((ratio - 1.1f) / 1.9f, 1f);
            return scaled * ScoringConfig.Peace_StrengthDisparity_Max;
        }

        private float CalculateWarWearinessScore(Kingdom kingdom, Kingdom enemy)
        {
            float weariness = _warScorer.GetWarWeariness(kingdom, enemy);
            return MathF.Min(weariness / ScoringConfig.Peace_WarWeariness_Max * 100f, 100f);
        }

        private static float CalculateTerritoryLossScore(Kingdom kingdom, Kingdom enemy, ConquestStrategy strategy) =>
            strategy.IsLosingTerritory(enemy) ? ScoringConfig.Peace_TerritoryLoss : 0f;

        private static float CalculateMultiWarPressureScore(Kingdom kingdom)
        {
            int wars = FactionManager.GetEnemyKingdoms(kingdom)?.Count() ?? 0;
            if (wars <= 1) return 0f;
            float baseScore = (wars - 1) * ScoringConfig.Peace_MultiWar_Base;
            float expMult = MathF.Pow(ScoringConfig.Peace_MultiWar_ExpGrowth, wars - 2);
            return MathF.Min(baseScore * expMult, ScoringConfig.Peace_MultiWar_Cap);
        }

        private float CalculateStrategicOpportunityScore(Kingdom kingdom, ConquestStrategy strategy)
        {
            float score = 0f;
            if (strategy.HasBetterExpansionTargets()) score += ScoringConfig.Peace_StrategicOpportunity_BetterTargets;
            if (_runawayAnalyzer?.GetBiggestThreat(kingdom) != null) score += ScoringConfig.Peace_StrategicOpportunity_RunawayThreat;
            return score;
        }

        private static float CalculateEliminationResistanceScore(Kingdom enemy)
        {
            int fiefs = enemy.Fiefs?.Count ?? 0;
            if (fiefs == 0) return 0f;
            float ratio = MathF.Min(fiefs / 8f, 1f);
            return (1f - ratio) * ScoringConfig.Peace_EliminationPenaltyMax;
        }

        private static float CalculateGeographicalPositioningScore(Kingdom kingdom, Kingdom enemy, ConquestStrategy strategy)
        {
            float score = 0f;
            if (!strategy.IsBorderingKingdom(enemy)) score += ScoringConfig.Peace_NonBorderBonus;
            if (!IsBlockingExpansion(kingdom, enemy, strategy)) score += ScoringConfig.Peace_NotBlockingExpansionBonus;
            return score;
        }

        private float CalculateWarCommitmentPenalty(Kingdom kingdom, Kingdom enemy)
        {
            var times = _warScorer?.GetWarStartTimes();
            if (times == null || !times.TryGetValue(kingdom, out var dict) || !dict.TryGetValue(enemy, out var start))
                return 0f;

            float days = start.ElapsedDaysUntilNow;
            if (days < ScoringConfig.Peace_SoftCommitmentDays)
            {
                float penalty = (ScoringConfig.Peace_SoftCommitmentDays - days) * ScoringConfig.Peace_CommitmentPenaltyPerDay;
                if (IsInEmergency(kingdom)) penalty *= 0.5f;
                return MathF.Max(penalty, ScoringConfig.Peace_CommitmentEarlyPenaltyCap);
            }
            if (days < ScoringConfig.Peace_SoftCommitmentDays + 10)
                return ScoringConfig.Peace_CommitmentHonourBonus;
            return 0f;
        }

        private static float CalculateTributeScore(Kingdom kingdom, Kingdom enemy)
        {
            int tribute = new StrategicDecisionManager(null, null, null, null).CalculatePeaceTribute(kingdom, enemy);
            return -tribute / 100f;
        }

        private static bool IsBlockingExpansion(Kingdom kingdom, Kingdom enemy, ConquestStrategy strategy)
        {
            var targets = strategy.PriorityTargets;
            if (!targets.Any()) return false;
            return targets.Any(t => t.Settlements.Any(ts =>
                enemy.Settlements.Any(es =>
                    kingdom.Settlements.Any(ks => IsBetween(ks, ts, es)))));
        }

        private static bool IsBetween(Settlement from, Settlement to, Settlement mid)
        {
            float distFT = from.Position2D.Distance(to.Position2D);
            float detour = from.Position2D.Distance(mid.Position2D) + mid.Position2D.Distance(to.Position2D);
            return Math.Abs(detour - distFT) < 50f;
        }

        private static bool IsInEmergency(Kingdom kingdom)
        {
            int wars = FactionManager.GetEnemyKingdoms(kingdom)?.Count() ?? 0;
            int fiefs = kingdom.Fiefs?.Count ?? 0;
            return fiefs <= 2 || wars >= 3 || (fiefs <= 4 && wars >= 2);
        }

        public float CalculatePeaceThreshold(Kingdom kingdom)
        {
            float threshold = 60f;
            if (IsInEmergency(kingdom)) threshold = 45f;
            if (_runawayAnalyzer?.IsRunawayThreat(kingdom) == true) threshold = 70f;
            return threshold;
        }
    }

    #endregion

    #region 🎯 STRATEGIC ANALYZER - Central Intelligence Hub

    /// <summary>
    /// Central strategic analysis engine that consolidates all strategic calculations.
    /// Provides caching, opportunity assessment, geographical analysis, and decision support.
    /// </summary>
    public class StrategicAnalyzer
    {
        private readonly WarScorer _warScorer;
        private readonly PeaceScorer _peaceScorer;
        private readonly RunawayFactionAnalyzer _runawayAnalyzer;

        // Performance caching
        private readonly Dictionary<Kingdom, GeographicalAnalysis> _geoAnalysisCache = new();
        private readonly Dictionary<Kingdom, StrategicPosition> _positionCache = new();
        private readonly Dictionary<(Kingdom, Kingdom), bool> _borderCache = new();
        private CampaignTime _lastCacheUpdate = CampaignTime.Never;

        public StrategicAnalyzer(WarScorer warScorer, PeaceScorer peaceScorer, RunawayFactionAnalyzer runawayAnalyzer)
        {
            _warScorer = warScorer;
            _peaceScorer = peaceScorer;
            _runawayAnalyzer = runawayAnalyzer;
        }

        #region High-Level Strategic Assessment

        public bool CanMakeStrategicDecisions(Kingdom kingdom)
        {
            if (kingdom?.RulingClan == null || kingdom.IsEliminated) return false;

            float minimumInfluence = Math.Max(ScoringConfig.Strategy_BaseMinInfluence,
                kingdom.Fiefs.Count * ScoringConfig.Strategy_MinInfluencePerFief);

            if (kingdom.RulingClan.Influence < minimumInfluence) return false;

            var position = GetStrategicPosition(kingdom);
            return position.IsInSurvivalMode || position.HasRunawayThreat || true;
        }

        public bool ShouldConsiderNewDecisions(Kingdom kingdom, CampaignTime lastDecisionTime)
        {
            if (lastDecisionTime.ElapsedDaysUntilNow < ScoringConfig.Strategy_DecisionCooldownDays) return false;

            var position = GetStrategicPosition(kingdom);
            if (position.IsInSurvivalMode && lastDecisionTime.ElapsedDaysUntilNow >= 3f) return true;

            return true;
        }

        public bool ShouldPrioritizeAntiRunaway(Kingdom kingdom) =>
            _runawayAnalyzer?.ShouldPrioritizeAntiRunawayActions(kingdom) == true;

        public bool ShouldConsiderPeace(Kingdom kingdom, ConquestStrategy strategy)
        {
            var enemies = GetActiveEnemies(kingdom);
            if (!enemies.Any()) return false;

            return enemies.Any(enemy =>
                _peaceScorer.CalculatePeacePriority(kingdom, enemy, strategy) >=
                _peaceScorer.CalculatePeaceThreshold(kingdom));
        }

        public bool ShouldConsiderWar(Kingdom kingdom) => HasSuitableWarTargets(kingdom);

        #endregion

        #region Target Assessment

        public List<Kingdom> GetPriorityTargets(Kingdom kingdom)
        {
            if (kingdom?.IsEliminated == true) return new List<Kingdom>();

            var targets = new List<Kingdom>();
            var allKingdoms = GetAllValidKingdoms(kingdom);

            var borderingKingdoms = allKingdoms
                .Where(k => IsBorderingKingdom(kingdom, k))
                .OrderBy(k => k.TotalStrength)
                .Take(ScoringConfig.Strategy_MaxBorderingTargets);

            targets.AddRange(borderingKingdoms);

            var remainingSlots = ScoringConfig.Strategy_MaxPriorityTargets - targets.Count;
            if (remainingSlots > 0)
            {
                var otherTargets = allKingdoms
                    .Except(targets)
                    .OrderBy(k => k.TotalStrength)
                    .Take(remainingSlots);

                targets.AddRange(otherTargets);
            }

            return targets;
        }

        public bool HasSuitableWarTargets(Kingdom kingdom)
        {
            if (kingdom?.IsEliminated == true) return false;
            return AnalyzeOpportunities(kingdom).HasAnyTargets;
        }

        public OpportunityAssessment AnalyzeOpportunities(Kingdom kingdom)
        {
            var assessment = new OpportunityAssessment();
            var potentialTargets = GetAllValidKingdoms(kingdom)
                .Where(target => !kingdom.IsAtWarWith(target))
                .ToList();

            if (!potentialTargets.Any()) return assessment;

            var position = GetStrategicPosition(kingdom);

            assessment.TraditionalTargets = potentialTargets
                .Where(target => kingdom.TotalStrength > target.TotalStrength * ScoringConfig.War_StrengthRatioLow)
                .ToList();

            assessment.OpportunisticTargets = FindOpportunisticTargets(kingdom, potentialTargets);
            assessment.CoalitionTargets = FindCoalitionTargets(kingdom, potentialTargets);

            if (position.IsInSurvivalMode)
                assessment.DesperationTargets = FindDesperationTargets(kingdom, potentialTargets);

            return assessment;
        }

        #endregion

        #region Geographical Analysis

        public bool IsBorderingKingdom(Kingdom kingdom1, Kingdom kingdom2)
        {
            if (kingdom1 == null || kingdom2 == null || kingdom1.IsEliminated || kingdom2.IsEliminated)
                return false;

            var cacheKey = (kingdom1, kingdom2);
            if (_borderCache.TryGetValue(cacheKey, out bool cached)) return cached;

            bool result = CalculateBorderingRelationship(kingdom1, kingdom2);
            _borderCache[cacheKey] = result;
            _borderCache[(kingdom2, kingdom1)] = result;

            return result;
        }

        private bool CalculateBorderingRelationship(Kingdom kingdom1, Kingdom kingdom2)
        {
            var settlements1 = kingdom1.Settlements?.Where(s => s != null) ?? Enumerable.Empty<Settlement>();
            var settlements2 = kingdom2.Settlements?.Where(s => s != null) ?? Enumerable.Empty<Settlement>();

            foreach (var s1 in settlements1)
            {
                var nearestInKingdom2 = settlements2
                    .OrderBy(s2 => s1.Position2D.Distance(s2.Position2D))
                    .FirstOrDefault();

                if (nearestInKingdom2 != null &&
                    s1.Position2D.Distance(nearestInKingdom2.Position2D) < ScoringConfig.Strategy_NeighborDetectionRange)
                {
                    return true;
                }
            }

            return false;
        }

        public bool WouldConsolidate(Kingdom attacker, Kingdom target) =>
            attacker.Settlements?.Any(our =>
                target.Settlements?.Any(their =>
                    our.Position2D.Distance(their.Position2D) < ScoringConfig.Strategy_ConsolidationRange) == true) == true;

        #endregion

        #region Strategic Value Assessment

        public float CalculateStrategicValue(Kingdom target)
        {
            if (target?.IsEliminated == true) return 0f;

            try
            {
                float value = target.Settlements?.Sum(s => CalculateSettlementValue(s)) ?? 0f;

                if (target.FactionMidSettlement != null &&
                    target.Settlements?.Contains(target.FactionMidSettlement) == true)
                {
                    value += ScoringConfig.Strategy_CapitalBonus;
                }

                return value;
            }
            catch (Exception ex)
            {
                LogError($"Error calculating strategic value for {target?.Name}: {ex.Message}");
                return 0f;
            }
        }

        private static float CalculateSettlementValue(Settlement settlement)
        {
            if (settlement == null) return 0f;
            return settlement.IsTown ?
                (settlement.Town?.Prosperity ?? 0f) * ScoringConfig.Strategy_TownProsperityWeight :
                settlement.IsVillage ?
                    (settlement.Village?.Hearth ?? 0f) * ScoringConfig.Strategy_VillageHearthWeight :
                    0f;
        }

        public bool WouldCreateStrategicAdvantage(Kingdom attacker, Kingdom target)
        {
            if (target == null) return false;
            try { return IsBorderingKingdom(attacker, target); }
            catch (Exception ex) { LogError($"Error checking strategic advantage for {target?.Name}: {ex.Message}"); return false; }
        }

        #endregion

        #region Strategic Position Analysis

        public StrategicPosition GetStrategicPosition(Kingdom kingdom)
        {
            RefreshCacheIfNeeded();

            if (_positionCache.TryGetValue(kingdom, out var cached)) return cached;

            var position = CalculateStrategicPosition(kingdom);
            _positionCache[kingdom] = position;
            return position;
        }

        private StrategicPosition CalculateStrategicPosition(Kingdom kingdom)
        {
            var enemies = GetActiveEnemies(kingdom);
            var strongestKingdom = Kingdom.All?.Where(k => k != null && !k.IsEliminated)
                .OrderByDescending(k => k.TotalStrength).FirstOrDefault();

            return new StrategicPosition
            {
                ActiveWars = enemies.Count,
                StrengthRelativeToStrongest = strongestKingdom != null ?
                    kingdom.TotalStrength / MathF.Max(strongestKingdom.TotalStrength, 1f) : 1f,
                InfluenceRatio = kingdom.RulingClan != null ?
                    kingdom.RulingClan.Influence / MathF.Max(kingdom.Fiefs.Count * ScoringConfig.Strategy_MinInfluencePerFief, 1f) : 0f,
                IsInSurvivalMode = kingdom.Fiefs.Count <= ScoringConfig.Strategy_DesperateExpansionThreshold || enemies.Count >= 3,
                HasRunawayThreat = _runawayAnalyzer?.GetBiggestThreat(kingdom) != null,
                PotentialAllies = FindPotentialAllies(kingdom)
            };
        }

        public bool HasBetterExpansionTargets(Kingdom kingdom, Kingdom currentEnemy)
        {
            try
            {
                if (kingdom?.IsEliminated == true) return false;

                var currentEnemies = GetActiveEnemies(kingdom);
                var potentialTargets = GetAllValidKingdoms(kingdom)
                    .Where(k => !currentEnemies.Contains(k))
                    .ToList();

                return potentialTargets.Any(target =>
                    kingdom.TotalStrength > target.TotalStrength * ScoringConfig.War_StrengthRatioHigh);
            }
            catch (Exception ex)
            {
                LogError($"Error in HasBetterExpansionTargets for {kingdom?.Name}: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Private Helper Methods

        private List<Kingdom> FindOpportunisticTargets(Kingdom kingdom, List<Kingdom> potentialTargets) =>
            potentialTargets.Where(target =>
            {
                int targetWars = GetActiveEnemies(target).Count;
                float strengthRatio = kingdom.TotalStrength / MathF.Max(target.TotalStrength, 1f);

                return (targetWars >= 2 && strengthRatio > ScoringConfig.Strategy_OpportunisticMinRatio * 1.4f) ||
                       (targetWars >= 3 && strengthRatio > ScoringConfig.Strategy_OpportunisticMinRatio);
            }).ToList();

        private List<Kingdom> FindCoalitionTargets(Kingdom kingdom, List<Kingdom> potentialTargets) =>
            potentialTargets.Where(target =>
            {
                var targetEnemies = GetActiveEnemies(target);
                if (targetEnemies.Count < 2) return false;

                float combinedStrength = targetEnemies.Sum(e => e.TotalStrength) + kingdom.TotalStrength;
                float coalitionRatio = combinedStrength / MathF.Max(target.TotalStrength, 1f);
                float ourRatio = kingdom.TotalStrength / MathF.Max(target.TotalStrength, 1f);

                return coalitionRatio > ScoringConfig.Strategy_CoalitionJoinThreshold &&
                       ourRatio > ScoringConfig.Strategy_OpportunisticMinRatio;
            }).ToList();

        private List<Kingdom> FindDesperationTargets(Kingdom kingdom, List<Kingdom> potentialTargets) =>
            potentialTargets.Where(target =>
            {
                float strengthRatio = kingdom.TotalStrength / MathF.Max(target.TotalStrength, 1f);
                if (strengthRatio < 0.33f) return false;

                return (target.Fiefs.Count <= 4 && strengthRatio > 0.5f) ||
                       strengthRatio > ScoringConfig.Strategy_OpportunisticMinRatio;
            }).ToList();

        private List<Kingdom> FindPotentialAllies(Kingdom kingdom) =>
            GetAllValidKingdoms(kingdom)
                .Where(k => !kingdom.IsAtWarWith(k) &&
                           GetActiveEnemies(k).Intersect(GetActiveEnemies(kingdom)).Any())
                .ToList();

        private List<Kingdom> GetAllValidKingdoms(Kingdom excludeKingdom) =>
            Kingdom.All?
                .Where(k => k != null && k != excludeKingdom && !k.IsEliminated && k.IsMapFaction)
                .ToList() ?? new List<Kingdom>();

        private List<Kingdom> GetActiveEnemies(Kingdom kingdom) =>
            FactionManager.GetEnemyKingdoms(kingdom)?
                .Where(k => k != null && !k.IsEliminated)
                .ToList() ?? new List<Kingdom>();

        private void RefreshCacheIfNeeded()
        {
            if (_lastCacheUpdate.ElapsedDaysUntilNow >= ScoringConfig.Strategy_CacheRefreshDays)
            {
                _geoAnalysisCache.Clear();
                _positionCache.Clear();
                _borderCache.Clear();
                _lastCacheUpdate = CampaignTime.Now;
            }
        }

        private static void LogError(string message) =>
            InformationManager.DisplayMessage(new InformationMessage($"[Debug] {message}", Colors.Yellow));

        #endregion
    }

    #endregion

    #region 🏰 CONQUEST STRATEGY - State Management

    /// <summary>
    /// Manages strategic state for a kingdom including priority targets and territorial history.
    /// All complex calculations are delegated to StrategicAnalyzer.
    /// </summary>
    public class ConquestStrategy
    {
        [SaveableField(1)] private Kingdom _kingdom;
        [SaveableField(2)] private List<Kingdom> _priorityTargets;
        [SaveableField(3)] private CampaignTime _lastUpdate;
        [SaveableField(4)] private Dictionary<Kingdom, List<int>> _territorialHistory;

        private readonly StrategicAnalyzer _analyzer;

        public Kingdom Kingdom => _kingdom;
        public List<Kingdom> PriorityTargets => _priorityTargets ?? new List<Kingdom>();
        public CampaignTime LastUpdate => _lastUpdate;

        public ConquestStrategy(Kingdom kingdom, StrategicAnalyzer analyzer = null)
        {
            _kingdom = kingdom;
            _priorityTargets = new List<Kingdom>();
            _territorialHistory = new Dictionary<Kingdom, List<int>>();
            _lastUpdate = CampaignTime.Now;
            _analyzer = analyzer;
            UpdateStrategy();
        }

        public void UpdateStrategy()
        {
            if (Kingdom?.IsEliminated == true) return;

            try
            {
                if (_analyzer != null)
                    _priorityTargets = _analyzer.GetPriorityTargets(_kingdom);
                else
                    UpdatePriorityTargetsLegacy();

                UpdateTerritorialHistory();
                _lastUpdate = CampaignTime.Now;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error updating strategy for {Kingdom?.Name}: {ex.Message}", Colors.Yellow));
            }
        }

        public bool IsLosingTerritory(Kingdom enemy)
        {
            if (enemy == null || enemy.IsEliminated || Kingdom == null || Kingdom.IsEliminated) return false;
            if (_territorialHistory == null) { _territorialHistory = new Dictionary<Kingdom, List<int>>(); return false; }
            if (!_territorialHistory.TryGetValue(enemy, out var history) || history == null || history.Count < 2) return false;

            try
            {
                var recentCount = Math.Min(5, history.Count);
                var recent = history.Skip(Math.Max(0, history.Count - recentCount)).ToList();
                return recent.Count >= 2 && recent.Last() < recent.First();
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Territory loss check error for {Kingdom?.Name} vs {enemy?.Name}: {ex.Message}", Colors.Yellow));
                return false;
            }
        }

        // Delegated methods
        public bool HasBetterExpansionTargets() =>
            _analyzer?.HasBetterExpansionTargets(Kingdom, null) ?? HasBetterExpansionTargetsLegacy();

        public bool HasSuitableWarTargets() =>
            _analyzer?.HasSuitableWarTargets(Kingdom) ?? HasSuitableWarTargetsLegacy();

        public float CalculateStrategicValue(Kingdom target) =>
            _analyzer?.CalculateStrategicValue(target) ?? CalculateStrategicValueLegacy(target);

        public bool WouldCreateStrategicAdvantage(Kingdom target) =>
            _analyzer?.WouldCreateStrategicAdvantage(Kingdom, target) ?? WouldCreateStrategicAdvantageLegacy(target);

        public bool IsBorderingKingdom(Kingdom other) =>
            _analyzer?.IsBorderingKingdom(Kingdom, other) ?? IsBorderingKingdomLegacy(other);

        #region Territory History Management

        private void UpdateTerritorialHistory()
        {
            if (Kingdom?.IsEliminated == true) return;

            var currentEnemies = FactionManager.GetEnemyKingdoms(Kingdom)?
                .Where(k => k != null && !k.IsEliminated) ?? Enumerable.Empty<Kingdom>();

            foreach (var enemy in currentEnemies)
            {
                if (!_territorialHistory.ContainsKey(enemy))
                    _territorialHistory[enemy] = new List<int>();

                var history = _territorialHistory[enemy];
                history.Add(enemy.Fiefs.Count);
                if (history.Count > 10) history.RemoveAt(0);
            }

            var historiesToRemove = _territorialHistory.Keys
                .Where(k => k == null || k.IsEliminated || !currentEnemies.Contains(k))
                .ToList();

            foreach (var kingdom in historiesToRemove)
                _territorialHistory.Remove(kingdom);
        }

        #endregion

        #region Legacy Fallback Methods

        private void UpdatePriorityTargetsLegacy()
        {
            PriorityTargets.Clear();
            var allKingdoms = Kingdom.All?
                .Where(k => k != null && k != Kingdom && !k.IsEliminated && k.IsMapFaction)
                .OrderBy(k => k.TotalStrength)
                .ToList() ?? new List<Kingdom>();

            var borderingKingdoms = allKingdoms.Where(IsBorderingKingdomLegacy).ToList();
            PriorityTargets.AddRange(borderingKingdoms.Take(3));

            var otherTargets = allKingdoms.Except(PriorityTargets).Take(2);
            PriorityTargets.AddRange(otherTargets);
        }

        private bool HasBetterExpansionTargetsLegacy()
        {
            try
            {
                if (Kingdom == null || Kingdom.IsEliminated) return false;

                var currentEnemies = FactionManager.GetEnemyKingdoms(Kingdom)?.Where(k => k != null).ToList() ?? new List<Kingdom>();
                var potentialTargets = Kingdom.All?
                    .Where(k => k != null && k != Kingdom && !k.IsEliminated && !currentEnemies.Contains(k) && k.IsMapFaction) ??
                    Enumerable.Empty<Kingdom>();

                return potentialTargets.Any(target => target != null && Kingdom.TotalStrength > target.TotalStrength * 2f);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error in HasBetterExpansionTargets for {Kingdom?.Name}: {ex.Message}", Colors.Yellow));
                return false;
            }
        }

        private bool HasSuitableWarTargetsLegacy()
        {
            try
            {
                if (Kingdom == null || Kingdom.IsEliminated) return false;

                var potentialTargets = Kingdom.All?
                    .Where(k => k != null && k != Kingdom && !k.IsEliminated && !Kingdom.IsAtWarWith(k) && k.IsMapFaction)
                    .ToList() ?? new List<Kingdom>();

                return potentialTargets.Any(target => target != null && Kingdom.TotalStrength > target.TotalStrength * 1.2f);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error in HasSuitableWarTargets for {Kingdom?.Name}: {ex.Message}", Colors.Yellow));
                return false;
            }
        }

        private float CalculateStrategicValueLegacy(Kingdom target)
        {
            if (target == null || target.IsEliminated) return 0f;

            try
            {
                float value = target.Settlements?.Sum(s => s?.IsTown == true ? s.Town?.Prosperity / 100f ?? 0f :
                                                          s?.IsVillage == true ? s.Village?.Hearth / 200f ?? 0f : 0f) ?? 0f;

                if (target.FactionMidSettlement != null && target.Settlements?.Contains(target.FactionMidSettlement) == true)
                    value += 50f;

                return value;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error calculating strategic value for {target?.Name}: {ex.Message}", Colors.Yellow));
                return 0f;
            }
        }

        private bool WouldCreateStrategicAdvantageLegacy(Kingdom target) =>
            target != null && IsBorderingKingdomLegacy(target);

        private bool IsBorderingKingdomLegacy(Kingdom other)
        {
            if (other == null || Kingdom == null || other.IsEliminated || Kingdom.IsEliminated) return false;

            var ourSettlements = Kingdom.Settlements;
            if (!ourSettlements.Any()) return false;

            foreach (var ourSettlement in ourSettlements)
            {
                if (ourSettlement == null) continue;

                var nearestSettlements = Settlement.All?
                    .Where(s => s != null && s != ourSettlement && s.MapFaction != null)
                    .OrderBy(s => ourSettlement.Position2D.Distance(s.Position2D))
                    .Take(5)
                    .ToList() ?? new List<Settlement>();

                if (nearestSettlements.Any(s => s.MapFaction == other)) return true;
            }

            return false;
        }

        #endregion
    }

    #endregion
}