using Diplomacy.Extensions;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
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

            // NEW LOGIC: Check for and penalize existing alliances
            if (FactionManager.GetEnemyKingdoms(proposer).Any(k => FactionManager.IsAlliedWithFaction(k, proposer)))
            {
                en.Add(-1000f, new TextObject("{=K78W292D}Already in an Alliance"));
            }

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
            if (p == c || p.IsAtWarWith(c) ||DiplomaticAgreementManager.HasNonAggressionPact(p, c, out _)) return en;

            float proposerStrength = p.TotalStrength;
            float candidateStrength = c.TotalStrength;

            // ADD THIS BLOCK
            // Prey Penalty: A strong kingdom shouldn't want a pact with a much weaker neighbor.
            if (proposerStrength > candidateStrength * 1.5f)
            {
                float strengthRatio = proposerStrength / (candidateStrength + 1f);
                float preyPenalty = (strengthRatio - 1.5f) * -20f; // Penalty increases the bigger the strength gap
                en.Add(preyPenalty, new TextObject("{=qB6b711e}Target is Weak Prey"));
            }
            // END OF BLOCK

            float threatRatio = (candidateStrength + 1f) / (proposerStrength + 1f);
            en.Add(TWMathF.Clamp(threatRatio, 0f, 2f) * 50f * ThreatWeight / Total, new TextObject("threat"));

            int borders = p.Settlements.Count(s => s.IsBorderSettlementWith(c));
            en.Add(TWMathF.Clamp(borders * 10f, 0f, 100f) * BorderWeight / Total, new TextObject("borders"));

            float recovery = p.GetCasualties() / (proposerStrength + 1f);
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

            en.Add(30f, new TextObject("{=betrayal_base}Desire for new territory"));

            float strengthRatio = breaker.TotalStrength / (ally.TotalStrength + 1f);
            if (strengthRatio > 1.2f)
            {
                en.Add((strengthRatio - 1.2f) * 50f, new TextObject("Military Advantage"));
            }

            float relations = breaker.GetRelation(ally);
            if (relations < 0)
            {
                en.Add(relations * -1.5f, new TextObject("Poor Relations"));
            }

            int sharedEnemies = FactionManager.GetEnemyKingdoms(breaker).Intersect(FactionManager.GetEnemyKingdoms(ally)).Count();
            if (sharedEnemies == 0)
            {
                en.Add(25f, new TextObject("No Shared Enemies"));
            }

            int honor = breaker.Leader.GetTraitLevel(DefaultTraits.Honor);
            if (honor > 0)
            {
                // MODIFIED: Penalty is now a strong deterrent, not an absolute block.
                en.Add(honor * -200f, DefaultTraits.Honor.Name);
            }

            int calculating = breaker.Leader.GetTraitLevel(DefaultTraits.Calculating);
            if (calculating > 0)
            {
                en.Add(calculating * 20f, DefaultTraits.Calculating.Name);
            }

            return en;
        }

        public bool ShouldBreakAlliance(Kingdom breaker, Kingdom ally, float threshold = 80f)
        {
            return GetBreakAllianceScore(breaker, ally).ResultNumber >= threshold;
        }
    }
}