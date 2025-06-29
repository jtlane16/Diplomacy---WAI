using Diplomacy.Extensions;

using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

using TodayWeFeast;

using WarAndAiTweaks.AI;

namespace Diplomacy.War_Peace_AI_Overhaul.PartyPatches
{
    [HarmonyPatch(typeof(PartyThinkParams), "AddBehaviorScore")]
    public class AIMilitaryBehaviorPatches
    {
        private static readonly Dictionary<string, DateTime> _lastProcessingTime = new Dictionary<string, DateTime>();
        private static readonly TimeSpan _processingCooldown = TimeSpan.FromSeconds(5); // Only process each party every 5 seconds

        private static bool _isAddingFeastBehaviors = false;
        private static readonly Dictionary<string, DateTime> _lastFeastCheck = new Dictionary<string, DateTime>();
        private static readonly TimeSpan _feastCheckCooldown = TimeSpan.FromSeconds(10); // Check feasts less frequently

        private static void Prefix(ref ValueTuple<AIBehaviorTuple, float> value, PartyThinkParams __instance)
        {
            var behavior = value.Item1;
            var originalScore = value.Item2;
            var party = __instance.MobilePartyOf;

            if (party?.LeaderHero?.Clan?.Kingdom == null) return;

            // PERFORMANCE: Skip frequent processing for the same party
            string partyKey = party.StringId;
            DateTime now = DateTime.UtcNow;

            if (_lastProcessingTime.TryGetValue(partyKey, out DateTime lastTime))
            {
                if (now - lastTime < _processingCooldown)
                {
                    return; // Skip processing for this party
                }
            }
            _lastProcessingTime[partyKey] = now;

            // Clean up old entries periodically
            if (_lastProcessingTime.Count > 200) // Arbitrary limit
            {
                var keysToRemove = _lastProcessingTime
                    .Where(kvp => now - kvp.Value > TimeSpan.FromMinutes(10))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _lastProcessingTime.Remove(key);
                }
            }

            // Apply enhanced AI logic
            var result = ApplyEnhancedAILogic(party, behavior, originalScore);
            var modifiedScore = result.modifiedScore;
            var modifications = result.modifications;

            // Only log major changes during wartime
            if (!string.IsNullOrEmpty(modifications) && Math.Abs(modifiedScore - originalScore) > 1000f)
            {
                //AIComputationLogger.WriteLine($"Major behavior change for {party.Name}: {behavior} | {originalScore:F0} -> {modifiedScore:F0} | {modifications}");
            }

            value = new ValueTuple<AIBehaviorTuple, float>(behavior, modifiedScore);
        }

