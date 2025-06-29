using Diplomacy.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

using WarAndAiTweaks.AI.Goals;
using WarAndAiTweaks.DiplomaticAction;

using static WarAndAiTweaks.AI.StrategicAI;

namespace WarAndAiTweaks.AI.Behaviors
{
    public class StrategicAICampaignBehavior : CampaignBehaviorBase
    {
        [SaveableField(1002)]
        private Dictionary<string, int> _peaceDays = new Dictionary<string, int>();

        [SaveableField(1003)]
        private Dictionary<string, int> _warDays = new Dictionary<string, int>();

        [SaveableField(1004)]
        private Dictionary<string, int> _daysSinceLastThinkPerKingdom = new Dictionary<string, int>();

        [SaveableField(1005)]
        private Dictionary<string, int> _thinkIntervalPerKingdom = new Dictionary<string, int>();

        [SaveableField(1006)]
        private Dictionary<string, StrategicState> _kingdomStrategicStates = new Dictionary<string, StrategicState>();

        [SaveableField(1007)]
        private Dictionary<string, CampaignTime> _lastPeaceTimes = new Dictionary<string, CampaignTime>();

        private IWarEvaluator _warEvaluator = new DefaultWarEvaluator();
        private IPeaceEvaluator _peaceEvaluator = new DefaultPeaceEvaluator();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
        }

        private void OnWarDeclared(IFaction faction1, IFaction faction2, DeclareWarAction.DeclareWarDetail detail)
        {
            // faction1 is the aggressor, faction2 is the defender.
            if (faction2 is Kingdom defender)
            {
                var alliesOfDefender = defender.GetAlliedKingdoms().ToList();
                foreach (var ally in alliesOfDefender)
                {
                    // If the ally is not already at war with the aggressor, they join the defensive war.
                    if (!ally.IsAtWarWith(faction1))
                    {
                        DeclareWarAction.ApplyByDefault(ally, faction1);
                        InformationManager.DisplayMessage(new InformationMessage($"{ally.Name} has joined the war against {faction1.Name} in defense of {defender.Name}.", Colors.Green));
                    }
                }
            }
        }

        public void OnPeaceDeclared(IFaction faction1, IFaction faction2, MakePeaceAction.MakePeaceDetail detail)
        {
            if (faction1 is Kingdom k1 && faction2 is Kingdom k2)
            {
                var key = (string.Compare(k1.StringId, k2.StringId) < 0)
                    ? $"{k1.StringId}_{k2.StringId}"
                    : $"{k2.StringId}_{k1.StringId}";
                _lastPeaceTimes[key] = CampaignTime.Now;
            }
        }

