using System.Collections.Generic;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.SaveSystem;

namespace WarAndAiTweaks.DiplomaticAction
{
    public class InfamyManager : CampaignBehaviorBase
    {
        [SaveableField(1)]
        private Dictionary<Kingdom, float> _infamy = new Dictionary<Kingdom, float>();

        public static InfamyManager Instance { get; private set; }

        public InfamyManager()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            // Correcting the usage of the event subscription
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        private void OnWarDeclared(IFaction faction1, IFaction faction2, DeclareWarAction.DeclareWarDetail detail)
        {
            if (faction1 is Kingdom aggressor && faction2 is Kingdom defender)
            {
                if (!_infamy.ContainsKey(aggressor))
                {
                    _infamy[aggressor] = 0;
                }
                _infamy[aggressor] += 10; // Base infamy for declaring war

                if (defender.TotalStrength < aggressor.TotalStrength * 0.5f)
                {
                    _infamy[aggressor] += 15; // Extra infamy for attacking a much weaker faction
                }
            }
        }

        private void OnDailyTick()
        {
            // Infamy decays over time
            var keys = new List<Kingdom>(_infamy.Keys);
            foreach (var kingdom in keys)
            {
                _infamy[kingdom] *= 0.995f; // Daily decay of 0.5%
            }
        }

        public float GetInfamy(Kingdom kingdom)
        {
            if (_infamy.TryGetValue(kingdom, out var infamy))
            {
                return infamy;
            }
            return 0;
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_infamy", ref _infamy);
            if (dataStore.IsLoading)
            {
                Instance = this;
                if (_infamy == null)
                {
                    _infamy = new Dictionary<Kingdom, float>();
                }
            }
        }
    }
}