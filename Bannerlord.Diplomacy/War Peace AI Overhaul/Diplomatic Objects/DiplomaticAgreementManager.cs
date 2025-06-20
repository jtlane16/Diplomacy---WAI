using System.Collections.Generic;
using System.Linq;
using WarAndAiTweaks.DiplomaticAction;
using TaleWorlds.CampaignSystem;
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

        // This property makes the alliance list accessible to other files.
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

        public static void FormNonAggressionPact(Kingdom kingdom1, Kingdom kingdom2)
        {
            if (!HasNonAggressionPact(kingdom1, kingdom2, out _))
            {
                Instance._nonAggressionPacts.Add(new NonAggressionPact(kingdom1, kingdom2));
            }
        }

        public static void DeclareAlliance(Kingdom kingdom1, Kingdom kingdom2)
        {
            if (Instance._alliances.All(a => (a.Faction1 != kingdom1 || a.Faction2 != kingdom2) && (a.Faction1 != kingdom2 || a.Faction2 != kingdom1)))
            {
                Instance._alliances.Add(new Alliance(kingdom1, kingdom2));
                if (kingdom1.IsAtWarWith(kingdom2))
                {
                    TaleWorlds.CampaignSystem.Actions.MakePeaceAction.Apply(kingdom1, kingdom2);
                }
            }
        }

        public static void BreakAlliance(Kingdom kingdom1, Kingdom kingdom2)
        {
            Instance._alliances.RemoveAll(a => (a.Faction1 == kingdom1 && a.Faction2 == kingdom2) || (a.Faction1 == kingdom2 && a.Faction2 == kingdom1));
        }
    }
}