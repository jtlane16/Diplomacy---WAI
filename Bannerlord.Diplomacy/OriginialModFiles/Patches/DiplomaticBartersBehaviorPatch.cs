using Diplomacy.Extensions;
using Diplomacy.PatchTools;

using System.Collections.Generic;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors.BarterBehaviors;

namespace Diplomacy.Patches
{
    /// <summary>
    /// Blocks AI from declaring war or peace due to the AI diplomatic barter behavior.
    /// The other way for the AI to consider war/peace is via a kingdom decision proposal.
    /// </summary>
    internal sealed class DiplomaticBartersBehaviorPatch : PatchClass<DiplomaticBartersBehaviorPatch, DiplomaticBartersBehavior>
    {
        protected override IEnumerable<Patch> Prepare() => new Patch[]
        {
            new Prefix(nameof(ConsiderWarPrefix), "ConsiderWar"),
            // ADDED: New patch to disable vanilla peace considerations from this behavior.
            new Prefix(nameof(ConsiderPeacePrefix), "ConsiderPeace")
        };

        private static bool ConsiderWarPrefix(Clan clan, IFaction otherMapFaction)
        {
            // if the opponent is a rebel kingdom
            if ((otherMapFaction as Kingdom)?.IsRebelKingdom() ?? false)
            {
                return false;
            }

            // if this clan is in a rebel kingdom
            if (clan.Kingdom?.IsRebelKingdom() ?? false)
            {
                return false;
            }

            // enforce cooldowns
            if (CooldownManager.HasDeclareWarCooldown(clan, otherMapFaction, out _))
            {
                return false;
            }

            return false;
        }

        // ADDED: This new method prevents the original ConsiderPeace method from running.
        private static bool ConsiderPeacePrefix()
        {
            return false;
        }
    }
}