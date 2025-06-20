using Diplomacy.DiplomaticAction.GenericConditions;

using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Localization;

namespace Diplomacy.DiplomaticAction
{
    internal abstract class AbstractConditionEvaluator<T> where T : AbstractConditionEvaluator<T>, new()
    {
        private List<IDiplomacyCondition> Conditions { get; }

        public static T Instance { get; } = new();

        protected AbstractConditionEvaluator(List<IDiplomacyCondition> conditions)
        {
            Conditions = new List<IDiplomacyCondition> { new HasAuthorityCondition() };
            Conditions.AddRange(conditions);
        }

        public bool CanApply(Kingdom kingdom, Kingdom otherKingdom, bool forcePlayerCosts = false, bool bypassCosts = false)
        {
            return CanApply(kingdom, otherKingdom, out _, forcePlayerCosts, bypassCosts);
        }

        public bool CanApply(Kingdom kingdom, Kingdom otherKingdom, out TextObject? failedReason, bool forcePlayerCosts = false, bool bypassCosts = false)
        {
            failedReason = null;
            foreach (var condition in Conditions)
            {
                if (!condition.ApplyCondition(kingdom, otherKingdom, out failedReason, forcePlayerCosts, bypassCosts))
                {
                    return false;
                }
            }
            return true;
        }

        public List<TextObject> CanApplyExceptions(Kingdom kingdom, Kingdom otherKingdom, bool forcePlayerCosts = false, bool bypassCosts = false)
        {
            TextObject? selector(IDiplomacyCondition c)
            {
                c.ApplyCondition(kingdom, otherKingdom, out var txt, forcePlayerCosts, bypassCosts);
                return txt;
            }

            return Conditions.Select(selector).OfType<TextObject>().ToList();
        }

        public List<TextObject> CanApplyExceptions(KingdomDiplomacyItemVM item, bool forcePlayerCosts = true, bool bypassCosts = false)
        {
            TextObject? selector(IDiplomacyCondition c)
            {
                c.ApplyCondition((Kingdom) item.Faction1, (Kingdom) item.Faction2, out var txt, forcePlayerCosts, bypassCosts);
                return txt;
            }

            return Conditions.Select(selector).OfType<TextObject>().ToList();
        }
    }
}
