using System;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

using static WarAndAiTweaks.AI.StrategicAI; // Allows access to IPeaceEvaluator

namespace WarAndAiTweaks.AI.Goals
{
    public class SurviveGoal : AIGoal
    {
        public Kingdom? PeaceCandidate { get; private set; }
        private readonly IPeaceEvaluator _peaceEvaluator;

        public SurviveGoal(Kingdom kingdom, IPeaceEvaluator peaceEvaluator) : base(kingdom, GoalType.Survive)
        {
            _peaceEvaluator = peaceEvaluator;
        }

        public override void EvaluatePriority()
        {
            var enemies = FactionManager.GetEnemyKingdoms(Kingdom).ToList();
            if (!enemies.Any())
            {
                this.Priority = 0;
                return;
            }

            // Survival is a high priority if losing a war badly.
            // This logic can be expanded significantly.
            float highestPeaceScore = 0f;
            foreach (var enemy in enemies)
            {
                var peaceScore = _peaceEvaluator.GetPeaceScore(Kingdom, enemy).ResultNumber;
                if (peaceScore > highestPeaceScore)
                {
                    highestPeaceScore = peaceScore;
                    PeaceCandidate = enemy;
                }
            }

            this.Priority = highestPeaceScore;
        }
    }
}