using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

using WarAndAiTweaks.Strategic.Decision;
using WarAndAiTweaks.Strategic.Diplomacy;
using WarAndAiTweaks.Strategic.Scoring;

namespace WarAndAiTweaks.Strategic
{
    public class StrategicConquestAI : CampaignBehaviorBase
    {
        private Dictionary<Kingdom, ConquestStrategy> _kingdomStrategies = new Dictionary<Kingdom, ConquestStrategy>();
        private Dictionary<Kingdom, CampaignTime> _lastDecisionTime = new Dictionary<Kingdom, CampaignTime>();

        // Modular components
        private RunawayFactionAnalyzer _runawayAnalyzer = new RunawayFactionAnalyzer();
        private WarScorer _warScorer;
        private PeaceScorer _peaceScorer;
        private PeaceNegotiationManager _peaceManager;
        private StrategicDecisionManager _decisionManager;

        public StrategicConquestAI()
        {
            _warScorer = new WarScorer(_runawayAnalyzer);
            _peaceScorer = new PeaceScorer(_runawayAnalyzer, _warScorer);
            _peaceManager = new PeaceNegotiationManager(_peaceScorer);
            _decisionManager = new StrategicDecisionManager(_warScorer, _peaceScorer, _peaceManager, _runawayAnalyzer);
        }

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
            _runawayAnalyzer?.SyncData(dataStore);
            _warScorer?.SyncData(dataStore);
            _peaceManager?.SyncData(dataStore);
        }

