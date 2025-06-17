using System;
using System.Linq;

using TaleWorlds.CampaignSystem;

using WarAndAiTweaks.AI; // Add this using statement

using static WarAndAiTweaks.AI.StrategicAI;

namespace WarAndAiTweaks.AI.Goals
{
    public class ExpandGoal : AIGoal
    {
        public Kingdom? Target { get; private set; }
        private readonly IWarEvaluator _warEvaluator;
        private readonly int _daysSinceLastWar;
        private readonly int _daysAtWar;

        public ExpandGoal(Kingdom kingdom, IWarEvaluator warEvaluator, int daysSinceLastWar, int daysAtWar) : base(kingdom, GoalType.Expand)
        {
            _warEvaluator = warEvaluator;
            _daysSinceLastWar = daysSinceLastWar;
            _daysAtWar = daysAtWar;
        }

        public override void EvaluatePriority()
        {
            float bestScore = float.MinValue;
            Kingdom? bestTarget = null;

            float warDesire = Math.Min(_daysSinceLastWar * MAX_WAR_DESIRE / WAR_DESIRE_RAMP_DAYS, MAX_WAR_DESIRE);
            float peaceDesire = Math.Max(_daysAtWar * MAX_WAR_FATIGUE_PENALTY / WAR_FATIGUE_RAMP_DAYS, MAX_WAR_FATIGUE_PENALTY);

            var candidates = Kingdom.All.Where(k =>
                k != Kingdom &&
                !Kingdom.IsAtWarWith(k) &&
                !FactionManager.IsAlliedWithFaction(Kingdom, k) &&
                !Diplomacy.DiplomaticAction.DiplomaticAgreementManager.HasNonAggressionPact(Kingdom, k, out _));

            foreach (var k in candidates)
            {
                var warScore = _warEvaluator.GetWarScore(Kingdom, k);
                float finalScore = warScore.ResultNumber + warDesire + peaceDesire;

                // Log every candidate considered for this goal
                AIComputationLogger.LogWarCandidate(this.Kingdom, k, warScore.ResultNumber, warDesire, peaceDesire, finalScore, warScore);

                if (finalScore > bestScore)
                {
                    bestScore = finalScore;
                    bestTarget = k;
                }
            }

            if (bestTarget != null)
            {
                this.Priority = bestScore;
                this.Target = bestTarget;
            }
            else
            {
                this.Priority = 0;
            }
        }
    }
}