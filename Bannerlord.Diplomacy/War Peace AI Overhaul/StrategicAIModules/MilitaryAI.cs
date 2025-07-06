using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

[HarmonyPatch(typeof(PartyThinkParams), "AddBehaviorScore")]
public class AICoordinationPatch
{
    // Coordination state tracking
    private static Dictionary<MobileParty, CampaignTime> _lastCoordinationUpdate = new Dictionary<MobileParty, CampaignTime>();
    private static Dictionary<MobileParty, Settlement> _committedTargets = new Dictionary<MobileParty, Settlement>();
    private static Dictionary<Settlement, List<MobileParty>> _siegeCoordination = new Dictionary<Settlement, List<MobileParty>>();

    // NEW: Proactive defense coordination
    private static Dictionary<Settlement, DefenseCoordinationPlan> _defenseCoordinationPlans = new Dictionary<Settlement, DefenseCoordinationPlan>();
    private static CampaignTime _lastDefensePlanUpdate = CampaignTime.Zero;

    public static bool Prefix(PartyThinkParams __instance, ref ValueTuple<AIBehaviorTuple, float> value)
    {
        var party = __instance.MobilePartyOf;
        var behaviorTuple = value.Item1;
        var originalScore = value.Item2;

        // UPDATED: Only block chase for ARMY LEADERS
        if (party?.Army?.LeaderParty == party && party.LeaderHero != null)
        {
            if (IsFasterPartyChase(party, behaviorTuple))
            {
                return false; // COMPLETELY BLOCK - don't add this behavior score at all
            }
        }

        return true; // Allow normal processing for non-chase behaviors
    }

    public static void Postfix(PartyThinkParams __instance, ref ValueTuple<AIBehaviorTuple, float> value)
    {
        var party = __instance.MobilePartyOf;
        var behaviorTuple = value.Item1;
        var originalScore = value.Item2;

        // Only apply complex coordination logic to army leaders
        if (party?.Army?.LeaderParty != party || party.LeaderHero == null)
            return;

        // NEW: Update defense coordination plans periodically
        UpdateDefenseCoordinationPlans();

        // Update coordination state periodically
        UpdateCoordinationState(party);

        // Apply coordination logic for army leaders
        float modifiedScore = ApplyCoordinationLogic(party, behaviorTuple, originalScore);

        // Update the score if it was modified
        if (Math.Abs(modifiedScore - originalScore) > 0.1f)
        {
            value = new ValueTuple<AIBehaviorTuple, float>(behaviorTuple, modifiedScore);
        }
    }

    // NEW: Defense coordination plan class
    private class DefenseCoordinationPlan
    {
        public Settlement ThreatenedSettlement { get; set; }
        public float ThreatStrength { get; set; }
        public float RequiredDefenseStrength { get; set; }
        public List<MobileParty> EligibleDefenders { get; set; } = new List<MobileParty>();
        public bool NeedsCoordination => EligibleDefenders.Count >= 2 && ThreatStrength > 0;
        public CampaignTime CreatedTime { get; set; }

        public float GetCombinedDefenseStrength()
        {
            float garrison = ThreatenedSettlement?.Party?.TotalStrength ?? 0f;
            float armies = EligibleDefenders.Sum(p => p.Army?.TotalStrength ?? p.Party.TotalStrength);
            return garrison + armies;
        }
    }

    // NEW: Proactively update defense coordination plans
    private static void UpdateDefenseCoordinationPlans()
    {
        // Only update every 2 hours to avoid performance issues
        if ((CampaignTime.Now - _lastDefensePlanUpdate).ToHours < 2f)
            return;

        _lastDefensePlanUpdate = CampaignTime.Now;

        // Clear old plans
        _defenseCoordinationPlans.Clear();

        // Find all threatened settlements
        var threatenedSettlements = new List<Settlement>();

        foreach (var settlement in Settlement.All.Where(s => s != null && (s.IsTown || s.IsCastle)))
        {
            float threatStrength = GetThreatStrength(settlement);
            if (threatStrength > 0)
            {
                threatenedSettlements.Add(settlement);
            }
        }

        // Create coordination plans for each threatened settlement
        foreach (var settlement in threatenedSettlements)
        {
            CreateDefenseCoordinationPlan(settlement);
        }
    }

