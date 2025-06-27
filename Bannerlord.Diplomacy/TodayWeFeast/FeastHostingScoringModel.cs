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
            else if (potentialHost.Gold < 30000)
            {
                score.Add(-20, new TextObject("Has limited funds"));
            }

            // Leadership and relations
            if (potentialHost == potentialHost.Clan.Kingdom.Leader)
            {
                score.Add(25, new TextObject("Is the ruler"));
            }
            else if (potentialHost.Clan.Tier >= 4)
            {
                score.Add(15, new TextObject("Is a major noble"));
            }

            // Settlement quality
            if (potentialHost.HomeSettlement != null)
            {
                if (potentialHost.HomeSettlement.IsTown)
                {
                    score.Add(15, new TextObject("Owns a prosperous town"));
                }
                else if (potentialHost.HomeSettlement.IsCastle)
                {
                    score.Add(10, new TextObject("Has a proper castle"));
                }
            }

            // Recent feast hosting penalty
            var recentFeastPenalty = GetRecentHostingPenalty(potentialHost);
            if (recentFeastPenalty < 0)
            {
                score.Add(recentFeastPenalty, new TextObject("Recently hosted a feast"));
            }

            // Kingdom stability check
            if (potentialHost.Clan.Kingdom.Lords.Any(l => l.GetRelation(potentialHost.Clan.Kingdom.Leader) < -20))
            {
                score.Add(-25, new TextObject("Low morale in the realm"));
            }

            // NEW: Strategic timing bonuses
            var strategicBonus = GetStrategicTimingBonus(potentialHost);
            if (strategicBonus > 0)
            {
                score.Add(strategicBonus, new TextObject("Strategic timing is favorable"));
            }

            // NEW: Diplomatic opportunity assessment
            var diplomaticValue = GetDiplomaticFeastValue(potentialHost);
            if (diplomaticValue > 0)
            {
                score.Add(diplomaticValue, new TextObject("Feast could improve diplomatic relations"));
            }

            return score;
        }

        private float GetRecentHostingPenalty(Hero host)
        {
            // Check if this hero recently hosted (would need tracking)
            // For now, return 0, but this could be expanded
            return 0f;
        }

        private float GetStrategicTimingBonus(Hero host)
        {
            float bonus = 0f;
            var kingdom = host.Clan.Kingdom;

            // Bonus for hosting after military victories
            var recentWars = FactionManager.GetEnemyKingdoms(kingdom);
            if (!recentWars.Any()) // Changed from DaysSinceLastWar reference
            {
                // Check if recently ended a war (this would need to be tracked separately)
                bonus += 25f;
            }

            // Bonus for consolidating power
            if (kingdom.Clans.Count >= 4 && kingdom.Lords.Average(l => l.GetRelation(kingdom.Leader)) < 10)
            {
                bonus += 20f; // Need to improve internal relations
            }

            return bonus;
        }

        private float GetDiplomaticFeastValue(Hero host)
        {
            var kingdom = host.Clan.Kingdom;
            var neutralKingdoms = Kingdom.All.Where(k =>
                k != kingdom &&
                !kingdom.IsAtWarWith(k) &&
                !FactionManager.IsAlliedWithFaction(kingdom, k) &&
                kingdom.Leader.GetRelation(k.Leader) < 20).Count(); // Fixed GetRelation call

            return neutralKingdoms * 5f; // Value for each potential diplomatic target
        }
    }
}
