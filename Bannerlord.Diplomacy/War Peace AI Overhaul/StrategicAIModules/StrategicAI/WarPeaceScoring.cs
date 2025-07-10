using Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

using MathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.WarPeaceAI
{
    public class PeaceScoring
    {
        public static float GetTotalPeaceScore(Kingdom self, Kingdom enemy)
        {
            float score = 0;

            // 1. Military & power balance
            score += IsStrongerArmy(enemy, self) ? 15f : 0f;
            score += MathF.Clamp((1f - KingdomPowerEvaluator.GetRelativePower(self, enemy)) * 60f, -30f, 30f);

            // 2. Geography & strategic context
            score -= WarPeaceLogicHelpers.GetBorderDistancePenalty(self, enemy, 30f, 100f);
            score += GetSnowballingPeaceBonus(self, enemy);
            score += WarPeaceLogicHelpers.GetMultipleWarsPenalty(self, 15f);

            // 3. Fatigue vs zeal
            score += WarPeaceLogicHelpers.GetWarWearinessScore(self) * 3f;      // heavier weariness
            score -= WarPeaceLogicHelpers.GetWarEagernessScore(self) * 0.75f;   // damped eagerness

            // 4. Tribute incentive
            score += GetPeaceTributeScore(self, enemy, 0.001f, 20f);

            // 5. Cool-down for very young wars
            score += WarPeaceLogicHelpers.GetShortWarPeacePenalty(
                         self, enemy,
                         minWarDays: 7,
                         maxPenalty: 75f);

            return score;
        }


        public static float GetPeaceTributeScore(Kingdom self, Kingdom enemy, float scaling = 0.0015f, float maxScore = 25f)
        {
            Clan selfClan = self.Leader?.Clan;
            Clan enemyClan = enemy.Leader?.Clan;
            if (selfClan == null || enemyClan == null)
                return 0f;

            int dailyTribute = WarPeaceLogicHelpers.GetPeaceTribute(selfClan, enemyClan, self, enemy);
            float score = dailyTribute * scaling;
            return MathF.Clamp(score, -maxScore, maxScore);
        }

        public static bool IsStrongerArmy(Kingdom self, Kingdom target)
        {
            if (target == null || self == null || target.TotalStrength <= 0)
                return false;
            if (self.TotalStrength >= 1.1f * target.TotalStrength)
                return true;
            return false;
        }

        public static float GetSnowballingPeaceBonus(Kingdom self, Kingdom enemy)
        {
            var snowballers = WarPeaceLogicHelpers.GetSnowballingKingdoms();
            foreach (var snowballer in snowballers)
            {
                if (snowballer != self && snowballer != enemy && !self.IsAtWarWith(snowballer))
                {
                    return 30;
                }
            }
            return 0;
        }

        public static bool IsInMultipleWars(Kingdom self)
        {
            return WarPeaceLogicHelpers.GetEnemyKingdoms(self).Count > 1;
        }
        public static ScoreBreakdown GetPeaceScoreBreakdown(Kingdom self, Kingdom enemy)
        {
            var breakdown = new ScoreBreakdown();
            float score = 0;

            float strongerArmy = PeaceScoring.IsStrongerArmy(enemy, self) ? 15 : 0;
            breakdown.Factors.Add(("EnemyStrongerArmy", strongerArmy));
            score += strongerArmy;

            float relPower = MathF.Clamp((1f - KingdomPowerEvaluator.GetRelativePower(self, enemy)) * 60f, -30f, 30f);
            breakdown.Factors.Add(("RelativePower", relPower));
            score += relPower;

            float borderPenalty = -WarPeaceLogicHelpers.GetBorderDistancePenalty(self, enemy, 30f, 100f);
            breakdown.Factors.Add(("BorderDistancePenalty", borderPenalty));
            score += borderPenalty;

            float snowball = PeaceScoring.GetSnowballingPeaceBonus(self, enemy);
            breakdown.Factors.Add(("SnowballingBonus", snowball));
            score += snowball;

            float multiWar = WarPeaceLogicHelpers.GetMultipleWarsPenalty(self, 15f);
            breakdown.Factors.Add(("MultipleWarsPenalty", multiWar));
            score += multiWar;

            float weariness = WarPeaceLogicHelpers.GetWarWearinessScore(self) * 2.5f;
            breakdown.Factors.Add(("WarWearinessScore", weariness));
            score += weariness;

            float eagerness = -WarPeaceLogicHelpers.GetWarEagernessScore(self);
            breakdown.Factors.Add(("WarEagernessScore", eagerness));
            score += eagerness;

            float tribute = PeaceScoring.GetPeaceTributeScore(self, enemy, 0.0015f, 25f);
            breakdown.Factors.Add(("PeaceTributeScore", tribute));
            score += tribute;

            float shortWarPenalty = WarPeaceLogicHelpers.GetShortPeaceWarPenalty(self, enemy);
            breakdown.Factors.Add(("ShortWarPenalty", shortWarPenalty));
            score += shortWarPenalty;

            breakdown.Total = score;
            return breakdown;
        }
    }

    public class WarScoring
    {
        public static float GetTotalWarScore(Kingdom self, Kingdom target)
        {
            float score = 0;

            // 1. Military & power balance
            score += IsStrongerArmy(self, target) ? 25f : 0f;  // slightly toned down from 30
            score += MathF.Clamp((KingdomPowerEvaluator.GetRelativePower(self, target) - 1f) * 60f, -30f, 30f);

            // 2. Geography & strategic context
            score += WarPeaceLogicHelpers.AreBordering(self, target) ? 10f : 0f;
            score -= WarPeaceLogicHelpers.GetBorderDistancePenalty(self, target, 50f, 100f);
            score += GetSnowballingWarBonus(self, target);
            score -= WarPeaceLogicHelpers.GetMultipleWarsPenalty(self, 20f);   // harsher penalty for over-extension

            // 3. Zeal vs fatigue
            score += WarPeaceLogicHelpers.GetWarEagernessScore(self) * 2.5f;   // eagerness helps, but a bit less
            score -= WarPeaceLogicHelpers.GetWarWearinessScore(self) * 5f;     // weariness cripples war score

            // 4. Cool-down: prevent instant re-declarations
            score -= WarPeaceLogicHelpers.GetShortPeaceWarPenalty(
                self, target,
                minPeaceDays: 5,
                maxPenalty: 75f);

            return score;
        }

        public static bool IsStrongerArmy(Kingdom self, Kingdom target)
        {
            if (target == null || self == null || target.TotalStrength <= 0)
                return false;
            if (self.TotalStrength >= 1.1f * target.TotalStrength)
                return true;
            return false;
        }

        public static bool isInWar(Kingdom target)
        {
            return WarPeaceLogicHelpers.GetEnemyKingdoms(target).Count > 0;
        }

        public static float GetSnowballingWarBonus(Kingdom self, Kingdom target)
        {
            var snowballers = WarPeaceLogicHelpers.GetSnowballingKingdoms();
            if (snowballers.Contains(target))
            {
                return 20;
            }
            return 0;
        }

        public static (Kingdom kingdom, float score) GetBestWarTarget(Kingdom self)
        {
            var possibleTargets = Kingdom.All
                .Where(k => k != self && !k.IsEliminated && !k.IsMinorFaction && !self.IsAtWarWith(k))
                .ToList();

            Kingdom bestKingdom = null;
            float bestScore = int.MinValue;

            foreach (var target in possibleTargets)
            {
                float score = GetTotalWarScore(self, target);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestKingdom = target;
                }
            }

            return (bestKingdom, bestScore);
        }

        public static ScoreBreakdown GetWarScoreBreakdown(Kingdom self, Kingdom target)
        {
            var breakdown = new ScoreBreakdown();
            float score = 0;

            float strongerArmy = WarScoring.IsStrongerArmy(self, target) ? 30 : 0;
            breakdown.Factors.Add(("StrongerArmy", strongerArmy));
            score += strongerArmy;

            float relPower = MathF.Clamp((KingdomPowerEvaluator.GetRelativePower(self, target) - 1f) * 80f, -40f, 40f);
            breakdown.Factors.Add(("RelativePower", relPower));
            score += relPower;

            float bordering = WarPeaceLogicHelpers.AreBordering(self, target) ? 10 : 0;
            breakdown.Factors.Add(("BorderingBonus", bordering));
            score += bordering;

            float borderPenalty = -WarPeaceLogicHelpers.GetBorderDistancePenalty(self, target, 20f, 100f);
            breakdown.Factors.Add(("BorderDistancePenalty", borderPenalty));
            score += borderPenalty;

            float snowball = WarScoring.GetSnowballingWarBonus(self, target);
            breakdown.Factors.Add(("SnowballingBonus", snowball));
            score += snowball;

            float multiWar = -WarPeaceLogicHelpers.GetMultipleWarsPenalty(self, 15f);
            breakdown.Factors.Add(("MultipleWarsPenalty", multiWar));
            score += multiWar;

            float eagerness = WarPeaceLogicHelpers.GetWarEagernessScore(self) * 3.5f;
            breakdown.Factors.Add(("WarEagernessScore", eagerness));
            score += eagerness;

            float weariness = -WarPeaceLogicHelpers.GetWarWearinessScore(self) * 1.5f;
            breakdown.Factors.Add(("WarWearinessScore", weariness));
            score += weariness;

            breakdown.Total = score;
            return breakdown;
        }
    }
    internal static class KingdomPowerEvaluator
    {
        /// <summary>
        /// Compares the "overall power" of two kingdoms based on military strength,
        /// average settlement prosperity, and average lord wealth.
        /// Returns true if kingdomA is more powerful than kingdomB.
        /// </summary>

        private static float GetOverallPowerScore(Kingdom kingdom)
        {
            // TotalStrength
            float strength = kingdom.TotalStrength;

            // Average prosperity across all settlements
            var settlements = kingdom.Settlements.Where(s => s != null && (s.IsTown || s.IsCastle)).ToList();
            float avgProsperity = settlements.Count > 0
                ? (float) settlements.Average(s => s.Town.Prosperity)
                : 0f;

            // Average wealth of all lords in the kingdom
            var lords = kingdom.Clans
                .Where(c => !c.IsEliminated && c.Leader != null)
                .Select(c => c.Leader)
                .Where(h => h.IsLord)
                .ToList();

            float avgWealth = lords.Count > 0
                ? (float) lords.Average(l => l.Gold)
                : 0f;

            // Combine the metrics (weights can be adjusted as needed)
            return strength + avgProsperity + avgWealth;
        }

        public static float GetRelativePower(Kingdom kingdomA, Kingdom kingdomB)
        {
            if (kingdomA == null || kingdomB == null)
                return 1f;
            float powerA = GetOverallPowerScore(kingdomA);
            float powerB = GetOverallPowerScore(kingdomB);
            if (powerB <= 0.01f) return 1f;
            return powerA / powerB;
        }
    }
    public class ScoreBreakdown
    {
        public float Total;
        public List<(string Label, float Value)> Factors = new();
    }
}