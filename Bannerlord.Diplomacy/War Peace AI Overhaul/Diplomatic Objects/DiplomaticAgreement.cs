using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace WarAndAiTweaks.DiplomaticAction // Changed from Diplomacy...
{
    public abstract class DiplomaticAgreement
    {
        [SaveableField(1)]
        public readonly CampaignTime StartDate;

        [SaveableField(2)]
        public readonly Kingdom Faction1;

        [SaveableField(3)]
        public readonly Kingdom Faction2;

        protected DiplomaticAgreement(Kingdom faction1, Kingdom faction2)
        {
            this.StartDate = CampaignTime.Now;
            this.Faction1 = faction1;
            this.Faction2 = faction2;
        }

        public Kingdom GetOtherKingdom(Kingdom kingdom)
        {
            if (kingdom == Faction1)
                return Faction2;
            else if (kingdom == Faction2)
                return Faction1;
            else
                return null;
        }
    }
}