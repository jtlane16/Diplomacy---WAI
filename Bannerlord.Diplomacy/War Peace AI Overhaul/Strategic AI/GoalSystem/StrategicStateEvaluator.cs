using TaleWorlds.CampaignSystem;
using System.Linq;
using Diplomacy.WarExhaustion;
using Diplomacy;

namespace WarAndAiTweaks.AI.Goals
{
    public static class StrategicStateEvaluator
    {
        private const float WEAK_STRENGTH_THRESHOLD = 0.8f;
        private const float STRONG_STRENGTH_THRESHOLD = 1.4f;  // Relaxed from 1.5f
        private const float CRITICAL_WEALTH_THRESHOLD = 150000; // Increased slightly
        private const float RICH_WEALTH_THRESHOLD = 750000;   // Relaxed from 1,000,000
        private const float CRITICAL_WAR_EXHAUSTION = 80f;

        public static StrategicState GetStrategicState(Kingdom kingdom)
        {
            var enemies = FactionManager.GetEnemyKingdoms(kingdom).ToList();

            // LOGIC REORDERED: War-based states are now evaluated first.
            if (enemies.Any())
            {
                var totalEnemyStrength = enemies.Sum(e => e.TotalStrength);
                var strengthRatioVsEnemies = totalEnemyStrength > 0 ? kingdom.TotalStrength / totalEnemyStrength : float.MaxValue;

                // Desperate State
                if (kingdom.Fiefs.Count == 0) return StrategicState.Desperate;
                if (Settings.Instance!.EnableWarExhaustion && WarExhaustionManager.Instance is { } wem)
                {
                    if (enemies.Max(enemy => wem.GetWarExhaustion(kingdom, enemy)) > CRITICAL_WAR_EXHAUSTION)
                        return StrategicState.Desperate;
                }
                if (strengthRatioVsEnemies < 0.5f && kingdom.Fiefs.Count < 3)
                {
                    return StrategicState.Desperate;
                }

                // Defensive State
                if (strengthRatioVsEnemies < WEAK_STRENGTH_THRESHOLD)
                {
                    return StrategicState.Defensive;
                }
            }

            // Expansionist State (Conditions relaxed)
            var averageKingdomStrength = Kingdom.All.Where(k => !k.IsEliminated).Average(k => k.TotalStrength);
            if (kingdom.TotalStrength > STRONG_STRENGTH_THRESHOLD * averageKingdomStrength
                && kingdom.RulingClan.Gold > RICH_WEALTH_THRESHOLD && !enemies.Any())
            {
                return StrategicState.Expansionist;
            }

            // Default state
            return StrategicState.Opportunistic;
        }
    }
}