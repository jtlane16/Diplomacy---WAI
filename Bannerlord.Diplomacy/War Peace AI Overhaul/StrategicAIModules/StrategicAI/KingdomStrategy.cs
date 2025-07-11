using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

using MathF = TaleWorlds.Library.MathF;

//Strategic AI Objectives:
//1.) AI must "think" intelligently about their decision
//2.) The ultimate goal of AI is conquer the map is smart way driven by peace/war declarations
//3.) Wars should not be too short but should be enticed to by longer organically. War too long is consider 30 days or more
//4.) Peace should should not too long/short. Peace too long is consider 30 days or more
//5.) Any design change should be as lean and simple as possible to avoid bloating and should be proposed when absolutely necessary.
//6.) Ensure there is no immediate war/peace back to back
//9.) The AI should always steer clear of having multiple wars.
//10.) The entire design is based off of the idea of a "needle" on a gauge from 0-100, where each kingdom has a stance toward every other kingdom. 0-30 is "consider peace" (if at war), 30-50 is "neutral/maintain status", and 50-100 is "consider war" (if at peace). This allows for a more nuanced and dynamic relationship system.

namespace WarAndAiTweaks.WarPeaceAI
{
    /// <summary>
    /// Core stance system: 0-100 needle gauge for each kingdom relationship
    /// 0-30: Consider Peace (if at war)
    /// 30-50: Neutral/Maintain Status  
    /// 50-100: Consider War (if at peace)
    /// </summary>
    public class KingdomStrategy
    {
        [SaveableField(1)]
        public Dictionary<string, float> Stances = new();

        // Core thresholds
        public const float PEACE_THRESHOLD = 30f;
        public const float NEUTRAL_THRESHOLD = 50f;
        public const float WAR_THRESHOLD = 50f;

        // Daily change limits (supports objectives #3, #4, #6)
        public const float MAX_DAILY_CHANGE = 5f;
        public const float MIN_DAILY_CHANGE = 0.3f;

        public KingdomStrategy()
        {
            Stances = new Dictionary<string, float>();
        }

        public float GetStance(Kingdom target)
        {
            if (target == null) return 50f;

            if (!Stances.TryGetValue(target.StringId, out float stance))
            {
                stance = CalculateInitialStance(target);
                Stances[target.StringId] = stance;
            }
            return stance;
        }

        private float CalculateInitialStance(Kingdom target)
        {
            // Start neutral with slight cultural variance
            float initialStance = 50f;
            if (target?.Culture != null)
            {
                initialStance += target.Culture.GetHashCode() % 10 - 5; // -5 to +5 deterministic variance
            }
            return MathF.Clamp(initialStance, 40f, 60f);
        }

        public void SetStance(Kingdom target, float value)
        {
            if (target == null) return;
            Stances[target.StringId] = MathF.Clamp(value, 0f, 100f);
        }

        public void AdjustStance(Kingdom target, float delta)
        {
            if (target == null) return;

            float currentStance = GetStance(target);
            float clampedDelta = MathF.Clamp(delta, -MAX_DAILY_CHANGE, MAX_DAILY_CHANGE);

            // Apply minimum change threshold (prevents micro-adjustments)
            if (Math.Abs(clampedDelta) < MIN_DAILY_CHANGE)
                clampedDelta = 0f;

            SetStance(target, currentStance + clampedDelta);
        }

        public bool ShouldConsiderWar(Kingdom target)
        {
            return GetStance(target) >= WAR_THRESHOLD;
        }

        public bool ShouldConsiderPeace(Kingdom target)
        {
            return GetStance(target) <= PEACE_THRESHOLD;
        }

        public string GetStanceDescription(Kingdom target)
        {
            float stance = GetStance(target);
            if (stance <= 15f) return "Seeks Peace";
            if (stance <= 30f) return "Desires Peace";
            if (stance <= 45f) return "Cautious";
            if (stance <= 55f) return "Neutral";
            if (stance <= 70f) return "Watchful";
            if (stance <= 85f) return "Aggressive";
            return "Seeks War";
        }

        public List<Kingdom> GetWarTargets(Kingdom self)
        {
            return Kingdom.All
                .Where(k => k != self && !k.IsEliminated && !k.IsMinorFaction
                           && k.Leader != null && !self.IsAtWarWith(k)
                           && ShouldConsiderWar(k))
                .OrderByDescending(k => GetStance(k))
                .ToList();
        }

        public List<Kingdom> GetPeaceTargets(Kingdom self)
        {
            return Kingdom.All
                .Where(k => k != self && !k.IsEliminated && !k.IsMinorFaction
                           && k.Leader != null && self.IsAtWarWith(k)
                           && ShouldConsiderPeace(k))
                .OrderBy(k => GetStance(k))
                .ToList();
        }
    }
}