
using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TWMathF = TaleWorlds.Library.MathF;
using Diplomacy.Extensions;
using TaleWorlds.CampaignSystem.Settlements;
using static WarAndAiTweaks.AI.StrategicAI;


#if !DIPOLOMACY_EXTENSIONS
internal static class DiplomacyFallbackExtensions
    {
        public static bool IsAlliedWithFaction(this Kingdom _, Kingdom __)               => false;
        public static bool HasNonAggressionPact(this Kingdom _, Kingdom __)             => false;
        public static float GetRelation(this Kingdom _, Kingdom __)                     => 0f;
        public static float GetCasualties(this Kingdom _)                               => 0f;

        // Naive border detection: settlements within threshold distance
        public static bool IsBorderSettlementWith(this Settlement s, Kingdom other)
        {
            const float Threshold = 15000f;
            return other.Settlements.Any(o => s.Position2D.Distance(o.Position2D) <= Threshold);
        }
    }
#endif

namespace WarAndAiTweaks.AI
{
    
    internal static class BorderHelper
    {
        public static bool BordersWith(Settlement s, Kingdom other)
        {
#if BANNERLORD_11
            return s.IsBorderSettlementWith(other);
#else
            return false; // placeholder when API not available
#endif
        }
    }

// ---------------- Alliance scoring ----------------
    public class AllianceScoringModel : IAllianceEvaluator
    {
        private const float SharedEnemyWeight     = 50f;
        private const float StrengthSynergyWeight = 30f;
        private const float RelationsWeight       = 20f;
        private const float TotalWeight = SharedEnemyWeight + StrengthSynergyWeight + RelationsWeight;

        public ExplainedNumber GetAllianceScore(Kingdom proposer, Kingdom candidate)
        {
            var en = new ExplainedNumber(0f, true);

            if (proposer == candidate)            { en.Add(0, new TextObject("same kingdom")); return en; }
            if (FactionManager.IsAlliedWithFaction(proposer, candidate)) { en.Add(0, new TextObject("already allied")); return en; }
            if (proposer.IsAtWarWith(candidate))  { en.Add(0, new TextObject("at war")); return en; }

            int sharedEnemies = FactionManager.GetEnemyKingdoms(proposer)
                                              .Intersect(FactionManager.GetEnemyKingdoms(candidate)).Count();
            en.Add(TWMathF.Clamp(sharedEnemies * 25f, 0f, 100f) * SharedEnemyWeight / TotalWeight,
                   new TextObject("shared enemies"));

            float synergy = (proposer.TotalStrength + candidate.TotalStrength) /
                            (FactionManager.GetEnemyKingdoms(proposer).Concat(FactionManager.GetEnemyKingdoms(candidate))
                                            .Select(k => k.TotalStrength).DefaultIfEmpty(1f).Max());
            en.Add(TWMathF.Clamp(synergy, 0f, 2f) * 50f * StrengthSynergyWeight / TotalWeight,
                   new TextObject("strength synergy"));

            float relation = proposer.GetRelation(candidate);
            float relScore = (TWMathF.Clamp(relation, -100f, 100f) + 100f) * 0.5f;
            en.Add(relScore * RelationsWeight / TotalWeight, new TextObject("relations"));

            return en;
        }

        public bool ShouldTakeActionBidirectional(Kingdom a, Kingdom b, float thr = 50f) =>
            GetAllianceScore(a, b).ResultNumber >= thr && GetAllianceScore(b, a).ResultNumber >= thr;
    }

    // ---------------- Non‑Aggression Pact scoring ----------------
    public class NonAggressionPackedScoringModel : INonAggressionPactEvaluator
    {
        private const float ThreatWeight = 50f;
        private const float BorderWeight = 30f;
        private const float RecoveryWeight = 20f;
        private const float Total = ThreatWeight + BorderWeight + RecoveryWeight;

        public ExplainedNumber GetPactScore(Kingdom p, Kingdom c)
        {
            var en = new ExplainedNumber(0f, true);

            if (p == c)                     { en.Add(0, new TextObject("same kingdom")); return en; }
            if (p.IsAtWarWith(c))           { en.Add(0, new TextObject("at war")); return en; }
            if (p.HasNonAggressionPact(c))  { en.Add(0, new TextObject("already pacted")); return en; }

            float threatRatio = (c.TotalStrength + 1f) / (p.TotalStrength + 1f);
            en.Add(TWMathF.Clamp(threatRatio, 0f, 2f) * 50f * ThreatWeight / Total,
                   new TextObject("threat"));

            int borders = p.Settlements.Count(s => BorderHelper.BordersWith(s, c));
            en.Add(TWMathF.Clamp(borders * 10f, 0f, 100f) * BorderWeight / Total,
                   new TextObject("borders"));

            float recovery = p.GetCasualties() / (p.TotalStrength + 1f);
            en.Add(recovery * 100f * RecoveryWeight / Total, new TextObject("recovery"));

            return en;
        }

        public bool ShouldTakeActionBidirectional(Kingdom a, Kingdom b, float thr = 50f) =>
            GetPactScore(a, b).ResultNumber >= thr && GetPactScore(b, a).ResultNumber >= thr;
    }

    // ---------------- Break Alliance scoring ----------------
    public class BreakAllianceScoringModel : IAllianceBreakEvaluator
    {
        public ExplainedNumber GetBreakAllianceScore(Kingdom breaker, Kingdom ally)
        {
            var en = new ExplainedNumber(0f, true);

            if (!FactionManager.IsAlliedWithFaction(breaker, ally)) { en.Add(0, new TextObject("not allied")); return en; }

            en.Add(TWMathF.Clamp(-breaker.GetRelation(ally), 0f, 100f) * 0.5f,
                   new TextObject("poor relations"));

            int sharedEnemies = FactionManager.GetEnemyKingdoms(breaker)
                                              .Intersect(FactionManager.GetEnemyKingdoms(ally)).Count();
            if (sharedEnemies == 0)
                en.Add(30f, new TextObject("no shared enemies"));

            int borders = breaker.Settlements.Count(s => BorderHelper.BordersWith(s, ally));
            en.Add(borders * 5f, new TextObject("border competition"));

            float ratio = breaker.TotalStrength / (ally.TotalStrength + 1f);
            if (ratio > 1.2f) en.Add(20f, new TextObject("military advantage"));

            return en;
        }

        public bool ShouldBreakAlliance(Kingdom breaker, Kingdom ally, float threshold = 60f) =>
            GetBreakAllianceScore(breaker, ally).ResultNumber >= threshold;
    }
}
