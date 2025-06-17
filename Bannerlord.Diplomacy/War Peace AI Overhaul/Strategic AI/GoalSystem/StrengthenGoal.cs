using System.Linq;

using TaleWorlds.CampaignSystem;

namespace WarAndAiTweaks.AI.Goals
{
    public class StrengthenGoal : AIGoal
    {
        public StrengthenGoal(Kingdom kingdom) : base(kingdom, GoalType.Strengthen) { }

        public override void EvaluatePriority()
        {
            var otherKingdoms = Kingdom.All.Where(k => k != this.Kingdom && !k.IsEliminated && k.Fiefs.Any()).ToList();
            if (!otherKingdoms.Any())
            {
                this.Priority = 0;
                return;
            }

            var averageStrength = otherKingdoms.Average(k => k.TotalStrength);

            if (averageStrength <= 0)
            {
                this.Priority = 0;
                return;
            }

            var strengthRatio = this.Kingdom.TotalStrength / averageStrength;

            // Don't pursue this goal if we are already strong enough.
            if (strengthRatio > 1.2f)
            {
                this.Priority = 0;
                return;
            }

            // Priority is higher the weaker the kingdom is compared to the average.
            // A ratio of 0.5 gives a priority of (1.2 - 0.5) * 50 = 35.
            // A ratio of 1.0 gives a priority of (1.2 - 1.0) * 50 = 10.
            this.Priority = (1.2f - strengthRatio) * 50f;
        }
    }
}