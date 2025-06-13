using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;
using Diplomacy.CampaignBehaviors;
using Diplomacy.Extensions;
using TaleWorlds.Localization;

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
            var diplomacyBehavior = Campaign.Current.GetCampaignBehavior<DiplomacyBehavior>();
            if (diplomacyBehavior == null) return false;

            return diplomacyBehavior.GetNeighborsOf(k1).Contains(k2);
        }

        /// <summary>
        /// Calculates the effective military strength of a kingdom, including its allies.
        /// </summary>
        public static float GetEffectiveStrength(Kingdom kingdom)
        {
            float totalStrength = kingdom.TotalStrength;
            // Add a portion of allied strength, as they may not fully commit.
            foreach (var ally in kingdom.GetAlliedKingdoms())
            {
                totalStrength += ally.TotalStrength * 0.75f;
            }
            return totalStrength;
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

            // LOGIC CHANGE: Use effective strength (including allies) for calculations
            float usStr = GetEffectiveStrength(us);
            float themStr = GetEffectiveStrength(them);

            breakdown.ThreatScore = (themStr / MathF.Max(1f, usStr)) * 50f + them.Settlements.Count * 2f;
            breakdown.PowerBalanceScore = (usStr - themStr) / (usStr + themStr + 1f);
            breakdown.MultiWarPenalty = MajorEnemies(us).Count() * 5f;

            float distKm = Campaign.Current.Models.MapDistanceModel.GetDistance(us.FactionMidSettlement, them.FactionMidSettlement);
            breakdown.DistancePenalty = distKm * 0.1f;

            var targetEnemies = MajorEnemies(them).ToList();
            var allKingdoms = MajorKingdoms().ToList();
            float avgFiefs = allKingdoms.Any() ? (float) allKingdoms.Average(k => k.Settlements.Count) : 0f;
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
                // LOGIC CHANGE: Use effective strength for danger calculation
                DangerScore = (GetEffectiveStrength(them) / MathF.Max(1f, GetEffectiveStrength(us))) * 50f + them.Settlements.Count * 2f,
                ExhaustionScore = usExhaustion
            };

            var tempPeaceDecision = new MakePeaceKingdomDecision(us.RulingClan, them);
            breakdown.TributeAmount = tempPeaceDecision.DailyTributeToBePaid;
            breakdown.TributeFactor = breakdown.TributeAmount * -0.01f;
            breakdown.FinalScore = breakdown.DangerScore + breakdown.ExhaustionScore + breakdown.TributeFactor;
            return breakdown;
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

            var textObject = new TextObject("{=WAR_REASONING}{PROPOSER} has declared war on {TARGET}; our strategists believe {REASONS}");
            textObject.SetTextVariable("PROPOSER", proposer.Name);
            textObject.SetTextVariable("TARGET", breakdown.Target.Name);
            textObject.SetTextVariable("REASONS", string.Join(" ", reasons));
            return textObject.ToString();
        }

        public static string GeneratePeaceReasoning(Kingdom proposer, PeaceScoreBreakdown breakdown)
        {
            var reasons = new List<string>();
            if (breakdown.TributeAmount < -100) reasons.Add("the terms of the proposed tribute payments are highly favorable.");
            else if (breakdown.ExhaustionScore > 50f) reasons.Add("their kingdom is weary and exhausted from the long and bloody conflict.");
            else if (breakdown.DangerScore > breakdown.ExhaustionScore) reasons.Add($"they believe continuing the war against the might of {breakdown.Target.Name} is no longer sustainable.");
            else if (breakdown.TributeAmount > 100) reasons.Add("though costly, they believe paying tribute is necessary to end the war.");
            else reasons.Add("they have determined that the time is right to seek a cessation of hostilities.");

            var textObject = new TextObject("{=PEACE_REASONING}{PROPOSER} has proposed peace with {TARGET}. Their envoys have indicated that {REASONS}");
            textObject.SetTextVariable("PROPOSER", proposer.Name);
            textObject.SetTextVariable("TARGET", breakdown.Target.Name);
            textObject.SetTextVariable("REASONS", string.Join(" ", reasons));
            return textObject.ToString();
        }
        #endregion
    }
}