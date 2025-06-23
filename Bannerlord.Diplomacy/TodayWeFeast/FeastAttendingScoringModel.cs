using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace TodayWeFeast
{
    public class FeastAttendingScoringModel
    {
        public ExplainedNumber GetFeastAttendingScore(Hero hero, FeastObject feast)
        {
            var score = new ExplainedNumber(0f, true);

            if (hero.IsPrisoner || hero.PartyBelongedTo == null)
            {
                score.Add(-1000f, new TextObject("Incapacitated"));
                return score;
            }

            // Relationship with the host is the most important factor
            float relation = hero.GetRelation(feast.hostOfFeast);
            score.Add(relation * 2.0f, new TextObject("Relation with Host"));

            // Personality traits
            int honor = hero.GetTraitLevel(DefaultTraits.Honor);
            score.Add(honor * 10f, DefaultTraits.Honor.Name); // Honorable lords attend social gatherings

            int mercy = hero.GetTraitLevel(DefaultTraits.Mercy);
            score.Add(mercy * 5f, DefaultTraits.Mercy.Name); // Kind lords are more social

            // Distance
            float distance = hero.PartyBelongedTo.GetPosition().Distance(feast.feastSettlement.GetPosition());

            // FIX: Replace the incorrect property with a constant value for scaling
            const float MaxMapDistance = 1000f;
            score.Add(-50f * (distance / MaxMapDistance), new TextObject("Distance to Feast"));


            // Party Needs
            if (hero.PartyBelongedTo.Food < hero.PartyBelongedTo.Party.NumberOfAllMembers)
            {
                score.Add(-100f, new TextObject("Low on Food"));
            }

            return score;
        }
    }
}