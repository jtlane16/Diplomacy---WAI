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

    // NEW: This class provides the logic for an AI host to decide when to end a feast.
    public class FeastEndingScoringModel
    {
        public ExplainedNumber GetFeastEndingScore(FeastObject feast)
        {
            var score = new ExplainedNumber(0f, true);

            // The desire to end the feast increases each day it goes on.
            score.Add(feast.currentDay * 8f, new TextObject("Feast Duration"));

            // Low attendance is a major reason to end a feast.
            float initialGuestCount = feast.initialLordsInFeast.Count > 1 ? feast.initialLordsInFeast.Count : 1;
            float currentGuestCount = feast.lordsInFeast.Count;
            float guestRetentionRatio = currentGuestCount / initialGuestCount;

            if (guestRetentionRatio < 0.6f)
            {
                // The score to end the feast increases dramatically as more guests leave.
                score.Add((1 - guestRetentionRatio) * 150f, new TextObject("Low Attendance"));
            }

            // Running out of food is also a strong motivator to end the party.
            if (feast.amountOfFood < (feast.lordsInFeast.Count * 2)) // Less than 2 days of food left
            {
                score.Add(40f, new TextObject("Low food supplies"));
            }

            // If the kingdom goes to war, feasts should end immediately.
            bool isAtWar = false;
            foreach (var stance in feast.kingdom.Stances)
            {
                if (stance.IsAtWar)
                {
                    isAtWar = true;
                    break;
                }
            }
            if (isAtWar)
            {
                score.Add(200f, new TextObject("The Kingdom is now at war"));
            }

            return score;
        }
    }
}
