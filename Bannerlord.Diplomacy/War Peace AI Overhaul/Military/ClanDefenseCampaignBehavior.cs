using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace WarAndAiTweaks.AI.Behaviors
{
    public class ClanDefenseCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, OnAiHourlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // This behavior doesn't need to save any data.
        }

        private void OnAiHourlyTick(MobileParty mobileParty, PartyThinkParams thinkParams)
        {
            if (mobileParty?.LeaderHero?.Clan == null || mobileParty.IsMainParty || !mobileParty.IsLordParty || mobileParty.Ai.IsDisabled)
            {
                return;
            }

            if (mobileParty.Army != null && mobileParty.Army.LeaderParty != mobileParty)
            {
                var escortBehavior = new AIBehaviorTuple(mobileParty.Army.LeaderParty, AiBehavior.EscortParty);
                thinkParams.AIBehaviorScores.Add((escortBehavior, 500f)); // Extremely high score to ensure army cohesion
                return;
            }

            AIBehaviorTuple bestOption = new AIBehaviorTuple(null, AiBehavior.Hold);
            float maxScore = 0f;

            EvaluateDefensiveActions(mobileParty, ref bestOption, ref maxScore);
            EvaluateOpportunisticActions(mobileParty, ref bestOption, ref maxScore);
            EvaluateRecruitmentActions(mobileParty, ref bestOption, ref maxScore);
            EvaluateSiegeSupportActions(mobileParty, ref bestOption, ref maxScore);
            DeprioritizeArmyRaiding(thinkParams);

            if (bestOption.Party != null && maxScore > 0)
            {
                thinkParams.AIBehaviorScores.Add((bestOption, maxScore));
            }
        }

        private void EvaluateDefensiveActions(MobileParty mobileParty, ref AIBehaviorTuple bestOption, ref float maxScore)
        {
            Clan partyClan = mobileParty.LeaderHero.Clan;
            Kingdom partyKingdom = mobileParty.MapFaction as Kingdom;
            if (partyKingdom == null) return;

            foreach (Settlement settlement in partyKingdom.Settlements)
            {
                if (settlement.IsUnderSiege || settlement.IsUnderRaid)
                {
                    float currentScore = 50f;

                    if (settlement.OwnerClan == partyClan)
                    {
                        currentScore += 150f;
                    }
                    else if (settlement.OwnerClan != null)
                    {
                        currentScore += partyClan.GetRelationWithClan(settlement.OwnerClan);
                    }

                    if (settlement.IsTown)
                    {
                        currentScore += 20;
                    }
                    else if (settlement.IsCastle)
                    {
                        currentScore += 10;
                    }

                    currentScore -= mobileParty.Position2D.Distance(settlement.Position2D) * 0.1f;

                    if (currentScore > maxScore)
                    {
                        maxScore = currentScore;
                        bestOption = new AIBehaviorTuple(settlement, AiBehavior.DefendSettlement);
                    }
                }
            }
        }

        private void EvaluateOpportunisticActions(MobileParty mobileParty, ref AIBehaviorTuple bestOption, ref float maxScore)
        {
            if (mobileParty.Party.NumberOfHealthyMembers < mobileParty.Party.NumberOfAllMembers * 0.5f)
            {
                return;
            }

            foreach (var mapEvent in Campaign.Current.MapEventManager.MapEvents.Where(e => e.IsFieldBattle))
            {
                BattleSideEnum sideToJoin = BattleSideEnum.None;
                if (mapEvent.CanPartyJoinBattle(mobileParty.Party, BattleSideEnum.Attacker))
                {
                    sideToJoin = BattleSideEnum.Attacker;
                }
                else if (mapEvent.CanPartyJoinBattle(mobileParty.Party, BattleSideEnum.Defender))
                {
                    sideToJoin = BattleSideEnum.Defender;
                }

                if (sideToJoin == BattleSideEnum.None)
                {
                    continue;
                }

                float distance = mobileParty.Position2D.Distance(mapEvent.Position);
                if (distance > 75) continue;

                PartyBase enemySideLeader = mapEvent.GetLeaderParty(sideToJoin.GetOppositeSide());

                if (enemySideLeader == null || !mobileParty.MapFaction.IsAtWarWith(enemySideLeader.MapFaction))
                {
                    continue;
                }

                float friendStr = mapEvent.StrengthOfSide[(int) sideToJoin];
                float enemyStr = mapEvent.StrengthOfSide[(int) sideToJoin.GetOppositeSide()];

                if (friendStr < enemyStr * 1.8f)
                {
                    float score = (enemyStr / (friendStr + mobileParty.Party.TotalStrength + 1f)) * 120f;
                    score -= distance * 0.3f;

                    if (score > maxScore)
                    {
                        maxScore = score;
                        bestOption = new AIBehaviorTuple(enemySideLeader.MobileParty, AiBehavior.EngageParty);
                    }
                }
            }
        }

        private void EvaluateRecruitmentActions(MobileParty mobileParty, ref AIBehaviorTuple bestOption, ref float maxScore)
        {
            if (mobileParty.Party.NumberOfAllMembers >= mobileParty.LimitedPartySize * 0.8f)
            {
                return;
            }

            foreach (Settlement settlement in mobileParty.LeaderHero.Clan.Settlements)
            {
                if (settlement.IsTown || settlement.IsCastle)
                {
                    int troopsToTake = Campaign.Current.Models.SettlementGarrisonModel.FindNumberOfTroopsToTakeFromGarrison(mobileParty, settlement);

                    if (troopsToTake > 5)
                    {
                        float distance = mobileParty.Position2D.Distance(settlement.Position2D);
                        if (distance > 150) continue;

                        float score = (troopsToTake * 1.5f) - (distance * 0.2f);

                        if (score > maxScore)
                        {
                            maxScore = score;
                            bestOption = new AIBehaviorTuple(settlement, AiBehavior.GoToSettlement);
                        }
                    }
                }
            }
        }

        private void EvaluateSiegeSupportActions(MobileParty mobileParty, ref AIBehaviorTuple bestOption, ref float maxScore)
        {
            if (mobileParty.Party.NumberOfHealthyMembers < mobileParty.Party.NumberOfAllMembers * 0.7f)
            {
                return;
            }

            foreach (var siegeEvent in Campaign.Current.SiegeEventManager.SiegeEvents)
            {
                // ADDED: Prevent the besieging army from trying to support its own siege.
                if (siegeEvent.BesiegerCamp.LeaderParty == mobileParty)
                {
                    continue;
                }

                if (siegeEvent.BesiegerCamp.LeaderParty.MapFaction == mobileParty.MapFaction)
                {
                    Settlement besiegedSettlement = siegeEvent.BesiegedSettlement;

                    float distance = mobileParty.Position2D.Distance(besiegedSettlement.Position2D);
                    if (distance > 200)
                    {
                        continue;
                    }

                    float score = 80f;

                    if (besiegedSettlement.IsTown)
                    {
                        score += 40f;
                    }
                    else if (besiegedSettlement.IsCastle)
                    {
                        score += 20f;
                    }

                    score -= distance * 0.15f;

                    if (score > maxScore)
                    {
                        maxScore = score;
                        bestOption = new AIBehaviorTuple(besiegedSettlement, AiBehavior.PatrolAroundPoint);
                    }
                }
            }
        }

        private void DeprioritizeArmyRaiding(PartyThinkParams thinkParams)
        {
            var scores = thinkParams.AIBehaviorScores;
            for (int i = 0; i < scores.Count; i++)
            {
                if (scores[i].Item1.AiBehavior == AiBehavior.RaidSettlement && scores[i].Item1.WillGatherArmy)
                {
                    var originalTuple = scores[i];
                    scores[i] = (originalTuple.Item1, originalTuple.Item2 * 0.05f);
                }
            }
        }
    }
}