using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace WarAndAiTweaks
{
    public static class DiplomacyHelpers
    {
        #region Constants
        private const float BASE_THRESH = 35f;
        private const float THRESH_PEACE_BONUS = -5f;
        private const float THRESH_WAR_PENALTY = +5f;
        private const float THRESH_PER_EXISTING_WAR = 10f;
        #endregion

        #region General Helpers
        public static IEnumerable<Kingdom> MajorKingdoms()
        {
            return Kingdom.All.Where(k => !k.IsMinorFaction && !k.IsEliminated);
        }

        public static IEnumerable<Kingdom> MajorEnemies(Kingdom k)
        {
            return FactionManager.GetEnemyKingdoms(k).Where(o => o != k && !o.IsMinorFaction && !o.IsEliminated);
        }

        public static bool AreNeighbors(Kingdom k1, Kingdom k2)
        {
            var diplomacyBehavior = DiplomacyBehavior.Instance;
            if (diplomacyBehavior == null) return false;

            // FIX: The GetNeighborsOf method now returns a List<Kingdom>, so Contains() works correctly.
            return diplomacyBehavior.GetNeighborsOf(k1).Contains(k2);
        }

        public static float CalculateEconomicBoost(Kingdom k, bool atWar)
        {
            var towns = k.Settlements.Where(s => s.IsTown).ToList();
            if (towns.Count == 0) return 0f;
            float avgProsperity = towns.Average(s => s.Town.Prosperity);
            float boost = MBMath.ClampFloat((avgProsperity - 4000f) / 8000f, -0.2f, 0.5f);
            return atWar ? boost * 0.5f : boost;
        }
        #endregion

        #region War & Peace Scoring
        public static float ComputeDynamicWarThreshold(Kingdom k)
        {
            int wars = MajorEnemies(k).Count();
            bool atWar = wars > 0;
            float baseThresh = BASE_THRESH
                              + (atWar ? THRESH_WAR_PENALTY : THRESH_PEACE_BONUS)
                              + THRESH_PER_EXISTING_WAR * wars;
            return baseThresh;
        }

        public static WarScoreBreakdown ComputeWarDesireScore(Kingdom us, Kingdom them)
        {
            var breakdown = new WarScoreBreakdown(them);
            breakdown.ThreatScore = CalculateDangerScore(us, them);

            float usStr = us.TotalStrength;
            float themStr = them.TotalStrength;
            breakdown.PowerBalanceScore = (usStr - themStr) / (usStr + themStr + 1f);
            breakdown.MultiWarPenalty = MajorEnemies(us).Count() * 5f;

            float distKm = Campaign.Current.Models.MapDistanceModel.GetDistance(us.FactionMidSettlement, them.FactionMidSettlement);
            breakdown.DistancePenalty = distKm * 0.1f;

            var targetEnemies = MajorEnemies(them).ToList();
            var allKingdoms = MajorKingdoms().ToList();
            float avgFiefs = allKingdoms.Any() ? (float)allKingdoms.Average(k => k.Settlements.Count) : 0f;
            bool isTargetSnowballing = (them.TotalStrength > us.TotalStrength * 1.5f) && (them.Settlements.Count > avgFiefs * 1.5f);

            if (isTargetSnowballing && targetEnemies.Any())
            {
                breakdown.DogpileBonus += 35f;
                breakdown.DogpileBonus += targetEnemies.Count * 10f;
            }

            const float W_THREAT = 1.0f, W_BALANCE = 2.0f, W_MULTI = 1.0f, W_DISTANCE = 1.0f;
            breakdown.FinalScore = W_THREAT * breakdown.ThreatScore + W_BALANCE * breakdown.PowerBalanceScore - W_MULTI * breakdown.MultiWarPenalty - W_DISTANCE * breakdown.DistancePenalty + breakdown.DogpileBonus;
            return breakdown;
        }

        public static PeaceScoreBreakdown ComputePeaceScore(Kingdom us, Kingdom them, float usExhaustion)
        {
            var breakdown = new PeaceScoreBreakdown(them)
            {
                DangerScore = CalculateDangerScore(us, them),
                ExhaustionScore = usExhaustion
            };

            var tempPeaceDecision = new MakePeaceKingdomDecision(us.RulingClan, them);
            breakdown.TributeAmount = tempPeaceDecision.DailyTributeToBePaid;
            breakdown.TributeFactor = breakdown.TributeAmount * -0.01f;
            breakdown.FinalScore = breakdown.DangerScore + breakdown.ExhaustionScore + breakdown.TributeFactor;
            return breakdown;
        }

        public static float CalculateDangerScore(Kingdom us, Kingdom them)
        {
            // [REMOVED] GetEffectiveStrength which relied on alliances
            float ourStr = us.TotalStrength;
            float theirStr = them.TotalStrength;
            return (theirStr / MathF.Max(1f, ourStr)) * 50f + them.Settlements.Count * 2f;
        }
        #endregion

        #region Reasoning Generators
        public static string GenerateWarReasoning(Kingdom proposer, WarScoreBreakdown breakdown)
        {
            var reasons = new List<string>();
            if (breakdown.DogpileBonus > 30f) reasons.Add("they see an opportunity to cull the power of a growing empire while it is distracted by other wars.");
            else if (breakdown.ThreatScore > 50f && breakdown.PowerBalanceScore < -0.2f) reasons.Add($"they view the superior strength of {breakdown.Target.Name} as an existential threat that must be confronted.");
            else if (breakdown.PowerBalanceScore > 0.3f) reasons.Add($"they believe {breakdown.Target.Name} is weak and ripe for conquest.");
            else reasons.Add("they consider it a moment of strategic opportunity.");
            return $"{proposer.Name} has declared war on {breakdown.Target.Name}; our strategists believe {string.Join(" ", reasons)}";
        }

        public static string GeneratePeaceReasoning(Kingdom proposer, PeaceScoreBreakdown breakdown)
        {
            var reasons = new List<string>();
            if (breakdown.TributeAmount < -100) reasons.Add("the terms of the proposed tribute payments are highly favorable.");
            else if (breakdown.ExhaustionScore > 50f) reasons.Add("their kingdom is weary and exhausted from the long and bloody conflict.");
            else if (breakdown.DangerScore > breakdown.ExhaustionScore) reasons.Add($"they believe continuing the war against the might of {breakdown.Target.Name} is no longer sustainable.");
            else if (breakdown.TributeAmount > 100) reasons.Add("though costly, they believe paying tribute is necessary to end the war.");
            else reasons.Add("they have determined that the time is right to seek a cessation of hostilities.");
            return $"{proposer.Name} has proposed peace with {breakdown.Target.Name}. Their envoys have indicated that {string.Join(" ", reasons)}";
        }
        #endregion

        // [REMOVED] Pact & Alliance Evaluation region
    }
}