        private static void AddFeastSpecificBehaviors(PartyThinkParams thinkParams, MobileParty party)
        {
            if (FeastBehavior.Instance == null) return;

            var kingdom = party.LeaderHero?.Clan?.Kingdom;
            if (kingdom == null) return;

            // PERFORMANCE: Skip feast behavior injection during active wars for non-feast participants
            bool isAtWar = FactionManager.GetEnemyKingdoms(kingdom).Any();
            if (isAtWar)
            {
                // During war, only process feast behaviors for actual feast participants
                var activeFeast = FeastBehavior.Instance.Feasts.FirstOrDefault(f => f.kingdom == kingdom);
                if (activeFeast == null || !activeFeast.lordsInFeast.Contains(party.LeaderHero))
                {
                    return; // Skip feast processing for non-participants during war
                }
            }

            // PERFORMANCE: Rate limit feast checks
            string partyKey = $"feast_{party.StringId}";
            DateTime now = DateTime.UtcNow;
            
            if (_lastFeastCheck.TryGetValue(partyKey, out DateTime lastCheck))
            {
                if (now - lastCheck < _feastCheckCooldown)
                {
                    return; // Skip feast check for this party
                }
            }
            _lastFeastCheck[partyKey] = now;

            var feast = FeastBehavior.Instance.Feasts.FirstOrDefault(f => f.kingdom == kingdom);
            if (feast == null) return;

            bool isHost = feast.hostOfFeast == party.LeaderHero;
            bool isInvited = feast.lordsInFeast?.Contains(party.LeaderHero) == true;
            bool isAtFeast = party.LeaderHero.CurrentSettlement == feast.feastSettlement;

            _isAddingFeastBehaviors = true;

            try
            {
                // SIMPLIFIED: Only add critical behaviors during wartime
                if (isHost)
                {
                    if (!isAtFeast)
                    {
                        // Host must return to feast
                        AIBehaviorTuple goToFeastBehavior = new AIBehaviorTuple(feast.feastSettlement, AiBehavior.GoToSettlement, false);
                        ValueTuple<AIBehaviorTuple, float> hostReturnBehavior = new ValueTuple<AIBehaviorTuple, float>(goToFeastBehavior, 30000f);
                        thinkParams.AddBehaviorScore(hostReturnBehavior);
                    }
                    else
                    {
                        // Host must stay at feast
                        AIBehaviorTuple holdBehavior = new AIBehaviorTuple(feast.feastSettlement, AiBehavior.Hold, false);
                        ValueTuple<AIBehaviorTuple, float> hostHoldBehavior = new ValueTuple<AIBehaviorTuple, float>(holdBehavior, 25000f);
                        thinkParams.AddBehaviorScore(hostHoldBehavior);
                    }
                }
                else if (isInvited && !isAtWar) // Only process guests during peacetime
                {
                    if (!isAtFeast)
                    {
                        var attendanceModel = new FeastAttendingScoringModel();
                        var attendanceScore = attendanceModel.GetFeastAttendingScore(party.LeaderHero, feast);

                        if (attendanceScore.ResultNumber > 25f)
                        {
                            AIBehaviorTuple goToFeastBehavior = new AIBehaviorTuple(feast.feastSettlement, AiBehavior.GoToSettlement, false);
                            ValueTuple<AIBehaviorTuple, float> guestAttendBehavior = new ValueTuple<AIBehaviorTuple, float>(goToFeastBehavior, 8000f + attendanceScore.ResultNumber);
                            thinkParams.AddBehaviorScore(guestAttendBehavior);
                        }
                    }
                    else
                    {
                        var attendanceModel = new FeastAttendingScoringModel();
                        var attendanceScore = attendanceModel.GetFeastAttendingScore(party.LeaderHero, feast);

                        if (attendanceScore.ResultNumber > -25f)
                        {
                            AIBehaviorTuple holdAtFeastBehavior = new AIBehaviorTuple(feast.feastSettlement, AiBehavior.Hold, false);
                            ValueTuple<AIBehaviorTuple, float> guestStayBehavior = new ValueTuple<AIBehaviorTuple, float>(holdAtFeastBehavior, 5000f + attendanceScore.ResultNumber);
                            thinkParams.AddBehaviorScore(guestStayBehavior);
                        }
                    }
                }
            }
            finally
            {
                _isAddingFeastBehaviors = false;
            }
        }

        private static (float modifiedScore, string modifications) ApplyEnhancedAILogic(MobileParty party, AIBehaviorTuple behavior, float originalScore)
        {
            float modifiedScore = originalScore;
            var modifications = new StringBuilder();
            var hero = party.LeaderHero;
            var kingdom = hero?.Clan?.Kingdom;

            if (kingdom == null) return (modifiedScore, "");

            // Enhanced defensive behavior
            var defensiveResult = ApplyDefensiveBehaviorEnhancements(party, behavior, modifiedScore);
            modifiedScore = defensiveResult.score;
            if (!string.IsNullOrEmpty(defensiveResult.changes))
            {
                modifications.Append($"Def:{defensiveResult.changes};");
            }

            // Enhanced offensive behavior
            var offensiveResult = ApplyOffensiveBehaviorEnhancements(party, behavior, modifiedScore);
            modifiedScore = offensiveResult.score;
            if (!string.IsNullOrEmpty(offensiveResult.changes))
            {
                modifications.Append($"Off:{offensiveResult.changes};");
            }

            // Strategic positioning improvements
            var positioningResult = ApplyStrategicPositioningLogic(party, behavior, modifiedScore);
            modifiedScore = positioningResult.score;
            if (!string.IsNullOrEmpty(positioningResult.changes))
            {
                modifications.Append($"Pos:{positioningResult.changes};");
            }

            // Feast-aware behavior modifications
            var feastResult = ApplyFeastAwareBehavior(party, behavior, modifiedScore, kingdom);
            modifiedScore = feastResult.score;
            if (!string.IsNullOrEmpty(feastResult.changes))
            {
                modifications.Append($"Feast:{feastResult.changes};");
            }

            // Seasonal and economic considerations
            var seasonalResult = ApplySeasonalConsiderations(party, behavior, modifiedScore);
            modifiedScore = seasonalResult.score;
            if (!string.IsNullOrEmpty(seasonalResult.changes))
            {
                modifications.Append($"Season:{seasonalResult.changes};");
            }

            return (modifiedScore, modifications.ToString());
        }

