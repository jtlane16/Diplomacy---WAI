﻿using Diplomacy.Extensions;
using WarAndAiTweaks.DiplomaticAction;
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
        // --- AI Behavior Constants ---
        public const float WAR_THRESHOLD = 75f;
        public const float WAR_DESIRE_RAMP_DAYS = 30f;
        public const float MAX_WAR_DESIRE = 35f;
        public const float WAR_FATIGUE_RAMP_DAYS = 90f;
        public const float MAX_WAR_FATIGUE_PENALTY = -50f;
        public const float PEACE_RAMP_DAYS = 30f;
        public const float PEACE_SCORE_THRESHOLD = 30f;

        // --- Class Fields ---
        private readonly Kingdom _owner;
        private readonly IWarEvaluator _warEvaluator;
        private readonly IPeaceEvaluator _peaceEvaluator;
        private readonly AIGoal _currentGoal;
        private readonly Dictionary<string, CampaignTime> _lastPeaceTimes;

        private readonly Dictionary<Kingdom, int> _daysAtWar = new Dictionary<Kingdom, int>();

        public int DaysSinceLastWar { get; set; } = 0;
        public int DaysAtWar { get; set; }

        public StrategicAI(Kingdom owner, IWarEvaluator warEval, IPeaceEvaluator peaceEval, AIGoal goal, Dictionary<string, CampaignTime> lastPeaceTimes)
        {
            _owner = owner;
            _warEvaluator = warEval;
            _peaceEvaluator = peaceEval;
            _currentGoal = goal;
            _lastPeaceTimes = lastPeaceTimes;
        }

        public void TickDaily(ref bool warDeclaredThisTick)
        {
            UpdateWarTimer();
            UpdatePeaceTimer();

            switch (_currentGoal.Type)
            {
                case GoalType.Expand:
                    ExecuteExpansion(ref warDeclaredThisTick, _currentGoal as ExpandGoal);
                    break;
                case GoalType.Survive:
                    ExecuteSurvival(_currentGoal as SurviveGoal);
                    break;
                case GoalType.Strengthen:
                    ExecuteStrengthening();
                    break;
            }

            var currentAllies = _owner.GetAlliedKingdoms().ToList();
            if (currentAllies.Count > 1)
            {
                var breakAllianceScoringModel = new WarAndAiTweaks.AI.BreakAllianceScoringModel();
                Kingdom? weakestAlly = null;
                float highestBreakScore = float.MinValue;

                foreach (var ally in currentAllies)
                {
                    var breakScore = breakAllianceScoringModel.GetBreakAllianceScore(_owner, ally).ResultNumber;
                    if (breakScore > highestBreakScore)
                    {
                        highestBreakScore = breakScore;
                        weakestAlly = ally;
                    }
                }

                if (weakestAlly != null)
                {
                    BreakAllianceAction.Apply(_owner, weakestAlly);
                    AIComputationLogger.LogBetrayalDecision(_owner, weakestAlly, highestBreakScore);
                    InformationManager.DisplayMessage(new InformationMessage($"{_owner.Name} has broken their alliance with {weakestAlly.Name}."));
                    return;
                }
            }

            if (_currentGoal.Type == GoalType.Expand && (_currentGoal as ExpandGoal)?.Target == null && !warDeclaredThisTick)
            {
                var betrayalScoringModel = new BreakAllianceScoringModel();
                var allies = Kingdom.All.Where(k => k != _owner && FactionManager.IsAlliedWithFaction(_owner, k)).ToList();

                if (allies.Any())
                {
                    var bestAllyToBetray = allies
                        .OrderByDescending(ally => betrayalScoringModel.GetBreakAllianceScore(_owner, ally).ResultNumber)
                        .FirstOrDefault();

                    if (bestAllyToBetray != null && betrayalScoringModel.ShouldBreakAlliance(_owner, bestAllyToBetray))
                    {
                        BreakAllianceAction.Apply(_owner, bestAllyToBetray);
                        AIComputationLogger.LogBetrayalDecision(_owner, bestAllyToBetray, betrayalScoringModel.GetBreakAllianceScore(_owner, bestAllyToBetray).ResultNumber);
                        InformationManager.DisplayMessage(new InformationMessage($"{_owner.Name} has broken their alliance with {bestAllyToBetray.Name}."));
                    }
                }
            }
        }

        private void ExecuteStrengthening()
        {
            var allianceScoringModel = new WarAndAiTweaks.AI.AllianceScoringModel();
            var napScoringModel = new WarAndAiTweaks.AI.NonAggressionPactScoringModel();

            var bestAllianceCandidate = Kingdom.All
                .Where(k => k != _owner && !_owner.IsAtWarWith(k) && !FactionManager.IsAlliedWithFaction(_owner, k))
                .OrderByDescending(k => allianceScoringModel.GetAllianceScore(_owner, k).ResultNumber)
                .FirstOrDefault();

            if (bestAllianceCandidate != null && allianceScoringModel.ShouldTakeActionBidirectional(_owner, bestAllianceCandidate, 60f))
            {
                if (bestAllianceCandidate.Leader == Hero.MainHero)
                {
                    var inquiryTitle = new TextObject("{=3pbwc8sh}Alliance Proposal");
                    var inquiryText = new TextObject("{=QbOqatd7}{KINGDOM} is proposing an alliance with {PLAYER_KINGDOM}.")
                        .SetTextVariable("KINGDOM", _owner.Name)
                        .SetTextVariable("PLAYER_KINGDOM", bestAllianceCandidate.Name);

                    InformationManager.ShowInquiry(new InquiryData(inquiryTitle.ToString(), inquiryText.ToString(), true, true, new TextObject("{=3fTqLwkC}Accept").ToString(), new TextObject("{=dRoMejb0}Decline").ToString(),
                        () => {
                            DiplomaticAction.DeclareAllianceAction.Apply(_owner, bestAllianceCandidate);
                            AIComputationLogger.LogAllianceDecision(_owner, bestAllianceCandidate, true, allianceScoringModel.GetAllianceScore(_owner, bestAllianceCandidate).ResultNumber);
                        },
                        () => {
                            AIComputationLogger.LogAllianceDecision(_owner, bestAllianceCandidate, false, allianceScoringModel.GetAllianceScore(_owner, bestAllianceCandidate).ResultNumber);
                        }));
                }
                else
                {
                    DiplomaticAction.DeclareAllianceAction.Apply(_owner, bestAllianceCandidate);
                    AIComputationLogger.LogAllianceDecision(_owner, bestAllianceCandidate, true, allianceScoringModel.GetAllianceScore(_owner, bestAllianceCandidate).ResultNumber);
                    InformationManager.DisplayMessage(new InformationMessage($"{_owner.Name} has formed an alliance with {bestAllianceCandidate.Name}!"));
                }
                return;
            }

            var bestNapCandidate = Kingdom.All
                .Where(k => k != _owner && !_owner.IsAtWarWith(k) && !FactionManager.IsAlliedWithFaction(_owner, k) && !DiplomaticAgreementManager.HasNonAggressionPact(_owner, k, out _))
                .OrderByDescending(k => napScoringModel.GetPactScore(_owner, k).ResultNumber)
                .FirstOrDefault();

            if (bestNapCandidate != null && napScoringModel.ShouldTakeActionBidirectional(_owner, bestNapCandidate, 50f))
            {
                if (bestNapCandidate.Leader == Hero.MainHero)
                {
                    var inquiryTitle = new TextObject("{=yj4XFa5T}Non-Aggression Pact Proposal");
                    var inquiryText = new TextObject("{=gyLjlpJB}{KINGDOM} is proposing a non-aggression pact with {PLAYER_KINGDOM}.")
                        .SetTextVariable("KINGDOM", _owner.Name)
                        .SetTextVariable("PLAYER_KINGDOM", bestNapCandidate.Name);

                    InformationManager.ShowInquiry(new InquiryData(inquiryTitle.ToString(), inquiryText.ToString(), true, true, new TextObject("{=3fTqLwkC}Accept").ToString(), new TextObject("{=dRoMejb0}Decline").ToString(),
                        () => {
                            DiplomaticAction.FormNonAggressionPactAction.Apply(_owner, bestNapCandidate);
                            AIComputationLogger.LogPactDecision(_owner, bestNapCandidate, true, napScoringModel.GetPactScore(_owner, bestNapCandidate).ResultNumber);
                        },
                        () => {
                            AIComputationLogger.LogPactDecision(_owner, bestNapCandidate, false, napScoringModel.GetPactScore(_owner, bestNapCandidate).ResultNumber);
                        }));
                }
                else
                {
                    DiplomaticAction.FormNonAggressionPactAction.Apply(_owner, bestNapCandidate);
                    AIComputationLogger.LogPactDecision(_owner, bestNapCandidate, true, napScoringModel.GetPactScore(_owner, bestNapCandidate).ResultNumber);
                    InformationManager.DisplayMessage(new InformationMessage($"The {_owner.Name} has formed a non-aggression pact with the {bestNapCandidate.Name}."));
                }
                return;
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
            if (goal == null || goal.Priority < PEACE_SCORE_THRESHOLD)
            {
                return;
            }

            var enemies = FactionManager.GetEnemyKingdoms(_owner).ToList();

            foreach (var enemy in enemies)
            {
                float peaceScore = _peaceEvaluator.GetPeaceScore(_owner, enemy).ResultNumber;

                if (peaceScore >= PEACE_SCORE_THRESHOLD)
                {
                    bool enemyIsPlayer = enemy.RulingClan == Hero.MainHero.Clan;
                    bool enemyAIAgrees = false;

                    if (!enemyIsPlayer)
                    {
                        var enemySurviveGoal = new SurviveGoal(enemy, _peaceEvaluator);
                        enemySurviveGoal.EvaluatePriority();

                        if (enemySurviveGoal.Priority >= PEACE_SCORE_THRESHOLD)
                        {
                            enemyAIAgrees = true;
                        }
                    }

                    if (enemyIsPlayer || enemyAIAgrees)
                    {
                        MakePeaceAction.Apply(_owner, enemy);
                        AIComputationLogger.LogPeaceDecision(_owner, enemy, peaceScore);
                        break;
                    }
                }
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

        public interface IWarEvaluator { ExplainedNumber GetWarScore(Kingdom a, Kingdom b, int daysSinceLastWar, Dictionary<string, CampaignTime> lastPeaceTimes); }
        public interface IPeaceEvaluator { ExplainedNumber GetPeaceScore(Kingdom a, Kingdom b); }

        public class DefaultWarEvaluator : IWarEvaluator
        {
            private const float DistanceWeight = 30f;
            private const float MaxDistance = 1500f;
            private const float SnowballRatioThreshold = 1.5f;
            private const float SnowballBonus = 30f;
            private const float TerritoryWeight = 25f;
            private const float AllianceTerritoryWeight = 15f;
            private const float EconomyWeight = 20f;
            private const float WarWeaknessWeight = 20f;
            private const float SharedBorderBonus = 30f;
            private const float NoSharedBorderPenalty = -50f;
            private const float RECENT_PEACE_PENALTY_MAX = -200f;

            public ExplainedNumber GetWarScore(Kingdom a, Kingdom b, int daysSinceLastWar, Dictionary<string, CampaignTime> lastPeaceTimes)
            {
                ExplainedNumber explainedNumber = new ExplainedNumber(0f, true);

                var key = (string.Compare(a.StringId, b.StringId) < 0) ? $"{a.StringId}_{b.StringId}" : $"{b.StringId}_{a.StringId}";
                if (lastPeaceTimes.TryGetValue(key, out var peaceTime))
                {
                    float elapsedDaysSincePeace = peaceTime.ElapsedDaysUntilNow;
                    float cooldownDays = 20f;

                    if (elapsedDaysSincePeace < cooldownDays)
                    {
                        float penaltyRatio = (cooldownDays - elapsedDaysSincePeace) / cooldownDays;
                        float penalty = RECENT_PEACE_PENALTY_MAX * penaltyRatio;
                        explainedNumber.Add(penalty, new TextObject("Recent Peace Treaty"));
                    }
                }

                float warDesire = Math.Min(daysSinceLastWar * MAX_WAR_DESIRE / WAR_DESIRE_RAMP_DAYS, MAX_WAR_DESIRE);
                if (warDesire > 0)
                {
                    explainedNumber.Add(warDesire, new TextObject("War Desire (from peace time)"));
                }

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
                float warPenalty = activeWars * 150f;
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

                var daysAtWar = stance.WarStartDate.ElapsedDaysUntilNow;
                float warDurationFactor = Math.Min(daysAtWar / 180f, 1.0f);
                explainedNumber.Add(warDurationFactor * 100f * (WarDurationWeight / 100f), new TextObject("{=XIPMI3gR}War Duration"));
                float casualtiesRatio = k.GetCasualties() / (k.TotalStrength + 1f);
                explainedNumber.Add(casualtiesRatio * 100f * (CasualtiesWeight / 100f), new TextObject("Casualties"));

                int fiefsLost = stance.GetSuccessfulSieges(enemy);
                explainedNumber.Add(fiefsLost * 5f * (FiefLossWeight / 100f), new TextObject("{=DrNBDhx3}Fiefs Lost"));

                return explainedNumber;
            }
        }
    }
}