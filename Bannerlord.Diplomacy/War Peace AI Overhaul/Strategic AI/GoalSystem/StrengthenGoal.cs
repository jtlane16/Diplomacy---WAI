using System.Linq;
using TaleWorlds.CampaignSystem;
using Diplomacy.Extensions;

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

            // --- Threat Assessment ---
            Kingdom? primaryThreat = otherKingdoms.OrderByDescending(k => k.TotalStrength).FirstOrDefault();
            float threatBonus = 0;

            if (primaryThreat != null)
            {
                bool bordersThreat = this.Kingdom.Settlements.Any(s => s.IsBorderSettlementWith(primaryThreat));
                if (bordersThreat)
                {
                    threatBonus = 25f; // Significant bonus to seek allies against a powerful neighbor.
                }

                float strengthRatioVsThreat = this.Kingdom.TotalStrength / (primaryThreat.TotalStrength + 1f);
                if (strengthRatioVsThreat < 0.7f)
                {
                    threatBonus += 15f;
                }
            }

            // --- Base Priority Calculation ---
            var averageStrength = otherKingdoms.Average(k => k.TotalStrength);
            if (averageStrength <= 0)
            {
                this.Priority = 0;
                return;
            }

            var strengthRatio = this.Kingdom.TotalStrength / averageStrength;

            if (strengthRatio < 1.2f)
            {
                this.Priority = (1.2f - strengthRatio) * 50f;
            }
            else
            {
                this.Priority = 0;
            }

            // Add the threat bonus to the final priority.
            this.Priority += threatBonus;
        }
    }
}