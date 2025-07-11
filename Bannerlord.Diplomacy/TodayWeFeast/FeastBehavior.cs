using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
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
    public class FeastBehavior : CampaignBehaviorBase
    {
        [SaveableField(1)] public List<FeastObject> Feasts;
        [SaveableField(2)] public Dictionary<Kingdom, double> timeSinceLastFeast;
        [SaveableField(3)] public Dictionary<Hero, CampaignTime> _lastTalkedToLords;

        private SoundEvent _ambienceLoop, _tavernTrack, _musicianTrack;
        public static FeastBehavior Instance { get; private set; }

        public FeastBehavior()
        {
            Instance = this;
            Feasts = new List<FeastObject>();
            timeSinceLastFeast = new Dictionary<Kingdom, double>();
            _lastTalkedToLords = new Dictionary<Hero, CampaignTime>();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.OnGameEarlyLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.LocationCharactersAreReadyToSpawnEvent.AddNonSerializedListener(this, OnLocationCharactersReady);
            CampaignEvents.OnMissionStartedEvent.AddNonSerializedListener(this, OnMissionStarted);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnMissionEnded);
            CampaignEvents.OnAgentJoinedConversationEvent.AddNonSerializedListener(this, OnConversationStarted);

            // CRITICAL: Hook into AI hourly tick to add feast attendance behavior
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, OnAiHourlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("Feasts", ref Feasts);
            dataStore.SyncData("timeSinceLastFeast", ref timeSinceLastFeast);
            dataStore.SyncData("lastTalkedToLords", ref _lastTalkedToLords);
        }

        #region Event Handlers
        private void OnDailyTick()
        {
            // Process existing feasts
            foreach (var feast in Feasts.ToList())
                feast.DailyTick();

            // Check for new feast creation
            foreach (var kingdom in Kingdom.All.Where(k => !k.IsEliminated && CanCreateFeast(k)))
            {
                var host = SelectBestHost(kingdom);
                if (host != null && ShouldHostFeast(host))
                {
                    CreateFeast(host, kingdom);
                }
            }
        }

        private void OnAiHourlyTick(MobileParty mobileParty, PartyThinkParams thinkParams)
        {
            // Only process lord parties
            if (mobileParty?.LeaderHero == null || !mobileParty.IsLordParty) return;

            var hero = mobileParty.LeaderHero;

            // Check if this hero should be attending a feast
            var targetFeast = GetFeastForHero(hero);
            if (targetFeast != null)
            {
                // Calculate feast attendance score
                var feastScore = CalculateFeastAttendanceScore(hero, targetFeast);

                if (feastScore > 0f)
                {
                    // Add high-priority GoToSettlement behavior for the feast
                    var feastBehavior = new AIBehaviorTuple(targetFeast.FeastSettlement, AiBehavior.GoToSettlement, false);
                    var behaviorScore = new ValueTuple<AIBehaviorTuple, float>(feastBehavior, feastScore);
                    thinkParams.AddBehaviorScore(behaviorScore);
                }
            }
        }

        private FeastObject GetFeastForHero(Hero hero)
        {
            // Find any active feast this hero should attend
            return Feasts.FirstOrDefault(f => f.Guests.Contains(hero) || f.Host == hero);
        }

        private float CalculateFeastAttendanceScore(Hero hero, FeastObject feast)
        {
            // Host gets maximum priority - must attend their own feast
            if (feast.Host == hero)
            {
                return hero.CurrentSettlement == feast.FeastSettlement ? 0f : 2500f; // Only move if not already there
            }

            // Skip player - they can decide for themselves
            if (hero == Hero.MainHero) return 0f;

            // Check if guest should attend
            if (!feast.Guests.Contains(hero)) return 0f;

            // Already at feast? No need to travel
            if (hero.CurrentSettlement == feast.FeastSettlement) return 0f;

            // Calculate distance
            var distance = hero.PartyBelongedTo?.Position2D.Distance(feast.FeastSettlement.Position2D) ?? 1000f;

            // Too far away? Don't bother
            if (distance > 150f) return 0f; // About 5 days travel

            // Base priority for feast attendance
            float basePriority = 1000f;

            // Relationship with host affects priority
            var relation = hero.GetRelation(feast.Host);
            basePriority += relation * 10f; // -300 to +1000 range

            // Reduce priority based on distance
            var distancePenalty = distance * 3f;
            basePriority -= distancePenalty;

            // Personality affects attendance desire
            basePriority += hero.GetTraitLevel(DefaultTraits.Honor) * 50f;
            basePriority += hero.GetTraitLevel(DefaultTraits.Generosity) * 30f;

            // Early days of feast have higher priority
            if (feast.CurrentDay <= 2)
                basePriority += 200f;
            else if (feast.CurrentDay > 5)
                basePriority -= 100f * (feast.CurrentDay - 5);

            // Urgent conditions override feast attendance
            if (hero.PartyBelongedTo?.Food < hero.PartyBelongedTo?.Party.NumberOfAllMembers)
                return 0f; // Food shortage

            if (hero.Clan.Settlements.Any(s => s.IsUnderSiege))
                return 0f; // Settlements under siege

            // Ensure minimum threshold
            return Math.Max(0f, basePriority);
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            var feast = Feasts.FirstOrDefault(f => f.FeastSettlement == settlement && f.Guests.Contains(hero));
            if (feast != null && feast.Host != Hero.MainHero && hero != Hero.MainHero)
            {
                feast.Host.Clan.Influence += 5;
                if (feast.Host == Hero.MainHero)
                    InformationManager.DisplayMessage(new InformationMessage($"{hero.Name} arrived at your feast! (+5 Influence)", Colors.Green));
            }
        }

        private void OnWarDeclared(IFaction faction1, IFaction faction2, DeclareWarAction.DeclareWarDetail detail)
        {
            foreach (var feast in Feasts.Where(f => f.Kingdom == faction1 || f.Kingdom == faction2).ToList())
                feast.EndFeast("War has been declared!");
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            Feasts ??= new List<FeastObject>();
            timeSinceLastFeast ??= new Dictionary<Kingdom, double>();
            _lastTalkedToLords ??= new Dictionary<Hero, CampaignTime>();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            AddMenuOptions(starter);
            AddDialogs(starter);
        }

        private void OnLocationCharactersReady(Dictionary<string, int> unusedPoints)
        {
            var location = CampaignMission.Current?.Location;
            var settlement = PlayerEncounter.LocationEncounter?.Settlement;

            if (location?.StringId == "lordshall" && settlement != null &&
                Feasts.Any(f => f.FeastSettlement == settlement) &&
                unusedPoints.TryGetValue("npc_common", out int count) && count > 0)
            {
                SpawnServants(location, settlement, Math.Min(count, 2));
            }
        }

        private void OnMissionStarted(IMission mission)
        {
            var location = CampaignMission.Current?.Location;
            var settlement = PlayerEncounter.LocationEncounter?.Settlement;

            if (location?.StringId == "lordshall" && settlement != null &&
                Feasts.Any(f => f.FeastSettlement == settlement))
            {
                PlayFeastAmbience();
            }
        }

        private void OnMissionEnded(IMission mission) => StopFeastAmbience();

        private void OnConversationStarted(IAgent agent)
        {
            if (agent.Character == null || agent.Character.IsPlayerCharacter) return;

            var characterObject = agent.Character as CharacterObject;
            if (characterObject?.HeroObject == null) return;

            var hero = characterObject.HeroObject;
            var settlement = PlayerEncounter.LocationEncounter?.Settlement;
            var location = CampaignMission.Current?.Location;

            if (location?.StringId == "lordshall" && settlement != null)
            {
                var feast = Feasts.FirstOrDefault(f => f.FeastSettlement == settlement);
                if (feast != null && CanTalkForRelation(hero) && feast.Guests.Contains(hero) &&
                    hero != feast.Host && hero != Hero.MainHero && feast.Host != Hero.MainHero)
                {
                    ApplyConversationBonus(hero, 2);
                }
            }
        }
        #endregion

        #region Feast Logic
        private bool CanCreateFeast(Kingdom kingdom)
        {
            if (FactionManager.GetEnemyKingdoms(kingdom).Any()) return false;
            if (Feasts.Any(f => f.Kingdom == kingdom)) return false;

            if (timeSinceLastFeast.TryGetValue(kingdom, out double lastFeast))
                return (CampaignTime.Now.ToDays - lastFeast) >= 15;

            return true;
        }

        private Hero SelectBestHost(Kingdom kingdom)
        {
            return kingdom.Clans
                .Where(c => !c.IsUnderMercenaryService && c.Fiefs.Any() && c.Leader != null)
                .Select(c => c.Leader)
                .Where(h => h != Hero.MainHero && h.Spouse != null && h.Gold > 20000 && h.PartyBelongedTo?.IsActive == true)
                .OrderByDescending(h => GetHostingScore(h))
                .FirstOrDefault();
        }

        private float GetHostingScore(Hero hero)
        {
            float score = 30f; // Base score

            // Traits
            score += hero.GetTraitLevel(DefaultTraits.Generosity) * 15f;
            score += hero.GetTraitLevel(DefaultTraits.Honor) * 10f;
            score -= hero.GetTraitLevel(DefaultTraits.Calculating) * 8f;

            // Wealth
            if (hero.Gold > 100000) score += 25f;
            else if (hero.Gold > 50000) score += 15f;
            else if (hero.Gold < 30000) score -= 15f;

            // Status
            if (hero == hero.Clan.Kingdom.Leader) score += 20f;
            else if (hero.Clan.Tier >= 4) score += 10f;

            // Settlement
            if (hero.HomeSettlement?.IsTown == true) score += 15f;
            else if (hero.HomeSettlement?.IsCastle == true) score += 8f;

            var season = CampaignTime.Now.GetSeasonOfYear;
            if (season == CampaignTime.Seasons.Spring || season == CampaignTime.Seasons.Summer)
                score -= 20f; // Busy season

            // Settlements need attention?
            var troubledSettlements = hero.Clan.Settlements.Count(s =>
                s.Town?.Loyalty < 50f || s.Town?.Security < 50f || s.Town?.Prosperity < 500f);
            score -= troubledSettlements * 10f;

            // Army nearby? Military opportunity
            if (hero.PartyBelongedTo?.Army != null)
                score -= 25f; // Should stay with army

            // Trading opportunities?
            if (hero.Gold > 50000f && season != CampaignTime.Seasons.Winter)
                score -= 10f; // Could be making money

            return score;
        }

        private bool ShouldHostFeast(Hero host)
        {
            var baseScore = GetHostingScore(host);
            if (baseScore < 60f) return false; // Lower base threshold

            // IMPROVEMENT: Context bonuses
            var contextBonus = GetContextualBonus(host);

            return (baseScore + contextBonus) > 75f;
        }

        private float GetContextualBonus(Hero host)
        {
            float bonus = 0f;

            // Just ended a war? Celebration time!
            if (!FactionManager.GetEnemyKingdoms(host.Clan.Kingdom).Any())
            {
                // Check if recently at war (would need tracking, simplified here)
                bonus += 30f; // Victory celebration
            }

            // Low kingdom morale? Time to boost spirits
            var avgRelation = host.Clan.Kingdom.Lords.Average(l => l.GetRelation(host.Clan.Kingdom.Leader));
            if (avgRelation < 10f) bonus += 25f;

            // Recent marriage/birth in family?
            if (host.Children.Any(c => c.Age < 1f)) bonus += 20f; // New baby
            if (host.Spouse != null && host.GetRelation(host.Spouse) > 80f) bonus += 15f; // Happy marriage

            // Economic prosperity
            if (host.Gold > 200000f) bonus += 15f; // Show off wealth

            // Rival hosting recently? Competition!
            var recentRivalFeast = FeastBehavior.Instance.timeSinceLastFeast
                .Where(kvp => kvp.Key != host.Clan.Kingdom && (CampaignTime.Now.ToDays - kvp.Value) < 30)
                .Any();
            if (recentRivalFeast) bonus += 20f;

            return bonus;
        }

        private void CreateFeast(Hero host, Kingdom kingdom)
        {
            var guests = GetFeastGuests(kingdom, host);
            if (guests.Count > 2) // Need at least a few guests
            {
                host.ChangeHeroGold(-5000);
                var feast = new FeastObject(host, kingdom, guests);
                Feasts.Add(feast);

                timeSinceLastFeast[kingdom] = CampaignTime.Now.ToDays;

                string message = host == Hero.MainHero
                    ? $"You host a feast at {host.HomeSettlement.Name}!"
                    : $"{host.Name} hosts a feast at {host.HomeSettlement.Name}!";
                InformationManager.DisplayMessage(new InformationMessage(message, Colors.Green));
            }
        }

        private List<Hero> GetFeastGuests(Kingdom kingdom, Hero host)
        {
            var potentialGuests = kingdom.Clans
                .Where(c => !c.IsMinorFaction)
                .Select(c => c.Leader)
                .Where(h => h != null && h != Hero.MainHero && h.IsPartyLeader)
                .ToList();

            // IMPROVEMENT: Prioritize by strategic value, not just random acceptance
            var prioritizedGuests = potentialGuests
                .Where(g => ShouldInviteHero(host, g))
                .OrderByDescending(g => GetGuestValue(host, g))
                .Take(Math.Min(12, potentialGuests.Count)) // Cap guest list
                .ToList();

            if (Hero.MainHero.MapFaction == kingdom)
                prioritizedGuests.Insert(0, Hero.MainHero); // Player always first priority

            return prioritizedGuests;
        }

        private float GetGuestValue(Hero host, Hero guest)
        {
            float value = 0f;

            // Clan power (want important guests)
            value += guest.Clan.Tier * 10f;
            value += guest.Clan.Settlements.Count * 5f;

            // Relationship building potential
            var currentRelation = host.GetRelation(guest);
            if (currentRelation < 0) value += 20f; // Prioritize fixing bad relations
            if (currentRelation < 20) value += 10f; // Room for improvement

            // Strategic marriages/alliances
            if (guest.Spouse?.Clan != null && guest.Spouse.Clan != host.Clan)
                value += 15f; // Connect to other clans

            return value;
        }

        private bool ShouldInviteHero(Hero host, Hero guest)
        {
            var relation = host.GetRelation(guest);
            if (relation <= -30) return false;
            if (relation >= 50) return true;

            // IMPROVEMENT: Consider travel distance and current location
            var distance = guest.PartyBelongedTo?.Position2D.Distance(host.HomeSettlement.Position2D) ?? 0f;
            var travelDays = distance / 30f; // Rough travel time

            // Closer guests more likely to accept
            float distancePenalty = Math.Min(30f, travelDays * 5f);

            // Currently traveling? Less likely to divert
            bool isTraveling = guest.PartyBelongedTo?.DefaultBehavior != AiBehavior.Hold;
            if (isTraveling) distancePenalty += 15f;

            // Adjust probability based on practical factors
            float probability = ((relation + 29f) / 78f) * 100f - distancePenalty;

            // VIPs always invited regardless of distance
            if (guest == guest.Clan.Kingdom.Leader || guest.Clan.Tier >= 5)
                probability += 20f;

            return MBRandom.RandomInt(0, 100) <= probability;
        }

        public bool CanTalkForRelation(Hero hero)
        {
            if (!_lastTalkedToLords.TryGetValue(hero, out var lastTalk)) return true;
            return (CampaignTime.Now.ToDays - lastTalk.ToDays) >= 3;
        }

        private void ApplyConversationBonus(Hero hero, int relationBonus)
        {
            ChangeRelationAction.ApplyPlayerRelation(hero, relationBonus, true, true);
            Hero.MainHero.Clan.AddRenown(1, true);
            _lastTalkedToLords[hero] = CampaignTime.Now;
            InformationManager.DisplayMessage(new InformationMessage($"Pleasant conversation with {hero.Name}! (+{relationBonus} Relation, +1 Renown)", Colors.Green));
        }
        #endregion

        #region Audio
        private void PlayFeastAmbience()
        {
            StopFeastAmbience();

            if (Mission.Current?.Scene != null)
            {
                var scene = Mission.Current.Scene;

                var ambienceId = SoundEvent.GetEventIdFromString("event:/mission/ambient/area/interior/tavern");
                if (ambienceId != -1)
                {
                    _ambienceLoop = SoundEvent.CreateEvent(ambienceId, scene);
                    _ambienceLoop?.Play();
                }

                var tavernId = SoundEvent.GetEventIdFromString("event:/mission/ambient/detail/tavern_track_01");
                if (tavernId != -1)
                {
                    _tavernTrack = SoundEvent.CreateEvent(tavernId, scene);
                    _tavernTrack?.Play();
                }

                var musicId = SoundEvent.GetEventIdFromString("event:/music/musicians/vlandia/01");
                if (musicId != -1)
                {
                    _musicianTrack = SoundEvent.CreateEvent(musicId, scene);
                    _musicianTrack?.Play();
                }
            }
        }

        private void StopFeastAmbience()
        {
            _ambienceLoop?.Stop();
            _tavernTrack?.Stop();
            _musicianTrack?.Stop();
        }
        #endregion

        #region UI
        private void AddMenuOptions(CampaignGameStarter starter)
        {
            // Town menus
            starter.AddGameMenuOption("town_keep", "host_feast", "Host a feast (5000 Gold)",
                CanHostFeast, HostFeast, false, 4);
            starter.AddGameMenuOption("town_keep", "manage_feast", "Manage feast inventory",
                CanManageFeast, ManageFeast, false, 5);
            starter.AddGameMenuOption("town_keep", "end_feast", "End the feast",
                CanEndFeast, EndFeast, false, 6);

            // Castle menus (same options)
            starter.AddGameMenuOption("castle", "host_feast", "Host a feast (5000 Gold)",
                CanHostFeast, HostFeast, false, 4);
            starter.AddGameMenuOption("castle", "manage_feast", "Manage feast inventory",
                CanManageFeast, ManageFeast, false, 5);
            starter.AddGameMenuOption("castle", "end_feast", "End the feast",
                CanEndFeast, EndFeast, false, 6);
        }

        private bool CanHostFeast(MenuCallbackArgs args) =>
            Hero.MainHero.MapFaction is Kingdom &&
            Hero.MainHero.CurrentSettlement?.Owner == Hero.MainHero &&
            !Feasts.Any(f => f.Kingdom == Hero.MainHero.MapFaction);

        private bool CanManageFeast(MenuCallbackArgs args) =>
            Feasts.Any(f => f.FeastSettlement == Settlement.CurrentSettlement && f.Host == Hero.MainHero);

        private bool CanEndFeast(MenuCallbackArgs args) => CanManageFeast(args);

        private void HostFeast(MenuCallbackArgs args)
        {
            if (Hero.MainHero.Clan.Kingdom.Stances.Any(s => s.IsAtWar))
                InformationManager.DisplayMessage(new InformationMessage("Cannot host feast during war.", Colors.Red));
            else if (!CanCreateFeast(Hero.MainHero.Clan.Kingdom))
                InformationManager.DisplayMessage(new InformationMessage("Must wait 15 days between feasts.", Colors.Red));
            else if (Hero.MainHero.Spouse == null)
                InformationManager.DisplayMessage(new InformationMessage("Must be married to host a feast.", Colors.Red));
            else if (Hero.MainHero.Gold < 5000)
                InformationManager.DisplayMessage(new InformationMessage("Need 5000 gold to host a feast.", Colors.Red));
            else
                CreateFeast(Hero.MainHero, Hero.MainHero.Clan.Kingdom);
        }

        private void ManageFeast(MenuCallbackArgs args)
        {
            var feast = Feasts.FirstOrDefault(f => f.Host == Hero.MainHero && f.FeastSettlement == Settlement.CurrentSettlement);
            if (feast?.FeastRoster != null)
                InventoryManager.OpenScreenAsStash(feast.FeastRoster);
        }

        private void EndFeast(MenuCallbackArgs args)
        {
            var feast = Feasts.FirstOrDefault(f => f.FeastSettlement == Settlement.CurrentSettlement && f.Host == Hero.MainHero);
            feast?.EndFeast("Host ended the feast.");
        }

        private void AddDialogs(CampaignGameStarter starter)
        {
            starter.AddDialogLine("feast_host_greeting", "start", "feast_response",
                "{FEAST_HOST_MESSAGE}", IsPlayerTalkingToFeastHost, HandleHostConversation, 150);

            starter.AddDialogLine("feast_response", "feast_response", "close_window",
                "Thank you for your hospitality.", () => true, null, 150);

            starter.AddDialogLine("feast_guest_greeting", "start", "close_window",
                "{FEAST_GUEST_MESSAGE}", IsPlayerHostTalkingToGuest, HandleGuestConversation, 150);
        }

        private bool IsPlayerTalkingToFeastHost()
        {
            var settlement = PlayerEncounter.LocationEncounter?.Settlement;
            var feast = Feasts.FirstOrDefault(f => f.FeastSettlement == settlement);
            var hero = Hero.OneToOneConversationHero;

            return feast != null && hero == feast.Host && feast.Host != Hero.MainHero &&
                   feast.Guests.Contains(Hero.MainHero) && CanTalkForRelation(hero);
        }

        private bool IsPlayerHostTalkingToGuest()
        {
            var settlement = PlayerEncounter.LocationEncounter?.Settlement;
            var feast = Feasts.FirstOrDefault(f => f.FeastSettlement == settlement);
            var hero = Hero.OneToOneConversationHero;

            return feast != null && feast.Host == Hero.MainHero && hero != null &&
                   feast.Guests.Contains(hero) && CanTalkForRelation(hero);
        }

        private void HandleHostConversation()
        {
            var host = Hero.OneToOneConversationHero;
            var relation = host.GetRelation(Hero.MainHero);

            string message = relation >= 20
                ? $"Welcome, {Hero.MainHero.Name}! Your presence brings me great joy!"
                : relation >= 0
                    ? $"Welcome to my feast, {Hero.MainHero.Name}."
                    : $"I acknowledge your presence, {Hero.MainHero.Name}.";

            MBTextManager.SetTextVariable("FEAST_HOST_MESSAGE", message);
            ApplyConversationBonus(host, CanTalkForRelation(host) ? 2 : 0);
        }

        private void HandleGuestConversation()
        {
            var guest = Hero.OneToOneConversationHero;
            var relation = guest.GetRelation(Hero.MainHero);

            string message = relation >= 20
                ? $"What a magnificent feast, {Hero.MainHero.Name}!"
                : relation >= 0
                    ? $"Thank you for your hospitality, {Hero.MainHero.Name}."
                    : $"I appreciate your invitation, {Hero.MainHero.Name}.";

            MBTextManager.SetTextVariable("FEAST_GUEST_MESSAGE", message);
            ApplyConversationBonus(guest, CanTalkForRelation(guest) ? 2 : 0);
        }

        private void SpawnServants(Location location, Settlement settlement, int count)
        {
            location.AddLocationCharacters((culture, relation) =>
            {
                var servant = culture.Townswoman;
                var monster = TaleWorlds.Core.FaceGen.GetMonsterWithSuffix(servant.Race, "_settlement");
                Campaign.Current.Models.AgeModel.GetAgeLimitForLocation(servant, out int minAge, out int maxAge, "");
                var agentData = new AgentData(new PartyAgentOrigin(PartyBase.MainParty, servant, -1, default, false))
                    .Monster(monster).Age(MBRandom.RandomInt(minAge, maxAge));
                return new LocationCharacter(agentData,
                    SandBoxManager.Instance.AgentBehaviorManager.AddWandererBehaviors,
                    "npc_common", true, relation,
                    ActionSetCode.GenerateActionSetNameWithSuffix(agentData.AgentMonster, agentData.AgentIsFemale, "_villager"),
                    true);
            }, settlement.Culture, LocationCharacter.CharacterRelations.Neutral, count);
        }
        #endregion
    }
}