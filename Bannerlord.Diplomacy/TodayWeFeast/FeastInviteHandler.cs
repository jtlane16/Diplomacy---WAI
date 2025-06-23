using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace TodayWeFeast
{
	internal class FeastInviteHandler
	{
		public static bool checkIfLordWantsToJoin(Hero hostOfFeast, Hero lordBeingInvited)
        {
            //Terrible Relations
            if (hostOfFeast.GetRelation(lordBeingInvited) <= -30)
            {
                return false;
            }
            //Great Relations
            else if (hostOfFeast.GetRelation(lordBeingInvited) >= 50) {
                 return true;
            }
            //This is the grey area.
            else
            {
                //Whatever the number is, level set it
                float numToWorkWith = (float)hostOfFeast.GetRelation(lordBeingInvited) + 29f;
                float probability = (numToWorkWith / 78f) * 100f;
                int randomGen = MBRandom.RandomInt(0, 100);
                if ((float)randomGen <= probability)
                {
                    return true;
                } else
                {
                     return false;
                }
            }
        }
	}

}

