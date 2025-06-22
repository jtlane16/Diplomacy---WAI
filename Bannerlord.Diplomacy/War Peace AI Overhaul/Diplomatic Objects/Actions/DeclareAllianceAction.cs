using TaleWorlds.CampaignSystem;

namespace WarAndAiTweaks.DiplomaticAction
{
    public static class DeclareAllianceAction
    {
        public static void Apply(Kingdom kingdom1, Kingdom kingdom2, string reason)
        {
            DiplomaticAgreementManager.DeclareAlliance(kingdom1, kingdom2, reason);
        }
    }
}