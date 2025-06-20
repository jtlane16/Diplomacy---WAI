using Bannerlord.ButterLib.Common.Extensions;
using Bannerlord.UIExtenderEx;
using Bannerlord.UIExtenderEx.ResourceManager;

using CompanionHighlighter;

using HarmonyLib;

using System;
using System.Linq;
using System.Windows;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

using WarAndAiTweaks;
using WarAndAiTweaks.AI;
using WarAndAiTweaks.AI.Behaviors;

namespace Diplomacy
{
    public sealed class SubModule : MBSubModuleBase
    {
        public static readonly string Version = $"v{typeof(SubModule).Assembly.GetName().Version!.ToString(3)}";

        public static readonly string Name = typeof(SubModule).Namespace!;
        public static readonly string DisplayName = new TextObject($"{{=MYz8nKqq}}{Name}").ToString();
        public static readonly string MainHarmonyDomain = "bannerlord." + Name.ToLower();
        public static readonly string CampaignHarmonyDomain = MainHarmonyDomain + ".campaign";
        public static readonly string WidgetHarmonyDomain = MainHarmonyDomain + ".widgets";

        internal static readonly Color StdTextColor = Color.FromUint(0x00F16D26); // Orange

        internal static SubModule Instance { get; set; } = default!;

        private bool _hasLoaded;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            Instance = this;

            var extender = UIExtender.Create(Name);
            extender.Register(typeof(SubModule).Assembly);
            extender.Enable();

            // Clear the AI log on every game load
            AIComputationLogger.ClearLog();
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

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            if (!_hasLoaded)
            {
                _hasLoaded = true;
                InformationManager.DisplayMessage(new InformationMessage(new TextObject($"{{=hPERH3u4}}Loaded {{NAME}}").SetTextVariable("NAME", DisplayName).ToString(), StdTextColor));
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);

            if (game.GameType is Campaign)
            {
                if (gameStarterObject is CampaignGameStarter campaignGameStarter)
                {
                    campaignGameStarter.AddBehavior(new StrategicAICampaignBehavior());
                    campaignGameStarter.AddBehavior(new DiplomaticAgreementManager());
                }
            }
        }

        private T? GetGameModel<T>(IGameStarter gameStarterObject) where T : GameModel
        {
            var models = gameStarterObject.Models.ToArray();

            for (int index = models.Length - 1; index >= 0; --index)
            {
                if (models[index] is T gameModel1)
                    return gameModel1;
            }
            return default;
        }

        public override void OnGameEnd(Game game)
        {
            base.OnGameEnd(game);
        }
    }
}