    // NEW: Get threat strength for a settlement
    private static float GetThreatStrength(Settlement settlement)
    {
        float threatStrength = 0f;

        // Check for active siege
        if (settlement.SiegeEvent != null)
        {
            foreach (var siegeParty in settlement.SiegeEvent.BesiegerCamp.GetInvolvedPartiesForEventType(TaleWorlds.CampaignSystem.MapEvents.MapEvent.BattleTypes.Siege))
            {
                if (siegeParty.MapFaction != settlement.MapFaction)
                {
                    threatStrength += siegeParty.TotalStrength;
                }
            }
        }

        // Check for recent attacker
        if (settlement.LastAttackerParty != null && settlement.LastAttackerParty.IsActive)
        {
            threatStrength = Math.Max(threatStrength, settlement.LastAttackerParty.Party.TotalStrength);
        }

        // Check for nearby enemy armies
        foreach (var enemyParty in MobileParty.AllLordParties)
        {
            if (enemyParty.MapFaction != settlement.MapFaction &&
                enemyParty.MapFaction.IsAtWarWith(settlement.MapFaction) &&
                enemyParty.Army?.LeaderParty == enemyParty)
            {
                float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(enemyParty, settlement);
                if (distance <= 150f) // Within 1.5 days travel
                {
                    threatStrength += enemyParty.Army?.TotalStrength ?? enemyParty.Party.TotalStrength;
                }
            }
        }

        return threatStrength;
    }

    // NEW: Create a defense coordination plan
    private static void CreateDefenseCoordinationPlan(Settlement settlement)
    {
        float threatStrength = GetThreatStrength(settlement);
        if (threatStrength <= 0) return;

        float currentDefense = settlement.Party?.TotalStrength ?? 0f;
        float requiredDefense = threatStrength * 1.2f; // Need 20% advantage

        // If current defense is sufficient, no coordination needed
        if (currentDefense >= requiredDefense)
            return;

        // Find eligible defenders
        var eligibleDefenders = new List<MobileParty>();

        foreach (var party in MobileParty.AllLordParties)
        {
            if (party.MapFaction == settlement.MapFaction &&
                party.Army?.LeaderParty == party &&
                party.IsActive &&
                !party.IsDisbanding)
            {
                float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(party, settlement);
                if (distance <= 300f) // Within 3 days travel
                {
                    eligibleDefenders.Add(party);
                }
            }
        }

        // Only create plan if we have multiple potential defenders
        if (eligibleDefenders.Count < 2)
            return;

        // Check if combined force would be sufficient
        float combinedStrength = currentDefense + eligibleDefenders.Sum(p => p.Army?.TotalStrength ?? p.Party.TotalStrength);

        if (combinedStrength >= requiredDefense)
        {
            var plan = new DefenseCoordinationPlan
            {
                ThreatenedSettlement = settlement,
                ThreatStrength = threatStrength,
                RequiredDefenseStrength = requiredDefense,
                EligibleDefenders = eligibleDefenders,
                CreatedTime = CampaignTime.Now
            };

            _defenseCoordinationPlans[settlement] = plan;
        }
    }

    // ENHANCED: More comprehensive chase detection
    private static bool IsFasterPartyChase(MobileParty chaser, AIBehaviorTuple behaviorTuple)
    {
        // Apply to ALL party-chasing behaviors
        if (behaviorTuple.AiBehavior != AiBehavior.GoAroundParty &&
            behaviorTuple.AiBehavior != AiBehavior.EngageParty)
            return false;

        MobileParty targetParty = behaviorTuple.Party as MobileParty;
        if (targetParty == null)
            return false;

        // If target is even slightly faster, block the chase
        float chaserSpeed = chaser.Speed;
        float targetSpeed = targetParty.Speed;

        // Block if target is faster or equal speed (no advantage)
        bool isFaster = targetSpeed >= chaserSpeed;

        return isFaster;
    }

    private static void UpdateCoordinationState(MobileParty party)
    {
        if (!_lastCoordinationUpdate.ContainsKey(party) ||
            (CampaignTime.Now - _lastCoordinationUpdate[party]).ToHours >= 1f)
        {
            _lastCoordinationUpdate[party] = CampaignTime.Now;

            // Clean up dead parties
            CleanupCoordinationState();

            // Update commitment tracking
            UpdateCommitmentTracking(party);
        }
    }