        private static (float score, string changes) ApplyDefensiveBehaviorEnhancements(MobileParty party, AIBehaviorTuple behavior, float score)
        {
            var changes = new StringBuilder();
            var hero = party.LeaderHero;
            var kingdom = hero.Clan.Kingdom;
            float totalBonus = 0f;
            int threatenedSettlements = 0;
            int alliedThreatenedSettlements = 0;

            if (IsDefensiveBehavior(behavior))
            {
                var ownedSettlements = hero.Clan.Settlements.ToList();

                foreach (var settlement in ownedSettlements)
                {
                    if (IsSettlementThreatened(settlement, kingdom))
                    {
                        threatenedSettlements++;
                        float distance = party.Position2D.Distance(settlement.Position2D);
                        float threatLevel = CalculateSettlementThreatLevel(settlement, kingdom);

                        // Log threat assessment
                        //AIComputationLogger.LogSettlementThreatAssessment(settlement, kingdom, threatLevel, GetNearbyEnemyCount(settlement, kingdom));

                        float defenseBonus = (threatLevel * 100f) / (1f + distance / 100f);
                        score += defenseBonus;
                        totalBonus += defenseBonus;
                        changes.Append($"OwnThreat+{defenseBonus:F1};");

                        if (settlement.IsTown && settlement.Town.Prosperity > 5000f)
                        {
                            score += 50f;
                            totalBonus += 50f;
                            changes.Append("RichTown+50;");
                        }

                        if (IsStrategicSettlement(settlement, kingdom))
                        {
                            score += 75f;
                            totalBonus += 75f;
                            changes.Append("Strategic+75;");
                        }
                    }
                }

                if (!ownedSettlements.Any(s => IsSettlementThreatened(s, kingdom)))
                {
                    var alliedSettlements = kingdom.Settlements.Where(s =>
                        s.OwnerClan != hero.Clan && IsSettlementThreatened(s, kingdom));

                    foreach (var settlement in alliedSettlements)
                    {
                        alliedThreatenedSettlements++;
                        float distance = party.Position2D.Distance(settlement.Position2D);
                        float threatLevel = CalculateSettlementThreatLevel(settlement, kingdom);

                        float allyDefenseBonus = (threatLevel * 50f) / (1f + distance / 150f);

                        // NEW: Check if we should provide screening for sieging allies
                        var screeningBonus = EvaluateScreeningNeed(party, settlement, kingdom);
                        allyDefenseBonus += screeningBonus.bonus;
                        if (!string.IsNullOrEmpty(screeningBonus.reason))
                        {
                            changes.Append($"{screeningBonus.reason};");
                        }

                        score += allyDefenseBonus;
                        totalBonus += allyDefenseBonus;
                        changes.Append($"AllyThreat+{allyDefenseBonus:F1};");
                    }
                }

                // Personality-based defense modifiers
                int honor = hero.GetTraitLevel(DefaultTraits.Honor);
                int calculating = hero.GetTraitLevel(DefaultTraits.Calculating);

                if (honor > 0)
                {
                    float honorBonus = honor * 25f;
                    score += honorBonus;
                    totalBonus += honorBonus;
                    changes.Append($"Honor+{honorBonus:F1};");
                }

                if (calculating > 0)
                {
                    var enemyParties = GetNearbyEnemyParties(party, 100f);
                    float strengthRatio = party.Party.TotalStrength / (enemyParties.Sum(e => e.Party.TotalStrength) + 1f);

                    if (strengthRatio < 0.7f)
                    {
                        float penalty = calculating * 30f;
                        score -= penalty;
                        totalBonus -= penalty;
                        changes.Append($"CalcAvoid-{penalty:F1};");
                    }
                }

                // Log defensive analysis
                //AIComputationLogger.LogDefensiveBehaviorAnalysis(party, threatenedSettlements, alliedThreatenedSettlements, totalBonus);
            }

            return (score, changes.ToString());
        }

