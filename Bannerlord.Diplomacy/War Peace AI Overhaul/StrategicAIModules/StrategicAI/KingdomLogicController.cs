using Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.SaveSystem;

namespace WarAndAiTweaks.WarPeaceAI
{
    public class KingdomLogicController : CampaignBehaviorBase
    {
        // New Strategy-based system
        [SaveableField(1)]
        public Dictionary<string, KingdomStrategy> Strategies = new();

        // Key: (AI Kingdom StringId, Player Kingdom StringId), Value: Last request time
        internal static readonly Dictionary<(string, string), CampaignTime> PeaceRequestCooldowns = new();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, DailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("KingdomStrategies", ref Strategies);

            // PeaceRequestCooldowns sync logic
            List<string> keys = null;
            List<double> values = null;
            if (dataStore.IsSaving)
            {
                keys = PeaceRequestCooldowns.Keys.Select(k => $"{k.Item1}|{k.Item2}").ToList();
                values = PeaceRequestCooldowns.Values.Select(v => v.ToHours).ToList();
            }
            dataStore.SyncData("WarPeace_PeaceRequestCooldownKeys", ref keys);
            dataStore.SyncData("WarPeace_PeaceRequestCooldownValues", ref values);
            if (dataStore.IsLoading && keys != null && values != null && keys.Count == values.Count)
            {
                PeaceRequestCooldowns.Clear();
                for (int i = 0; i < keys.Count; i++)
                {
                    var parts = keys[i].Split('|');
                    if (parts.Length == 2)
                    {
                        var key = (parts[0], parts[1]);
                        var time = CampaignTime.Hours((float) values[i]);
                        PeaceRequestCooldowns[key] = time;
                    }
                }
            }

