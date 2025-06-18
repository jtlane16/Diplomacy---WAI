namespace WarAndAiTweaks.AI.Goals
{
    public enum StrategicState
    {
        Rebuilding,   // Focus on economy and garrisons, avoid new wars
        Defensive,      // Threatened by a superior foe, seek alliances
        Opportunistic,  // Default balanced state
        Expansionist,   // Strong and wealthy, actively seek war
        Desperate       // Losing badly, seek peace at all costs
    }
}