        // NEW: Evaluate need for screening/defensive support
        private static (float bonus, string reason) EvaluateScreeningNeed(MobileParty party, Settlement settlement, Kingdom kingdom)
        {
            // Check if any allies are besieging nearby enemy settlements
            var nearbyEnemySettlements = Kingdom.All
                .Where(k => k != kingdom && kingdom.IsAtWarWith(k))
                .SelectMany(k => k.Settlements)
                .Where(s => party.Position2D.Distance(s.Position2D) < 100f);

            foreach (var enemySettlement in nearbyEnemySettlements)
            {
                var besiegingAllies = GetNearbyAlliedParties(party, 75f, kingdom)
                    .Where(p => p.BesiegedSettlement == enemySettlement)
                    .ToList();

                if (besiegingAllies.Any())
                {
                    // Allies are besieging nearby - check for enemy relief forces
                    var incomingEnemies = GetNearbyEnemyParties(party, 200f)
                        .Where(e => e.Position2D.Distance(enemySettlement.Position2D) < 150f)
                        .ToList();

                    if (incomingEnemies.Any())
                    {
                        float totalEnemyStrength = incomingEnemies.Sum(e => e.Party.TotalStrength);
                        float totalAllyStrength = besiegingAllies.Sum(a => a.Party.TotalStrength);

                        if (totalEnemyStrength > totalAllyStrength * 0.8f)
                        {
                            // Enemy relief force is significant - screening is valuable
                            return (100f, "ScreeningNeeded+100");
                        }
                    }
                }
            }

            return (0f, "");
        }

        private static (float score, string changes) ApplyOffensiveBehaviorEnhancements(MobileParty party, AIBehaviorTuple behavior, float score)
        {
            var changes = new StringBuilder();
            var hero = party.LeaderHero;
            var kingdom = hero.Clan.Kingdom;
            float totalBonus = 0f;
            int viableTargets = 0;
            int weakEnemyParties = 0;

            if (IsOffensiveBehavior(behavior))
            {
                var enemies = FactionManager.GetEnemyKingdoms(kingdom).ToList();

                foreach (var enemy in enemies)
                {
                    var targetSettlements = enemy.Settlements.Where(s => s.IsTown || s.IsCastle);

                    foreach (var target in targetSettlements)
                    {
                        float distance = party.Position2D.Distance(target.Position2D);

                        if (distance < 200f)
                        {
                            viableTargets++;
                            float targetValue = CalculateTargetValue(target);
                            float proximityBonus = (200f - distance) / 200f * targetValue;

                            // NEW: Check for army coordination needs
                            var coordinationResult = EvaluateArmyCoordination(party, target, kingdom);
                            proximityBonus += coordinationResult.bonus;
                            if (!string.IsNullOrEmpty(coordinationResult.reason))
                            {
                                changes.Append($"{coordinationResult.reason};");
                            }

                            score += proximityBonus;
                            totalBonus += proximityBonus;
                            changes.Append($"Target+{proximityBonus:F1};");

                            if (IsTargetPoorlyDefended(target, party))
                            {
                                score += 100f;
                                totalBonus += 100f;
                                changes.Append("WeakTarget+100;");
                            }
                        }
                    }

                    var enemyParties = enemy.AllParties.Where(p =>
                        p.IsLordParty && p.Position2D.Distance(party.Position2D) < 150f);

                    foreach (var enemyParty in enemyParties)
                    {
                        float strengthRatio = party.Party.TotalStrength / enemyParty.Party.TotalStrength;

                        if (strengthRatio > 1.2f)
                        {
                            weakEnemyParties++;
                            score += 75f;
                            totalBonus += 75f;
                            changes.Append("WeakEnemy+75;");
                        }
                        else if (strengthRatio < 0.8f)
                        {
                            score -= 50f;
                            totalBonus -= 50f;
                            changes.Append("StrongEnemy-50;");
                        }
                    }
                }

                // Personality-based offense modifiers
                int valor = hero.GetTraitLevel(DefaultTraits.Valor);
                int calculating = hero.GetTraitLevel(DefaultTraits.Calculating);

                if (valor > 0)
                {
                    float valorBonus = valor * 20f;
                    score += valorBonus;
                    totalBonus += valorBonus;
                    changes.Append($"Valor+{valorBonus:F1};");
                }

                if (calculating > 0)
                {
                    var nearbyAllies = GetNearbyAlliedParties(party, 100f);
                    if (nearbyAllies.Count() < 2)
                    {
                        float penalty = calculating * 25f;
                        score -= penalty;
                        totalBonus -= penalty;
                        changes.Append($"CalcCoord-{penalty:F1};");
                    }
                }

                // Log offensive analysis
                //AIComputationLogger.LogOffensiveBehaviorAnalysis(party, viableTargets, weakEnemyParties, totalBonus);
            }

            return (score, changes.ToString());
        }

