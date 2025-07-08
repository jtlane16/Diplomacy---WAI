// ╔══════════════════════════════════════════════════════════════════════════════════════════════════════════════════╗
// ║                                       STRATEGIC CONQUEST AI                                                     ║
// ║                                    Pure Orchestrator and Executor                                               ║
// ╚══════════════════════════════════════════════════════════════════════════════════════════════════════════════════╝
//
//  This file contains ONLY orchestration logic for the Bannerlord Strategic AI:
//  • Event handling and coordination
//  • High-level decision flow management  
//  • Component initialization and lifecycle
//  • Strategy state management
//  
//  ALL strategic calculations are delegated to StrategicEngine.cs
//  This separation makes the system much easier to maintain and debug.
//
// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

using WarAndAiTweaks.Strategic.Decision;
using WarAndAiTweaks.Strategic.Diplomacy;
using WarAndAiTweaks.Strategic.Scoring;

namespace WarAndAiTweaks.Strategic
{
    /// <summary>
    /// Main AI orchestrator that coordinates all strategic decision making.
    /// Delegates all calculations to StrategicEngine components and focuses on high-level flow control.
    /// </summary>
    public class StrategicConquestAI : CampaignBehaviorBase
    {
        #region 💾 Persistent State

        [SaveableField(1)]
        private Dictionary<Kingdom, ConquestStrategy> _kingdomStrategies = new Dictionary<Kingdom, ConquestStrategy>();

        [SaveableField(2)]
        private Dictionary<Kingdom, CampaignTime> _lastDecisionTime = new Dictionary<Kingdom, CampaignTime>();

        #endregion

        #region 🧠 Strategic Engine Components

        // Core analysis engine
        private RunawayFactionAnalyzer _runawayAnalyzer;
        private WarScorer _warScorer;
        private PeaceScorer _peaceScorer;
        private StrategicAnalyzer _strategicAnalyzer;

        // Decision execution components
        private PeaceNegotiationManager _peaceManager;
        private StrategicDecisionManager _decisionManager;

        #endregion

        #region 🏗️ Initialization

        public StrategicConquestAI()
        {
            InitializeStrategicEngine();
        }

        /// <summary>
        /// Initialize all strategic engine components with proper dependency injection
        /// </summary>
        private void InitializeStrategicEngine()
        {
            // Core analyzers
            _runawayAnalyzer = new RunawayFactionAnalyzer();
            _warScorer = new WarScorer(_runawayAnalyzer);
            _peaceScorer = new PeaceScorer(_runawayAnalyzer, _warScorer);

            // Central strategic intelligence
            _strategicAnalyzer = new StrategicAnalyzer(_warScorer, _peaceScorer, _runawayAnalyzer);

            // Decision execution managers
            _peaceManager = new PeaceNegotiationManager(_peaceScorer);
            _decisionManager = new StrategicDecisionManager(_warScorer, _peaceScorer, _peaceManager, _runawayAnalyzer);
        }

        #endregion

        #region 📅 Campaign Event Management

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnPeaceMade);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_kingdomStrategies", ref _kingdomStrategies);
            dataStore.SyncData("_lastDecisionTime", ref _lastDecisionTime);

