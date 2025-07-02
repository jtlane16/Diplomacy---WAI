using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
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
        public FeastObject(Settlement feastSettlementPar, Kingdom kingdomPar, List<Hero> lordsInFeastPar, Hero hostOfFeast, float foodContribution)
        {
            this.feastSettlement = feastSettlementPar;
            this.kingdom = kingdomPar;
            this.lordsInFeast = lordsInFeastPar;
            // Store the initial list of guests to compare against later.
            this.initialLordsInFeast = new List<Hero>(lordsInFeastPar);
            this.hostOfFeast = hostOfFeast;
            this.sendAllLordsToFeast();
            this.createTournamentIfApplicable(feastSettlementPar);
            if (foodContribution != -1)
            {
                this.makeAILordContributeToTheFeast(hostOfFeast, foodContribution);
            }
        }

        public void createTournamentIfApplicable(Settlement feastSettlement)
        {
            if (feastSettlement.IsTown)
            {
                Town town = feastSettlement.Town;
                ITournamentManager tournamentManager = Campaign.Current.TournamentManager;
                TournamentGame tournamentGame = tournamentManager.GetTournamentGame(town);
                if (tournamentGame == null)
                {
                    tournamentManager.AddTournament(Campaign.Current.Models.TournamentModel.CreateTournament(town));
                }
            }
        }

        public void sendAllLordsToFeast()
        {
            if (this.hostOfFeast == Hero.MainHero)
            {
                InformationManager.ShowInquiry(new InquiryData("Today, We Feast!", $"You have decided to host a feast, where the lords of the realm will gather at {this.feastSettlement.Name} to celebrate with you!", true, false, "OK", null, null, null), true);
            }
            else if (this.lordsInFeast.Contains(Hero.MainHero))
            {
                InformationManager.ShowInquiry(new InquiryData("Feast Invite", $"You have received an invitation to a feast hosted by {this.hostOfFeast.Name} at his home, {this.feastSettlement.Name}", true, false, "OK", null, null, null), true);
            }

            if (this.hostOfFeast != Hero.MainHero && this.kingdom == Hero.MainHero.MapFaction)
            {
                TextObject message = new TextObject("{=feast_notification}A feast has begun at {SETTLEMENT_NAME}, hosted by {HOST_NAME}!");
                message.SetTextVariable("SETTLEMENT_NAME", this.feastSettlement.Name);
                message.SetTextVariable("HOST_NAME", this.hostOfFeast.Name);
                MBInformationManager.AddQuickInformation(message);
            }

            // NOTE: AI behavior is now handled through the behavior scoring system in AIMilitaryBehaviorPatches
            // No need to disable AI or force movement here - the scoring system will handle feast attendance
        }

        public void endFeast()
        {
            InformationManager.DisplayMessage(new InformationMessage($"The feast at {this.feastSettlement.Name} has ended.", Colors.Green));

            this.hostOfFeast.Clan.AddRenown(10, true);
            this.hostOfFeast.AddSkillXp(DefaultSkills.Steward, 500);

            // Create a comprehensive list of ALL heroes who might have been affected by this feast
            var allAffectedHeroes = new HashSet<Hero>();
            
            // Add all heroes from both lists (handles saves from before initialLordsInFeast was added)
            if (this.initialLordsInFeast != null)
            {
                foreach (var hero in this.initialLordsInFeast)
                {
                    allAffectedHeroes.Add(hero);
                }
            }
            
            if (this.lordsInFeast != null)
            {
                foreach (var hero in this.lordsInFeast)
                {
                    allAffectedHeroes.Add(hero);
                }
            }

            // Apply relation bonuses only to original guests (not the host)
            foreach (Hero hero in allAffectedHeroes)
            {
                if (hero != this.hostOfFeast && this.initialLordsInFeast?.Contains(hero) == true)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(this.hostOfFeast, hero, 1, true);
                }
            }

            if (this.feastSettlement.IsTown)
            {
                Town town = this.feastSettlement.Town;
                ITournamentManager tournamentManager = Campaign.Current.TournamentManager;
                TournamentGame tournamentGame = tournamentManager.GetTournamentGame(town);
                if (tournamentGame != null)
                {
                    tournamentManager.ResolveTournament(tournamentGame, town);
                }
            }
            
            FeastBehavior.Instance.Feasts.Remove(this);
            if (!FeastBehavior.Instance.timeSinceLastFeast.ContainsKey(this.kingdom))
            {
                FeastBehavior.Instance.timeSinceLastFeast.Add(this.kingdom, CampaignTime.Now.ToDays);
            }
        }

        public void dailyFeastTick()
        {
            currentDay++; // A feast gets older each day.

            // ENHANCED: Process feast attendance AI to ensure proper behavior scoring
            ProcessFeastAttendanceAI();

            // --- GUEST LEAVING LOGIC ---
            var feastAttendingScoringModel = new FeastAttendingScoringModel();
            var lordsToLeave = new List<Hero>();

            // Check if any guests want to leave using the improved scoring model
            if (this.lordsInFeast != null)
            {
                foreach (Hero guest in this.lordsInFeast.ToList())
                {
                    if (guest == this.hostOfFeast || guest == Hero.MainHero)
                        continue; // Skip host and player

                    // Use the enhanced scoring model to determine if they should leave
                    var attendanceScore = feastAttendingScoringModel.GetFeastAttendingScore(guest, this);

                    // If score is negative, they want to leave
                    if (attendanceScore.ResultNumber < -25f)
                    {
                        lordsToLeave.Add(guest);
                    }

                    // ADDITIONAL: Random chance to leave after day 5 regardless of score
                    if (this.currentDay > 5 && MBRandom.RandomFloat < 0.15f) // 15% chance per day after day 5
                    {
                        if (!lordsToLeave.Contains(guest))
                        {
                            lordsToLeave.Add(guest);
                        }
                    }
                }
            }

            // Remove the lords who decided to leave.
            foreach (var lord in lordsToLeave)
            {
                this.lordsInFeast.Remove(lord);
            }

            // --- HOST ENDING FEAST LOGIC ---
            // The AI host will check if it's time to end the feast.
            if (this.hostOfFeast != Hero.MainHero)
            {
                var feastEndingScoringModel = new FeastEndingScoringModel();
                var endingScore = feastEndingScoringModel.GetFeastEndingScore(this);

                // If the score to end the feast is high enough, the host ends it.
                if (endingScore.ResultNumber >= 100f)
                {
                    this.endFeast();
                    return; // Feast has ended, no more logic needed for this tick.
                }

                // Standard daily relation gain for guests that are still present.
                if (lordsInFeast != null)
                {
                    foreach (Hero guest in this.lordsInFeast)
                    {
                        if (guest != this.hostOfFeast && guest != Hero.MainHero)
                        {
                            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(guest, this.hostOfFeast, 1, false);
                        }
                    }
                }
            }

            // If the feast runs out of food or hits its maximum duration, it ends.
            if (this.amountOfFood <= 0 || (CampaignTime.Now.ToDays > this.endFeastDay.ToDays))
            {
                this.endFeast();
                return;
            }
            else
            {
                // Food consumption logic remains the same...
                if (lordsInFeast != null)
                {
                    foreach (Hero hero in this.lordsInFeast)
                    {
                        if (this.amountOfFood <= 0 && this.hostOfFeast == Hero.MainHero && CampaignTime.Now.ToDays > playerTimeBeforeEnd.ToDays)
                        {
                            this.endFeast();
                            return;
                        }
                        else if (this.amountOfFood <= 0 && this.hostOfFeast != Hero.MainHero)
                        {
                            this.endFeast();
                            return;
                        }
                        else if (hero.CurrentSettlement == this.feastSettlement)
                        {
                            try
                            {
                                if (this.feastRoster.Count > 1)
                                {
                                    this.feastRoster.AddToCounts(this.feastRoster[1].EquipmentElement, -1);
                                    this.amountOfFood--;
                                }
                                else if (this.feastRoster.Count > 0)
                                {
                                    this.feastRoster.AddToCounts(this.feastRoster[0].EquipmentElement, -1);
                                    this.amountOfFood--;
                                }
                            }
                            catch
                            {
                                // Could be empty, do nothing
                            }
                        }
                    }
                }
            }
        }

        public void makeAILordContributeToTheFeast(Hero hero, float foodToContribute)
        {
            if (hero.PartyBelongedTo == null || hero.PartyBelongedTo.ItemRoster == null) return;

            float count = 0f;
            ItemRoster roster = hero.PartyBelongedTo.ItemRoster;
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                if (count > foodToContribute) break;

                ItemObject item = roster[i].EquipmentElement.Item;
                if (item == null || !item.IsFood) continue;

                int amountToTake = Math.Min(roster[i].Amount, (int) (foodToContribute - count));

                if (amountToTake > 0)
                {
                    this.feastRoster.AddToCounts(roster[i].EquipmentElement, amountToTake);
                    roster.AddToCounts(roster[i].EquipmentElement, -amountToTake);
                    count += amountToTake;
                }
            }
            this.amountOfFood = count;
        }

        // ENHANCED: New method that works purely through AI scoring without disabling AI
        // ENHANCED: New method that works purely through AI scoring without disabling AI
        private void ProcessFeastAttendanceAI()
        {
            if (this.lordsInFeast == null) return;

            foreach (var lord in this.lordsInFeast.ToList())
            {
                if (lord == Hero.MainHero) continue; // Skip player

                var currentLocation = lord.CurrentSettlement?.Name?.ToString() ?? "traveling";
                var isAtFeast = lord.CurrentSettlement == this.feastSettlement;
                var isHost = lord == this.hostOfFeast;

                // The actual scoring boost is handled in AIMilitaryBehaviorPatches.cs
                // This method is mainly for logging and any additional feast logic
            }
        }

        [SaveableField(11)]
        public Settlement feastSettlement;
        [SaveableField(12)]
        public CampaignTime endFeastDay = CampaignTime.DaysFromNow(20f);
        [SaveableField(18)]
        public CampaignTime playerTimeBeforeEnd = CampaignTime.DaysFromNow(2f);
        [SaveableField(13)]
        public Kingdom kingdom;
        [SaveableField(14)]
        public List<Hero> lordsInFeast;
        [SaveableField(15)]
        public Hero hostOfFeast;
        [SaveableField(16)]
        public float amountOfFood;
        [SaveableField(17)]
        public ItemRoster feastRoster = new ItemRoster();
        [SaveableField(19)]
        public int currentDay = 0;
        [SaveableField(20)]
        public List<Hero> initialLordsInFeast;
    }
}