    private static float ApplyCoordinationLogic(MobileParty party, AIBehaviorTuple behaviorTuple, float score)
    {
        float modifiedScore = score;

        // 1. COMMITMENT SYSTEM: Prevent target switching for committed armies
        if (IsPartyCommitted(party, behaviorTuple))
        {
            Settlement targetSettlement = behaviorTuple.Party as Settlement;

            if (targetSettlement == party.TargetSettlement)
            {
                // Boost score for committed target
                modifiedScore *= 3.0f;
            }
            else
            {
                // Heavily penalize switching away from committed target
                modifiedScore *= 0.1f;
            }
        }

        // 2. SIEGE COORDINATION: Boost scores for coordinated siege operations
        Settlement siegeTarget = behaviorTuple.Party as Settlement;

        if (behaviorTuple.AiBehavior == AiBehavior.BesiegeSettlement &&
            siegeTarget?.IsFortification == true)
        {
            float coordinationBonus = CalculateSiegeCoordinationBonus(party, siegeTarget);
            modifiedScore *= coordinationBonus;
        }

        // NEW: 3. PROACTIVE DEFENSE COORDINATION
        if (behaviorTuple.AiBehavior == AiBehavior.DefendSettlement &&
            behaviorTuple.Party is Settlement defenseTarget)
        {
            float defenseBonus = CalculateProactiveDefenseBonus(party, defenseTarget);
            modifiedScore *= defenseBonus;
        }

        // 4. ANTI-OSCILLATION: Penalize rapid behavior changes
        float stabilityPenalty = CalculateStabilityPenalty(party, behaviorTuple);
        modifiedScore *= stabilityPenalty;

        // 5. PRIORITY ENFORCEMENT: Boost military objectives over civilian activities
        if (ShouldPrioritizeMilitaryAction(party))
        {
            if (IsMilitaryBehavior(behaviorTuple.AiBehavior))
            {
                modifiedScore *= 2.0f; // Boost military actions
            }
            else if (IsCivilianBehavior(behaviorTuple.AiBehavior))
            {
                modifiedScore *= 0.3f; // Reduce civilian activities during war
            }
        }

        return Math.Max(0.01f, modifiedScore); // Ensure minimum positive score
    }

