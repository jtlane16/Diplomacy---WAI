using Diplomacy.Extensions;

using JetBrains.Annotations;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
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

using WarAndAiTweaks.AI;

namespace TodayWeFeast
{
    public class FeastBehavior : CampaignBehaviorBase // Changed from internal to public
    {
        private readonly FeastHostingScoringModel _feastScoringModel = new FeastHostingScoringModel();

        [SaveableField(1)]
        public List<FeastObject> Feasts;

        private SoundEvent _ambienceLoop;
        private SoundEvent _tavernTrack;
        private SoundEvent _musicianTrack;

        [SaveableField(3)]
        public List<Hero> _talkedToLordsToday;

        [SaveableField(4)] // Add new field for tracking 3-day cooldowns
        public Dictionary<Hero, CampaignTime> _lastTalkedToLords;

        [SaveableField(2)]
        public Dictionary<Kingdom, double> timeSinceLastFeast;

        public static FeastBehavior Instance { get; private set; } // Changed to property with private setter

        public FeastBehavior()
        {
            Instance = this; // Set the instance
            this.Feasts = new List<FeastObject>();
            this.timeSinceLastFeast = new Dictionary<Kingdom, double>();
            this._talkedToLordsToday = new List<Hero>();
            this._lastTalkedToLords = new Dictionary<Hero, CampaignTime>(); // Initialize new field

            // InformationManager.DisplayMessage(new InformationMessage("Feast system initialized!", Colors.Green));
        }

