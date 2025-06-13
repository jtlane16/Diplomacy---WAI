using Diplomacy.DiplomaticAction;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Diplomacy.Actions
{
    public class BreakNonAggressionPactAction : AbstractDiplomaticAction<BreakNonAggressionPactAction>
    {
        public override bool PassesConditions(Kingdom proposingKingdom, Kingdom otherKingdom, bool forcePlayerCharacterCosts = false, bool bypassCosts = false)
        {
            // For now, we assume the scoring model handles the decision.
            // We can add more conditions here if needed (e.g., a cooldown).
            return true;
        }

        protected override void ApplyInternal(Kingdom proposingKingdom, Kingdom otherKingdom, float? customDurationInDays)
        {
            if (DiplomaticAgreementManager.HasNonAggressionPact(proposingKingdom, otherKingdom, out var pact))
            {
                pact!.Expire();

                // Apply more significant relation penalties
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(proposingKingdom.Leader, otherKingdom.Leader, -25);

                var textObject = new TextObject("{=PactBroken}{PROPOSING_KINGDOM} has broken their non-aggression pact with {OTHER_KINGDOM}.")
                    .SetTextVariable("PROPOSING_KINGDOM", proposingKingdom.Name)
                    .SetTextVariable("OTHER_KINGDOM", otherKingdom.Name);

                InformationManager.DisplayMessage(new InformationMessage(textObject.ToString(), Colors.Red));
            }
        }

        protected override void AssessCosts(Kingdom proposingKingdom, Kingdom otherKingdom, bool forcePlayerCharacterCosts)
        {
            // No direct costs, but there could be indirect ones (e.g., reputation loss)
        }

        protected override void ShowPlayerInquiry(Kingdom proposingKingdom, System.Action acceptAction)
        {
            // This action is initiated by the AI, so no player inquiry is needed.
        }
    }
}
