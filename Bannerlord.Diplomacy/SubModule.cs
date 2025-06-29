﻿using Bannerlord.UIExtenderEx;

using CompanionHighlighter;

using HarmonyLib;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

using TodayWeFeast;

using WarAndAiTweaks.AI.Behaviors;
using WarAndAiTweaks.DiplomaticAction;
using WarAndAiTweaks.Systems;
using WarAndAiTweaks.AI;

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
                // It's better to use the in-game logger for error messages
                InformationManager.DisplayMessage(new InformationMessage($"War & AI Tweaks Harmony Error: {e.Message}", Colors.Red));
            }

            // UIExtender will find and apply both your XML and C# mixins
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
                campaignStarter.AddBehavior(new StrategicAICampaignBehavior());
                campaignStarter.AddBehavior(new DiplomaticAgreementManager());
                campaignStarter.AddBehavior(new InfamyManager());
                campaignStarter.AddBehavior(new FeastBehavior());
                campaignStarter.AddBehavior(new SettlementCultureChangerBehavior());
                AIComputationLogger.ClearLog();
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