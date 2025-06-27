using Diplomacy.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

using TodayWeFeast;

using WarAndAiTweaks.AI;
using WarAndAiTweaks.AI.Goals;
using WarAndAiTweaks.DiplomaticAction;

using TWMathF = TaleWorlds.Library.MathF; // This alias resolves the ambiguity

namespace WarAndAiTweaks.AI
{
    public sealed class StrategicAI
    {
        // --- AI Behavior Constants ---
        public const float WAR_THRESHOLD = 75f;
        public const float WAR_DESIRE_RAMP_DAYS = 30f;
        public const float MAX_WAR_DESIRE = 35f;
        public const float WAR_FATIGUE_RAMP_DAYS = 30f;
        public const float MAX_WAR_FATIGUE_PENALTY = -75f;
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

            // NEW: Evaluate alliance value before any actions
            var allianceValue = EvaluateCurrentAllianceValue();

            switch (_currentGoal.Type)
            {
                case GoalType.Expand:
                    // NEW: Only consider betrayal if expansion is truly blocked
                    if (IsExpansionBlocked() && allianceValue.weakestAlly != null)
                    {
                        ExecuteExpansion(ref warDeclaredThisTick, _currentGoal as ExpandGoal);
                    }
                    break;
                case GoalType.Survive:
                    ExecuteSurvival(_currentGoal as SurviveGoal);
                    break;
                case GoalType.Strengthen:
                    ExecuteStrengthening();
                    break;
            }

            // NEW: Smarter alliance breaking logic
            if (ShouldConsiderBetrayalTiming())
            {
                HandleAllianceBetrayalLogic(allianceValue);
            }
        }

        private bool IsExpansionBlocked()
        {
            // Check if all viable targets are allied or protected
            var candidates = Kingdom.All.Where(k =>
                k != _owner &&
                !_owner.IsAtWarWith(k) &&
                !FactionManager.IsAlliedWithFaction(_owner, k) &&
                !DiplomaticAgreementManager.HasNonAggressionPact(_owner, k, out _));

            return !candidates.Any() || candidates.All(k => k.TotalStrength > _owner.TotalStrength * 1.5f);
        }

        private bool ShouldConsiderBetrayalTiming()
        {
            // NEW: Only betray during optimal windows
            var currentSeason = CampaignTime.Now.GetSeasonOfYear;

            // Don't betray during winter (harsh campaign conditions)
            if (currentSeason == CampaignTime.Seasons.Winter) return false;

            // Don't betray if we're economically strained
            if (_owner.RulingClan.Gold < 100000) return false;

            // Don't betray if we have active feasts
            if (FeastBehavior.Instance?.feastIsPresent(_owner) == true) return false;

            return true;
        }

