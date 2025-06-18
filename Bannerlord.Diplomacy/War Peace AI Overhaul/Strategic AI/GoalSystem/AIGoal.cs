using TaleWorlds.CampaignSystem;

namespace WarAndAiTweaks.AI.Goals
{
    public abstract class AIGoal
    {
        public Kingdom Kingdom { get; }
        public GoalType Type { get; }
        public float Priority { get; set; } // Changed from protected set; to public set;

        protected AIGoal(Kingdom kingdom, GoalType type)
        {
            Kingdom = kingdom;
            Type = type;
            Priority = 0f;
        }

        // This method will be responsible for calculating how important this goal is right now.
        public abstract void EvaluatePriority();
    }

    public enum GoalType
    {
        Survive,
        Strengthen,
        Expand
    }
}