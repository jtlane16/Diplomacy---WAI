using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace TodayWeFeast
{
	internal class FeastObject
    {
		public FeastObject(Settlement feastSettlementPar, Kingdom kingdomPar, List<Hero> lordsInFeastPar, Hero hostOfFeast, float foodContribution)
        {
			this.feastSettlement = feastSettlementPar;
			this.kingdom = kingdomPar;
			this.lordsInFeast = lordsInFeastPar;
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
			foreach (Hero lord in this.lordsInFeast)
            {
				if (lord == Hero.MainHero && this.hostOfFeast == Hero.MainHero)
				{
					InformationManager.ShowInquiry(new InquiryData("Today, We Feast!", "You have decided to host a feast, where the lords of the realm wll gather at " + this.feastSettlement.Name.ToString() + " to celebrate with you!", true, false, "OK", null, null, null), true);
				}
				else if (lord == Hero.MainHero && this.hostOfFeast == Hero.MainHero) {
					InformationManager.ShowInquiry(new InquiryData("Feast Invite", "You have recieved an invitation to a feast hosted by " + this.hostOfFeast.Name + " at his home, " + this.feastSettlement.Name.ToString(), true, false, "OK", null, null, null), true);
				}
				else if (lord.PartyBelongedTo != null && lord.CurrentSettlement != this.feastSettlement)
				{
					lord.PartyBelongedTo.Ai.SetMoveGoToSettlement(this.feastSettlement);
					lord.PartyBelongedTo.Ai.SetDoNotMakeNewDecisions(true);
				}
				else if (lord.PartyBelongedTo != null && lord.CurrentSettlement == feastSettlement)
				{
					lord.PartyBelongedTo.Ai.SetDoNotMakeNewDecisions(true);
				}
            }
        }

		public void endFeast()
		{
			foreach(Hero hero in this.lordsInFeast)
            {
				if (hero.PartyBelongedTo != null && hero != Hero.MainHero)
				{
					hero.PartyBelongedTo.Ai.SetDoNotMakeNewDecisions(false);
				}
            }
			//End the tournament at the feast if there is one
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
			FeastBehavior.Instance.timeSinceLastFeast.Add(this.kingdom, CampaignTime.Now.ToDays);
			InformationManager.DisplayMessage(new InformationMessage("The feast at " + this.feastSettlement.Name.ToString() + " has ended."));
		}

		public void dailyFeastTick()
        {
			if (this.amountOfFood <= 0 || (CampaignTime.Now.ToDays > this.endFeastDay.ToDays))
            {
				this.endFeast();
				return;
            }
			else
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
							this.feastRoster.AddToCounts(this.feastRoster[1].EquipmentElement, -1);
							this.amountOfFood--;
						} catch {
							this.feastRoster.AddToCounts(this.feastRoster[0].EquipmentElement, -1);
							this.amountOfFood--;
						}

						
					}
				}
			}

        }

		public void makeAILordContributeToTheFeast(Hero hero, float foodToContribute)
        {
			//Copy the original roster
			float count = 0f;
			for (int i = 0; i < hero.PartyBelongedTo.ItemRoster.Count; i++)
            {
				if (count > foodToContribute)
				{
					break;
				} else if (!hero.PartyBelongedTo.ItemRoster[i].EquipmentElement.Item.IsFood)
                {
					continue;
                } else
                {
					var tempItemCount = 0;
					while (tempItemCount < hero.PartyBelongedTo.ItemRoster[i].Amount && count < foodToContribute)
                    {
						tempItemCount++;
						this.feastRoster.AddToCounts(hero.PartyBelongedTo.ItemRoster[i].EquipmentElement, 1);
						hero.PartyBelongedTo.ItemRoster.AddToCounts(hero.PartyBelongedTo.ItemRoster[i].EquipmentElement, -1);
						count++;
                    }

				}

            }
			this.amountOfFood = count;
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

	}

}

