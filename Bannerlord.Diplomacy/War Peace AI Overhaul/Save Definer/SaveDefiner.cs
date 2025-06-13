// File: SaveDefiner.cs
using System.Collections.Generic;
using TaleWorlds.SaveSystem;
using WarAndAiTweaks;

public class WarAndAiTweaksSaveDefiner : SaveableTypeDefiner
{
    // It's good practice to use a high, random-looking number for your mod's save base ID.
    public WarAndAiTweaksSaveDefiner() : base(1852400000) { }

    protected override void DefineClassTypes()
    {
        // FIX: Added all three of your CampaignBehaviors.
        AddClassDefinition(typeof(DiplomacyBehavior), 1);
    }

    protected override void DefineContainerDefinitions()
    {
        // FIX: Added the missing container types used in your behaviors.
        ConstructContainerDefinition(typeof(Dictionary<string, float>));
        ConstructContainerDefinition(typeof(Dictionary<string, int>));
        ConstructContainerDefinition(typeof(Dictionary<string, List<string>>));
        ConstructContainerDefinition(typeof(Dictionary<string, Dictionary<string, float>>));
    }
}