
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace WarAndAiTweaks.AI
{
    /// <summary>
    /// Builds concise notifications explaining war and peace choices.
    /// </summary>
    public static class DiplomacyReasoning
    {
        public static string WarNotification(Kingdom aggressor, Kingdom target,
                                             StrategicAI.DefaultWarEvaluator eval, int peaceDays)
        {
            float powerRatio = GetStrength(aggressor) / (GetStrength(target) + 1f);
            int borders      = aggressor.Settlements.Count(s => s.IsBorderSettlementWith(target));
            float relation   = aggressor.GetRelation(target);
            int wars         = FactionManager.GetEnemyKingdoms(aggressor).Count();

            var sb = new StringBuilder();

            sb.Append(powerRatio switch
            {
                > 1.5f => "it holds a decisive military advantage",
                > 1.2f => "it feels stronger",
                _      => "it rates the odds as even"
            });

            if (borders > 0)
                sb.Append($", they share {borders} contested border settlement{(borders > 1 ? "s" : "")}");

            if (relation < -20)
                sb.Append(", relations are hostile");

            if (peaceDays > 15)
                sb.Append($", after {peaceDays} days of peace it seeks expansion");

            if (wars > 0)
                sb.Append($", and is willing to fight on {wars + 1} fronts");

            return new TextObject("{=notif_war}{KINGDOM} declares war on {TARGET} because {REASON}.")
                   .SetTextVariable("KINGDOM", aggressor.Name)
                   .SetTextVariable("TARGET",  target.Name)
                   .SetTextVariable("REASON",  sb.ToString())
                   .ToString();
        }

        public static string PeaceNotification(Kingdom k, Kingdom enemy,
                                               StrategicAI.DefaultPeaceEvaluator eval)
        {
            float casualtiesRatio = k.GetCasualties() / (k.TotalStrength + 1f);
            int fronts            = FactionManager.GetEnemyKingdoms(k).Count();

            var sb = new StringBuilder();

            sb.Append(casualtiesRatio switch
            {
                > 0.5f => "its army has taken severe losses",
                > 0.25f => "casualties are mounting",
                _       => "it seeks a strategic pause"
            });

            if (fronts > 1)
                sb.Append($" while fighting on {fronts} fronts");

#if DIPOLOMACY_WAR_EXHAUSTION
            if (Diplomacy.WarExhaustion.WarExhaustionManager.Instance is { } wem &&
                wem.IsEnabled && wem.TryGetWarExhaustion(k, enemy, out var we) && we > 50f)
            {
                sb.Append(", war exhaustion is high");
            }
#endif
            return new TextObject("{=notif_peace}{KINGDOM} makes peace with {TARGET} because {REASON}.")
                   .SetTextVariable("KINGDOM", k.Name)
                   .SetTextVariable("TARGET", enemy.Name)
                   .SetTextVariable("REASON", sb.ToString())
                   .ToString();
        }

        private static float GetStrength(Kingdom kingdom) =>
            kingdom.TotalStrength +
            Kingdom.All.Where(k => FactionManager.IsAlliedWithFaction(k, kingdom)).Sum(k => k.TotalStrength);
    }
}
