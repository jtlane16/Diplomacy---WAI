using System.Collections.Generic;
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

        /// <summary>
        /// Gets all kingdoms that share a border with the given kingdom.
        /// </summary>
        public static IEnumerable<Kingdom> GetBorderingKingdoms(this Kingdom kingdom)
        {
            // Get all active kingdoms except for the kingdom itself.
            // FIXED: Removed redundant 'k.IsKingdomFaction' check, as 'k' is already of type Kingdom.
            var otherKingdoms = Kingdom.All.Where(k => !k.IsEliminated && k != kingdom).ToList();
            var borderingKingdoms = new List<Kingdom>();

            foreach (var otherKingdom in otherKingdoms)
            {
                // A kingdom borders another if any of its settlements is a border settlement with the other kingdom.
                if (kingdom.Settlements.Any(s => s.IsBorderSettlementWith(otherKingdom)))
                {
                    borderingKingdoms.Add(otherKingdom);
                }
            }
            return borderingKingdoms;
        }
    }
}