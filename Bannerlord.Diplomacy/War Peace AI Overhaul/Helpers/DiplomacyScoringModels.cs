using Diplomacy.Extensions;

using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Localization;

using TWMathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.AI
{
    public class AllianceScoringModel
    {
        private const float SharedEnemyWeight = 75f;
        private const float StrengthSynergyWeight = 20f;
        private const float RelationsWeight = 5f;
        private const float TotalWeight = SharedEnemyWeight + StrengthSynergyWeight + RelationsWeight;

        public ExplainedNumber GetAllianceScore(Kingdom proposer, Kingdom candidate)
        {
            var en = new ExplainedNumber(0f, true);
            bool alreadyAllied = DiplomaticAgreementManager.Alliances.Any(a =>
                (a.Faction1 == proposer && a.Faction2 == candidate) ||
                (a.Faction1 == candidate && a.Faction2 == proposer));

            if (proposer == candidate || alreadyAllied || proposer.IsAtWarWith(candidate)) return en;

            if (proposer.GetAlliedKingdoms().Any())
            {
                en.Add(-1000f, new TextObject("an existing alliance commitment"));
            }

            var enemyKingdomsOfCandidate = FactionManager.GetEnemyKingdoms(candidate).ToList();
            if (enemyKingdomsOfCandidate.Count > 2)
            {
                float totalEnemyStrength = enemyKingdomsOfCandidate.Sum(k => k.TotalStrength);
                if (candidate.TotalStrength < totalEnemyStrength * 0.5f)
                {
                    en.Add(30f, new TextObject("a desire to defend the underdog"));
                }
            }

            int sharedEnemies = FactionManager.GetEnemyKingdoms(proposer).Intersect(enemyKingdomsOfCandidate).Count();
            en.Add(TWMathF.Clamp(sharedEnemies * 25f, 0f, 100f) * SharedEnemyWeight / TotalWeight, new TextObject("their shared enemies"));

            var enemyKingdoms = FactionManager.GetEnemyKingdoms(proposer).Concat(enemyKingdomsOfCandidate);
            float maxEnemyStrength = enemyKingdoms.Any() ? enemyKingdoms.Max(k => k.TotalStrength) : 1f;
            float synergy = (proposer.TotalStrength + candidate.TotalStrength) / maxEnemyStrength;
            en.Add(TWMathF.Clamp(synergy, 0f, 2f) * 50f * StrengthSynergyWeight / TotalWeight, new TextObject("their combined military strength"));

            float relation = proposer.GetRelation(candidate);
            float relScore = (TWMathF.Clamp(relation, -100f, 100f) + 100f) * 0.5f;
            en.Add(relScore * RelationsWeight / TotalWeight, new TextObject("their positive relations"));

            AIComputationLogger.LogAllianceCandidate(proposer, candidate, en);
            return en;
        }

        public bool ShouldTakeActionBidirectional(Kingdom a, Kingdom b, float thr = 50f) =>
            GetAllianceScore(a, b).ResultNumber >= thr && GetAllianceScore(b, a).ResultNumber >= thr;
    }

    public class NonAggressionPactScoringModel
    {
        private const float ThreatWeight = 50f;
        private const float BorderWeight = 30f;
        private const float RelationsWeight = 20f;
        private const float Total = ThreatWeight + BorderWeight + RelationsWeight;

        public ExplainedNumber GetPactScore(Kingdom p, Kingdom c)
        {
            var en = new ExplainedNumber(0f, true);
            if (p == c || p.IsAtWarWith(c) || DiplomaticAgreementManager.HasNonAggressionPact(p, c, out _)) return en;

            var otherKingdoms = Kingdom.All.Where(k => k != p && !k.IsEliminated && !p.IsAtWarWith(k) && !FactionManager.IsAlliedWithFaction(p, k) && !DiplomaticAgreementManager.HasNonAggressionPact(p, k, out _));
            if (otherKingdoms.Count() == 1 && otherKingdoms.First() == c)
            {
                en.Add(-50f, new TextObject("it being their last potential target for expansion"));
            }

            float proposerStrength = p.TotalStrength;
            float candidateStrength = c.TotalStrength;

            if (proposerStrength > candidateStrength * 1.5f)
            {
                float strengthRatio = proposerStrength / (candidateStrength + 1f);
                float preyPenalty = (strengthRatio - 1.5f) * -40f;
                en.Add(preyPenalty, new TextObject("the power disparity between them"));
            }

            float threatRatio = (candidateStrength + 1f) / (proposerStrength + 1f);
            en.Add(TWMathF.Clamp(threatRatio, 0f, 2f) * 40f * ThreatWeight / Total, new TextObject("a mutual assessment of military threat"));

            int borders = p.Settlements.Count(s => s.IsBorderSettlementWith(c));
            en.Add(TWMathF.Clamp(borders * 10f, 0f, 100f) * BorderWeight / Total, new TextObject("their shared border"));

            float relation = p.GetRelation(c);
            float relScore = (TWMathF.Clamp(relation, -100f, 100f) + 100f) * 0.5f * (RelationsWeight / Total);
            en.Add(relScore, new TextObject("their diplomatic relations"));

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

            if (ally == null) return en;

            bool isAllied = DiplomaticAgreementManager.Alliances.Any(a =>
                (a.Faction1 == breaker && a.Faction2 == ally) ||
                (a.Faction1 == ally && a.Faction2 == breaker));
            if (!isAllied) return en;

            var otherKingdoms = Kingdom.All.Where(k => k != breaker && !k.IsEliminated && !breaker.IsAtWarWith(k) && !FactionManager.IsAlliedWithFaction(breaker, k));
            if (otherKingdoms.Count() == 1 && otherKingdoms.First() == ally)
            {
                en.Add(50f, new TextObject("Sole Target for Expansion"));
            }

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