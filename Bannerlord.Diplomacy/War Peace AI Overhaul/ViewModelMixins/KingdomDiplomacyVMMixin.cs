using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;

using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Library;
using TaleWorlds.Localization;

using WarAndAiTweaks;

namespace Diplomacy.ViewModelMixin
{
    [ViewModelMixin(nameof(KingdomDiplomacyVM.RefreshValues))]
    internal sealed class KingdomDiplomacyVMMixin : BaseViewModelMixin<KingdomDiplomacyVM>
    {
        [DataSourceProperty]
        public MBBindingList<KingdomTruceItemVM> PlayerAlliances { get; }
        [DataSourceProperty]
        public MBBindingList<KingdomTruceItemVM> PlayerNAPs { get; }
        [DataSourceProperty]
        public string PlayerAlliancesText { get; }
        [DataSourceProperty]
        public string PlayerNAPsText { get; }

        public KingdomDiplomacyVMMixin(KingdomDiplomacyVM vm) : base(vm)
        {
            this.PlayerAlliances = new MBBindingList<KingdomTruceItemVM>();
            this.PlayerNAPs = new MBBindingList<KingdomTruceItemVM>();
            this.PlayerAlliancesText = new TextObject("{=zpNalMeA}Alliances").ToString();
            this.PlayerNAPsText = new TextObject("{=noWHMN1W}Non-Aggression Pacts").ToString();
        }

        public override void OnRefresh()
        {
            if (ViewModel?.PlayerTruces == null) return;

            PlayerAlliances.Clear();
            PlayerNAPs.Clear();

            var alliances = ViewModel.PlayerTruces.Where(t => t.Faction1.GetStanceWith(t.Faction2).IsAllied).ToList();
            var naps = ViewModel.PlayerTruces.Where(t => !alliances.Contains(t) && DiplomaticAgreementManager.HasNonAggressionPact((Kingdom) t.Faction1, (Kingdom) t.Faction2, out _)).ToList();

            alliances.ForEach(a => { ViewModel.PlayerTruces.Remove(a); this.PlayerAlliances.Add(a); });
            naps.ForEach(n => { ViewModel.PlayerTruces.Remove(n); this.PlayerNAPs.Add(n); });
        }
    }
}