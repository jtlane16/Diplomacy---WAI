using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace TodayWeFeast
{
    public class FeastAttendingScoringModel
    {
        // The scoring model now accepts the FeastObject to know its duration.
        public ExplainedNumber GetFeastAttendingScore(Hero hero, FeastObject feast)
        {
            var score = new ExplainedNumber(0f, true);

            if (hero.IsPrisoner || hero.PartyBelongedTo == null)
            {
                score.Add(-1000f, new TextObject("Incapacitated"));
                return score;
            }

            // Relationship with the host remains an important factor.
            float relation = hero.GetRelation(feast.hostOfFeast);
            score.Add(relation * 2.0f, new TextObject("Relation with Host"));

            // Personality traits influence desire to socialize.
            int honor = hero.GetTraitLevel(DefaultTraits.Honor);
            score.Add(honor * 10f, DefaultTraits.Honor.Name);

            int mercy = hero.GetTraitLevel(DefaultTraits.Mercy);
            score.Add(mercy * 5f, DefaultTraits.Mercy.Name);

            // NEW: Penalty for feast duration. The longer it goes on, the more lords want to leave.
            // The penalty starts after the third day and grows daily.
            if (feast.currentDay > 3)
            {
                score.Add(-15f * (feast.currentDay - 3), new TextObject("Growing tired of the feast"));
            }

            // Distance from the feast still matters if the lord is not yet present.
            if (hero.CurrentSettlement != feast.feastSettlement)
            {
                // FIX: Used the correct properties for position.
                float distance = hero.PartyBelongedTo.Position2D.Distance(feast.feastSettlement.Position2D);
                const float MaxMapDistance = 1000f; // A scaling factor for distance penalty
                score.Add(-50f * (distance / MaxMapDistance), new TextObject("Distance to Feast"));
            }

            // Practical needs, like having enough food for their own party.
            if (hero.PartyBelongedTo.Food < hero.PartyBelongedTo.Party.NumberOfAllMembers)
            {
                score.Add(-100f, new TextObject("Low on Food"));
            }

            return score;
        }
    }
}
