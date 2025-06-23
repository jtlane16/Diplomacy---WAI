// In TodayWeFeast/FeastHostingScoringModel.cs

using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization; // <-- Add this using statement

namespace TodayWeFeast
{
    public class FeastHostingScoringModel
    {
        public ExplainedNumber GetFeastHostingScore(Hero potentialHost)
        {
            var score = new ExplainedNumber(0f, true);

            // Base desire to host a feast
            score.Add(20, new TextObject("Base desire to host a feast")); // FIX

            // Trait-based scoring
            int generosity = potentialHost.GetTraitLevel(DefaultTraits.Generosity);
            if (generosity > 0)
            {
                score.Add(generosity * 20, DefaultTraits.Generosity.Name); // This is already correct
            }

            int calculating = potentialHost.GetTraitLevel(DefaultTraits.Calculating);
            if (calculating < 0)
            {
                score.Add(calculating * -10, DefaultTraits.Calculating.Name); // This is also correct
            }

            // Wealth check
            if (potentialHost.Gold > 100000)
            {
                score.Add(25, new TextObject("Is very wealthy")); // FIX
            }
            else if (potentialHost.Gold > 50000)
            {
                score.Add(15, new TextObject("Is wealthy")); // FIX
            }

            // Kingdom stability - less likely to feast if lords are discontent
            if (potentialHost.Clan.Kingdom.Lords.Any(l => l.GetRelation(potentialHost.Clan.Kingdom.Leader) < -20))
            {
                score.Add(-30, new TextObject("Low morale in the realm")); // FIX
            }

            return score;
        }
    }
}