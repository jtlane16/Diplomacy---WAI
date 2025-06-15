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

        /// <summary>
        /// Gets or seeds the number of consecutive days this kingdom has been at war.
        /// </summary>
        public int DaysAtWar
        {
            get
            {
                // if we’re at war with anyone, return the *longest* streak
                return _daysAtWar.Values.DefaultIfEmpty(0).Max();
            }
            set
            {
                // initialize the counter for each current enemy to this value
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

        /// <summary>
        /// Increment per‐enemy day‐at‐war counters.
        /// </summary>
        private void UpdateWarTimer()
        {
            var currentEnemies = FactionManager.GetEnemyKingdoms(_owner).ToList();
            // increment existing or add new
            foreach (var enemy in currentEnemies)
            {
                if (_daysAtWar.ContainsKey(enemy))
                    _daysAtWar[enemy] += 1;
                else
                    _daysAtWar[enemy] = 1;
            }
            // remove those no longer at war
            var toRemove = _daysAtWar.Keys.Except(currentEnemies).ToList();
            foreach (var old in toRemove)
                _daysAtWar.Remove(old);
        }

        /// <summary>
        /// Reset our peace‐hunger counter if any war is active, else count another day of peace.
        /// </summary>
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
                    !k.IsMinorFaction &&
                    k.Settlements.Any());

            foreach (var k in candidates)
            {
                float baseScore = _warEvaluator.GetWarScore(_owner, k);
                float peaceBonus = Math.Min(_daysSinceLastWar * PEACE_THRESHOLD / PeaceRampDays, PEACE_THRESHOLD);
                float totalScore = baseScore + peaceBonus;

                // guard in case our logger isn't set up
                AIComputationLogger.LogWarCandidate(_owner, k, baseScore, peaceBonus, totalScore);

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

                // show a notification
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
            foreach (var enemy in FactionManager.GetEnemyKingdoms(_owner))
            {
                // base peace score
                float baseScore = _peaceEvaluator.GetPeaceScore(_owner, enemy);

                // how many days we've been at war
                int daysWar = _daysAtWar.TryGetValue(enemy, out var d) ? d : 0;

                // ramp factor: 0 at day0, 1 at PeaceRampDays
                float ramp = TWMathF.Clamp(daysWar / PeaceRampDays, 0f, 1f);

                // final adjusted peace score
                float peaceScore = baseScore * ramp;

                AIComputationLogger.LogPeaceCandidate(_owner, enemy, peaceScore);

                if (peaceScore >= PEACE_THRESHOLD)
                {
                    MakePeaceAction.Apply(_owner, enemy, 0);
                    AIComputationLogger.LogPeaceDecision(_owner, enemy, peaceScore);

                    var defPeace = _peaceEvaluator as DefaultPeaceEvaluator;
                    string note = defPeace != null
                        ? DiplomacyReasoning.PeaceNotification(_owner, enemy, defPeace)
                        : new TextObject("{=ai_peace_simple}{KINGDOM} makes peace with {TARGET}.")
                            .SetTextVariable("KINGDOM", _owner.Name)
                            .SetTextVariable("TARGET", enemy.Name)
                            .ToString();

                    InformationManager.DisplayMessage(new InformationMessage(note));
                }
            }
        }

        #region Interfaces
        public interface IWarEvaluator { float GetWarScore(Kingdom a, Kingdom b); }
        public interface IPeaceEvaluator { float GetPeaceScore(Kingdom a, Kingdom b); }
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
            // weights & thresholds
            private const float DistanceWeight = 30f;
            private const float MaxDistance = 50000f;
            private const float SnowballRatioThreshold = 1.5f;
            private const float SnowballBonus = 20f;
            private const float TerritoryWeight = 25f;
            private const float AllianceTerritoryWeight = 15f;
            // **New factors**
            private const float EconomyWeight = 20f;         // #2 economic factor
            private const float WarWeaknessWeight = 20f;     // #5 war-weariness factor

            public float GetWarScore(Kingdom a, Kingdom b)
            {
                // 1) Strength comparison (including allies)
                float strengthA = GetStrength(a);
                float strengthB = GetStrength(b);
                float powerRatio = strengthA / (strengthB + 1f);
                float strengthScore = TWMathF.Clamp(powerRatio, 0f, 2f) * 60f;

                // 2) Border tension
                int borders = a.Settlements.Count(s => s.IsBorderSettlementWith(b));
                float tension = borders * 2.5f;

                // 3) Rivalry penalty
                float relation = a.GetRelation(b);
                float rivalry = relation < -20 ? 15f : 0f;

                // 4) Distance penalty
                float distPenalty = ComputeDistancePenalty(a, b);

                // 5) Active wars penalty
                int activeWars = FactionManager.GetEnemyKingdoms(a).Count();
                float warPenalty = activeWars * 25f;

                // 6) Snowball strength bonus
                float snowball = strengthB > strengthA * SnowballRatioThreshold ? SnowballBonus : 0f;

                // 7) Territory share
                int totalFiefs = Kingdom.All.Sum(k => k.Settlements.Count);
                int bFiefs = b.Settlements.Count;
                float territoryShare = totalFiefs > 0 ? (bFiefs / (float) totalFiefs) * 100f : 0f;
                float territoryScore = territoryShare * (TerritoryWeight / 100f);

                // 8) Alliance territory share
                var bAlliance = Kingdom.All.Where(o => o == b || FactionManager.IsAlliedWithFaction(o, b));
                int allianceFiefs = bAlliance.Sum(o => o.Settlements.Count);
                float allianceShare = totalFiefs > 0 ? (allianceFiefs / (float) totalFiefs) * 100f : 0f;
                float allianceTerritoryScore = allianceShare * (AllianceTerritoryWeight / 100f);

                // **9) Economic strength comparison**
                float econA = a.Settlements.Where(s => s.IsTown).Sum(s => s.Town.Prosperity);
                float econB = b.Settlements.Where(s => s.IsTown).Sum(s => s.Town.Prosperity);
                float econRatio = econA / (econB + 1f);
                float econScore = TWMathF.Clamp(econRatio, 0f, 2f) * EconomyWeight;

                // **10) War-weariness bonus** (higher if the target has bled heavily)
                float casualtiesRatio = b.GetCasualties() / (b.TotalStrength + 1f);
                float warWeaknessScore = casualtiesRatio * WarWeaknessWeight;

                // final raw score
                float raw = strengthScore
                          + tension
                          + rivalry
                          + distPenalty
                          - warPenalty
                          + snowball
                          + territoryScore
                          + allianceTerritoryScore
                          + econScore
                          + warWeaknessScore;

                return TWMathF.Clamp(raw, 0f, 100f);
            }

            private float ComputeDistancePenalty(Kingdom a, Kingdom b)
            {
                var aList = a.Settlements.ToList();
                var bList = b.Settlements.ToList();
                if (!aList.Any() || !bList.Any())
                    return 0f;

                var posA = new Vec2(
                    aList.Average(s => s.Position2D.X),
                    aList.Average(s => s.Position2D.Y)
                );
                var posB = new Vec2(
                    bList.Average(s => s.Position2D.X),
                    bList.Average(s => s.Position2D.Y)
                );
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
            public float GetPeaceScore(Kingdom k, Kingdom enemy)
            {
#if DIPOLOMACY_WAR_EXHAUSTION
                if (_wem != null && _wem.IsEnabled &&
                    _wem.TryGetWarExhaustion(k, enemy, out var we))
                    return we;
#endif
                float casualtiesRatio = k.GetCasualties() / (k.TotalStrength + 1f);
                bool multiFront = FactionManager.GetEnemyKingdoms(k).Count() > 1;
                return casualtiesRatio * 400f + (multiFront ? 30f : 0f);
            }
        }
        #endregion
    }
}
