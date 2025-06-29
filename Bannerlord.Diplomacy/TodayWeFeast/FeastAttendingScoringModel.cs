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

            // Base desire to attend feast
            score.Add(20f, new TextObject("Base social desire"));

            // Relationship with the host remains an important factor.
            float relation = hero.GetRelation(feast.hostOfFeast);
            score.Add(relation * 2.0f, new TextObject("Relation with Host"));

            // Personality traits influence desire to socialize.
            int honor = hero.GetTraitLevel(DefaultTraits.Honor);
            score.Add(honor * 10f, DefaultTraits.Honor.Name);

            int mercy = hero.GetTraitLevel(DefaultTraits.Mercy);
            score.Add(mercy * 5f, DefaultTraits.Mercy.Name);

            int generosity = hero.GetTraitLevel(DefaultTraits.Generosity);
            score.Add(generosity * 8f, DefaultTraits.Generosity.Name);

            // ENHANCED: More aggressive feast duration penalties
            if (feast.currentDay > 3)
            {
                float durationPenalty = -20f * (feast.currentDay - 3); // Increased from -15f
                score.Add(durationPenalty, new TextObject("Growing tired of the feast"));
            }

            // ADDITIONAL: Stronger penalties for very long feasts
            if (feast.currentDay > 7)
            {
                float extremePenalty = -30f * (feast.currentDay - 7);
                score.Add(extremePenalty, new TextObject("Feast has gone on far too long"));
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
