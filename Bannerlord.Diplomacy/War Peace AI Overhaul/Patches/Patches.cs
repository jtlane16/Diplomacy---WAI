using Diplomacy.Extensions;

using HarmonyLib;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Library;

using WarAndAiTweaks.AI.Behaviors;

namespace Diplomacy.War_Peace_AI_Overhaul
{
    internal class Patches
    {
        [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomWarDecision")]
        public class Patch_DisableRandomWar { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

        [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomPeaceDecision")]
        public class Patch_DisableRandomPeace { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

        [HarmonyPatch(typeof(Building), "GetBuildingEffectAmount")]
        public class MilitiaPatch
        {
            // Token: 0x06000001 RID: 1 RVA: 0x00002048 File Offset: 0x00000248
            static void Postfix(Building __instance, BuildingEffectEnum effect, ref float __result)
            {
                //If disabled, skip logic

                if (effect == BuildingEffectEnum.Militia && __instance.Name.ToString() == "Militia Grounds")
                {
                    if (__instance.Town.IsCastle) { __result = __result + 5; }
                    if (__instance.Town.IsTown) { __result = __result + 10; }
                }
                return;
            }
        }
        [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnDeclarePeace")]
        public class KingdomPlayerPeacePatch
        {
            // We need to get an instance of the behavior to call its public method
            private static bool WantsPeace(Kingdom us, Kingdom them)
            {
                // This is a simplified version of the logic in StrategicAIBehavior for the player's perspective.
                // You might want to expose the main behavior's method publicly if more complexity is needed.
                var warProgress = (us.GetStanceWith(them)?.GetSuccessfulSieges(us) ?? 0) - (us.GetStanceWith(them)?.GetSuccessfulSieges(them) ?? 0);
                return (warProgress * 10) > 60f; // Simplified threshold
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
        [HarmonyPatch(typeof(MakePeaceAction), "ApplyInternal")]
        public class MakePeaceActionPatch
        {
            public static void Postfix(IFaction faction1, IFaction faction2, MakePeaceAction.MakePeaceDetail detail)
            {
                // We need a way to access the StrategicAICampaignBehavior instance.
                var strategicAIBehavior = Campaign.Current.GetCampaignBehavior<StrategicAICampaignBehavior>();
                strategicAIBehavior?.OnPeaceDeclared(faction1, faction2, detail);

                // If peace is made between two kingdoms, ensure all their allies also make peace.
                if (faction1 is Kingdom kingdom1 && faction2 is Kingdom kingdom2)
                {
                    // Get all allies of the first kingdom
                    var alliesOf1 = kingdom1.GetAlliedKingdoms().ToList();
                    // Get all allies of the second kingdom
                    var alliesOf2 = kingdom2.GetAlliedKingdoms().ToList();

                    // Allies of kingdom1 make peace with kingdom2 and all of kingdom2's allies.
                    foreach (var ally1 in alliesOf1)
                    {
                        if (ally1.IsAtWarWith(kingdom2))
                        {
                            MakePeaceAction.Apply(ally1, kingdom2);
                        }
                        foreach (var ally2 in alliesOf2)
                        {
                            if (ally1.IsAtWarWith(ally2))
                            {
                                MakePeaceAction.Apply(ally1, ally2);
                            }
                        }
                    }

                    // Allies of kingdom2 make peace with kingdom1.
                    foreach (var ally2 in alliesOf2)
                    {
                        if (ally2.IsAtWarWith(kingdom1))
                        {
                            MakePeaceAction.Apply(ally2, kingdom1);
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(DeclareWarAction), "ApplyByDefault")]
        public class DeclareWarAction_ApplyByDefault_Patch
        {
            /// <summary>
            /// This patch stops allies from being called into offensive wars.
            /// It only establishes war between the two primary factions.
            /// The defensive call-to-arms is handled in StrategicAICampaignBehavior.
            /// </summary>
            public static bool Prefix(IFaction faction1, IFaction faction2)
            {
                // Establish war only between the aggressor (faction1) and the defender (faction2)
                DeclareWarAction.ApplyByDefault(faction1, faction2);

                // Return false to skip the original method, which would have called all allies.
                return false;
            }
        }
    }
}