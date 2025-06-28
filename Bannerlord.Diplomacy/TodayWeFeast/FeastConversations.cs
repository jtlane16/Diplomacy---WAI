using System;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace TodayWeFeast
{
    public class FeastConversations
    {
        public static void AddFeastDialogs(CampaignGameStarter campaignGameStarter)
        {
            // Host greeting when player talks to them at their feast
            campaignGameStarter.AddDialogLine(
                "feast_host_greeting",
                "start",
                "feast_host_response",
                "{=feast_host_greeting}{FEAST_HOST_MESSAGE}", // Changed format
                () => IsPlayerTalkingToFeastHost(),
                () =>
                {
                    HandleFeastHostConversation();
                },
                150, // Very high priority
                null);

            // Add a player response that closes the dialog
            campaignGameStarter.AddDialogLine(
                "feast_host_response",
                "feast_host_response",
                "close_window",
                "{=feast_host_player_response}Thank you for your hospitality.",
                () => true,
                null,
                150,
                null);

            // Guest greeting when player (host) talks to them at player's feast
            campaignGameStarter.AddDialogLine(
                "feast_guest_greeting",
                "start",
                "close_window",
                "{FEAST_GUEST_MESSAGE}",
                () => IsPlayerHostTalkingToGuest(),
                () => HandleFeastGuestConversation(),
                150, // Very high priority
                null);
        }

        private static bool IsPlayerTalkingToFeastHost()
        {
            try
            {
                // Check if we're in a feast location
                var settlement = PlayerEncounter.LocationEncounter?.Settlement;
                if (settlement == null) return false;

                // Find if there's a feast at this settlement
                var currentFeast = FeastBehavior.Instance?.Feasts?.FirstOrDefault(f => f.feastSettlement == settlement);
                if (currentFeast == null) return false;

                // Check if we're talking to the host
                var conversationHero = Hero.OneToOneConversationHero;
                if (conversationHero == null || conversationHero != currentFeast.hostOfFeast) return false;

                // Player must be a guest (not the host)
                if (currentFeast.hostOfFeast == Hero.MainHero) return false;

                // Player must be invited
                if (!currentFeast.lordsInFeast.Contains(Hero.MainHero)) return false;

                // Only once per day
                if (FeastBehavior.Instance._talkedToLordsToday.Contains(conversationHero)) return false;

                // Add debug output to help diagnose
                if (Hero.OneToOneConversationHero != null)
                {
                    InformationManager.DisplayMessage(new InformationMessage($"[FEAST DIALOG] Checking dialog for {Hero.OneToOneConversationHero.Name}", Colors.Magenta));
                }

                return true;
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage($"[FEAST ERROR] {ex.Message}", Colors.Red));
                return false;
            }
        }

        private static bool IsPlayerHostTalkingToGuest()
        {
            try
            {
                // Check if we're in a feast location
                var settlement = PlayerEncounter.LocationEncounter?.Settlement;
                if (settlement == null) return false;

                // Find if there's a feast at this settlement
                var currentFeast = FeastBehavior.Instance?.Feasts?.FirstOrDefault(f => f.feastSettlement == settlement);
                if (currentFeast == null) return false;

                // Player must be the host
                if (currentFeast.hostOfFeast != Hero.MainHero) return false;

                // Check if we're talking to a guest
                var conversationHero = Hero.OneToOneConversationHero;
                if (conversationHero == null || conversationHero == Hero.MainHero) return false;

                // Must be invited to the feast
                if (!currentFeast.lordsInFeast.Contains(conversationHero)) return false;

                // Only once per day
                if (FeastBehavior.Instance._talkedToLordsToday.Contains(conversationHero)) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void HandleFeastHostConversation()
        {
            var host = Hero.OneToOneConversationHero;
            string message = GenerateHostMessage(host);

            // THIS IS THE KEY PART - set the variable BEFORE the dialog shows
            MBTextManager.SetTextVariable("FEAST_HOST_MESSAGE", message);

            // Check if we can give relation bonus (3-day cooldown)
            if (FeastBehavior.Instance.CanTalkToLordForRelation(host))
            {
                // Apply bonuses - CHANGED from +1 to +2
                ChangeRelationAction.ApplyPlayerRelation(host, 2, true, true);
                Hero.MainHero.Clan.AddRenown(1, true);
                FeastBehavior.Instance._lastTalkedToLords[host] = CampaignTime.Now; // Update last talked time

                InformationManager.DisplayMessage(new InformationMessage($"Your conversation with {host.Name} strengthens your relationship! (+2 Relation, +1 Renown)", Colors.Green));
            }
            else
            {
                // Just renown, no relation bonus
                Hero.MainHero.Clan.AddRenown(1, true);
                InformationManager.DisplayMessage(new InformationMessage($"You enjoy the feast with {host.Name}. (+1 Renown)", Colors.Yellow));
            }

            FeastBehavior.Instance._talkedToLordsToday.Add(host); // Still mark as talked to today to prevent multiple conversations
        }

        private static void HandleFeastGuestConversation()
        {
            var guest = Hero.OneToOneConversationHero;
            var message = GenerateGuestMessage(guest);

            MBTextManager.SetTextVariable("FEAST_GUEST_MESSAGE", message);

            // Check if we can give relation bonus (3-day cooldown)
            if (FeastBehavior.Instance.CanTalkToLordForRelation(guest))
            {
                // Apply bonuses - CHANGED from +1 to +2
                ChangeRelationAction.ApplyPlayerRelation(guest, 2, true, true);
                Hero.MainHero.Clan.AddRenown(1, true);
                FeastBehavior.Instance._lastTalkedToLords[guest] = CampaignTime.Now; // Update last talked time

                InformationManager.DisplayMessage(new InformationMessage($"{guest.Name} enjoys your feast! (+2 Relation, +1 Renown)", Colors.Green));
            }
            else
            {
                // Just renown, no relation bonus
                Hero.MainHero.Clan.AddRenown(1, true);
                InformationManager.DisplayMessage(new InformationMessage($"{guest.Name} enjoys your feast! (+1 Renown)", Colors.Yellow));
            }

            // Mark as talked to
            FeastBehavior.Instance._talkedToLordsToday.Add(guest);
        }

        private static string GenerateHostMessage(Hero host)
        {
            var relation = host.GetRelation(Hero.MainHero);

            if (relation >= 20)
            {
                return $"Ah, {Hero.MainHero.Name}! Welcome to my feast! Your presence brings me great joy. Come, let us celebrate together!";
            }
            else if (relation >= 0)
            {
                return $"Welcome to my feast, {Hero.MainHero.Name}. I am pleased you could attend this celebration.";
            }
            else
            {
                return $"I... acknowledge your presence at my feast, {Hero.MainHero.Name}. Perhaps this gathering can bring us closer.";
            }
        }

        private static string GenerateGuestMessage(Hero guest)
        {
            var relation = guest.GetRelation(Hero.MainHero);

            if (relation >= 20)
            {
                return $"My dear {Hero.MainHero.Name}! What a magnificent feast you have prepared! Your generosity knows no bounds!";
            }
            else if (relation >= 0)
            {
                return $"Thank you for your hospitality, {Hero.MainHero.Name}. This is a well-organized celebration.";
            }
            else
            {
                return $"I... appreciate your invitation, {Hero.MainHero.Name}. Your hospitality is noted.";
            }
        }
    }
}