using TaleWorlds.CampaignSystem;

namespace WarAndAiTweaks.DiplomaticAction // Changed from Diplomacy...
{
    public static class FormNonAggressionPactAction
    {
        public static void Apply(Kingdom kingdom1, Kingdom kingdom2, string reason)
        {
            DiplomaticAgreementManager.FormNonAggressionPact(kingdom1, kingdom2, reason);
        }
    }
}