using System;
using System.Linq;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TWMathF = TaleWorlds.Library.MathF;
using Diplomacy.Extensions;
using Diplomacy.DiplomaticAction;
using Diplomacy.DiplomaticAction.WarPeace;

#if DIPOLOMACY_WAR_EXHAUSTION
using Diplomacy.WarExhaustion;
#endif

namespace WarAndAiTweaks.AI
{
#if !DIPOLOMACY_EXTENSIONS
    internal static class DiplomacyFallbackExtensions
    {
        public static bool IsAlliedWithFaction(this Kingdom _, Kingdom __) => false;
        public static bool HasNonAggressionPact(this Kingdom _, Kingdom __) => false;
        public static float GetRelation(this Kingdom _, Kingdom __) => 0f;
        public static float GetCasualties(this Kingdom _) => 0f;
        public static bool IsBorderSettlementWith(this Settlement s, Kingdom other) => false;
    }
#endif

    public sealed class StrategicAI
    {
        private readonly Kingdom _owner;
        private readonly IWarEvaluator _warEvaluator;
        private readonly IPeaceEvaluator _peaceEvaluator;

        private const float WAR_THRESHOLD = 75f;
        private const float PEACE_THRESHOLD = 30f;

        // ramp‐up days before full peace desire
        private const float PeaceRampDays = 30f;

        // tracks how many days each enemy has been at war
        private readonly Dictionary<Kingdom, int> _daysAtWar = new Dictionary<Kingdom, int>();

        // tracks days since last war (for war‐hunger)
        private int _daysSinceLastWar = 0;
        public int DaysSinceLastWar
        {
            get => _daysSinceLastWar;
            set => _daysSinceLastWar = value;
        }

        public int DaysAtWar
        {
            get
            {
                return _daysAtWar.Values.DefaultIfEmpty(0).Max();
            }
            set
            {
                foreach (var enemy in FactionManager.GetEnemyKingdoms(_owner))
                    _daysAtWar[enemy] = value;
            }
        }

        public StrategicAI(Kingdom owner, IWarEvaluator warEval, IPeaceEvaluator peaceEval)
        {
            _owner = owner;
            _warEvaluator = warEval;
            _peaceEvaluator = peaceEval;
        }

        public void TickDaily()
        {
            UpdateWarTimer();
            UpdatePeaceTimer();
            ConsiderBestWarTarget();
            TryMakePeace();
        }

        private void UpdateWarTimer()
        {
            var currentEnemies = FactionManager.GetEnemyKingdoms(_owner).ToList();
            foreach (var enemy in currentEnemies)
            {
                if (_daysAtWar.ContainsKey(enemy))
                    _daysAtWar[enemy] += 1;
                else
                    _daysAtWar[enemy] = 1;
            }
            var toRemove = _daysAtWar.Keys.Except(currentEnemies).ToList();
            foreach (var old in toRemove)
                _daysAtWar.Remove(old);
        }

        private void UpdatePeaceTimer()
        {
            bool atWar = FactionManager.GetEnemyKingdoms(_owner).Any();
            _daysSinceLastWar = atWar ? 0 : _daysSinceLastWar + 1;
        }

        private void ConsiderBestWarTarget()
        {
            float bestScore = float.MinValue;
            Kingdom bestTarget = null;

            var candidates = Kingdom.All
                .Where(k =>
                    k != _owner &&
                    !_owner.IsAtWarWith(k) &&
                    !FactionManager.IsAlliedWithFaction(_owner, k) &&
                    !DiplomaticAgreementManager.HasNonAggressionPact(_owner, k, out _) &&
                    !k.IsMinorFaction &&
                    k.Settlements.Any());

            foreach (var k in candidates)
            {
                ExplainedNumber warScore = _warEvaluator.GetWarScore(_owner, k);
                float baseScore = warScore.ResultNumber;
                float peaceBonus = Math.Min(_daysSinceLastWar * PEACE_THRESHOLD / PeaceRampDays, PEACE_THRESHOLD);
                float totalScore = baseScore + peaceBonus;

                AIComputationLogger.LogWarCandidate(_owner, k, baseScore, peaceBonus, totalScore, warScore);

                if (totalScore > bestScore)
                {
                    bestScore = totalScore;
                    bestTarget = k;
                }
            }

            if (bestTarget == null) return;

            AIComputationLogger.LogWarDecision(_owner, bestTarget, bestScore);

            if (bestScore >= WAR_THRESHOLD)
            {
                DeclareWarAction.ApplyByDefault(_owner, bestTarget);
                _daysSinceLastWar = 0;

                var defWar = _warEvaluator as DefaultWarEvaluator;
                string note = defWar != null
                    ? DiplomacyReasoning.WarNotification(_owner, bestTarget, defWar, _daysSinceLastWar)
                    : new TextObject("{=ai_war_simple}{KINGDOM} declares war on {TARGET}.")
                        .SetTextVariable("KINGDOM", _owner.Name)
                        .SetTextVariable("TARGET", bestTarget.Name)
                        .ToString();

                InformationManager.DisplayMessage(new InformationMessage(note));
            }
        }

