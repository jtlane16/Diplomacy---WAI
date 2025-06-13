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
                ConsiderBreakingNonAggressionPacts(clan.Kingdom);
            }
        }

        private void ConsiderNonAggressionPact(Kingdom proposingKingdom)
        {
            var inverseNormalizedValorLevel = 1 - proposingKingdom.Leader.GetNormalizedTraitValue(DefaultTraits.Valor);

            if (MBRandom.RandomFloat < BasePactChance * inverseNormalizedValorLevel)
            {
                var proposedKingdom = KingdomExtensions.AllActiveKingdoms
                    .Except(new[] { proposingKingdom })
                    .Where(kingdom => NonAggressionPactConditions.Instance.CanApply(proposingKingdom, kingdom))
                    .Where(kingdom => new WarAndAiTweaks.NonAggressionPactScoringModel().ShouldTakeActionBidirectional(proposingKingdom, kingdom))
                    .OrderByDescending(kingdom => kingdom.GetExpansionism()).FirstOrDefault();

                if (proposedKingdom is not null)
                {
                    LogFactory.Get<DiplomaticAgreementBehavior>()
                        .LogTrace($"[{CampaignTime.Now}] {proposingKingdom.Name} decided to form a NAP with {proposedKingdom.Name}.");
                    FormNonAggressionPactAction.Apply(proposingKingdom, proposedKingdom);
                }
            }
        }

        private void ConsiderBreakingNonAggressionPacts(Kingdom kingdom)
        {
            var pacts = DiplomaticAgreementManager.GetPacts(kingdom);
            foreach (var pact in pacts)
            {
                var otherKingdom = pact.GetOtherKingdom(kingdom);
                if (MBRandom.RandomFloat < 0.03f // A bit less likely to break pacts than alliances
                    && new WarAndAiTweaks.BreakNonAggressionPactScoringModel().ShouldTakeAction(kingdom, otherKingdom))
                {
                    pact.Expire();
                }
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