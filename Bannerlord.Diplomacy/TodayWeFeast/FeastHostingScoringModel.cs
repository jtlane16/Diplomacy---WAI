using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace TodayWeFeast
{
    public class FeastHostingScoringModel
    {
        public ExplainedNumber GetFeastHostingScore(Hero potentialHost)
        {
            var score = new ExplainedNumber(0f, true);

            // --- RE-BALANCED SCORES ---

            // Increased base desire to make all lords more social.
            score.Add(40, new TextObject("Base desire to host a feast"));

            // Trait-based scoring remains a key factor.
            int generosity = potentialHost.GetTraitLevel(DefaultTraits.Generosity);
            if (generosity > 0)
            {
                score.Add(generosity * 15, DefaultTraits.Generosity.Name);
            }

            int calculating = potentialHost.GetTraitLevel(DefaultTraits.Calculating);
            if (calculating < 0)
            {
                score.Add(calculating * -10, DefaultTraits.Calculating.Name);
            }

            // Increased wealth bonuses to better reflect a lord's ability to host.
            if (potentialHost.Gold > 100000)
            {
                score.Add(30, new TextObject("Is very wealthy"));
            }
            else if (potentialHost.Gold > 50000)
            {
                score.Add(20, new TextObject("Is wealthy"));
            }

            // This penalty remains a good check on unstable kingdoms.
            if (potentialHost.Clan.Kingdom.Lords.Any(l => l.GetRelation(potentialHost.Clan.Kingdom.Leader) < -20))
            {
                score.Add(-30, new TextObject("Low morale in the realm"));
            }

            return score;
        }
    }
}