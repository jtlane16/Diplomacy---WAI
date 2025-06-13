using Diplomacy.DiplomaticAction;

using TaleWorlds.CampaignSystem;

namespace Diplomacy.Extensions
{
    internal static class StanceLinkExtensions
    {
        public static string GetStanceName(this StanceLink stance)
        {
            if (stance.IsAtWar) return "War";
            if (stance.IsAllied) return "Alliance";

            // Ensure both factions are Kingdoms before checking for a pact.
            if (stance.Faction1 is Kingdom k1 && stance.Faction2 is Kingdom k2 && DiplomaticAgreementManager.HasNonAggressionPact(k1, k2, out _))
            {
                return "Non-Aggression Pact";
            }
            return "Neutral";
        }
    }
}
