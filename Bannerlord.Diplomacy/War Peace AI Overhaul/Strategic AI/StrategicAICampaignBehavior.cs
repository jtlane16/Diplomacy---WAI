using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
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

        private IWarEvaluator _warEvaluator = new StrategicAI.DefaultWarEvaluator();
        private IPeaceEvaluator _peaceEvaluator = new StrategicAI.DefaultPeaceEvaluator();

        public override void RegisterEvents()
        {
            // Fires once per in-game day
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        private void OnDailyTick()
        {
            foreach (var kingdom in Kingdom.All.ToList())
            {
                if (kingdom.IsEliminated) continue;
                if (Clan.PlayerClan.Kingdom == kingdom) continue;

                var id = kingdom.StringId;
                bool atWar = FactionManager.GetEnemyKingdoms(kingdom).Any();

                if (!_peaceDays.ContainsKey(id))
                    _peaceDays[id] = 0;
                if (!_warDays.ContainsKey(id))
                    _warDays[id] = 0;

                if (atWar)
                {
                    _warDays[id]++;
                    _peaceDays[id] = 0;
                }
                else
                {
                    _peaceDays[id]++;
                    _warDays[id] = 0;
                }

                var ai = new StrategicAI(kingdom, _warEvaluator, _peaceEvaluator)
                {
                    DaysSinceLastWar = _peaceDays[id],
                    DaysAtWar = _warDays[id]    // now valid
                };

                ai.TickDaily();

                // Persist any changes back
                _peaceDays[id] = ai.DaysSinceLastWar;
                _warDays[id] = ai.DaysAtWar;
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_peaceDays", ref _peaceDays);
            dataStore.SyncData("_warDays", ref _warDays);
        }
    }
}