        public override void RegisterEvents()
        {
            // InformationManager.DisplayMessage(new InformationMessage("Registering feast events...", Colors.Blue));

            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, new Action<MobileParty, Settlement, Hero>(OnAfterSettlementEntered));
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.OnGameEarlyLoadedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(OnGameLoaded));
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(OnGameLoaded));
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(MenuItems));
            CampaignEvents.LocationCharactersAreReadyToSpawnEvent.AddNonSerializedListener(this, new Action<Dictionary<string, int>>(OnLocationCharactersAreReadyToSpawn));
            CampaignEvents.OnMissionStartedEvent.AddNonSerializedListener(this, new Action<IMission>(OnMissionStarted));
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, new Action<IMission>(OnMissionEnded));
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(RegisterFeastDialogs));

            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, new Action<IEnumerable<CharacterObject>>(OnConversationEnded));
            CampaignEvents.OnAgentJoinedConversationEvent.AddNonSerializedListener(this, new Action<IAgent>(OnAgentJoinedConversation));

            // InformationManager.DisplayMessage(new InformationMessage("Feast events registered successfully!", Colors.Green));
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("Feasts", ref Feasts);
            dataStore.SyncData("timeSinceLastFeast", ref timeSinceLastFeast);
            dataStore.SyncData("talkedToLordsToday", ref _talkedToLordsToday);
            dataStore.SyncData("lastTalkedToLords", ref _lastTalkedToLords); // Add new field to save data
        }

        private void RegisterFeastDialogs(CampaignGameStarter campaignGameStarter)
        {
            FeastConversations.AddFeastDialogs(campaignGameStarter);
            // InformationManager.DisplayMessage(new InformationMessage("Feast dialogues registered successfully!", Colors.Green));
        }

        private void OnAgentJoinedConversation(IAgent agent)
        {
            // Skip if it's not the player joining the conversation
            if (agent.Character == null || agent.Character.IsPlayerCharacter) return;

            // Get the character from the agent
            CharacterObject character = agent.Character as CharacterObject;
            if (character?.HeroObject == null) return;

            if (CampaignMission.Current?.Location == null) return;

            Location location = CampaignMission.Current.Location;
            Settlement settlement = PlayerEncounter.LocationEncounter?.Settlement;
            Hero talkedToHero = character.HeroObject;

            // InformationManager.DisplayMessage(new InformationMessage($"[FEAST] Starting conversation with {talkedToHero.Name} at {settlement?.Name?.ToString() ?? "null"}", Colors.Yellow));

            if (location.StringId.Equals("lordshall", StringComparison.OrdinalIgnoreCase) && settlement != null)
            {
                FeastObject currentFeast = Feasts.FirstOrDefault(f => f.feastSettlement == settlement);

                if (currentFeast != null)
                {
                    // InformationManager.DisplayMessage(new InformationMessage($"[FEAST] Found feast hosted by {currentFeast.hostOfFeast.Name}", Colors.Yellow));
                    // InformationManager.DisplayMessage(new InformationMessage($"[FEAST] Player invited? {currentFeast.lordsInFeast.Contains(Hero.MainHero)}", Colors.Yellow));
                    // InformationManager.DisplayMessage(new InformationMessage($"[FEAST] Talked already? {_talkedToLordsToday.Contains(talkedToHero)}", Colors.Yellow));
                    // InformationManager.DisplayMessage(new InformationMessage($"[FEAST] Matching host? {currentFeast.hostOfFeast == talkedToHero}", Colors.Yellow));
                }

                if (currentFeast != null && CanTalkToLordForRelation(talkedToHero))
                {
                    // ONLY handle cases that DON'T have proper dialog lines
                    // Let the dialog system handle host/guest conversations

                    // OTHER FEAST ATTENDEES: Regular feast conversations (not host, not when player is host)
                    if (currentFeast.lordsInFeast.Contains(talkedToHero) &&
                        talkedToHero != currentFeast.hostOfFeast &&
                        talkedToHero != Hero.MainHero &&
                        currentFeast.hostOfFeast != Hero.MainHero)
                    {
                        ChangeRelationAction.ApplyPlayerRelation(talkedToHero, 2, true, true); // Changed from 1 to 2
                        _lastTalkedToLords[talkedToHero] = CampaignTime.Now; // Update last talked time
                        InformationManager.DisplayMessage(new InformationMessage($"You share pleasant conversation with {talkedToHero.Name} at the feast. (+2 Relation)", Colors.Green));
                    }

                    // Note: Removed the host and guest conversation handling - let the dialog system handle those
                }
            }
        }

        // Add this public method so FeastConversations can access it
        public bool CanTalkToLordForRelation(Hero hero)
        {
            if (!_lastTalkedToLords.ContainsKey(hero))
            {
                return true; // Never talked to them before
            }

            var daysSinceLastTalk = CampaignTime.Now.ToDays - _lastTalkedToLords[hero].ToDays;
            return daysSinceLastTalk >= 3; // Can only get relation bonus every 3 days
        }

        private void OnConversationEnded(IEnumerable<CharacterObject> characters)
        {
            // This can be left empty now or used for any post-conversation cleanup
            // The feast interaction is handled in OnAgentJoinedConversation
        }

        private string GenerateHostMessage(Hero host)
        {
            var relation = host.GetRelation(Hero.MainHero);

            if (relation >= 20)
            {
                return $"{host.Name}: \"Ah, {Hero.MainHero.Name}! Welcome to my feast! Your presence brings me great joy. Come, let us celebrate together!\"";
            }
            else if (relation >= 0)
            {
                return $"{host.Name}: \"Welcome to my feast, {Hero.MainHero.Name}. I am pleased you could attend this celebration.\"";
            }
            else
            {
                return $"{host.Name}: \"I... acknowledge your presence at my feast, {Hero.MainHero.Name}. Perhaps this gathering can bring us closer.\"";
            }
        }

        private string GenerateGuestMessage(Hero guest)
        {
            var relation = guest.GetRelation(Hero.MainHero);

            if (relation >= 20)
            {
                return $"{guest.Name}: \"My dear {Hero.MainHero.Name}! What a magnificent feast you have prepared! Your generosity knows no bounds!\"";
            }
            else if (relation >= 0)
            {
                return $"{guest.Name}: \"Thank you for your hospitality, {Hero.MainHero.Name}. This is a well-organized celebration.\"";
            }
            else
            {
                return $"{guest.Name}: \"I... appreciate your invitation, {Hero.MainHero.Name}. Your hospitality is noted.\"";
            }
        }

        private static string GenerateReturnHostMessage(Hero host)
        {
            var relation = host.GetRelation(Hero.MainHero);

            if (relation >= 20)
            {
                return $"Ah, {Hero.MainHero.Name}! Thanks for coming back to chat! Your company makes this feast even more delightful.";
            }
            else if (relation >= 0)
            {
                return $"Welcome back, {Hero.MainHero.Name}. I'm glad you're enjoying the festivities enough to return!";
            }
            else
            {
                return $"You've returned, {Hero.MainHero.Name}. Perhaps we can continue to... build understanding between us.";
            }
        }

        private static string GenerateReturnGuestMessage(Hero guest)
        {
            var relation = guest.GetRelation(Hero.MainHero);

            if (relation >= 20)
            {
                return $"My dear {Hero.MainHero.Name}! I simply had to thank you again for this wonderful feast! Your hospitality knows no bounds!";
            }
            else if (relation >= 0)
            {
                return $"Thank you again, {Hero.MainHero.Name}. I wanted to express once more how much I'm enjoying your celebration.";
            }
            else
            {
                return $"I... wanted to speak with you again, {Hero.MainHero.Name}. This feast is helping me see you in a different light.";
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
            foreach (FeastObject feast in Feasts.ToList())
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
            // InformationManager.DisplayMessage(new InformationMessage("Feast system: Game loaded", Colors.Yellow));

            if (Feasts == null) Feasts = new List<FeastObject>();
            if (timeSinceLastFeast == null) timeSinceLastFeast = new Dictionary<Kingdom, double>();
            if (_talkedToLordsToday == null) _talkedToLordsToday = new List<Hero>();
            if (_lastTalkedToLords == null) _lastTalkedToLords = new Dictionary<Hero, CampaignTime>(); // Initialize if null

            // InformationManager.DisplayMessage(new InformationMessage($"Feast system: {Feasts.Count} active feasts loaded", Colors.Yellow));
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
            // int dayCounter = 0;
            // dayCounter++;

            // if (dayCounter % 10 == 0) // Log every 10 days
            // {
            //     InformationManager.DisplayMessage(new InformationMessage($"Feast system: Daily tick {dayCounter}, {Feasts.Count} active feasts", Colors.Cyan));
            // }

            _talkedToLordsToday.Clear();
            foreach (FeastObject f in Feasts.ToList()) 
            {
                f.dailyFeastTick();
                
                ProcessFeastAttendanceAI(f);
            }

            foreach (var kingdom in Kingdom.All.Where(k => !k.IsEliminated))
            {
                bool isAtWar = FactionManager.GetEnemyKingdoms(kingdom).Any();
                if (isAtWar || feastIsPresent(kingdom) || !canHaveFeast(kingdom)) continue;

                Hero bestHost = SelectBestFeastHost(kingdom);
                if (bestHost != null)
                {
                    // InformationManager.DisplayMessage(new InformationMessage($"Feast system: {bestHost.Name} considering hosting a feast", Colors.Cyan));

                    var hostingDecision = ShouldHostFeast(bestHost, kingdom);
                    if (hostingDecision.shouldHost)
                    {
                        float foodContribution = calculateFoodContribution(bestHost);
                        if (foodContribution > 20f)
                        {
                            Feasts.Add(createFeast(bestHost, bestHost.HomeSettlement, kingdom, getAllClanMembersInKingdomWhoWantToJoin(kingdom, bestHost), foodContribution));

                            string reasonText = hostingDecision.primaryReason;
                            InformationManager.DisplayMessage(new InformationMessage($"{bestHost.Name} is hosting a feast at {bestHost.HomeSettlement.Name} {reasonText}!"));
                        }
                        // else
                        // {
                        //     InformationManager.DisplayMessage(new InformationMessage($"Feast system: {bestHost.Name} wanted to host but insufficient food ({foodContribution:F1})", Colors.Red));
                        // }
                    }
                    // else
                    // {
                    //     InformationManager.DisplayMessage(new InformationMessage($"Feast system: {bestHost.Name} decided not to host (score too low)", Colors.Red));
                    // }
                }
            }
        }

        private void ProcessFeastAttendanceAI(FeastObject feast)
        {
            if (feast?.lordsInFeast == null) return;

            // SPECIAL HANDLING FOR HOST - Make absolutely sure the host stays put
            if (feast.hostOfFeast != Hero.MainHero && feast.hostOfFeast.PartyBelongedTo != null)
            {
                var hostParty = feast.hostOfFeast.PartyBelongedTo;

                // Always enforce host AI restrictions
                hostParty.Ai.SetDoNotMakeNewDecisions(true);

                if (feast.hostOfFeast.CurrentSettlement != feast.feastSettlement)
                {
                    // Force the host back to the feast
                    hostParty.Ai.SetMoveGoToSettlement(feast.feastSettlement);
                    // InformationManager.DisplayMessage(new InformationMessage($"[FEAST] Host {feast.hostOfFeast.Name} ordered to return to {feast.feastSettlement.Name}", Colors.Red));
                }
            }

            // Process guests as before
            foreach (var invitedLord in feast.lordsInFeast.ToList())
            {
                // SKIP the host - they should never leave their own feast through this logic
                if (invitedLord == Hero.MainHero ||
                    invitedLord.PartyBelongedTo == null ||
                    invitedLord == feast.hostOfFeast)
                    continue;

                var party = invitedLord.PartyBelongedTo;

                // Check if they should travel to the feast
                if (invitedLord.CurrentSettlement != feast.feastSettlement)
                {
                    var attendanceModel = new FeastAttendingScoringModel();
                    var attendanceScore = attendanceModel.GetFeastAttendingScore(invitedLord, feast);

                    if (attendanceScore.ResultNumber > 50f) // Threshold for traveling
                    {
                        // Use the existing API to set their destination
                        party.Ai.SetMoveGoToSettlement(feast.feastSettlement);
                        
                        // Log the decision
                        // AIComputationLogger.WriteLine($"{DateTime.UtcNow:o},FEAST_TRAVEL_INITIATED,{feast.kingdom.StringId},{invitedLord.Name},{feast.feastSettlement.Name},{attendanceScore.ResultNumber:F2}");
                    }
                    else if (attendanceScore.ResultNumber < -50f)
                    {
                        // They don't want to attend - remove them from the feast AND re-enable their AI
                        feast.lordsInFeast.Remove(invitedLord);
                        party.Ai.SetDoNotMakeNewDecisions(false); // IMPORTANT: Re-enable AI for declining lords
                        // InformationManager.DisplayMessage(new InformationMessage($"Re-enabled AI for declining lord {invitedLord.Name}", Colors.Orange));
                        // AIComputationLogger.WriteLine($"{DateTime.UtcNow:o},FEAST_DECLINED,{feast.kingdom.StringId},{invitedLord.Name},{attendanceScore.ResultNumber:F2}");
                    }
                }
                else
                {
                    // They're already at the feast - keep them there
                    party.Ai.SetDoNotMakeNewDecisions(true);
                }
            }

            // Remove the lords who decided to leave.
            foreach (var lord in feast.lordsInFeast.ToList())
            {
                if (lord.PartyBelongedTo != null)
                {
                    // IMPORTANT: Re-enable their AI so they can go about their business.
                    lord.PartyBelongedTo.Ai.SetDoNotMakeNewDecisions(false);
                    // InformationManager.DisplayMessage(new InformationMessage($"Re-enabled AI for departing lord {lord.Name}", Colors.Orange));
                }
                // TextObject message = new TextObject("{=leaving_feast_message}{LORD_NAME} has left the feast at {SETTLEMENT_NAME}.");
                // message.SetTextVariable("LORD_NAME", lord.Name);
                // message.SetTextVariable("SETTLEMENT_NAME", feast.feastSettlement.Name);
                // MBInformationManager.AddQuickInformation(message);
            }
        }

        private Hero SelectBestFeastHost(Kingdom kingdom)
        {
            Hero bestHost = null;
            float highestScore = 0;

            foreach (var clan in kingdom.Clans.Where(c => !c.IsUnderMercenaryService && c.Fiefs.Any() && c.Leader != null))
            {
                var host = clan.Leader;
                if (host != Hero.MainHero && host.Spouse != null && host.Gold > 20000 && host.PartyBelongedTo != null && host.PartyBelongedTo.IsActive)
                {
                    var score = _feastScoringModel.GetFeastHostingScore(host).ResultNumber;

                    score += GetSeasonalBonus();
                    score += GetDiplomaticBonus(host, kingdom);
                    score += GetStabilityBonus(kingdom);
                    score += GetCulturalBonus(host);

                    if (score > highestScore)
                    {
                        highestScore = score;
                        bestHost = host;
                    }
                }
            }

            return bestHost;
        }

        private (bool shouldHost, string primaryReason) ShouldHostFeast(Hero host, Kingdom kingdom)
        {
            var score = _feastScoringModel.GetFeastHostingScore(host);

            var reasons = new List<(float weight, string reason)>();

            if (HasRecentDiplomaticSuccess(kingdom))
            {
                score.Add(25f, new TextObject("Recent diplomatic success"));
                reasons.Add((25f, "to celebrate recent diplomatic success"));
            }

            if (IsKingdomProperous(kingdom))
            {
                score.Add(20f, new TextObject("Kingdom prosperity"));
                reasons.Add((20f, "to celebrate the kingdom's prosperity"));
            }

            if (HasLowMorale(kingdom))
            {
                score.Add(30f, new TextObject("Boost kingdom morale"));
                reasons.Add((30f, "to boost morale in the realm"));
            }

            if (HasRecentCelebratoryEvent(host))
            {
                score.Add(35f, new TextObject("Recent celebratory event"));
                reasons.Add((35f, "to celebrate recent joyous events"));
            }

            float seasonalBonus = GetSeasonalBonus();
            if (seasonalBonus > 0)
            {
                score.Add(seasonalBonus, new TextObject("Favorable season"));
                reasons.Add((seasonalBonus, "as the season is favorable for celebration"));
            }

            string primaryReason = reasons.OrderByDescending(r => r.weight).FirstOrDefault().reason ?? "to strengthen bonds among the nobility";

            return (score.ResultNumber > 75f, primaryReason);
        }

        private float GetSeasonalBonus()
        {
            var currentSeason = CampaignTime.Now.GetSeasonOfYear; // Fixed: Removed parentheses to access the property instead of invoking it as a method.
            switch (currentSeason)
            {
                case CampaignTime.Seasons.Autumn: return 15f;
                case CampaignTime.Seasons.Winter: return 20f;
                case CampaignTime.Seasons.Spring: return 5f;
                case CampaignTime.Seasons.Summer: return 0f;
                default: return 0f;
            }
        }

        private float GetDiplomaticBonus(Hero host, Kingdom kingdom)
        {
            float bonus = 0f;

            var recentAlliances = kingdom.GetAlliedKingdoms().Count();
            if (recentAlliances > 0)
            {
                bonus += recentAlliances * 10f;
            }

            var neighborKingdoms = Kingdom.All.Where(k => k != kingdom && !k.IsEliminated);
            var goodRelations = neighborKingdoms.Count(k => kingdom.GetRelation(k) > 0);
            bonus += goodRelations * 5f;

            return Math.Min(bonus, 25f);
        }

        private float GetStabilityBonus(Kingdom kingdom)
        {
            float stability = 0f;

            var discontentLords = kingdom.Lords.Count(l => l.GetRelation(kingdom.Leader) < -10);
            var totalLords = kingdom.Lords.Count();

            if (totalLords > 0)
            {
                float loyaltyRatio = 1f - (discontentLords / (float)totalLords);
                if (loyaltyRatio > 0.8f)
                {
                    stability += 15f;
                }
                else if (loyaltyRatio < 0.6f)
                {
                    stability -= 10f;
                }
            }

            return stability;
        }

        private float GetCulturalBonus(Hero host)
        {
            float bonus = 0f;

            switch (host.Culture.StringId.ToLower())
            {
                case "vlandia": bonus += 10f; break;
                case "empire": bonus += 5f; break;
                case "battania": bonus += 8f; break;
                case "sturgia": bonus += 12f; break;
                case "khuzait": bonus += 3f; break;
                case "aserai": bonus += 6f; break;
            }

            return bonus;
        }

        private bool HasRecentDiplomaticSuccess(Kingdom kingdom)
        {
            return kingdom.GetAlliedKingdoms().Any() ||
                   (!FactionManager.GetEnemyKingdoms(kingdom).Any() && timeSinceLastFeast.ContainsKey(kingdom));
        }

        private bool IsKingdomProperous(Kingdom kingdom)
        {
            var averageWealth = kingdom.Lords.Average(l => l.Gold);
            var settlementCount = kingdom.Settlements.Count();

            return averageWealth > 50000 && settlementCount >= 3;
        }

        private bool HasLowMorale(Kingdom kingdom)
        {
            var discontentLords = kingdom.Lords.Count(l => l.GetRelation(kingdom.Leader) < -5);
            return discontentLords > kingdom.Lords.Count() * 0.3f;
        }

        private bool HasRecentCelebratoryEvent(Hero host)
        {
            return host.Children.Any(c => c.Age < 2f) ||
                   (host.Spouse != null && host.GetRelation(host.Spouse) > 50);
        }

        public float calculateFoodContribution(Hero hero) => hero.PartyBelongedTo?.Food * 0.8f ?? 0f;

        public bool canHaveFeast(Kingdom kingdom)
        {
            if (timeSinceLastFeast.TryGetValue(kingdom, out double lastFeast) && (lastFeast + 15) >= CampaignTime.Now.ToDays)
                return false;

            // DON'T remove the entry when checking - only remove when actually creating a feast
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

            // ALWAYS add the player first if they're in this kingdom
            if (Hero.MainHero.MapFaction == kingdom)
            {
                partyHeros.Add(Hero.MainHero);
            }

            foreach (Clan clan in kingdom.Clans)
            {
                // Only invite clan leaders, not all lords in the clan
                if (clan.Leader != null && clan.Leader != Hero.MainHero && !clan.IsMinorFaction)
                {
                    if (clan.Leader.IsPartyLeader && FeastInviteHandler.checkIfLordWantsToJoin(feastHost, clan.Leader))
                    {
                        partyHeros.Add(clan.Leader);
                    }
                }
            }

            // InformationManager.DisplayMessage(new InformationMessage($"Debug: Invited {partyHeros.Count} lords to feast", Colors.Yellow));
            // foreach (var lord in partyHeros)
            // {
            //     InformationManager.DisplayMessage(new InformationMessage($"  - {lord.Name}", Colors.Yellow));
            // }

            return partyHeros;
        }

        public void MenuItems(CampaignGameStarter campaignGameSystemStarter)
        {
            // InformationManager.DisplayMessage(new InformationMessage("Registering feast menu items...", Colors.Yellow));

            campaignGameSystemStarter.AddGameMenuOption("town_keep", "host_a_feast_town", "Host a feast (Cost: 5000 Gold)", game_menu_host_feast_on_condition, game_menu_host_feast_consequence, false, 4, false);
            campaignGameSystemStarter.AddGameMenuOption("town_keep", "manage_feast_inventory_town", "Manage feast inventory", game_menu_manage_feast_on_condition, game_menu_manage_feast_consequence, false, 5, false);
            campaignGameSystemStarter.AddGameMenuOption("town_keep", "end_feast_town", "End the feast", game_menu_end_feast_on_condition, game_menu_end_feast_consequence, false, 6, false);
            campaignGameSystemStarter.AddGameMenuOption("castle", "host_a_feast_castle", "Host a feast (Cost: 5000 Gold)", game_menu_host_feast_on_condition, game_menu_host_feast_consequence, false, 4, false);
            campaignGameSystemStarter.AddGameMenuOption("castle", "manage_feast_inventory_castle", "Manage feast inventory", game_menu_manage_feast_on_condition, game_menu_manage_feast_consequence, false, 5, false);
            campaignGameSystemStarter.AddGameMenuOption("castle", "end_feast_castle", "End the feast", game_menu_end_feast_on_condition, game_menu_end_feast_consequence, false, 6, false);

            // InformationManager.DisplayMessage(new InformationMessage("Feast menu items registered successfully!", Colors.Green));
        }

        private bool game_menu_host_feast_on_condition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Leave;

            // Debug: Check what's preventing the menu from appearing
            bool hasNoActiveFeast = !Feasts.Any(f => f.kingdom == Hero.MainHero.MapFaction);
            bool hasKingdom = Hero.MainHero.MapFaction is Kingdom;
            bool isOwner = Hero.MainHero.CurrentSettlement?.Owner == Hero.MainHero;

            // Commented out debug messages
            // InformationManager.DisplayMessage(new InformationMessage($"Debug Feast Menu Check:", Colors.Blue));
            // InformationManager.DisplayMessage(new InformationMessage($"  - Player Faction: {Hero.MainHero.MapFaction?.Name?.ToString() ?? "null"}", Colors.Blue));
            // InformationManager.DisplayMessage(new InformationMessage($"  - Is Kingdom: {hasKingdom}", Colors.Blue));
            // InformationManager.DisplayMessage(new InformationMessage($"  - Current Settlement: {Hero.MainHero.CurrentSettlement?.Name?.ToString() ?? "null"}", Colors.Blue));
            // InformationManager.DisplayMessage(new InformationMessage($"  - Settlement Owner: {Hero.MainHero.CurrentSettlement?.Owner?.Name?.ToString() ?? "null"}", Colors.Blue));
            // InformationManager.DisplayMessage(new InformationMessage($"  - Is Owner: {isOwner}", Colors.Blue));
            // InformationManager.DisplayMessage(new InformationMessage($"  - Total Feasts: {Feasts.Count}", Colors.Blue));
            // InformationManager.DisplayMessage(new InformationMessage($"  - No Active Feast: {hasNoActiveFeast}", Colors.Blue));

            // if (!hasNoActiveFeast)
            // {
            //     var activeFeast = Feasts.First(f => f.kingdom == Hero.MainHero.MapFaction);
            //     InformationManager.DisplayMessage(new InformationMessage($"Debug: Active feast exists for {Hero.MainHero.MapFaction?.Name?.ToString() ?? "null"} at {activeFeast.feastSettlement?.Name?.ToString() ?? "null"}, hosted by {activeFeast.hostOfFeast?.Name?.ToString() ?? "null"}", Colors.Red));
            // }
            // if (!hasKingdom)
            // {
            //     InformationManager.DisplayMessage(new InformationMessage("Debug: Player not in kingdom", Colors.Red));
            // }
            // if (!isOwner)
            // {
            //     InformationManager.DisplayMessage(new InformationMessage($"Debug: Not settlement owner. Current: {Hero.MainHero.CurrentSettlement?.Name?.ToString() ?? "null"}, Owner: {Hero.MainHero.CurrentSettlement?.Owner?.Name?.ToString() ?? "null"}", Colors.Red));
            // }

            return hasNoActiveFeast && hasKingdom && isOwner;
        }

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
            if (Hero.MainHero.Clan.Kingdom.Stances.Any(s => s.IsAtWar))
                InformationManager.DisplayMessage(new InformationMessage("You cannot start a feast while at war."));
            else if (!canHaveFeast(Hero.MainHero.Clan.Kingdom))
                InformationManager.DisplayMessage(new InformationMessage("You must wait " + Convert.ToInt32((timeSinceLastFeast[Hero.MainHero.Clan.Kingdom] + 15) - CampaignTime.Now.ToDays) + " days before your kingdom can host another feast."));
            else if (Hero.MainHero.CurrentSettlement.Owner != Hero.MainHero)
                InformationManager.DisplayMessage(new InformationMessage("You may only host feasts at fiefs that you own."));
            else if (Hero.MainHero.Spouse == null)
                InformationManager.DisplayMessage(new InformationMessage("You have no partner. You must be married to host a feast."));
            else if (Hero.MainHero.Gold < 5000)
                InformationManager.DisplayMessage(new InformationMessage("You do not have at least 5000 Gold."));
            else if (!feastIsPresent(Hero.MainHero.Clan.Kingdom))
            {
                // Clear the timing restriction when actually creating a feast
                if (timeSinceLastFeast.ContainsKey(Hero.MainHero.Clan.Kingdom))
                    timeSinceLastFeast.Remove(Hero.MainHero.Clan.Kingdom);

                Feasts.Add(createFeast(Hero.MainHero, Hero.MainHero.CurrentSettlement, Hero.MainHero.Clan.Kingdom, getAllClanMembersInKingdomWhoWantToJoin(Hero.MainHero.Clan.Kingdom, Hero.MainHero), -1));
            }
            else
                InformationManager.DisplayMessage(new InformationMessage("A feast is already present for your kingdom."));
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