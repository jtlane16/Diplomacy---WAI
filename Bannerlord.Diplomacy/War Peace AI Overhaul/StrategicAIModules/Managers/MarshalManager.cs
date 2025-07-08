using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace WarAndAiTweaks.Strategic.Marshal
{
    public class MarshalManager : CampaignBehaviorBase
    {
        [SaveableField(1)]
        private Dictionary<Kingdom, Hero> _kingdomMarshals = new();

        [SaveableField(2)]
        private Dictionary<Kingdom, CampaignTime> _marshalAssignmentTime = new();

        private const int MarshalDurationDays = 20;

        public static MarshalManager Instance { get; private set; }

        public MarshalManager() => Instance = this;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public class SimpleTextInformationData : InformationData
        {
            public override TextObject TitleText => DescriptionText;
            public override string SoundEventPath => "";

            public SimpleTextInformationData(TextObject description) : base(description) { }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_kingdomMarshals", ref _kingdomMarshals);
            dataStore.SyncData("_marshalAssignmentTime", ref _marshalAssignmentTime);
        }

        public Hero GetMarshal(Kingdom kingdom)
        {
            if (kingdom == null)
                return null;
            _kingdomMarshals.TryGetValue(kingdom, out var marshal);
            return marshal;
        }

        public void SetMarshal(Kingdom kingdom, Hero hero)
        {
            if (kingdom == null || hero == null)
                return;
            _kingdomMarshals[kingdom] = hero;
            _marshalAssignmentTime[kingdom] = CampaignTime.Now;
        }

        private void OnDailyTick()
        {
            var allKingdoms = Kingdom.All;
            if (allKingdoms == null)
                return;

            foreach (var kingdom in allKingdoms)
            {
                if (kingdom == null || kingdom.IsEliminated || kingdom.RulingClan == null || kingdom.Leader == null)
                    continue;

                if (!kingdom.Leader.IsAlive)
                    continue;

                // Only rulers can assign marshals
                if (!kingdom.Leader.IsKingdomLeader)
                    continue;

                Hero currentMarshal = GetMarshal(kingdom);
                _marshalAssignmentTime.TryGetValue(kingdom, out var assignedTime);
                bool expired = currentMarshal == null || assignedTime == null || assignedTime.ElapsedDaysUntilNow >= MarshalDurationDays;

                // If expired or not set, pick a new marshal
                if (expired)
                {
                    Hero newMarshal = SelectBestMarshal(kingdom, out Hero oldMarshal, out bool isRevoked);

                    if (newMarshal != currentMarshal && newMarshal != null)
                    {
                        SetMarshal(kingdom, newMarshal);

                        // Notify player if they are assigned or revoked
                        if (newMarshal == Hero.MainHero)
                        {
                            MBInformationManager.AddQuickInformation(
                                new TextObject($"You have been appointed Marshal of {kingdom.Name}!"), 0, Hero.MainHero.CharacterObject
                            );
                        }
                        else if (currentMarshal == Hero.MainHero)
                        {
                            MBInformationManager.AddQuickInformation(
                                new TextObject($"You are no longer Marshal of {kingdom.Name}."), 0, Hero.MainHero.CharacterObject
                            );
                        }

                        // Print AI marshal selection
                        if (newMarshal != Hero.MainHero)
                        {
                            string skillDesc = GetMarshalSkillDescription(newMarshal);
                            string reason = GetMarshalAppointmentReason(kingdom, newMarshal);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{kingdom.Name} has appointed {newMarshal.Name} as Marshal. {skillDesc}. This is because {reason}.", Colors.Yellow));
                        }

                        // If revoked from the player, apply relation penalty
                        if (isRevoked && currentMarshal == Hero.MainHero)
                        {
                            int penalty = -10; // Example penalty
                            if (kingdom.Leader != null && Hero.MainHero != null)
                                kingdom.Leader.SetPersonalRelation(Hero.MainHero, kingdom.Leader.GetRelation(Hero.MainHero) + penalty);
                        }
                    }
                }
            }
        }

        private Hero SelectBestMarshal(Kingdom kingdom, out Hero oldMarshal, out bool isRevoked)
        {
            oldMarshal = GetMarshal(kingdom);
            isRevoked = false;

            if (kingdom == null || kingdom.Lords == null || kingdom.Leader == null)
                return kingdom?.Leader;

            var candidates = kingdom.Lords
                .Where(h => h != null && h != kingdom.Leader && h.IsAlive && !h.IsPrisoner && h.Clan != null && !h.Clan.IsMinorFaction)
                .ToList();

            candidates.Add(kingdom.Leader);

            Hero best = null;
            float bestScore = float.MinValue;

            foreach (var hero in candidates)
            {
                if (hero == null)
                    continue;

                float leadership = 0f, tactics = 0f;
                try
                {
                    leadership = hero.GetSkillValue(DefaultSkills.Leadership);
                    tactics = hero.GetSkillValue(DefaultSkills.Tactics);
                }
                catch { }

                float trueSkill = (leadership + tactics) / 2f;
                float relation = 0f;
                try
                {
                    relation = kingdom.Leader.GetRelation(hero);
                }
                catch { }

                float clanTier = hero.Clan?.Tier ?? 0;
                float renown = hero.Clan?.Renown ?? 0;

                // Use relation between clan leader and kingdom leader as a loyalty proxy
                float clanLoyalty = 0f;
                try
                {
                    clanLoyalty = kingdom.Leader.GetRelation(hero.Clan.Leader);
                }
                catch { }

                // TrueSkill is the dominant factor, others are supporting
                float score =
                    (trueSkill * 3.0f) +                // Skill is the main factor
                    (float) (Math.Tanh(relation / 50.0) * 8.0) + // Relation, capped
                    (clanTier * 1.5f) +                  // Clan tier, moderate
                    (renown / 200f) +                    // Renown, small
                    (clanLoyalty * 0.05f);               // Loyalty proxy, very small

                if (oldMarshal != null && hero != oldMarshal)
                    score -= 2f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = hero;
                }
            }

            isRevoked = oldMarshal != null && best != oldMarshal;
            return best;
        }

        private string GetMarshalSkillDescription(Hero marshal)
        {
            int leadership = marshal.GetSkillValue(DefaultSkills.Leadership);
            int tactics = marshal.GetSkillValue(DefaultSkills.Tactics);
            float trueSkill = (leadership + tactics) / 2f;

            if (trueSkill >= 300)
                return "Their mastery of war is the stuff of legend, inspiring awe in friend and foe alike";
            if (trueSkill >= 250)
                return "They are a paragon of generalship, their stratagems oft spoken of in noble halls";
            if (trueSkill >= 200)
                return "They are a seasoned captain, well-versed in the arts of command and battle";
            if (trueSkill >= 150)
                return "They are a proven leader, respected for their experience in the field";
            if (trueSkill >= 100)
                return "They are competent in the ways of war, though not without flaw";
            if (trueSkill >= 50)
                return "They are but a fledgling in the art of command, with much yet to learn";
            return "Their understanding of war is meager, and their command inspires little confidence";
        }

        private string GetMarshalAppointmentReason(Kingdom kingdom, Hero marshal)
        {
            if (marshal == null || kingdom == null || kingdom.Leader == null)
                return "no other lord was deemed worthy";

            float relation = kingdom.Leader.GetRelation(marshal);

            if (marshal == kingdom.Leader)
                return "none but the sovereign could bear the burden";

            if (relation > 80)
                return "they are the ruler's most trusted confidant";
            if (relation > 60)
                return "they have long enjoyed the ruler's favor";
            if (relation > 30)
                return "they are held in good esteem by the ruler";

            if (marshal.Clan == kingdom.RulingClan)
                return "they are of the ruling clan, and thus favored";

            return "their merit outshone all other claimants";
        }
    }
    [HarmonyPatch(typeof(DefaultArmyManagementCalculationModel), "CalculatePartyInfluenceCost")]
    public class MarshalPartyInfluenceDiscountPatch
    {
        public static void Postfix(MobileParty armyLeaderParty, MobileParty party, ref int __result)
        {
            if (armyLeaderParty?.LeaderHero == null || armyLeaderParty.LeaderHero.Clan?.Kingdom == null)
                return;

            var marshal = MarshalManager.Instance?.GetMarshal(armyLeaderParty.LeaderHero.Clan.Kingdom);

            if (marshal == null)
                return;

            if (armyLeaderParty.LeaderHero == marshal)
            {
                // Marshal discount logic (optional)
                float leadership = marshal.GetSkillValue(DefaultSkills.Leadership);
                float tactics = marshal.GetSkillValue(DefaultSkills.Tactics);
                float trueSkill = (leadership + tactics) / 2f;
                float discount = Math.Min(trueSkill * 0.005f, 0.5f); // 0.5% per point, max 50%
                __result = (int) Math.Ceiling(__result * (1f - discount));
            }
            else
            {
                // Not marshal: double the cost
                __result = __result * 2;
            }
        }
    }
    [HarmonyPatch(typeof(DefaultArmyManagementCalculationModel), "CalculateDailyCohesionChange")]
    public class MarshalCohesionPenaltyPatch
    {
        public static void Postfix(Army army, bool includeDescriptions, ref ExplainedNumber __result)
        {
            var leaderHero = army.LeaderParty?.LeaderHero;
            var kingdom = leaderHero?.MapFaction as Kingdom;
            var marshalManager = MarshalManager.Instance;
            if (leaderHero != null && kingdom != null && marshalManager != null)
            {
                var marshal = marshalManager.GetMarshal(kingdom);
                if (marshal == null || leaderHero != marshal)
                {
                    // Only apply if cohesion is negative (loss)
                    if (__result.ResultNumber < 0f)
                    {
                        float original = __result.ResultNumber;
                        __result.AddFactor(2.0f, new TaleWorlds.Localization.TextObject("Not Marshal Penalty"));
                    }
                }
            }
        }
    }
}