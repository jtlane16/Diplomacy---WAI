using Diplomacy.Extensions;

using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace WarAndAiTweaks.AI.Goals
{
    public class FormAllianceGoal : AIGoal
    {
        public Kingdom OtherKingdom { get; }

        // Constants and logic taken directly from your DiplomacyScoringModels.cs
        private const float SharedEnemyWeight = 60f;
        private const float StrengthSynergyWeight = 35f;
        private const float RelationsWeight = 5f;
        private const float TotalWeight = SharedEnemyWeight + StrengthSynergyWeight + RelationsWeight;

        public FormAllianceGoal(Kingdom kingdom, Kingdom otherKingdom) : base(kingdom, GoalType.FormAlliance)
        {
            this.OtherKingdom = otherKingdom;
        }

        public override void EvaluatePriority()
        {
            var explainedNumber = new ExplainedNumber(0f, true);

            if (this.Kingdom == this.OtherKingdom || FactionManager.IsAlliedWithFaction(this.Kingdom, this.OtherKingdom) || this.Kingdom.IsAtWarWith(this.OtherKingdom))
            {
                this.Priority = -100; // Invalid goal
                return;
            }

            int sharedEnemies = FactionManager.GetEnemyKingdoms(this.Kingdom)
                                              .Intersect(FactionManager.GetEnemyKingdoms(this.OtherKingdom)).Count();
            explainedNumber.Add(MathF.Clamp(sharedEnemies * 25f, 0f, 100f) * SharedEnemyWeight / TotalWeight, new TextObject("{=DP0INA9b}Shared Enemies"));

            var enemyKingdoms = FactionManager.GetEnemyKingdoms(this.Kingdom).Concat(FactionManager.GetEnemyKingdoms(this.OtherKingdom));
            float maxEnemyStrength = enemyKingdoms.Any() ? enemyKingdoms.Max(k => k.TotalStrength) : 1f;
            float synergy = (this.Kingdom.TotalStrength + this.OtherKingdom.TotalStrength) / maxEnemyStrength;
            explainedNumber.Add(MathF.Clamp(synergy, 0f, 2f) * 50f * StrengthSynergyWeight / TotalWeight, new TextObject("{=H8oVp21s}Strength Synergy"));

            float relation = this.Kingdom.GetRelation(this.OtherKingdom);
            float relScore = (MathF.Clamp(relation, -100f, 100f) + 100f) * 0.5f;
            explainedNumber.Add(relScore * RelationsWeight / TotalWeight, new TextObject("{=D64t2BEi}Relations"));

            this.Priority = explainedNumber.ResultNumber;
        }
    }
}