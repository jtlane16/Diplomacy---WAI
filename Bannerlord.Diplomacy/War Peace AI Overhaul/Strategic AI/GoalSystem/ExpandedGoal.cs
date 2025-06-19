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
            /*
            // Economic and Manpower checks remain
            if (Kingdom.RulingClan.Gold < MINIMUM_TREASURY_FOR_WAR && Kingdom.RulingClan != Hero.MainHero.Clan)
            {
                this.Priority = -100;
                return;
            }
            */
            float currentManpowerRatio = GetManpowerRatio(Kingdom);
            if (currentManpowerRatio < MINIMUM_MANPOWER_RATIO)
            {
                this.Priority = -50;
                return;
            }

            float bestScore = float.MinValue;
            Kingdom? bestTarget = null;

            var candidates = Kingdom.All.Where(k =>
                k != Kingdom &&
                !Kingdom.IsAtWarWith(k) &&
                !FactionManager.IsAlliedWithFaction(Kingdom, k) &&
                !Diplomacy.DiplomaticAction.DiplomaticAgreementManager.HasNonAggressionPact(Kingdom, k, out _));

            foreach (var k in candidates)
            {
                // MODIFIED: We now pass _daysSinceLastWar directly to the evaluator.
                var warScore = _warEvaluator.GetWarScore(Kingdom, k, _daysSinceLastWar);
                // The log now shows the final, combined score.
                AIComputationLogger.LogWarCandidate(this.Kingdom, k, warScore);

                if (warScore.ResultNumber > bestScore)
                {
                    bestScore = warScore.ResultNumber;
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