        private void ExecuteStrengthening()
        {
            var allianceScoringModel = new AllianceScoringModel();
            var napScoringModel = new NonAggressionPactScoringModel();

            var bestAllianceCandidate = Kingdom.All
                .Where(k => k != _owner && !_owner.IsAtWarWith(k) && !k.GetAlliedKingdoms().Contains(_owner))
                .OrderByDescending(k => allianceScoringModel.GetAllianceScore(_owner, k).ResultNumber)
                .FirstOrDefault();

            if (bestAllianceCandidate != null)
            {
                var allianceScore = allianceScoringModel.GetAllianceScore(_owner, bestAllianceCandidate);
                if (allianceScoringModel.ShouldTakeActionBidirectional(_owner, bestAllianceCandidate, 60f))
                {
                    string reason = GetPrimaryReason(allianceScore);
                    DeclareAllianceAction.Apply(_owner, bestAllianceCandidate, reason);
                    AIComputationLogger.LogAllianceDecision(_owner, bestAllianceCandidate, true, allianceScore.ResultNumber, reason);
                    return; // Only do one diplomatic action per day.
                }

            }

            var bestNapCandidate = Kingdom.All
                .Where(k => k != _owner && !_owner.IsAtWarWith(k) && !k.GetAlliedKingdoms().Contains(_owner) && !DiplomaticAgreementManager.HasNonAggressionPact(_owner, k, out _))
                .OrderByDescending(k => napScoringModel.GetPactScore(_owner, k).ResultNumber)
                .FirstOrDefault();

            if (bestNapCandidate != null)
            {
                var napScore = napScoringModel.GetPactScore(_owner, bestNapCandidate);
                if (napScoringModel.ShouldTakeActionBidirectional(_owner, bestNapCandidate, 60f))
                {
                    string reason = GetPrimaryReason(napScore);
                    if (bestNapCandidate.Leader == Hero.MainHero)
                    {
                        var inquiryTitle = new TextObject("{=yj4XFa5T}Non-Aggression Pact Proposal");
                        var inquiryText = new TextObject("{=gyLjlpJB}{KINGDOM} is proposing a non-aggression pact with {PLAYER_KINGDOM} because {REASON}.")
                            .SetTextVariable("KINGDOM", _owner.Name)
                            .SetTextVariable("PLAYER_KINGDOM", bestNapCandidate.Name)
                            .SetTextVariable("REASON", reason);


                        InformationManager.ShowInquiry(new InquiryData(inquiryTitle.ToString(), inquiryText.ToString(), true, true, new TextObject("{=3fTqLwkC}Accept").ToString(), new TextObject("{=dRoMejb0}Decline").ToString(),
                            () =>
                            {
                                FormNonAggressionPactAction.Apply(_owner, bestNapCandidate, reason);
                                AIComputationLogger.LogPactDecision(_owner, bestNapCandidate, true, napScore.ResultNumber, reason);
                            },
                            () =>
                            {
                                AIComputationLogger.LogPactDecision(_owner, bestNapCandidate, false, napScore.ResultNumber, reason);
                            }));
                    }
                    else
                    {
                        FormNonAggressionPactAction.Apply(_owner, bestNapCandidate, reason);
                        AIComputationLogger.LogPactDecision(_owner, bestNapCandidate, true, napScore.ResultNumber, reason);
                    }
                    return;
                }
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

            // Enhanced feast-related war reluctance
            if (FeastBehavior.Instance != null)
            {
                score = ApplyFeastWarPenalties(score, bestTarget);
                
                // NEW: Check for diplomatic feast opportunities
                if (HasDiplomaticFeastOpportunity(bestTarget))
                {
                    score -= 100f; // Strong penalty if we could use feasts diplomatically instead
                }
            }

            // NEW: Consider economic timing
            score = ApplyEconomicTimingBonus(score, bestTarget);
            
            // NEW: Apply strategic patience for better opportunities
            score = ApplyStrategicPatienceModifier(score, bestTarget);

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

        private bool HasDiplomaticFeastOpportunity(Kingdom target)
        {
            // Check if we could invite target's lords to our feast instead of warring
            var ourFeast = FeastBehavior.Instance.Feasts.FirstOrDefault(f => f.kingdom == _owner);
            if (ourFeast != null && ourFeast.currentDay <= 3)
            {
                // Early in our feast - diplomatic opportunity exists
                var targetLords = target.Lords.Where(l => l.GetRelation(_owner.Leader) > -10);
                return targetLords.Any(); // We could improve relations instead
            }
            return false;
        }

        private float ApplyEconomicTimingBonus(float score, Kingdom target)
        {
            // NEW: Economic opportunity timing
            if (target.RulingClan.Gold < 50000 && _owner.RulingClan.Gold > target.RulingClan.Gold * 2)
            {
                score += 25f; // Strike when enemy is economically weak
            }
            
            // NEW: Harvest season bonus for capturing settlements
            var currentSeason = CampaignTime.Now.GetSeasonOfYear;
            if (currentSeason == CampaignTime.Seasons.Autumn)
            {
                score += 15f; // Better time to capture and hold territory
            }
            
            return score;
        }

        private float ApplyStrategicPatienceModifier(float score, Kingdom target)
        {
            // NEW: Wait for better opportunities
            var targetEnemies = FactionManager.GetEnemyKingdoms(target).Count();
            if (targetEnemies >= 2)
            {
                score += 35f; // Strike when they're distracted
            }
            
            // NEW: Avoid wars when target has strong allies
            var targetAllies = target.GetAlliedKingdoms().ToList();
            var strongAllies = targetAllies.Count(ally => ally.TotalStrength > _owner.TotalStrength * 0.8f);
            if (strongAllies > 0)
            {
                score -= strongAllies * 40f; // Significant penalty for strong allied opposition
            }
            
            return score;
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
                        var defPeace = _peaceEvaluator as DefaultPeaceEvaluator;
                        string note = defPeace != null
                            ? DiplomacyReasoning.PeaceNotification(_owner, enemy, defPeace)
                            : new TextObject("{=ai_peace_simple}{KINGDOM} has made peace with {TARGET}.")
                                .SetTextVariable("KINGDOM", _owner.Name)
                                .SetTextVariable("TARGET", enemy.Name)
                                .ToString();
                        InformationManager.DisplayMessage(new InformationMessage(note));

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

        private string GetPrimaryReason(ExplainedNumber explainedNumber)
        {
            var lines = explainedNumber.GetLines();
            if (lines == null || !lines.Any())
            {
                return "of unforeseen circumstances";
            }

            var primaryReasonLine = lines
                .OrderByDescending(line => Math.Abs(line.number))
                .FirstOrDefault();

            if (primaryReasonLine == default)
            {
                return "of a complex set of factors";
            }

            return primaryReasonLine.name.ToString();
        }

        private (Kingdom weakestAlly, float value) EvaluateCurrentAllianceValue()
        {
            var allies = _owner.GetAlliedKingdoms().ToList();
            if (!allies.Any())
            {
                return (null, 0f);
            }

            Kingdom weakestAlly = null;
            float lowestValue = float.MaxValue;

            foreach (var ally in allies)
            {
                // Calculate alliance value based on strength, relations, and strategic position
                float value = ally.TotalStrength * 0.5f; // Base strength value

                // Add relation bonus
                var relation = _owner.Leader.GetRelation(ally.Leader);
                value += relation * 2f;

                // Add shared border bonus
                var sharedBorders = _owner.Settlements.Count(s => s.IsBorderSettlementWith(ally));
                value += sharedBorders * 10f;

                // Check if this is the weakest alliance
                if (value < lowestValue)
                {
                    lowestValue = value;
                    weakestAlly = ally;
                }
            }

            return (weakestAlly, lowestValue);
        }

        private void HandleAllianceBetrayalLogic((Kingdom weakestAlly, float value) allianceValue)
        {
            if (allianceValue.weakestAlly == null)
                return;

            // Only consider betrayal if the alliance value is low enough
            if (allianceValue.value > 100f) // Threshold for valuable alliances
                return;

            var betrayalScoringModel = new BreakAllianceScoringModel();
            var betrayalScore = betrayalScoringModel.GetBreakAllianceScore(_owner, allianceValue.weakestAlly);

            if (betrayalScore.ResultNumber > 75f) // Threshold for betrayal
            {
                string reason = GetPrimaryReason(betrayalScore);

                // Break the alliance
                if (FactionManager.IsAlliedWithFaction(_owner, allianceValue.weakestAlly))
                {
                    var allianceToBreak = _owner.GetAlliedKingdoms().FirstOrDefault(a =>
                        a == allianceValue.weakestAlly);

                    if (allianceToBreak != null)
                    {
                        BreakAllianceAction.Apply(_owner, allianceToBreak, reason); // Fix: Added missing arguments
                        AIComputationLogger.LogBetrayalDecision(_owner, allianceValue.weakestAlly, betrayalScore.ResultNumber, reason);

                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{_owner.Name} has broken their alliance with {allianceValue.weakestAlly.Name}!"));
                    }
                }
            }
        }

        private float ApplyFeastWarPenalties(float score, Kingdom target)
        {
            // Apply the same feast penalties as in the war evaluator
            if (FeastBehavior.Instance.feastIsPresent(_owner))
            {
                var ourFeast = FeastBehavior.Instance.Feasts.FirstOrDefault(f => f.kingdom == _owner);
                float basePenalty = -200f;

                if (ourFeast != null)
                {
                    float investmentPenalty = (ourFeast.lordsInFeast?.Count ?? 0) * 8f;
                    float durationPenalty = ourFeast.currentDay * 20f;

                    int generosity = _owner.Leader.GetTraitLevel(DefaultTraits.Generosity);
                    if (generosity > 0)
                    {
                        investmentPenalty += generosity * 25f;
                    }

                    basePenalty -= investmentPenalty + durationPenalty;
                }

                score += basePenalty;
            }

            if (FeastBehavior.Instance.feastIsPresent(target))
            {
                var targetFeast = FeastBehavior.Instance.Feasts.FirstOrDefault(f => f.kingdom == target);
                float basePenalty = -75f;
                float honorPenalty = _owner.Leader.GetTraitLevel(DefaultTraits.Honor) * 35f;

                if (targetFeast != null)
                {
                    float guestPenalty = (targetFeast.lordsInFeast?.Count ?? 0) * 5f;

                    if (targetFeast.currentDay <= 3)
                    {
                        guestPenalty += 25f;
                    }

                    basePenalty -= guestPenalty;
                }

                score += basePenalty - honorPenalty;
            }

            return score;
        }

        // --- Nested Evaluator Classes and Interfaces ---

        public interface IWarEvaluator { ExplainedNumber GetWarScore(Kingdom a, Kingdom b, int daysSinceLastWar, Dictionary<string, CampaignTime> lastPeaceTimes); }
        public interface IPeaceEvaluator { ExplainedNumber GetPeaceScore(Kingdom a, Kingdom b); }

        public class DefaultWarEvaluator : IWarEvaluator
        {
            private const float DistanceWeight = 75f;
            private const float MaxDistance = 1500f;
            private const float SnowballRatioThreshold = 1.5f;
            private const float SnowballBonus = 30f;
            private const float TerritoryWeight = 25f;
            private const float AllianceTerritoryWeight = 15f;
            private const float SharedBorderBonus = 30f;
            private const float NoSharedBorderPenalty = -300f;
            private const float RECENT_PEACE_PENALTY_MAX = -400f;
            private const float INFAMY_PENALTY_MAX = -100f;

            public ExplainedNumber GetWarScore(Kingdom a, Kingdom b, int daysSinceLastWar, Dictionary<string, CampaignTime> lastPeaceTimes)
            {
                ExplainedNumber explainedNumber = new ExplainedNumber(0f, true);

                if (b == null || b.IsEliminated)
                {
                    return explainedNumber;
                }

                if (daysSinceLastWar < WAR_FATIGUE_RAMP_DAYS)
                {
                    float fatiguePenalty = MAX_WAR_FATIGUE_PENALTY * (1 - (daysSinceLastWar / WAR_FATIGUE_RAMP_DAYS));
                    explainedNumber.Add(fatiguePenalty, new TextObject("Recovering from the last war"));
                }

                // War Desire: Builds up quickly over 30 days.
                float warDesire = Math.Min(daysSinceLastWar * MAX_WAR_DESIRE / WAR_DESIRE_RAMP_DAYS, MAX_WAR_DESIRE);
                if (warDesire > 0)
                {
                    explainedNumber.Add(warDesire, new TextObject("Desire for war (from peace time)"));
                }

                // Post-War Political Cooldown: A short, very harsh penalty.
                var mostRecentPeace = lastPeaceTimes.Values.OrderByDescending(t => t.ToDays).FirstOrDefault();
                if (mostRecentPeace != default(CampaignTime))
                {
                    var daysSinceAnyPeace = CampaignTime.Now.ToDays - mostRecentPeace.ToDays;
                    if (daysSinceAnyPeace < 20) // Apply a heavy penalty for 20 days after ANY peace treaty.
                    {
                        var penalty = RECENT_PEACE_PENALTY_MAX * (float) (1 - (daysSinceAnyPeace / 20f));
                        explainedNumber.Add(penalty, new TextObject("Consolidating after a recent war"));
                    }
                }

                // Enhanced feast considerations
                if (FeastBehavior.Instance != null)
                {
                    // Check if target kingdom is hosting a feast
                    if (FeastBehavior.Instance.feastIsPresent(b))
                    {
                        var targetFeast = FeastBehavior.Instance.Feasts.FirstOrDefault(f => f.kingdom == b);
                        float basePenalty = -75f; // Increased from -50f
                        float honorPenalty = a.Leader.GetTraitLevel(DefaultTraits.Honor) * 35f; // Increased from 25f
                        
                        // Additional penalties based on feast characteristics
                        if (targetFeast != null)
                        {
                            // Penalty increases with feast guest count (more dishonor)
                            float guestPenalty = (targetFeast.lordsInFeast?.Count ?? 0) * 5f;
                            
                            // Penalty increases if it's early in the feast (more disruptive)
                            if (targetFeast.currentDay <= 3)
                            {
                                guestPenalty += 25f; // Early feast disruption is worse
                            }
                            
                            basePenalty -= guestPenalty;
                        }
                        
                        explainedNumber.Add(basePenalty - honorPenalty, new TextObject("Attacking while they are feasting would be dishonorable"));
                    }

                    // Check if our kingdom is hosting a feast
                    if (FeastBehavior.Instance.feastIsPresent(a))
                    {
                        var ourFeast = FeastBehavior.Instance.Feasts.FirstOrDefault(f => f.kingdom == a);
                        float basePenalty = -200f; // Increased from -150f
                        
                        if (ourFeast != null)
                        {
                            // Penalty increases with our investment in the feast
                            float investmentPenalty = (ourFeast.lordsInFeast?.Count ?? 0) * 8f;
                            float durationPenalty = ourFeast.currentDay * 20f;
                            
                            // Cultural penalty for breaking hospitality traditions
                            int generosity = a.Leader.GetTraitLevel(DefaultTraits.Generosity);
                            if (generosity > 0)
                            {
                                investmentPenalty += generosity * 25f; // Generous hosts hate abandoning guests
                            }
                            
                            basePenalty -= investmentPenalty + durationPenalty;
                        }
                        
                        explainedNumber.Add(basePenalty, new TextObject("We are hosting a feast and a war would disrupt the festivities"));
                    }

                    // Check if allies are hosting feasts
                    var allies = a.GetAlliedKingdoms().ToList();
                    foreach (var ally in allies)
                    {
                        if (FeastBehavior.Instance.feastIsPresent(ally))
                        {
                            float allyPenalty = -25f;
                            float relationBonus = a.GetRelation(ally) > 20 ? -15f : 0f; // Closer allies matter more
                            explainedNumber.Add(allyPenalty + relationBonus, new TextObject($"Our ally {ally.Name} is hosting a feast"));
                        }
                    }

                    // Seasonal feast considerations
                    var currentSeason = CampaignTime.Now.GetSeasonOfYear;
                    if (currentSeason == CampaignTime.Seasons.Winter || currentSeason == CampaignTime.Seasons.Autumn)
                    {
                        int activeFeastsCount = FeastBehavior.Instance.Feasts.Count;
                        if (activeFeastsCount > 0)
                        {
                            float seasonalPenalty = -activeFeastsCount * 10f;
                            explainedNumber.Add(seasonalPenalty, new TextObject("Traditional feast season discourages warfare"));
                        }
                    }
                }

                var peaceKey = (string.Compare(a.StringId, b.StringId) < 0) ? $"{a.StringId}_{b.StringId}" : $"{b.StringId}_{a.StringId}";
                if (lastPeaceTimes.TryGetValue(peaceKey, out var peaceTime))
                {
                    var daysSincePeace = CampaignTime.Now.ToDays - peaceTime.ToDays;
                    if (daysSincePeace < 100)
                    {
                        var penalty = RECENT_PEACE_PENALTY_MAX * (float) (1 - (daysSincePeace / 100f));
                        explainedNumber.Add(penalty, new TextObject("Recently made peace"));
                    }
                }

                float strengthA = a.TotalStrength + a.GetAlliedKingdoms().Sum(ally => ally.TotalStrength);
                float strengthB = b.TotalStrength + b.GetAlliedKingdoms().Sum(ally => ally.TotalStrength);
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
                    // Give a stronger bonus if there are multiple contested borders.
                    explainedNumber.Add(SharedBorderBonus + (borders * 5f), new TextObject("Shared Border"));
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

                var bAllianceFactions = new List<Kingdom> { b }.Concat(b.GetAlliedKingdoms());
                int allianceFiefs = bAllianceFactions.Sum(o => o.Settlements.Count);
                float allianceShare = totalFiefs > 0 ? (allianceFiefs / (float) totalFiefs) * 100f : 0f;
                float allianceTerritoryScore = allianceShare * (AllianceTerritoryWeight / 100f);
                explainedNumber.Add(allianceTerritoryScore, new TextObject("Alliance Territory Score"));

                float targetWarWeariness = 0;
                var targetEnemies = FactionManager.GetEnemyKingdoms(b).ToList();
                if (targetEnemies.Any())
                {
                    foreach (var enemyOfB in targetEnemies)
                    {
                        var stance = b.GetStanceWith(enemyOfB);
                        if (stance != null && stance.IsAtWar)
                        {
                            targetWarWeariness += stance.GetCasualties(b);
                        }
                    }
                }
                if (targetWarWeariness > 0)
                {
                    explainedNumber.Add(targetWarWeariness / 50f, new TextObject("target's existing war exhaustion"));
                }

                if (WarAndAiTweaks.DiplomaticAction.InfamyManager.Instance != null)
                {
                    float infamy = WarAndAiTweaks.DiplomaticAction.InfamyManager.Instance.GetInfamy(a);
                    if (infamy > 10)
                    {
                        var penalty = Math.Min(infamy, 100) / 100f * INFAMY_PENALTY_MAX;
                        explainedNumber.Add(penalty, new TextObject("High Infamy"));
                    }
                }
                var stanceCheck = a.GetStanceWith(b);
                if (stanceCheck != null)
                {
                    float recentCasualties = stanceCheck.GetCasualties(a);
                    if (recentCasualties > 0)
                        explainedNumber.Add(-recentCasualties / 100, new TextObject("recent casualties"));
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
        }

        public class DefaultPeaceEvaluator : IPeaceEvaluator
        {
            private const float WarDurationWeight = 20f;
            private const float CasualtiesWeight = 30f;
            private const float FiefLossWeight = 30f;
            private const float RaidWeight = 10f;
            private const float WarWearinessFromDistanceWeight = 120f;
            private const float MaxDistance = 1500f;

            public ExplainedNumber GetPeaceScore(Kingdom k, Kingdom enemy)
            {
                var explainedNumber = new ExplainedNumber(0f, true);
                var stance = k.GetStanceWith(enemy);
                if (stance == null || !stance.IsAtWar) return explainedNumber;

                var daysAtWar = stance.WarStartDate.ElapsedDaysUntilNow;

                // --- RE-BALANCED LOGIC ---
                // "Commitment to new war" penalty now decays over 30 days instead of 90.
                if (daysAtWar < 30f)
                {
                    float penalty = -200f * (1 - (daysAtWar / 30f));
                    explainedNumber.Add(penalty, new TextObject("Commitment to the new war"));
                }

                int warCount = FactionManager.GetEnemyKingdoms(k).Count();
                if (warCount > 1)
                {
                    explainedNumber.Add((warCount - 1) * 35f, new TextObject("Pressure from fighting a multi-front war"));
                }

                // "Length of the war" bonus now maxes out at 30 days.
                float warDurationFactor = Math.Min(daysAtWar / 30f, 1.0f);
                explainedNumber.Add(warDurationFactor * 100f * (WarDurationWeight / 100f), new TextObject("the length of the war"));

                // "Casualties suffered" impact now maxes out at 30 days.
                float casualtiesRatio = stance.GetCasualties(k) / (k.TotalStrength + 1f);
                float casualtyImpactFactor = TWMathF.Min(1f, daysAtWar / 30f);
                explainedNumber.Add(casualtiesRatio * 100f * (CasualtiesWeight / 100f) * casualtyImpactFactor, new TextObject("casualties suffered (war duration considered)"));
                // --- END RE-BALANCED LOGIC ---

                int fiefsLost = stance.GetSuccessfulSieges(enemy);
                explainedNumber.Add(fiefsLost * 10f * (FiefLossWeight / 100f), new TextObject("fiefs lost in the war"));

                int successfulRaids = stance.GetSuccessfulRaids(enemy);
                explainedNumber.Add(successfulRaids * 5f * (RaidWeight / 100f), new TextObject("successful raids against them"));

                float distBonus = ComputeDistanceBonus(k, enemy);
                explainedNumber.Add(distBonus, new TextObject("War weariness from distance"));

                return explainedNumber;
            }

            private float ComputeDistanceBonus(Kingdom a, Kingdom b)
            {
                var aList = a.Settlements.ToList();
                var bList = b.Settlements.ToList();
                if (!aList.Any() || !bList.Any())
                    return 0f;

                var posA = new Vec2(aList.Average(s => s.Position2D.X), aList.Average(s => s.Position2D.Y));
                var posB = new Vec2(bList.Average(s => s.Position2D.X), bList.Average(s => s.Position2D.Y));
                float dist = posA.Distance(posB);
                return TWMathF.Clamp(dist / MaxDistance, 0f, 1f) * WarWearinessFromDistanceWeight;
            }
        }
    }
}