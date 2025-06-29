﻿using System.Collections.Generic;
using System.Linq;
using WarAndAiTweaks.DiplomaticAction;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;


namespace WarAndAiTweaks
{
    public class DiplomaticAgreementManager : CampaignBehaviorBase
    {
        [SaveableField(1)]
        private List<Alliance> _alliances;

        [SaveableField(2)]
        private List<NonAggressionPact> _nonAggressionPacts;

        public static DiplomaticAgreementManager Instance { get; private set; }

        public static IEnumerable<Alliance> Alliances
        {
            get
            {
                if (Instance == null)
                {
                    return Enumerable.Empty<Alliance>();
                }
                return Instance._alliances;
            }
        }

        public static IEnumerable<NonAggressionPact> NonAggressionPacts
        {
            get
            {
                if (Instance == null)
                {
                    return Enumerable.Empty<NonAggressionPact>();
                }
                return Instance._nonAggressionPacts;
            }
        }

        public DiplomaticAgreementManager()
        {
            this._alliances = new List<Alliance>();
            this._nonAggressionPacts = new List<NonAggressionPact>();
            Instance = this;
        }

        public override void RegisterEvents()
        {
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_alliances", ref _alliances);
            dataStore.SyncData("_nonAggressionPacts", ref _nonAggressionPacts);
            if (dataStore.IsLoading)
            {
                Instance = this;
                _alliances ??= new List<Alliance>();
                _nonAggressionPacts ??= new List<NonAggressionPact>();
            }
        }

        public static bool HasNonAggressionPact(Kingdom kingdom1, Kingdom kingdom2, out NonAggressionPact pact)
        {
            pact = Instance._nonAggressionPacts.FirstOrDefault(p =>
                (p.Faction1 == kingdom1 && p.Faction2 == kingdom2) || (p.Faction1 == kingdom2 && p.Faction2 == kingdom1));
            return pact != null;
        }

        public static IEnumerable<NonAggressionPact> GetPacts(Kingdom kingdom)
        {
            return Instance._nonAggressionPacts.Where(p => p.Faction1 == kingdom || p.Faction2 == kingdom);
        }

        public static void FormNonAggressionPact(Kingdom kingdom1, Kingdom kingdom2, string reason)
        {
            if (!HasNonAggressionPact(kingdom1, kingdom2, out _))
            {
                Instance._nonAggressionPacts.Add(new NonAggressionPact(kingdom1, kingdom2));
                InformationManager.DisplayMessage(new InformationMessage($"{kingdom1.Name} and {kingdom2.Name} have signed a non-aggression pact because {reason}.", Colors.Green));
            }
        }

        public static void BreakNonAggressionPact(Kingdom kingdom1, Kingdom kingdom2)
        {
            Instance._nonAggressionPacts.RemoveAll(p => (p.Faction1 == kingdom1 && p.Faction2 == kingdom2) || (p.Faction1 == kingdom2 && p.Faction2 == kingdom1));
        }

        public static void DeclareAlliance(Kingdom kingdom1, Kingdom kingdom2, string reason)
        {
            if (Instance._alliances.All(a => (a.Faction1 != kingdom1 || a.Faction2 != kingdom2) && (a.Faction1 != kingdom2 || a.Faction2 != kingdom1)))
            {
                Instance._alliances.Add(new Alliance(kingdom1, kingdom2));
                InformationManager.DisplayMessage(new InformationMessage($"{kingdom1.Name} and {kingdom2.Name} have formed an alliance because {reason}.", Colors.Green));
                if (kingdom1.IsAtWarWith(kingdom2))
                {
                    TaleWorlds.CampaignSystem.Actions.MakePeaceAction.Apply(kingdom1, kingdom2);
                }
            }
        }

        public static void BreakAlliance(Kingdom kingdom1, Kingdom kingdom2, string reason)
        {
            Instance._alliances.RemoveAll(a => (a.Faction1 == kingdom1 && a.Faction2 == kingdom2) || (a.Faction1 == kingdom2 && a.Faction2 == kingdom1));
            InformationManager.DisplayMessage(new InformationMessage($"{kingdom1.Name} has broken their alliance with {kingdom2.Name} because {reason}.", Colors.Red));
        }
    }
}