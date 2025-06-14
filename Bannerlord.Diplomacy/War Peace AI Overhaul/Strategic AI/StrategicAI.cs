
using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
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
        public static bool IsAlliedWithFaction(this Kingdom _, Kingdom __)               => false;
        public static bool HasNonAggressionPact(this Kingdom _, Kingdom __)             => false;
        public static float GetRelation(this Kingdom _, Kingdom __)                     => 0f;
        public static float GetCasualties(this Kingdom _)                               => 0f;
        public static bool IsBorderSettlementWith(this Settlement s, Kingdom other)     => false;
    }
#endif

    /// <summary>
    /// Strategic AI that decides war and peace, with distance penalty in war scoring.
    /// </summary>
    public sealed class StrategicAI
    {
        private readonly Kingdom _owner;
        private readonly IWarEvaluator _warEvaluator;
        private readonly IPeaceEvaluator _peaceEvaluator;

        private const float WAR_THRESHOLD        = 60f;
        private const float PEACE_THRESHOLD      = 15f;
        private const float PeaceAggroPerDay     = 0.6f;  // bonus per peaceful day
        private const float PeaceAggroCap        = 30f;   // max bonus

        private int _daysSinceLastWar = 0;

        /// <summary>
        /// For persistence across saves.
        /// </summary>
        public int DaysSinceLastWar
        {
            get => _daysSinceLastWar;
            set => _daysSinceLastWar = value;
        }

        public StrategicAI(Kingdom owner, IWarEvaluator warEval, IPeaceEvaluator peaceEval)
        {
            _owner          = owner;
            _warEvaluator   = warEval;
            _peaceEvaluator = peaceEval;
        }

        public void TickDaily()
        {
            UpdatePeaceTimer();
            ConsiderBestWarTarget();
            TryMakePeace();
        }

        private void UpdatePeaceTimer()
        {
            _daysSinceLastWar =
                FactionManager.GetEnemyKingdoms(_owner).Any() ? 0 : _daysSinceLastWar + 1;
        }

        private void ConsiderBestWarTarget()
        {
            var best = Kingdom.All
                .Where(k => k != _owner
                            && !_owner.IsAtWarWith(k)
                            && !FactionManager.IsAlliedWithFaction(_owner, k))
                .Select(k =>
                {
                    float baseScore  = _warEvaluator.GetWarScore(_owner, k);
                    float peaceBonus = Math.Min(_daysSinceLastWar * PeaceAggroPerDay, PeaceAggroCap);
                    return (kingdom: k, score: baseScore + peaceBonus);
                })
                .OrderByDescending(t => t.score)
                .FirstOrDefault();

            if (best.kingdom != null && best.score >= WAR_THRESHOLD)
            {
                DeclareWarAction.ApplyByDefault(_owner, best.kingdom);
                _daysSinceLastWar = 0;

                var defWar = _warEvaluator as DefaultWarEvaluator;
                string note = defWar != null
                    ? DiplomacyReasoning.WarNotification(_owner, best.kingdom, defWar, _daysSinceLastWar)
                    : new TextObject("{=ai_war_simple}{KINGDOM} declares war on {TARGET}.")
                        .SetTextVariable("KINGDOM", _owner.Name)
                        .SetTextVariable("TARGET", best.kingdom.Name)
                        .ToString();

                InformationManager.DisplayMessage(new InformationMessage(note));
            }
        }

        private void TryMakePeace()
        {
            foreach (var enemy in FactionManager.GetEnemyKingdoms(_owner))
            {
                if (_peaceEvaluator.GetPeaceScore(_owner, enemy) < PEACE_THRESHOLD) continue;

                MakePeaceAction.Apply(_owner, enemy, 0);

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

        #region Interfaces
        public interface IWarEvaluator               { float GetWarScore(Kingdom a, Kingdom b); }
        public interface IPeaceEvaluator             { float GetPeaceScore(Kingdom a, Kingdom b); }
        public interface IAllianceEvaluator          { ExplainedNumber GetAllianceScore(Kingdom a, Kingdom b); bool ShouldTakeActionBidirectional(Kingdom a, Kingdom b, float threshold = 50f); }
        public interface INonAggressionPactEvaluator { ExplainedNumber GetPactScore(Kingdom a, Kingdom b); bool ShouldTakeActionBidirectional(Kingdom a, Kingdom b, float threshold = 50f); }
        public interface IAllianceBreakEvaluator     { ExplainedNumber GetBreakAllianceScore(Kingdom breaker, Kingdom ally); bool ShouldBreakAlliance(Kingdom breaker, Kingdom ally, float threshold = 60f); }
        #endregion

        #region Default evaluators
        public class DefaultWarEvaluator : IWarEvaluator
        {
            private const float DistanceWeight = 30f;    // weight for distance penalty
            private const float MaxDistance    = 50000f; // cells, cap for normalization

            public float GetWarScore(Kingdom a, Kingdom b)
            {
                float powerRatio = GetStrength(a) / (GetStrength(b) + 1f);
                float strength   = TWMathF.Clamp(powerRatio, 0f, 2f) * 60f;

                int borders      = a.Settlements.Count(s => s.IsBorderSettlementWith(b));
                float tension    = borders * 10f * 0.25f; // 2.5 pts per border

                float relation   = a.GetRelation(b);
                float rivalry    = relation < -20 ? 15f : 0f;

                // Distance penalty: 0 at 0 cells, -DistanceWeight at MaxDistance or beyond
                float avgAX = a.Settlements.Average(s => s.Position2D.X);
                float avgAY = a.Settlements.Average(s => s.Position2D.Y);
                Vec2 posA = new Vec2(avgAX, avgAY);
                float avgBX = b.Settlements.Average(s => s.Position2D.X);
                float avgBY = b.Settlements.Average(s => s.Position2D.Y);
                Vec2 posB = new Vec2(avgBX, avgBY);
                float dist = posA.Distance(posB);
                float distPenalty = -TWMathF.Clamp(dist / MaxDistance, 0f, 1f) * DistanceWeight;

                float baseScore  = strength + tension + rivalry + distPenalty;

                int activeWars   = FactionManager.GetEnemyKingdoms(a).Count();
                float warPenalty = activeWars * 25f;

                return TWMathF.Clamp(baseScore - warPenalty, 0f, 100f);
            }

            private float GetStrength(Kingdom k) =>
                k.TotalStrength + Kingdom.All.Where(o => FactionManager.IsAlliedWithFaction(o, k)).Sum(o => o.TotalStrength);
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
                bool multiFront       = FactionManager.GetEnemyKingdoms(k).Count() > 1;
                return casualtiesRatio * 400f + (multiFront ? 30f : 0f);
            }
        }
        #endregion
    }
}
