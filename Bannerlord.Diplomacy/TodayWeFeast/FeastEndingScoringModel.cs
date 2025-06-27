using System;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace TodayWeFeast
{
    public class FeastEndingScoringModel
    {
        public ExplainedNumber GetFeastEndingScore(FeastObject feast)
        {
            var score = new ExplainedNumber(0f, true);

            if (feast?.hostOfFeast == null || feast.kingdom == null)
            {
                score.Add(1000f, new TextObject("Invalid feast data"));
                return score;
            }

            // --- DURATION-BASED FACTORS ---

            // Base pressure to end increases with duration
            float durationPressure = feast.currentDay * 8f;
            score.Add(durationPressure, new TextObject("Natural conclusion after {DAYS} days").SetTextVariable("DAYS", feast.currentDay));

            // Significant pressure after optimal duration (5-7 days)
            if (feast.currentDay > 7)
            {
                float overstayPenalty = (feast.currentDay - 7) * 15f;
                score.Add(overstayPenalty, new TextObject("Feast has run too long"));
            }
            else if (feast.currentDay <= 3)
            {
                // Resist ending too early unless urgent
                score.Add(-20f, new TextObject("Too early to end the festivities"));
            }

            // --- GUEST SATISFACTION ---

            int totalGuests = feast.initialLordsInFeast?.Count ?? feast.lordsInFeast?.Count ?? 0;
            int currentGuests = feast.lordsInFeast?.Count ?? 0;

            if (totalGuests > 0)
            {
                float guestRetentionRatio = (float)currentGuests / totalGuests; // Fix: Declare and calculate guestRetentionRatio here

                if (guestRetentionRatio < 0.3f)
                {
                    score.Add(40f, new TextObject("Most guests have already departed"));
                }
                else if (guestRetentionRatio < 0.5f)
                {
                    score.Add(25f, new TextObject("Many guests have left"));
                }
                else if (guestRetentionRatio > 0.8f && feast.currentDay <= 5)
                {
                    score.Add(-15f, new TextObject("Guests are still enjoying themselves"));
                }
            }

            // --- RESOURCE CONSIDERATIONS ---

            // Food scarcity pressure
            if (feast.amountOfFood <= 0)
            {
                score.Add(100f, new TextObject("No food remaining"));
            }
            else if (feast.amountOfFood < currentGuests * 2)
            {
                score.Add(30f, new TextObject("Food supplies running low"));
            }

            // Host's financial situation
            Hero host = feast.hostOfFeast;
            if (host.Gold < 10000)
            {
                score.Add(25f, new TextObject("Host's treasury is strained"));
            }
            else if (host.Gold < 5000)
            {
                score.Add(50f, new TextObject("Host cannot afford to continue"));
            }

            // --- EXTERNAL PRESSURES ---

            // War pressure
            var enemyKingdoms = FactionManager.GetEnemyKingdoms(feast.kingdom);
            if (enemyKingdoms.Any())
            {
                int enemyCount = enemyKingdoms.Count();
                float warPressure = enemyCount * 15f;
                score.Add(warPressure, new TextObject("Kingdom faces military threats"));

                // Additional pressure if host's settlements are under threat
                if (host.Clan.Settlements.Any(s => s.IsUnderSiege))
                {
                    score.Add(75f, new TextObject("Host's lands are under siege"));
                }
            }

            // NEW: Strategic war preparation pressure
            var potentialWarTargets = Kingdom.All.Where(k => 
                k != feast.kingdom && 
                !feast.kingdom.IsAtWarWith(k) && 
                !FactionManager.IsAlliedWithFaction(feast.kingdom, k)).ToList();

            foreach (var target in potentialWarTargets)
            {
                // If target is vulnerable, pressure to end feast and strike
                if (target.TotalStrength < feast.kingdom.TotalStrength * 1.2f && 
                    FactionManager.GetEnemyKingdoms(target).Any())
                {
                    score.Add(30f, new TextObject("Strategic opportunity requires attention"));
                    break;
                }
            }

            // Kingdom stability issues
            var discontentLords = feast.kingdom.Lords.Count(l => l.GetRelation(feast.kingdom.Leader) < -20);
            if (discontentLords > feast.kingdom.Lords.Count() * 0.4f)
            {
                score.Add(20f, new TextObject("Political tensions require attention"));
            }

            // --- HOST PERSONALITY FACTORS ---

            // Calculating hosts end feasts more strategically
            int calculating = host.GetTraitLevel(DefaultTraits.Calculating);
            if (calculating > 0)
            {
                score.Add(calculating * 10f, DefaultTraits.Calculating.Name);
            }

            // Generous hosts are reluctant to end early
            int generosity = host.GetTraitLevel(DefaultTraits.Generosity);
            if (generosity > 0 && feast.currentDay <= 5)
            {
                score.Add(generosity * -8f, DefaultTraits.Generosity.Name);
            }

            // Honorable hosts maintain proper feast duration
            int honor = host.GetTraitLevel(DefaultTraits.Honor);
            if (honor > 0)
            {
                if (feast.currentDay < 3)
                {
                    score.Add(honor * -15f, new TextObject("Honor demands proper hospitality"));
                }
                else if (feast.currentDay > 8)
                {
                    score.Add(honor * 10f, new TextObject("Honor satisfied, duties call"));
                }
            }

            // --- SEASONAL AND CULTURAL FACTORS ---

            // Winter feasts naturally last longer
            var currentSeason = CampaignTime.Now.GetSeasonOfYear;
            if (currentSeason == CampaignTime.Seasons.Winter && feast.currentDay <= 6)
            {
                score.Add(-10f, new TextObject("Winter celebrations are expected to last longer"));
            }

            // Cultural preferences
            switch (host.Culture.StringId.ToLower())
            {
                case "sturgia": // Norse feast culture - longer celebrations
                    if (feast.currentDay <= 6) score.Add(-5f, new TextObject("Northern tradition favors longer feasts"));
                    break;
                case "khuzait": // Nomadic - shorter, more practical
                    if (feast.currentDay >= 4) score.Add(8f, new TextObject("Nomadic culture prefers brief celebrations"));
                    break;
                case "vlandia": // Chivalric - proper duration important
                    if (feast.currentDay < 3) score.Add(-12f, new TextObject("Chivalric honor demands adequate celebration"));
                    else if (feast.currentDay > 7) score.Add(10f, new TextObject("Proper feast duration has been observed"));
                    break;
            }

            // --- OPPORTUNITY COSTS ---

            // Campaign season pressure (spring/summer)
            if (currentSeason == CampaignTime.Seasons.Spring || currentSeason == CampaignTime.Seasons.Summer)
            {
                if (feast.currentDay >= 5)
                {
                    score.Add(15f, new TextObject("Campaign season demands action"));
                }
            }

            // Settlement management needs
            if (host.Clan.Settlements.Any(s => s.Town?.Security < 30f || s.Town?.Loyalty < 30f))
            {
                score.Add(20f, new TextObject("Settlements require immediate attention"));
            }

            // NEW: Economic pressure from opportunity costs
            if (host.Gold > 200000 && feast.currentDay >= 4)
            {
                var tradePotential = host.Clan.Settlements.Count(s => s.IsTown) * 5f;
                score.Add(tradePotential, new TextObject("Economic opportunities await"));
            }

            // --- SUCCESS THRESHOLD MODIFIERS ---

            // Successful feast bonus (many guests stayed long)
            if (totalGuests > 0) // Ensure guestRetentionRatio is calculated before use
            {
                float guestRetentionRatio = (float)currentGuests / totalGuests;
                if (guestRetentionRatio > 0.7f && feast.currentDay >= 4)
                {
                    score.Add(10f, new TextObject("Feast has been a great success"));
                }
            }

            // Tournament completion (if applicable)
            if (feast.feastSettlement.IsTown && feast.currentDay >= 3)
            {
                var tournament = Campaign.Current.TournamentManager.GetTournamentGame(feast.feastSettlement.Town);
                if (tournament == null) // Tournament finished
                {
                    score.Add(15f, new TextObject("Tournament has concluded"));
                }
            }

            return score;
        }
    }
}