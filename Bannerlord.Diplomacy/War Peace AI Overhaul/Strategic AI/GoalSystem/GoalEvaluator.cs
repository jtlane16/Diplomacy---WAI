using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;

using WarAndAiTweaks.AI.Goals;

using static WarAndAiTweaks.AI.StrategicAI;

namespace WarAndAiTweaks.AI
{
    public static class GoalEvaluator
    {
        public static AIGoal GetHighestPriorityGoal(Kingdom kingdom, int daysSinceLastWar, int daysAtWar)
        {
            var warEvaluator = new DefaultWarEvaluator();
            var peaceEvaluator = new DefaultPeaceEvaluator();

            // ONLY evaluate the three core goals.
            var potentialGoals = new List<AIGoal>
            {
                new ExpandGoal(kingdom, warEvaluator, daysSinceLastWar, daysAtWar),
                new SurviveGoal(kingdom, peaceEvaluator),
                new StrengthenGoal(kingdom)
            };

            foreach (var goal in potentialGoals)
            {
                goal.EvaluatePriority();
            }

            return potentialGoals.OrderByDescending(g => g.Priority).First();
        }
    }
}