﻿using Diplomacy.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;

using Diplomacy.Extensions;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;
using WarAndAiTweaks.AI.Goals;

using static WarAndAiTweaks.AI.StrategicAI;
using WarAndAiTweaks.DiplomaticAction;

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

        private IWarEvaluator _warEvaluator = new StrategicAI.DefaultWarEvaluator();
        private IPeaceEvaluator _peaceEvaluator = new StrategicAI.DefaultPeaceEvaluator();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
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
                InformationManager.DisplayMessage(new InformationMessage($"The non-aggression pact between {pact.Faction1.Name} and {pact.Faction2.Name} has expired.", TaleWorlds.Library.Colors.Green));
            }

            foreach (var kingdom in kingdoms)
            {
                if (kingdom.IsEliminated || kingdom.Leader == Hero.MainHero)
                {
                    continue;
                }

                var kingdomId = kingdom.StringId;

                if (!_daysSinceLastThinkPerKingdom.ContainsKey(kingdomId)) _daysSinceLastThinkPerKingdom[kingdomId] = 0;
                if (!_thinkIntervalPerKingdom.ContainsKey(kingdomId)) _thinkIntervalPerKingdom[kingdomId] = MBRandom.RandomInt(2, 4);

                _daysSinceLastThinkPerKingdom[kingdomId]++;

                if (_daysSinceLastThinkPerKingdom[kingdomId] < _thinkIntervalPerKingdom[kingdomId])
                {
                    continue;
                }

                _daysSinceLastThinkPerKingdom[kingdomId] = 0;
                _thinkIntervalPerKingdom[kingdomId] = MBRandom.RandomInt(2, 4);

                bool atWar = FactionManager.GetEnemyKingdoms(kingdom).Any();

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

                var currentAllies = Kingdom.All.Where(k => k != kingdom && FactionManager.IsAlliedWithFaction(kingdom, k)).ToList();
                if (currentAllies.Count > 1)
                {
                    var breakAllianceScoringModel = new WarAndAiTweaks.AI.BreakAllianceScoringModel();
                    Kingdom? weakestAlly = null;
                    float highestBreakScore = float.MinValue;

                    foreach (var ally in currentAllies)
                    {
                        var breakScore = breakAllianceScoringModel.GetBreakAllianceScore(kingdom, ally).ResultNumber;
                        if (breakScore > highestBreakScore)
                        {
                            highestBreakScore = breakScore;
                            weakestAlly = ally;
                        }
                    }

                    // If a weakest ally was found and conditions allow, break the alliance.
                    if (weakestAlly != null)
                    {
                        DiplomaticAction.BreakAllianceAction.Apply(kingdom, weakestAlly);
                        AIComputationLogger.LogBetrayalDecision(kingdom, weakestAlly, highestBreakScore);
                        continue;
                    }
                }
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