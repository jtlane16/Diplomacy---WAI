using System.Collections.Generic;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace CompanionHighlighter
{
    [DefaultView]
    public class CompanionMissionView : MissionView
    {
        private GauntletLayer _gauntletLayer;
        private CompanionHighlighterVM _dataSource;
        private readonly Dictionary<Agent, CompanionIconVM> _agentToIconMap = new Dictionary<Agent, CompanionIconVM>();
        private bool _isInitialized = false;
        private Camera _camera;

       public override void OnBehaviorInitialize()
		{
			if (base.Mission != null)
			{
				MissionMode mode = base.Mission.Mode;
				if (mode == MissionMode.Battle || mode == MissionMode.Deployment || mode <= MissionMode.StartUp)
                {
                    base.OnBehaviorInitialize();
                }
			}
		}

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);


            if (!_isInitialized)
            {
                if (CompanionMissionLogic.Instance != null && CompanionMissionLogic.Instance.IsReady)
                {
                    InitializeUI();
                    _isInitialized = true;
                }
                return;
            }

            UpdateCompanionIconPositions();
        }

        private void InitializeUI()
        {
            _gauntletLayer = new GauntletLayer(110, "GauntletLayer");
            _dataSource = new CompanionHighlighterVM();
            _gauntletLayer.LoadMovie("CompanionMissionHUD", _dataSource);
            MissionScreen.AddLayer(_gauntletLayer);

            _camera = MissionScreen.CombatCamera;

            _agentToIconMap.Clear();

            int companionIndex = 0;
            foreach (var agent in CompanionMissionLogic.Instance.MissionCompanions)
            {
                if (agent?.Character is CharacterObject co && companionIndex < 5) // Max 5 companions
                {
                    var iconVM = _dataSource.GetCompanionSlot(companionIndex);
                    if (iconVM != null)
                    {
                        iconVM.CompanionName = co.Name.ToString();
                        iconVM.IsVisible = false;
                        iconVM.Width = 30f;
                        iconVM.Height = 30f;
                        iconVM.FontSize = 16;

                        _agentToIconMap[agent] = iconVM;
                        companionIndex++;
                    }
                }
            }
        }

        private void UpdateCompanionIconPositions()
        {
            // Add a check for Agent.Main here
            if (!_isInitialized || _camera == null || Agent.Main == null) return;

            foreach (var entry in _agentToIconMap)
            {
                var agent = entry.Key;
                var iconVM = entry.Value;

                if (agent.IsActive())
                {
                    Vec3 position = agent.GetEyeGlobalPosition();
                    position.z += 0.8f; // Position above head

                    float screenX = 0f, screenY = 0f, screenW = 0f;
                    MBWindowManager.WorldToScreen(_camera, position, ref screenX, ref screenY, ref screenW);

                    if (screenW > 0 && screenX > 0 && screenY > 0 &&
                        screenX < Screen.RealScreenResolutionWidth &&
                        screenY < Screen.RealScreenResolutionHeight)
                    {
                        iconVM.IsVisible = true;

                        // Center the widget on the character
                        iconVM.PositionX = screenX - 100f; // Half of widget width (200)
                        iconVM.PositionY = screenY - 25f;  // Half of widget height (50)

                        // Scale icon size based on distance
                        // This line is now safe because of the check at the start of the method
                        float distance = agent.Position.Distance(Agent.Main.Position);
                        float scale = MBMath.Lerp(1f, 0.5f, (distance - 5f) / 50f);
                        scale = MBMath.ClampFloat(scale, 0.5f, 1f);

                        iconVM.Width = 30f * scale;
                        iconVM.Height = 30f * scale;
                        iconVM.FontSize = (int) (16f * scale);
                    }
                    else
                    {
                        iconVM.IsVisible = false;
                    }
                }
                else
                {
                    iconVM.IsVisible = false;
                }
            }
        }

        // Removed the HighlightCompanions() method entirely

        public override void OnMissionScreenFinalize()
        {
            base.OnMissionScreenFinalize();

            if (_gauntletLayer != null)
            {
                MissionScreen.RemoveLayer(_gauntletLayer);
            }

            _agentToIconMap.Clear();
            _gauntletLayer = null;
            _dataSource = null;
            _camera = null;
            _isInitialized = false;
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);

            if (_agentToIconMap.ContainsKey(affectedAgent))
            {
                // Hide the icon
                _agentToIconMap[affectedAgent].IsVisible = false;

                // Removed the contour removal code since we're no longer adding contours
            }
        }
    }
}