        // NEW: Army coordination evaluation
        private static (float bonus, string reason) EvaluateArmyCoordination(MobileParty party, Settlement target, Kingdom kingdom)
        {
            // Check if there are already friendly armies near this target
            var nearbyAllies = GetNearbyAlliedParties(party, 100f, kingdom)
                .Where(p => p.Position2D.Distance(target.Position2D) < 50f)
                .ToList();

            if (!nearbyAllies.Any())
                return (0f, ""); // No coordination needed

            // Check if any ally is already besieging this target
            var besiegingAlly = nearbyAllies.FirstOrDefault(ally =>
                ally.BesiegedSettlement == target);

            if (besiegingAlly != null)
            {
                // Another army is already sieging - we should provide defensive support
                var nearbyEnemies = GetNearbyEnemyParties(party, 150f).ToList();

                if (nearbyEnemies.Any())
                {
                    // Enemy relief force nearby - defensive role is more valuable
                    return (-150f, "AllyBesieging-150"); // Reduce offensive behavior
                }
                else
                {
                    // No immediate threat - less penalty but still discourage doubling up
                    return (-75f, "AllyBesieging-75");
                }
            }

            // Check if we're the strongest army in the group
            var ourStrength = party.Party.TotalStrength;
            var strongestAlly = nearbyAllies.OrderByDescending(p => p.Party.TotalStrength).FirstOrDefault();

            if (strongestAlly != null && ourStrength > strongestAlly.Party.TotalStrength * 1.2f)
            {
                // We're significantly stronger - we should lead the siege
                return (50f, "StrongestArmy+50");
            }

            // We're not the strongest - support role is better
            return (-100f, "SupportRole-100");
        }

        private static (float score, string changes) ApplyStrategicPositioningLogic(MobileParty party, AIBehaviorTuple behavior, float score)
        {
            var changes = new StringBuilder();
            var hero = party.LeaderHero;
            var kingdom = hero.Clan.Kingdom;
            float totalBonus = 0f;
            bool isMultiFrontWar = false;
            bool nearBorder = false;
            bool withAllies = false;

            var enemies = FactionManager.GetEnemyKingdoms(kingdom).Count();
            isMultiFrontWar = enemies > 1;

            if (isMultiFrontWar)
            {
                var borderSettlements = GetBorderSettlements(kingdom);
                var nearestBorder = borderSettlements.OrderBy(s =>
                    party.Position2D.Distance(s.Position2D)).FirstOrDefault();

                if (nearestBorder != null)
                {
                    float distance = party.Position2D.Distance(nearestBorder.Position2D);
                    if (distance < 50f && IsDefensiveBehavior(behavior))
                    {
                        nearBorder = true;
                        score += 50f;
                        totalBonus += 50f;
                        changes.Append("BorderDef+50;");
                    }
                }
            }

            var nearbyAllies = GetNearbyAlliedParties(party, 75f);
            if (nearbyAllies.Count() >= 2)
            {
                withAllies = true;
                score += 30f;
                totalBonus += 30f;
                changes.Append("WithAllies+30;");
            }

            // Log strategic positioning
            //AIComputationLogger.LogStrategicPositioning(party, isMultiFrontWar, nearBorder, withAllies, totalBonus);

            return (score, changes.ToString());
        }

