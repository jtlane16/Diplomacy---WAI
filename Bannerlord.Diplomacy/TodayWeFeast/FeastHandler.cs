using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace TodayWeFeast
{
	internal class FeastBehavior : CampaignBehaviorBase
	{
		// Token: 0x0600010C RID: 268 RVA: 0x0000669C File Offset: 0x0000489C
		public override void RegisterEvents()
		{
			this.Feasts = new List<FeastObject>();
			CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, new Action(this.OnDailyTick));
			CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, new Action<MobileParty, Settlement, Hero>(this.OnAfterSettlementEntered));
			CampaignEvents.WarDeclared.AddNonSerializedListener(this, new Action<IFaction, IFaction>(this.OnWarDeclared));
			CampaignEvents.OnGameEarlyLoadedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.OnGameLoaded));
			CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.OnGameLoaded));
			CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.MenuItems));
		}

		private void OnAfterSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
		{
			foreach (FeastObject feast in this.Feasts)
			{
				if (feast.lordsInFeast.Contains(hero) && settlement == feast.feastSettlement && hero.PartyBelongedTo != null && hero != Hero.MainHero)
				{
					feast.hostOfFeast.Clan.Influence = feast.hostOfFeast.Clan.Influence + 5;
					hero.PartyBelongedTo.Ai.SetDoNotMakeNewDecisions(true);
					if (feast.hostOfFeast == Hero.MainHero)
					{
						InformationManager.DisplayMessage(new InformationMessage(hero.Name.ToString() + " has arrived at your feast! You have gained 5 influence."));
					}
				}
			}
		}

		private void OnGameLoaded(CampaignGameStarter gameStarterObject)
		{
			if (this.Feasts?.Any() != true)
			{
				this.Feasts = new List<FeastObject>();
			}

			try
            {
				var checkList = timeSinceLastFeast.Count;
			} catch
            {
				this.timeSinceLastFeast = new Dictionary<Kingdom, double>();
			}
		}

		private void OnWarDeclared(IFaction faction1, IFaction faction2)
		{
			foreach (FeastObject feast in Feasts.ToList())
			{
				if (feast.kingdom == faction1)
				{
					feast.endFeast();
				}
				if (feast.kingdom == faction2)
				{
					feast.endFeast();
				}
			}
		}

		private void OnDailyTick()
		{
			foreach (FeastObject f in Feasts.ToList())
			{
				InformationManager.DisplayMessage(new InformationMessage((f.kingdom.Name.Contains("Empire") ? "The " + f.kingdom.Name.ToString() : f.kingdom.Name.ToString()) + " is currently hosting a feast at " + f.feastSettlement.ToString()));
				this.addRelation(f);
				f.dailyFeastTick();
			}

			Hero hostOfFeast = null;
			Settlement feastSettlement = null;
			Kingdom kingdom = null;
			List<Hero> lordsToInvite = new List<Hero>();

			foreach (Kingdom k in getKingdomsAtPeace())
			{
				if (this.feastIsPresent(k) || !canHaveFeast(k))
				{
					continue;
				}
				var clanLeaders = this.getAllClanLeadersInKingdom(k);
				foreach (Hero partyLeader in clanLeaders)
				{
					int randomGen = MBRandom.RandomInt(0, 100);
					float foodContribution = calculateFoodContribution(partyLeader);
					if (partyLeader != Hero.MainHero && randomGen > 98 && partyLeader.Spouse != null && partyLeader.Gold > 20000 && partyLeader.Clan.Fiefs.Count > 0 && !this.feastIsPresent(k) && foodContribution > 20f)
					{
						hostOfFeast = partyLeader;
						feastSettlement = hostOfFeast.HomeSettlement;
						kingdom = hostOfFeast.Clan.Kingdom;
						var currentFeast = this.createFeast(hostOfFeast, feastSettlement, kingdom, getAllClanMembersInKingdomWhoWantToJoin(kingdom, hostOfFeast), foodContribution);
						this.Feasts.Add(currentFeast);
						InformationManager.DisplayMessage(new InformationMessage(currentFeast.hostOfFeast.Name.ToString() + " is hosting a feast! The lords of the realm are gathering at " + currentFeast.feastSettlement + " to celebrate!"));
					}
				}
			}
		}

		public float calculateFoodContribution(Hero hero)
		{
			if (hero.PartyBelongedTo != null)
            {
				//get 80% of current food total
				var result = ((float)hero.PartyBelongedTo.Food / 100) * 80;
				return result;
				//return MBRandom.RandomFloatRanged(0f, result);
			} else
            {
				return 0f;
            }
		}

		public void addRelation(FeastObject feast)
		{
			foreach (Hero lord in feast.lordsInFeast)
			{
				if (lord != feast.hostOfFeast && (feast.hostOfFeast.CurrentSettlement == feast.feastSettlement && lord.CurrentSettlement == feast.feastSettlement))
				{
					if (lord == Hero.MainHero || feast.hostOfFeast == Hero.MainHero)
					{
						ChangeRelationAction.ApplyRelationChangeBetweenHeroes(feast.hostOfFeast, lord, 1, true);
					} else
					{
						ChangeRelationAction.ApplyRelationChangeBetweenHeroes(feast.hostOfFeast, lord, 1, false);
					}
				}
			}
		}

		public bool canHaveFeast(Kingdom kingdom)
        {
			if (this.timeSinceLastFeast.ContainsKey(kingdom))
            {
				if ((this.timeSinceLastFeast[kingdom] + 6) < CampaignTime.Now.ToDays)
                {
					this.timeSinceLastFeast.Remove(kingdom);
					return true;
                } else
                {
					return false;
				}
            } else
            {
				return true;
            }
        }

		public FeastObject createFeast(Hero hostOfFeast, Settlement feastSettlement, Kingdom kingdom, List<Hero> lordsInFeast, float foodContribution)
		{
			hostOfFeast.ChangeHeroGold(-5000);
			return new FeastObject(feastSettlement, kingdom, lordsInFeast, hostOfFeast, foodContribution);
		}

		public bool feastIsPresent(Kingdom kingdom)
		{
			bool hasFeast = false;
			foreach (FeastObject f in Feasts)
			{
				if (f.kingdom == kingdom)
				{
					hasFeast = true;
					this.makeSureAllLordsTargetFeast(f);
				}
			}
			return hasFeast;
		}

		public void makeSureAllLordsTargetFeast(FeastObject f)
		{
			foreach (Hero lord in f.lordsInFeast)
			{
				//The lord's party is starving, set them free for the time being.
				if (lord.PartyBelongedTo != null && lord.PartyBelongedTo.Food == 0)
				{
					lord.PartyBelongedTo.Ai.SetDoNotMakeNewDecisions(false);
				}
				//if they are not at the feast, send the lord there.
				else if (lord.PartyBelongedTo != null && lord.CurrentSettlement != f.feastSettlement && lord != Hero.MainHero)
				{
					lord.PartyBelongedTo.Ai.SetMoveGoToSettlement(f.feastSettlement);
					lord.PartyBelongedTo.Ai.SetDoNotMakeNewDecisions(true);
				}
				else if (lord.PartyBelongedTo != null && lord.CurrentSettlement == f.feastSettlement && lord != Hero.MainHero)
				{
					lord.PartyBelongedTo.Ai.SetDoNotMakeNewDecisions(true);
				}
			}
		}

		public List<Kingdom> getKingdomsAtPeace()
		{
			List<Kingdom> KingdomsAtPeace = new List<Kingdom>();
			foreach (Kingdom kingdom in Campaign.Current.Kingdoms)
			{
				bool isAtWar = false;
				foreach (Kingdom kingdomToCheck in Campaign.Current.Kingdoms)
				{
					if (kingdom.IsAtWarWith(kingdomToCheck))
					{
						isAtWar = true;
					}
				}
				if (!isAtWar)
				{
					KingdomsAtPeace.Add(kingdom);
				}
			}
			return KingdomsAtPeace;
		}

		public List<Hero> getAllClanMembersInKingdomWhoWantToJoin(Kingdom kingdom, Hero feastHost)
		{
			List<Hero> partyHeros = new List<Hero>();
			foreach (Clan clan in kingdom.Clans)
			{
				foreach (Hero hero in clan.Lords)
				{
					if (hero == Hero.MainHero)
                    {
						partyHeros.Add(hero);
					}
					else if (hero.IsPartyLeader && !clan.IsMinorFaction && FeastInviteHandler.checkIfLordWantsToJoin(feastHost, hero))
					{
						partyHeros.Add(hero);
					}
				}
			}
			return partyHeros;
		}

		public List<Hero> getAllClanLeadersInKingdom(Kingdom kingdom)
		{
			List<Hero> partyHeros = new List<Hero>();
			foreach (Clan clan in kingdom.Clans)
			{
				if (clan.Leader != Hero.MainHero)
                {
					partyHeros.Add(clan.Leader);
                }
			}
			return partyHeros;
		}

		public void MenuItems(CampaignGameStarter campaignGameSystemStarter)
		{
			campaignGameSystemStarter.AddGameMenuOption("town_keep", "host_a_feast_town", "Host a feast (Cost: 5000 Gold)", new GameMenuOption.OnConditionDelegate(this.game_any_on_condition), new GameMenuOption.OnConsequenceDelegate(this.game_menu_host_feast_consequence), false, 4, false);
			campaignGameSystemStarter.AddGameMenuOption("town_keep", "manage_feast_inventory_town", "Manage feast inventory", new GameMenuOption.OnConditionDelegate(this.game_any_on_condition), new GameMenuOption.OnConsequenceDelegate(this.game_menu_manage_feast_consequence), false, 5, false);
			campaignGameSystemStarter.AddGameMenuOption("castle", "host_a_feast_castle", "Host a feast (Cost: 5000 Gold)", new GameMenuOption.OnConditionDelegate(this.game_any_on_condition), new GameMenuOption.OnConsequenceDelegate(this.game_menu_host_feast_consequence), false, 4, false);
			campaignGameSystemStarter.AddGameMenuOption("castle", "manage_feast_inventory_castle", "Manage feast inventory", new GameMenuOption.OnConditionDelegate(this.game_any_on_condition), new GameMenuOption.OnConsequenceDelegate(this.game_menu_manage_feast_consequence), false, 5, false);
		}

		private bool game_any_on_condition(MenuCallbackArgs args)
        {
			args.optionLeaveType = GameMenuOption.LeaveType.Manage;
			return true;
		}

		private void game_menu_host_feast_consequence(MenuCallbackArgs args)
		{
			if (!this.getKingdomsAtPeace().Contains(Hero.MainHero.Clan.Kingdom))
			{
				InformationManager.DisplayMessage(new InformationMessage("You cannot start a feast while at war."));
			}
			else if (!canHaveFeast(Hero.MainHero.Clan.Kingdom))
			{
				InformationManager.DisplayMessage(new InformationMessage("You must wait " + Convert.ToInt32((this.timeSinceLastFeast[Hero.MainHero.Clan.Kingdom] + 6) - CampaignTime.Now.ToDays) + " days before your kingdom can host another feast."));
			}
			else if (Hero.MainHero.CurrentSettlement.Owner != Hero.MainHero)
			{
				InformationManager.DisplayMessage(new InformationMessage("You may only host feasts at fiefs that you own."));
			}
			else if (Hero.MainHero.Spouse == null)
			{
				InformationManager.DisplayMessage(new InformationMessage("You have no partner. You must be married to host a feast."));
			}
			else if (Hero.MainHero.Gold < 5000)
			{
				InformationManager.DisplayMessage(new InformationMessage("You do not have at least 5000 Gold."));
			}
			else if (!this.feastIsPresent(Hero.MainHero.Clan.Kingdom))
			{
				this.Feasts.Add(this.createFeast(Hero.MainHero, Hero.MainHero.CurrentSettlement, Hero.MainHero.Clan.Kingdom, this.getAllClanMembersInKingdomWhoWantToJoin(Hero.MainHero.Clan.Kingdom, Hero.MainHero), -1));
			}
			else
			{
				InformationManager.DisplayMessage(new InformationMessage("A feast is already present for your kingdom."));
			}
		}

		private void game_menu_manage_feast_consequence(MenuCallbackArgs args)
		{
			args.optionLeaveType = GameMenuOption.LeaveType.Manage;
			ItemRoster feastRoster = null;
			foreach (FeastObject feast in this.Feasts)
			{
				if (feast.hostOfFeast == Hero.MainHero && Settlement.CurrentSettlement.Owner == Hero.MainHero)
                {
					feastRoster = feast.feastRoster;
					break;
                }
			}
			if (feastRoster != null)
			{
				InventoryManager.OpenScreenAsStash(feastRoster);
			} else
            {
				InformationManager.DisplayMessage(new InformationMessage("You are not hosting a feast here."));
			}
		}

		private static void Finalizer(Exception __exception)
		{
			bool flag = __exception != null;
			if (flag)
			{
                InformationManager.DisplayMessage(new InformationMessage("e."));
            }
		}

		public override void SyncData(IDataStore dataStore)
		{
			dataStore.SyncData("Feasts", ref this.Feasts);
		}

		public static FeastBehavior Instance = new FeastBehavior();

		[SaveableField(1)]
		public List<FeastObject> Feasts;

		[SaveableField(2)]
		public Dictionary<Kingdom, double> timeSinceLastFeast;

	}
}