            // Sync strategic engine data
            _runawayAnalyzer?.SyncData(dataStore);
            _warScorer?.SyncData(dataStore);
            _peaceManager?.SyncData(dataStore);
        }

        #endregion

        #region 📊 Main Processing Loop

        /// <summary>
        /// Daily processing: maintenance tasks and strategic decision making
        /// </summary>
        private void OnDailyTick()
        {
            try
            {
                ExecuteMaintenanceTasks();
                ProcessAllKingdomStrategies();
            }
            catch (Exception ex)
            {
                LogError($"Daily tick error in StrategicConquestAI: {ex.Message}");
            }
        }

        /// <summary>
        /// Weekly processing: strategy updates and long-term planning
        /// </summary>
        private void OnWeeklyTick()
        {
            try
            {
                var validKingdoms = GetValidKingdoms();
                foreach (var kingdom in validKingdoms)
                {
                    ExecuteSafeAction(() => UpdateKingdomStrategy(kingdom),
                        $"Error updating strategy for {kingdom?.Name}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Weekly tick error in StrategicConquestAI: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute core maintenance tasks that run every day
        /// </summary>
        private void ExecuteMaintenanceTasks()
        {
            // Update runaway faction analysis
            _runawayAnalyzer?.UpdateAnalysis();

            // Process ongoing peace negotiations
            _peaceManager?.ProcessPeaceProposals();

            // Update war weariness for all kingdom pairs
            UpdateGlobalWarWeariness();
        }

        /// <summary>
        /// Process strategic decisions for all valid kingdoms
        /// </summary>
        private void ProcessAllKingdomStrategies()
        {
            var validKingdoms = GetValidKingdoms();
            foreach (var kingdom in validKingdoms)
            {
                ExecuteSafeAction(() => ProcessKingdomStrategy(kingdom),
                    $"Error processing strategy for {kingdom?.Name}");
            }
        }

        #endregion

        #region 🎯 Strategic Decision Orchestration

        /// <summary>
        /// MAIN STRATEGIC DECISION FLOW - Orchestrates all strategic calculations
        /// All complex logic is delegated to StrategicAnalyzer
        /// </summary>
        private void ProcessKingdomStrategy(Kingdom kingdom)
        {
            if (kingdom?.RulingClan == null) return;

            // Gate 1: Can this kingdom make strategic decisions?
            if (!_strategicAnalyzer.CanMakeStrategicDecisions(kingdom)) return;

            // Gate 2: Is it time for new decisions?
            if (!_strategicAnalyzer.ShouldConsiderNewDecisions(kingdom, GetLastDecisionTime(kingdom))) return;

            var strategy = GetOrCreateStrategy(kingdom);
            if (strategy == null) return;

            // Decision Flow: Prioritize based on strategic situation
            if (_strategicAnalyzer.ShouldPrioritizeAntiRunaway(kingdom))
            {
                ExecuteRunawayResponse(kingdom, strategy);
            }
            else if (_strategicAnalyzer.ShouldConsiderPeace(kingdom, strategy))
            {
                ExecutePeaceConsideration(kingdom, strategy);
            }
            else if (_strategicAnalyzer.ShouldConsiderWar(kingdom))
            {
                ExecuteWarConsideration(kingdom, strategy);
            }

            // Record that we made a decision
            _lastDecisionTime[kingdom] = CampaignTime.Now;
        }

        /// <summary>
        /// Execute anti-runaway faction response
        /// </summary>
        private void ExecuteRunawayResponse(Kingdom kingdom, ConquestStrategy strategy)
        {
            var biggestThreat = _runawayAnalyzer?.GetBiggestThreat(kingdom);
            if (biggestThreat == null) return;

            // Strategy 1: Join existing coalition against the threat
            if (TryJoinAntiRunawayCoalition(kingdom, biggestThreat)) return;

            // Strategy 2: Make peace with weaker enemies to focus on the threat  
            if (TryConsolidateAgainstThreat(kingdom, biggestThreat)) return;

            // Strategy 3: Launch preemptive strike if coalition is strong enough
            TryPreemptiveStrike(kingdom, biggestThreat);
        }

        /// <summary>
        /// Execute peace consideration flow
        /// </summary>
        private void ExecutePeaceConsideration(Kingdom kingdom, ConquestStrategy strategy)
        {
            _decisionManager?.ConsiderPeaceDecisions(kingdom, strategy);
        }

        /// <summary>
        /// Execute war consideration flow
        /// </summary>
        private void ExecuteWarConsideration(Kingdom kingdom, ConquestStrategy strategy)
        {
            _decisionManager?.ConsiderWarDecisions(kingdom, strategy);
        }

        #endregion

        #region 🚨 Runaway Faction Response Tactics

        /// <summary>
        /// Try to join an existing coalition against a runaway threat
        /// </summary>
        private bool TryJoinAntiRunawayCoalition(Kingdom kingdom, Kingdom threat)
        {
            var existingEnemies = FactionManager.GetEnemyKingdoms(threat)?.Count() ?? 0;
            if (existingEnemies > 0)
            {
                InitiateRunawayWarDeclaration(kingdom, threat, "Coalition against dominant faction");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Try to make peace with weaker enemies to focus on the runaway threat
        /// </summary>
        private bool TryConsolidateAgainstThreat(Kingdom kingdom, Kingdom threat)
        {
            var currentEnemies = FactionManager.GetEnemyKingdoms(kingdom)?
                .Where(k => k != null && k != threat).ToList();

            if (currentEnemies?.Any() == true)
            {
                var weakestEnemy = currentEnemies.OrderBy(e => e?.TotalStrength ?? 0).FirstOrDefault();
                if (weakestEnemy != null)
                {
                    int tributeAmount = Math.Min(1000, (int) (kingdom.TotalStrength * 0.1f));
                    _peaceManager?.InitiatePeaceProposal(kingdom, weakestEnemy, tributeAmount);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Try to launch a preemptive strike against the runaway threat
        /// </summary>
        private void TryPreemptiveStrike(Kingdom kingdom, Kingdom threat)
        {
            // Calculate combined coalition strength
            float combinedEnemyStrength = (FactionManager.GetEnemyKingdoms(threat)?
                .Where(k => k != null)
                .Sum(e => e.TotalStrength) ?? 0f) + kingdom.TotalStrength;

            // Only attack if coalition is strong enough
            if (combinedEnemyStrength > threat.TotalStrength * 0.8f)
            {
                InitiateRunawayWarDeclaration(kingdom, threat, "Pre-emptive strike against dominant faction");
            }
        }

        /// <summary>
        /// Execute a war declaration against a runaway threat
        /// </summary>
        private void InitiateRunawayWarDeclaration(Kingdom kingdom, Kingdom threatKingdom, string reason)
        {
            var rulingClan = kingdom?.RulingClan;
            if (rulingClan == null || threatKingdom == null) return;

            // Check if war declaration already pending
            if (kingdom.UnresolvedDecisions?.Any(d => d is DeclareWarDecision war &&
                war.FactionToDeclareWarOn == threatKingdom) == true)
                return;

            var warDecision = new DeclareWarDecision(rulingClan, threatKingdom);

            // Only proceed if the decision has sufficient support
            if (warDecision.CalculateSupport(rulingClan) > 30f)
            {
                kingdom.AddDecision(warDecision, true);

                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Runaway Response] {kingdom.Name} declares war on dominant {threatKingdom.Name} - {reason}",
                    Colors.Red));
            }
        }

        #endregion

        #region 🏰 Strategy State Management

        /// <summary>
        /// Get or create a conquest strategy for a kingdom
        /// </summary>
        private ConquestStrategy GetOrCreateStrategy(Kingdom kingdom)
        {
            if (kingdom == null) return null;

            if (!_kingdomStrategies.TryGetValue(kingdom, out var strategy))
            {
                // Inject strategic analyzer for calculation delegation
                strategy = new ConquestStrategy(kingdom, _strategicAnalyzer);
                _kingdomStrategies[kingdom] = strategy;
            }
            return strategy;
        }

        /// <summary>
        /// Update the strategic plan for a kingdom
        /// </summary>
        private void UpdateKingdomStrategy(Kingdom kingdom)
        {
            if (kingdom == null) return;

            var strategy = GetOrCreateStrategy(kingdom);
            strategy?.UpdateStrategy();
        }

        #endregion

        #region 📅 Event Response Handlers

        /// <summary>
        /// Handle war declaration events
        /// </summary>
        private void OnWarDeclared(IFaction side1, IFaction side2, DeclareWarAction.DeclareWarDetail detail)
        {
            // Update war tracking systems
            if (side1 is Kingdom k1 && side2 is Kingdom k2)
            {
                _warScorer?.RecordWarStart(k1, k2);
                _warScorer?.RecordWarStart(k2, k1);
                _peaceManager?.OnWarDeclared(k1, k2);
                _warScorer?.UpdateWarWeariness(k1, k2, true);
            }

            // Update strategies for affected kingdoms
            if (side1 is Kingdom kingdom1) UpdateKingdomStrategy(kingdom1);
            if (side2 is Kingdom kingdom2) UpdateKingdomStrategy(kingdom2);
        }

        /// <summary>
        /// Handle peace treaty events
        /// </summary>
        private void OnPeaceMade(IFaction side1, IFaction side2, MakePeaceAction.MakePeaceDetail detail)
        {
            // Update peace tracking systems
            if (side1 is Kingdom k1 && side2 is Kingdom k2)
            {
                _peaceManager?.OnPeaceMade(k1, k2);
                _warScorer?.UpdateWarWeariness(k1, k2, false);
            }

            // Update strategies for affected kingdoms
            if (side1 is Kingdom kingdom1) UpdateKingdomStrategy(kingdom1);
            if (side2 is Kingdom kingdom2) UpdateKingdomStrategy(kingdom2);
        }

        /// <summary>
        /// Handle clan defection events
        /// </summary>
        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            // Update strategies for affected kingdoms
            if (oldKingdom != null) UpdateKingdomStrategy(oldKingdom);
            if (newKingdom != null) UpdateKingdomStrategy(newKingdom);
        }

        #endregion

        #region 🔧 Utility Methods

        /// <summary>
        /// Update war weariness for all kingdom pairs
        /// </summary>
        private void UpdateGlobalWarWeariness()
        {
            var kingdoms = Kingdom.All?.Where(k => k != null && !k.IsEliminated) ?? Enumerable.Empty<Kingdom>();

            foreach (var k1 in kingdoms)
            {
                foreach (var k2 in kingdoms)
                {
                    if (k1 == k2) continue;

                    // Only update each pair once (avoid duplicate work)
                    if (k1.GetHashCode() > k2.GetHashCode()) continue;

                    bool atWar = k1.IsAtWarWith(k2);
                    _warScorer?.UpdateWarWeariness(k1, k2, atWar);
                }
            }
        }

        /// <summary>
        /// Get all kingdoms that can participate in strategic decision making
        /// </summary>
        private IEnumerable<Kingdom> GetValidKingdoms()
        {
            return Kingdom.All?.Where(k => k != null && !k.IsEliminated &&
                   k.Leader != Hero.MainHero && k.RulingClan != null) ?? Enumerable.Empty<Kingdom>();
        }

        /// <summary>
        /// Get the last decision time for a kingdom
        /// </summary>
        private CampaignTime GetLastDecisionTime(Kingdom kingdom)
        {
            return _lastDecisionTime.TryGetValue(kingdom, out var time) ? time : CampaignTime.Never;
        }

        /// <summary>
        /// Execute an action safely with error handling
        /// </summary>
        private void ExecuteSafeAction(Action action, string errorContext)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogError($"{errorContext}: {ex.Message}");
            }
        }

        /// <summary>
        /// Log error messages in a consistent format
        /// </summary>
        private static void LogError(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                $"[Debug] {message}", Colors.Yellow));
        }

        #endregion
    }
}