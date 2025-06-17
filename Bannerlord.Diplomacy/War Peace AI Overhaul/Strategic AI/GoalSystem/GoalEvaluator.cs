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

            // Start with base goals
            var potentialGoals = new List<AIGoal>
            {
                new ExpandGoal(kingdom, warEvaluator, daysSinceLastWar, daysAtWar),
                new SurviveGoal(kingdom, peaceEvaluator),
                new StrengthenGoal(kingdom)
            };

            // Consider forming alliances
            foreach (var otherKingdom in Kingdom.All.Where(k => k != kingdom && !k.IsEliminated))
            {
                if (!FactionManager.IsAlliedWithFaction(kingdom, otherKingdom) && !kingdom.IsAtWarWith(otherKingdom))
                {
                    potentialGoals.Add(new FormAllianceGoal(kingdom, otherKingdom));
                }
            }

            // Consider forming non-aggression pacts
            foreach (var otherKingdom in Kingdom.All.Where(k => k != kingdom && !k.IsEliminated))
            {
                if (!Diplomacy.DiplomaticAction.DiplomaticAgreementManager.HasNonAggressionPact(kingdom, otherKingdom, out _) && !kingdom.IsAtWarWith(otherKingdom))
                {
                    potentialGoals.Add(new FormNapGoal(kingdom, otherKingdom));
                }
            }

            // Consider breaking existing alliances
            foreach (var ally in FactionManager.GetEnemyKingdoms(kingdom).Where(k => FactionManager.IsAlliedWithFaction(kingdom, k)))
            {
                potentialGoals.Add(new BreakAllianceGoal(kingdom, ally));
            }

            // Evaluate all potential goals and return the best one
            foreach (var goal in potentialGoals)
            {
                goal.EvaluatePriority();
            }

            return potentialGoals.OrderByDescending(g => g.Priority).First();
        }
    }
}