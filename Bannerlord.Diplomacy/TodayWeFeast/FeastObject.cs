using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace TodayWeFeast
{
    public class FeastObject
    {
        [SaveableField(1)] public Settlement FeastSettlement;
        [SaveableField(2)] public Kingdom Kingdom;
        [SaveableField(3)] public List<Hero> Guests;
        [SaveableField(4)] public Hero Host;
        [SaveableField(5)] public ItemRoster FeastRoster;
        [SaveableField(6)] public float FoodAmount;
        [SaveableField(7)] public int CurrentDay;
        [SaveableField(8)] public CampaignTime EndDate;
        [SaveableField(9)] public List<Hero> InitialGuests;

        public FeastObject(Hero host, Kingdom kingdom, List<Hero> guests)
        {
            Host = host;
            Kingdom = kingdom;
            FeastSettlement = host.HomeSettlement;
            Guests = new List<Hero>(guests);
            InitialGuests = new List<Hero>(guests);
            FeastRoster = new ItemRoster();
            CurrentDay = 0;
            EndDate = CampaignTime.DaysFromNow(15f);

            Initialize();
        }

        private void Initialize()
        {
            SendInvitations();
            CreateTournament();
            ContributeFood();
            ShowFeastQuality();
        }

        private void SendInvitations()
        {
            if (Host == Hero.MainHero)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Today, We Feast!",
                    $"You host a feast at {FeastSettlement.Name} where lords will gather to celebrate!",
                    true, false, "OK", null, null, null), true);
            }
            else if (Guests.Contains(Hero.MainHero))
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Feast Invitation",
                    $"You're invited to {Host.Name}'s feast at {FeastSettlement.Name}!",
                    true, false, "OK", null, null, null), true);
            }
            else if (Kingdom == Hero.MainHero.MapFaction)
            {
                var message = new TextObject("A feast has begun at {SETTLEMENT}, hosted by {HOST}!");
                message.SetTextVariable("SETTLEMENT", FeastSettlement.Name);
                message.SetTextVariable("HOST", Host.Name);
                MBInformationManager.AddQuickInformation(message);
            }
        }
        private void ShowFeastQuality()
        {
            if (Host == Hero.MainHero)
            {
                bool isLuxury = FoodAmount > Guests.Count * 4f;
                string quality = isLuxury ? "luxurious" : "modest";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"You prepare a {quality} feast with {FoodAmount:F0} food for {Guests.Count} guests.",
                    isLuxury ? Colors.Green : Colors.Yellow));
            }
        }

        private void CreateTournament()
        {
            if (FeastSettlement.IsTown)
            {
                var tournamentManager = Campaign.Current.TournamentManager;
                var existingTournament = tournamentManager.GetTournamentGame(FeastSettlement.Town);
                if (existingTournament == null)
                {
                    tournamentManager.AddTournament(
                        Campaign.Current.Models.TournamentModel.CreateTournament(FeastSettlement.Town));
                }
            }
        }

        private void ContributeFood()
        {
            if (Host.PartyBelongedTo?.ItemRoster == null) return;

            // IMPROVEMENT: Smarter food contribution based on guest count and host wealth
            float baseFood = Guests.Count * 3f; // 3 days worth minimum
            float wealthMultiplier = Math.Min(3f, Host.Gold / 50000f); // Up to 3x for very wealthy
            float targetFood = baseFood * wealthMultiplier;

            // Quality over quantity for rich hosts
            if (Host.Gold > 100000f)
            {
                ContributeQualityFood(targetFood);
            }
            else
            {
                ContributeBasicFood(targetFood);
            }
        }

        private void ContributeQualityFood(float target)
        {
            // Prefer expensive foods (better feast quality)
            var foods = Host.PartyBelongedTo.ItemRoster
                .Where(item => item.EquipmentElement.Item.IsFood)
                .OrderByDescending(item => item.EquipmentElement.Item.Value) // Expensive first
                .ToList();

            float contributed = 0f;
            foreach (var food in foods)
            {
                if (contributed >= target) break;

                int toTake = Math.Min(food.Amount, (int) (target - contributed));
                if (toTake > 0)
                {
                    FeastRoster.AddToCounts(food.EquipmentElement, toTake);
                    Host.PartyBelongedTo.ItemRoster.AddToCounts(food.EquipmentElement, -toTake);
                    contributed += toTake;
                }
            }
            FoodAmount = contributed * 1.2f; // Quality food lasts longer
        }

        private void ContributeBasicFood(float target)
        {
            // Use any available food efficiently
            var foods = Host.PartyBelongedTo.ItemRoster
                .Where(item => item.EquipmentElement.Item.IsFood)
                .OrderBy(item => item.EquipmentElement.Item.Value) // Cheap first (save the good stuff)
                .ToList();

            float contributed = 0f;
            foreach (var food in foods)
            {
                if (contributed >= target) break;

                // Take up to 80% of each food type (don't completely empty the party)
                int maxToTake = (int) (food.Amount * 0.8f);
                int toTake = Math.Min(maxToTake, (int) (target - contributed));

                if (toTake > 0)
                {
                    FeastRoster.AddToCounts(food.EquipmentElement, toTake);
                    Host.PartyBelongedTo.ItemRoster.AddToCounts(food.EquipmentElement, -toTake);
                    contributed += toTake;
                }
            }
            FoodAmount = contributed; // Standard efficiency
        }

        public void DailyTick()
        {
            CurrentDay++;

            ProcessGuestDepartures();
            ConsumeFood();
            ApplyDailyEffects();

            // Check if feast should end
            if (ShouldEndFeast())
            {
                EndFeast("The feast has run its course.");
            }
        }

        private void ProcessGuestDepartures()
        {
            var departing = new List<Hero>();

            foreach (var guest in Guests.ToList())
            {
                if (guest == Host || guest == Hero.MainHero) continue;

                // IMPROVEMENT: Smarter departure reasons
                var departureReason = ShouldGuestLeave(guest);
                if (departureReason != null)
                {
                    departing.Add(guest);

                    if (Host == Hero.MainHero)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{guest.Name} leaves: {departureReason}", Colors.Yellow));
                    }
                }
            }

            foreach (var guest in departing)
                Guests.Remove(guest);
        }

        private string ShouldGuestLeave(Hero guest)
        {
            // Urgent duties (high priority)
            if (guest.PartyBelongedTo?.Food < guest.PartyBelongedTo?.Party.NumberOfAllMembers)
                return "Urgent food shortage";

            if (guest.Clan.Settlements.Any(s => s.IsUnderSiege))
                return "Lands under siege";

            // Personal obligations
            if (guest.Spouse != null && guest.GetRelation(guest.Spouse) < -10f)
                return "Marital troubles"; // Bad marriage, need to go home

            // Social reasons
            var attendanceScore = GetAttendanceScore(guest);
            if (attendanceScore < -40f)
                return "Growing discomfort";

            // Natural departure after reasonable time
            if (CurrentDay > 5)
            {
                // Personality-based departure
                if (guest.GetTraitLevel(DefaultTraits.Calculating) > 0 && MBRandom.RandomFloat < 0.2f)
                    return "Strategic departure";

                if (CurrentDay > 8 && MBRandom.RandomFloat < 0.3f)
                    return "Natural conclusion";
            }

            return null; // Stay
        }

        private float GetAttendanceScore(Hero guest)
        {
            float score = 20f; // Base desire to stay

            // Relationship with host
            score += guest.GetRelation(Host) * 2f;

            // Personality traits
            score += guest.GetTraitLevel(DefaultTraits.Honor) * 8f;
            score += guest.GetTraitLevel(DefaultTraits.Generosity) * 6f;

            // Duration penalties
            if (CurrentDay > 3)
                score -= 15f * (CurrentDay - 3);

            if (CurrentDay > 7)
                score -= 25f * (CurrentDay - 7);

            // Distance penalty if not at feast
            if (guest.CurrentSettlement != FeastSettlement)
            {
                var distance = guest.PartyBelongedTo?.Position2D.Distance(FeastSettlement.Position2D) ?? 0f;
                score -= 40f * (distance / 800f);
            }

            // Practical needs
            if (guest.PartyBelongedTo?.Food < guest.PartyBelongedTo?.Party.NumberOfAllMembers)
                score -= 80f;

            return score;
        }

        private void ConsumeFood()
        {
            int attendingGuests = Guests.Count(g => g.CurrentSettlement == FeastSettlement);

            if (attendingGuests > 0 && FoodAmount > 0)
            {
                float consumption = Math.Min(FoodAmount, attendingGuests);
                FoodAmount -= consumption;

                // Remove from roster
                for (int i = 0; i < FeastRoster.Count && consumption > 0; i++)
                {
                    var item = FeastRoster[i];
                    int toRemove = Math.Min(item.Amount, (int) consumption);
                    if (toRemove > 0)
                    {
                        FeastRoster.AddToCounts(item.EquipmentElement, -toRemove);
                        consumption -= toRemove;
                    }
                }
            }
        }

        private void ApplyDailyEffects()
        {
            // Daily relation gains for attending guests
            if (Host != Hero.MainHero)
            {
                foreach (var guest in Guests.Where(g => g != Host && g != Hero.MainHero))
                {
                    if (guest.CurrentSettlement == FeastSettlement)
                    {
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(guest, Host, 1, false);
                    }
                }
            }
        }

        private bool ShouldEndFeast()
        {
            if (FoodAmount <= 0) return true;

            // IMPROVEMENT: Adaptive duration based on success
            var adaptiveDuration = CalculateOptimalDuration();

            if (CurrentDay >= adaptiveDuration) return true;

            if (Host != Hero.MainHero)
            {
                var endScore = GetEndingScore();
                return endScore >= GetDynamicEndThreshold();
            }

            return false;
        }

        private int CalculateOptimalDuration()
        {
            int baseDuration = 7; // Default week

            // Successful feast? Extend it
            if (InitialGuests.Count > 0)
            {
                float retention = (float) Guests.Count / InitialGuests.Count;
                if (retention > 0.8f) baseDuration += 3; // Great success
                else if (retention < 0.4f) baseDuration -= 2; // Poor turnout
            }

            // Rich host? Can afford longer
            if (Host.Gold > 150000f) baseDuration += 2;

            // Season
            if (CampaignTime.Now.GetSeasonOfYear == CampaignTime.Seasons.Winter)
                baseDuration += 2; // Winter feasts last longer

            return Math.Max(3, Math.Min(15, baseDuration)); // 3-15 day range
        }

        private float GetDynamicEndThreshold()
        {
            float baseThreshold = 100f;

            // Lower threshold if feast is going poorly
            if (InitialGuests.Count > 0)
            {
                float retention = (float) Guests.Count / InitialGuests.Count;
                if (retention < 0.3f) baseThreshold = 75f; // End early if failing
            }

            // Personality affects threshold
            baseThreshold += Host.GetTraitLevel(DefaultTraits.Generosity) * -10f; // Generous = longer
            baseThreshold += Host.GetTraitLevel(DefaultTraits.Calculating) * 10f; // Calculating = shorter

            return baseThreshold;
        }

        private float GetEndingScore()
        {
            float score = CurrentDay * 8f; // Base pressure increases with time

            // Duration-based pressure
            if (CurrentDay > 7)
                score += (CurrentDay - 7) * 15f;
            else if (CurrentDay <= 3)
                score -= 20f;

            // Guest retention
            if (InitialGuests.Count > 0)
            {
                float retention = (float) Guests.Count / InitialGuests.Count;
                if (retention < 0.3f) score += 40f;
                else if (retention < 0.5f) score += 25f;
                else if (retention > 0.8f && CurrentDay <= 5) score -= 15f;
            }

            // Resource pressure
            if (FoodAmount <= 0) score += 100f;
            else if (FoodAmount < Guests.Count * 2) score += 30f;

            // Financial pressure
            if (Host.Gold < 5000) score += 50f;
            else if (Host.Gold < 10000) score += 25f;

            // War pressure
            var enemies = FactionManager.GetEnemyKingdoms(Kingdom);
            if (enemies.Any())
            {
                score += enemies.Count() * 15f;
                if (Host.Clan.Settlements.Any(s => s.IsUnderSiege))
                    score += 75f;
            }

            // Trait influence
            score += Host.GetTraitLevel(DefaultTraits.Calculating) * 10f;
            if (Host.GetTraitLevel(DefaultTraits.Generosity) > 0 && CurrentDay <= 5)
                score -= Host.GetTraitLevel(DefaultTraits.Generosity) * 8f;

            return score;
        }

        public void EndFeast(string reason = "")
        {
            var message = string.IsNullOrEmpty(reason)
                ? $"The feast at {FeastSettlement.Name} has ended."
                : $"The feast at {FeastSettlement.Name} has ended: {reason}";

            InformationManager.DisplayMessage(new InformationMessage(message, Colors.Green));

            // Host rewards
            Host.Clan.AddRenown(10, true);
            Host.AddSkillXp(DefaultSkills.Steward, 500);

            // Relation bonuses for original guests
            if (InitialGuests != null)
            {
                foreach (var guest in InitialGuests.Where(g => g != Host))
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Host, guest, 1, true);
                }
            }

            // Clean up tournament
            if (FeastSettlement.IsTown)
            {
                var tournamentManager = Campaign.Current.TournamentManager;
                var tournament = tournamentManager.GetTournamentGame(FeastSettlement.Town);
                if (tournament != null)
                {
                    tournamentManager.ResolveTournament(tournament, FeastSettlement.Town);
                }
            }

            // Update timing restrictions
            if (!FeastBehavior.Instance.timeSinceLastFeast.ContainsKey(Kingdom))
            {
                FeastBehavior.Instance.timeSinceLastFeast.Add(Kingdom, CampaignTime.Now.ToDays);
            }

            // Remove from active feasts
            FeastBehavior.Instance.Feasts.Remove(this);
        }
    }
}