// In: Bannerlord.Diplomacy/War Peace AI Overhaul/Strategic AI/GoalSystem/ExpandedGoal.cs

using System;
using System.Collections.Generic;
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
        private readonly Dictionary<string, CampaignTime> _lastPeaceTimes;

        private const int MINIMUM_TREASURY_FOR_WAR = 200000;
        private const float MINIMUM_MANPOWER_RATIO = 0.6f;

        public ExpandGoal(Kingdom kingdom, IWarEvaluator warEvaluator, int daysSinceLastWar, int daysAtWar, Dictionary<string, CampaignTime> lastPeaceTimes) : base(kingdom, GoalType.Expand)
        {
            _warEvaluator = warEvaluator;
            _daysSinceLastWar = daysSinceLastWar;
            _daysAtWar = daysAtWar;
            _lastPeaceTimes = lastPeaceTimes;
        }

        public override void EvaluatePriority()
        {
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
                !DiplomaticAgreementManager.HasNonAggressionPact(Kingdom, k, out _));

            foreach (var k in candidates)
            {
                var warScore = _warEvaluator.GetWarScore(Kingdom, k, _daysSinceLastWar, _lastPeaceTimes);
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