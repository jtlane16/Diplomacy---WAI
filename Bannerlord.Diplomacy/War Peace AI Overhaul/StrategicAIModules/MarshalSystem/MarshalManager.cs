using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

using WarAndAiTweaks.Patches;

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
            CampaignEvents.ArmyCreated.AddNonSerializedListener(this, OnArmyCreated);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnPartyDestroyed);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnHeroPrisonerReleased);
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

            Hero oldMarshal = GetMarshal(kingdom);
            _kingdomMarshals[kingdom] = hero;
            _marshalAssignmentTime[kingdom] = CampaignTime.Now;

            // Notify the map tracker about marshal changes
            MarshalTrackingExtensions.OnMarshalChanged(kingdom, oldMarshal, hero);
        }

        public void RemoveMarshal(Kingdom kingdom)
        {
            if (kingdom == null)
                return;

            Hero oldMarshal = GetMarshal(kingdom);
            _kingdomMarshals.Remove(kingdom);
            _marshalAssignmentTime.Remove(kingdom);

            // Notify the map tracker
            MarshalTrackingExtensions.OnMarshalChanged(kingdom, oldMarshal, null);
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification = true)
        {
            if (victim == null)
                return;

            // Check if the killed hero was a marshal
            foreach (var kvp in _kingdomMarshals.ToList())
            {
                if (kvp.Value == victim)
                {
                    // Remove the dead marshal and notify map tracker
                    Kingdom kingdom = kvp.Key;
                    _kingdomMarshals.Remove(kingdom);
                    _marshalAssignmentTime.Remove(kingdom);
                    MarshalTrackingExtensions.OnMarshalChanged(kingdom, victim, null);

                    // Optionally notify player if it was their kingdom's marshal
                    if (kingdom == Hero.MainHero?.Clan?.Kingdom)
                    {
                        MBInformationManager.AddQuickInformation(
                            new TextObject($"Marshal {victim.Name} has died. A new marshal will be appointed soon."),
                            0,
                            null,
                            "event:/ui/notification/relation"
                        );
                    }
                }
            }
        }

        private void OnPartyDestroyed(MobileParty destroyedParty, PartyBase arg2)
        {
            if (destroyedParty?.LeaderHero == null)
                return;

            // Check if the destroyed party belonged to a marshal
            foreach (var kvp in _kingdomMarshals.ToList())
            {
                if (kvp.Value?.PartyBelongedTo == destroyedParty)
                {
                    Kingdom kingdom = kvp.Key;
                    Hero marshal = kvp.Value;

                    // Remove marshal assignment since their party is destroyed
                    _kingdomMarshals.Remove(kingdom);
                    _marshalAssignmentTime.Remove(kingdom);
                    MarshalTrackingExtensions.OnMarshalChanged(kingdom, marshal, null);

                    if (kingdom == Hero.MainHero?.Clan?.Kingdom)
                    {
                        MBInformationManager.AddQuickInformation(
                            new TextObject($"Marshal {marshal.Name}'s party has been destroyed. A new marshal will be appointed."),
                            0,
                            null,
                            "event:/ui/notification/relation"
                        );
                    }
                }
            }
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            // If a marshal's clan changes kingdom, remove them as marshal
            foreach (var kvp in _kingdomMarshals.ToList())
            {
                if (kvp.Value?.Clan == clan)
                {
                    Kingdom kingdom = kvp.Key;
                    Hero marshal = kvp.Value;

                    _kingdomMarshals.Remove(kingdom);
                    _marshalAssignmentTime.Remove(kingdom);
                    MarshalTrackingExtensions.OnMarshalChanged(kingdom, marshal, null);

                    if (kingdom == Hero.MainHero?.Clan?.Kingdom)
                    {
                        MBInformationManager.AddQuickInformation(
                            new TextObject($"Marshal {marshal.Name} has left the kingdom. A new marshal will be appointed."),
                            0,
                            null,
                            "event:/ui/notification/relation"
                        );
                    }
                }
            }
        }

        private void OnHeroPrisonerTaken(PartyBase capturerParty, Hero prisoner)
        {
            if (prisoner == null)
                return;

            // If a marshal is captured, remove them temporarily
            foreach (var kvp in _kingdomMarshals.ToList())
            {
                if (kvp.Value == prisoner)
                {
                    Kingdom kingdom = kvp.Key;

                    // Don't remove completely, just notify tracking to remove them from map
                    MarshalTrackingExtensions.OnMarshalChanged(kingdom, prisoner, null);

                    if (kingdom == Hero.MainHero?.Clan?.Kingdom)
                    {
                        MBInformationManager.AddQuickInformation(
                            new TextObject($"Marshal {prisoner.Name} has been captured!"),
                            0,
                            prisoner.CharacterObject,
                            "event:/ui/notification/relation"
                        );
                    }
                }
            }
        }

        private void OnHeroPrisonerReleased(Hero releasedHero, PartyBase capturerParty, IFaction faction, EndCaptivityDetail detail)
        {
            if (releasedHero == null)
                return;

            // If a marshal is released and still assigned, add them back to tracking
            foreach (var kvp in _kingdomMarshals)
            {
                if (kvp.Value == releasedHero && kvp.Key == Hero.MainHero?.Clan?.Kingdom)
                {
                    // Re-add to tracking if they're still the marshal
                    MarshalTrackingExtensions.OnMarshalChanged(kvp.Key, null, releasedHero);

                    MBInformationManager.AddQuickInformation(
                        new TextObject($"Marshal {releasedHero.Name} has been released!"),
                        0,
                        releasedHero.CharacterObject,
                        "event:/ui/notification/relation"
                    );
                }
            }
        }

        private void OnArmyCreated(Army army)
        {
            if (army?.LeaderParty?.LeaderHero == null || army.Kingdom == null)
                return;

            // Only notify if player is in a kingdom
            if (Hero.MainHero?.Clan?.Kingdom == null)
                return;

            // Only notify if this army is from the player's kingdom
            if (army.Kingdom != Hero.MainHero.Clan.Kingdom)
                return;

            Hero armyLeader = army.LeaderParty.LeaderHero;
            Hero marshal = GetMarshal(army.Kingdom);

            // Check if the army leader is the current marshal
            if (marshal != null && armyLeader == marshal)
            {
                // Notify player if they are the marshal calling the army
                if (marshal == Hero.MainHero)
                {
                    MBInformationManager.AddQuickInformation(
                        new TextObject($"You have called an army for {army.Kingdom.Name}!"),
                        0,
                        Hero.MainHero.CharacterObject,
                        "event:/ui/notification/army_created"
                    );
                }
                else
                {
                    // Notify player about their kingdom's marshal calling an army
                    string marshalTitle = GetMarshalTitle(marshal);
                    MBInformationManager.AddQuickInformation(
                        new TextObject($"{marshalTitle} {marshal.Name} has called an army for {army.Kingdom.Name}!"),
                        0,
                        marshal.CharacterObject,
                        "event:/ui/notification/army_created"
                    );
                }
            }
        }

        private string GetMarshalTitle(Hero marshal)
        {
            if (marshal?.Clan?.Kingdom == null)
                return "Marshal";

            // Return appropriate title based on culture or just use "Marshal"
            return "Marshal";
        }

        private void OnDailyTick()
        {
            var allKingdoms = Kingdom.All;
            if (allKingdoms == null)
                return;

            foreach (var kingdom in allKingdoms)
            {
                if (kingdom == null || kingdom.IsEliminated || kingdom.IsMinorFaction || kingdom.IsRebelClan || kingdom.RulingClan == null || kingdom.Leader == null)
                    continue;

                if (!kingdom.Leader.IsAlive)
                    continue;

                // Only rulers can assign marshals
                if (!kingdom.Leader.IsKingdomLeader)
                    continue;

                Hero currentMarshal = GetMarshal(kingdom);
                _marshalAssignmentTime.TryGetValue(kingdom, out var assignedTime);
                bool expired = currentMarshal == null || assignedTime == null || assignedTime.ElapsedDaysUntilNow >= MarshalDurationDays;

                // Also check if current marshal is dead, captured, or left kingdom
                if (currentMarshal != null && (
                    !currentMarshal.IsAlive ||
                    currentMarshal.IsPrisoner ||
                    currentMarshal.Clan?.Kingdom != kingdom))
                {
                    expired = true;
                }

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

        private string BuildMarshalAppointmentMessage(Kingdom kingdom, Hero newMarshal)
        {
            if (kingdom == null || newMarshal == null) return string.Empty;

            string skill = GetMarshalSkillDescription(newMarshal);
            string reason = GetMarshalAppointmentReason(kingdom, newMarshal);

            // ✅ Single, easy-to-tweak line controls the whole announcement
            return $"{kingdom.Name} has entrusted the marshal's baton to {newMarshal.Name}. {skill}. {reason}.";
        }

        private string GetMarshalSkillDescription(Hero marshal)
        {
            int leadership = marshal.GetSkillValue(DefaultSkills.Leadership);
            int tactics = marshal.GetSkillValue(DefaultSkills.Tactics);
            float trueSkill = (leadership + tactics) / 2f;

            if (trueSkill >= 300)
                return "Veterans whisper their name in awe—few commanders rival such genius";
            if (trueSkill >= 250)
                return "Seasoned captains study their stratagems in hopes of imitation";
            if (trueSkill >= 200)
                return "Their orders carry the calm certainty of many victorious campaigns";
            if (trueSkill >= 150)
                return "Battle-tested and steady, they have risen to every challenge";
            if (trueSkill >= 100)
                return "Competent in the arts of war and diligent in their duties";
            if (trueSkill >= 50)
                return "Green in command but eager to prove their worth";

            return "Their grasp of tactics is tentative, and some question this choice";
        }

        private string GetMarshalAppointmentReason(Kingdom kingdom, Hero marshal)
        {
            if (marshal == null || kingdom?.Leader == null)
                return "No other lord stepped forward to shoulder the burden";

            float relation = kingdom.Leader.GetRelation(marshal);

            if (marshal == kingdom.Leader)
                return "No hands but the sovereign's could bear such weight";
            if (relation > 80)
                return "Their loyalty to the throne is beyond reproach";
            if (relation > 60)
                return "Long-standing service has earned the ruler's deep trust";
            if (relation > 30)
                return "Good standing at court favored their elevation";
            if (marshal.Clan == kingdom.RulingClan)
                return "Blood ties to the ruling clan all but sealed the appointment";

            return "Their blend of courage, repute, and readiness outshone all rivals";
        }
    }

    [HarmonyPatch(typeof(DefaultArmyManagementCalculationModel), "CalculatePartyInfluenceCost")]
    public class MarshalPartyInfluenceDiscountPatch
    {
        public static void Postfix(MobileParty armyLeaderParty, MobileParty party, ref int __result)
        {
            if (armyLeaderParty?.LeaderHero == null || armyLeaderParty.LeaderHero.Clan?.Kingdom == null || MarshalManager.Instance == null)
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
        public static void Postfix(Army army, ref ExplainedNumber __result, bool includeDescriptions = false)
        {
            if (MarshalManager.Instance == null || MarshalManager.Instance.GetMarshal(army.Kingdom) == null || army.LeaderParty.LeaderHero != MarshalManager.Instance.GetMarshal(army.Kingdom)) return;
            __result.AddFactor(-0.5f, new TextObject("Marshal Cohesion Bonus"));
        }
    }
}