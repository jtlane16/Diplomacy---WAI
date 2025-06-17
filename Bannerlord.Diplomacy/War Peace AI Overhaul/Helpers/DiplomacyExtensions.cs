using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

// This namespace should be accessible by the rest of your code.
namespace Diplomacy.Extensions
{
    /// <summary>
    /// Extension methods for Campaign objects.
    /// </summary>
    internal static class DiplomacyKitExtensions
    {
        public static float GetRelation(this Kingdom kingdom, Kingdom otherKingdom)
        {
            if (kingdom == otherKingdom)
                return 100f;
            return kingdom.RulingClan.GetRelationWithClan(otherKingdom.RulingClan);
        }

        public static float GetCasualties(this Kingdom kingdom)
        {
            // The original logic seems to have been removed, returning 0 to ensure compilation.
            // You may need to wire this up to your war exhaustion system if needed.
            return 0f;
        }

        public static bool IsBorderSettlementWith(this Settlement s, Kingdom other)
        {
            var homeKingdom = s.OwnerClan.Kingdom;
            if (homeKingdom == other)
                return false;

            // CORRECTED: This version uses a valid API call to find nearby settlements.
            float searchRadius = 30f;
            var nearbyFortifications = Settlement.All.Where(set => set.IsFortification && set.Position2D.Distance(s.Position2D) < searchRadius);

            foreach (var settlement in nearbyFortifications)
            {
                if (settlement.OwnerClan?.Kingdom == other)
                {
                    return true;
                }
            }
            return false;
        }
    }
}