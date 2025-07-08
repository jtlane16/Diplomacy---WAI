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
using TaleWorlds.Library;
using TaleWorlds.LinQuick;

using MathF = TaleWorlds.Library.MathF;

namespace TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors
{
    public class StrategicAiMilitaryBehavior : CampaignBehaviorBase
    {
        // Map of settlement -> total friendly strength that could plausibly contribute
        private Dictionary<Settlement, float> _combinedStrengths = new Dictionary<Settlement, float>();

        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, new Action<MobileParty, Settlement, Hero>(this.OnSettlementEntered));
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, new Action<MobileParty, PartyThinkParams>(this.AiHourlyTick));
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.OnSessionLaunched));
        }

        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            this._disbandPartyCampaignBehavior = Campaign.Current.GetCampaignBehavior<IDisbandPartyCampaignBehavior>();
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSettlementEntered(MobileParty mobileParty, Settlement settlement, Hero hero)
        {
            if (mobileParty != null && mobileParty.IsBandit && settlement.IsHideout && mobileParty.DefaultBehavior != AiBehavior.GoToSettlement)
            {
                mobileParty.Ai.SetMoveGoToSettlement(settlement);
            }
        }

        // --- Combined Strength Calculation ---

        /// <summary>
        /// Build a map of settlements under threat or high-value targets to the sum of all friendly parties/armies that could plausibly contribute.
        /// </summary>
        private void BuildCombinedStrengthsMap(IFaction faction)
        {
            _combinedStrengths.Clear();

            // Only consider settlements under siege/raid or enemy fortifications near the faction
            List<Settlement> relevantSettlements = new List<Settlement>();

            // Own settlements under siege/raid
            foreach (var settlement in faction.Settlements)
            {
                bool isUnderRaid = settlement.LastAttackerParty != null &&
                                   settlement.LastAttackerParty.MapEvent != null &&
                                   settlement.LastAttackerParty.MapEvent.EventType == MapEvent.BattleTypes.Raid;
                if (settlement.IsUnderSiege || isUnderRaid)
                    relevantSettlements.Add(settlement);
            }

            // Enemy fortifications (for overcommitting to sieges)
            foreach (var enemyFaction in FactionManager.GetEnemyFactions(faction))
            {
                foreach (var enemySettlement in enemyFaction.Settlements)
                {
                    if (enemySettlement.IsFortification)
                        relevantSettlements.Add(enemySettlement);
                }
            }

            // Remove duplicates
            relevantSettlements = relevantSettlements.Distinct().ToList();

            // For each relevant settlement, sum up all friendly parties/armies that could plausibly reach and participate
            foreach (var settlement in relevantSettlements)
            {
                float totalStrength = 0f;

                foreach (var party in MobileParty.All)
                {
                    if (!party.IsActive || party.IsBandit || party.IsMilitia || party.IsCaravan || party.IsVillager || party.IsDisbanding)
                        continue;
                    if (party.MapFaction != faction)
                        continue;
                    if (party.LeaderHero == null)
                        continue;

                    // Only consider parties within 60km (tunable) and not already committed to another siege/raid
                    float dist = party.Position2D.Distance(settlement.Position2D);
                    if (dist > 60f)
                        continue;

                    // If party is already besieging/raiding another settlement, skip
                    if (party.DefaultBehavior == AiBehavior.BesiegeSettlement && party.TargetSettlement != null && party.TargetSettlement != settlement)
                        continue;
                    if (party.DefaultBehavior == AiBehavior.RaidSettlement && party.TargetSettlement != null && party.TargetSettlement != settlement)
                        continue;

                    // Army: only count leader party to avoid double-counting
                    if (party.Army != null && party.Army.LeaderParty != party)
                        continue;

                    // Use total strength with followers for armies, otherwise party strength
                    float strength = party.Army != null
                        ? party.Army.Parties.Sum(p => p.Party.TotalStrength)
                        : party.Party.TotalStrength;

                    totalStrength += strength;
                }

                _combinedStrengths[settlement] = totalStrength;
            }
        }

        private float GetCombinedStrengthForObjective(Settlement settlement, float fallbackStrength)
        {
            if (_combinedStrengths.TryGetValue(settlement, out float value))
                return value;
            return fallbackStrength;
        }

        // --- Main AI logic ---

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

            // --- Build combined strengths map once per tick for this faction ---
            BuildCombinedStrengthsMap(mapFaction);

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

        private void CalculateMilitaryBehaviorForFactionSettlementsParallel(IFaction faction, PartyThinkParams p, Army.ArmyTypes missionType, AiBehavior aiBehavior, float ourStrength, float partySizeScore, float newArmyCreatingAdditionalConstant)
        {
            MobileParty mobilePartyOf = p.MobilePartyOf;
            int count = faction.Settlements.Count;
            float totalStrength = faction.TotalStrength;
            for (int i = 0; i < faction.Settlements.Count; i++)
            {
                Settlement settlement = faction.Settlements[i];
                bool isOwnClan = settlement.OwnerClan != null && mobilePartyOf.LeaderHero != null && settlement.OwnerClan == mobilePartyOf.LeaderHero.Clan;
                bool isUnderRaid = settlement.LastAttackerParty != null &&
                                   settlement.LastAttackerParty.MapEvent != null &&
                                   settlement.LastAttackerParty.MapEvent.EventType == MapEvent.BattleTypes.Raid;

                // Use combined strength for this objective if available
                float combinedStrength = GetCombinedStrengthForObjective(settlement, ourStrength);

                if (this.CheckIfSettlementIsSuitableForMilitaryAction(settlement, mobilePartyOf, missionType))
                {
                    this.CalculateMilitaryBehaviorForSettlement(
                        settlement, missionType, aiBehavior, p, combinedStrength, partySizeScore, count, totalStrength, newArmyCreatingAdditionalConstant,
                        forceDefenseScore: false
                    );
                }

                // --- Ensure defense score is always added for own settlements under siege/raid ---
                if (missionType == Army.ArmyTypes.Defender
                    && isOwnClan
                    && (settlement.IsUnderSiege || isUnderRaid))
                {
                    bool alreadyScored = p.AIBehaviorScores != null &&
                        p.AIBehaviorScores.Any(tuple =>
                            tuple.Item1 != null &&
                            tuple.Item1.Party != null &&
                            tuple.Item1.Party as Settlement == settlement &&
                            tuple.Item1.AiBehavior == AiBehavior.DefendSettlement);

                    if (!alreadyScored)
                    {
                        this.CalculateMilitaryBehaviorForSettlement(settlement, Army.ArmyTypes.Defender, AiBehavior.DefendSettlement, p, combinedStrength, partySizeScore, count, totalStrength, newArmyCreatingAdditionalConstant, forceDefenseScore: true);
                    }
                }
            }
        }

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

        private void CalculateMilitaryBehaviorForSettlement(Settlement settlement, Army.ArmyTypes missionType, AiBehavior aiBehavior, PartyThinkParams p, float ourStrength, float partySizeScore, int numberOfEnemyFactionSettlements, float totalEnemyMobilePartyStrength, float newArmyCreatingAdditionalConstant = 1f, bool forceDefenseScore = false)
        {
            bool isDefendingOwnUnderAttack =
                missionType == Army.ArmyTypes.Defender
                && settlement.OwnerClan != null
                && p.MobilePartyOf.LeaderHero != null
                && settlement.OwnerClan == p.MobilePartyOf.LeaderHero.Clan
                && (settlement.IsUnderSiege ||
                    (settlement.LastAttackerParty != null &&
                     settlement.LastAttackerParty.MapEvent != null &&
                     settlement.LastAttackerParty.MapEvent.EventType == MapEvent.BattleTypes.Raid));

            bool shouldScore =
                (missionType == Army.ArmyTypes.Defender && settlement.LastAttackerParty != null && settlement.LastAttackerParty.IsActive)
                || (missionType == Army.ArmyTypes.Raider && settlement.IsVillage && settlement.Village.VillageState == Village.VillageStates.Normal)
                || (missionType == Army.ArmyTypes.Besieger && settlement.IsFortification && (settlement.SiegeEvent == null || settlement.SiegeEvent.BesiegerCamp.LeaderParty.MapFaction == p.MobilePartyOf.MapFaction))
                || (missionType == Army.ArmyTypes.Patrolling && !settlement.IsCastle && p.WillGatherAnArmy)
                || (forceDefenseScore && isDefendingOwnUnderAttack);

            if (shouldScore)
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
                num5 *= partySizeScore * num4 * newArmyCreatingAdditionalConstant;

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
                // Apply 100x bonus if defending own settlement under siege/raid
                if (isDefendingOwnUnderAttack)
                {
                    num5 *= 100f;
                }
                AIBehaviorTuple aibehaviorTuple = new AIBehaviorTuple(settlement, aiBehavior, p.WillGatherAnArmy);
                ValueTuple<AIBehaviorTuple, float> valueTuple = new ValueTuple<AIBehaviorTuple, float>(aibehaviorTuple, num5);
                p.AddBehaviorScore(valueTuple);
            }
        }

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
                int num15 = 50 + count * count * 20 + mobileParty.LeaderHero.RandomInt(20) + traitLevel * 20;
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
        }

        private const int MinimumInfluenceNeededToCreateArmy = 50;
        private IDisbandPartyCampaignBehavior _disbandPartyCampaignBehavior;
    }
}