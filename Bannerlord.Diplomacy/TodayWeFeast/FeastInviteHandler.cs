using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TodayWeFeast
{
    internal class FeastInviteHandler
    {
        public static bool checkIfLordWantsToJoin(Hero hostOfFeast, Hero lordBeingInvited)
        {
            // DEBUG: Log the invitation check
            var relation = hostOfFeast.GetRelation(lordBeingInvited);

            //Terrible Relations
            if (relation <= -30)
            {
                InformationManager.DisplayMessage(new InformationMessage($"Debug: {lordBeingInvited.Name} declined feast (relation {relation} <= -30)", Colors.Red));
                return false;
            }
            //Great Relations
            else if (relation >= 50)
            {
                InformationManager.DisplayMessage(new InformationMessage($"Debug: {lordBeingInvited.Name} accepted feast (relation {relation} >= 50)", Colors.Green));
                return true;
            }
            //This is the grey area.
            else
            {
                //Whatever the number is, level set it
                float numToWorkWith = (float) relation + 29f;
                float probability = (numToWorkWith / 78f) * 100f;
                int randomGen = MBRandom.RandomInt(0, 100);
                bool accepted = (float) randomGen <= probability;

                InformationManager.DisplayMessage(new InformationMessage($"Debug: {lordBeingInvited.Name} {(accepted ? "accepted" : "declined")} feast (relation {relation}, probability {probability:F1}%, roll {randomGen})", accepted ? Colors.Green : Colors.Red));

                return accepted;
            }
        }
    }
}