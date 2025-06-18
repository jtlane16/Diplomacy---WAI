using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using Diplomacy;
using WarAndAiTweaks.AI;
using static WarAndAiTweaks.AI.StrategicAI;

namespace WarAndAiTweaks.AI.Goals
{
    public class ExpandGoal : AIGoal
    {
        public Kingdom? Target { get; private set; }
        private readonly IWarEvaluator _warEvaluator;
        private readonly int _daysSinceLastWar;
        private readonly int _daysAtWar;

        // New constants for economic/manpower checks
        private const int MINIMUM_TREASURY_FOR_WAR = 200000;
        private const float MINIMUM_MANPOWER_RATIO = 0.6f; // Kingdom should have at least 60% of its potential strength

        public ExpandGoal(Kingdom kingdom, IWarEvaluator warEvaluator, int daysSinceLastWar, int daysAtWar) : base(kingdom, GoalType.Expand)
        {
            _warEvaluator = warEvaluator;
            _daysSinceLastWar = daysSinceLastWar;
            _daysAtWar = daysAtWar;
        }

        public override void EvaluatePriority()
        {
            // Economic Check
            if (Kingdom.RulingClan.Gold < MINIMUM_TREASURY_FOR_WAR && Kingdom.RulingClan != Hero.MainHero.Clan)
            {
                this.Priority = -100; // Heavily penalize expansion if economy is weak
                return;
            }

            // Manpower Check
            float currentManpowerRatio = GetManpowerRatio(Kingdom);
            if (currentManpowerRatio < MINIMUM_MANPOWER_RATIO)
            {
                this.Priority = -50; // Penalize expansion if armies are depleted
                return;
            }

            const float PeaceDesirePenaltyMax = -100f;
            const float PeaceDesirePenaltyDecayDuration = 30f;

            float bestScore = float.MinValue;
            Kingdom? bestTarget = null;

            float warDesire = Math.Min(_daysSinceLastWar * MAX_WAR_DESIRE / WAR_DESIRE_RAMP_DAYS, MAX_WAR_DESIRE);
            float peaceDesire = Math.Max(_daysAtWar * MAX_WAR_FATIGUE_PENALTY / WAR_FATIGUE_RAMP_DAYS, MAX_WAR_FATIGUE_PENALTY);

            float recentPeacePenalty = 0f;
            if (!FactionManager.GetEnemyKingdoms(this.Kingdom).Any())
            {
                if (CooldownManager.GetDaysSinceLastPeace(this.Kingdom, out var daysSincePeace))
                {
                    if (daysSincePeace < PeaceDesirePenaltyDecayDuration)
                    {
                        float decayFactor = 1.0f - (daysSincePeace / PeaceDesirePenaltyDecayDuration);
                        recentPeacePenalty = PeaceDesirePenaltyMax * decayFactor;
                    }
                }
            }

            var candidates = Kingdom.All.Where(k =>
                k != Kingdom &&
                !Kingdom.IsAtWarWith(k) &&
                !FactionManager.IsAlliedWithFaction(Kingdom, k) &&
                !Diplomacy.DiplomaticAction.DiplomaticAgreementManager.HasNonAggressionPact(Kingdom, k, out _));

            foreach (var k in candidates)
            {
                var warScore = _warEvaluator.GetWarScore(Kingdom, k);
                float finalScore = warScore.ResultNumber + warDesire + peaceDesire + recentPeacePenalty;

                AIComputationLogger.LogWarCandidate(this.Kingdom, k, warScore.ResultNumber, warDesire, peaceDesire, recentPeacePenalty, finalScore, warScore);

                if (finalScore > bestScore)
                {
                    bestScore = finalScore;
                    bestTarget = k;
                }
            }

            this.Priority = bestTarget != null ? bestScore : 0;
            this.Target = bestTarget;
        }

        private float GetManpowerRatio(Kingdom kingdom)
        {
            float currentStrength = kingdom.TotalStrength;
            float potentialStrength = 0;
            foreach (var clan in kingdom.Clans)
            {
                if (!clan.IsUnderMercenaryService)
                {
                    potentialStrength += clan.Tier * 100;
                }
            }
            return potentialStrength > 0 ? currentStrength / potentialStrength : 0;
        }
    }
}