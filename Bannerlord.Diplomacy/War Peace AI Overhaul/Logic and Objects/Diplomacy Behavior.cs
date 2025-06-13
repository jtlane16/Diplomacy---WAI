using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapNotificationTypes;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

using static Diplomacy.WarExhaustion.WarExhaustionManager;

namespace WarAndAiTweaks
{
    // FIX: Create a simple, save-friendly class to store the cache data.
    // The game's save system can easily handle a list of these objects.
    public class NeighborCacheEntry
    {
        [SaveableField(1)]
        public string Kingdom1Id;

        [SaveableField(2)]
        public string Kingdom2Id;

        [SaveableField(3)]
        public int ProximityScore;
    }


    public class DiplomacyBehavior : CampaignBehaviorBase
    {
        private static bool _isHandlingWarDeclaration = false;
        private const bool SHOW_DIPLOMACY_MSGS = true;

        public static DiplomacyBehavior Instance { get; private set; }

        [SaveableField(3)] // Make sure this number is unique within the class
        private Dictionary<string, int> _daysAtPeace = new Dictionary<string, int>();

        // FIX: The cache is now a simple list of our saveable class.
        private List<NeighborCacheEntry> _neighborCache = new List<NeighborCacheEntry>();

        // This public property is just for external read-only access if needed.
        public IReadOnlyList<NeighborCacheEntry> NeighborCache => _neighborCache;

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
            // The list of our custom class is now safe to sync.
            dataStore.SyncData("_neighborCache", ref _neighborCache);
            dataStore.SyncData("_daysAtPeace", ref _daysAtPeace);
        }

        // This is a required definition for the save system to recognize our custom class.
        public class DiplomacyBehaviorTypeDefiner : SaveableTypeDefiner
        {
            public DiplomacyBehaviorTypeDefiner() : base(123456789) { } // Use a number unique to your mod

            protected override void DefineClassTypes()
            {
                AddClassDefinition(typeof(NeighborCacheEntry), 1);
            }

            protected override void DefineContainerDefinitions()
            {
                // We need to tell the game it can save a List of our custom type.
                ConstructContainerDefinition(typeof(List<NeighborCacheEntry>));
            }
        }


        private void DailyTick()
        {
            UpdateNeighborCache();

            foreach (var kingdom in Kingdom.All.Where(k => !k.IsMinorFaction && !k.IsEliminated))
            {
                // If the kingdom has no enemies, increment its peace counter.
                if (!FactionManager.GetEnemyKingdoms(kingdom).Any())
                {
                    if (_daysAtPeace.ContainsKey(kingdom.StringId))
                    {
                        _daysAtPeace[kingdom.StringId]++;
                    }
                    else
                    {
                        _daysAtPeace[kingdom.StringId] = 1;
                    }
                }
                else
                {
                    // If at war, reset the counter.
                    _daysAtPeace[kingdom.StringId] = 0;
                }
            }
        }

        private void UpdateNeighborCache()
        {
            _neighborCache.Clear();
            var kingdoms = Kingdom.All.Where(k => !k.IsMinorFaction && !k.IsEliminated).ToList();
            const float BORDER_DISTANCE_THRESHOLD = 150f;

            // Use a temporary set to avoid adding duplicate pairs (e.g., K1-K2 and K2-K1)
            var processedPairs = new HashSet<Tuple<string, string>>();


            foreach (var k1 in kingdoms)
            {
                foreach (var k2 in kingdoms)
                {
                    if (k1 == k2) continue;

                    // Ensure we only process each pair once
                    var pair = k1.StringId.CompareTo(k2.StringId) < 0
                        ? Tuple.Create(k1.StringId, k2.StringId)
                        : Tuple.Create(k2.StringId, k1.StringId);

                    if (processedPairs.Contains(pair)) continue;

                    int borderingSettlementCount = k1.Settlements.Count(s1 => k2.Settlements.Any(s2 => Campaign.Current.Models.MapDistanceModel.GetDistance(s1, s2) < BORDER_DISTANCE_THRESHOLD));

                    if (borderingSettlementCount > 0)
                    {
                        _neighborCache.Add(new NeighborCacheEntry
                        {
                            Kingdom1Id = k1.StringId,
                            Kingdom2Id = k2.StringId,
                            ProximityScore = borderingSettlementCount
                        });
                    }
                    processedPairs.Add(pair);
                }
            }
        }

        // Add a public method to get the days at peace
        public int GetDaysAtPeace(Kingdom kingdom)
        {
            return _daysAtPeace.TryGetValue(kingdom.StringId, out var days) ? days : 0;
        }

        // FIX: This method is updated to query the new list-based cache.
        public int GetNeighborProximityScore(Kingdom k1, Kingdom k2)
        {
            var entry = _neighborCache.FirstOrDefault(e =>
                (e.Kingdom1Id == k1.StringId && e.Kingdom2Id == k2.StringId) ||
                (e.Kingdom1Id == k2.StringId && e.Kingdom2Id == k1.StringId));

            return entry?.ProximityScore ?? 0;
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

        // FIX: This method is updated to work with the new list-based cache.
        public List<Kingdom> GetNeighborsOf(Kingdom k)
        {
            var neighborIds = new HashSet<string>();
            foreach (var entry in _neighborCache)
            {
                if (entry.Kingdom1Id == k.StringId)
                {
                    neighborIds.Add(entry.Kingdom2Id);
                }
                else if (entry.Kingdom2Id == k.StringId)
                {
                    neighborIds.Add(entry.Kingdom1Id);
                }
            }

            return Kingdom.All.Where(kingdom => neighborIds.Contains(kingdom.StringId)).ToList();
        }
    }
}