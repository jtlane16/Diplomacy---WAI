using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.SaveSystem;

using static WarAndAiTweaks.AI.StrategicAI;

namespace WarAndAiTweaks.AI.Behaviors
{
    /// <summary>
    /// Persists peace-days and war-days per kingdom and invokes StrategicAI daily.
    /// </summary>
    public class StrategicAICampaignBehavior : CampaignBehaviorBase
    {
        [SaveableField(1002)]
        private Dictionary<string, int> _peaceDays = new Dictionary<string, int>();

        [SaveableField(1003)]
        private Dictionary<string, int> _warDays = new Dictionary<string, int>();

        // New dictionaries for per-kingdom thinking intervals
        [SaveableField(1004)]
        private Dictionary<string, int> _daysSinceLastThinkPerKingdom = new Dictionary<string, int>();

        [SaveableField(1005)]
        private Dictionary<string, int> _thinkIntervalPerKingdom = new Dictionary<string, int>();

        private IWarEvaluator _warEvaluator = new StrategicAI.DefaultWarEvaluator();
        private IPeaceEvaluator _peaceEvaluator = new StrategicAI.DefaultPeaceEvaluator();

        public StrategicAICampaignBehavior()
        {
            // Initializations for dictionaries can be done here or in SyncData on load
        }

        public override void RegisterEvents()
        {
            // Fires once per in-game day
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        private void OnDailyTick()
        {
            var kingdoms = Kingdom.All.ToList();
            kingdoms.Shuffle();
            bool warDeclaredThisTick = false;

            foreach (var kingdom in kingdoms)
            {
                if (kingdom.IsEliminated || Clan.PlayerClan.Kingdom == kingdom)
                {
                    continue;
                }

                var kingdomId = kingdom.StringId;

                // Initialize per-kingdom thinking data if not present
                if (!_daysSinceLastThinkPerKingdom.ContainsKey(kingdomId))
                {
                    _daysSinceLastThinkPerKingdom[kingdomId] = 0;
                }
                if (!_thinkIntervalPerKingdom.ContainsKey(kingdomId))
                {
                    _thinkIntervalPerKingdom[kingdomId] = MBRandom.RandomInt(2, 4); // Random interval between 2 and 3 days
                }

                _daysSinceLastThinkPerKingdom[kingdomId]++;

                // Only execute AI thinking logic if the think interval has passed for this specific kingdom
                if (_daysSinceLastThinkPerKingdom[kingdomId] < _thinkIntervalPerKingdom[kingdomId])
                {
                    continue;
                }

                // Reset the counter and set a new random interval for the next cycle for this kingdom
                _daysSinceLastThinkPerKingdom[kingdomId] = 0;
                _thinkIntervalPerKingdom[kingdomId] = MBRandom.RandomInt(2, 4);

                bool atWar = FactionManager.GetEnemyKingdoms(kingdom).Any();

                if (!_peaceDays.ContainsKey(kingdomId))
                    _peaceDays[kingdomId] = 0;
                if (!_warDays.ContainsKey(kingdomId))
                    _warDays[kingdomId] = 0;

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

                // 1. EVALUATE GOAL
                var currentGoal = GoalEvaluator.GetHighestPriorityGoal(kingdom, _peaceDays[kingdomId], _warDays[kingdomId]);
                AIComputationLogger.LogAIGoal(kingdom, currentGoal);

                // 2. CREATE AI INSTANCE WITH THE GOAL
                var ai = new StrategicAI(kingdom, _warEvaluator, _peaceEvaluator, currentGoal)
                {
                    DaysSinceLastWar = _peaceDays[kingdomId],
                    DaysAtWar = _warDays[kingdomId]
                };

                // 3. EXECUTE ACTIONS BASED ON GOAL
                ai.TickDaily(ref warDeclaredThisTick);

                // Persist any changes back
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

            // Handle potential loading from older saves where these fields didn't exist
            if (dataStore.IsLoading)
            {
                _daysSinceLastThinkPerKingdom ??= new Dictionary<string, int>();
                _thinkIntervalPerKingdom ??= new Dictionary<string, int>();
            }
        }
    }
}