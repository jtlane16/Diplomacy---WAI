using Diplomacy.DiplomaticAction;
using Diplomacy.DiplomaticAction.Alliance;
using Diplomacy.DiplomaticAction.NonAggressionPact;
using Diplomacy.Events;
using Diplomacy.Extensions;

using Microsoft.Extensions.Logging;

using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;

using WarAndAiTweaks.AI;

using static WarAndAiTweaks.AI.StrategicAI;

namespace Diplomacy.CampaignBehaviors
{
    internal sealed class DiplomaticAgreementBehavior : CampaignBehaviorBase
    {
        private const float BasePactChance = 0.05f;

        private DiplomaticAgreementManager _diplomaticAgreementManager;

        public DiplomaticAgreementBehavior()
        {
            _diplomaticAgreementManager = new();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, UpdateDiplomaticAgreements);
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, ConsiderDiplomaticAgreements);
            DiplomacyEvents.AllianceFormed.AddNonSerializedListener(this, ExpireNonAggressionPact);
        }

        private void ConsiderDiplomaticAgreements(Clan clan)
        {
            // only apply to kingdom leader clans
            if (clan.MapFaction.IsKingdomFaction && clan.MapFaction.Leader == clan.Leader && !clan.Leader.IsHumanPlayerCharacter)
            {
                ConsiderNonAggressionPact(clan.Kingdom);
            }
        }

        private void ConsiderNonAggressionPact(Kingdom proposingKingdom)
        {
            // 1) Re-use a single evaluator instance
            INonAggressionPactEvaluator napEvaluator = new WarAndAiTweaks.AI.NonAggressionPackedScoringModel();

            // 2) Pick the best candidate that *both* sides agree on (≥ 50 by default)
            Kingdom? proposedKingdom = KingdomExtensions.AllActiveKingdoms
            .Except(new[] { proposingKingdom })
            .Where(k => NonAggressionPactConditions.Instance.CanApply(proposingKingdom, k))
            .Where(k => napEvaluator.ShouldTakeActionBidirectional(proposingKingdom, k, threshold: 50f))
            .OrderByDescending(k => napEvaluator.GetPactScore(proposingKingdom, k).ResultNumber)
            .FirstOrDefault();

            if (proposedKingdom != null)
            {
                LogFactory.Get<DiplomaticAgreementBehavior>()
                    .LogTrace($"[{CampaignTime.Now}] {proposingKingdom.Name} proposed a NAP to {proposedKingdom.Name}.");

                // Diplomacy’s built-in action
                FormNonAggressionPactAction.Apply(proposingKingdom, proposedKingdom);
            }
        }

        private void UpdateDiplomaticAgreements()
        {
            DiplomaticAgreementManager.Instance!.Agreements.Values
            .SelectMany(x => x)
            .ToList()
            .ForEach(x => x.TryExpireNotification());
        }

        private void ExpireNonAggressionPact(AllianceEvent obj)
        {
            if (DiplomaticAgreementManager.HasNonAggressionPact(obj.Kingdom, obj.OtherKingdom, out var pactAgreement))
                pactAgreement!.Expire();
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_diplomaticAgreementManager", ref _diplomaticAgreementManager);

            if (dataStore.IsLoading)
            {
                _diplomaticAgreementManager ??= new();
                _diplomaticAgreementManager.Sync();
            }
        }
    }
}