using Diplomacy.Extensions;

using System;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

using static WarAndAiTweaks.AI.StrategicAI;

using TWMathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.AI
{
    // CORRECTED: Removed the obsolete interfaces like ": IAllianceEvaluator"
    public class AllianceScoringModel
    {
        private const float SharedEnemyWeight = 75f;
        private const float StrengthSynergyWeight = 20f;
        private const float RelationsWeight = 5f;
        private const float TotalWeight = SharedEnemyWeight + StrengthSynergyWeight + RelationsWeight;

        public ExplainedNumber GetAllianceScore(Kingdom proposer, Kingdom candidate)
        {
            var en = new ExplainedNumber(0f, true);
            if (proposer == candidate || FactionManager.IsAlliedWithFaction(proposer, candidate) || proposer.IsAtWarWith(candidate)) return en;

            int sharedEnemies = FactionManager.GetEnemyKingdoms(proposer).Intersect(FactionManager.GetEnemyKingdoms(candidate)).Count();
            en.Add(TWMathF.Clamp(sharedEnemies * 25f, 0f, 100f) * SharedEnemyWeight / TotalWeight, new TextObject("shared enemies"));

            var enemyKingdoms = FactionManager.GetEnemyKingdoms(proposer).Concat(FactionManager.GetEnemyKingdoms(candidate));
            float maxEnemyStrength = enemyKingdoms.Any() ? enemyKingdoms.Max(k => k.TotalStrength) : 1f;
            float synergy = (proposer.TotalStrength + candidate.TotalStrength) / maxEnemyStrength;
            en.Add(TWMathF.Clamp(synergy, 0f, 2f) * 50f * StrengthSynergyWeight / TotalWeight, new TextObject("strength synergy"));

            float relation = proposer.GetRelation(candidate);
            float relScore = (TWMathF.Clamp(relation, -100f, 100f) + 100f) * 0.5f;
            en.Add(relScore * RelationsWeight / TotalWeight, new TextObject("relations"));

            AIComputationLogger.LogAllianceCandidate(proposer, candidate, en);
            return en;
        }

        public bool ShouldTakeActionBidirectional(Kingdom a, Kingdom b, float thr = 50f) =>
            GetAllianceScore(a, b).ResultNumber >= thr && GetAllianceScore(b, a).ResultNumber >= thr;
    }

    public class NonAggressionPactScoringModel // CORRECTED: Typo "Packed" fixed
    {
        private const float ThreatWeight = 50f;
        private const float BorderWeight = 30f;
        private const float RecoveryWeight = 20f;
        private const float Total = ThreatWeight + BorderWeight + RecoveryWeight;

        public ExplainedNumber GetPactScore(Kingdom p, Kingdom c)
        {
            var en = new ExplainedNumber(0f, true);
            if (p == c || p.IsAtWarWith(c) || Diplomacy.DiplomaticAction.DiplomaticAgreementManager.HasNonAggressionPact(p, c, out _)) return en;

            float threatRatio = (c.TotalStrength + 1f) / (p.TotalStrength + 1f);
            en.Add(TWMathF.Clamp(threatRatio, 0f, 2f) * 50f * ThreatWeight / Total, new TextObject("threat"));

            int borders = p.Settlements.Count(s => s.IsBorderSettlementWith(c));
            en.Add(TWMathF.Clamp(borders * 10f, 0f, 100f) * BorderWeight / Total, new TextObject("borders"));

            float recovery = p.GetCasualties() / (p.TotalStrength + 1f);
            en.Add(recovery * 100f * RecoveryWeight / Total, new TextObject("recovery"));

            AIComputationLogger.LogPactCandidate(p, c, en);
            return en;
        }

        public bool ShouldTakeActionBidirectional(Kingdom a, Kingdom b, float thr = 50f) =>
            GetPactScore(a, b).ResultNumber >= thr && GetPactScore(b, a).ResultNumber >= thr;
    }

    public class BreakAllianceScoringModel
    {
        public ExplainedNumber GetBreakAllianceScore(Kingdom breaker, Kingdom ally)
        {
            var en = new ExplainedNumber(0f, true);
            if (!FactionManager.IsAlliedWithFaction(breaker, ally)) return en;

            en.Add(TWMathF.Clamp(-breaker.GetRelation(ally), 0f, 100f) * 0.25f, new TextObject("poor relations"));

            int sharedEnemies = FactionManager.GetEnemyKingdoms(breaker).Intersect(FactionManager.GetEnemyKingdoms(ally)).Count();
            if (sharedEnemies == 0) en.Add(40f, new TextObject("no shared enemies"));

            int borders = breaker.Settlements.Count(s => s.IsBorderSettlementWith(ally));
            en.Add(borders * 5f, new TextObject("border competition"));

            float ratio = breaker.TotalStrength / (ally.TotalStrength + 1f);
            if (ratio > 1.2f) en.Add(20f, new TextObject("military advantage"));

            var warEvaluator = new DefaultWarEvaluator();
            var warScoreVsAlly = warEvaluator.GetWarScore(breaker, ally);
            if (warScoreVsAlly.ResultNumber > 50f)
            {
                en.Add(warScoreVsAlly.ResultNumber * 0.75f, new TextObject("Ally is an opportune target"));
            }
            return en;
        }

        public bool ShouldBreakAlliance(Kingdom breaker, Kingdom ally, float threshold = 60f) =>
            GetBreakAllianceScore(breaker, ally).ResultNumber >= threshold;
    }
}