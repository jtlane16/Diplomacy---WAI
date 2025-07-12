using Bannerlord.UIExtenderEx;

using Diplomacy.Companion;

using HarmonyLib;

using System;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

using TodayWeFeast;

using WarAndAiTweaks.Strategic.Marshal;
using WarAndAiTweaks.Systems;
using WarAndAiTweaks.WarPeaceAI;

namespace WarAndAiTweaks
{
    public class SubModule : MBSubModuleBase
    {
        public static readonly string Name = typeof(SubModule).Namespace;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                new Harmony("mod.waraitweaks.diplomacy").PatchAll();
            }
            catch (System.Exception e)
            {
                InformationManager.DisplayMessage(new InformationMessage($"War & AI Tweaks Harmony Error: {e.Message}", Colors.Red));
            }

            var extender = new UIExtender("WarAndAiTweaks.DiplomacyUI");
            extender.Register(typeof(SubModule).Assembly);
            extender.Enable();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);
            if (game.GameType is Campaign)
            {
                var campaignStarter = (CampaignGameStarter) gameStarter;
                campaignStarter.AddBehavior(new SettlementCultureChangerBehavior());
                campaignStarter.AddBehavior(new MarshalManager());
                campaignStarter.AddBehavior(new EnhancedAiMilitaryBehavior());
                campaignStarter.AddBehavior(new KingdomLogicController());
                campaignStarter.AddBehavior(new FeastBehavior());
            }
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            if (mission.PlayerTeam != null)
            {
                mission.AddMissionBehavior(new CompanionMissionLogic());
                mission.AddMissionBehavior(new CompanionMissionView());
            }
        }
    }
}