        private static (float score, string changes) ApplyFeastAwareBehavior(MobileParty party, AIBehaviorTuple behavior, float score, Kingdom kingdom)
        {
            var changes = new StringBuilder();
            float totalImpact = 0f;
            bool attendingFeast = false;
            bool kingdomFeasting = false;
            bool enemyFeasting = false;
            bool isHost = false;

            if (FeastBehavior.Instance == null) return (score, "");

            kingdomFeasting = FeastBehavior.Instance.feastIsPresent(kingdom);
            if (kingdomFeasting)
            {
                var feast = FeastBehavior.Instance.Feasts.FirstOrDefault(f => f.kingdom == kingdom);
                if (feast != null)
                {
                    attendingFeast = feast.lordsInFeast?.Contains(party.LeaderHero) == true;
                    isHost = feast.hostOfFeast == party.LeaderHero;

                    // MASSIVE HOST PRIORITY: Host must ALWAYS prioritize staying at feast
                    if (isHost)
                    {
                        var behaviorType = behavior.AiBehavior;
                        var behaviorTarget = behavior.Party;

                        if (party.LeaderHero.CurrentSettlement != feast.feastSettlement)
                        {
                            // Host is not at feast - MASSIVELY boost behaviors that go to feast settlement
                            if (behaviorType == AiBehavior.GoToSettlement && behaviorTarget == feast.feastSettlement)
                            {
                                float hostReturnBonus = 20000f; // MASSIVE bonus for returning to feast
                                score += hostReturnBonus;
                                totalImpact += hostReturnBonus;
                                changes.Append($"HostReturnToFeast+{hostReturnBonus:F0};");
                            }
                            else if (behaviorType == AiBehavior.GoToSettlement ||
                                     behaviorType == AiBehavior.PatrolAroundPoint ||
                                     behaviorType == AiBehavior.RaidSettlement ||
                                     behaviorType == AiBehavior.BesiegeSettlement ||
                                     behaviorType == AiBehavior.AssaultSettlement)
                            {
                                // MASSIVELY penalize any other movement behavior when host should return
                                float hostPenalty = -15000f; // EXTREME penalty
                                score += hostPenalty;
                                totalImpact += hostPenalty;
                                changes.Append($"HostMustReturnHome{hostPenalty:F0};");
                            }
                        }
                        else
                        {
                            // Host is at feast - heavily boost staying behaviors
                            if (behaviorType == AiBehavior.Hold)
                            {
                                float hostStayBonus = 15000f; // MASSIVE bonus for staying put
                                score += hostStayBonus;
                                totalImpact += hostStayBonus;
                                changes.Append($"HostStayAtFeast+{hostStayBonus:F0};");
                            }

                            // MASSIVELY penalize ANY movement away from feast
                            if (behaviorType == AiBehavior.GoToSettlement && behaviorTarget != feast.feastSettlement ||
                                behaviorType == AiBehavior.RaidSettlement ||
                                behaviorType == AiBehavior.BesiegeSettlement ||
                                behaviorType == AiBehavior.AssaultSettlement ||
                                behaviorType == AiBehavior.EngageParty ||
                                behaviorType == AiBehavior.PatrolAroundPoint || // Penalize ANY patrol behavior
                                behaviorType == AiBehavior.DefendSettlement) // Penalize defend during peacetime feast
                            {
                                float hostStayPenalty = -20000f; // EXTREME penalty for leaving
                                score += hostStayPenalty;
                                totalImpact += hostStayPenalty;
                                changes.Append($"HostNoLeaveFeast{hostStayPenalty:F0};");
                            }
                        }
                    }
                    // GUEST LOGIC (invited attendees who are not the host)
                    else if (attendingFeast && !isHost)
                    {
                        var behaviorType = behavior.AiBehavior;
                        var behaviorTarget = behavior.Party;

                        if (party.LeaderHero.CurrentSettlement != feast.feastSettlement)
                        {
                            // Guest should attend feast
                            if (behaviorType == AiBehavior.GoToSettlement && behaviorTarget == feast.feastSettlement)
                            {
                                float guestAttendBonus = 6000f;
                                score += guestAttendBonus;
                                totalImpact += guestAttendBonus;
                                changes.Append($"GuestAttendFeast+{guestAttendBonus:F0};");
                            }
                            else if (behaviorType == AiBehavior.GoToSettlement ||
                                     behaviorType == AiBehavior.RaidSettlement ||
                                     behaviorType == AiBehavior.BesiegeSettlement)
                            {
                                // Moderate penalty for going elsewhere
                                float guestPenalty = -3000f;
                                score += guestPenalty;
                                totalImpact += guestPenalty;
                                changes.Append($"GuestShouldAttendFeast{guestPenalty:F0};");
                            }
                        }
                        else
                        {
                            // Guest is at feast - boost staying behaviors
                            if (behaviorType == AiBehavior.Hold ||
                                (behaviorType == AiBehavior.PatrolAroundPoint && behaviorTarget == feast.feastSettlement))
                            {
                                float guestStayBonus = 3000f;
                                score += guestStayBonus;
                                totalImpact += guestStayBonus;
                                changes.Append($"GuestStayAtFeast+{guestStayBonus:F0};");
                            }

                            // Penalty for leaving behaviors
                            if (behaviorType == AiBehavior.GoToSettlement && behaviorTarget != feast.feastSettlement ||
                                behaviorType == AiBehavior.RaidSettlement ||
                                behaviorType == AiBehavior.BesiegeSettlement)
                            {
                                float guestLeavePenalty = -4000f;
                                score += guestLeavePenalty;
                                totalImpact += guestLeavePenalty;
                                changes.Append($"GuestNoLeaveFeast{guestLeavePenalty:F0};");
                            }
                        }
                    }
                    // Lords not invited to the feast - slight defensive boost
                    else if (!attendingFeast && !isHost && IsDefensiveBehavior(behavior))
                    {
                        score += 200f;
                        totalImpact += 200f;
                        changes.Append("NotInvited+200;");
                    }
                }
            }
            else
            {
                // NO ACTIVE FEAST: If lords are stuck at settlements, encourage leaving
                if (party.LeaderHero.CurrentSettlement != null)
                {
                    // Check if they're at a settlement that used to have a feast
                    bool wasFormerFeastLocation = FeastBehavior.Instance.Feasts.Any(f =>
                        f.feastSettlement == party.LeaderHero.CurrentSettlement);

                    if (!wasFormerFeastLocation)
                    {
                        var behaviorType = behavior.AiBehavior;

                        // Encourage movement behaviors when no feast is active
                        if (behaviorType == AiBehavior.GoToSettlement ||
                            behaviorType == AiBehavior.PatrolAroundPoint ||
                            behaviorType == AiBehavior.RaidSettlement ||
                            behaviorType == AiBehavior.BesiegeSettlement)
                        {
                            float encourageMovement = 800f;
                            score += encourageMovement;
                            totalImpact += encourageMovement;
                            changes.Append($"NoFeastMove+{encourageMovement:F0};");
                        }

                        // Penalize staying put when no feast
                        if (behaviorType == AiBehavior.Hold ||
                            behaviorType == AiBehavior.DefendSettlement)
                        {
                            float discourageStaying = -500f;
                            score += discourageStaying;
                            totalImpact += discourageStaying;
                            changes.Append($"NoFeastStay{discourageStaying:F0};");
                        }
                    }
                }
            }

            // Honor-based enemy feast considerations
            var enemies = FactionManager.GetEnemyKingdoms(kingdom);
            foreach (var enemy in enemies)
            {
                if (FeastBehavior.Instance.feastIsPresent(enemy))
                {
                    enemyFeasting = true;
                    int honor = party.LeaderHero.GetTraitLevel(DefaultTraits.Honor);
                    if (honor > 0 && IsOffensiveBehavior(behavior))
                    {
                        float penalty = honor * 100f;
                        score -= penalty;
                        totalImpact -= penalty;
                        changes.Append($"HonorEnemyFeast-{penalty:F1};");
                    }
                }
            }

            // DISABLED: Log feast behavior impact for debugging - commenting out for performance
            // AIComputationLogger.LogFeastBehaviorImpact(party, attendingFeast, kingdomFeasting, enemyFeasting, totalImpact);

            return (score, changes.ToString());
        }

