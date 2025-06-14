using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

using static WarAndAiTweaks.AI.StrategicAI;

namespace WarAndAiTweaks.AI.Behaviors
{
    /// <summary>
    /// Persists peace-days per kingdom and invokes StrategicAI daily.
    /// </summary>
    public class StrategicAICampaignBehavior : CampaignBehaviorBase
    {
        [SaveableField(1002)]
        private Dictionary<string, int> _peaceDays = new Dictionary<string, int>();
        private IWarEvaluator _warEvaluator = new StrategicAI.DefaultWarEvaluator();
        private IPeaceEvaluator _peaceEvaluator = new StrategicAI.DefaultPeaceEvaluator();

        public override void RegisterEvents()
        {
            // Fires once per in-game day without parameters
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        private void OnDailyTick()
        {
            foreach (var kingdom in Kingdom.All.ToList())
            {
                if (kingdom.IsEliminated) continue;
                // skip player's own kingdom
                if (Clan.PlayerClan.Kingdom == kingdom) continue;

                if (!_peaceDays.ContainsKey(kingdom.StringId))
                    _peaceDays[kingdom.StringId] = 0;

                bool atWar = FactionManager.GetEnemyKingdoms(kingdom).Any();
                _peaceDays[kingdom.StringId] = atWar ? 0 : _peaceDays[kingdom.StringId] + 1;

                var ai = new StrategicAI(kingdom, _warEvaluator, _peaceEvaluator)
                {
                    DaysSinceLastWar = _peaceDays[kingdom.StringId]
                };
                ai.TickDaily();
                _peaceDays[kingdom.StringId] = ai.DaysSinceLastWar;
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_peaceDays", ref _peaceDays);
        }
    }
}
