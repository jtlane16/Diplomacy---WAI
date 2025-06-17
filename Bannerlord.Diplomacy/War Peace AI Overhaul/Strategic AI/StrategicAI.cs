using Diplomacy;
using Diplomacy.DiplomaticAction;
using Diplomacy.DiplomaticAction.WarPeace;
using Diplomacy.Extensions;
using Diplomacy.WarExhaustion;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

using WarAndAiTweaks.AI.Goals;

using TWMathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.AI
{
    public sealed class StrategicAI
    {
        // --- AI Behavior Constants (now public) ---
        public const float WAR_THRESHOLD = 75f;
        public const float WAR_DESIRE_RAMP_DAYS = 45f; // From 30
        public const float MAX_WAR_DESIRE = 20f;       // From 30
        public const float WAR_FATIGUE_RAMP_DAYS = 90f; // From 120
        public const float MAX_WAR_FATIGUE_PENALTY = -50f; // From -40
        public const float PEACE_RAMP_DAYS = 30f;
        public const float PEACE_SCORE_THRESHOLD = 30f;

        // --- Class Fields ---
        private readonly Kingdom _owner;
        private readonly IWarEvaluator _warEvaluator;
        private readonly IPeaceEvaluator _peaceEvaluator;
        private readonly AIGoal _currentGoal;

        private readonly Dictionary<Kingdom, int> _daysAtWar = new Dictionary<Kingdom, int>();

        public int DaysSinceLastWar { get; set; } = 0;
        public int DaysAtWar { get; set; }

        public StrategicAI(Kingdom owner, IWarEvaluator warEval, IPeaceEvaluator peaceEval, AIGoal goal)
        {
            _owner = owner;
            _warEvaluator = warEval;
            _peaceEvaluator = peaceEval;
            _currentGoal = goal;
        }

        // Inside the TickDaily method of StrategicAI.cs
        public void TickDaily(ref bool warDeclaredThisTick)
        {
            UpdateWarTimer();
            UpdatePeaceTimer();

            // Execute logic based on the current goal
            switch (_currentGoal.Type)
            {
                case GoalType.Expand:
                    ExecuteExpansion(ref warDeclaredThisTick, _currentGoal as ExpandGoal);
                    break;
                case GoalType.Survive:
                    ExecuteSurvival(_currentGoal as SurviveGoal);
                    break;
                case GoalType.Strengthen:
                    // Does nothing, avoids war.
                    break;
                case GoalType.FormAlliance:
                    var formAllianceGoal = _currentGoal as FormAllianceGoal;
                    if (formAllianceGoal != null && formAllianceGoal.Priority > 60)
                    {
                        // NEW: Check if the other kingdom also wants an alliance
                        var otherKingdomsGoal = GoalEvaluator.GetHighestPriorityGoal(formAllianceGoal.OtherKingdom, 0, 0);
                        if (otherKingdomsGoal is FormAllianceGoal otherAllianceGoal && otherAllianceGoal.OtherKingdom == this._owner && otherAllianceGoal.Priority > 60)
                        {
                            Diplomacy.DiplomaticAction.Alliance.DeclareAllianceAction.Apply(this._owner, formAllianceGoal.OtherKingdom);
                        }
                    }
                    break;
                case GoalType.FormNonAggressionPact:
                    var formNapGoal = _currentGoal as FormNapGoal;
                    if (formNapGoal != null && formNapGoal.Priority > 50)
                    {
                        // NEW: Check if the other kingdom also wants a pact
                        var otherKingdomsGoal = GoalEvaluator.GetHighestPriorityGoal(formNapGoal.OtherKingdom, 0, 0);
                        if (otherKingdomsGoal is FormNapGoal otherNapGoal && otherNapGoal.OtherKingdom == this._owner && otherNapGoal.Priority > 50)
                        {
                            Diplomacy.DiplomaticAction.NonAggressionPact.FormNonAggressionPactAction.Apply(this._owner, formNapGoal.OtherKingdom);
                        }
                    }
                    break;
            }
        }

        private void ExecuteExpansion(ref bool warDeclaredThisTick, ExpandGoal? goal)
        {
            if (goal?.Target == null || warDeclaredThisTick)
            {
                return;
            }

            var bestTarget = goal.Target;
            var score = goal.Priority;

            AIComputationLogger.LogWarDecision(_owner, bestTarget, score);

            bool declared = score >= WAR_THRESHOLD;
            //AIComputationLogger.LogWarThresholdCheck(_owner, bestTarget, score, WAR_THRESHOLD, declared);

            if (declared)
            {
                DeclareWarAction.ApplyByDefault(_owner, bestTarget);
                warDeclaredThisTick = true;
                DaysSinceLastWar = 0;

                var defWar = _warEvaluator as DefaultWarEvaluator;
                string note = defWar != null
                    ? DiplomacyReasoning.WarNotification(_owner, bestTarget, defWar, DaysSinceLastWar)
                    : new TextObject("{=ai_war_simple}{KINGDOM} declares war on {TARGET}.")
                        .SetTextVariable("KINGDOM", _owner.Name)
                        .SetTextVariable("TARGET", bestTarget.Name)
                        .ToString();

                InformationManager.DisplayMessage(new InformationMessage(note));
            }
        }

        private void ExecuteSurvival(SurviveGoal? goal)
        {
            if (goal?.PeaceCandidate == null)
            {
                return;
            }

            var enemy = goal.PeaceCandidate;
            float peaceScore = goal.Priority; // The goal's priority IS the peace score.

            if (peaceScore < PEACE_SCORE_THRESHOLD) return;

            bool enemyIsPlayer = enemy.RulingClan == Hero.MainHero.Clan;
            bool enemyAIAgrees = false;

            if (!enemyIsPlayer)
            {
                // Check if the enemy is also receptive to peace.
                var enemySurviveGoal = new SurviveGoal(enemy, _peaceEvaluator);
                enemySurviveGoal.EvaluatePriority();

                if (enemySurviveGoal.Priority >= PEACE_SCORE_THRESHOLD)
                {
                    enemyAIAgrees = true;
                }
            }

            if (enemyIsPlayer || enemyAIAgrees)
            {
                KingdomPeaceAction.ApplyPeace(_owner, enemy);
                AIComputationLogger.LogPeaceDecision(_owner, enemy, peaceScore);
            }
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
            DaysSinceLastWar = atWar ? 0 : DaysSinceLastWar + 1;
        }

        // --- Nested Evaluator Classes and Interfaces ---

        public interface IWarEvaluator { ExplainedNumber GetWarScore(Kingdom a, Kingdom b); }
        public interface IPeaceEvaluator { ExplainedNumber GetPeaceScore(Kingdom a, Kingdom b); }

        public class DefaultWarEvaluator : IWarEvaluator
        {
            private const float DistanceWeight = 30f;
            private const float MaxDistance = 800000f;
            private const float SnowballRatioThreshold = 1.5f;
            private const float SnowballBonus = 20f;
            private const float TerritoryWeight = 25f;
            private const float AllianceTerritoryWeight = 15f;
            private const float EconomyWeight = 20f;
            private const float WarWeaknessWeight = 20f;
            private const float SharedBorderBonus = 30f;
            private const float NoSharedBorderPenalty = -200;

            public ExplainedNumber GetWarScore(Kingdom a, Kingdom b)
            {
                ExplainedNumber explainedNumber = new ExplainedNumber(0f, true);

                float strengthA = a.TotalStrength;
                float strengthB = b.TotalStrength + Kingdom.All.Where(o => FactionManager.IsAlliedWithFaction(o, b)).Sum(o => o.TotalStrength);
                float powerRatio = strengthA / (strengthB + 1f);

                if (powerRatio < 0.8f)
                {
                    float powerPenalty = (1.0f - powerRatio) * -100f;
                    explainedNumber.Add(powerPenalty, new TextObject("Military Disadvantage"));
                }

                float strengthScore = TWMathF.Clamp(powerRatio, 0f, 2f) * 60f;
                explainedNumber.Add(strengthScore, new TextObject("Strength Ratio"));

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
                // Increased penalty for active wars
                float warPenalty = activeWars * 150f; // Increased from 50f
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
        }

        public class DefaultPeaceEvaluator : IPeaceEvaluator
        {
            private const float CasualtiesWeight = 30f;
            private const float WarExhaustionWeight = 40f;
            private const float WarDurationWeight = 20f;
            private const float FiefLossWeight = 10f;

            public ExplainedNumber GetPeaceScore(Kingdom k, Kingdom enemy)
            {
                var explainedNumber = new ExplainedNumber(0f, true);
                var stance = k.GetStanceWith(enemy);
                if (stance == null || !stance.IsAtWar) return explainedNumber;

                // War Duration
                var daysAtWar = stance.WarStartDate.ElapsedDaysUntilNow;
                float warDurationFactor = Math.Min(daysAtWar / 180f, 1.0f); // scales up to ~6 months
                explainedNumber.Add(warDurationFactor * 100f * (WarDurationWeight / 100f), new TextObject("{=XIPMI3gR}War Duration"));

                // War Exhaustion (if enabled)
                if (Settings.Instance!.EnableWarExhaustion && WarExhaustionManager.Instance is { } wem)
                {
                    float exhaustion = wem.GetWarExhaustion(k, enemy);
                    explainedNumber.Add(exhaustion * (WarExhaustionWeight / 100f), new TextObject("{=V542tneW}War Exhaustion"));
                }
                else // Fallback to casualties if exhaustion is disabled
                {
                    float casualtiesRatio = k.GetCasualties() / (k.TotalStrength + 1f);
                    explainedNumber.Add(casualtiesRatio * 100f * (CasualtiesWeight / 100f), new TextObject("Casualties"));
                }

                // Fiefs Lost
                int fiefsLost = stance.GetSuccessfulSieges(enemy); // Fiefs `k` lost to `enemy`
                explainedNumber.Add(fiefsLost * 5f * (FiefLossWeight / 100f), new TextObject("{=DrNBDhx3}Fiefs Lost"));

                return explainedNumber;
            }
        }
    }
}