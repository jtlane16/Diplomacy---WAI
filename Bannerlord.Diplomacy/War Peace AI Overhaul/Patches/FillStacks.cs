using HarmonyLib;

using MCM.Abstractions.Base.Global;

using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace WarAndAiTweaks
{
	//Feature integration with fillstacks
	// Token: 0x02000002 RID: 2
	[HarmonyPatch(typeof(MobileParty), "FillPartyStacks")]
	public class SpawnLordPartyInternalPatch
	{
		// Token: 0x06000001 RID: 1 RVA: 0x00002048 File Offset: 0x00000248
		public static void Prefix(PartyTemplateObject pt, ref int troopNumberLimit, ref MobileParty __instance)
		{
            if (__instance.LeaderHero == null || !__instance.IsLordParty || __instance.LeaderHero.Clan == null || __instance.LeaderHero.Clan.IsUnderMercenaryService == true) { return; }
            //InformationManager.DisplayMessage(new InformationMessage("Modfying: " + __instance.LeaderHero.Name.ToString() + "'s party to spawn with 1 troop"));
            troopNumberLimit = 1;
		}
	}
}