        private static (float score, string changes) ApplySeasonalConsiderations(MobileParty party, AIBehaviorTuple behavior, float score)
        {
            var changes = new StringBuilder();
            var currentSeason = CampaignTime.Now.GetSeasonOfYear;
            bool isOffensive = IsOffensiveBehavior(behavior);
            bool isDefensive = IsDefensiveBehavior(behavior);
            float seasonalBonus = 0f;
            string seasonName = currentSeason.ToString();

            switch (currentSeason)
            {
                case CampaignTime.Seasons.Winter:
                    if (isOffensive)
                    {
                        float reduction = score * 0.2f; // 20% reduction
                        score *= 0.8f;
                        seasonalBonus -= reduction;
                        changes.Append($"WinterOff-{reduction:F1};");
                    }
                    if (isDefensive)
                    {
                        score += 25f;
                        seasonalBonus += 25f;
                        changes.Append("WinterDef+25;");
                    }
                    break;

                case CampaignTime.Seasons.Spring:
                    if (isOffensive)
                    {
                        score += 20f;
                        seasonalBonus += 20f;
                        changes.Append("SpringOff+20;");
                    }
                    break;

                case CampaignTime.Seasons.Summer:
                    if (isOffensive)
                    {
                        score += 30f;
                        seasonalBonus += 30f;
                        changes.Append("SummerOff+30;");
                    }
                    break;

                case CampaignTime.Seasons.Autumn:
                    if (isDefensive)
                    {
                        score += 15f;
                        seasonalBonus += 15f;
                        changes.Append("AutumnDef+15;");
                    }
                    break;
            }

            // Log seasonal behavior impact
            //AIComputationLogger.LogSeasonalBehaviorImpact(party, seasonName, isOffensive, isDefensive, seasonalBonus);

            return (score, changes.ToString());
        }

        // Helper method to count nearby enemies for threat assessment
        private static int GetNearbyEnemyCount(Settlement settlement, Kingdom kingdom)
        {
            var enemies = FactionManager.GetEnemyKingdoms(kingdom);
            int count = 0;

            foreach (var enemy in enemies)
            {
                count += enemy.AllParties.Count(p =>
                    p.IsLordParty && p.Position2D.Distance(settlement.Position2D) < 150f);
            }

            return count;
        }

