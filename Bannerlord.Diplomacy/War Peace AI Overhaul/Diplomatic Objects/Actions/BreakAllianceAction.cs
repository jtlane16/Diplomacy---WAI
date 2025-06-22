using TaleWorlds.CampaignSystem;

namespace WarAndAiTweaks.DiplomaticAction
{
    public static class BreakAllianceAction
    {
        public static void Apply(Kingdom kingdom1, Kingdom kingdom2, string reason)
        {
            DiplomaticAgreementManager.BreakAlliance(kingdom1, kingdom2, reason);
        }
    }
}