using Diplomacy.CivilWar;
using Diplomacy.CivilWar.Factions;
using Diplomacy.DiplomaticAction;
using Diplomacy.Messengers;
using Diplomacy.WarExhaustion;
using Diplomacy.WarExhaustion.EventRecords;

using JetBrains.Annotations;

using System.Collections.Generic;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

using WarAndAiTweaks.AI.Goals; // Added this using statement

using static Diplomacy.WarExhaustion.WarExhaustionRecord;

namespace Diplomacy
{
    [UsedImplicitly]
    internal class CustomSavedTypeDefiner : SaveableTypeDefiner
    {
        private const int SaveBaseId = 1_984_110_150;

        public CustomSavedTypeDefiner() : base(SaveBaseId) { }

        protected override void DefineClassTypes()
        {
            AddClassDefinition(typeof(Messenger), 1);
            AddClassDefinition(typeof(WarExhaustionManager), 2);
            AddClassDefinition(typeof(CooldownManager), 3);
            AddClassDefinition(typeof(MessengerManager), 4);
            AddClassDefinition(typeof(DiplomaticAgreement), 5);
            AddClassDefinition(typeof(NonAggressionPactAgreement), 6);
            AddClassDefinition(typeof(DiplomaticAgreementManager), 7);
            AddClassDefinition(typeof(ExpansionismManager), 8);
            AddClassDefinition(typeof(RebelFactionManager), 10);
            AddClassDefinition(typeof(RebelFaction), 11);
            AddClassDefinition(typeof(AbdicationFaction), 13);
            AddClassDefinition(typeof(SecessionFaction), 14);
            AddWarExhaustionEventRecordDefinitions();
        }

        private void AddWarExhaustionEventRecordDefinitions()
        {
            AddClassDefinition(typeof(WarExhaustionEventRecord), 19);
            AddClassDefinition(typeof(DailyRecord), 20);
            AddClassDefinition(typeof(CasualtyRecord), 21);
            AddClassDefinition(typeof(SummaryCasualtyRecord), 22);
            AddClassDefinition(typeof(BattleCasualtyRecord), 23);
            AddClassDefinition(typeof(SiegeRecord), 24);
            AddClassDefinition(typeof(RaidRecord), 25);
            AddClassDefinition(typeof(HeroRelatedRecord), 26);
            AddClassDefinition(typeof(HeroImprisonedRecord), 27);
            AddClassDefinition(typeof(HeroPerishedRecord), 28);
            AddClassDefinition(typeof(OccupiedRecord), 29);
            AddClassDefinition(typeof(DivineInterventionRecord), 30);
            AddClassDefinition(typeof(CaravanRaidRecord), 31);
        }

        protected override void DefineStructTypes()
        {
            AddStructDefinition(typeof(FactionPair), 9);
            AddStructDefinition(typeof(WarExhaustionRecord), 18);
        }

        protected override void DefineEnumTypes()
        {
            AddEnumDefinition(typeof(RebelDemandType), 12);
            AddEnumDefinition(typeof(WarExhaustionType), 15);
            AddEnumDefinition(typeof(VictoriousFactionType), 16);
            AddEnumDefinition(typeof(ActiveQuestState), 17);
            AddEnumDefinition(typeof(StrategicState), 40); // Added new StrategicState enum
        }

        protected override void DefineContainerDefinitions()
        {
            ConstructContainerDefinition(typeof(List<Messenger>));
            ConstructContainerDefinition(typeof(Dictionary<IFaction, CampaignTime>));
            ConstructContainerDefinition(typeof(Dictionary<Kingdom, CampaignTime>));
            ConstructContainerDefinition(typeof(List<DiplomaticAgreement>));
            ConstructContainerDefinition(typeof(Dictionary<FactionPair, List<DiplomaticAgreement>>));
            ConstructContainerDefinition(typeof(List<RebelFaction>));
            ConstructContainerDefinition(typeof(Dictionary<Kingdom, List<RebelFaction>>));
            ConstructContainerDefinition(typeof(Dictionary<Town, Clan>));
            ConstructContainerDefinition(typeof(Dictionary<string, WarExhaustionRecord>));
            ConstructContainerDefinition(typeof(List<WarExhaustionEventRecord>));
            ConstructContainerDefinition(typeof(Dictionary<string, List<WarExhaustionEventRecord>>));
            ConstructContainerDefinition(typeof(Dictionary<string, StrategicState>)); // Added new Dictionary container
        }
    }
}