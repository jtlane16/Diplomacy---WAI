using System.Collections.Generic;
using System.Linq;

using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CompanionHighlighter
{
    internal class CompanionMissionLogic : MissionLogic
    {
        public static CompanionMissionLogic Instance { get; private set; }
        public List<Agent> MissionCompanions { get; private set; }
        public bool IsReady { get; private set; }

        public CompanionMissionLogic()
        {
            Instance = this;
            MissionCompanions = new List<Agent>();
            IsReady = false;
        }

        public override void OnDeploymentFinished()
        {
            base.OnDeploymentFinished();
            MissionCompanions = Mission.Current.PlayerTeam.ActiveAgents
                .Where(agent => agent.IsHuman && agent.Character != null && agent.Character.IsHero && agent != Agent.Main)
                .ToList();
            IsReady = true;
        }

        public override void OnClearScene()
        {
            base.OnClearScene();
            MissionCompanions.Clear();
            IsReady = false;
        }
    }
}