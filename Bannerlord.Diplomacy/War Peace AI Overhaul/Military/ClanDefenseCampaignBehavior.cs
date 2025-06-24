using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;

using TodayWeFeast;

namespace WarAndAiTweaks.AI.Behaviors
{
    public class ClanDefenseCampaignBehavior : CampaignBehaviorBase
    {
        private FeastAttendingScoringModel _feastAttendingScoringModel = new FeastAttendingScoringModel();

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
                thinkParams.AIBehaviorScores.Add((escortBehavior, 500f));
                return;
            }

            AIBehaviorTuple bestOption = new AIBehaviorTuple(null, AiBehavior.Hold);
            float maxScore = 0f;

            EvaluateFeastActions(mobileParty, ref bestOption, ref maxScore);
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

        // --- HELPER METHOD FOR RECRUITMENT ---
        private int ApproximateNumberOfVolunteersCanBeRecruitedFromSettlement(Hero hero, Settlement settlement)
        {
            int num = 4;
            if (hero.MapFaction != settlement.MapFaction)
            {
                num = 2;
            }
            int num2 = 0;
            if (settlement.Notables != null)
            {
                foreach (Hero hero2 in settlement.Notables)
                {
                    if (hero2.IsAlive)
                    {
                        for (int i = 0; i < num; i++)
                        {
                            if (hero2.VolunteerTypes[i] != null)
                            {
                                num2++;
                            }
                        }
                    }
                }
            }
            return num2;
        }

        // --- FULLY REWRITTEN & CORRECTED METHOD ---
        private void EvaluateRecruitmentActions(MobileParty mobileParty, ref AIBehaviorTuple bestOption, ref float maxScore)
        {
            // If party is already strong, no need to focus on recruiting.
            if (mobileParty.Party.NumberOfAllMembers >= mobileParty.LimitedPartySize * 0.8f)
            {
                return;
            }

            // If a lord's party is critically weak, they should be desperate to get more troops.
            float desperationMultiplier = 1.0f;
            if (mobileParty.Party.NumberOfAllMembers < mobileParty.LimitedPartySize * 0.25f)
            {
                // When weak, the drive to recruit is 5 times stronger.
                desperationMultiplier = 5.0f;
            }

            // A single, safe loop through all settlements.
            foreach (Settlement settlement in Settlement.All)
            {
                // We only care about Towns and Castles for recruitment.
                if (!settlement.IsTown && !settlement.IsCastle)
                {
                    continue;
                }

                // Don't go to hostile settlements to recruit.
                if (settlement.IsInspected && FactionManager.IsAtWarAgainstFaction(settlement.MapFaction, mobileParty.MapFaction))
                {
                    continue;
                }

                float distance = mobileParty.Position2D.Distance(settlement.Position2D);
                if (distance > 250) continue; // Don't travel across the map for troops.

                int totalTroopsAvailable = 0;

                // Option 1: Recruit from the local population (notables). This works for both towns and castles.
                totalTroopsAvailable = ApproximateNumberOfVolunteersCanBeRecruitedFromSettlement(mobileParty.LeaderHero, settlement);

                // Option 2: If this is the clan's own fief, check the garrison.
                if (settlement.IsTown && mobileParty.LeaderHero.Clan.Fiefs.Contains(settlement.Town))
                {
                    // Fix: Pass the correct type 'Settlement' instead of 'Town'.
                    totalTroopsAvailable += Campaign.Current.Models.SettlementGarrisonModel.FindNumberOfTroopsToTakeFromGarrison(mobileParty, settlement);
                }

                if (totalTroopsAvailable > 5)
                {
                    float score = ((totalTroopsAvailable * 2.0f) - (distance * 0.2f)) * desperationMultiplier;

                    if (score > maxScore)
                    {
                        maxScore = score;
                        bestOption = new AIBehaviorTuple(settlement, AiBehavior.GoToSettlement);
                    }
                }
            }
        }

        // --- Your other AI evaluation methods (EvaluateDefensiveActions, etc.) go here ---
        private void EvaluateFeastActions(MobileParty mobileParty, ref AIBehaviorTuple bestOption, ref float maxScore)
        {
            if (FeastBehavior.Instance?.Feasts == null) return;

            foreach (var feast in FeastBehavior.Instance.Feasts)
            {
                if (feast.lordsInFeast.Contains(mobileParty.LeaderHero))
                {
                    var score = _feastAttendingScoringModel.GetFeastAttendingScore(mobileParty.LeaderHero, feast);
                    if (score.ResultNumber > maxScore)
                    {
                        maxScore = score.ResultNumber;
                        bestOption = new AIBehaviorTuple(feast.feastSettlement, AiBehavior.GoToSettlement);
                    }
                }
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
        private void EvaluateSiegeSupportActions(MobileParty mobileParty, ref AIBehaviorTuple bestOption, ref float maxScore)
        {
            if (mobileParty.Party.NumberOfHealthyMembers < mobileParty.Party.NumberOfAllMembers * 0.7f)
            {
                return;
            }

            foreach (var siegeEvent in Campaign.Current.SiegeEventManager.SiegeEvents)
            {
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