using Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

using MathF = TaleWorlds.Library.MathF;

//Strategic AI Objectives:
//1.) AI must "think" intelligently about their decision
//2.) The ultmiate goal of AI is conquer the map is smart way driven by peace/war declarions
//3.) Wars should not be too short but should be enticed to by longer organically. War too long is consider 30 days or more
//4.) Peace should should not too long/short. Peace too long is consider 30 days or more
//5.) GetTotalWarScore and GetTotalPeaceScore should utlmiatly determine what the AI is thinking and must pass the thresholds commit to an action
//6.) Ensure there is no immediate war/peace back to back
//7.) WarWeariness and WarEagerness determine the AI want for peace and want for war respectivly.
//8.) Do not add factors, only balance existing factors to make the AI more intelligent and more organic in their decision making.
//9.) The AI should always steer clear of having mutliple wars.


namespace WarAndAiTweaks.WarPeaceAI
{
    /// <summary>
    /// Represents the strategic stance of one kingdom toward all other kingdoms.
    /// Each relationship is tracked as a "needle" on a gauge from 0-100:
    /// 0-30: Consider Peace (if at war)
    /// 30-50: Neutral/Maintain Status
    /// 50-100: Consider War (if at peace)
    /// </summary>
    public class KingdomStrategy
    {
        // Stance values: 0-100 for each target kingdom
        [SaveableField(1)]
        public Dictionary<string, float> Stances = new();

        // Configuration
        public const float PEACE_THRESHOLD = 30f;
        public const float NEUTRAL_THRESHOLD = 50f;
        public const float WAR_THRESHOLD = 50f;

        // Stance change limits per day
        public const float MAX_DAILY_CHANGE = 8f;
        public const float MIN_DAILY_CHANGE = 0.5f;

        public KingdomStrategy()
        {
            Stances = new Dictionary<string, float>();
        }

        /// <summary>
        /// Gets the current stance value (0-100) toward a target kingdom
        /// </summary>
        public float GetStance(Kingdom target)
        {
            if (target == null) return 50f;

            if (!Stances.TryGetValue(target.StringId, out float stance))
            {
                // Initialize new relationships deterministically based on kingdom characteristics
                stance = CalculateInitialStance(target);
                Stances[target.StringId] = stance;
            }
            return stance;
        }
        private float CalculateInitialStance(Kingdom target)
        {
            // Start at neutral
            float initialStance = 50f;

            // Adjust based on cultural relationships
            if (target?.Culture != null)
            {
                // Same culture = slight friendship
                // Different culture = slight tension
                // This creates consistent initial relationships
                initialStance += target.Culture.GetHashCode() % 10 - 5; // -5 to +5 deterministic variance
            }

            // Clamp to reasonable initial range (40-60)
            return MathF.Clamp(initialStance, 40f, 60f);
        }

        /// <summary>
        /// Sets the stance value, clamping to 0-100 range
        /// </summary>
        public void SetStance(Kingdom target, float value)
        {
            if (target == null) return;
            Stances[target.StringId] = MathF.Clamp(value, 0f, 100f);
        }

        /// <summary>
        /// Adjusts stance by a delta, respecting daily change limits
        /// </summary>
        public void AdjustStance(Kingdom target, float delta)
        {
            if (target == null) return;

            float currentStance = GetStance(target);
            float clampedDelta = MathF.Clamp(delta, -MAX_DAILY_CHANGE, MAX_DAILY_CHANGE);

            // Apply minimum change threshold
            if (Math.Abs(clampedDelta) < MIN_DAILY_CHANGE && Math.Abs(delta) > MIN_DAILY_CHANGE)
            {
                clampedDelta = Math.Sign(delta) * MIN_DAILY_CHANGE;
            }

            SetStance(target, currentStance + clampedDelta);
        }

        /// <summary>
        /// Determines if this kingdom should consider declaring war on target
        /// </summary>
        public bool ShouldConsiderWar(Kingdom target)
        {
            return GetStance(target) >= WAR_THRESHOLD;
        }

        /// <summary>
        /// Determines if this kingdom should consider making peace with target
        /// </summary>
        public bool ShouldConsiderPeace(Kingdom target)
        {
            return GetStance(target) <= PEACE_THRESHOLD;
        }

        /// <summary>
        /// Gets a readable description of the stance
        /// </summary>
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

        /// <summary>
        /// Gets all kingdoms this strategy should consider for war
        /// </summary>
        public List<Kingdom> GetWarTargets(Kingdom self)
        {
            return Kingdom.All
                .Where(k => k != self && !k.IsEliminated && !k.IsMinorFaction
                           && k.Leader != null && !self.IsAtWarWith(k)
                           && ShouldConsiderWar(k))
                .OrderByDescending(k => GetStance(k))
                .ToList();
        }

        /// <summary>
        /// Gets all kingdoms this strategy should consider for peace
        /// </summary>
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