using System.Collections.Generic;

using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

using WarAndAiTweaks;
using WarAndAiTweaks.AI.Behaviors;
using WarAndAiTweaks.AI.Goals;
using WarAndAiTweaks.DiplomaticAction;

public class WarAndAiTweaksSaveDefiner : SaveableTypeDefiner
{
    public WarAndAiTweaksSaveDefiner() : base(1852400000) { }

    protected override void DefineClassTypes()
    {
        AddClassDefinition(typeof(StrategicAICampaignBehavior), 1);
        AddClassDefinition(typeof(DiplomaticAgreementManager), 2);
        AddClassDefinition(typeof(Alliance), 3);
        AddClassDefinition(typeof(NonAggressionPact), 4);
        AddClassDefinition(typeof(DiplomaticAgreement), 5);
    }

    protected override void DefineContainerDefinitions()
    {
        ConstructContainerDefinition(typeof(List<Alliance>));
        ConstructContainerDefinition(typeof(List<NonAggressionPact>));
        ConstructContainerDefinition(typeof(Dictionary<string, int>));
        ConstructContainerDefinition(typeof(Dictionary<string, StrategicState>));
        ConstructContainerDefinition(typeof(Dictionary<string, CampaignTime>));
    }

    protected override void DefineEnumTypes()
    {
        AddEnumDefinition(typeof(StrategicState), 10);
    }
}