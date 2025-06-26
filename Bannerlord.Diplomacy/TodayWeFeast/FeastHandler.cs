using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.SaveSystem;

namespace TodayWeFeast
{
    internal class FeastBehavior : CampaignBehaviorBase
    {
        private readonly FeastHostingScoringModel _feastScoringModel = new FeastHostingScoringModel();

        [SaveableField(1)]
        public List<FeastObject> Feasts;

        private SoundEvent _ambienceLoop;
        private SoundEvent _tavernTrack;
        private SoundEvent _musicianTrack;

        private List<Hero> _talkedToLordsToday;

        [SaveableField(2)]
        public Dictionary<Kingdom, double> timeSinceLastFeast;

        public static readonly FeastBehavior Instance = new FeastBehavior();

        public FeastBehavior()
        {
            this.Feasts = new List<FeastObject>();
            this.timeSinceLastFeast = new Dictionary<Kingdom, double>();
            this._talkedToLordsToday = new List<Hero>();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, new Action<MobileParty, Settlement, Hero>(OnAfterSettlementEntered));
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.OnGameEarlyLoadedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(OnGameLoaded));
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(OnGameLoaded));
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(MenuItems));
            CampaignEvents.LocationCharactersAreReadyToSpawnEvent.AddNonSerializedListener(this, new Action<Dictionary<string, int>>(OnLocationCharactersAreReadyToSpawn));
            CampaignEvents.OnMissionStartedEvent.AddNonSerializedListener(this, new Action<IMission>(OnMissionStarted));
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, new Action<IMission>(OnMissionEnded));
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, new Action<IEnumerable<CharacterObject>>(OnConversationEnded));
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("Feasts", ref Feasts);
            dataStore.SyncData("timeSinceLastFeast", ref timeSinceLastFeast);
            dataStore.SyncData("talkedToLordsToday", ref _talkedToLordsToday);
        }

        private void OnConversationEnded(IEnumerable<CharacterObject> characters)
        {
            CharacterObject character = characters.FirstOrDefault(c => c != CharacterObject.PlayerCharacter);

            if (CampaignMission.Current?.Location == null || character?.HeroObject == null) return;

            Location location = CampaignMission.Current.Location;
            Settlement settlement = PlayerEncounter.LocationEncounter?.Settlement;
            Hero talkedToHero = character.HeroObject;

            if (location.StringId.Equals("lordshall", StringComparison.OrdinalIgnoreCase) && settlement != null)
            {
                FeastObject currentFeast = Feasts.FirstOrDefault(f => f.feastSettlement == settlement);

                if (currentFeast != null && currentFeast.hostOfFeast == Hero.MainHero && talkedToHero != Hero.MainHero && !_talkedToLordsToday.Contains(talkedToHero))
                {
                    ChangeRelationAction.ApplyPlayerRelation(talkedToHero, 2, true, true);
                    _talkedToLordsToday.Add(talkedToHero);
                    InformationManager.DisplayMessage(new InformationMessage($"You spent some time with {talkedToHero.Name}. (+2 Relation)", Colors.Green));
                }
                else if (currentFeast != null && currentFeast.hostOfFeast != Hero.MainHero && talkedToHero == currentFeast.hostOfFeast && !_talkedToLordsToday.Contains(talkedToHero))
                {
                    ChangeRelationAction.ApplyPlayerRelation(talkedToHero, 2, true, true);
                    Hero.MainHero.Clan.AddRenown(1, true);
                    _talkedToLordsToday.Add(talkedToHero);
                    InformationManager.DisplayMessage(new InformationMessage($"You share pleasantries with the host, {talkedToHero.Name}. (+2 Relation, +1 Renown)", Colors.Green));
                }
            }
        }

        private void OnMissionStarted(IMission mission)
        {
            if (CampaignMission.Current?.Location != null)
            {
                Location location = CampaignMission.Current.Location;
                Settlement settlement = PlayerEncounter.LocationEncounter?.Settlement;

                if (location.StringId.Equals("lordshall", StringComparison.OrdinalIgnoreCase) && settlement != null)
                {
                    FeastObject currentFeast = Feasts.FirstOrDefault(f => f.feastSettlement == settlement);
                    if (currentFeast != null)
                    {
                        StopFeastAmbientSound();

                        if (Mission.Current?.Scene != null)
                        {
                            int ambienceId = SoundEvent.GetEventIdFromString("event:/mission/ambient/area/interior/tavern");
                            if (ambienceId != -1) { _ambienceLoop = SoundEvent.CreateEvent(ambienceId, Mission.Current.Scene); _ambienceLoop.Play(); }

                            int tavernTrackId = SoundEvent.GetEventIdFromString("event:/mission/ambient/detail/tavern_track_01");
                            if (tavernTrackId != -1) { _tavernTrack = SoundEvent.CreateEvent(tavernTrackId, Mission.Current.Scene); _tavernTrack.Play(); }

                            List<string> musicianTracks = GetMusicianTracksByCulture(currentFeast.kingdom.Culture);

                            string randomTrack = musicianTracks[MBRandom.RandomInt(musicianTracks.Count)];
                            int musicianTrackId = SoundEvent.GetEventIdFromString(randomTrack);
                            if (musicianTrackId != -1) { _musicianTrack = SoundEvent.CreateEvent(musicianTrackId, Mission.Current.Scene); _musicianTrack.Play(); }
                        }
                    }
                }
            }
        }

        private List<string> GetMusicianTracksByCulture(CultureObject culture)
        {
            var defaultTracks = new List<string> { "event:/music/musicians/vlandia/01" };
            if (culture == null) return defaultTracks;
            switch (culture.StringId.ToLower())
            {
                case "aserai": return new List<string> { "event:/music/musicians/aserai/01", "event:/music/musicians/aserai/02", "event:/music/musicians/aserai/03", "event:/music/musicians/aserai/04" };
                case "battania": return new List<string> { "event:/music/musicians/battania/01", "event:/music/musicians/battania/02", "event:/music/musicians/battania/03", "event:/music/musicians/battania/04" };
                case "empire": return new List<string> { "event:/music/musicians/empire/01", "event:/music/musicians/empire/02", "event:/music/musicians/empire/03", "event:/music/musicians/empire/04" };
                case "khuzait": return new List<string> { "event:/music/musicians/khuzait/01", "event:/music/musicians/khuzait/02", "event:/music/musicians/khuzait/03", "event:/music/musicians/khuzait/04" };
                case "sturgia": return new List<string> { "event:/music/musicians/sturgia/01", "event:/music/musicians/sturgia/02", "event:/music/musicians/sturgia/03", "event:/music/musicians/sturgia/04" };
                case "vlandia": return new List<string> { "event:/music/musicians/vlandia/01", "event:/music/musicians/vlandia/02", "event:/music/musicians/vlandia/03", "event:/music/musicians/vlandia/04" };
                default: return defaultTracks;
            }
        }

        private void OnMissionEnded(IMission mission) => StopFeastAmbientSound();
        private void StopFeastAmbientSound()
        {
            if (_ambienceLoop != null && _ambienceLoop.IsValid) _ambienceLoop.Stop();
            if (_tavernTrack != null && _tavernTrack.IsValid) _tavernTrack.Stop();
            if (_musicianTrack != null && _musicianTrack.IsValid) _musicianTrack.Stop();
        }

        private void OnLocationCharactersAreReadyToSpawn(Dictionary<string, int> unusedUsablePointCount)
        {
            Location location = CampaignMission.Current.Location;
            if (location == null || PlayerEncounter.LocationEncounter.Settlement == null) return;
            Settlement settlement = PlayerEncounter.LocationEncounter.Settlement;
            if (location.StringId.Equals("lordshall", StringComparison.OrdinalIgnoreCase))
            {
                if (Feasts.Any(f => f.feastSettlement == settlement) && unusedUsablePointCount.TryGetValue("npc_common", out int servantCount) && servantCount > 0)
                {
                    SpawnServants(location, settlement, Math.Min(servantCount, 2));
                }
            }
        }

        private void SpawnServants(Location location, Settlement settlement, int count)
        {
            location.AddLocationCharacters(
                (CultureObject culture, LocationCharacter.CharacterRelations relation) =>
                {
                    CharacterObject servant = culture.Townswoman;
                    Monster monsterWithSuffix = TaleWorlds.Core.FaceGen.GetMonsterWithSuffix(servant.Race, "_settlement");
                    Campaign.Current.Models.AgeModel.GetAgeLimitForLocation(servant, out int minValue, out int maxValue, "");
                    AgentData agentData = new AgentData(new SimpleAgentOrigin(servant, -1, null, default(UniqueTroopDescriptor))).Monster(monsterWithSuffix).Age(MBRandom.RandomInt(minValue, maxValue));
                    return new LocationCharacter(agentData, new LocationCharacter.AddBehaviorsDelegate(SandBoxManager.Instance.AgentBehaviorManager.AddWandererBehaviors), "npc_common", true, relation, ActionSetCode.GenerateActionSetNameWithSuffix(agentData.AgentMonster, agentData.AgentIsFemale, "_villager"), true, false, null, false, false, true);
                },
                settlement.Culture, LocationCharacter.CharacterRelations.Neutral, count);
        }

        private void OnAfterSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            foreach (FeastObject feast in Feasts)
            {
                if (feast.lordsInFeast.Contains(hero) && settlement == feast.feastSettlement && hero.PartyBelongedTo != null && hero != Hero.MainHero)
                {
                    feast.hostOfFeast.Clan.Influence += 5;
                    if (feast.hostOfFeast == Hero.MainHero) InformationManager.DisplayMessage(new InformationMessage($"{hero.Name} has arrived at your feast! You have gained 5 influence."));
                }
            }
        }

        private void OnGameLoaded(CampaignGameStarter gameStarterObject)
        {
            if (Feasts == null) Feasts = new List<FeastObject>();
            if (timeSinceLastFeast == null) timeSinceLastFeast = new Dictionary<Kingdom, double>();
            if (_talkedToLordsToday == null) _talkedToLordsToday = new List<Hero>();
        }

        private void OnWarDeclared(IFaction faction1, IFaction faction2, DeclareWarAction.DeclareWarDetail detail)
        {
            foreach (FeastObject feast in Feasts.ToList())
            {
                if (feast.kingdom == faction1 || feast.kingdom == faction2) feast.endFeast();
            }
        }

        private void OnDailyTick()
        {
            _talkedToLordsToday.Clear();
            foreach (FeastObject f in Feasts.ToList()) f.dailyFeastTick();

            foreach (var kingdom in Kingdom.All.Where(k => !k.IsEliminated))
            {
                bool isAtWar = FactionManager.GetEnemyKingdoms(kingdom).Any();
                if (isAtWar || feastIsPresent(kingdom) || !canHaveFeast(kingdom)) continue;

                Hero bestHost = null;
                float highestScore = 0;

                foreach (var clan in kingdom.Clans.Where(c => !c.IsUnderMercenaryService && c.Fiefs.Any() && c.Leader != null))
                {
                    var host = clan.Leader;
                    if (host != Hero.MainHero && host.Spouse != null && host.Gold > 20000 && host.PartyBelongedTo != null && host.PartyBelongedTo.IsActive)
                    {
                        var score = _feastScoringModel.GetFeastHostingScore(host).ResultNumber;
                        if (score > highestScore)
                        {
                            highestScore = score;
                            bestHost = host;
                        }
                    }
                }

                if (bestHost != null && highestScore > 65f)
                {
                    float foodContribution = calculateFoodContribution(bestHost);
                    if (foodContribution > 20f)
                    {
                        Feasts.Add(createFeast(bestHost, bestHost.HomeSettlement, kingdom, getAllClanMembersInKingdomWhoWantToJoin(kingdom, bestHost), foodContribution));
                        InformationManager.DisplayMessage(new InformationMessage($"{bestHost.Name} is hosting a feast! The lords of the realm are gathering at {bestHost.HomeSettlement.Name} to celebrate!"));
                    }
                }
            }
        }

        public float calculateFoodContribution(Hero hero) => hero.PartyBelongedTo?.Food * 0.8f ?? 0f;
        public bool canHaveFeast(Kingdom kingdom)
        {
            if (timeSinceLastFeast.TryGetValue(kingdom, out double lastFeast) && (lastFeast + 6) >= CampaignTime.Now.ToDays) return false;
            timeSinceLastFeast.Remove(kingdom);
            return true;
        }
        public FeastObject createFeast(Hero host, Settlement s, Kingdom k, List<Hero> lords, float food)
        {
            host.ChangeHeroGold(-5000);
            return new FeastObject(s, k, lords, host, food);
        }
        public bool feastIsPresent(Kingdom kingdom) => Feasts.Any(f => f.kingdom == kingdom);

        public List<Hero> getAllClanMembersInKingdomWhoWantToJoin(Kingdom kingdom, Hero feastHost)
        {
            List<Hero> partyHeros = new List<Hero>();
            foreach (Clan clan in kingdom.Clans)
            {
                foreach (Hero hero in clan.Lords)
                {
                    if (hero == Hero.MainHero || (hero.IsPartyLeader && !clan.IsMinorFaction && FeastInviteHandler.checkIfLordWantsToJoin(feastHost, hero)))
                    {
                        partyHeros.Add(hero);
                    }
                }
            }
            return partyHeros;
        }

        public void MenuItems(CampaignGameStarter campaignGameSystemStarter)
        {
            campaignGameSystemStarter.AddGameMenuOption("town_keep", "host_a_feast_town", "Host a feast (Cost: 5000 Gold)", game_menu_host_feast_on_condition, game_menu_host_feast_consequence, false, 4, false);
            campaignGameSystemStarter.AddGameMenuOption("town_keep", "manage_feast_inventory_town", "Manage feast inventory", game_menu_manage_feast_on_condition, game_menu_manage_feast_consequence, false, 5, false);
            campaignGameSystemStarter.AddGameMenuOption("town_keep", "end_feast_town", "End the feast", game_menu_end_feast_on_condition, game_menu_end_feast_consequence, false, 6, false);
            campaignGameSystemStarter.AddGameMenuOption("castle", "host_a_feast_castle", "Host a feast (Cost: 5000 Gold)", game_menu_host_feast_on_condition, game_menu_host_feast_consequence, false, 4, false);
            campaignGameSystemStarter.AddGameMenuOption("castle", "manage_feast_inventory_castle", "Manage feast inventory", game_menu_manage_feast_on_condition, game_menu_manage_feast_consequence, false, 5, false);
            campaignGameSystemStarter.AddGameMenuOption("castle", "end_feast_castle", "End the feast", game_menu_end_feast_on_condition, game_menu_end_feast_consequence, false, 6, false);
        }
        private bool game_menu_host_feast_on_condition(MenuCallbackArgs args) { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return !Feasts.Any(f => f.kingdom == Hero.MainHero.MapFaction); }
        private bool game_menu_manage_feast_on_condition(MenuCallbackArgs args) { args.optionLeaveType = GameMenuOption.LeaveType.Manage; return Feasts.Any(f => f.feastSettlement == Settlement.CurrentSettlement && f.hostOfFeast == Hero.MainHero); }
        private bool game_menu_end_feast_on_condition(MenuCallbackArgs args) { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return Feasts.Any(f => f.feastSettlement == Settlement.CurrentSettlement && f.hostOfFeast == Hero.MainHero); }
        private void game_menu_end_feast_consequence(MenuCallbackArgs args)
        {
            FeastObject feastToEnd = Feasts.FirstOrDefault(f => f.feastSettlement == Settlement.CurrentSettlement && f.hostOfFeast == Hero.MainHero);
            if (feastToEnd != null) feastToEnd.endFeast();
            GameMenu.SwitchToMenu(args.MenuContext.GameMenu.StringId);
        }
        private void game_menu_host_feast_consequence(MenuCallbackArgs args)
        {
            if (Hero.MainHero.Clan.Kingdom.Stances.Any(s => s.IsAtWar)) InformationManager.DisplayMessage(new InformationMessage("You cannot start a feast while at war."));
            else if (!canHaveFeast(Hero.MainHero.Clan.Kingdom)) InformationManager.DisplayMessage(new InformationMessage("You must wait " + Convert.ToInt32((timeSinceLastFeast[Hero.MainHero.Clan.Kingdom] + 6) - CampaignTime.Now.ToDays) + " days before your kingdom can host another feast."));
            else if (Hero.MainHero.CurrentSettlement.Owner != Hero.MainHero) InformationManager.DisplayMessage(new InformationMessage("You may only host feasts at fiefs that you own."));
            else if (Hero.MainHero.Spouse == null) InformationManager.DisplayMessage(new InformationMessage("You have no partner. You must be married to host a feast."));
            else if (Hero.MainHero.Gold < 5000) InformationManager.DisplayMessage(new InformationMessage("You do not have at least 5000 Gold."));
            else if (!feastIsPresent(Hero.MainHero.Clan.Kingdom)) Feasts.Add(createFeast(Hero.MainHero, Hero.MainHero.CurrentSettlement, Hero.MainHero.Clan.Kingdom, getAllClanMembersInKingdomWhoWantToJoin(Hero.MainHero.Clan.Kingdom, Hero.MainHero), -1));
            else InformationManager.DisplayMessage(new InformationMessage("A feast is already present for your kingdom."));
        }
        private void game_menu_manage_feast_consequence(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Manage;
            ItemRoster feastRoster = Feasts.FirstOrDefault(feast => feast.hostOfFeast == Hero.MainHero && Settlement.CurrentSettlement == feast.feastSettlement)?.feastRoster;
            if (feastRoster != null) InventoryManager.OpenScreenAsStash(feastRoster);
            else InformationManager.DisplayMessage(new InformationMessage("You are not hosting a feast here."));
        }
    }
}