using System.Collections.Generic;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.SaveSystem;

using WarAndAiTweaks.Strategic;
using TodayWeFeast; // ADD THIS

public class WarAndAiTweaksSaveDefiner : SaveableTypeDefiner
{
    public WarAndAiTweaksSaveDefiner() : base(1852400000) { }

    protected override void DefineClassTypes()
    {
        // FIX: Register KingdomStrategy for save system
        AddClassDefinition(typeof(WarAndAiTweaks.WarPeaceAI.KingdomStrategy), 145324325);

        // ADD: Register FeastObject for save system
        AddClassDefinition(typeof(FeastObject), 145324326);
    }

    protected override void DefineContainerDefinitions()
    {
        // Basic containers
        ConstructContainerDefinition(typeof(List<Hero>));
        ConstructContainerDefinition(typeof(List<Kingdom>));
        ConstructContainerDefinition(typeof(List<Settlement>)); // ADD THIS

        // Basic dictionaries
        ConstructContainerDefinition(typeof(Dictionary<Kingdom, double>));
        ConstructContainerDefinition(typeof(Dictionary<Hero, CampaignTime>));
        ConstructContainerDefinition(typeof(Dictionary<Kingdom, float>));
        ConstructContainerDefinition(typeof(Dictionary<Kingdom, CampaignTime>));
        ConstructContainerDefinition(typeof(Dictionary<Kingdom, Hero>));
        ConstructContainerDefinition(typeof(Dictionary<string, float>));

        // Add missing container definitions that might be needed
        ConstructContainerDefinition(typeof(List<int>));
        ConstructContainerDefinition(typeof(Dictionary<Kingdom, List<int>>));

        // UPDATED: Simplified strategic objective containers
        ConstructContainerDefinition(typeof(List<Army>));
        ConstructContainerDefinition(typeof(Dictionary<IFaction, CampaignTime>));

        // Add missing containers for save system
        ConstructContainerDefinition(typeof(Dictionary<string, WarAndAiTweaks.WarPeaceAI.KingdomStrategy>));
        ConstructContainerDefinition(typeof(Dictionary<string, float>));

        // ADD: Feast system containers
        ConstructContainerDefinition(typeof(List<FeastObject>));
        ConstructContainerDefinition(typeof(Dictionary<string, CampaignTime>)); // For AI conversation history
    }
}