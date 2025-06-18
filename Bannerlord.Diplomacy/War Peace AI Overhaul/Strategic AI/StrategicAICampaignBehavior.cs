using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.SaveSystem;

using WarAndAiTweaks.AI.Goals;

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

        private IWarEvaluator _warEvaluator = new StrategicAI.DefaultWarEvaluator();
        private IPeaceEvaluator _peaceEvaluator = new StrategicAI.DefaultPeaceEvaluator();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        private void OnDailyTick()
        {
            var kingdoms = Kingdom.All.ToList();
            kingdoms.Shuffle();
            bool warDeclaredThisTick = false;

            foreach (var kingdom in kingdoms)
            {
                // CORRECTED LOGIC: AI will not run for a kingdom if the player is its leader.
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

                var currentGoal = GoalEvaluator.GetHighestPriorityGoal(kingdom, _peaceDays[kingdomId], _warDays[kingdomId], strategicState);
                AIComputationLogger.LogAIGoal(kingdom, currentGoal, strategicState);

                var ai = new StrategicAI(kingdom, _warEvaluator, _peaceEvaluator, currentGoal)
                {
                    DaysSinceLastWar = _peaceDays[kingdomId],
                    DaysAtWar = _warDays[kingdomId]
                };

                ai.TickDaily(ref warDeclaredThisTick);

                _peaceDays[kingdomId] = ai.DaysSinceLastWar;
                _warDays[kingdomId] = ai.DaysAtWar;
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_peaceDays", ref _peaceDays);
            dataStore.SyncData("_warDays", ref _warDays);
            dataStore.SyncData("_daysSinceLastThinkPerKingdom", ref _daysSinceLastThinkPerKingdom);
            dataStore.SyncData("_thinkIntervalPerKingdom", ref _thinkIntervalPerKingdom);
            dataStore.SyncData("_kingdomStrategicStates", ref _kingdomStrategicStates);

            if (dataStore.IsLoading)
            {
                _daysSinceLastThinkPerKingdom ??= new Dictionary<string, int>();
                _thinkIntervalPerKingdom ??= new Dictionary<string, int>();
                _kingdomStrategicStates ??= new Dictionary<string, StrategicState>();
            }
        }
    }
}