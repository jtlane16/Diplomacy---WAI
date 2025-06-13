using Diplomacy.Costs;

using Microsoft.Extensions.Logging;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

using WarAndAiTweaks;

namespace Diplomacy.DiplomaticAction.WarPeace.Conditions
{
    class HasEnoughInfluenceForWarCondition : AbstractCostCondition
    {
        private static readonly ILogger _logger = LogFactory.Get<HasEnoughInfluenceForWarCondition>();
        protected override TextObject FailedConditionText => new(StringConstants.NotEnoughInfluence);

        protected override bool ApplyConditionInternal(Kingdom kingdom, Kingdom otherKingdom, ref TextObject? textObject, bool forcePlayerCharacterCosts = false)
        {
            // NEW LOGGING: Let's see what the inputs are every time this is called.
            _logger.LogInformation($"[DEBUG] Checking influence for {kingdom.Name}. Is Leader Player: {kingdom.Leader.IsHumanPlayerCharacter}. forcePlayerCharacterCosts: {forcePlayerCharacterCosts}");

            if (kingdom.Leader.IsHumanPlayerCharacter || forcePlayerCharacterCosts)
            {
                var influenceCost = DiplomacyCostCalculator.DetermineCostForDeclaringWar(kingdom, forcePlayerCharacterCosts);
                var hasEnoughInfluence = influenceCost.CanPayCost();
                if (!hasEnoughInfluence)
                {
                    textObject = FailedConditionText;
                    _logger.LogInformation($"[DEBUG] {kingdom.Name} FAILED influence check. Cost: {influenceCost.Value}, Has: {kingdom.Leader.Clan.Influence}");
                }
                return hasEnoughInfluence;
            }

            _logger.LogInformation($"[DEBUG] {kingdom.Name} is AI, skipping influence check.");
            return true;
        }
    }
}