            // Ensure initialization if loading
            if (dataStore.IsLoading)
            {
                if (Strategies == null)
                    Strategies = new Dictionary<string, KingdomStrategy>();
            }
        }

        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            // Clear the logger before we start mutating anything that might raise events
            Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI.WarPeaceLogger.Clear();
        }

        /// <summary>
        /// Gets or creates a strategy for the given kingdom
        /// </summary>
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

            // Get all valid AI kingdoms (exclude only if player is ruler)
            var aiKingdoms = Kingdom.All
                .Where(k => k != null
                    && !k.IsEliminated
                    && !k.IsMinorFaction
                    && k.Leader != null
                    && k.Leader != Hero.MainHero)
                .ToList();

            int kingdomCount = aiKingdoms.Count;
            if (kingdomCount == 0)
                return;

            // Pick a random number of kingdoms to make decisions (at least 1)
            int numToThink = Math.Min(3, kingdomCount); // Always process up to 3 kingdoms per day

            // Use deterministic selection based on day count to ensure rotation
            int dayOffset = (int) (CampaignTime.Now.ToDays % kingdomCount);
            var thinkingKingdoms = aiKingdoms
                .Skip(dayOffset)
                .Concat(aiKingdoms.Take(dayOffset)) // Wrap around for rotation
                .Take(numToThink)
                .ToList();

            foreach (var selectedKingdom in thinkingKingdoms)
            {
                ProcessKingdomDecisions(selectedKingdom);
            }
        }

        private void UpdateAllKingdomStrategies()
        {
            var allKingdoms = Kingdom.All
                .Where(k => k != null && !k.IsEliminated && !k.IsMinorFaction && k.Leader != null)
                .ToList();

            foreach (var kingdom in allKingdoms)
            {
                if (kingdom.Leader == Hero.MainHero) continue; // Skip player kingdom for strategy updates

                var strategy = GetKingdomStrategy(kingdom);
                if (strategy == null) continue;

                // Update stance toward each other kingdom
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

            // Log kingdom thinking
            LogKingdomThinking(selectedKingdom, strategy);

            // First, check for peace opportunities
            var peaceTargets = strategy.GetPeaceTargets(selectedKingdom);
            foreach (var target in peaceTargets.Take(2)) // Limit to top 2 peace candidates
            {
                ProcessPeaceDecision(selectedKingdom, target, strategy);
            }

            // Then, if not overwhelmed by wars, check for war opportunities
            var currentWars = KingdomLogicHelpers.GetEnemyKingdoms(selectedKingdom).Count;
            if (currentWars <= 1) // Don't start new wars if already in multiple conflicts
            {
                var warTargets = strategy.GetWarTargets(selectedKingdom);
                foreach (var target in warTargets.Take(1)) // Only consider one war target per day
                {
                    ProcessWarDecision(selectedKingdom, target, strategy);
                    break; // Only declare one war per day
                }
            }
        }

        private void ProcessPeaceDecision(Kingdom self, Kingdom target, KingdomStrategy strategy)
        {
            float stance = strategy.GetStance(target);

            // Check if decision is too soon ⬅️ ADD THIS  
            if (StrategicSafeguards.IsDecisionTooSoon(self, target, false))
                return; // Block peace declaration

            // Check for emergency coalition peace
            bool emergencyPeace = CoalitionSystem.ShouldForceEmergencyPeace(self, target);

            // Check for forced peace due to safeguards (forever war prevention) ⬅️ ADD THIS
            bool forcedPeace = StrategicSafeguards.ShouldForceWarEnd(self, target);

            // Strategy-based decision OR emergency coalition peace OR forced peace ⬅️ UPDATE THIS
            bool shouldMakePeace = stance <= KingdomStrategy.PEACE_THRESHOLD || emergencyPeace || forcedPeace;

            if (shouldMakePeace)
            {
                if (target.Leader == Hero.MainHero)
                {
                    // Send request to player
                    KingdomLogicHelpers.SendAIRequestToPlayerKingdom(self, target, "peace", stance);
                }
                else
                {
                    // Check if target also wants peace (mutual agreement)
                    var targetStrategy = GetKingdomStrategy(target);
                    float targetStance = targetStrategy?.GetStance(self) ?? 50f;

                    bool mutualPeace = targetStance <= KingdomStrategy.PEACE_THRESHOLD;

                    if (mutualPeace)
                    {
                        int dailyTribute = KingdomLogicHelpers.GetPeaceTribute(
                            self.Leader.Clan,
                            target.Leader.Clan,
                            self,
                            target
                        );

                        MakePeaceAction.Apply(self, target, dailyTribute);

                        // UPDATE THIS SECTION ⬅️
                        string reason;
                        if (emergencyPeace)
                            reason = "Emergency coalition peace - bigger threats require attention!";
                        else if (forcedPeace)
                            reason = $"Strategic wisdom demands peace: {StrategicSafeguards.GetForcedPeaceReason(self, target)}";
                        else
                            reason = $"Strategic assessment favors peace (stance: {stance:F0}%).";

                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{self.Name} made peace with {target.Name}. {reason}",
                            Colors.Green
                        ));
                    }
                }
            }
        }
        private void ProcessWarDecision(Kingdom self, Kingdom target, KingdomStrategy strategy)
        {
            float stance = strategy.GetStance(target);

            // Check if decision is too soon ⬅️ ADD THIS
            if (StrategicSafeguards.IsDecisionTooSoon(self, target, true))
                return; // Block war declaration

            bool isWarWise = StrategicSafeguards.IsWarDeclarationWise(self, target);
            bool shouldDeclareWar = stance >= KingdomStrategy.WAR_THRESHOLD && isWarWise;

            if (shouldDeclareWar)
            {
                DeclareWarAction.ApplyByDefault(self, target);

                string reason = $"Strategic analysis indicates war is favorable (stance: {stance:F0}%).";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{self.Name} declared war on {target.Name}. {reason}",
                    Colors.Red
                ));
            }
            // ADD THIS SECTION ⬅️
            else if (stance >= KingdomStrategy.WAR_THRESHOLD && !isWarWise)
            {
                // Log why war was blocked for debugging
                string blockedReason = StrategicSafeguards.GetWarBlockedReason(self, target);
                WarPeaceLogger.Log($"{self.Name} wanted war with {target.Name} (stance: {stance:F0}%) but was blocked: {blockedReason}");
            }
        }
        private void LogKingdomThinking(Kingdom kingdom, KingdomStrategy strategy)
        {
            if (kingdom == null || strategy == null) return;

            var enemies = KingdomLogicHelpers.GetEnemyKingdoms(kingdom);
            var allKingdoms = Kingdom.All
                .Where(k => k != kingdom && !k.IsEliminated && !k.IsMinorFaction && k.Leader != null)
                .ToList();

            var log = new System.Text.StringBuilder();
            log.AppendLine($"AI Strategy: {kingdom.Name} (ID: {kingdom.StringId})");
            log.AppendLine($"  TotalStrength: {kingdom.TotalStrength}");
            log.AppendLine($"  Settlements: {kingdom.Settlements.Count}");
            log.AppendLine($"  Current Wars: {enemies.Count}");
            log.AppendLine($"  Strategic Stances:");

            // Show stance toward each kingdom
            foreach (var target in allKingdoms.OrderByDescending(k => strategy.GetStance(k)))
            {
                float stance = strategy.GetStance(target);
                string status = kingdom.IsAtWarWith(target) ? "[WAR]" : "[PEACE]";
                string description = strategy.GetStanceDescription(target);

                // UPDATE THIS SECTION ⬅️
                // Show coalition influence
                float coalitionAdj = CoalitionSystem.GetCoalitionStanceAdjustment(kingdom, target);
                float safeguardAdj = StrategicSafeguards.GetSafeguardStanceAdjustment(kingdom, target);

                string modifiers = "";
                if (coalitionAdj != 0f) modifiers += $" Coalition:{coalitionAdj:+0.0;-0.0}";
                if (safeguardAdj != 0f) modifiers += $" Safeguard:{safeguardAdj:+0.0;-0.0}";

                log.AppendLine($"    {target.Name}: {stance:F1}% - {description} {status}{modifiers}");

                // UPDATE DECISION INDICATORS ⬅️
                if (kingdom.IsAtWarWith(target) && strategy.ShouldConsiderPeace(target))
                    log.AppendLine($"      → Considering peace");
                else if (!kingdom.IsAtWarWith(target) && strategy.ShouldConsiderWar(target))
                {
                    bool warWise = StrategicSafeguards.IsWarDeclarationWise(kingdom, target);
                    if (warWise)
                        log.AppendLine($"      → Considering war");
                    else
                        log.AppendLine($"      → Wants war but blocked: {StrategicSafeguards.GetWarBlockedReason(kingdom, target)}");
                }

                // Add emergency peace check
                if (kingdom.IsAtWarWith(target) && CoalitionSystem.ShouldForceEmergencyPeace(kingdom, target))
                    log.AppendLine($"      → EMERGENCY COALITION PEACE REQUIRED!");

                // ADD THIS ⬅️
                // Add forced peace check
                if (kingdom.IsAtWarWith(target) && StrategicSafeguards.ShouldForceWarEnd(kingdom, target))
                    log.AppendLine($"      → FORCED PEACE: {StrategicSafeguards.GetForcedPeaceReason(kingdom, target)}");
            }

            // Show top peace and war candidates
            var peaceTargets = strategy.GetPeaceTargets(kingdom);
            var warTargets = strategy.GetWarTargets(kingdom);

            if (peaceTargets.Any())
            {
                log.AppendLine($"  Peace Candidates: {string.Join(", ", peaceTargets.Select(k => k.Name.ToString()))}");
            }

            if (warTargets.Any())
            {
                log.AppendLine($"  War Candidates: {string.Join(", ", warTargets.Select(k => k.Name.ToString()))}");
            }

            WarPeaceLogger.Log(log.ToString());
        }

        /// <summary>
        /// Gets the stance of one kingdom toward another (for external access)
        /// </summary>
        public float GetKingdomStance(Kingdom self, Kingdom target)
        {
            var strategy = GetKingdomStrategy(self);
            return strategy?.GetStance(target) ?? 50f;
        }

        /// <summary>
        /// Manually adjusts the stance of one kingdom toward another (for events/modding)
        /// </summary>
        public void AdjustKingdomStance(Kingdom self, Kingdom target, float delta)
        {
            var strategy = GetKingdomStrategy(self);
            strategy?.AdjustStance(target, delta);
        }
    }
}