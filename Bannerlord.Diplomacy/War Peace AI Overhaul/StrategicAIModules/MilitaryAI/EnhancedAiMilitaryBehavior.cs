using HarmonyLib;

using Helpers;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;

using MathF = TaleWorlds.Library.MathF;

namespace TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors
{
    [HarmonyPatch(typeof(AiMilitaryBehavior), "RegisterEvents")]
    public class Patch_DisableAiMilitaryBehavior
    {
        public static bool Prefix()
        {
            return false;
        }
    }
    // Token: 0x02000404 RID: 1028
    public class EnhancedAiMilitaryBehavior : CampaignBehaviorBase
    {
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
        private void FindBestTargetAndItsValueForFaction(Army.ArmyTypes missionType, PartyThinkParams p, float ourStrength, float newArmyCreatingAdditionalConstant = 1f)
        {
            MobileParty mobilePartyOf = p.MobilePartyOf;
            IFaction mapFaction = mobilePartyOf.MapFaction;
            if (mobilePartyOf.Army != null && mobilePartyOf.Army.LeaderParty != mobilePartyOf)
            {
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

            // MODIFICATION: Handle priority defense of owned settlements under attack
            if (missionType == Army.ArmyTypes.Defender)
            {
                // First, check for owned settlements under attack with high priority
                foreach (Settlement ownedSettlement in mapFaction.Settlements)
                {
                    if (this.IsSettlementUnderAttackOrRaid(ownedSettlement))
                    {
                        // Force calculation for owned settlements under attack with priority boost
                        this.CalculateMilitaryBehaviorForSettlement(ownedSettlement, missionType, aiBehavior, p, ourStrength, partySizeScore,
                            mapFaction.Settlements.Count, mapFaction.TotalStrength, newArmyCreatingAdditionalConstant, true);
                    }
                }

                // Then proceed with normal defense calculations
                this.CalculateMilitaryBehaviorForFactionSettlementsParallel(mapFaction, p, missionType, aiBehavior, ourStrength, partySizeScore, newArmyCreatingAdditionalConstant);
                return;
            }

            if (missionType == Army.ArmyTypes.Patrolling)
            {
                this.CalculateMilitaryBehaviorForFactionSettlementsParallel(mapFaction, p, missionType, aiBehavior, ourStrength, partySizeScore, newArmyCreatingAdditionalConstant);
                return;
            }
            foreach (IFaction faction in FactionManager.GetEnemyFactions(mapFaction))
            {
                this.CalculateMilitaryBehaviorForFactionSettlementsParallel(faction, p, missionType, aiBehavior, ourStrength, partySizeScore, newArmyCreatingAdditionalConstant);
            }
        }

        // MODIFICATION: Helper method to check if settlement is under attack or raid
        private bool IsSettlementUnderAttackOrRaid(Settlement settlement)
        {
            return (settlement.LastAttackerParty != null && settlement.LastAttackerParty.IsActive) ||
                   (settlement.SiegeEvent != null && settlement.SiegeEvent.BesiegerCamp != null) ||
                   (settlement.IsVillage && settlement.Village.VillageState == Village.VillageStates.BeingRaided);
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

            // MODIFICATION: Always allow defending owned settlements under attack
            if (missionType == Army.ArmyTypes.Defender && settlement.MapFaction == mobileParty.MapFaction && this.IsSettlementUnderAttackOrRaid(settlement))
            {
                return true;
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
        private void CalculateMilitaryBehaviorForSettlement(
    Settlement settlement,
    Army.ArmyTypes missionType,
    AiBehavior aiBehavior,
    PartyThinkParams p,
    float ourStrength,
    float partySizeScore,
    int numberOfEnemyFactionSettlements,
    float totalEnemyMobilePartyStrength,
    float newArmyCreatingAdditionalConstant = 1f,
    bool isPriorityDefense = false)
        {
            bool shouldCalculate = false;

            // Original conditions
            if ((missionType == Army.ArmyTypes.Defender && settlement.LastAttackerParty != null && settlement.LastAttackerParty.IsActive) ||
                (missionType == Army.ArmyTypes.Raider && settlement.IsVillage && settlement.Village.VillageState == Village.VillageStates.Normal) ||
                (missionType == Army.ArmyTypes.Besieger && settlement.IsFortification && (settlement.SiegeEvent == null || settlement.SiegeEvent.BesiegerCamp.LeaderParty.MapFaction == p.MobilePartyOf.MapFaction)) ||
                (missionType == Army.ArmyTypes.Patrolling && !settlement.IsCastle && p.WillGatherAnArmy))
            {
                shouldCalculate = true;
            }

            // MODIFICATION: Force calculation for priority defense of owned settlements under attack
            if (isPriorityDefense || (missionType == Army.ArmyTypes.Defender && settlement.MapFaction == p.MobilePartyOf.MapFaction && this.IsSettlementUnderAttackOrRaid(settlement)))
            {
                shouldCalculate = true;
            }

            if (shouldCalculate)
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
                float num5 = (missionType != Army.ArmyTypes.Patrolling)
                    ? Campaign.Current.Models.TargetScoreCalculatingModel.GetTargetScoreForFaction(settlement, missionType, mobilePartyOf, ourStrength, numberOfEnemyFactionSettlements, totalEnemyMobilePartyStrength)
                    : Campaign.Current.Models.TargetScoreCalculatingModel.CalculatePatrollingScoreForSettlement(settlement, mobilePartyOf);
                num5 *= partySizeScore * num4 * newArmyCreatingAdditionalConstant;

                // MODIFICATION: Boost scores for siege and defend operations
                if (missionType == Army.ArmyTypes.Besieger)
                {
                    num5 *= 2.5f; // Increased from 1.5f to 2.5x for siege operations
                }
                else if (missionType == Army.ArmyTypes.Defender)
                {
                    num5 *= 2.0f; // Increased from 1.3f to 2.0x for defend operations

                    // Massive priority boost for defending owned settlements under attack
                    if (settlement.MapFaction == mapFaction && this.IsSettlementUnderAttackOrRaid(settlement))
                    {
                        num5 *= 7.0f; // Increased from 5.0f to 7.0x

                        // Additional boost based on settlement importance
                        if (settlement.IsTown)
                        {
                            num5 *= 2.0f; // Increased from 1.5f to 2.0x for towns
                        }
                        else if (settlement.IsCastle)
                        {
                            num5 *= 1.5f; // Increased from 1.3f to 1.5x for castles
                        }
                    }
                }

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

                // MODIFICATION: Boost current objective scores for siege and defend
                if (mobileParty.DefaultBehavior == AiBehavior.BesiegeSettlement)
                {
                    num11 *= 2.5f; // Increased from 1.5f to 2.5x for siege operations
                }
                else if (mobileParty.DefaultBehavior == AiBehavior.DefendSettlement)
                {
                    num11 *= 2.0f; // Increased from 1.3f to 2.0x for defend operations

                    // Additional massive boost if defending own settlement under attack
                    if (mobileParty.TargetSettlement != null && mobileParty.TargetSettlement.MapFaction == mobileParty.MapFaction && this.IsSettlementUnderAttackOrRaid(mobileParty.TargetSettlement))
                    {
                        num11 *= 7.0f; // Increased from 5.0f to 7.0x
                    }
                }

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
            if (mobileParty.LeaderHero != null && mobileParty.Army == null && mobileParty.LeaderHero.Clan != null && mobileParty.PartySizeRatio > 0.6f && (mobileParty.LeaderHero.Clan.Leader == mobileParty.LeaderHero || (mobileParty.LeaderHero.Clan.Leader.PartyBelongedTo == null && mobileParty.LeaderHero.Clan.WarPartyComponents != null && Enumerable.FirstOrDefault<WarPartyComponent>(mobileParty.LeaderHero.Clan.WarPartyComponents) == mobileParty.WarPartyComponent)))
            {
                int traitLevel = mobileParty.LeaderHero.GetTraitLevel(DefaultTraits.Calculating);
                IFaction mapFaction = mobileParty.MapFaction;
                Kingdom kingdom = (Kingdom) mapFaction;
                int count = ((Kingdom) mapFaction).Armies.Count;
                int num15 = 30 + count * 10 + traitLevel * 10; // Lower base, lower multipliers
                float num16 = 1f - (float) count * 0.2f;
                bool flag2;
                if (mobileParty.LeaderHero.Clan.Influence > (float) num15 && mobileParty.MapFaction.IsKingdomFaction && !mobileParty.LeaderHero.Clan.IsUnderMercenaryService)
                {
                    flag2 = FactionManager.GetEnemyFactions(mobileParty.MapFaction as Kingdom).AnyQ((IFaction x) => Enumerable.Any<Town>(x.Fiefs));
                }
                else
                {
                    flag2 = false;
                }
                flag = flag2;
                if (flag)
                {
                    float num17 = (kingdom.Armies.Count == 0) ? (1f + MathF.Sqrt((float) ((int) CampaignTime.Now.ToDays - kingdom.LastArmyCreationDay)) * 0.15f) : 1f;
                    float num18 = (10f + MathF.Sqrt(MathF.Min(900f, mobileParty.LeaderHero.Clan.Influence))) / 50f;
                    float num19 = MathF.Sqrt(mobileParty.PartySizeRatio);
                    newArmyCreatingAdditionalConstant = num17 * num18 * num16 * num19;
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
                if (flag)
                {
                    p.WillGatherAnArmy = true;
                    this.FindBestTargetAndItsValueForFaction((Army.ArmyTypes) i, p, num14, newArmyCreatingAdditionalConstant);
                }
                p.WillGatherAnArmy = false;
                this.FindBestTargetAndItsValueForFaction((Army.ArmyTypes) i, p, totalStrengthWithFollowers, 1f);
            }

            // --- MODIFICATION: Always add highest-priority defend objective for owned settlements under attack ---
            var mapFactionFinal = mobileParty.MapFaction;
            var clan = mobileParty.LeaderHero?.Clan;
            if (mapFactionFinal != null && clan != null)
            {
                foreach (var ownedSettlement in mapFactionFinal.Settlements)
                {
                    if (ownedSettlement.OwnerClan == clan && this.IsSettlementUnderAttackOrRaid(ownedSettlement))
                    {
                        var defendTuple = new AIBehaviorTuple(ownedSettlement, AiBehavior.DefendSettlement, false);
                        float maxScore = p.AIBehaviorScores.Count > 0 ? p.AIBehaviorScores.Max(bs => bs.Item2) : 1000f;
                        float priorityScore = maxScore + 10000f; // Always higher than any other

                        int idx = p.AIBehaviorScores.FindIndex(bs => bs.Item1.Equals(defendTuple));
                        if (idx >= 0)
                        {
                            if (p.AIBehaviorScores[idx].Item2 < priorityScore)
                                p.AIBehaviorScores[idx] = (defendTuple, priorityScore);
                        }
                        else
                        {
                            p.AddBehaviorScore((defendTuple, priorityScore));
                        }
                    }
                }
            }
        }

        // Token: 0x04001269 RID: 4713
        private const int MinimumInfluenceNeededToCreateArmy = 50;

        // Token: 0x0400126A RID: 4714
        private IDisbandPartyCampaignBehavior _disbandPartyCampaignBehavior;
    }
}