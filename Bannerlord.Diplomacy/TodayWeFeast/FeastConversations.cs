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
                "{=feast_host_greeting}{FEAST_HOST_MESSAGE}", // This line uses a variable
                () => IsPlayerTalkingToFeastHost(),
                () =>
                {
                    var host = Hero.OneToOneConversationHero;
                    string message = GenerateHostMessage(host);

                    // Debug the variable setting
                    InformationManager.DisplayMessage(new InformationMessage($"[FEAST DEBUG] Setting message: {message}", Colors.Magenta));

                    // THIS IS THE KEY PART - set the variable BEFORE the dialog shows
                    MBTextManager.SetTextVariable("FEAST_HOST_MESSAGE", message);

                    // Apply bonuses - moved to here instead of HandleFeastHostConversation
                    ChangeRelationAction.ApplyPlayerRelation(host, 4, true, true);
                    Hero.MainHero.Clan.AddRenown(2, true);
                    FeastBehavior.Instance._talkedToLordsToday.Add(host);
                },
                150,
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
                if (settlement == null) 
                {
                    InformationManager.DisplayMessage(new InformationMessage("[FEAST DEBUG] No settlement found", Colors.Red));
                    return false;
                }

                // Find if there's a feast at this settlement
                var currentFeast = FeastBehavior.Instance?.Feasts?.FirstOrDefault(f => f.feastSettlement == settlement);
                if (currentFeast == null) 
                {
                    InformationManager.DisplayMessage(new InformationMessage($"[FEAST DEBUG] No feast found at {settlement.Name}", Colors.Red));
                    return false;
                }

                // Check if we're talking to the host
                var conversationHero = Hero.OneToOneConversationHero;
                if (conversationHero == null) 
                {
                    InformationManager.DisplayMessage(new InformationMessage("[FEAST DEBUG] No conversation hero", Colors.Red));
                    return false;
                }

                if (conversationHero != currentFeast.hostOfFeast) 
                {
                    InformationManager.DisplayMessage(new InformationMessage($"[FEAST DEBUG] Not talking to host. Talking to: {conversationHero.Name}, Host is: {currentFeast.hostOfFeast.Name}", Colors.Red));
                    return false;
                }

                // Player must be a guest (not the host)
                if (currentFeast.hostOfFeast == Hero.MainHero) 
                {
                    InformationManager.DisplayMessage(new InformationMessage("[FEAST DEBUG] Player is the host", Colors.Red));
                    return false;
                }

                // Player must be invited
                if (!currentFeast.lordsInFeast.Contains(Hero.MainHero)) 
                {
                    InformationManager.DisplayMessage(new InformationMessage("[FEAST DEBUG] Player not invited to feast", Colors.Red));
                    return false;
                }

                // Only once per day
                if (FeastBehavior.Instance._talkedToLordsToday.Contains(conversationHero)) 
                {
                    InformationManager.DisplayMessage(new InformationMessage($"[FEAST DEBUG] Already talked to {conversationHero.Name} today", Colors.Red));
                    return false;
                }

                InformationManager.DisplayMessage(new InformationMessage($"[FEAST DEBUG] All conditions met for host dialog with {conversationHero.Name}", Colors.Green));
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

        private static void HandleFeastGuestConversation()
        {
            var guest = Hero.OneToOneConversationHero;
            var message = GenerateGuestMessage(guest);

            MBTextManager.SetTextVariable("FEAST_GUEST_MESSAGE", message);

            // Apply bonuses
            ChangeRelationAction.ApplyPlayerRelation(guest, 3, true, true);
            Hero.MainHero.Clan.AddRenown(1, true);

            // Mark as talked to
            FeastBehavior.Instance._talkedToLordsToday.Add(guest);

            InformationManager.DisplayMessage(new InformationMessage($"{guest.Name} enjoys your feast! (+3 Relation, +1 Renown)", Colors.Green));
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