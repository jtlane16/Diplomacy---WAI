using Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace WarAndAiTweaks.WarPeaceAI
{
    public class KingdomLogicController : CampaignBehaviorBase
    {
        [SaveableField(1)]
        public Dictionary<string, KingdomStrategy> Strategies = new();

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
            UpdateAllKingdomStrategies();

            var aiKingdoms = Kingdom.All
                .Where(k => k != null && !k.IsEliminated && !k.IsMinorFaction
                           && k.Leader != null && k.Leader != Hero.MainHero)
                .ToList();

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
            var currentWars = KingdomLogicHelpers.GetEnemyKingdoms(selectedKingdom).Count;
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
}