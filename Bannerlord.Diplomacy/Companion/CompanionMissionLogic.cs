using System.Collections.Generic;
using System.Linq;

using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem;
using Diplomacy.Companion;

namespace Diplomacy.Companion
{
    internal class CompanionMissionLogic : MissionLogic
    {
        public static CompanionMissionLogic Instance { get; private set; }
        public List<Agent> MissionCompanions { get; private set; }
        public bool IsReady { get; private set; }
        private bool IsBattleMission => Mission?.CombatType == Mission.MissionCombatType.Combat;

        public CompanionMissionLogic()
        {
            Instance = this;
            MissionCompanions = new List<Agent>();
            IsReady = false;
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            if (!IsBattleMission)
            {
                // Non-battle mission (like party screen), mark as ready but don't track companions
                IsReady = true;
                return;
            }
        }

        public override void OnDeploymentFinished()
        {
            base.OnDeploymentFinished();

            // Only track companions in actual battle missions
            if (IsBattleMission && Mission.Current?.PlayerTeam != null)
            {
                MissionCompanions = Mission.Current.PlayerTeam.ActiveAgents
                    .Where(agent => agent?.IsHuman == true
                           && agent.Character != null
                           && agent.Character.IsHero
                           && agent != Agent.Main)
                    .ToList();
                IsReady = true;
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            // Clean up any null or inactive agents
            if (IsBattleMission && IsReady)
            {
                MissionCompanions.RemoveAll(agent => agent == null || !agent.IsActive());
            }
        }

        public override void OnClearScene()
        {
            base.OnClearScene();
            MissionCompanions.Clear();
            IsReady = false;
        }

        public override void OnRemoveBehavior()
        {
            base.OnRemoveBehavior();
            MissionCompanions.Clear();
            IsReady = false;
            if (Instance == this)
                Instance = null;
        }
    }
}