using System;
using System.Collections.Generic;
using System.Linq;

using Helpers;

using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;

using static TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors.ArmyCoordinationManager;

using MathF = TaleWorlds.Library.MathF;

namespace TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors
{
    // Token: 0x02000404 RID: 1028
    public class EnhancedAiMilitaryBehavior : CampaignBehaviorBase
    {
        private ArmyCoordinationManager _coordinationManager;

        // Token: 0x06003EEF RID: 16111 RVA: 0x00135764 File Offset: 0x00133964
        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, new Action<MobileParty, Settlement, Hero>(this.OnSettlementEntered));
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, new Action<MobileParty, PartyThinkParams>(this.AiHourlyTick));
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.OnSessionLaunched));
        }

        // Token: 0x06003EF0 RID: 16112 RVA: 0x001357B6 File Offset: 0x001339B6
        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            this._disbandPartyCampaignBehavior = Campaign.Current.GetCampaignBehavior<IDisbandPartyCampaignBehavior>();
            this._coordinationManager = Campaign.Current.GetCampaignBehavior<ArmyCoordinationManager>();
        }

        // Token: 0x06003EF1 RID: 16113 RVA: 0x001357C8 File Offset: 0x001339C8
        public override void SyncData(IDataStore dataStore)
        {
        }

        // Token: 0x06003EF2 RID: 16114 RVA: 0x001357CA File Offset: 0x001339CA
        private void OnSettlementEntered(MobileParty mobileParty, Settlement settlement, Hero hero)
        {
            if (mobileParty != null && mobileParty.IsBandit && settlement.IsHideout && mobileParty.DefaultBehavior != AiBehavior.GoToSettlement)
            {
                mobileParty.Ai.SetMoveGoToSettlement(settlement);
            }
        }

        // Token: 0x06003EF3 RID: 16115 RVA: 0x001357F4 File Offset: 0x001339F4
        // Token: 0x06003EF3 RID: 16115 RVA: 0x001357F4 File Offset: 0x001339F4
        private void FindBestTargetAndItsValueForFaction(Army.ArmyTypes missionType, PartyThinkParams p, float ourStrength, float newArmyCreatingAdditionalConstant = 1f)
        {
            MobileParty mobilePartyOf = p.MobilePartyOf;
            IFaction mapFaction = mobilePartyOf.MapFaction;
            if (mobilePartyOf.Army != null && mobilePartyOf.Army.LeaderParty != mobilePartyOf)
            {
                return;
            }

            // ARMY RESTRICTION: Only allow armies for siege and defense operations
            if ((missionType == Army.ArmyTypes.Raider || missionType == Army.ArmyTypes.Patrolling) && newArmyCreatingAdditionalConstant > 1.01f)
            {
                // This is army creation mode for raiding/patrolling - skip it
                return;
            }

            float x;
            if (mobilePartyOf.Army != null)
            {
                float num = 0f;
                foreach (MobileParty mobileParty in mobilePartyOf.Army.Parties)
                {
                    float num2 = (PartyBaseHelper.FindPartySizeNormalLimit(mobileParty) + 1f) * 0.5f;
                    float num3 = mobileParty.PartySizeRatio / num2;
                    num += num3;
                }
                x = num / (float) mobilePartyOf.Army.Parties.Count;
            }
            else if (newArmyCreatingAdditionalConstant <= 1.01f)
            {
                float num4 = (PartyBaseHelper.FindPartySizeNormalLimit(mobilePartyOf) + 1f) * 0.5f;
                x = mobilePartyOf.PartySizeRatio / num4;
            }
            else
            {
                x = 1f;
            }
            float num5 = MathF.Max(1f, MathF.Min((float) mobilePartyOf.MapFaction.Fiefs.Count / 5f, 2.5f));
            if (missionType == Army.ArmyTypes.Defender)
            {
                num5 = MathF.Pow(num5, 0.75f);
            }
            float partySizeScore = MathF.Min(1f, MathF.Pow(x, num5));
            AiBehavior aiBehavior = AiBehavior.Hold;
            switch (missionType)
            {
                case Army.ArmyTypes.Besieger:
                    aiBehavior = AiBehavior.BesiegeSettlement;
                    break;
                case Army.ArmyTypes.Raider:
                    aiBehavior = AiBehavior.RaidSettlement;
                    break;
                case Army.ArmyTypes.Defender:
                    aiBehavior = AiBehavior.DefendSettlement;
                    break;
                case Army.ArmyTypes.Patrolling:
                    aiBehavior = AiBehavior.PatrolAroundPoint;
                    break;
            }

            // Enhanced priority system: Only consider raiding/patrolling if no high-priority strategic objectives exist
            if (missionType == Army.ArmyTypes.Raider || missionType == Army.ArmyTypes.Patrolling)
            {
                // For individual parties: significantly reduce priority if strategic objectives exist
                if (HasHighPriorityObjectives(mobilePartyOf, mapFaction))
                {
                    partySizeScore *= 0.1f; // Very low priority when strategic objectives exist
                }

                // For existing armies: completely avoid raiding/patrolling missions
                if (mobilePartyOf.Army != null)
                {
                    return; // Armies should not raid or patrol
                }
            }

            if (missionType == Army.ArmyTypes.Defender || missionType == Army.ArmyTypes.Patrolling)
            {
                this.CalculateMilitaryBehaviorForFactionSettlementsParallel(mapFaction, p, missionType, aiBehavior, ourStrength, partySizeScore, newArmyCreatingAdditionalConstant);
                return;
            }
            foreach (IFaction faction in FactionManager.GetEnemyFactions(mapFaction))
            {
                this.CalculateMilitaryBehaviorForFactionSettlementsParallel(faction, p, missionType, aiBehavior, ourStrength, partySizeScore, newArmyCreatingAdditionalConstant);
            }
        }

        private bool HasHighPriorityObjectives(MobileParty party, IFaction faction)
        {
            // 1) CHECK FOR CLAN SETTLEMENTS UNDER ATTACK - HIGHEST PRIORITY
            if (HasClanSettlementUnderAttack(party))
                return true;

            if (_coordinationManager == null) return false;

            // Check if there are any siege or defense coordination objectives for this faction
            var enemyFactions = FactionManager.GetEnemyFactions(faction);
            var threatenedSettlements = faction.Settlements.Where(s => s.IsFortification);

            // Check for valuable enemy targets that could be besieged
            foreach (var enemyFaction in enemyFactions)
            {
                foreach (var settlement in enemyFaction.Settlements.Where(s => s.IsFortification))
                {
                    float coordinationBonus = _coordinationManager?.GetCoordinationBonus(party, settlement, Army.ArmyTypes.Besieger) ?? 1f;
                    if (coordinationBonus > 1.1f) // Has coordination objective
                        return true;
                }
            }

            // Check for threatened own settlements
            foreach (var settlement in threatenedSettlements)
            {
                float coordinationBonus = _coordinationManager?.GetCoordinationBonus(party, settlement, Army.ArmyTypes.Defender) ?? 1f;
                if (coordinationBonus > 1.1f) // Has coordination objective
                    return true;
            }

            return false;
        }

        // NEW FEATURE 1: Check if party's clan has a settlement under attack
        private bool HasClanSettlementUnderAttack(MobileParty party)
        {
            if (party?.LeaderHero?.Clan == null) return false;

            foreach (var settlement in party.LeaderHero.Clan.Settlements)
            {
                if (IsSettlementUnderAttack(settlement))
                    return true;
            }
            return false;
        }

        // Helper method to determine if a settlement is under attack
        private bool IsSettlementUnderAttack(Settlement settlement)
        {
            if (settlement == null) return false;

            // Active siege
            if (settlement.SiegeEvent != null)
                return true;

            // Recent attacker still active and nearby
            if (settlement.LastAttackerParty != null &&
                settlement.LastAttackerParty.IsActive &&
                Campaign.Current.Models.MapDistanceModel.GetDistance(settlement.LastAttackerParty, settlement) <= 50f)
                return true;

            // Check for nearby enemy armies threatening the settlement
            var enemyFactions = FactionManager.GetEnemyFactions(settlement.MapFaction);
            foreach (var faction in enemyFactions)
            {
                if (faction is Kingdom kingdom)
                {
                    foreach (var army in kingdom.Armies)
                    {
                        if (army.LeaderParty != null &&
                            Campaign.Current.Models.MapDistanceModel.GetDistance(army.LeaderParty, settlement) <= 80f)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        // Token: 0x06003EF4 RID: 16116 RVA: 0x001359D0 File Offset: 0x00133BD0
        private void CalculateMilitaryBehaviorForFactionSettlementsParallel(IFaction faction, PartyThinkParams p, Army.ArmyTypes missionType, AiBehavior aiBehavior, float ourStrength, float partySizeScore, float newArmyCreatingAdditionalConstant)
        {
            MobileParty mobilePartyOf = p.MobilePartyOf;
            int count = faction.Settlements.Count;
            float totalStrength = faction.TotalStrength;
            for (int i = 0; i < faction.Settlements.Count; i++)
            {
                Settlement settlement = faction.Settlements[i];
                if (this.CheckIfSettlementIsSuitableForMilitaryAction(settlement, mobilePartyOf, missionType))
                {
                    this.CalculateMilitaryBehaviorForSettlement(settlement, missionType, aiBehavior, p, ourStrength, partySizeScore, count, totalStrength, newArmyCreatingAdditionalConstant);
                }
            }

            // NEW FEATURE 2: Add siege support patrol opportunities for individual parties
            if (missionType == Army.ArmyTypes.Patrolling && mobilePartyOf.Army == null)
            {
                AddSiegeSupportPatrolOpportunities(faction, p, aiBehavior, ourStrength, partySizeScore, newArmyCreatingAdditionalConstant);
            }
        }

        // NEW FEATURE 2: Add siege support patrol opportunities
        private void AddSiegeSupportPatrolOpportunities(IFaction faction, PartyThinkParams p, AiBehavior aiBehavior, float ourStrength, float partySizeScore, float newArmyCreatingAdditionalConstant)
        {
            MobileParty mobilePartyOf = p.MobilePartyOf;

            // Only for individual parties (not armies)
            if (mobilePartyOf.Army != null) return;

            // Find ongoing sieges by this faction
            var enemyFactions = FactionManager.GetEnemyFactions(faction);
            foreach (var enemyFaction in enemyFactions)
            {
                foreach (var settlement in enemyFaction.Settlements.Where(s => s.IsFortification))
                {
                    // Check if this settlement is being sieged by our faction
                    if (settlement.SiegeEvent != null &&
                        settlement.SiegeEvent.BesiegerCamp?.LeaderParty?.MapFaction == faction)
                    {
                        // Calculate if this party can provide meaningful support
                        float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(mobilePartyOf, settlement);
                        if (distance <= 150f) // Within reasonable support range
                        {
                            float supportValue = CalculateSiegeSupportValue(mobilePartyOf, settlement);
                            if (supportValue > 0.5f) // Minimum threshold for support
                            {
                                // Create a patrol behavior for siege support
                                float siegeSupportScore = supportValue * partySizeScore * newArmyCreatingAdditionalConstant;

                                // Apply modest bonus (not too strong to preserve balance)
                                siegeSupportScore *= 1.5f; // 50% bonus for siege support

                                AIBehaviorTuple siegeSupportTuple = new AIBehaviorTuple(settlement, aiBehavior, false);
                                ValueTuple<AIBehaviorTuple, float> valueTuple = new ValueTuple<AIBehaviorTuple, float>(siegeSupportTuple, siegeSupportScore);
                                p.AddBehaviorScore(valueTuple);
                            }
                        }
                    }
                }
            }
        }

        // Calculate how valuable this party would be for siege support
        private float CalculateSiegeSupportValue(MobileParty party, Settlement besiegedSettlement)
        {
            if (party == null || besiegedSettlement?.SiegeEvent == null) return 0f;

            // Base value from party strength relative to siege scale
            float partyStrength = party.Party.TotalStrength;
            float siegeScale = besiegedSettlement.SiegeEvent.BesiegerCamp.GetInvolvedPartiesForEventType(MapEvent.BattleTypes.Siege)
                .Sum(p => p.TotalStrength);

            // Party should be meaningful but not overwhelmingly large
            float strengthRatio = partyStrength / Math.Max(siegeScale * 0.1f, 100f); // At least 10% of siege strength
            float baseValue = MathF.Min(strengthRatio, 2f); // Cap at 2.0

            // Distance factor - closer is better
            float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(party, besiegedSettlement);
            float distanceFactor = Math.Max(0.2f, 1f - (distance / 150f));

            // Settlement importance factor
            float importanceFactor = 1f;
            if (besiegedSettlement.IsTown)
                importanceFactor = 1.3f;
            else if (besiegedSettlement.IsCastle)
                importanceFactor = 1.1f;

            return baseValue * distanceFactor * importanceFactor;
        }

        // Token: 0x06003EF5 RID: 16117 RVA: 0x00135A3C File Offset: 0x00133C3C
        private bool CheckIfSettlementIsSuitableForMilitaryAction(Settlement settlement, MobileParty mobileParty, Army.ArmyTypes missionType)
        {
            if (Game.Current.CheatMode && !CampaignCheats.MainPartyIsAttackable && settlement.Party.MapEvent != null && settlement.Party.MapEvent == MapEvent.PlayerMapEvent)
            {
                return false;
            }
            if (((mobileParty.DefaultBehavior == AiBehavior.BesiegeSettlement && missionType == Army.ArmyTypes.Besieger) || (mobileParty.DefaultBehavior == AiBehavior.RaidSettlement && missionType == Army.ArmyTypes.Raider) || (mobileParty.DefaultBehavior == AiBehavior.DefendSettlement && missionType == Army.ArmyTypes.Defender)) && mobileParty.TargetSettlement == settlement)
            {
                return false;
            }
            if (missionType == Army.ArmyTypes.Raider)
            {
                float num = MathF.Max(100f, MathF.Min(250f, Campaign.Current.Models.MapDistanceModel.GetDistance(mobileParty.MapFaction.FactionMidSettlement, settlement.MapFaction.FactionMidSettlement)));
                if (Campaign.Current.Models.MapDistanceModel.GetDistance(mobileParty, settlement) > num)
                {
                    return false;
                }
            }
            return true;
        }

        // Token: 0x06003EF6 RID: 16118 RVA: 0x00135B10 File Offset: 0x00133D10
        private void CalculateMilitaryBehaviorForSettlement(Settlement settlement, Army.ArmyTypes missionType, AiBehavior aiBehavior, PartyThinkParams p, float ourStrength, float partySizeScore, int numberOfEnemyFactionSettlements, float totalEnemyMobilePartyStrength, float newArmyCreatingAdditionalConstant = 1f)
        {
            if ((missionType == Army.ArmyTypes.Defender && settlement.LastAttackerParty != null && settlement.LastAttackerParty.IsActive) || (missionType == Army.ArmyTypes.Raider && settlement.IsVillage && settlement.Village.VillageState == Village.VillageStates.Normal) || (missionType == Army.ArmyTypes.Besieger && settlement.IsFortification && (settlement.SiegeEvent == null || settlement.SiegeEvent.BesiegerCamp.LeaderParty.MapFaction == p.MobilePartyOf.MapFaction)) || (missionType == Army.ArmyTypes.Patrolling && !settlement.IsCastle && p.WillGatherAnArmy))
            {
                MobileParty mobilePartyOf = p.MobilePartyOf;
                IFaction mapFaction = mobilePartyOf.MapFaction;
                float num = mobilePartyOf.Food;
                float num2 = -mobilePartyOf.FoodChange;
                if (mobilePartyOf.Army != null && mobilePartyOf == mobilePartyOf.Army.LeaderParty)
                {
                    foreach (MobileParty mobileParty in mobilePartyOf.Army.LeaderParty.AttachedParties)
                    {
                        num += mobileParty.Food;
                        num2 += -mobileParty.FoodChange;
                    }
                }
                float num3 = MathF.Max(0f, num) / num2;
                float num4 = (num3 < 5f) ? (0.1f + 0.9f * (num3 / 5f)) : 1f;
                float num5 = (missionType != Army.ArmyTypes.Patrolling) ? Campaign.Current.Models.TargetScoreCalculatingModel.GetTargetScoreForFaction(settlement, missionType, mobilePartyOf, ourStrength, numberOfEnemyFactionSettlements, totalEnemyMobilePartyStrength) : Campaign.Current.Models.TargetScoreCalculatingModel.CalculatePatrollingScoreForSettlement(settlement, mobilePartyOf);

                // Apply coordination bonus - this is the key integration point
                float coordinationBonus = _coordinationManager?.GetCoordinationBonus(mobilePartyOf, settlement, missionType) ?? 1f;

                // NEW FEATURE 1: Apply massive priority boost for clan settlements under attack
                float clanDefenseBonus = CalculateClanDefenseBonus(mobilePartyOf, settlement, missionType);

                num5 *= partySizeScore * num4 * newArmyCreatingAdditionalConstant * coordinationBonus * clanDefenseBonus;

                if (mobilePartyOf.Objective == MobileParty.PartyObjective.Defensive)
                {
                    if (aiBehavior == AiBehavior.DefendSettlement || (aiBehavior == AiBehavior.PatrolAroundPoint && settlement.MapFaction == mapFaction))
                    {
                        num5 *= 1.2f;
                    }
                    else
                    {
                        num5 *= 0.8f;
                    }
                }
                else if (mobilePartyOf.Objective == MobileParty.PartyObjective.Aggressive)
                {
                    if (aiBehavior == AiBehavior.BesiegeSettlement || aiBehavior == AiBehavior.RaidSettlement)
                    {
                        num5 *= 1.2f;
                    }
                    else
                    {
                        num5 *= 0.8f;
                    }
                }
                if (!mobilePartyOf.IsDisbanding)
                {
                    IDisbandPartyCampaignBehavior disbandPartyCampaignBehavior = this._disbandPartyCampaignBehavior;
                    if (disbandPartyCampaignBehavior == null || !disbandPartyCampaignBehavior.IsPartyWaitingForDisband(mobilePartyOf))
                    {
                        goto IL_209;
                    }
                }
                num5 *= 0.25f;
            IL_209:
                AIBehaviorTuple aibehaviorTuple = new AIBehaviorTuple(settlement, aiBehavior, p.WillGatherAnArmy);
                ValueTuple<AIBehaviorTuple, float> valueTuple = new ValueTuple<AIBehaviorTuple, float>(aibehaviorTuple, num5);
                p.AddBehaviorScore(valueTuple);
            }
        }

        // NEW FEATURE 1: Calculate clan defense bonus
        private float CalculateClanDefenseBonus(MobileParty party, Settlement settlement, Army.ArmyTypes missionType)
        {
            // Only apply to defense missions
            if (missionType != Army.ArmyTypes.Defender) return 1f;

            // Check if this is the party's clan settlement
            if (party?.LeaderHero?.Clan == null || settlement?.OwnerClan != party.LeaderHero.Clan)
                return 1f;

            // Check if settlement is under attack
            if (!IsSettlementUnderAttack(settlement))
                return 1f;

            // FIXED: Scale base bonus with settlement importance
            float baseBonus = settlement.IsTown ? 4f : (settlement.IsCastle ? 3f : 2f);

            // FIXED: Scale with prosperity instead of hard-coded values
            float prosperityFactor = 1f;
            if (settlement.Town?.Prosperity > 0f)
            {
                prosperityFactor = 1f + (settlement.Town.Prosperity / 10000f) * 0.5f; // 0-1 prosperity ratio to 1.0-1.5x
            }

            // FIXED: Scale distance penalty smoothly
            float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(party, settlement);
            float kingdomRadius = CalculateKingdomRadius(party.MapFaction);
            float distanceFactor = Math.Max(0.3f, 1f - (distance / kingdomRadius)); // Smooth falloff

            float finalBonus = baseBonus * prosperityFactor * distanceFactor;

            // FIXED: Scale cap with kingdom size
            int kingdomSize = party.MapFaction?.Settlements?.Count ?? 5;
            float maxMultiplier = Math.Max(10f, kingdomSize * 1.5f);

            return Math.Min(finalBonus, maxMultiplier);
        }

        // Helper method for kingdom size scaling
        private float CalculateKingdomRadius(IFaction faction)
        {
            if (faction?.Settlements == null || faction.Settlements.Count <= 1) return 200f;

            var center = faction.FactionMidSettlement;
            if (center == null) return 200f;

            float maxDistance = faction.Settlements
                .Select(s => Campaign.Current.Models.MapDistanceModel.GetDistance(center, s))
                .DefaultIfEmpty(200f)
                .Max();

            return Math.Max(200f, maxDistance);
        }

        // Token: 0x06003EF7 RID: 16119 RVA: 0x00135D5C File Offset: 0x00133F5C
        private void AiHourlyTick(MobileParty mobileParty, PartyThinkParams p)
        {
            if (mobileParty.IsMilitia || mobileParty.IsCaravan || mobileParty.IsVillager || mobileParty.IsBandit || mobileParty.IsDisbanding || mobileParty.LeaderHero == null || (mobileParty.MapFaction != Clan.PlayerClan.MapFaction && !mobileParty.MapFaction.IsKingdomFaction))
            {
                return;
            }
            Settlement currentSettlement = mobileParty.CurrentSettlement;
            if (((currentSettlement != null) ? currentSettlement.SiegeEvent : null) != null)
            {
                return;
            }
            if (mobileParty.Army != null)
            {
                mobileParty.Ai.SetInitiative(0.33f, 0.33f, 24f);
                if (mobileParty.Army.LeaderParty == mobileParty && (mobileParty.Army.AIBehavior == Army.AIBehaviorFlags.Gathering || mobileParty.Army.AIBehavior == Army.AIBehaviorFlags.WaitingForArmyMembers))
                {
                    mobileParty.Ai.SetInitiative(0.33f, 1f, 24f);
                    p.DoNotChangeBehavior = true;
                }
                else if (mobileParty.Army.AIBehavior == Army.AIBehaviorFlags.Patrolling)
                {
                    mobileParty.Ai.SetInitiative(1f, 1f, 24f);
                }
                else if (mobileParty.Army.AIBehavior == Army.AIBehaviorFlags.Defending && mobileParty.Army.LeaderParty == mobileParty && mobileParty.Army.AiBehaviorObject != null && mobileParty.Army.AiBehaviorObject is Settlement && ((Settlement) mobileParty.Army.AiBehaviorObject).GatePosition.DistanceSquared(mobileParty.Position2D) < 100f)
                {
                    mobileParty.Ai.SetInitiative(1f, 1f, 24f);
                }
                if (mobileParty.Army.LeaderParty != mobileParty)
                {
                    return;
                }
            }
            else if (mobileParty.DefaultBehavior == AiBehavior.DefendSettlement || mobileParty.Objective == MobileParty.PartyObjective.Defensive)
            {
                mobileParty.Ai.SetInitiative(0.33f, 1f, 2f);
            }

            // NEW FEATURE 1: Check for clan settlements under attack and boost defensive initiative
            if (mobileParty.Army == null && HasClanSettlementUnderAttack(mobileParty))
            {
                mobileParty.Ai.SetInitiative(0.8f, 1f, 1f); // High initiative for clan defense
            }

            float x3;
            if (mobileParty.Army != null)
            {
                float num = 0f;
                foreach (MobileParty mobileParty2 in mobileParty.Army.Parties)
                {
                    float num2 = (PartyBaseHelper.FindPartySizeNormalLimit(mobileParty2) + 1f) * 0.5f;
                    float num3 = mobileParty2.PartySizeRatio / num2;
                    num += num3;
                }
                x3 = num / (float) mobileParty.Army.Parties.Count;
            }
            else
            {
                float num4 = (PartyBaseHelper.FindPartySizeNormalLimit(mobileParty) + 1f) * 0.5f;
                x3 = mobileParty.PartySizeRatio / num4;
            }
            float y = MathF.Max(1f, MathF.Min((float) mobileParty.MapFaction.Fiefs.Count / 5f, 2.5f));
            float num5 = MathF.Min(1f, MathF.Pow(x3, y));
            float num6 = mobileParty.Food;
            float num7 = -mobileParty.FoodChange;
            int num8 = 1;
            if (mobileParty.Army != null && mobileParty == mobileParty.Army.LeaderParty)
            {
                foreach (MobileParty mobileParty3 in mobileParty.Army.LeaderParty.AttachedParties)
                {
                    num6 += mobileParty3.Food;
                    num7 += -mobileParty3.FoodChange;
                    num8++;
                }
            }
            float num9 = MathF.Max(0f, num6) / num7;
            float num10 = (num9 < 5f) ? (0.1f + 0.9f * (num9 / 5f)) : 1f;
            float totalStrengthWithFollowers = mobileParty.GetTotalStrengthWithFollowers(false);
            if ((mobileParty.DefaultBehavior == AiBehavior.BesiegeSettlement || mobileParty.DefaultBehavior == AiBehavior.RaidSettlement || mobileParty.DefaultBehavior == AiBehavior.DefendSettlement) && mobileParty.TargetSettlement != null)
            {
                float num11 = Campaign.Current.Models.TargetScoreCalculatingModel.CurrentObjectiveValue(mobileParty);
                num11 *= ((mobileParty.MapEvent == null || mobileParty.SiegeEvent == null) ? (num10 * num5) : 1f);
                if (mobileParty.SiegeEvent != null)
                {
                    float num12 = 0f;
                    foreach (PartyBase partyBase in ((mobileParty.DefaultBehavior == AiBehavior.BesiegeSettlement) ? mobileParty.SiegeEvent.BesiegedSettlement.GetInvolvedPartiesForEventType(MapEvent.BattleTypes.Siege) : mobileParty.SiegeEvent.BesiegerCamp.GetInvolvedPartiesForEventType(MapEvent.BattleTypes.Siege)))
                    {
                        num12 += partyBase.TotalStrength;
                    }
                    float x2 = totalStrengthWithFollowers / num12;
                    float num13 = MathF.Max(1f, MathF.Pow(x2, 1.75f) * 0.15f);
                    num11 *= num13;
                }
                if (!mobileParty.IsDisbanding)
                {
                    IDisbandPartyCampaignBehavior disbandPartyCampaignBehavior = this._disbandPartyCampaignBehavior;
                    if (disbandPartyCampaignBehavior == null || !disbandPartyCampaignBehavior.IsPartyWaitingForDisband(mobileParty))
                    {
                        goto IL_48D;
                    }
                }
                num11 *= 0.25f;
            IL_48D:
                p.CurrentObjectiveValue = num11;
                AiBehavior defaultBehavior = mobileParty.DefaultBehavior;
                AIBehaviorTuple aibehaviorTuple = new AIBehaviorTuple(mobileParty.TargetSettlement, defaultBehavior, false);
                ValueTuple<AIBehaviorTuple, float> valueTuple = new ValueTuple<AIBehaviorTuple, float>(aibehaviorTuple, num11);
                p.AddBehaviorScore(valueTuple);
            }
            p.Initialization();
            bool flag = false;
            float newArmyCreatingAdditionalConstant = 1f;
            float num14 = totalStrengthWithFollowers;

            // ENHANCED: Check strategic need for army creation
            if (mobileParty.LeaderHero != null && mobileParty.Army == null && mobileParty.LeaderHero.Clan != null && mobileParty.PartySizeRatio > 0.6f && (mobileParty.LeaderHero.Clan.Leader == mobileParty.LeaderHero || (mobileParty.LeaderHero.Clan.Leader.PartyBelongedTo == null && mobileParty.LeaderHero.Clan.WarPartyComponents != null && Enumerable.FirstOrDefault<WarPartyComponent>(mobileParty.LeaderHero.Clan.WarPartyComponents) == mobileParty.WarPartyComponent)))
            {
                int traitLevel = mobileParty.LeaderHero.GetTraitLevel(DefaultTraits.Calculating);
                IFaction mapFaction = mobileParty.MapFaction;
                Kingdom kingdom = (Kingdom) mapFaction;
                int count = ((Kingdom) mapFaction).Armies.Count;

                // ENHANCED: Check strategic need for army creation
                float strategicArmyBonus = CalculateStrategicArmyCreationBonus(mobileParty);

                // Initialize the out variable to null first
                StrategicObjective? strategicObjective = null;
                bool hasStrategicNeed = _coordinationManager?.ShouldCreateArmy(mobileParty, out strategicObjective) ?? false;

                // Modify influence requirement based on strategic need
                int baseInfluenceNeed = 50 + count * count * 20 + mobileParty.LeaderHero.RandomInt(20) + traitLevel * 20;
                if (hasStrategicNeed && strategicObjective != null)
                {
                    // UPDATED: Use simplified enum values
                    // Reduce influence requirement for strategic needs
                    if (strategicObjective.Type == ObjectiveType.Defend)
                        baseInfluenceNeed = (int) (baseInfluenceNeed * 0.6f); // 40% reduction for defense
                    else
                        baseInfluenceNeed = (int) (baseInfluenceNeed * 0.8f); // 20% reduction for other strategic needs
                }

                int num15 = baseInfluenceNeed;
                float num16 = 1f - (float) count * 0.2f;

                // ENHANCED: Strategic need can override some restrictions
                bool hasInfluence = mobileParty.LeaderHero.Clan.Influence > (float) num15;
                bool hasEnemies = FactionManager.GetEnemyFactions(mobileParty.MapFaction as Kingdom).AnyQ((IFaction x) => Enumerable.Any<Town>(x.Fiefs));
                bool isNotMercenary = !mobileParty.LeaderHero.Clan.IsUnderMercenaryService;

                // UPDATED: Use simplified enum values
                // Allow army creation if strategic need is urgent (defensive with high priority)
                bool strategicOverride = hasStrategicNeed && strategicObjective != null &&
                                       strategicObjective.Type == ObjectiveType.Defend &&
                                       strategicObjective.Priority >= 2f;

                flag = (hasInfluence && mobileParty.MapFaction.IsKingdomFaction && isNotMercenary && hasEnemies) || strategicOverride;

                if (flag)
                {
                    float num17 = (kingdom.Armies.Count == 0) ? (1f + MathF.Sqrt((float) ((int) CampaignTime.Now.ToDays - kingdom.LastArmyCreationDay)) * 0.15f) : 1f;
                    float num18 = (10f + MathF.Sqrt(MathF.Min(900f, mobileParty.LeaderHero.Clan.Influence))) / 50f;
                    float num19 = MathF.Sqrt(mobileParty.PartySizeRatio);

                    // ENHANCED: Apply strategic bonus
                    newArmyCreatingAdditionalConstant = num17 * num18 * num16 * num19 * strategicArmyBonus;

                    num14 = mobileParty.Party.TotalStrength;
                    List<MobileParty> mobilePartiesToCallToArmy = Campaign.Current.Models.ArmyManagementCalculationModel.GetMobilePartiesToCallToArmy(mobileParty);
                    if (mobilePartiesToCallToArmy.Count == 0)
                    {
                        flag = false;
                    }
                    else
                    {
                        foreach (MobileParty mobileParty4 in mobilePartiesToCallToArmy)
                        {
                            num14 += mobileParty4.Party.TotalStrength;
                        }
                    }
                }
            }
            for (int i = 0; i < 4; i++)
            {
                // ARMY RESTRICTION: Only create armies for siege (0) and defense (2) operations
                Army.ArmyTypes currentMissionType = (Army.ArmyTypes) i;

                if (flag && (currentMissionType == Army.ArmyTypes.Besieger || currentMissionType == Army.ArmyTypes.Defender))
                {
                    p.WillGatherAnArmy = true;
                    this.FindBestTargetAndItsValueForFaction(currentMissionType, p, num14, newArmyCreatingAdditionalConstant);
                }

                // Individual party behavior (all mission types allowed)
                p.WillGatherAnArmy = false;
                this.FindBestTargetAndItsValueForFaction(currentMissionType, p, totalStrengthWithFollowers, 1f);
            }
        }

        // Add this method to the EnhancedAiMilitaryBehavior class

        private float CalculateStrategicArmyCreationBonus(MobileParty mobileParty)
        {
            if (_coordinationManager == null) return 1f;

            // FIXED: Use the GetArmyCreationPriorityBonus method that exists in ArmyCoordinationManager
            float strategicBonus = _coordinationManager.GetArmyCreationPriorityBonus(mobileParty);

            // Check if this party's clan has urgent defensive needs
            if (HasClanSettlementUnderAttack(mobileParty))
            {
                strategicBonus *= 2f; // Double bonus for clan defense
            }

            // Check if kingdom really needs this army
            if (mobileParty.MapFaction is Kingdom kingdom)
            {
                // If kingdom already has many armies relative to its size, reduce bonus
                int armyCount = kingdom.Armies.Count;
                int settlementCount = kingdom.Settlements.Count;

                if (armyCount > settlementCount / 2) // More than 1 army per 2 settlements
                {
                    strategicBonus *= 0.7f; // Reduce enthusiasm for more armies
                }
            }

            return strategicBonus;
        }

        // Modify the army creation section in AiHourlyTick method

        // Token: 0x04001269 RID: 4713
        private const int MinimumInfluenceNeededToCreateArmy = 50;

        // Token: 0x0400126A RID: 4714
        private IDisbandPartyCampaignBehavior _disbandPartyCampaignBehavior;
    }
}