using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

using MathF = TaleWorlds.Library.MathF;

namespace TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors
{
    public class ArmyCoordinationManager : CampaignBehaviorBase
    {
        // Simple objective management - lean and focused
        private Dictionary<IFaction, List<StrategicObjective>> _factionObjectives;
        private Dictionary<IFaction, CampaignTime> _lastObjectiveUpdate;

        private const int MAX_OBJECTIVES_PER_KINGDOM = 2; // Keep it simple - 3 max objectives

        public ArmyCoordinationManager()
        {
            // CRITICAL: Initialize collections to prevent null reference exceptions
            try
            {
                _factionObjectives = new Dictionary<IFaction, List<StrategicObjective>>();
                _lastObjectiveUpdate = new Dictionary<IFaction, CampaignTime>();
            }
            catch (Exception ex)
            {
                // Fallback initialization
                _factionObjectives = new Dictionary<IFaction, List<StrategicObjective>>();
                _lastObjectiveUpdate = new Dictionary<IFaction, CampaignTime>();

                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] ArmyCoordinationManager constructor error: {ex.Message}",
                    Colors.Yellow));
            }
        }

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.ArmyCreated.AddNonSerializedListener(this, OnArmyCreated);
            CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, OnArmyDispersed);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_factionObjectives", ref _factionObjectives);
            dataStore.SyncData("_lastObjectiveUpdate", ref _lastObjectiveUpdate);
        }

        private void OnHourlyTick()
        {
            // Update objectives every 6 hours
            if (CampaignTime.Now.ToHours % 6 < 1f)
            {
                UpdateObjectives();
            }
        }

        private void OnArmyCreated(Army army)
        {
            if (army?.LeaderParty?.MapFaction != null)
            {
                AssignArmyToObjective(army);
            }
        }

        private void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy)
        {
            if (army?.LeaderParty?.MapFaction != null)
            {
                RemoveArmyFromObjectives(army);
            }
        }

        private void UpdateObjectives()
        {
            foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
            {
                UpdateObjectivesForKingdom(kingdom);
            }
        }

        private void UpdateObjectivesForKingdom(Kingdom kingdom)
        {
            // Check if we need to update
            if (_lastObjectiveUpdate.TryGetValue(kingdom, out var lastUpdate) &&
                lastUpdate.ElapsedHoursUntilNow < 6f) return;

            if (!_factionObjectives.ContainsKey(kingdom))
            {
                _factionObjectives[kingdom] = new List<StrategicObjective>();
            }

            var objectives = _factionObjectives[kingdom];

            // Remove completed or expired objectives
            objectives.RemoveAll(obj => IsObjectiveCompleted(obj) || obj.ExpirationTime < CampaignTime.Now);

            // Remove excess armies from objectives (rebalance)
            RebalanceArmyAssignments(kingdom, objectives);

            // UPDATED: Check if we need new objectives based on type-specific limits
            bool needsNewOffensiveObjectives = objectives.Count(obj => obj.Type == ObjectiveType.Capture) < 2;
            bool needsNewDefensiveObjectives = true; // Always check for new defensive needs

            if (needsNewOffensiveObjectives || needsNewDefensiveObjectives)
            {
                CreateNewObjectives(kingdom, objectives);
            }

            _lastObjectiveUpdate[kingdom] = CampaignTime.Now;
        }

        private void CreateNewObjectives(Kingdom kingdom, List<StrategicObjective> currentObjectives)
        {
            var enemyFactions = FactionManager.GetEnemyFactions(kingdom);
            var candidates = new List<ObjectiveCandidate>();

            // Find offensive targets - limit to 2 best siege targets
            foreach (IFaction enemy in enemyFactions)
            {
                foreach (Settlement settlement in enemy.Settlements.Where(s => s.IsFortification))
                {
                    // Skip if our system is already targeting this settlement
                    if (currentObjectives.Any(obj => obj.TargetSettlement == settlement))
                        continue;

                    // --- THIS IS THE FIX ---
                    // Skip if the settlement is under siege by ANYONE.
                    if (settlement.SiegeEvent != null)
                        continue;

                    float priority = CalculateOffensivePriority(settlement, kingdom);
                    if (priority > 0.5f) // Minimum threshold
                    {
                        candidates.Add(new ObjectiveCandidate
                        {
                            Settlement = settlement,
                            Type = ObjectiveType.Capture,
                            Priority = priority
                        });
                    }
                }
            }

            // Take only the top 2 offensive targets
            var topOffensiveTargets = candidates
                .Where(c => c.Type == ObjectiveType.Capture)
                .OrderByDescending(c => c.Priority)
                .Take(2);

            // Clear candidates and add selected offensive targets
            candidates.Clear();
            candidates.AddRange(topOffensiveTargets);

            // Find ALL defensive targets that need defending
            foreach (Settlement settlement in kingdom.Settlements.Where(s => s.IsFortification))
            {
                // Skip if already defending this settlement
                if (currentObjectives.Any(obj => obj.TargetSettlement == settlement && obj.Type == ObjectiveType.Defend))
                    continue;

                float threatLevel = CalculateThreatLevel(settlement);
                if (threatLevel > 500f) // Minimum threat threshold
                {
                    float priority = CalculateDefensivePriority(settlement, kingdom, threatLevel);
                    candidates.Add(new ObjectiveCandidate
                    {
                        Settlement = settlement,
                        Type = ObjectiveType.Defend,
                        Priority = priority
                    });
                }
            }

            var allSelectedCandidates = candidates
                .OrderByDescending(c => c.Priority);

            foreach (var candidate in allSelectedCandidates)
            {
                var objective = new StrategicObjective
                {
                    Type = candidate.Type,
                    TargetSettlement = candidate.Settlement, // Corrected from candidate.Settlement
                    Priority = candidate.Priority,
                    RequiredStrength = CalculateRequiredStrength(candidate.Settlement, candidate.Type),
                    ExpirationTime = CampaignTime.HoursFromNow(candidate.Type == ObjectiveType.Defend ? 48f : 72f),
                    AssignedArmies = new List<Army>()
                };

                currentObjectives.Add(objective);
            }
        }

        private void AssignArmyToObjective(Army army)
        {
            if (army?.LeaderParty?.MapFaction == null)
                return;

            // Ensure _factionObjectives is initialized
            if (_factionObjectives == null)
            {
                _factionObjectives = new Dictionary<IFaction, List<StrategicObjective>>();
                return;
            }

            if (!_factionObjectives.TryGetValue(army.LeaderParty.MapFaction, out var objectives))
                return;

            if (objectives == null)
                return;

            try
            {
                var bestObjective = FindBestObjectiveForArmy(army, objectives);

                if (bestObjective?.AssignedArmies != null)
                {
                    bestObjective.AssignedArmies.Add(army);
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error assigning army to objective: {ex.Message}",
                    Colors.Yellow));
            }
        }

        private StrategicObjective FindBestObjectiveForArmy(Army army, List<StrategicObjective> objectives)
        {
            if (army?.LeaderParty == null) return null;

            return objectives
                .Where(obj => !IsObjectiveCompleted(obj))
                .Where(obj => IsArmySuitableForObjective(army, obj))
                .OrderBy(obj => obj.Type == ObjectiveType.Defend ? 0 : 1) // Prioritize defense first
                .ThenByDescending(obj => obj.Priority) // Then by priority
                .ThenBy(obj => Campaign.Current.Models.MapDistanceModel.GetDistance(army.LeaderParty, obj.TargetSettlement)) // Then by distance
                .FirstOrDefault();
        }

        private bool IsArmySuitableForObjective(Army army, StrategicObjective objective)
        {
            if (army?.LeaderParty == null || objective?.TargetSettlement == null) return false;

            // Distance check
            float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(army.LeaderParty, objective.TargetSettlement);
            float maxDistance = objective.Type == ObjectiveType.Defend ? 150f : 400f;

            return distance <= maxDistance;
        }

        private void RebalanceArmyAssignments(Kingdom kingdom, List<StrategicObjective> objectives)
        {
            // Remove armies that are no longer suitable or have been dispersed
            foreach (var objective in objectives)
            {
                objective.AssignedArmies.RemoveAll(army =>
                    army == null ||
                    army.LeaderParty == null ||
                    !IsArmySuitableForObjective(army, objective));
            }

            // Reassign unassigned armies
            var allArmies = kingdom.Armies.ToList();
            var assignedArmies = objectives.SelectMany(obj => obj.AssignedArmies).ToHashSet();
            var unassignedArmies = allArmies.Where(army => !assignedArmies.Contains(army)).ToList();

            foreach (var army in unassignedArmies)
            {
                var bestObjective = FindBestObjectiveForArmy(army, objectives);
                if (bestObjective != null)
                {
                    bestObjective.AssignedArmies.Add(army);
                }
            }
        }

        private void RemoveArmyFromObjectives(Army army)
        {
            // Add comprehensive null safety checks
            if (army?.LeaderParty?.MapFaction == null)
                return;

            // Ensure _factionObjectives is initialized
            if (_factionObjectives == null)
            {
                _factionObjectives = new Dictionary<IFaction, List<StrategicObjective>>();
                return;
            }

            // Check if faction exists in objectives
            if (!_factionObjectives.TryGetValue(army.LeaderParty.MapFaction, out var objectives))
                return;

            // Ensure objectives list is not null
            if (objectives == null)
                return;

            // Safely remove army from all objectives
            try
            {
                foreach (var objective in objectives)
                {
                    if (objective?.AssignedArmies != null)
                    {
                        objective.AssignedArmies.Remove(army);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] Error removing army from objectives: {ex.Message}",
                    Colors.Yellow));
            }
        }

        private bool IsObjectiveCompleted(StrategicObjective objective)
        {
            if (objective?.TargetSettlement == null) return true;

            switch (objective.Type)
            {
                case ObjectiveType.Capture:
                    // Completed if we captured it or someone else is already sieging it
                    return objective.TargetSettlement.MapFaction == objective.AssignedArmies.FirstOrDefault()?.LeaderParty?.MapFaction ||
                           objective.TargetSettlement.SiegeEvent != null;

                case ObjectiveType.Defend:
                    // Completed if threat level dropped significantly
                    float currentThreat = CalculateThreatLevel(objective.TargetSettlement);
                    return currentThreat < 300f; // Threshold for "safe"

                default:
                    return false;
            }
        }

        // Strategic objective bonus system
        // Strategic objective bonus system
        public float GetCoordinationBonus(MobileParty party, Settlement targetSettlement, Army.ArmyTypes missionType)
        {
            // FIXED: Add comprehensive null safety checks to prevent crashes
            if (party?.MapFaction == null)
                return 1f;

            // Ensure _factionObjectives is initialized
            if (_factionObjectives == null)
            {
                _factionObjectives = new Dictionary<IFaction, List<StrategicObjective>>();
                return 1f;
            }

            // Check if faction exists and get objectives safely
            if (!_factionObjectives.TryGetValue(party.MapFaction, out var objectives) || objectives == null)
                return 1f;

            try
            {
                var relevantObjective = objectives.FirstOrDefault(obj =>
                    obj?.TargetSettlement == targetSettlement &&
                    IsObjectiveTypeCompatible(obj.Type, missionType));

                if (relevantObjective != null)
                {
                    // Check if this army is assigned to this objective
                    bool isAssigned = party.Army != null &&
                                     relevantObjective.AssignedArmies != null &&
                                     relevantObjective.AssignedArmies.Contains(party.Army);

                    if (isAssigned)
                    {
                        // Provide coordination bonus based on priority
                        float priorityBonus = 1f + (relevantObjective.Priority * 0.3f);
                        return Math.Min(priorityBonus, 2f); // Cap at 2x bonus
                    }
                    else
                    {
                        // Small bonus for relevant but unassigned objectives
                        return 1.1f;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error and return safe default
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Debug] GetCoordinationBonus error: {ex.Message}",
                    Colors.Yellow));
            }

            return 1f;
        }

        private bool IsObjectiveTypeCompatible(ObjectiveType objType, Army.ArmyTypes missionType)
        {
            return (objType == ObjectiveType.Capture && missionType == Army.ArmyTypes.Besieger) ||
                   (objType == ObjectiveType.Defend && missionType == Army.ArmyTypes.Defender);
        }

        // Army creation guidance
        public bool ShouldCreateArmy(MobileParty potentialLeader, out StrategicObjective recommendedObjective)
        {
            recommendedObjective = null;

            if (potentialLeader?.MapFaction == null || !potentialLeader.MapFaction.IsKingdomFaction)
                return false;

            Kingdom kingdom = potentialLeader.MapFaction as Kingdom;
            if (kingdom == null) return false;

            if (!_factionObjectives.ContainsKey(kingdom))
                return false;

            var objectives = _factionObjectives[kingdom];

            // FIXED: Remove strength requirement check to allow unlimited overcommitment
            var availableObjective = objectives
                .Where(obj => !IsObjectiveCompleted(obj))
                .Where(obj => obj.Type == ObjectiveType.Defend ?
                    GetAssignedStrength(obj) < obj.RequiredStrength * 0.8f : // Still check defensive needs
                    true) // BUT allow unlimited offensive overcommitment
                .OrderBy(obj => obj.Type == ObjectiveType.Defend ? 0 : 1) // Defend first, then capture
                .ThenByDescending(obj => obj.Priority)
                .FirstOrDefault();

            if (availableObjective != null)
            {
                // Check if this potential leader can contribute meaningfully
                float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(potentialLeader, availableObjective.TargetSettlement);
                float maxDistance = availableObjective.Type == ObjectiveType.Defend ? 200f : 400f;

                if (distance <= maxDistance)
                {
                    recommendedObjective = availableObjective;
                    return true;
                }
            }

            return false;
        }

        private float GetAssignedStrength(StrategicObjective objective)
        {
            return objective.AssignedArmies
                .Where(army => army?.LeaderParty != null)
                .Sum(army => army.TotalStrength);
        }

        // Calculation methods
        private float CalculateOffensivePriority(Settlement settlement, Kingdom kingdom)
        {
            // Start with a base priority on settlement type.
            float priority = settlement.IsTown ? 1.0f : 0.6f;

            // 1. VULNERABILITY: Prioritize targets that are weak *relative to us*.
            float defenderStrength = CalculateSettlementDefensiveStrength(settlement);

            // Calculate the average strength of our own lords' parties to set a baseline for what we consider "strong".
            float averageLordStrength = kingdom.Lords
                .Where(l => l.IsAlive && l.PartyBelongedTo != null)
                .Select(l => l.PartyBelongedTo.Party.TotalStrength)
                .DefaultIfEmpty(300f) // Fallback for kingdoms with no lords
                .Average();

            // The multiplier is now based on our own average party strength.
            // A target with half our average strength is highly attractive.
            priority *= (averageLordStrength / MathF.Max(1f, defenderStrength));

            // 2. STRATEGIC BONUS: Add a simple bonus for being a frontier settlement.
            // Corrected to use BoundSettlements as you pointed out.
            if (settlement.BoundVillages.Any(n => n.Settlement.MapFaction != null && n.Settlement.MapFaction.IsAtWarWith(kingdom)))
            {
                priority += 0.5f;
            }

            // 3. DYNAMIC DISTANCE PENALTY: Discourage over-extending based on our kingdom's size.
            float kingdomRadius = CalculateAverageKingdomFiefDistance(kingdom);
            float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(kingdom.FactionMidSettlement, settlement);

            // We only start penalizing targets that are further than our average fief distance.
            if (distance > kingdomRadius)
            {
                // The penalty scales with how far beyond our "border" the target is.
                priority *= MathF.Max(0.2f, 1f - (distance - kingdomRadius) / (kingdomRadius * 1.5f));
            }

            return MathF.Max(0.1f, priority);
        }

        /// <summary>
        /// Calculates the average distance of a kingdom's fiefs from its geographical center.
        /// This provides a dynamic "radius" for the kingdom's operational range.
        /// </summary>
        private float CalculateAverageKingdomFiefDistance(Kingdom kingdom)
        {
            if (kingdom.Fiefs.Count <= 1)
            {
                return 250f; // Default operational range for a kingdom with one or zero fiefs.
            }

            Settlement center = kingdom.FactionMidSettlement;
            if (center == null)
            {
                return 250f; // Fallback if a center can't be determined.
            }

            // Calculate the average distance of all fiefs from the central point.
            float averageDistance = kingdom.Fiefs
                .Select(f => f.Settlement)
                .Average(s => Campaign.Current.Models.MapDistanceModel.GetDistance(center, s));

            // We return a value slightly larger than the average to define a reasonable "border".
            return averageDistance * 1.2f;
        }

        private float CalculateDefensivePriority(Settlement settlement, Kingdom kingdom, float threatLevel)
        {
            float priority = 1f; // Base defensive priority

            if (settlement.IsTown) priority += 0.8f;

            // FIXED: Scale prosperity instead of hard-coded 8000f threshold
            if (settlement.Town?.Prosperity > 0f)
            {
                float prosperityRatio = settlement.Town.Prosperity / 12000f; // Scale 0-12k prosperity to 0-1
                priority += prosperityRatio * 0.4f; // Convert to 0-0.4 bonus
            }

            // FIXED: Scale threat instead of hard-coded 1000f divisor
            float avgThreatLevel = 1500f; // Average expected threat level
            float threatMultiplier = 1f + (threatLevel / avgThreatLevel); // Scale based on average
            priority *= Math.Min(threatMultiplier, 3f); // Cap at 3x multiplier

            return priority;
        }

        private float CalculateAverageKingdomDistance(Kingdom kingdom)
        {
            if (kingdom.Settlements.Count <= 1) return 150f;

            float totalDistance = 0f;
            int count = 0;

            foreach (var settlement1 in kingdom.Settlements)
            {
                foreach (var settlement2 in kingdom.Settlements)
                {
                    if (settlement1 != settlement2)
                    {
                        totalDistance += Campaign.Current.Models.MapDistanceModel.GetDistance(settlement1, settlement2);
                        count++;
                    }
                }
            }

            return count > 0 ? totalDistance / count : 150f;
        }

        private float CalculateRequiredStrength(Settlement settlement, ObjectiveType type)
        {
            float baseStrength = CalculateSettlementDefensiveStrength(settlement);

            switch (type)
            {
                case ObjectiveType.Capture:
                    // FIXED: Reduced overcommitment to allow more armies to participate
                    // This is now just the "minimum recommended" not a hard limit
                    float minRecommended = settlement.IsTown ? 1.2f : 1.1f; // Reduced from 1.5f/1.3f
                    return baseStrength * minRecommended; // Removed the *2f multiplier
                case ObjectiveType.Defend:
                    return CalculateThreatLevel(settlement);
                default:
                    return baseStrength;
            }
        }

        private float CalculateSettlementDefensiveStrength(Settlement settlement)
        {
            float strength = settlement.Party?.TotalStrength ?? 0f;

            if (settlement.Town?.GarrisonParty != null)
                strength += settlement.Town.GarrisonParty.Party.TotalStrength;

            strength += settlement.MilitiaPartyComponent?.Party?.TotalStrength ?? 0f;

            if (settlement.IsFortification)
                strength *= 1f + (settlement.Town.GetWallLevel() * 0.2f);

            return strength;
        }

        private float CalculateThreatLevel(Settlement settlement)
        {
            float threatLevel = 0f;
            var enemyFactions = FactionManager.GetEnemyFactions(settlement.MapFaction);

            foreach (var faction in enemyFactions)
            {
                foreach (var army in (faction as Kingdom)?.Armies ?? Enumerable.Empty<Army>())
                {
                    if (army.LeaderParty != null)
                    {
                        float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(army.LeaderParty, settlement);
                        if (distance <= 100f)
                        {
                            threatLevel += army.TotalStrength * (1f - distance / 100f);
                        }
                    }
                }
            }

            return threatLevel;
        }

        public float GetArmyCreationPriorityBonus(MobileParty potentialLeader)
        {
            if (ShouldCreateArmy(potentialLeader, out StrategicObjective objective))
            {
                // Provide bonus based on objective priority and urgency
                float priorityBonus = 1f + (objective.Priority * 0.5f);

                // Urgency bonus based on expiration time
                float hoursToExpiration = (float) (objective.ExpirationTime.ToHours - CampaignTime.Now.ToHours);
                float urgencyBonus = hoursToExpiration < 24f ? 1.5f : 1f; // Urgent if less than 24 hours

                // Defense gets additional urgency
                float defensiveBonus = objective.Type == ObjectiveType.Defend ? 1.3f : 1f;

                return priorityBonus * urgencyBonus * defensiveBonus;
            }

            return 1f;
        }

        // Simple data classes
        public enum ObjectiveType
        {
            Capture,
            Defend
        }

        public class StrategicObjective
        {
            [SaveableField(1)]
            public ObjectiveType Type;

            [SaveableField(2)]
            public Settlement TargetSettlement;

            [SaveableField(3)]
            public float RequiredStrength;

            [SaveableField(4)]
            public float Priority;

            [SaveableField(5)]
            public CampaignTime ExpirationTime;

            [SaveableField(6)]
            public List<Army> AssignedArmies;

            public StrategicObjective()
            {
                AssignedArmies = new List<Army>();
            }
        }

        private class ObjectiveCandidate
        {
            public Settlement Settlement;
            public ObjectiveType Type;
            public float Priority;
        }
    }
}