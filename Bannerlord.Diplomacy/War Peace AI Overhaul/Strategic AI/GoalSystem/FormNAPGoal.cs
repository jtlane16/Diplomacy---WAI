using Diplomacy.Extensions;

using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace WarAndAiTweaks.AI.Goals
{
    public class FormNapGoal : AIGoal
    {
        public Kingdom OtherKingdom { get; }

        // Constants and logic taken directly from your DiplomacyScoringModels.cs
        private const float ThreatWeight = 50f;
        private const float BorderWeight = 30f;
        private const float RecoveryWeight = 20f;
        private const float Total = ThreatWeight + BorderWeight + RecoveryWeight;

        public FormNapGoal(Kingdom kingdom, Kingdom otherKingdom) : base(kingdom, GoalType.FormNonAggressionPact)
        {
            this.OtherKingdom = otherKingdom;
        }

        public override void EvaluatePriority()
        {
            var explainedNumber = new ExplainedNumber(0f, true);

            if (this.Kingdom == this.OtherKingdom || this.Kingdom.IsAtWarWith(this.OtherKingdom) || Diplomacy.DiplomaticAction.DiplomaticAgreementManager.HasNonAggressionPact(this.Kingdom, this.OtherKingdom, out _))
            {
                this.Priority = -100; // Invalid goal
                return;
            }

            float threatRatio = (this.OtherKingdom.TotalStrength + 1f) / (this.Kingdom.TotalStrength + 1f);
            explainedNumber.Add(MathF.Clamp(threatRatio, 0f, 2f) * 50f * ThreatWeight / Total, new TextObject("{=qO2yPZp2}Threat"));

            int borders = this.Kingdom.Settlements.Count(s => s.IsBorderSettlementWith(this.OtherKingdom));
            explainedNumber.Add(MathF.Clamp(borders * 10f, 0f, 100f) * BorderWeight / Total, new TextObject("{=kd5s37aT}Borders"));

            float recovery = this.Kingdom.GetCasualties() / (this.Kingdom.TotalStrength + 1f);
            explainedNumber.Add(recovery * 100f * RecoveryWeight / Total, new TextObject("{=exCaxz2C}Recovery"));

            this.Priority = explainedNumber.ResultNumber;
        }
    }
}