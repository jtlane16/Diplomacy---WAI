using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

using WarAndAiTweaks.Strategic.Scoring;

namespace WarAndAiTweaks.Strategic.Diplomacy
{
    // NEW: Simple peace offer record instead of nested dictionary
    public class PeaceOfferRecord
    {
        [SaveableField(1)]
        public Kingdom Proposer;

        [SaveableField(2)]
        public Kingdom Target;

        [SaveableField(3)]
        public CampaignTime OfferTime;

        public PeaceOfferRecord() { }

        public PeaceOfferRecord(Kingdom proposer, Kingdom target, CampaignTime offerTime)
        { 
            Proposer = proposer;
            Target = target;
            OfferTime = offerTime;
        }
    }

    public class PeaceNegotiationManager
    {
        // REPLACED: Nested dictionaries with simple lists
        private List<PeaceProposal> _activePeaceProposals = new List<PeaceProposal>();
        private List<PeaceOfferRecord> _peaceOfferHistory = new List<PeaceOfferRecord>();
        private PeaceScorer _peaceScorer;

        public PeaceNegotiationManager(PeaceScorer peaceScorer)
        {
            _peaceScorer = peaceScorer;
        }

        public void ProcessPeaceProposals()
        {
            var expiredProposals = _activePeaceProposals
                .Where(p => p.ProposalTime.ElapsedDaysUntilNow > 7f)
                .ToList();

            foreach (var proposal in expiredProposals)
            {
                _activePeaceProposals.Remove(proposal);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[Peace Negotiation] Peace proposal between {proposal.Proposer.Name} and {proposal.Target.Name} has expired",
                    Colors.Gray));
            }
        }

        public void CheckForPendingPeaceProposals(Kingdom kingdom, ConquestStrategy strategy)
        {
            var proposalsForKingdom = _activePeaceProposals
                .Where(p => p.Target == kingdom)
                .ToList();

            foreach (var proposal in proposalsForKingdom)
            {
                float acceptancePriority = _peaceScorer.CalculatePeacePriority(kingdom, proposal.Proposer, strategy);
                float acceptanceThreshold = _peaceScorer.CalculatePeaceThreshold(kingdom) - 15f;

                if (acceptancePriority > acceptanceThreshold)
                {
                    AcceptPeaceProposal(kingdom, proposal.Proposer, proposal);
                }
                else if (proposal.ProposalTime.ElapsedDaysUntilNow > 3f)
                {
                    RejectPeaceProposal(kingdom, proposal.Proposer, proposal);
                }
            }
        }

        public void InitiatePeaceProposal(Kingdom proposer, Kingdom target, int tributeAmount)
        {
            // FIXED: Check if target kingdom ruler is the player, not just any player faction member
            if (target.RulingClan?.Leader == Hero.MainHero)
            {
                InitiatePlayerPeaceProposal(proposer, target, tributeAmount);
                return;
            }

            var proposal = new PeaceProposal
            {
                Proposer = proposer,
                Target = target,
                TributeAmount = tributeAmount,
                ProposalTime = CampaignTime.Now,
                IsPlayerInvolved = false
            };

            _activePeaceProposals.Add(proposal);
            RecordPeaceOffer(proposer, target);

            InformationManager.DisplayMessage(new InformationMessage(
                $"[Peace Negotiation] {proposer.Name} proposes peace to {target.Name}" +
                (tributeAmount != 0 ? $" (Tribute: {Math.Abs(tributeAmount)} denars {(tributeAmount > 0 ? "paid by" : "paid to")} {proposer.Name})" : ""),
                Colors.Cyan));
        }

        private void InitiatePlayerPeaceProposal(Kingdom proposer, Kingdom playerKingdom, int tributeAmount)
        {
            var proposal = new PeaceProposal
            {
                Proposer = proposer,
                Target = playerKingdom,
                TributeAmount = tributeAmount,
                ProposalTime = CampaignTime.Now,
                IsPlayerInvolved = true
            };

            _activePeaceProposals.Add(proposal);
            RecordPeaceOffer(proposer, playerKingdom);

            ShowPlayerPeaceProposalNotification(proposal);
        }

        private void ShowPlayerPeaceProposalNotification(PeaceProposal proposal)
        {
            string tributeText = "";
            if (proposal.TributeAmount > 0)
            {
                tributeText = $" and pay {proposal.TributeAmount} denars as tribute";
            }
            else if (proposal.TributeAmount < 0)
            {
                tributeText = $" and receive {Math.Abs(proposal.TributeAmount)} denars as tribute";
            }

            var titleText = new TextObject("Peace Proposal");
            var descriptionText = new TextObject($"{proposal.Proposer.Name} offers to make peace{tributeText}. Do you accept?");

            InformationManager.ShowInquiry(
                new InquiryData(
                    titleText.ToString(),
                    descriptionText.ToString(),
                    true, true,
                    new TextObject("Accept").ToString(),
                    new TextObject("Reject").ToString(),
                    () => OnPlayerAcceptPeace(proposal),
                    () => OnPlayerRejectPeace(proposal),
                    ""
                ), true);

            InformationManager.DisplayMessage(new InformationMessage(
                $"[Peace Proposal] {proposal.Proposer.Name} has offered peace terms. Check your notifications.",
                Colors.Yellow));
        }

        private void OnPlayerAcceptPeace(PeaceProposal proposal)
        {
            AcceptPeaceProposal(proposal.Target, proposal.Proposer, proposal);
            InformationManager.DisplayMessage(new InformationMessage(
                $"[Peace Accepted] You have accepted peace with {proposal.Proposer.Name}",
                Colors.Green));
        }

        private void OnPlayerRejectPeace(PeaceProposal proposal)
        {
            RejectPeaceProposal(proposal.Target, proposal.Proposer, proposal);
            InformationManager.DisplayMessage(new InformationMessage(
                $"[Peace Rejected] You have rejected peace with {proposal.Proposer.Name}",
                Colors.Red));
        }

        private void AcceptPeaceProposal(Kingdom acceptor, Kingdom proposer, PeaceProposal proposal)
        {
            MakePeaceAction.Apply(proposer, acceptor);

            if (proposal.TributeAmount != 0)
            {
                var payer = proposal.TributeAmount > 0 ? proposer : acceptor;
                var receiver = proposal.TributeAmount > 0 ? acceptor : proposer;

                if (payer.Leader?.Gold >= Math.Abs(proposal.TributeAmount))
                {
                    GiveGoldAction.ApplyBetweenCharacters(payer.Leader, receiver.Leader, Math.Abs(proposal.TributeAmount), false);
                }
            }

            _activePeaceProposals.Remove(proposal);

            InformationManager.DisplayMessage(new InformationMessage(
                $"[Peace Agreement] {proposer.Name} and {acceptor.Name} have made peace" +
                (proposal.TributeAmount != 0 ? $" (Tribute: {Math.Abs(proposal.TributeAmount)} denars)" : ""),
                Colors.Green));
        }

        private void RejectPeaceProposal(Kingdom rejector, Kingdom proposer, PeaceProposal proposal)
        {
            _activePeaceProposals.Remove(proposal);
            InformationManager.DisplayMessage(new InformationMessage(
                $"[Peace Rejected] {rejector.Name} has rejected peace offer from {proposer.Name}",
                Colors.Red));
        }

        public bool HasActivePeaceProposal(Kingdom proposer, Kingdom target)
        {
            return _activePeaceProposals.Any(p =>
                (p.Proposer == proposer && p.Target == target) ||
                (p.Proposer == target && p.Target == proposer));
        }

        public bool HasRecentPeaceOffer(Kingdom proposer, Kingdom target)
        {
            return _peaceOfferHistory.Any(offer =>
                offer.Proposer == proposer &&
                offer.Target == target &&
                offer.OfferTime.ElapsedDaysUntilNow < 30f);
        }

        private void RecordPeaceOffer(Kingdom proposer, Kingdom target)
        {
            _peaceOfferHistory.Add(new PeaceOfferRecord(proposer, target, CampaignTime.Now));
        }

        public void OnWarDeclared(Kingdom k1, Kingdom k2)
        {
            _activePeaceProposals.RemoveAll(p =>
                (p.Proposer == k1 && p.Target == k2) ||
                (p.Proposer == k2 && p.Target == k1));
        }

        public void OnPeaceMade(Kingdom k1, Kingdom k2)
        {
            _activePeaceProposals.RemoveAll(p =>
                (p.Proposer == k1 && p.Target == k2) ||
                (p.Proposer == k2 && p.Target == k1));
        }

        public void SyncData(IDataStore dataStore)
        {
            // FIXED: Now saving simple lists instead of nested dictionaries
            dataStore.SyncData("_activePeaceProposals", ref _activePeaceProposals);
            dataStore.SyncData("_peaceOfferHistory", ref _peaceOfferHistory);
        }
    }
}