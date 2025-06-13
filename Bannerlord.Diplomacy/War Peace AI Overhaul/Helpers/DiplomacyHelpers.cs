using System;
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
using TaleWorlds.TwoDimension;

namespace WarAndAiTweaks
{
    public static class DiplomacyHelpers
    {
        #region Constants
        private const float BASE_THRESH = 55f; // Increased base threshold
        private const float THRESH_PEACE_BONUS = -5f;
        private const float THRESH_WAR_PENALTY = +15f; // Increased penalty for being at war
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

        public static float GetEffectiveStrength(Kingdom kingdom)
        {
            float totalStrength = kingdom.TotalStrength;
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
                              + (wars * 20f); // Additive penalty per war
            return baseThresh;
        }

        public static float GetCoalitionStrength(Kingdom kingdom)
        {
            float ownStrength = GetEffectiveStrength(kingdom);

            var allies = kingdom.GetAlliedKingdoms();
            if (allies == null || !allies.Any())
                return ownStrength;

            float alliesStrength = allies.Sum(GetEffectiveStrength) * 0.75f;
            return ownStrength + alliesStrength;
        }

        public static float GetConquestScore(Kingdom us, Kingdom them)
        {
            float conquestScore = 0;

            // 1. Prioritize weaker neighbors
            if (AreNeighbors(us, them))
            {
                float powerRatio = them.TotalStrength / Math.Max(1f, us.TotalStrength);
                if (powerRatio < 0.8f) // If they are significantly weaker
                {
                    // Add a score bonus based on how much weaker they are.
                    // A very weak neighbor is a very tempting target.
                    conquestScore += (1 - powerRatio) * 50f;
                }
            }

            // 2. Incentive to eliminate a faction
            // If a kingdom has few fiefs, they are close to elimination.
            if (them.Fiefs.Count() <= 2 && them.Fiefs.Count() > 0)
            {
                conquestScore += 40f; // Strong bonus to finish them off
            }

            // 3. Reclaim cultural lands
            // Bonus for attacking a kingdom that holds fiefs of our culture.
            int reclaimableFiefs = them.Fiefs.Count(f => f.Culture == us.Culture);
            if (reclaimableFiefs > 0)
            {
                conquestScore += reclaimableFiefs * 15f;
            }

            return conquestScore;
        }

        public static WarScoreBreakdown ComputeWarDesireScore(Kingdom us, Kingdom them)
        {
            var breakdown = new WarScoreBreakdown(them);

            float usStr = GetCoalitionStrength(us);
            float themStr = GetCoalitionStrength(them);

            // REBALANCE: Drastically reduced the impact of both relative strength and fief count on threat.
            breakdown.ThreatScore = (themStr / Mathf.Max(1f, usStr)) * 20f + them.Settlements.Count * 0.5f;
            breakdown.PowerBalanceScore = (usStr - themStr) / (usStr + themStr + 1f);

            // REBALANCE: A more nuanced and punitive multi-war penalty.
            int numWars = MajorEnemies(us).Count();
            float multiWarPenalty;
            if (numWars == 1)
            {
                // High penalty for a 2nd war
                multiWarPenalty = 60f;
            }
            else if (numWars == 2)
            {
                // Very high penalty for a 3rd war
                multiWarPenalty = 150f;
            }
            else if (numWars > 2)
            {
                // Prohibitive penalty for any more wars
                multiWarPenalty = 500f;
            }
            else
            {
                multiWarPenalty = 0f;
            }
            breakdown.MultiWarPenalty = multiWarPenalty;

            float distKm = Campaign.Current.Models.MapDistanceModel.GetDistance(us.FactionMidSettlement, them.FactionMidSettlement);
            breakdown.DistancePenalty = distKm * 0.25f; // Slightly increased distance penalty

            var targetEnemies = MajorEnemies(them).ToList();
            var allKingdoms = MajorKingdoms().ToList();
            float avgFiefs = allKingdoms.Any() ? (float) allKingdoms.Average(k => k.Settlements.Count) : 0f;
            bool isTargetSnowballing = (them.TotalStrength > us.TotalStrength * 1.5f) && (them.Settlements.Count > avgFiefs * 1.5f);

            if (isTargetSnowballing && targetEnemies.Any())
            {
                breakdown.DogpileBonus = 35f + (targetEnemies.Count * 10f);
            }

            const float W_THREAT = 1.0f, W_BALANCE = 1.5f, W_MULTI = 1.0f, W_DISTANCE = 1.0f;
            breakdown.FinalScore = W_THREAT * breakdown.ThreatScore
                                 + W_BALANCE * breakdown.PowerBalanceScore
                                 - W_MULTI * breakdown.MultiWarPenalty
                                 - W_DISTANCE * breakdown.DistancePenalty
                                 + breakdown.DogpileBonus;

            float ourEconomicReadiness = CalculateEconomicBoost(us, false) * 20f;
            breakdown.FinalScore += ourEconomicReadiness;

            float targetEconomicValue = CalculateEconomicBoost(them, false) * 25f;
            breakdown.FinalScore += targetEconomicValue;

            breakdown.ConquestScore = GetConquestScore(us, them);
            breakdown.FinalScore += breakdown.ConquestScore;

            return breakdown;
        }

        public static PeaceScoreBreakdown ComputePeaceScore(Kingdom us, Kingdom them, float usExhaustion)
        {
            var breakdown = new PeaceScoreBreakdown(them)
            {
                DangerScore = (GetCoalitionStrength(them) / Mathf.Max(1f, GetCoalitionStrength(us))) * 50f + them.Settlements.Count * 2f,
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
        public static string GenerateWarReasoning(Kingdom us, WarScoreBreakdown breakdown)
        {
            // Determine the primary driver for the war based on which score component was the highest
            var scores = new Dictionary<string, float>
            {
                { "Conquest", breakdown.ConquestScore },
                { "Threat", breakdown.ThreatScore },
                { "Dogpile", breakdown.DogpileBonus }
            };

            var primaryReason = scores.OrderByDescending(kv => kv.Value).First().Key;

            TextObject reasonText;

            switch (primaryReason)
            {
                case "Conquest":
                    reasonText = new TextObject("{=war_reason_conquest}Driven by ambition, {KINGDOM_NAME} sees {ENEMY_NAME} as a prime target for expansion and has declared war!");
                    break;
                case "Dogpile":
                    reasonText = new TextObject("{=war_reason_dogpile}Seeing that {ENEMY_NAME} is embroiled in other conflicts, {KINGDOM_NAME} has declared war, hoping to capitalize on their distraction.");
                    break;
                case "Threat":
                default:
                    reasonText = new TextObject("{=war_reason_threat}Feeling threatened by the growing power of {ENEMY_NAME}, {KINGDOM_NAME} has launched a preventative war to curb their influence.");
                    break;
            }

            reasonText.SetTextVariable("KINGDOM_NAME", us.Name);
            reasonText.SetTextVariable("ENEMY_NAME", breakdown.Target.Name);
            return reasonText.ToString();
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