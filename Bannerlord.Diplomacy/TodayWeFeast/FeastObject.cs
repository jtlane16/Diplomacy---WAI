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

        // NEW: Main constructor that accepts settlement parameter
        public FeastObject(Hero host, Kingdom kingdom, List<Hero> guests, Settlement feastSettlement, float foodContribution = -1)
        {
            Host = host;
            Kingdom = kingdom;
            FeastSettlement = feastSettlement; // FIXED: Use passed settlement instead of always using home settlement
            Guests = new List<Hero>(guests);
            InitialGuests = new List<Hero>(guests);
            FeastRoster = new ItemRoster();
            CurrentDay = 0;
            EndDate = CampaignTime.DaysFromNow(15f);

            Initialize();

            // RESTORED: Handle food contribution
            if (foodContribution != -1)
            {
                MakeAILordContributeToFeast(host, foodContribution);
            }
            else
            {
                // Player feast - use their party food
                ContributePlayerFood();
            }
        }

        // BACKWARD COMPATIBILITY: Old constructor for existing AI feast code
        public FeastObject(Hero host, Kingdom kingdom, List<Hero> guests, float foodContribution = -1)
            : this(host, kingdom, guests, host.HomeSettlement, foodContribution)
        {
            // Delegates to main constructor with home settlement for AI hosts
        }

        private void MakeAILordContributeToFeast(Hero hero, float foodToContribute)
        {
            if (hero.PartyBelongedTo?.ItemRoster == null) return;

            float count = 0f;
            var roster = hero.PartyBelongedTo.ItemRoster;

            for (int i = roster.Count - 1; i >= 0; i--)
            {
                if (count >= foodToContribute) break;

                var item = roster[i].EquipmentElement.Item;
                if (item?.IsFood != true) continue;

                int amountToTake = Math.Min(roster[i].Amount, (int) (foodToContribute - count));

                if (amountToTake > 0)
                {
                    FeastRoster.AddToCounts(roster[i].EquipmentElement, amountToTake);
                    roster.AddToCounts(roster[i].EquipmentElement, -amountToTake);
                    count += amountToTake;
                }
            }

            FoodAmount = count;
        }

        private void ContributePlayerFood()
        {
            if (Host != Hero.MainHero || Host.PartyBelongedTo?.ItemRoster == null) return;

            // Player contributes based on guest count and wealth
            float baseFood = Guests.Count * 3f; // 3 days worth minimum
            float wealthMultiplier = Math.Min(2f, Host.Gold / 100000f); // Up to 2x for wealthy
            float targetFood = Math.Max(baseFood, baseFood * wealthMultiplier);

            var roster = Host.PartyBelongedTo.ItemRoster;
            float contributed = 0f;

            // Take food from player's inventory
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                if (contributed >= targetFood) break;

                var item = roster[i].EquipmentElement.Item;
                if (item?.IsFood != true) continue;

                // Don't take ALL food - leave some for the party
                int maxToTake = Math.Max(0, roster[i].Amount - 2); // Keep at least 2 of each food type
                int amountToTake = Math.Min(maxToTake, (int) (targetFood - contributed));

                if (amountToTake > 0)
                {
                    FeastRoster.AddToCounts(roster[i].EquipmentElement, amountToTake);
                    roster.AddToCounts(roster[i].EquipmentElement, -amountToTake);
                    contributed += amountToTake;
                }
            }

            FoodAmount = contributed;

            // Show food contribution message
            InformationManager.DisplayMessage(new InformationMessage(
                $"You contribute {FoodAmount:F0} food from your party to the feast.", Colors.Yellow));
        }

        // RESTORED: Critical method for managing feast inventory
        public void RecalculateFoodFromRoster()
        {
            FoodAmount = 0f;
            if (FeastRoster != null)
            {
                for (int i = 0; i < FeastRoster.Count; i++)
                {
                    var item = FeastRoster[i];
                    if (item.EquipmentElement.Item?.IsFood == true)
                    {
                        FoodAmount += item.Amount;
                    }
                }
            }
        }

        // RESTORED: Show updated feast quality after player contributes food
        public void ShowUpdatedFeastQuality()
        {
            if (Host != Hero.MainHero) return;

            float foodPerGuest = Guests.Count > 0 ? FoodAmount / Guests.Count : 0f;

            if (foodPerGuest >= 8f)
                InformationManager.DisplayMessage(new InformationMessage("Your feast is now magnificent!", Colors.Cyan));
            else if (foodPerGuest >= 6f)
                InformationManager.DisplayMessage(new InformationMessage("Your feast is now luxurious.", Colors.Green));
            else if (foodPerGuest >= 4f)
                InformationManager.DisplayMessage(new InformationMessage("Your feast is fine quality.", Colors.Yellow));
            else if (foodPerGuest >= 3f)
                InformationManager.DisplayMessage(new InformationMessage("Your feast is modest quality.", Colors.White));
            else if (foodPerGuest < 3f)
                InformationManager.DisplayMessage(new InformationMessage("Your feast needs more food for better quality.", Colors.Red));
        }

        private void Initialize()
        {
            SendInvitations();
            CreateTournament();
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
            if (Host != Hero.MainHero) return; // Only show for player-hosted feasts

            float foodPerGuest = Guests.Count > 0 ? FoodAmount / Guests.Count : 0f;

            string quality;
            Color messageColor;

            if (foodPerGuest >= 8f)
            {
                quality = "magnificent";
                messageColor = Colors.Cyan;
            }
            else if (foodPerGuest >= 6f)
            {
                quality = "luxurious";
                messageColor = Colors.Green;
            }
            else if (foodPerGuest >= 4f)
            {
                quality = "fine";
                messageColor = Colors.Yellow;
            }
            else if (foodPerGuest >= 3f)
            {
                quality = "modest";
                messageColor = Colors.White;
            }
            else
            {
                quality = "meager";
                messageColor = Colors.Red;
            }

            // Show wealth tier indicator
            string wealthIndicator = Host.Gold > 100000f ? " (befitting your wealth)" : "";

            InformationManager.DisplayMessage(new InformationMessage(
                $"You prepare a {quality} feast with {FoodAmount:F0} food for {Guests.Count} guests{wealthIndicator}.",
                messageColor));

            // Additional advice for poor quality feasts
            if (foodPerGuest < 3f)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Consider using 'Manage feast inventory' to add more food and improve feast quality.",
                    Colors.Gray));
            }
            else if (foodPerGuest >= 6f)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Your generous provisions will surely impress the guests!",
                    Colors.Green));
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

                var departureReason = ShouldGuestLeave(guest);
                if (departureReason != null)
                {
                    departing.Add(guest);

                    // ENHANCED: Show departure messages for player-hosted feasts
                    if (Host == Hero.MainHero)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{guest.Name} departs the feast: {departureReason}", Colors.Yellow));
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

        // IMPROVED: More robust food consumption system
        private void ConsumeFood()
        {
            int attendingGuests = Guests.Count(g => g.CurrentSettlement == FeastSettlement);

            if (attendingGuests > 0)
            {
                // Calculate consumption
                float targetConsumption = attendingGuests; // 1 food per attending guest per day
                float actualConsumption = Math.Min(FoodAmount, targetConsumption);

                if (actualConsumption > 0)
                {
                    // Remove items from feast roster in order
                    float remainingToRemove = actualConsumption;

                    for (int i = FeastRoster.Count - 1; i >= 0 && remainingToRemove > 0; i--)
                    {
                        try
                        {
                            var item = FeastRoster[i];
                            if (item.EquipmentElement.Item?.IsFood == true)
                            {
                                int toRemove = Math.Min(item.Amount, (int) Math.Ceiling(remainingToRemove));

                                if (toRemove > 0)
                                {
                                    FeastRoster.AddToCounts(item.EquipmentElement, -toRemove);
                                    remainingToRemove -= toRemove;
                                }
                            }
                        }
                        catch
                        {
                            // Handle roster issues gracefully
                            break;
                        }
                    }

                    // Update total food amount
                    FoodAmount = Math.Max(0, FoodAmount - actualConsumption);
                }

                // Player warnings for their own feast
                if (Host == Hero.MainHero)
                {
                    if (FoodAmount <= 0)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Your feast has run out of food! Use 'Manage feast inventory' to add more or the feast will end.", Colors.Red));
                    }
                    else if (FoodAmount < attendingGuests * 2)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Food running low! {FoodAmount:F0} remaining. Use 'Manage feast inventory' to add more.", Colors.Yellow));
                    }
                }
            }
        }

        private void ApplyDailyEffects()
        {
            // AI follows same 2-day cooldown rules as player
            if (Host != Hero.MainHero)
            {
                var attendingGuests = Guests.Where(g => g != Host && g != Hero.MainHero && g.CurrentSettlement == FeastSettlement).ToList();

                foreach (var guest in attendingGuests)
                {
                    // Check if this guest can have a meaningful conversation with host today
                    if (FeastBehavior.Instance.CanAITalkForRelation(guest, Host, this))
                    {
                        // Apply relation bonus (same as player system)
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(guest, Host, 1, false);

                        // Record the conversation
                        FeastBehavior.Instance.RecordAIConversation(guest, Host);

                        // Optional: Show message for player-hosted feasts
                        if (Host == Hero.MainHero)
                        {
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{guest.Name} and {Host.Name} have a meaningful conversation at the feast. (+1 Relation)", Colors.Green));
                        }
                    }

                    // AI guests also talk to each other (with same cooldown)
                    foreach (var otherGuest in attendingGuests.Where(og => og != guest))
                    {
                        if (FeastBehavior.Instance.CanAITalkForRelation(guest, otherGuest, this))
                        {
                            // 50% chance for AI-to-AI conversations per day
                            if (MBRandom.RandomFloat < 0.5f)
                            {
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(guest, otherGuest, 1, false);
                                FeastBehavior.Instance.RecordAIConversation(guest, otherGuest);

                                // Optional: Show message for player-hosted feasts
                                if (Host == Hero.MainHero && MBRandom.RandomFloat < 0.3f) // Only show some messages to avoid spam
                                {
                                    InformationManager.DisplayMessage(new InformationMessage(
                                        $"{guest.Name} and {otherGuest.Name} bond over shared interests. (+1 Relation)", Colors.Blue));
                                }
                            }
                        }
                    }
                }
            }
        }

        private bool ShouldEndFeast()
        {
            // CRITICAL: Never end feast on the same day it was created
            if (CurrentDay == 0) return false;

            // CRITICAL: Minimum feast duration - never end before day 2
            if (CurrentDay < 2) return false;

            // End if completely out of food
            if (FoodAmount <= 0) return true;

            // IMPROVEMENT: Adaptive duration based on success
            var adaptiveDuration = CalculateOptimalDuration();

            if (CurrentDay >= adaptiveDuration) return true;

            if (Host != Hero.MainHero)
            {
                var endScore = GetEndingScore();
                var threshold = GetDynamicEndThreshold();

                // CRITICAL: Add minimum duration penalty to prevent early endings
                if (CurrentDay <= 3)
                {
                    threshold += 50f; // Much harder to end early
                }

                return endScore >= threshold;
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
                score -= 30f; // INCREASED penalty for ending early (was -20f)

            // CRITICAL: Extra penalty for very early endings
            if (CurrentDay <= 1)
                score -= 50f; // Heavy penalty for ending on day 0-1

            // Guest retention
            if (InitialGuests.Count > 0)
            {
                float retention = (float) Guests.Count / InitialGuests.Count;
                if (retention < 0.3f) score += 40f;
                else if (retention < 0.5f) score += 25f;
                else if (retention > 0.8f && CurrentDay <= 5) score -= 15f;
            }

            // Resource pressure (but reduced for early days)
            if (FoodAmount <= 0)
                score += 100f;
            else if (FoodAmount < Guests.Count * 2)
            {
                // Reduce food pressure early in feast
                float foodPressure = CurrentDay <= 2 ? 15f : 30f;
                score += foodPressure;
            }

            // Financial pressure (reduced early)
            if (Host.Gold < 5000)
            {
                float financialPressure = CurrentDay <= 2 ? 25f : 50f;
                score += financialPressure;
            }
            else if (Host.Gold < 10000)
            {
                float financialPressure = CurrentDay <= 2 ? 12f : 25f;
                score += financialPressure;
            }

            // War pressure (significantly reduced early in feast)
            var enemies = FactionManager.GetEnemyKingdoms(Kingdom);
            if (enemies.Any())
            {
                float warPressure = enemies.Count() * (CurrentDay <= 2 ? 5f : 15f);
                score += warPressure;

                if (Host.Clan.Settlements.Any(s => s.IsUnderSiege))
                {
                    // Even siege pressure is reduced early - host needs time to enjoy feast
                    float siegePressure = CurrentDay <= 2 ? 30f : 75f;
                    score += siegePressure;
                }
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