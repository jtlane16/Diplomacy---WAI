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

            if (homeKingdom == null || homeKingdom == other)
                return false;

            // 1. Get all castles and towns that DO NOT belong to the checking kingdom.
            var foreignFortifications = Settlement.All
                .Where(set => (set.IsTown || set.IsCastle) && set.OwnerClan?.Kingdom != homeKingdom);

            // 2. From that pre-filtered list, find the 5 closest to our settlement.
            var nearestForeignFortifications = foreignFortifications
                .OrderBy(set => set.Position2D.DistanceSquared(s.Position2D))
                .Take(5);

            // 3. Check if any of these 5 closest foreign fiefs belong to the target kingdom 'other'.
            return nearestForeignFortifications.Any(set => set.OwnerClan?.Kingdom == other);
        }
    }
}