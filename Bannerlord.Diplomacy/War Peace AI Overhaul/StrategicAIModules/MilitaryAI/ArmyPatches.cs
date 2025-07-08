using HarmonyLib;

using System;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

[HarmonyPatch(typeof(PartyThinkParams), "AddBehaviorScore")]
public class AIChasePreventionPatch
{
    public static bool Prefix(PartyThinkParams __instance, ref ValueTuple<AIBehaviorTuple, float> value)
    {
        var party = __instance.MobilePartyOf;
        var behaviorTuple = value.Item1;

        // Only apply restrictions to ARMY LEADERS
        if (party?.Army?.LeaderParty == party && party.LeaderHero != null)
        {
            // ARMY RESTRICTION 1: Block chase behaviors for faster parties
            if (IsFasterPartyChase(party, behaviorTuple))
            {
                return false; // COMPLETELY BLOCK - don't add this behavior score at all
            }

            // ARMY RESTRICTION 2: Block patrol behaviors - armies should not patrol
            if (IsPatrolBehavior(behaviorTuple))
            {
                return false; // COMPLETELY BLOCK - armies don't patrol
            }

            // ARMY RESTRICTION 3: Block raid behaviors - armies focus on major operations
            if (IsRaidBehavior(behaviorTuple))
            {
                return false; // COMPLETELY BLOCK - armies don't raid
            }
        }

        return true; // Allow normal processing for other behaviors
    }

    // Chase detection logic
    private static bool IsFasterPartyChase(MobileParty chaser, AIBehaviorTuple behaviorTuple)
    {
        // Apply to ALL party-chasing behaviors
        if (behaviorTuple.AiBehavior != AiBehavior.GoAroundParty &&
            behaviorTuple.AiBehavior != AiBehavior.EngageParty)
            return false;

        MobileParty targetParty = behaviorTuple.Party as MobileParty;
        if (targetParty == null)
            return false;

        // If target is even slightly faster, block the chase
        float chaserSpeed = chaser.Speed;
        float targetSpeed = targetParty.Speed;

        // Block if target is faster or equal speed (no advantage)
        bool isFaster = targetSpeed >= chaserSpeed;

        return isFaster;
    }

    // NEW: Patrol behavior detection
    private static bool IsPatrolBehavior(AIBehaviorTuple behaviorTuple)
    {
        return behaviorTuple.AiBehavior == AiBehavior.PatrolAroundPoint;
    }

    // NEW: Raid behavior detection
    private static bool IsRaidBehavior(AIBehaviorTuple behaviorTuple)
    {
        return behaviorTuple.AiBehavior == AiBehavior.RaidSettlement;
    }
}