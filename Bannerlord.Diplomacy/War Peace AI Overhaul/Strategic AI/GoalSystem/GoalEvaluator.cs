using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;

using WarAndAiTweaks.AI.Goals;

using static WarAndAiTweaks.AI.StrategicAI;

namespace WarAndAiTweaks.AI
{
    public static class GoalEvaluator
    {
        public static AIGoal GetHighestPriorityGoal(Kingdom kingdom, int daysSinceLastWar, int daysAtWar, StrategicState strategicState)
        {
            var warEvaluator = new DefaultWarEvaluator();
            var peaceEvaluator = new DefaultPeaceEvaluator();

            var potentialGoals = new List<AIGoal>
            {
                new ExpandGoal(kingdom, warEvaluator, daysSinceLastWar, daysAtWar),
                new SurviveGoal(kingdom, peaceEvaluator),
                new StrengthenGoal(kingdom)
            };

            foreach (var goal in potentialGoals)
            {
                goal.EvaluatePriority();

                // CORRECTED LOGIC: A kingdom at peace should never have "Survive" as its goal.
                // Apply a massive penalty to the Survive goal if the kingdom is not at war.
                if (goal.Type == GoalType.Survive && !FactionManager.GetEnemyKingdoms(kingdom).Any())
                {
                    goal.Priority = -1000f;
                }

                // Adjust priority based on strategic state
                switch (strategicState)
                {
                    case StrategicState.Desperate:
                        if (goal.Type == GoalType.Survive) goal.Priority += 100;
                        else goal.Priority -= 200;
                        break;
                    case StrategicState.Rebuilding:
                        if (goal.Type == GoalType.Strengthen) goal.Priority += 50;
                        if (goal.Type == GoalType.Expand) goal.Priority -= 100;
                        break;
                    case StrategicState.Defensive:
                        if (goal.Type == GoalType.Strengthen) goal.Priority += 30;
                        if (goal.Type == GoalType.Expand) goal.Priority -= 50;
                        break;
                    case StrategicState.Expansionist:
                        if (goal.Type == GoalType.Expand) goal.Priority += 50;
                        break;
                    case StrategicState.Opportunistic:
                        // No changes
                        break;
                }
            }
            return potentialGoals.OrderByDescending(g => g.Priority).First();
        }
    }
}