    // NEW: Calculate proactive defense bonus using coordination plans
    private static float CalculateProactiveDefenseBonus(MobileParty party, Settlement target)
    {
        if (target == null || target.MapFaction != party.MapFaction)
            return 1.0f;

        // Check if there's a coordination plan for this settlement
        if (!_defenseCoordinationPlans.TryGetValue(target, out var plan))
            return 1.0f;

        // Check if this party is eligible for the coordination plan
        if (!plan.EligibleDefenders.Contains(party))
            return 1.0f;

        float bonus = 1.0f;

        // Base coordination bonus - significant boost to overcome the chicken-and-egg problem
        bonus += 4.0f; // Strong base incentive for coordination

        // Calculate how many armies are needed
        float currentDefense = target.Party?.TotalStrength ?? 0f;
        float armyStrength = party.Army?.TotalStrength ?? party.Party.TotalStrength;
        float otherArmiesStrength = plan.EligibleDefenders
            .Where(p => p != party)
            .Sum(p => p.Army?.TotalStrength ?? p.Party.TotalStrength);

        // Bonus based on urgency of the threat
        float threatRatio = plan.ThreatStrength / Math.Max(currentDefense, 1f);
        if (threatRatio > 3.0f)
        {
            bonus += 3.0f; // Critical threat
        }
        else if (threatRatio > 2.0f)
        {
            bonus += 2.0f; // Serious threat
        }
        else if (threatRatio > 1.5f)
        {
            bonus += 1.0f; // Moderate threat
        }

        // Bonus for being part of a viable defense plan
        float combinedDefense = currentDefense + armyStrength + (otherArmiesStrength * 0.7f); // Assume 70% of other armies will come
        if (combinedDefense >= plan.RequiredDefenseStrength)
        {
            bonus += 2.0f; // Extra bonus for viable coordinated defense
        }

        // Priority bonus based on settlement importance
        if (target.IsTown)
        {
            bonus += 2.0f; // Towns are critical
        }
        else if (target.IsCastle)
        {
            bonus += 1.5f; // Castles are important
        }

        // Clan loyalty bonus
        if (party.LeaderHero?.Clan != null && target.OwnerClan == party.LeaderHero.Clan)
        {
            bonus += 2.0f; // Defend your own holdings
        }

        // Distance penalty - closer armies get higher priority
        float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(party, target);
        float distancePenalty = Math.Max(0.3f, 1.0f - (distance / 300f)); // Penalty based on distance
        bonus *= distancePenalty;

        // Debug message for significant coordination bonuses
        if (bonus > 3.0f)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                $"[Defense Coord] {party.Name} incentivized to defend {target.Name} (Bonus: {bonus:F1}x, Plan: {plan.EligibleDefenders.Count} armies vs {plan.ThreatStrength:F0} threat)",
                Colors.Cyan));
        }

        return Math.Min(bonus, 12.0f); // Cap at 12x bonus
    }

    private static bool IsPartyCommitted(MobileParty party, AIBehaviorTuple behaviorTuple)
    {
        // Party is committed if:
        // 1. Currently engaged in combat/siege
        if (party.MapEvent != null || party.SiegeEvent != null)
            return true;

        // 2. Close to current target (within 1.5 days travel)
        if (party.TargetSettlement != null)
        {
            float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(party, party.TargetSettlement);
            if (distance <= 150f) // ~1.5 days travel
                return true;
        }

        // 3. Has been targeting the same settlement for a while
        if (_committedTargets.ContainsKey(party) &&
            _committedTargets[party] == party.TargetSettlement)
        {
            return true;
        }

        return false;
    }

    private static float CalculateSiegeCoordinationBonus(MobileParty party, Settlement target)
    {
        if (target == null || !target.IsFortification)
            return 1.0f;

        // Count friendly armies also targeting this settlement
        int coordinatingArmies = 0;
        float totalFriendlyStrength = party.Army?.TotalStrength ?? party.Party.TotalStrength;

        foreach (var otherParty in MobileParty.AllLordParties)
        {
            if (otherParty != party &&
                otherParty.MapFaction == party.MapFaction &&
                otherParty.Army?.LeaderParty == otherParty &&
                otherParty.TargetSettlement == target)
            {
                coordinatingArmies++;
                totalFriendlyStrength += otherParty.Army?.TotalStrength ?? otherParty.Party.TotalStrength;
            }
        }

        float defenderStrength = target.Party?.TotalStrength ?? 1f;
        float strengthRatio = totalFriendlyStrength / defenderStrength;

        // Calculate coordination bonus
        float bonus = 1.0f;

        if (coordinatingArmies >= 1)
        {
            bonus += 0.5f + (coordinatingArmies * 0.3f); // Base coordination bonus

            if (strengthRatio > 1.5f)
            {
                bonus *= 1.5f; // Extra bonus for overwhelming force
            }

            if (target.IsTown)
            {
                bonus *= 1.3f; // Higher priority for towns
            }
        }

        return Math.Min(bonus, 4.0f); // Cap at 4x bonus
    }

    private static float CalculateStabilityPenalty(MobileParty party, AIBehaviorTuple behaviorTuple)
    {
        // Penalize switching between different behaviors/targets rapidly
        if (party.TargetSettlement != null &&
            behaviorTuple.Party is Settlement targetSettlement &&
            targetSettlement != party.TargetSettlement)
        {
            return 0.7f; // 30% penalty for switching targets
        }

        if (party.DefaultBehavior != behaviorTuple.AiBehavior)
        {
            return 0.8f; // 20% penalty for switching behaviors
        }

        return 1.0f; // No penalty for consistent behavior
    }

    private static bool ShouldPrioritizeMilitaryAction(MobileParty party)
    {
        // Prioritize military actions if faction is at war
        var enemies = FactionManager.GetEnemyFactions(party.MapFaction);
        return enemies?.Any() == true;
    }

    private static bool IsMilitaryBehavior(AiBehavior behavior)
    {
        return behavior == AiBehavior.BesiegeSettlement ||
               behavior == AiBehavior.DefendSettlement ||
               behavior == AiBehavior.GoAroundParty ||
               behavior == AiBehavior.EngageParty;
    }

    private static bool IsCivilianBehavior(AiBehavior behavior)
    {
        return behavior == AiBehavior.GoToSettlement ||
               behavior == AiBehavior.PatrolAroundPoint;
    }

    private static void UpdateCommitmentTracking(MobileParty party)
    {
        if (party.TargetSettlement != null)
        {
            // Track commitment to current target
            if (!_committedTargets.ContainsKey(party))
            {
                _committedTargets[party] = party.TargetSettlement;
            }
            else if (_committedTargets[party] != party.TargetSettlement)
            {
                // Target changed - reset commitment tracking
                _committedTargets[party] = party.TargetSettlement;
            }
        }
        else
        {
            // No target - remove commitment
            _committedTargets.Remove(party);
        }
    }

    private static void CleanupCoordinationState()
    {
        // Remove dead or inactive parties
        var partiesToRemove = _lastCoordinationUpdate.Keys
            .Where(p => p == null || !p.IsActive || p.IsDisbanding)
            .ToList();

        foreach (var party in partiesToRemove)
        {
            _lastCoordinationUpdate.Remove(party);
            _committedTargets.Remove(party);
        }

        // Clean up siege coordination tracking
        var settlementsToRemove = _siegeCoordination.Keys
            .Where(s => s == null || _siegeCoordination[s].All(p => p == null || !p.IsActive))
            .ToList();

        foreach (var settlement in settlementsToRemove)
        {
            _siegeCoordination.Remove(settlement);
        }

        // Clean up old defense coordination plans
        var expiredPlans = _defenseCoordinationPlans
            .Where(kvp => (CampaignTime.Now - kvp.Value.CreatedTime).ToHours > 12f)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var settlement in expiredPlans)
        {
            _defenseCoordinationPlans.Remove(settlement);
        }
    }
}