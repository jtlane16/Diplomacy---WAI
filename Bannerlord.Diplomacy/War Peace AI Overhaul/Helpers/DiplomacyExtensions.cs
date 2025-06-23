using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

using WarAndAiTweaks;
using WarAndAiTweaks.DiplomaticAction;

namespace Diplomacy.Extensions
{
    internal static class DiplomacyKitExtensions
    {
        public static float GetRelation(this Kingdom kingdom, Kingdom otherKingdom)
        {
            if (kingdom == otherKingdom)
                return 100f;
            if (kingdom.RulingClan == null || otherKingdom.RulingClan == null)
                return 0f; // Return a neutral relationship if a clan is destroyed
            return kingdom.RulingClan.GetRelationWithClan(otherKingdom.RulingClan);
        }

        public static bool IsBorderSettlementWith(this Settlement s, Kingdom other)
        {
            var homeKingdom = s.OwnerClan.Kingdom;

            if (homeKingdom == null || homeKingdom == other)
                return false;

            var foreignFortifications = Settlement.All
                .Where(set => (set.IsTown || set.IsCastle) && set.OwnerClan?.Kingdom != homeKingdom);

            var nearestForeignFortifications = foreignFortifications
                .OrderBy(set => set.Position2D.DistanceSquared(s.Position2D))
                .Take(5);

            return nearestForeignFortifications.Any(set => set.OwnerClan?.Kingdom == other);
        }

        public static IEnumerable<Kingdom> GetBorderingKingdoms(this Kingdom kingdom)
        {
            var otherKingdoms = Kingdom.All.Where(k => !k.IsEliminated && k != kingdom).ToList();
            var borderingKingdoms = new List<Kingdom>();

            foreach (var otherKingdom in otherKingdoms)
            {
                if (kingdom.Settlements.Any(s => s.IsBorderSettlementWith(otherKingdom)))
                {
                    borderingKingdoms.Add(otherKingdom);
                }
            }
            return borderingKingdoms;
        }

        public static IEnumerable<Kingdom> GetAlliedKingdoms(this Kingdom kingdom)
        {
            return DiplomaticAgreementManager.Alliances
                .Where(a => a.Faction1 == kingdom || a.Faction2 == kingdom)
                .Select(a => a.GetOtherKingdom(kingdom));
        }
    }
}