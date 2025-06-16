using HarmonyLib;

using MCM.Abstractions.Base.Global;

using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

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
			//If disabled, skip logic
			if (__instance.LeaderHero != null && __instance.LeaderHero.Clan != null) { return; }
			if (!__instance.IsLordParty) { return; }
			if (__instance.LeaderHero.Clan.IsUnderMercenaryService) { return; }

			troopNumberLimit = 3;
		}
	}

    //Feature to change the cost of a garrison
    // Token: 0x02000002 RID: 2
    [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculatePartyWage")]
    public class garrisonWagePatch
    {
        //Changes for Garrison cost calculation
        // Token: 0x06000001 RID: 1 RVA: 0x00002048 File Offset: 0x00000248
        static void Postfix(MobileParty mobileParty, ref int __result)
        {

            if (mobileParty.IsGarrison) { __result = (int) (__result * 0.5); }
            return;
        }
    }
}
