﻿using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using Diplomacy;
using static WarAndAiTweaks.AI.StrategicAI;

namespace WarAndAiTweaks.AI.Goals
{
    public class SurviveGoal : AIGoal
    {
        // The PeaceCandidate is removed, as the goal is no longer tied to a single enemy.
        private readonly IPeaceEvaluator _peaceEvaluator;

        public SurviveGoal(Kingdom kingdom, IPeaceEvaluator peaceEvaluator) : base(kingdom, GoalType.Survive)
        {
            _peaceEvaluator = peaceEvaluator;
        }

        public override void EvaluatePriority()
        {
            var enemies = FactionManager.GetEnemyKingdoms(Kingdom).ToList();
            if (!enemies.Any())
            {
                this.Priority = 0;
                return;
            }

            // The goal's priority is now the HIGHEST peace score among all enemies.
            // This represents the kingdom's most urgent need for peace with at least one party.
            float highestPeaceScore = 0f;
            foreach (var enemy in enemies)
            {
                var explainedScore = _peaceEvaluator.GetPeaceScore(Kingdom, enemy);
                var peaceScore = explainedScore.ResultNumber;

                AIComputationLogger.LogPeaceCandidate(this.Kingdom, enemy, peaceScore, explainedScore);

                if (peaceScore > highestPeaceScore)
                {
                    highestPeaceScore = peaceScore;
                }
            }

            this.Priority = highestPeaceScore;

            // Add a boost based on relative strength against all enemies.
            float totalEnemyStrength = enemies.Sum(e => e.TotalStrength);
            if (totalEnemyStrength > 0)
            {
                float strengthRatio = this.Kingdom.TotalStrength / totalEnemyStrength;

                // If we are significantly weaker, boost the priority to seek peace.
                if (strengthRatio < 0.7f) // i.e., less than 70% of the combined enemy strength
                {
                    // The weaker we are, the bigger the boost.
                    float weaknessFactor = (0.7f - strengthRatio) / 0.7f; // A value from 0 to 1
                    this.Priority += weaknessFactor * 25; // Add up to 25 extra points
                }
            }

            // Keep the fief-less boost as it's a clear sign of desperation.
            if (this.Kingdom.Fiefs.Count == 0)
            {
                this.Priority += 30;
            }
        }
    }
}