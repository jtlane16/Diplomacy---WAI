using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using SandBox.ViewModelCollection.Map;
using WarAndAiTweaks.Strategic.Marshal;

namespace WarAndAiTweaks.Patches
{
    // Track marshal parties specifically to avoid removing caravans
    public static class MarshalPartyTracker
    {
        private static HashSet<MobileParty> _trackedMarshalParties = new HashSet<MobileParty>();

        public static void AddMarshalParty(MobileParty party)
        {
            if (party != null)
                _trackedMarshalParties.Add(party);
        }

        public static void RemoveMarshalParty(MobileParty party)
        {
            if (party != null)
                _trackedMarshalParties.Remove(party);
        }

        public static bool IsMarshalParty(MobileParty party)
        {
            return party != null && _trackedMarshalParties.Contains(party);
        }

        public static void ClearAll()
        {
            _trackedMarshalParties.Clear();
        }

        public static void SetMarshalTrackItemName(MobilePartyTrackItemVM trackItem, string name)
        {
            if (trackItem == null || string.IsNullOrEmpty(name))
                return;

            try
            {
                // Set the _nameBind field directly - this is what gets copied to Name property
                var nameBindField = AccessTools.Field(typeof(MobilePartyTrackItemVM), "_nameBind");
                if (nameBindField != null)
                {
                    nameBindField.SetValue(trackItem, name);
                }
            }
            catch (Exception ex)
            {
                // If reflection fails, log but don't crash
                InformationManager.DisplayMessage(new InformationMessage($"Failed to set marshal name: {ex.Message}"));
            }
        }
    }

    // Patch the InitList method to include marshal parties
    [HarmonyPatch(typeof(MapMobilePartyTrackerVM), "InitList")]
    public class MapMobilePartyTrackerVM_InitList_Patch
    {
        public static void Postfix(MapMobilePartyTrackerVM __instance)
        {
            try
            {
                AddMarshalPartyTracking(__instance);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage($"Error adding marshal tracking: {ex.Message}"));
            }
        }