        // Helper methods
        private static bool IsDefensiveBehavior(AIBehaviorTuple behavior)
        {
            // You'll need to identify the actual behavior types from the game's AI system
            // This is a placeholder - check the actual enum/class values
            return behavior.ToString().Contains("Defend") ||
                    behavior.ToString().Contains("Garrison") ||
                    behavior.ToString().Contains("PatrolAroundPoint");
        }

        private static bool IsOffensiveBehavior(AIBehaviorTuple behavior)
        {
            return behavior.ToString().Contains("Attack") ||
                    behavior.ToString().Contains("Raid") ||
                    behavior.ToString().Contains("BesiegeSettlement");
        }

        private static bool IsSettlementThreatened(Settlement settlement, Kingdom kingdom)
        {
            var enemies = FactionManager.GetEnemyKingdoms(kingdom);
            return enemies.Any(enemy =>
                enemy.AllParties.Any(p =>
                    p.IsLordParty && p.Position2D.Distance(settlement.Position2D) < 100f));
        }

        private static float CalculateSettlementThreatLevel(Settlement settlement, Kingdom kingdom)
        {
            float threatLevel = 0f;
            var enemies = FactionManager.GetEnemyKingdoms(kingdom);

            foreach (var enemy in enemies)
            {
                var nearbyEnemies = enemy.AllParties.Where(p =>
                    p.IsLordParty && p.Position2D.Distance(settlement.Position2D) < 150f);

                threatLevel += nearbyEnemies.Sum(p => p.Party.TotalStrength) / 100f;
            }

            return Math.Min(threatLevel, 10f); // Cap at 10
        }

        private static bool IsStrategicSettlement(Settlement settlement, Kingdom kingdom)
        {
            // Check if settlement controls important trade routes or borders
            var borderCount = Kingdom.All.Count(k =>
                k != kingdom &&
                settlement.IsBorderSettlementWith(k));

            return borderCount >= 2 || // Border with multiple kingdoms
                    (settlement.IsTown && settlement.Town.Prosperity > 7000f); // Major trade center
        }

        private static float CalculateTargetValue(Settlement target)
        {
            float value = 10f; // Base value

            if (target.IsTown)
            {
                value += target.Town.Prosperity / 100f;
                value += target.Town.Security > 80f ? -20f : 20f; // Prefer less secure towns
            }
            else if (target.IsCastle)
            {
                value += 30f;
                value += target.Town?.Security > 80f ? -15f : 15f;
            }

            return value;
        }

        private static bool IsTargetPoorlyDefended(Settlement target, MobileParty attacker)
        {
            float defenseStrength = target.Town?.GarrisonParty?.Party.TotalStrength ?? 0f;
            var nearbyDefenders = GetNearbyAlliedParties(target, 50f, target.MapFaction as Kingdom);
            defenseStrength += nearbyDefenders.Sum(p => p.Party.TotalStrength);

            return attacker.Party.TotalStrength > defenseStrength * 1.5f;
        }

        private static IEnumerable<MobileParty> GetNearbyEnemyParties(MobileParty party, float radius)
        {
            var kingdom = party.LeaderHero?.Clan?.Kingdom;
            if (kingdom == null) return Enumerable.Empty<MobileParty>();

            var enemies = FactionManager.GetEnemyKingdoms(kingdom);
            return enemies.SelectMany(e => e.AllParties)
                            .Where(p => p.IsLordParty && p.Position2D.Distance(party.Position2D) <= radius);
        }

        private static IEnumerable<MobileParty> GetNearbyAlliedParties(MobileParty party, float radius)
        {
            var kingdom = party.LeaderHero?.Clan?.Kingdom;
            return GetNearbyAlliedParties(party, radius, kingdom);
        }

        private static IEnumerable<MobileParty> GetNearbyAlliedParties(MobileParty party, float radius, Kingdom kingdom)
        {
            if (kingdom == null) return Enumerable.Empty<MobileParty>();

            return kingdom.AllParties.Where(p =>
                p.IsLordParty && p != party &&
                p.Position2D.Distance(party.Position2D) <= radius);
        }

        private static IEnumerable<MobileParty> GetNearbyAlliedParties(Settlement settlement, float radius, Kingdom kingdom)
        {
            if (kingdom == null) return Enumerable.Empty<MobileParty>();

            return kingdom.AllParties.Where(p =>
                p.IsLordParty && p.Position2D.Distance(settlement.Position2D) <= radius);
        }

        private static IEnumerable<Settlement> GetBorderSettlements(Kingdom kingdom)
        {
            return kingdom.Settlements.Where(s =>
                Kingdom.All.Any(k => k != kingdom && s.IsBorderSettlementWith(k)));
        }
    }
}