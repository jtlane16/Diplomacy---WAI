using Diplomacy.Extensions;

using System.Linq;
using System.Text;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

using static WarAndAiTweaks.AI.StrategicAI;

namespace WarAndAiTweaks.AI
{
    /// <summary>
    /// Builds concise notifications explaining war and peace choices.
    /// </summary>
    public static class DiplomacyReasoning
    {
        /// <summary>
        /// "{KINGDOM} declares war on {TARGET} because {REASON}."
        /// </summary>
        public static string WarNotification(Kingdom aggressor, Kingdom target,
                                             DefaultWarEvaluator eval, int peaceDays)
        {
            float powerRatio = GetStrength(aggressor) / (GetStrength(target) + 1f);
            int borders = aggressor.Settlements.Count(s => s.IsBorderSettlementWith(target));
            float relation = aggressor.GetRelation(target);
            int wars = FactionManager.GetEnemyKingdoms(aggressor).Count();

            var sb = new StringBuilder();

            // Military advantage phrasing
            sb.Append(powerRatio switch
            {
                > 1.5f => "it holds a decisive military advantage",
                > 1.2f => "it feels stronger",
                _ => "it rates the odds as even"
            });

            // Border friction
            if (borders > 0)
                sb.Append($", they share {borders} contested border settlement{(borders > 1 ? "s" : "")}");

            // Hostile relations
            if (relation < -20)
                sb.Append(", relations are hostile");

            // Peace‑buildup note
            if (peaceDays > 15)
                sb.Append($", after {peaceDays} days of peace it seeks expansion");

            // Front count: show actual number of ongoing wars
            if (wars > 0)
                sb.Append($", and is willing to fight on {wars} front{(wars > 1 ? "s" : "")}");

            return new TextObject("{=notif_war}{KINGDOM} declares war on {TARGET} because {REASON}.")
                   .SetTextVariable("KINGDOM", aggressor.Name)
                   .SetTextVariable("TARGET", target.Name)
                   .SetTextVariable("REASON", sb.ToString())
                   .ToString();
        }

        /// <summary>
        /// "{KINGDOM} makes peace with {TARGET} because {REASON}."
        /// </summary>
        public static string PeaceNotification(Kingdom k, Kingdom enemy,
                                       DefaultPeaceEvaluator eval)
        {
            int fronts = FactionManager.GetEnemyKingdoms(k).Count();

            var sb = new StringBuilder();

            sb.Append("it seeks a strategic pause");

            if (fronts > 1)
                sb.Append($" while fighting on {fronts} fronts");

#if DIPOLOMACY_WAR_EXHAUSTION
    if (Diplomacy.WarExhaustion.WarExhaustionManager.Instance is { } wem &&
        wem.IsEnabled &&
        wem.TryGetWarExhaustion(k, enemy, out var we) && we > 50f)
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
