using Diplomacy.Extensions;

using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Library;

using WarAndAiTweaks;
using WarAndAiTweaks.DiplomaticAction;

namespace Diplomacy.ViewModel
{
    // Fully qualify the base class to prevent namespace collision
    public class DiplomacyPropertiesVM : TaleWorlds.Library.ViewModel
    {
        private readonly Kingdom _faction1;
        private readonly Kingdom _faction2;

        public MBBindingList<KingdomTruceItemVM> Faction1Allies { get; private set; }
        public MBBindingList<KingdomTruceItemVM> Faction2Allies { get; private set; }
        public MBBindingList<KingdomTruceItemVM> Faction1Pacts { get; private set; }
        public MBBindingList<KingdomTruceItemVM> Faction2Pacts { get; private set; }

        public DiplomacyPropertiesVM(Kingdom faction1, Kingdom faction2)
        {
            _faction1 = faction1;
            _faction2 = faction2;
            Faction1Allies = new MBBindingList<KingdomTruceItemVM>();
            Faction2Allies = new MBBindingList<KingdomTruceItemVM>();
            Faction1Pacts = new MBBindingList<KingdomTruceItemVM>();
            Faction2Pacts = new MBBindingList<KingdomTruceItemVM>();
        }

        public void UpdateDiplomacyProperties()
        {
            // Empty actions to satisfy the constructor
            System.Action<KingdomDiplomacyItemVM> onSelection = _ => { };
            System.Action<KingdomTruceItemVM> onAction = _ => { };

            // Allies
            Faction1Allies.Clear();
            _faction1.GetAlliedKingdoms().Where(k => k != _faction2).ToList()
                .ForEach(ally => Faction1Allies.Add(new KingdomTruceItemVM(ally, _faction1, onSelection, onAction)));

            Faction2Allies.Clear();
            _faction2.GetAlliedKingdoms().Where(k => k != _faction1).ToList()
                .ForEach(ally => Faction2Allies.Add(new KingdomTruceItemVM(ally, _faction2, onSelection, onAction)));

            // Non-Aggression Pacts
            Faction1Pacts.Clear();
            DiplomaticAgreementManager.GetPacts(_faction1).Where(p => p.GetOtherKingdom(_faction1) != _faction2).ToList()
                .ForEach(pact => Faction1Pacts.Add(new KingdomTruceItemVM(pact.GetOtherKingdom(_faction1), _faction1, onSelection, onAction)));

            Faction2Pacts.Clear();
            DiplomaticAgreementManager.GetPacts(_faction2).Where(p => p.GetOtherKingdom(_faction2) != _faction1).ToList()
                .ForEach(pact => Faction2Pacts.Add(new KingdomTruceItemVM(pact.GetOtherKingdom(_faction2), _faction2, onSelection, onAction)));
        }
    }
}