        private void OnDailyTick()
        {
            var kingdoms = Kingdom.All.ToList();
            kingdoms.Shuffle();
            bool warDeclaredThisTick = false;

            // PERFORMANCE: Process fewer kingdoms per day during active wars
            int maxKingdomsToProcess = kingdoms.Any(k => FactionManager.GetEnemyKingdoms(k).Any()) ? 2 : kingdoms.Count;
            int processedCount = 0;

            var expiredPacts = new List<NonAggressionPact>();
            foreach (var pact in DiplomaticAgreementManager.NonAggressionPacts.ToList())
            {
                if (pact.StartDate.ElapsedDaysUntilNow >= 20)
                {
                    expiredPacts.Add(pact);
                }
            }

            foreach (var pact in expiredPacts)
            {
                DiplomaticAgreementManager.BreakNonAggressionPact(pact.Faction1, pact.Faction2);
                InformationManager.DisplayMessage(new InformationMessage($"The non-aggression pact between {pact.Faction1.Name} and {pact.Faction2.Name} has expired.", Colors.Green));
            }

            foreach (var kingdom in kingdoms)
            {
                if (kingdom.IsEliminated || kingdom.Leader == null)
                {
                    continue;
                }

                // PERFORMANCE: Skip processing if we've hit our limit
                if (processedCount >= maxKingdomsToProcess)
                {
                    break;
                }

                var kingdomId = kingdom.StringId;

                if (!_daysSinceLastThinkPerKingdom.ContainsKey(kingdomId)) _daysSinceLastThinkPerKingdom[kingdomId] = 0;
                if (!_thinkIntervalPerKingdom.ContainsKey(kingdomId))
                {
                    // PERFORMANCE: Longer think intervals during war
                    bool isAtWar = FactionManager.GetEnemyKingdoms(kingdom).Any();
                    int interval = isAtWar ? MBRandom.RandomInt(3, 7) : MBRandom.RandomInt(2, 4);
                    _thinkIntervalPerKingdom[kingdomId] = interval;
                }

                _daysSinceLastThinkPerKingdom[kingdomId]++;

                if (_daysSinceLastThinkPerKingdom[kingdomId] < _thinkIntervalPerKingdom[kingdomId])
                {
                    continue;
                }

                _daysSinceLastThinkPerKingdom[kingdomId] = 0;

                // PERFORMANCE: Adjust next interval based on war status
                bool atWar = FactionManager.GetEnemyKingdoms(kingdom).Any();
                _thinkIntervalPerKingdom[kingdomId] = atWar ? MBRandom.RandomInt(3, 7) : MBRandom.RandomInt(2, 4);

                if (!_peaceDays.ContainsKey(kingdomId)) _peaceDays[kingdomId] = 0;
                if (!_warDays.ContainsKey(kingdomId)) _warDays[kingdomId] = 0;

                if (atWar)
                {
                    _warDays[kingdomId]++;
                    _peaceDays[kingdomId] = 0;
                }
                else
                {
                    _peaceDays[kingdomId]++;
                    _warDays[kingdomId] = 0;
                }

                var strategicState = StrategicStateEvaluator.GetStrategicState(kingdom);
                _kingdomStrategicStates[kingdomId] = strategicState;

                var enemies = FactionManager.GetEnemyKingdoms(kingdom).ToList();
                var allies = kingdom.GetAlliedKingdoms().ToList();
                var pacts = DiplomaticAgreementManager.GetPacts(kingdom).ToList();
                var bordering = kingdom.GetBorderingKingdoms().ToList();
                AIComputationLogger.LogDiplomaticOverview(kingdom, strategicState, enemies, allies, pacts, bordering);

                var currentGoal = GoalEvaluator.GetHighestPriorityGoal(kingdom, _peaceDays[kingdomId], _warDays[kingdomId], strategicState, _lastPeaceTimes);
                AIComputationLogger.LogAIGoal(kingdom, currentGoal, strategicState);

                var ai = new StrategicAI(kingdom, _warEvaluator, _peaceEvaluator, currentGoal, _lastPeaceTimes)
                {
                    DaysSinceLastWar = _peaceDays[kingdomId],
                    DaysAtWar = _warDays[kingdomId]
                };

                ai.TickDaily(ref warDeclaredThisTick);

                _peaceDays[kingdomId] = ai.DaysSinceLastWar;
                _warDays[kingdomId] = ai.DaysAtWar;

                processedCount++;
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_peaceDays", ref _peaceDays);
            dataStore.SyncData("_warDays", ref _warDays);
            dataStore.SyncData("_daysSinceLastThinkPerKingdom", ref _daysSinceLastThinkPerKingdom);
            dataStore.SyncData("_thinkIntervalPerKingdom", ref _thinkIntervalPerKingdom);
            dataStore.SyncData("_kingdomStrategicStates", ref _kingdomStrategicStates);
            dataStore.SyncData("_lastPeaceTimes", ref _lastPeaceTimes);

            if (dataStore.IsLoading)
            {
                _daysSinceLastThinkPerKingdom ??= new Dictionary<string, int>();
                _thinkIntervalPerKingdom ??= new Dictionary<string, int>();
                _kingdomStrategicStates ??= new Dictionary<string, StrategicState>();
                _lastPeaceTimes ??= new Dictionary<string, CampaignTime>();
            }
        }
    }
}