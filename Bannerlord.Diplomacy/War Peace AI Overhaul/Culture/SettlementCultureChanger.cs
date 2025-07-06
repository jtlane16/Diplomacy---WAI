using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace WarAndAiTweaks.Systems
{
    public class SettlementCultureChangerBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            // This behavior will run its logic once every in-game day.
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // This behavior doesn't need to save any data.
        }

        private void OnDailyTick()
        {
            // We iterate through every settlement in the game.
            foreach (Settlement settlement in Settlement.All.Where(x => x.IsCastle || x.IsTown))
            {
                // We only care about Towns and Castles that have an owner.
                if (settlement.OwnerClan != null && settlement.OwnerClan.Kingdom != null && settlement.OwnerClan.Kingdom.Culture != null && settlement.Culture != null)
                {
                    // Get the culture of the clan that owns the settlement.
                    var ownerCulture = settlement.OwnerClan.Kingdom.Culture;

                    // If the settlement's culture is already the same as the owner's, do nothing.
                    if ( settlement.Culture == ownerCulture)
                    {
                        continue;
                    }

                    // Change the settlement's culture to match the owner's.
                    settlement.Culture = ownerCulture;

                    // It's crucial to also change the culture of the notables living in the settlement.
                    // This affects which troops they offer for recruitment.
                    foreach (Hero notable in settlement.Notables)
                    {
                        if (notable.Culture != ownerCulture)
                        {
                            notable.Culture = ownerCulture;
                        }
                    }
                    foreach (Village village in settlement.BoundVillages)
                    {
                        village.Settlement.Culture = ownerCulture;
                        foreach (Hero notable in village.Settlement.Notables)
                        {
                            if (notable.Culture != ownerCulture)
                            {
                                notable.Culture = ownerCulture;
                            }
                        }
                    }
                }
            }
        }
    }
}