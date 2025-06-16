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

#if !DIPOLOMACY_EXTENSIONS
internal static class DiplomacyFallbackExtensions
{
    public static bool IsAlliedWithFaction(this Kingdom _, Kingdom __) => false;
    public static bool HasNonAggressionPact(this Kingdom _, Kingdom __) => false;
    public static float GetRelation(this Kingdom _, Kingdom __) => 0f;
    public static float GetCasualties(this Kingdom _) => 0f;

    // Naive border detection: settlements within threshold distance
    public static bool IsBorderSettlementWith(this Settlement s, Kingdom other)
    {
        // A list of all towns and castles in the game, filtering out villages.
        var allTownsAndCastles = Campaign.Current.Settlements
            .Where(settlement => settlement.IsTown || settlement.IsCastle)
            .ToList();

        var homeKingdom = s.OwnerClan.Kingdom;

        // Find the 3 closest towns or castles to the settlement 's', excluding any from the same kingdom.
        var closestForeignSettlements = allTownsAndCastles
            .Where(settlement => settlement.OwnerClan.Kingdom != homeKingdom)
            .OrderBy(settlement => s.Position2D.Distance(settlement.Position2D))
            .Take(3);

        // Check if any of these 3 closest foreign settlements belong to the 'other' kingdom.
        return closestForeignSettlements.Any(closestSettlement => closestSettlement.OwnerClan.Kingdom == other);
    }
}
#endif

namespace WarAndAiTweaks.AI
{

    internal static class BorderHelper
    {
        public static bool BordersWith(Settlement s, Kingdom other)
        {
            return s.IsBorderSettlementWith(other);
        }
    }

    // ---------------- Alliance scoring ----------------
    public class AllianceScoringModel : IAllianceEvaluator
    {
        // MODIFICATION: Reduced RelationsWeight and boosted strategic weights.
        private const float SharedEnemyWeight = 60f;
        private const float StrengthSynergyWeight = 35f;
        private const float RelationsWeight = 5f;
        private const float TotalWeight = SharedEnemyWeight + StrengthSynergyWeight + RelationsWeight;

        public ExplainedNumber GetAllianceScore(Kingdom proposer, Kingdom candidate)
        {
            var en = new ExplainedNumber(0f, true);

            if (proposer == candidate) { en.Add(0, new TextObject("same kingdom")); return en; }
            if (FactionManager.IsAlliedWithFaction(proposer, candidate)) { en.Add(0, new TextObject("already allied")); return en; }
            if (proposer.IsAtWarWith(candidate)) { en.Add(0, new TextObject("at war")); return en; }

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

            AIComputationLogger.LogAllianceCandidate(proposer, candidate, en);

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

            if (p == c) { en.Add(0, new TextObject("same kingdom")); return en; }
            if (p.IsAtWarWith(c)) { en.Add(0, new TextObject("at war")); return en; }
            if (p.HasNonAggressionPact(c)) { en.Add(0, new TextObject("already pacted")); return en; }

            float threatRatio = (c.TotalStrength + 1f) / (p.TotalStrength + 1f);
            en.Add(TWMathF.Clamp(threatRatio, 0f, 2f) * 50f * ThreatWeight / Total,
                   new TextObject("threat"));

            int borders = p.Settlements.Count(s => BorderHelper.BordersWith(s, c));
            en.Add(TWMathF.Clamp(borders * 10f, 0f, 100f) * BorderWeight / Total,
                   new TextObject("borders"));

            float recovery = p.GetCasualties() / (p.TotalStrength + 1f);
            en.Add(recovery * 100f * RecoveryWeight / Total, new TextObject("recovery"));

            AIComputationLogger.LogPactCandidate(p, c, en);

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

            // Poor relations have a minor effect
            en.Add(TWMathF.Clamp(-breaker.GetRelation(ally), 0f, 100f) * 0.25f, // Reduced from 0.5f
                   new TextObject("poor relations"));

            // No longer having a common enemy is a major reason to break an alliance
            int sharedEnemies = FactionManager.GetEnemyKingdoms(breaker)
                                              .Intersect(FactionManager.GetEnemyKingdoms(ally)).Count();
            if (sharedEnemies == 0)
                en.Add(40f, new TextObject("no shared enemies")); // Increased from 30f

            int borders = breaker.Settlements.Count(s => BorderHelper.BordersWith(s, ally));
            en.Add(borders * 5f, new TextObject("border competition"));

            float ratio = breaker.TotalStrength / (ally.TotalStrength + 1f);
            if (ratio > 1.2f) en.Add(20f, new TextObject("military advantage"));

            // MODIFICATION: Add a major incentive to break alliance if the ally is a good war target.
            var warEvaluator = new DefaultWarEvaluator();
            var warScoreVsAlly = warEvaluator.GetWarScore(breaker, ally);

            // If the war score is high, it provides a strong reason to betray the ally for conquest.
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