        private static void AddMarshalPartyTracking(MapMobilePartyTrackerVM instance)
        {
            if (Hero.MainHero?.Clan?.Kingdom == null || MarshalManager.Instance == null)
                return;

            var playerKingdom = Hero.MainHero.Clan.Kingdom;
            var marshal = MarshalManager.Instance.GetMarshal(playerKingdom);

            if (marshal?.PartyBelongedTo != null && !marshal.PartyBelongedTo.IsMainParty)
            {
                var trackers = instance.Trackers;
                bool alreadyTracked = false;

                for (int i = 0; i < trackers.Count; i++)
                {
                    if (trackers[i].TrackedParty == marshal.PartyBelongedTo)
                    {
                        alreadyTracked = true;
                        break;
                    }
                }

                if (!alreadyTracked)
                {
                    var mapCameraField = AccessTools.Field(typeof(MapMobilePartyTrackerVM), "_mapCamera");
                    var fastMoveCameraField = AccessTools.Field(typeof(MapMobilePartyTrackerVM), "_fastMoveCameraToPosition");

                    var mapCamera = (Camera) mapCameraField.GetValue(instance);
                    var fastMoveCameraAction = (Action<Vec2>) fastMoveCameraField.GetValue(instance);

                    if (mapCamera != null && fastMoveCameraAction != null)
                    {
                        var trackItem = new MobilePartyTrackItemVM(marshal.PartyBelongedTo, mapCamera, fastMoveCameraAction);
                        trackers.Add(trackItem);
                        MarshalPartyTracker.AddMarshalParty(marshal.PartyBelongedTo);

                        // Force set the name after a delay to ensure it sticks
                        string marshalName = $"{playerKingdom.Name} Marshal";
                        MarshalPartyTracker.SetMarshalTrackItemName(trackItem, marshalName);
                    }
                }
            }
        }
    }

    // Patch the CanAddParty method to include marshal parties (but NOT armies or player party)
    [HarmonyPatch(typeof(MapMobilePartyTrackerVM), "CanAddParty")]
    public class MapMobilePartyTrackerVM_CanAddParty_Patch
    {
        public static void Postfix(ref bool __result, MobileParty party)
        {
            // Don't override if already trackable
            if (__result)
                return;

            // Only add marshal parties, never override army/caravan logic
            if (IsMarshalParty(party))
            {
                __result = true;
            }
        }

        private static bool IsMarshalParty(MobileParty party)
        {
            if (party?.LeaderHero == null || Hero.MainHero?.Clan?.Kingdom == null || MarshalManager.Instance == null)
                return false;

            // NEVER track the player's party, even if they're marshal
            if (party == MobileParty.MainParty || party.IsMainParty)
                return false;

            var playerKingdom = Hero.MainHero.Clan.Kingdom;
            var marshal = MarshalManager.Instance.GetMarshal(playerKingdom);

            return marshal != null && party.LeaderHero == marshal;
        }
    }

    // Patch the OnClanChangedKingdom method to refresh marshal tracking when kingdoms change
    [HarmonyPatch(typeof(MapMobilePartyTrackerVM), "OnClanChangedKingdom")]
    public class MapMobilePartyTrackerVM_OnClanChangedKingdom_Patch
    {
        public static void Postfix(MapMobilePartyTrackerVM __instance, Clan clan, Kingdom oldKingdom, Kingdom newKingdom)
        {
            if (clan == Clan.PlayerClan)
            {
                MarshalPartyTracker.ClearAll();
            }
        }
    }

    // Add a new patch to handle daily updates for marshal changes
    [HarmonyPatch(typeof(MapMobilePartyTrackerVM), "Update")]
    public class MapMobilePartyTrackerVM_Update_Patch
    {
        private static CampaignTime _lastMarshalCheck = CampaignTime.Zero;

        public static void Postfix(MapMobilePartyTrackerVM __instance)
        {
            if (CampaignTime.Now.ToHours - _lastMarshalCheck.ToHours >= 1.0)
            {
                _lastMarshalCheck = CampaignTime.Now;
                CheckMarshalTracking(__instance);
            }
        }

        private static void CheckMarshalTracking(MapMobilePartyTrackerVM instance)
        {
            if (Hero.MainHero?.Clan?.Kingdom == null || MarshalManager.Instance == null)
                return;

            var playerKingdom = Hero.MainHero.Clan.Kingdom;
            var currentMarshal = MarshalManager.Instance.GetMarshal(playerKingdom);

            if (currentMarshal?.PartyBelongedTo == null || currentMarshal.PartyBelongedTo == MobileParty.MainParty || currentMarshal.PartyBelongedTo.IsMainParty)
            {
                var trackers = instance.Trackers;
                for (int i = trackers.Count - 1; i >= 0; i--)
                {
                    if (MarshalPartyTracker.IsMarshalParty(trackers[i].TrackedParty))
                    {
                        MarshalPartyTracker.RemoveMarshalParty(trackers[i].TrackedParty);
                        trackers.RemoveAt(i);
                    }
                }
                return;
            }

            var trackers2 = instance.Trackers;
            bool marshalTracked = false;

            for (int i = 0; i < trackers2.Count; i++)
            {
                var trackedParty = trackers2[i].TrackedParty;
                if (trackedParty == currentMarshal.PartyBelongedTo)
                {
                    marshalTracked = true;
                    MarshalPartyTracker.AddMarshalParty(trackedParty);

                    // Continuously enforce the marshal name
                    string marshalName = $"Marshal {currentMarshal.Name}";
                    MarshalPartyTracker.SetMarshalTrackItemName(trackers2[i], marshalName);
                }
                else if (MarshalPartyTracker.IsMarshalParty(trackedParty))
                {
                    MarshalPartyTracker.RemoveMarshalParty(trackedParty);
                    trackers2.RemoveAt(i);
                    i--;
                }
            }

            if (!marshalTracked)
            {
                var mapCameraField = AccessTools.Field(typeof(MapMobilePartyTrackerVM), "_mapCamera");
                var fastMoveCameraField = AccessTools.Field(typeof(MapMobilePartyTrackerVM), "_fastMoveCameraToPosition");

                var mapCamera = (Camera) mapCameraField.GetValue(instance);
                var fastMoveCameraAction = (Action<Vec2>) fastMoveCameraField.GetValue(instance);

                if (mapCamera != null && fastMoveCameraAction != null)
                {
                    var trackItem = new MobilePartyTrackItemVM(currentMarshal.PartyBelongedTo, mapCamera, fastMoveCameraAction);
                    trackers2.Add(trackItem);
                    MarshalPartyTracker.AddMarshalParty(currentMarshal.PartyBelongedTo);

                    string marshalName = $"Marshal {currentMarshal.Name}";
                    MarshalPartyTracker.SetMarshalTrackItemName(trackItem, marshalName);
                }
            }
        }
    }

    // Extension to handle marshal assignment changes specifically
    public static class MarshalTrackingExtensions
    {
        private static MapMobilePartyTrackerVM _trackerInstance;

        public static void RegisterTrackerInstance(MapMobilePartyTrackerVM instance)
        {
            _trackerInstance = instance;
        }

        public static void OnMarshalChanged(Kingdom kingdom, Hero oldMarshal, Hero newMarshal)
        {
            if (_trackerInstance == null || kingdom != Hero.MainHero?.Clan?.Kingdom)
                return;

            var trackers = _trackerInstance.Trackers;

            if (oldMarshal?.PartyBelongedTo != null)
            {
                for (int i = trackers.Count - 1; i >= 0; i--)
                {
                    if (trackers[i].TrackedParty == oldMarshal.PartyBelongedTo)
                    {
                        MarshalPartyTracker.RemoveMarshalParty(oldMarshal.PartyBelongedTo);
                        trackers.RemoveAt(i);
                        break;
                    }
                }
            }

            // Only add new marshal if it's not the player's party
            if (newMarshal?.PartyBelongedTo != null &&
                newMarshal.PartyBelongedTo != MobileParty.MainParty &&
                !newMarshal.PartyBelongedTo.IsMainParty)
            {
                var mapCameraField = AccessTools.Field(typeof(MapMobilePartyTrackerVM), "_mapCamera");
                var fastMoveCameraField = AccessTools.Field(typeof(MapMobilePartyTrackerVM), "_fastMoveCameraToPosition");

                var mapCamera = (Camera) mapCameraField.GetValue(_trackerInstance);
                var fastMoveCameraAction = (Action<Vec2>) fastMoveCameraField.GetValue(_trackerInstance);

                if (mapCamera != null && fastMoveCameraAction != null)
                {
                    var trackItem = new MobilePartyTrackItemVM(newMarshal.PartyBelongedTo, mapCamera, fastMoveCameraAction);
                    trackers.Add(trackItem);
                    MarshalPartyTracker.AddMarshalParty(newMarshal.PartyBelongedTo);

                    string marshalName = $"Marshal {newMarshal.Name}";
                    MarshalPartyTracker.SetMarshalTrackItemName(trackItem, marshalName);
                }
            }
        }
    }

    // Patch constructor to register the instance
    [HarmonyPatch(typeof(MapMobilePartyTrackerVM), MethodType.Constructor, new Type[] { typeof(Camera), typeof(Action<Vec2>) })]
    public class MapMobilePartyTrackerVM_Constructor_Patch
    {
        public static void Postfix(MapMobilePartyTrackerVM __instance)
        {
            MarshalTrackingExtensions.RegisterTrackerInstance(__instance);
        }
    }

    // Patch UpdateProperties to override _nameBind for marshal parties
    [HarmonyPatch(typeof(MobilePartyTrackItemVM), "UpdateProperties")]
    public class MobilePartyTrackItemVM_UpdateProperties_Patch
    {
        public static void Postfix(MobilePartyTrackItemVM __instance)
        {
            try
            {
                // Check if this is a marshal party we're tracking
                if (__instance?.TrackedParty != null && MarshalPartyTracker.IsMarshalParty(__instance.TrackedParty))
                {
                    var hero = __instance.TrackedParty.LeaderHero;
                    if (hero != null)
                    {
                        string marshalName = $"Marshal {hero.Name}";

                        // Override the _nameBind field that was just set in UpdateProperties
                        var nameBindField = AccessTools.Field(typeof(MobilePartyTrackItemVM), "_nameBind");
                        if (nameBindField != null)
                        {
                            nameBindField.SetValue(__instance, marshalName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Fail silently to avoid breaking the UI
                InformationManager.DisplayMessage(new InformationMessage($"Marshal name patch error: {ex.Message}"));
            }
        }
    }
}