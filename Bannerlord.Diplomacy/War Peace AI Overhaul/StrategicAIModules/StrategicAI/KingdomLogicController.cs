using Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI;

using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace WarAndAiTweaks.WarPeaceAI
{
    public class KingdomLogicController : CampaignBehaviorBase
    {
        [SaveableField(1)]
        public Dictionary<string, KingdomStrategy> Strategies = new();

        // Performance caches
        private Dictionary<string, List<Kingdom>> _enemyKingdomsCache = new();
        private Dictionary<string, bool> _borderingCache = new();
        private List<Kingdom> _aiKingdomsCache = null;
        private float _lastCacheDay = -1f;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, DailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("KingdomStrategies", ref Strategies);
            if (dataStore.IsLoading && Strategies == null)
                Strategies = new Dictionary<string, KingdomStrategy>();
        }

        public KingdomStrategy GetKingdomStrategy(Kingdom kingdom)
        {
            if (kingdom == null) return null;
            if (!Strategies.TryGetValue(kingdom.StringId, out KingdomStrategy strategy))
            {
                strategy = new KingdomStrategy();
                Strategies[kingdom.StringId] = strategy;
            }
            return strategy;
        }

        private void DailyTick()
        {
            // Clear daily caches
            InvalidateDailyCaches();

            UpdateAllKingdomStrategies();

            var aiKingdoms = GetAIKingdomsCached();
            if (aiKingdoms.Count == 0) return;

            // Process up to 3 kingdoms per day
            int numToThink = Math.Min(3, aiKingdoms.Count);
            int dayOffset = (int) (CampaignTime.Now.ToDays % aiKingdoms.Count);

            var thinkingKingdoms = aiKingdoms
                .Skip(dayOffset)
                .Concat(aiKingdoms.Take(dayOffset))
                .Take(numToThink)
                .ToList();

            foreach (var kingdom in thinkingKingdoms)
            {
                ProcessKingdomDecisions(kingdom);
            }
        }

        private void InvalidateDailyCaches()
        {
            float currentDay = (float) CampaignTime.Now.ToDays;

            // Clear daily caches
            _enemyKingdomsCache.Clear();

            // Keep AI kingdoms cache and bordering cache for 1 day
            if (currentDay - _lastCacheDay > 1f)
            {
                _aiKingdomsCache = null;
                _borderingCache.Clear();
                _lastCacheDay = currentDay;
            }
        }

        private List<Kingdom> GetAIKingdomsCached()
        {
            if (_aiKingdomsCache == null)
            {
                _aiKingdomsCache = Kingdom.All
                    .Where(k => k != null && !k.IsEliminated && !k.IsMinorFaction
                               && k.Leader != null && k.Leader != Hero.MainHero)
                    .ToList();
            }
            return _aiKingdomsCache;
        }

        private void UpdateAllKingdomStrategies()
        {
            var allKingdoms = Kingdom.All
                .Where(k => k != null && !k.IsEliminated && !k.IsMinorFaction && k.Leader != null)
                .ToList();

            foreach (var kingdom in allKingdoms)
            {
                if (kingdom.Leader == Hero.MainHero) continue;

                var strategy = GetKingdomStrategy(kingdom);
                if (strategy == null) continue;

                foreach (var target in allKingdoms)
                {
                    if (target == kingdom) continue;
                    float stanceChange = StrategyEvaluator.CalculateStanceChange(kingdom, target);
                    strategy.AdjustStance(target, stanceChange);
                }
            }
        }

        private void ProcessKingdomDecisions(Kingdom selectedKingdom)
        {
            var strategy = GetKingdomStrategy(selectedKingdom);
            if (strategy == null) return;

            // Process peace decisions first
            var peaceTargets = strategy.GetPeaceTargets(selectedKingdom);
            foreach (var target in peaceTargets)
            {
                ProcessPeaceDecision(selectedKingdom, target, strategy);
            }

            // Process war decisions if not overwhelmed
            var currentWars = GetEnemyKingdomsCached(selectedKingdom).Count;
            if (currentWars <= 1)
            {
                var warTargets = strategy.GetWarTargets(selectedKingdom);
                foreach (var target in warTargets)
                {
                    if (ProcessWarDecision(selectedKingdom, target, strategy))
                        break; // Only declare one war at a time
                }
            }
        }

        // OPTIMIZATION: Cached enemy kingdoms lookup
        public List<Kingdom> GetEnemyKingdomsCached(Kingdom kingdom)
        {
            if (kingdom == null) return new List<Kingdom>();

            if (!_enemyKingdomsCache.TryGetValue(kingdom.StringId, out var enemies))
            {
                enemies = Kingdom.All
                    .Where(k => k != null && k != kingdom && !k.IsEliminated && !k.IsMinorFaction
                               && k.Leader != null && kingdom.IsAtWarWith(k))
                    .ToList();
                _enemyKingdomsCache[kingdom.StringId] = enemies;
            }

            return enemies;
        }

        // OPTIMIZATION: Cached bordering check
        public bool AreBorderingCached(Kingdom kingdomA, Kingdom kingdomB)
        {
            if (kingdomA == null || kingdomB == null || kingdomA == kingdomB)
                return false;

            string cacheKey = $"{kingdomA.StringId}_{kingdomB.StringId}";
            string reverseCacheKey = $"{kingdomB.StringId}_{kingdomA.StringId}";

            if (_borderingCache.TryGetValue(cacheKey, out bool result))
                return result;
            if (_borderingCache.TryGetValue(reverseCacheKey, out result))
                return result;

            // Optimized bordering check
            result = AreBorderingOptimized(kingdomA, kingdomB);
            _borderingCache[cacheKey] = result;

            return result;
        }

        private bool AreBorderingOptimized(Kingdom kingdomA, Kingdom kingdomB)
        {
            // Get the closest settlements between the two kingdoms
            var settlementsA = kingdomA.Settlements.Where(s => s.IsFortification || s.IsVillage).Take(8);
            var settlementsB = kingdomB.Settlements.Where(s => s.IsFortification || s.IsVillage).Take(8);

            if (!settlementsA.Any() || !settlementsB.Any()) return false;

            // Find minimum distance between any settlements of the two kingdoms
            float minDistanceSquared = float.MaxValue;
            foreach (var settlementA in settlementsA)
            {
                foreach (var settlementB in settlementsB)
                {
                    float distSquared = settlementA.Position2D.DistanceSquared(settlementB.Position2D);
                    if (distSquared < minDistanceSquared)
                        minDistanceSquared = distSquared;
                }
            }

            // Compare against map-relative threshold
            float borderingThresholdSquared = GetBorderingThresholdSquared();
            return minDistanceSquared <= borderingThresholdSquared;
        }

        // Cache for bordering threshold
        private static float _cachedBorderingThresholdSquared = -1f;
        private static float _lastBorderingThresholdCalculation = -1f;

        private float GetBorderingThresholdSquared()
        {
            float currentDay = (float) CampaignTime.Now.ToDays;

            // Recalculate threshold every 3 days
            if (_cachedBorderingThresholdSquared < 0 || currentDay - _lastBorderingThresholdCalculation > 3f)
            {
                _cachedBorderingThresholdSquared = CalculateBorderingThresholdSquared();
                _lastBorderingThresholdCalculation = currentDay;
            }

            return _cachedBorderingThresholdSquared;
        }

        private float CalculateBorderingThresholdSquared()
        {
            var kingdoms = Kingdom.All.Where(k => !k.IsEliminated && !k.IsMinorFaction && k.Leader != null).ToList();

            if (kingdoms.Count < 2) return 1000000f; // 1000^2 fallback

            // Find the minimum distance between any two settlements of different kingdoms
            // This gives us the "closest neighbors" distance on this map
            float minGlobalDistanceSquared = float.MaxValue;
            int sampleCount = 0;
            const int maxSamples = 50; // Limit samples for performance

            foreach (var kingdom in kingdoms.Take(10)) // Sample from first 10 kingdoms only
            {
                var settlements = kingdom.Settlements.Where(s => s.IsFortification || s.IsVillage).Take(3);

                foreach (var settlement in settlements)
                {
                    foreach (var otherKingdom in kingdoms.Where(k => k != kingdom).Take(5)) // Check against 5 other kingdoms
                    {
                        var otherSettlements = otherKingdom.Settlements.Where(s => s.IsFortification || s.IsVillage).Take(3);

                        foreach (var otherSettlement in otherSettlements)
                        {
                            float distSquared = settlement.Position2D.DistanceSquared(otherSettlement.Position2D);
                            if (distSquared < minGlobalDistanceSquared)
                                minGlobalDistanceSquared = distSquared;

                            sampleCount++;
                            if (sampleCount >= maxSamples) break;
                        }
                        if (sampleCount >= maxSamples) break;
                    }
                    if (sampleCount >= maxSamples) break;
                }
                if (sampleCount >= maxSamples) break;
            }

            // Use 1.5x the minimum distance as the bordering threshold
            // This means settlements need to be closer than 1.5x the tightest spacing on the map
            return minGlobalDistanceSquared * 2.25f; // 1.5^2 = 2.25
        }

        private bool ProcessWarDecision(Kingdom self, Kingdom target, KingdomStrategy strategy)
        {
            // Feast integration: Prevent war declaration if a feast is active for this kingdom
            if (StrategyEvaluator.IsFeastActiveForKingdom(self))
                return false;

            float stance = strategy.GetStance(target);

            if (StrategicSafeguards.IsDecisionTooSoon(self, target, true))
                return false;

            bool isWarWise = StrategicSafeguards.IsWarDeclarationWise(self, target);
            bool shouldDeclareWar = stance >= KingdomStrategy.WAR_THRESHOLD && isWarWise;

            if (shouldDeclareWar)
            {
                DeclareWarAction.ApplyByDefault(self, target);
                string message = StrategyEvaluator.GetWarReason(self, target);
                InformationManager.DisplayMessage(new InformationMessage(message, Colors.Red));
                return true;
            }

            return false;
        }

        private void ProcessPeaceDecision(Kingdom self, Kingdom target, KingdomStrategy strategy)
        {
            float stance = strategy.GetStance(target);

            if (StrategicSafeguards.IsDecisionTooSoon(self, target, false))
                return;

            // Check for emergency coalition peace or forced peace
            bool emergencyPeace = CoalitionSystem.ShouldForceEmergencyPeace(self, target);
            bool forcedPeace = StrategicSafeguards.ShouldForceWarEnd(self, target);
            bool shouldMakePeace = stance <= KingdomStrategy.PEACE_THRESHOLD || emergencyPeace || forcedPeace;

            if (shouldMakePeace)
            {
                string message = emergencyPeace
                    ? $"Couriers proclaim: {self.Name} and {target.Name} have made peace, heeding the call of a great coalition."
                    : forcedPeace
                        ? $"Scribes record: {self.Name} and {target.Name} have ended their war out of sheer necessity."
                        : StrategyEvaluator.GetPeaceReason(self, target);

                if (target.Leader == Hero.MainHero)
                {
                    KingdomLogicHelpers.SendAIRequestToPlayerKingdom(self, target, "peace", stance);
                }
                else
                {
                    // Check if target also wants peace (mutual agreement)
                    var targetStrategy = GetKingdomStrategy(target);
                    float targetStance = targetStrategy?.GetStance(self) ?? 50f;

                    if (targetStance <= KingdomStrategy.PEACE_THRESHOLD)
                    {
                        int dailyTribute = KingdomLogicHelpers.GetPeaceTribute(
                            self.Leader.Clan, target.Leader.Clan, self, target);

                        MakePeaceAction.Apply(self, target, dailyTribute);

                        InformationManager.DisplayMessage(new InformationMessage(message, Colors.Green));
                    }
                }
            }
        }

        // External API
        public float GetKingdomStance(Kingdom self, Kingdom target)
        {
            var strategy = GetKingdomStrategy(self);
            return strategy?.GetStance(target) ?? 50f;
        }

        public void AdjustKingdomStance(Kingdom self, Kingdom target, float delta)
        {
            var strategy = GetKingdomStrategy(self);
            strategy?.AdjustStance(target, delta);
        }
    }
    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomWarDecision")]
    public class Patch_DisableRandomWar { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }
    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "ConsiderWar")]
    public class Patch_DisableConsiderWar { private static bool Prefix(Clan clan, Kingdom kingdom, IFaction otherFaction) { return false; } }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomPeaceDecision")]
    public class Patch_DisableRandomPeace { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }
    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "ConsiderPeace")]
    public class Patch_DisableConsiderPeace { private static bool Prefix(Clan clan, Clan otherClan, Kingdom kingdom, IFaction otherFaction, out MakePeaceKingdomDecision decision) { decision = null; return false; } }
}