        private void TryMakePeace()
        {
            foreach (var enemy in FactionManager.GetEnemyKingdoms(_owner).ToList())
            {
                // --- Calculate peace score for this AI kingdom ("owner") ---
                ExplainedNumber explainedBaseScore = _peaceEvaluator.GetPeaceScore(_owner, enemy);
                float baseScore = explainedBaseScore.ResultNumber;

                int daysWar = _daysAtWar.TryGetValue(_owner, out var d) ? d : 0;
                float ramp = TWMathF.Clamp(daysWar / PeaceRampDays, 0f, 1f);
                float peaceScore = baseScore * ramp;

                AIComputationLogger.LogPeaceCandidate(_owner, enemy, peaceScore, explainedBaseScore, ramp);

                // --- Check if this AI wants peace ---
                if (peaceScore >= PEACE_THRESHOLD)
                {
                    bool enemyIsPlayer = enemy.RulingClan == Hero.MainHero.Clan;
                    bool enemyAIAgrees = false;

                    // If the enemy is another AI, check if they also want peace
                    if (!enemyIsPlayer)
                    {
                        ExplainedNumber enemyExplainedBaseScore = _peaceEvaluator.GetPeaceScore(enemy, _owner);
                        float enemyBaseScore = enemyExplainedBaseScore.ResultNumber;

                        // Note: War duration is the same for both sides
                        float enemyPeaceScore = enemyBaseScore * ramp;

                        if (enemyPeaceScore >= PEACE_THRESHOLD)
                        {
                            enemyAIAgrees = true;
                        }
                    }

                    // --- Apply Peace if conditions are met ---
                    // Peace can happen if:
                    // 1. The enemy is the player (player will get an inquiry)
                    // 2. The enemy is an AI and they also agree to peace
                    if (enemyIsPlayer || enemyAIAgrees)
                    {
                        KingdomPeaceAction.ApplyPeace(_owner, enemy);
                        AIComputationLogger.LogPeaceDecision(_owner, enemy, peaceScore);
                    }
                }
            }
        }

        #region Interfaces
        public interface IWarEvaluator { ExplainedNumber GetWarScore(Kingdom a, Kingdom b); }
        public interface IPeaceEvaluator { ExplainedNumber GetPeaceScore(Kingdom a, Kingdom b); }
        public interface IAllianceEvaluator
        {
            ExplainedNumber GetAllianceScore(Kingdom a, Kingdom b);
            bool ShouldTakeActionBidirectional(Kingdom a, Kingdom b, float threshold = 50f);
        }
        public interface INonAggressionPactEvaluator
        {
            ExplainedNumber GetPactScore(Kingdom a, Kingdom b);
            bool ShouldTakeActionBidirectional(Kingdom a, Kingdom b, float threshold = 50f);
        }
        public interface IAllianceBreakEvaluator
        {
            ExplainedNumber GetBreakAllianceScore(Kingdom breaker, Kingdom ally);
            bool ShouldBreakAlliance(Kingdom breaker, Kingdom ally, float threshold = 60f);
        }
        #endregion

        #region Default evaluators
        public class DefaultWarEvaluator : IWarEvaluator
        {
            private const float DistanceWeight = 30f;
            private const float MaxDistance = 800000f; // Corrected value
            private const float SnowballRatioThreshold = 1.5f;
            private const float SnowballBonus = 20f;
            private const float TerritoryWeight = 25f;
            private const float AllianceTerritoryWeight = 15f;
            private const float EconomyWeight = 20f;
            private const float WarWeaknessWeight = 20f;
            private const float SharedBorderBonus = 30f;
            private const float NoSharedBorderPenalty = -50f;

