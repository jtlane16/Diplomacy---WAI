using Diplomacy.Extensions;

using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

using static WarAndAiTweaks.AI.StrategicAI;

namespace WarAndAiTweaks.AI.Goals
{
    public class BreakAllianceGoal : AIGoal
    {
        public Kingdom Ally { get; }

        public BreakAllianceGoal(Kingdom kingdom, Kingdom ally) : base(kingdom, GoalType.BreakAlliance)
        {
            this.Ally = ally;
        }

        public override void EvaluatePriority()
        {
            var explainedNumber = new ExplainedNumber(0f, true);

            if (!FactionManager.IsAlliedWithFaction(this.Kingdom, this.Ally))
            {
                this.Priority = -100; // Invalid goal
                return;
            }

            // Logic from your BreakAllianceScoringModel
            float relation = this.Kingdom.GetRelation(this.Ally);
            explainedNumber.Add(MathF.Clamp(-relation, 0f, 100f) * 0.25f, new TextObject("{=D64t2BEi}Relations"));

            int sharedEnemies = FactionManager.GetEnemyKingdoms(this.Kingdom).Intersect(FactionManager.GetEnemyKingdoms(this.Ally)).Count();
            if (sharedEnemies == 0)
            {
                explainedNumber.Add(40f, new TextObject("{=DP0INA9b}No Shared Enemies"));
            }

            int borders = this.Kingdom.Settlements.Count(s => s.IsBorderSettlementWith(this.Ally));
            explainedNumber.Add(borders * 5f, new TextObject("{=j4FesgAb}Border Competition"));

            float ratio = this.Kingdom.TotalStrength / (this.Ally.TotalStrength + 1f);
            if (ratio > 1.2f)
            {
                explainedNumber.Add(20f, new TextObject("{=1iJc5I2V}Military Advantage"));
            }

            var warEvaluator = new DefaultWarEvaluator();
            var warScoreVsAlly = warEvaluator.GetWarScore(this.Kingdom, this.Ally);

            if (warScoreVsAlly.ResultNumber > 50f)
            {
                explainedNumber.Add(warScoreVsAlly.ResultNumber * 0.75f, new TextObject("{=Lxdg4c3T}Ally is an Opportune Target"));
            }

            this.Priority = explainedNumber.ResultNumber;
        }
    }
}