using Diplomacy.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace WarAndAiTweaks
{
    /// <summary>
    /// Abstract base class for evaluating the strategic value of a diplomatic action.
    /// This model replaces the previous simple weighted score with a more nuanced evaluation
    /// based on the kingdom's current strategic situation.
    /// </summary>
    public abstract class AbstractStrategicScoringModel
    {
        /// <summary>
        /// The minimum score required for the AI to consider this action.
        /// </summary>
        public abstract float ScoreThreshold { get; }

        /// <summary>
        /// Calculates the strategic score for a given diplomatic action between two kingdoms.
        /// </summary>
        /// <param name="proposingKingdom">The kingdom considering the action.</param>
        /// <param name="otherKingdom">The target of the action.</param>
        /// <param name="includeDesc">Whether to include descriptive text for the score breakdown.</param>
        /// <returns>An ExplainedNumber representing the strategic score.</returns>
        public virtual ExplainedNumber GetScore(Kingdom proposingKingdom, Kingdom otherKingdom, bool includeDesc = false)
        {
            var score = new ExplainedNumber(0, includeDesc);
            EvaluateComponentScores(proposingKingdom, otherKingdom, ref score);
            return score;
        }

        /// <summary>
        /// The core evaluation logic to be implemented by child classes.
        /// </summary>
        protected abstract void EvaluateComponentScores(Kingdom us, Kingdom them, ref ExplainedNumber score);

        /// <summary>
        /// Determines if a diplomatic action should be pursued based on the score.
        /// </summary>
        /// <param name="proposingKingdom">The kingdom considering the action.</param>
        /// <param name="otherKingdom">The target of the action.</param>
        /// <returns>True if the score meets the threshold, false otherwise.</returns>
        public bool ShouldTakeAction(Kingdom proposingKingdom, Kingdom otherKingdom)
        {
            //InformationManager.DisplayMessage(new InformationMessage("The score for forming a non-aggression pact from: " + proposingKingdom.Name + " to " + otherKingdom.Name + " was: " + GetScore(proposingKingdom, otherKingdom).ResultNumber));
            return GetScore(proposingKingdom, otherKingdom).ResultNumber >= ScoreThreshold;
        }

        /// <summary>
        /// Determines if a diplomatic action should be pursued, checking from both perspectives.
        /// </summary>
        /// <param name="kingdom1">The first kingdom.</param>
        /// <param name="kingdom2">The second kingdom.</param>
        /// <returns>True if both kingdoms find the action favorable, false otherwise.</returns>
        public bool ShouldTakeActionBidirectional(Kingdom kingdom1, Kingdom kingdom2)
        {
            return ShouldTakeAction(kingdom1, kingdom2) && ShouldTakeAction(kingdom2, kingdom1);
        }
    }

    /// <summary>
    /// Scores the strategic value of forming an alliance.
    /// Alliances are valued for mutual defense, shared objectives, and containing threats.
    /// </summary>
    public class AllianceScoringModel : AbstractStrategicScoringModel
    {
        public override float ScoreThreshold => 80f;

        protected override void EvaluateComponentScores(Kingdom us, Kingdom them, ref ExplainedNumber score)
        {
            // --- Base Desire ---
            score.Add(20f, new TextObject("Base Desire for Alliance"));

            // --- Power Balance ---
            // It's generally favorable to ally with a slightly stronger kingdom for protection,
            // but a much stronger ally can be a future threat.
            float powerRatio = them.TotalStrength / Math.Max(1f, us.TotalStrength);
            float powerScore = (powerRatio > 1.0f) ? (1.0f / powerRatio) * 30f : powerRatio * 20f;
            score.Add(powerScore, new TextObject("Relative Power Balance"));

            // --- Common Enemies ---
            // A strong incentive for an alliance.
            var commonEnemies = FactionManager.GetEnemyKingdoms(us).Intersect(FactionManager.GetEnemyKingdoms(them));
            foreach (var enemy in commonEnemies)
            {
                score.Add(40f, new TextObject("Common Enemy: {ENEMY_NAME}").SetTextVariable("ENEMY_NAME", enemy.Name));
            }

            // --- Proximity ---
            // Neighbors make better allies as they can respond to threats more easily.
            int proximityScore = DiplomacyBehavior.Instance.GetNeighborProximityScore(us, them);
if (proximityScore > 0)
{
    // The bonus now scales with how many borders they share.
    // The multiplier (e.g., 4f) can be tweaked for balance.
    score.Add(proximityScore * 4f, new TextObject("Geographic Proximity"));
}

            // --- Existing Relationships ---
            // Existing alliances with our enemies are a major red flag.
            var theirAllies = them.GetAlliedKingdoms();
            foreach (var ourEnemy in FactionManager.GetEnemyKingdoms(us))
            {
                if (theirAllies.Contains(ourEnemy))
                {
                    score.Add(-200f, new TextObject("Allied with our Enemy: {ENEMY_NAME}").SetTextVariable("ENEMY_NAME", ourEnemy.Name));
                }
            }

            // --- Relationship between leaders ---
            if (us.Leader != null && them.Leader != null)
            {
                score.Add(us.Leader.GetRelation(them.Leader) * 0.5f, new TextObject("Leader Relations"));
            }

            // --- Defensive Coalition against a major threat ---
            // More likely to ally against a kingdom that is "snowballing".
            var allKingdoms = DiplomacyHelpers.MajorKingdoms().ToList();
            var strongestKingdom = allKingdoms.OrderByDescending(k => k.TotalStrength).FirstOrDefault();
            if (strongestKingdom != null && strongestKingdom != us && strongestKingdom != them)
            {
                if (FactionManager.IsAtWarAgainstFaction(us, strongestKingdom) && FactionManager.IsAtWarAgainstFaction(them, strongestKingdom))
                {
                    score.Add(50, new TextObject("Coalition against {THREAT_NAME}").SetTextVariable("THREAT_NAME", strongestKingdom.Name));
                }
            }
        }
    }

    /// <summary>
    /// Scores the strategic value of forming a Non-Aggression Pact.
    /// Pacts are valued for securing borders to focus on other wars or to avoid a multi-front conflict.
    /// </summary>
    // In StrategicScoringModel.cs

    public class NonAggressionPactScoringModel : AbstractStrategicScoringModel
    {
        // The goal score is now higher.
        public override float ScoreThreshold => 75f;

        protected override void EvaluateComponentScores(Kingdom us, Kingdom them, ref ExplainedNumber score)
        {
            // Base desire is lower.
            score.Add(15f, new TextObject("Base Desire for Pact"));

            // Kingdoms already at war are more likely to seek pacts to secure their other borders.
            int wars = DiplomacyHelpers.MajorEnemies(us).Count();
            if (wars > 0)
            {
                // This is now a more powerful incentive.
                score.Add(wars * 30f, new TextObject("Engaged in Other Wars"));
            }

            // A pact with a much stronger neighbor is still valuable.
            float powerRatio = them.TotalStrength / Math.Max(1f, us.TotalStrength);
            if (powerRatio > 1.2f)
            {
                score.Add(powerRatio * 25f, new TextObject("Perceived Threat"));
            }

            // The neighbor bonus is slightly reduced.
            if (DiplomacyHelpers.AreNeighbors(us, them))
            {
                score.Add(25f, new TextObject("Shared Border Security"));
            }
            else
            {
                // Made the penalty for non-neighbors stronger.
                score.Add(-50f, new TextObject("Not a direct neighbor"));
            }

            // --- Relationship between leaders ---
            if (us.Leader != null && them.Leader != null)
            {
                score.Add(us.Leader.GetRelation(them.Leader) * 0.6f, new TextObject("Leader Relations"));
            }

            // --- Economic Factor ---
            // A prosperous kingdom is less likely to need a pact out of desperation.
            score.Add(DiplomacyHelpers.CalculateEconomicBoost(us, false) * -50f, new TextObject("Economic Stability"));
        }
    }

    /// <summary>
    /// Scores the strategic value of breaking an alliance.
    /// A high score indicates a desire to break the alliance.
    /// </summary>
    public class BreakAllianceScoringModel : AbstractStrategicScoringModel
    {
        public override float ScoreThreshold => 70f;

        protected override void EvaluateComponentScores(Kingdom us, Kingdom them, ref ExplainedNumber score)
        {
            // A high score means the alliance should be broken.
            // --- Base Desire to maintain alliances ---
            score.Add(-50f, new TextObject("Inertia to maintain alliance"));

            // --- Negative Leader Relations ---
            // A very strong reason to break an alliance.
            if (us.Leader != null && them.Leader != null)
            {
                float relation = us.Leader.GetRelation(them.Leader);
                if (relation < -10)
                {
                    score.Add(-relation * 1.5f, new TextObject("Poor Leader Relations"));
                }
            }

            // --- No Common Enemies ---
            // If the original reason for the alliance is gone, it's less valuable.
            var commonEnemies = FactionManager.GetEnemyKingdoms(us).Intersect(FactionManager.GetEnemyKingdoms(them));
            if (!commonEnemies.Any())
            {
                score.Add(40f, new TextObject("No Common Enemies"));
            }

            // --- Ally has become weak ---
            // A weak ally can be a liability.
            float powerRatio = them.TotalStrength / Math.Max(1f, us.TotalStrength);
            if (powerRatio < 0.5f)
            {
                score.Add((1 - powerRatio) * 50f, new TextObject("Ally is Weak"));
            }

            // --- Opportunity to attack a vulnerable neighbor ---
            // If a weak neighbor is not allied with our current ally, breaking the alliance could open up an opportunity.
            var weakNeighbor = DiplomacyHelpers.MajorKingdoms().FirstOrDefault(k => k != us && k != them && DiplomacyHelpers.AreNeighbors(us, k) && (k.TotalStrength < us.TotalStrength * 0.6f));
            if (weakNeighbor != null && !FactionManager.IsAlliedWithFaction(them, weakNeighbor))
            {
                score.Add(30f, new TextObject("Opportunity to attack {WEAK_NEIGHBOR}").SetTextVariable("WEAK_NEIGHBOR", weakNeighbor.Name));
            }


        }
    }

    /// <summary>
    /// Scores the strategic value of breaking a non-aggression pact.
    /// A high score indicates a desire to break the pact.
    /// </summary>
    public class BreakNonAggressionPactScoringModel : AbstractStrategicScoringModel
    {
        public override float ScoreThreshold => 50f;

        protected override void EvaluateComponentScores(Kingdom us, Kingdom them, ref ExplainedNumber score)
        {
            // --- Base Desire to maintain pacts ---
            score.Add(-30f, new TextObject("Inertia to maintain pact"));

            // --- Target has become very weak ---
            // A prime opportunity for conquest.
            float powerRatio = them.TotalStrength / Math.Max(1f, us.TotalStrength);
            if (powerRatio < 0.4f)
            {
                score.Add((1 - powerRatio) * 60f, new TextObject("Target is Vulnerable"));
            }

            // --- Opportunity for a strong alliance that conflicts with the pact ---
            // If a powerful kingdom is at war with our pact-partner, we might break the pact to ally with the powerful kingdom.
            var powerfulKingdom = DiplomacyHelpers.MajorKingdoms().FirstOrDefault(k => k != us && k != them && k.TotalStrength > us.TotalStrength * 1.5f);
            if (powerfulKingdom != null && FactionManager.IsAtWarAgainstFaction(powerfulKingdom, them))
            {
                score.Add(50f, new TextObject("Opportunity for a stronger alliance with {NEW_ALLY}").SetTextVariable("NEW_ALLY", powerfulKingdom.Name));
            }

            // --- Negative Relations ---
            if (us.Leader != null && them.Leader != null)
            {
                float relation = us.Leader.GetRelation(them.Leader);
                if (relation < -20)
                {
                    score.Add(-relation, new TextObject("Deteriorated Relations"));
                }
            }

            // --- Relationship and Trust ---
            if (us.Leader != null && them.Leader != null)
            {
                // FIX: Added a negative sign. High relation now SUBTRACTS from the score.
                score.Add(us.Leader.GetRelation(them.Leader) * -1.0f, new TextObject("Leader Relations"));
            }

            // --- Economic Factor ---
            // A stable economy makes a kingdom MORE willing to consider a war (and thus break a pact).
            // FIX: Removed the negative sign. A positive boost now ADDS to the score.
            score.Add(DiplomacyHelpers.CalculateEconomicBoost(us, false) * 50f, new TextObject("Economic Readiness for War"));

        }
    }
}