        private void OnDailyTick()
        {
            try
            {
                _runawayAnalyzer?.UpdateAnalysis();
                _peaceManager?.ProcessPeaceProposals();

                // FIXED: Add comprehensive null safety checks
                var validKingdoms = Kingdom.All?.Where(k => k != null && !k.IsEliminated &&
                         k.Leader != Hero.MainHero && k.RulingClan != null) ?? Enumerable.Empty<Kingdom>();

                foreach (var kingdom in validKingdoms)
                {
                    try
                    {
                        ProcessKingdomStrategy(kingdom);
                    }
                    catch (Exception ex)
                    {
                        // Log individual kingdom processing errors but continue with others
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"[Debug] Error processing strategy for {kingdom?.Name}: {ex.Message}",
                            Colors.Yellow));
                    }
                }
            }
            catch (Exception ex)
            {
                // Catch-all to prevent crashes from breaking the entire daily tick
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Daily tick error in StrategicConquestAI: {ex.Message}",
                    Colors.Red));
            }
        }

        private void OnWeeklyTick()
        {
            try
            {
                // FIXED: Add null safety checks
                var validKingdoms = Kingdom.All?.Where(k => k != null && !k.IsEliminated &&
                         k.Leader != Hero.MainHero) ?? Enumerable.Empty<Kingdom>();

                foreach (var kingdom in validKingdoms)
                {
                    try
                    {
                        UpdateKingdomStrategy(kingdom);
                    }
                    catch (Exception ex)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"[Debug] Error updating strategy for {kingdom?.Name}: {ex.Message}",
                            Colors.Yellow));
                    }
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Weekly tick error in StrategicConquestAI: {ex.Message}",
                    Colors.Red));
            }
        }

        private void ProcessKingdomStrategy(Kingdom kingdom)
        {
            if (kingdom?.RulingClan == null || !CanKingdomMakeStrategicDecisions(kingdom))
                return;

            if (_lastDecisionTime.TryGetValue(kingdom, out var lastTime) &&
                lastTime.ElapsedDaysUntilNow < 7f)
                return;

            var strategy = GetOrCreateStrategy(kingdom);
            if (strategy == null) return;

            // Check for runaway faction response first
            if (_runawayAnalyzer?.ShouldPrioritizeAntiRunawayActions(kingdom) == true)
            {
                if (HandleRunawayThreatResponse(kingdom, strategy))
                    return;
            }

            // Normal strategic considerations
            if (_decisionManager?.ShouldConsiderPeace(kingdom, strategy) == true)
            {
                _decisionManager.ConsiderPeaceDecisions(kingdom, strategy);
            }
            else if (_decisionManager?.ShouldConsiderWar(kingdom, strategy) == true)
            {
                _decisionManager.ConsiderWarDecisions(kingdom, strategy);
            }
        }

        private bool CanKingdomMakeStrategicDecisions(Kingdom kingdom)
        {
            // FIXED: Add null safety
            if (kingdom?.RulingClan == null || kingdom.IsEliminated)
                return false;

            // Base requirement: Kingdom must have some influence relative to its size
            float minimumInfluence = Math.Max(50f, kingdom.Fiefs.Count * 25f); // 50 base + 25 per fief

            if (kingdom.RulingClan.Influence < minimumInfluence)
                return false;

            // Allow weak kingdoms to make decisions in desperate situations
            if (kingdom.Fiefs.Count <= 3)
                return true; // Desperate survival mode

            // Allow decisions if under threat from runaway factions
            var threats = _runawayAnalyzer?.GetAllThreats(kingdom);
            if (threats?.Any() == true)
                return true; // Anti-runaway response mode

            return true; // Default: allow decisions
        }

        private bool HandleRunawayThreatResponse(Kingdom kingdom, ConquestStrategy strategy)
        {
            var biggestThreat = _runawayAnalyzer?.GetBiggestThreat(kingdom);
            if (biggestThreat == null) return false;

            var existingEnemies = FactionManager.GetEnemyKingdoms(biggestThreat)?.Count() ?? 0;
            if (existingEnemies > 0)
            {
                InitiateRunawayWarDeclaration(kingdom, biggestThreat, "Coalition against dominant faction");
                return true;
            }

            var currentEnemies = FactionManager.GetEnemyKingdoms(kingdom)?.Where(k => k != null).ToList();
            if (currentEnemies?.Any() == true && !currentEnemies.Contains(biggestThreat))
            {
                var weakestEnemy = currentEnemies.OrderBy(e => e?.TotalStrength ?? 0).FirstOrDefault();
                if (weakestEnemy != null)
                {
                    int tributeAmount = Math.Min(1000, (int) (kingdom.TotalStrength * 0.1f));
                    _peaceManager?.InitiatePeaceProposal(kingdom, weakestEnemy, tributeAmount);
                    return true;
                }
            }

            float combinedEnemyStrength = FactionManager.GetEnemyKingdoms(biggestThreat)?
                .Where(k => k != null)
                .Sum(e => e.TotalStrength) + kingdom.TotalStrength ?? kingdom.TotalStrength;

            if (combinedEnemyStrength > biggestThreat.TotalStrength * 0.8f)
            {
                InitiateRunawayWarDeclaration(kingdom, biggestThreat, "Pre-emptive strike against dominant faction");
                return true;
            }

            return false;
        }

        private void InitiateRunawayWarDeclaration(Kingdom kingdom, Kingdom threatKingdom, string reason)
        {
            var rulingClan = kingdom?.RulingClan;
            if (rulingClan == null || threatKingdom == null) return;

            if (kingdom.UnresolvedDecisions?.Any(d => d is DeclareWarDecision war &&
                war.FactionToDeclareWarOn == threatKingdom) == true)
                return;

            var warDecision = new DeclareWarDecision(rulingClan, threatKingdom);

            if (warDecision.CalculateSupport(rulingClan) > 30f)
            {
                kingdom.AddDecision(warDecision, true);
                _lastDecisionTime[kingdom] = CampaignTime.Now;

                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Runaway Response] {kingdom.Name} declares war on dominant {threatKingdom.Name} - {reason}",
                    Colors.Red));
            }
        }

        private ConquestStrategy GetOrCreateStrategy(Kingdom kingdom)
        {
            if (kingdom == null) return null;

            if (!_kingdomStrategies.TryGetValue(kingdom, out var strategy))
            {
                strategy = new ConquestStrategy(kingdom);
                _kingdomStrategies[kingdom] = strategy;
            }
            return strategy;
        }

        private void UpdateKingdomStrategy(Kingdom kingdom)
        {
            if (kingdom == null) return;

            var strategy = GetOrCreateStrategy(kingdom);
            strategy?.UpdateStrategy();
        }

        private void OnWarDeclared(IFaction side1, IFaction side2, DeclareWarAction.DeclareWarDetail detail)
        {
            if (side1 is Kingdom k1 && side2 is Kingdom k2)
            {
                _warScorer?.RecordWarStart(k1, k2);
                _warScorer?.RecordWarStart(k2, k1);
                _peaceManager?.OnWarDeclared(k1, k2);
            }

            if (side1 is Kingdom kingdom1) UpdateKingdomStrategy(kingdom1);
            if (side2 is Kingdom kingdom2) UpdateKingdomStrategy(kingdom2);
        }

        private void OnPeaceMade(IFaction side1, IFaction side2, MakePeaceAction.MakePeaceDetail detail)
        {
            if (side1 is Kingdom k1 && side2 is Kingdom k2)
            {
                _peaceManager?.OnPeaceMade(k1, k2);
            }

            if (side1 is Kingdom kingdom1) UpdateKingdomStrategy(kingdom1);
            if (side2 is Kingdom kingdom2) UpdateKingdomStrategy(kingdom2);
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            if (oldKingdom != null) UpdateKingdomStrategy(oldKingdom);
            if (newKingdom != null) UpdateKingdomStrategy(newKingdom);
        }
    }

    // PeaceProposal class for bilateral negotiation system
    public class PeaceProposal
    {
        public Kingdom Proposer { get; set; }
        public Kingdom Target { get; set; }
        public int TributeAmount { get; set; } // Positive = proposer pays, Negative = proposer receives
        public CampaignTime ProposalTime { get; set; }
        public bool IsPlayerInvolved { get; set; }
    }

    // ConquestStrategy class - main strategic planning logic
    public class ConquestStrategy
    {
        public Kingdom Kingdom { get; private set; }
        public List<Kingdom> PriorityTargets { get; private set; }
        public CampaignTime LastUpdate { get; private set; }

        // Territorial history tracking for proper loss detection
        private Dictionary<Kingdom, List<int>> _territorialHistory;

        public ConquestStrategy(Kingdom kingdom)
        {
            Kingdom = kingdom;
            PriorityTargets = new List<Kingdom>();
            _territorialHistory = new Dictionary<Kingdom, List<int>>();
            LastUpdate = CampaignTime.Now;
            UpdateStrategy();
        }

        public void UpdateStrategy()
        {
            // FIXED: Add null safety
            if (Kingdom == null || Kingdom.IsEliminated) return;

            try
            {
                UpdatePriorityTargets();
                UpdateTerritorialHistory();
                LastUpdate = CampaignTime.Now;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error updating strategy for {Kingdom?.Name}: {ex.Message}",
                    Colors.Yellow));
            }
        }

        private void UpdatePriorityTargets()
        {
            PriorityTargets.Clear();

            // FIXED: Add comprehensive null safety checks
            var allKingdoms = Kingdom.All?
                .Where(k => k != null && k != Kingdom && !k.IsEliminated && k.IsMapFaction)
                .OrderBy(k => k.TotalStrength)
                .ToList() ?? new List<Kingdom>();

            // Prioritize weak neighboring kingdoms
            var borderingKingdoms = allKingdoms.Where(IsBorderingKingdom).ToList();
            PriorityTargets.AddRange(borderingKingdoms.Take(3));

            // Add other weak kingdoms
            var otherTargets = allKingdoms.Except(PriorityTargets).Take(2);
            PriorityTargets.AddRange(otherTargets);
        }

        private void UpdateTerritorialHistory()
        {
            // FIXED: Add null safety checks
            if (Kingdom?.IsEliminated == true) return;

            // Update territorial history for all kingdoms we're tracking
            var currentEnemies = FactionManager.GetEnemyKingdoms(Kingdom)?
                .Where(k => k != null && !k.IsEliminated) ?? Enumerable.Empty<Kingdom>();

            foreach (var enemy in currentEnemies)
            {
                if (!_territorialHistory.ContainsKey(enemy))
                    _territorialHistory[enemy] = new List<int>();

                var history = _territorialHistory[enemy];
                history.Add(enemy.Fiefs.Count);

                // Keep only last 10 entries to prevent memory bloat
                if (history.Count > 10)
                    history.RemoveAt(0);
            }

            // Clean up history for kingdoms we're no longer at war with
            var historiesToRemove = _territorialHistory.Keys
                .Where(k => k == null || k.IsEliminated || !currentEnemies.Contains(k))
                .ToList();

            foreach (var kingdom in historiesToRemove)
            {
                _territorialHistory.Remove(kingdom);
            }
        }

        public bool IsBorderingKingdom(Kingdom other)
        {
            // FIXED: Add null safety checks
            if (other == null || Kingdom == null || other.IsEliminated || Kingdom.IsEliminated)
                return false;

            // Get all settlements from our kingdom
            var ourSettlements = Kingdom.Settlements;
            if (!ourSettlements.Any()) return false;

            // For each of our settlements, find the 5 nearest settlements and check if any belong to the target kingdom
            foreach (var ourSettlement in ourSettlements)
            {
                if (ourSettlement == null) continue;

                var nearestSettlements = Settlement.All?
                    .Where(s => s != null && s != ourSettlement && s.MapFaction != null) // Exclude our own settlement and ensure it has a faction
                    .OrderBy(s => ourSettlement.Position2D.Distance(s.Position2D))
                    .Take(5) // Get the 5 nearest settlements
                    .ToList() ?? new List<Settlement>();

                // Check if any of the 5 nearest settlements belong to the target kingdom
                if (nearestSettlements.Any(s => s.MapFaction == other))
                {
                    return true; // Found a border - the kingdoms are neighboring
                }
            }

            return false; // No borders found
        }

        public bool IsLosingTerritory(Kingdom enemy)
        {
            // Comprehensive null safety checks
            if (enemy == null || enemy.IsEliminated || Kingdom == null || Kingdom.IsEliminated)
                return false;

            // Ensure _territorialHistory is initialized
            if (_territorialHistory == null)
            {
                _territorialHistory = new Dictionary<Kingdom, List<int>>();
                return false;
            }

            if (!_territorialHistory.TryGetValue(enemy, out var history) || history == null)
                return false;

            if (history.Count < 2)
                return false;

            try
            {
                // Use Skip instead of TakeLast for .NET Framework 4.7.2 compatibility
                var recentCount = Math.Min(5, history.Count);
                var recent = history.Skip(Math.Max(0, history.Count - recentCount)).ToList();

                if (recent.Count < 2)
                    return false;

                return recent.Last() < recent.First();
            }
            catch (Exception ex)
            {
                // Log error and return safe default
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Territory loss check error for {Kingdom?.Name} vs {enemy?.Name}: {ex.Message}",
                    Colors.Yellow));
                return false;
            }
        }

        public bool HasBetterExpansionTargets()
        {
            try
            {
                // Add null safety checks
                if (Kingdom == null || Kingdom.IsEliminated)
                    return false;

                // Get current enemies with null safety
                var currentEnemies = FactionManager.GetEnemyKingdoms(Kingdom);
                if (currentEnemies == null)
                {
                    // If we can't get enemy kingdoms, assume no enemies and check all other kingdoms
                    var allPotentialTargets = Kingdom.All?
                        .Where(k => k != null && k != Kingdom && !k.IsEliminated && k.IsMapFaction) ??
                        Enumerable.Empty<Kingdom>();

                    return allPotentialTargets.Any(target =>
                        target != null && Kingdom.TotalStrength > target.TotalStrength * 2f);
                }

                // Convert to list to avoid multiple enumeration and filter out nulls
                var currentEnemiesList = currentEnemies.Where(k => k != null).ToList();

                var potentialTargets = Kingdom.All?
                    .Where(k => k != null && k != Kingdom && !k.IsEliminated &&
                           !currentEnemiesList.Contains(k) && k.IsMapFaction) ??
                    Enumerable.Empty<Kingdom>();

                return potentialTargets.Any(target =>
                    target != null && Kingdom.TotalStrength > target.TotalStrength * 2f);
            }
            catch (Exception ex)
            {
                // Log the error and return safe default
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error in HasBetterExpansionTargets for {Kingdom?.Name}: {ex.Message}",
                    Colors.Yellow));
                return false;
            }
        }

        public bool HasSuitableWarTargets()
        {
            try
            {
                // FIXED: Add comprehensive null safety check to prevent null reference exceptions
                if (Kingdom == null || Kingdom.IsEliminated)
                    return false;

                var potentialTargets = Kingdom.All?
                    .Where(k => k != null && k != Kingdom && !k.IsEliminated &&
                           !Kingdom.IsAtWarWith(k) && k.IsMapFaction)
                    .ToList() ?? new List<Kingdom>();

                if (!potentialTargets.Any()) return false;

                // Primary condition: Traditional strength advantage
                bool hasStrongTargets = potentialTargets.Any(target =>
                    target != null && Kingdom.TotalStrength > target.TotalStrength * 1.2f);

                if (hasStrongTargets) return true;

                // Secondary conditions for weaker kingdoms to find opportunities
                return HasOpportunisticTargets(potentialTargets);
            }
            catch (Exception ex)
            {
                // Log the error and return safe default
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error in HasSuitableWarTargets for {Kingdom?.Name}: {ex.Message}",
                    Colors.Yellow));
                return false;
            }
        }

        private bool HasOpportunisticTargets(List<Kingdom> potentialTargets)
        {
            try
            {
                if (potentialTargets == null || !potentialTargets.Any() || Kingdom == null)
                    return false;

                // Opportunity 1: Target kingdoms that are heavily engaged in multiple wars
                var overwhelmedTargets = potentialTargets.Where(target =>
                {
                    if (target == null) return false;

                    int targetWars = FactionManager.GetEnemyKingdoms(target)?.Count() ?? 0;
                    float targetStrengthRatio = Kingdom.TotalStrength / Math.Max(target.TotalStrength, 1f);

                    // If target is fighting 2+ wars, we need less strength advantage
                    if (targetWars >= 2 && targetStrengthRatio > 0.7f)
                        return true;

                    // If target is fighting 3+ wars, even weaker kingdoms can attack
                    if (targetWars >= 3 && targetStrengthRatio > 0.5f)
                        return true;

                    return false;
                }).Any();

                if (overwhelmedTargets) return true;

                // Opportunity 2: Target kingdoms that are losing territories (weakened)
                var weakeningTargets = potentialTargets.Where(target =>
                {
                    if (target == null) return false;

                    float strengthRatio = Kingdom.TotalStrength / Math.Max(target.TotalStrength, 1f);

                    // If target has lost settlements recently, they might be vulnerable
                    if (target.Fiefs.Count <= 3 && strengthRatio > 0.8f)
                        return true;

                    // If target is very small (1-2 fiefs), even much weaker kingdoms can try
                    if (target.Fiefs.Count <= 2 && strengthRatio > 0.6f)
                        return true;

                    return false;
                }).Any();

                if (weakeningTargets) return true;

                // Opportunity 3: Desperate expansion for very weak kingdoms
                if (Kingdom.Fiefs.Count <= 2)
                {
                    // Very weak kingdoms can attack anyone who isn't massively stronger
                    var desperateTargets = potentialTargets.Where(target =>
                    {
                        if (target == null) return false;

                        float strengthRatio = Kingdom.TotalStrength / Math.Max(target.TotalStrength, 1f);

                        // Don't attack kingdoms more than 3x stronger
                        if (strengthRatio < 0.33f) return false;

                        // Prefer targets that are also struggling
                        if (target.Fiefs.Count <= 4 && strengthRatio > 0.5f)
                            return true;

                        // Consider any target if we're desperate and not completely outmatched
                        return strengthRatio > 0.4f;
                    }).Any();

                    if (desperateTargets) return true;
                }

                // Opportunity 4: Coalition warfare - attack stronger kingdoms if they have many enemies
                var coalitionTargets = potentialTargets.Where(target =>
                {
                    if (target == null) return false;

                    int targetEnemies = FactionManager.GetEnemyKingdoms(target)?.Count() ?? 0;
                    float strengthRatio = Kingdom.TotalStrength / Math.Max(target.TotalStrength, 1f);

                    // If target has many enemies, we can join the pile-on even if individually weaker
                    if (targetEnemies >= 2)
                    {
                        // Calculate combined enemy strength vs target
                        float combinedEnemyStrength = (FactionManager.GetEnemyKingdoms(target)?
                            .Where(e => e != null)
                            .Sum(enemy => enemy.TotalStrength) ?? 0f) + Kingdom.TotalStrength;

                        float coalitionRatio = combinedEnemyStrength / Math.Max(target.TotalStrength, 1f);

                        // Join if coalition is strong enough and we're not too weak individually
                        return coalitionRatio > 1.5f && strengthRatio > 0.4f;
                    }

                    return false;
                }).Any();

                if (coalitionTargets) return true;

                // Opportunity 5: Geographical advantage - attack isolated or bordering weak kingdoms
                var geographicalTargets = potentialTargets.Where(target =>
                {
                    if (target == null) return false;

                    float strengthRatio = Kingdom.TotalStrength / Math.Max(target.TotalStrength, 1f);

                    // Only consider if not completely outmatched
                    if (strengthRatio < 0.5f) return false;

                    return false;
                }).Any();

                return geographicalTargets;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error in HasOpportunisticTargets for {Kingdom?.Name}: {ex.Message}",
                    Colors.Yellow));
                return false;
            }
        }

        public float CalculateStrategicValue(Kingdom target)
        {
            if (target == null || target.IsEliminated) return 0f;

            try
            {
                float value = 0f;

                // Value based on prosperity and number of settlements
                value += target.Settlements?.Sum(s => s?.IsTown == true ? s.Town?.Prosperity / 100f ?? 0f :
                                                      s?.IsVillage == true ? s.Village?.Hearth / 200f ?? 0f : 0f) ?? 0f;

                // Bonus for capitals and important strategic locations
                if (target.FactionMidSettlement != null && target.Settlements?.Contains(target.FactionMidSettlement) == true)
                    value += 50f;

                return value;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error calculating strategic value for {target?.Name}: {ex.Message}",
                    Colors.Yellow));
                return 0f;
            }
        }

        public bool WouldCreateStrategicAdvantage(Kingdom target)
        {
            if (target == null) return false;

            try
            {
                // Check if conquering this kingdom would create strategic advantages
                // such as unifying territories, controlling trade routes, etc.

                // Simplified: prefer targets that would connect our territories
                return IsBorderingKingdom(target);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error checking strategic advantage for {target?.Name}: {ex.Message}",
                    Colors.Yellow));
                return false;
            }
        }
    }
}