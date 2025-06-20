using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace WarAndAiTweaks.DiplomaticAction // Changed from Diplomacy...
{
    public class Alliance : DiplomaticAgreement
    {
        public Alliance(Kingdom faction1, Kingdom faction2) : base(faction1, faction2) { }
    }
}