            public ExplainedNumber GetWarScore(Kingdom a, Kingdom b)
            {
                ExplainedNumber explainedNumber = new ExplainedNumber(0f, true);

                float strengthA = GetStrength(a);
                float strengthB = GetStrength(b);
                float powerRatio = strengthA / (strengthB + 1f);
                float strengthScore = TWMathF.Clamp(powerRatio, 0f, 2f) * 60f;
                explainedNumber.Add(strengthScore, new TextObject("Strength Ratio"));

                // FIX: Added strong penalty for not sharing a border.
                int borders = a.Settlements.Count(s => s.IsBorderSettlementWith(b));
                if (borders > 0)
                {
                    explainedNumber.Add(SharedBorderBonus, new TextObject("Shared Border"));
                }
                else
                {
                    explainedNumber.Add(NoSharedBorderPenalty, new TextObject("No Shared Border"));
                }

                float relation = a.GetRelation(b);
                float rivalry = relation < -20 ? 15f : 0f;
                explainedNumber.Add(rivalry, new TextObject("Rivalry"));

                float distPenalty = ComputeDistancePenalty(a, b);
                explainedNumber.Add(distPenalty, new TextObject("Distance Penalty"));

                int activeWars = FactionManager.GetEnemyKingdoms(a).Count();
                float warPenalty = activeWars * 25f;
                explainedNumber.Add(-warPenalty, new TextObject("Active Wars Penalty"));

                float snowball = strengthB > strengthA * SnowballRatioThreshold ? SnowballBonus : 0f;
                explainedNumber.Add(snowball, new TextObject("Anti-Snowball Bonus"));

                int totalFiefs = Kingdom.All.Sum(k => k.Settlements.Count);
                int bFiefs = b.Settlements.Count;
                float territoryShare = totalFiefs > 0 ? (bFiefs / (float) totalFiefs) * 100f : 0f;
                float territoryScore = territoryShare * (TerritoryWeight / 100f);
                explainedNumber.Add(territoryScore, new TextObject("Territory Score"));

                var bAlliance = Kingdom.All.Where(o => o == b || FactionManager.IsAlliedWithFaction(o, b));
                int allianceFiefs = bAlliance.Sum(o => o.Settlements.Count);
                float allianceShare = totalFiefs > 0 ? (allianceFiefs / (float) totalFiefs) * 100f : 0f;
                float allianceTerritoryScore = allianceShare * (AllianceTerritoryWeight / 100f);
                explainedNumber.Add(allianceTerritoryScore, new TextObject("Alliance Territory Score"));

                float econA = a.Settlements.Where(s => s.IsTown).Sum(s => s.Town.Prosperity);
                float econB = b.Settlements.Where(s => s.IsTown).Sum(s => s.Town.Prosperity);
                float econRatio = econA / (econB + 1f);
                float econScore = TWMathF.Clamp(econRatio, 0f, 2f) * EconomyWeight;
                explainedNumber.Add(econScore, new TextObject("Economic Strength"));

                float casualtiesRatio = b.GetCasualties() / (b.TotalStrength + 1f);
                float warWeaknessScore = casualtiesRatio * WarWeaknessWeight;
                explainedNumber.Add(warWeaknessScore, new TextObject("Target War Weariness"));

                float currentValue = explainedNumber.ResultNumber;
                if (currentValue > 100f)
                {
                    explainedNumber.Add(100f - currentValue, new TextObject("Limit Max"));
                }
                else if (currentValue < 0f)
                {
                    explainedNumber.Add(-currentValue, new TextObject("Limit Min"));
                }

                return explainedNumber;
            }

            private float ComputeDistancePenalty(Kingdom a, Kingdom b)
            {
                var aList = a.Settlements.ToList();
                var bList = b.Settlements.ToList();
                if (!aList.Any() || !bList.Any())
                    return 0f;

                var posA = new Vec2(aList.Average(s => s.Position2D.X), aList.Average(s => s.Position2D.Y));
                var posB = new Vec2(bList.Average(s => s.Position2D.X), bList.Average(s => s.Position2D.Y));
                float dist = posA.Distance(posB);
                return -TWMathF.Clamp(dist / MaxDistance, 0f, 1f) * DistanceWeight;
            }

            private float GetStrength(Kingdom k) =>
                k.TotalStrength
              + Kingdom.All
                    .Where(o => FactionManager.IsAlliedWithFaction(o, k))
                    .Sum(o => o.TotalStrength);
        }

        public class DefaultPeaceEvaluator : IPeaceEvaluator
        {
#if DIPOLOMACY_WAR_EXHAUSTION
            private readonly WarExhaustionManager _wem = WarExhaustionManager.Instance;
#endif
            public ExplainedNumber GetPeaceScore(Kingdom k, Kingdom enemy)
            {
                var explainedNumber = new ExplainedNumber(0f, true);
#if DIPOLOMACY_WAR_EXHAUSTION
                if (_wem != null && Settings.Instance.EnableWarExhaustion && _wem.TryGetWarExhaustion(k, enemy, out var we))
                {
                    explainedNumber.Add(we, new TextObject("War Exhaustion"));
                    return explainedNumber;
                }
#endif
                float casualtiesRatio = k.GetCasualties() / (k.TotalStrength + 1f);
                explainedNumber.Add(casualtiesRatio * 400f, new TextObject("Casualties"));

                bool multiFront = FactionManager.GetEnemyKingdoms(k).Count() > 1;
                if (multiFront)
                {
                    explainedNumber.Add(30f, new TextObject("Multi-Front War"));
                }
                return explainedNumber;
            }
        }
        #endregion
    }
}