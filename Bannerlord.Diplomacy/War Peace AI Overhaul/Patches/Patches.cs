using Diplomacy.WarExhaustion;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Library;

namespace Diplomacy.War_Peace_AI_Overhaul
{
    internal class Patches
    {
        [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomWarDecision")]
        public static class Patch_DisableRandomWar { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

        [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomPeaceDecision")]
        public static class Patch_DisableRandomPeace { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

        [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnDeclarePeace")]
        public class KingdomPlayerPeacePatch
        {
            // We need to get an instance of the behavior to call its public method
            private static bool WantsPeace(Kingdom us, Kingdom them)
            {
                // This is a simplified version of the logic in StrategicAIBehavior for the player's perspective.
                // You might want to expose the main behavior's method publicly if more complexity is needed.
                var warProgress = (us.GetStanceWith(them)?.GetSuccessfulSieges(us) ?? 0) - (us.GetStanceWith(them)?.GetSuccessfulSieges(them) ?? 0);
                var exhaustion = WarExhaustionManager.Instance?.GetWarExhaustion(us, them) ?? 0f;
                return (exhaustion - (warProgress * 10)) > 60f; // Simplified threshold
            }

            public static bool Prefix(KingdomWarItemVM item)
            {
                var playerKingdom = Hero.MainHero.Clan.Kingdom;
                var targetKingdom = item.Faction2 as Kingdom;
                if (playerKingdom == null || targetKingdom == null) return true;

                // We check if the AI kingdom wants peace with the player kingdom.
                if (WantsPeace(targetKingdom, playerKingdom))
                {
                    return true;
                }

                InformationManager.DisplayMessage(new InformationMessage($"{targetKingdom.Name} is not interested in peace at this time.", Colors.Red));
                return false;
            }
        }
    }
}
