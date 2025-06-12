using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.CampaignSystem.MapNotificationTypes;
using TaleWorlds.Localization;
using System;

namespace WarAndAiTweaks
{
    public class DiplomacyBehavior : CampaignBehaviorBase
    {
        private static bool _isHandlingWarDeclaration = false;
        private const bool SHOW_DIPLOMACY_MSGS = true;

        public static DiplomacyBehavior Instance { get; private set; }

        // FIX: Changed the dictionary to use strings, which are always safe to save.
        private Dictionary<string, List<string>> _neighborCache = new Dictionary<string, List<string>>();

        // This public property is now just a convenient accessor.
        public Dictionary<string, List<string>> NeighborCache => _neighborCache;

        public DiplomacyBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, DailyTick);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // FIX: We now save and load the cache since it's in a safe format.
            dataStore.SyncData("_neighborCache", ref _neighborCache);
        }

        private void DailyTick()
        {
            UpdateNeighborCache();
        }

        private void UpdateNeighborCache()
        {
            NeighborCache.Clear();
            var kingdoms = Kingdom.All.Where(k => !k.IsMinorFaction && !k.IsEliminated).ToList();
            foreach (var k1 in kingdoms)
            {
                // FIX: Use the kingdom's StringId as the key
                string k1Id = k1.StringId;
                if (!NeighborCache.ContainsKey(k1Id))
                {
                    NeighborCache[k1Id] = new List<string>();
                }
                foreach (var k2 in kingdoms)
                {
                    if (k1 == k2) continue;
                    const float BORDER_DISTANCE_THRESHOLD = 150f;
                    if (k1.Settlements.Any(s1 => k2.Settlements.Any(s2 => Campaign.Current.Models.MapDistanceModel.GetDistance(s1, s2) < BORDER_DISTANCE_THRESHOLD)))
                    {
                        // FIX: Add the other kingdom's StringId to the list
                        NeighborCache[k1Id].Add(k2.StringId);
                    }
                }
            }
        }

        // This method remains unchanged
        private void OnWarDeclared(IFaction a, IFaction b, DeclareWarAction.DeclareWarDetail detail)
        {
            if (_isHandlingWarDeclaration) return;
            _isHandlingWarDeclaration = true;
            try
            {
                if (a is Kingdom aggressor && b is Kingdom defender)
                {
                    var playerK = Clan.PlayerClan.Kingdom;
                    if (playerK != null && SHOW_DIPLOMACY_MSGS)
                    {
                        if (aggressor == playerK)
                        {
                            var note = new WarMapNotification(playerK, defender, new TextObject($"Your kingdom has declared war on {defender.Name}!"));
                            MBInformationManager.AddNotice(note);
                        }
                        else if (defender == playerK)
                        {
                            var note = new WarMapNotification(aggressor, playerK, new TextObject($"{aggressor.Name} has declared war on your kingdom!"));
                            MBInformationManager.AddNotice(note);
                        }
                    }
                }
            }
            finally
            {
                _isHandlingWarDeclaration = false;
            }
        }

        // FIX: This method is updated to work with the new string-based cache.
        public List<Kingdom> GetNeighborsOf(Kingdom k)
        {
            if (NeighborCache.TryGetValue(k.StringId, out var neighborIds))
            {
                var neighbors = new List<Kingdom>();
                foreach (var id in neighborIds)
                {
                    var neighborKingdom = Kingdom.All.FirstOrDefault(x => x.StringId == id);
                    if (neighborKingdom != null)
                    {
                        neighbors.Add(neighborKingdom);
                    }
                }
                return neighbors;
            }
            return new List<Kingdom>();
        }
    }
}