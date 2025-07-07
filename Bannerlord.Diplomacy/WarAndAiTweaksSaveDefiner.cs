using System.Collections.Generic;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.SaveSystem;

using TodayWeFeast;
using WarAndAiTweaks.Strategic;
using WarAndAiTweaks.Strategic.Scoring;
using WarAndAiTweaks.Strategic.Diplomacy;

public class WarAndAiTweaksSaveDefiner : SaveableTypeDefiner
{
    public WarAndAiTweaksSaveDefiner() : base(1852400000) { }

    protected override void DefineClassTypes()
    {
        // Feast system classes
        AddClassDefinition(typeof(FeastObject), 2137782638);
        AddClassDefinition(typeof(FeastBehavior), 2137782639);

        // Strategic AI classes
        AddClassDefinition(typeof(RunawayThreatData), 2137782640);
        AddClassDefinition(typeof(PeaceProposal), 2137782641);
        AddClassDefinition(typeof(ConquestStrategy), 2137782642);

        // Simple record classes instead of nested dictionaries
        AddClassDefinition(typeof(WarRecord), 2137782643);
        AddClassDefinition(typeof(PeaceOfferRecord), 2137782644);

        // NEW: Simplified strategic objective class
        AddClassDefinition(typeof(TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors.ArmyCoordinationManager.StrategicObjective), 2137782648);
    }

    protected override void DefineContainerDefinitions()
    {
        // Basic containers
        ConstructContainerDefinition(typeof(List<Hero>));
        ConstructContainerDefinition(typeof(List<Kingdom>));
        ConstructContainerDefinition(typeof(List<FeastObject>));

        // Basic dictionaries
        ConstructContainerDefinition(typeof(Dictionary<Kingdom, double>));
        ConstructContainerDefinition(typeof(Dictionary<Hero, CampaignTime>));
        ConstructContainerDefinition(typeof(Dictionary<Kingdom, float>));
        ConstructContainerDefinition(typeof(Dictionary<Kingdom, CampaignTime>));

        // Strategic AI containers - simple lists instead of nested dictionaries
        ConstructContainerDefinition(typeof(Dictionary<Kingdom, RunawayThreatData>));
        ConstructContainerDefinition(typeof(Dictionary<Kingdom, ConquestStrategy>));
        ConstructContainerDefinition(typeof(List<PeaceProposal>));
        ConstructContainerDefinition(typeof(List<WarRecord>));
        ConstructContainerDefinition(typeof(List<PeaceOfferRecord>));

        // Add missing container definitions that might be needed
        ConstructContainerDefinition(typeof(List<int>));
        ConstructContainerDefinition(typeof(Dictionary<Kingdom, List<int>>));

        // UPDATED: Simplified strategic objective containers
        ConstructContainerDefinition(typeof(List<Army>));
        ConstructContainerDefinition(typeof(List<TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors.ArmyCoordinationManager.StrategicObjective>));
        ConstructContainerDefinition(typeof(Dictionary<IFaction, List<TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors.ArmyCoordinationManager.StrategicObjective>>));
        ConstructContainerDefinition(typeof(Dictionary<IFaction, CampaignTime>));
    }

    protected override void DefineEnumTypes()
    {
        // NEW: Simplified strategic objective enum
        AddEnumDefinition(typeof(TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors.ArmyCoordinationManager.ObjectiveType), 2137782649);
    }
}