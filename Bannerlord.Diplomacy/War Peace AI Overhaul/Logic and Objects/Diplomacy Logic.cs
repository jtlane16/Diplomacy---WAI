using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;
using TaleWorlds.Library;
using Diplomacy.WarExhaustion;
using Diplomacy.DiplomaticAction.WarPeace;
using TaleWorlds.Localization;

namespace WarAndAiTweaks
{
    #region Data Structures (No Change)
    public class WarScoreBreakdown
    {
        public Kingdom Target { get; }
        public float FinalScore { get; set; }
        public float ThreatScore { get; set; }
        public float PowerBalanceScore { get; set; }
        public float MultiWarPenalty { get; set; }
        public float DistancePenalty { get; set; }
        public float DogpileBonus { get; set; }
        public WarScoreBreakdown(Kingdom target) { Target = target; }
    }

    public class PeaceScoreBreakdown
    {
        public Kingdom Target { get; }
        public float FinalScore { get; set; }
        public float DangerScore { get; set; }
        public float ExhaustionScore { get; set; }
        public float TributeFactor { get; set; }
        public int TributeAmount { get; set; }
        public PeaceScoreBreakdown(Kingdom target) { Target = target; }
    }
    #endregion

    /// <summary>
    /// This new unified behavior manages a kingdom's strategic decisions for both war and peace,
    /// making the AI more proactive and goal-oriented.
    /// </summary>
    public class StrategicAIBehavior : CampaignBehaviorBase
    {
        // How often, in days, a kingdom will evaluate its diplomatic situation.
        private const int DIPLOMACY_EVALUATION_COOLDOWN_DAYS = 3;
        private Dictionary<string, CampaignTime> _lastDiplomaticEvaluation;

        public StrategicAIBehavior()
        {
            _lastDiplomaticEvaluation = new Dictionary<string, CampaignTime>();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, DailyClanTick);
        }

        public override void SyncData(IDataStore store)
        {
            store.SyncData("_lastDiplomaticEvaluation", ref _lastDiplomaticEvaluation);
        }

        private void DailyClanTick(Clan clan)
        {
            // AI logic only runs for kingdom rulers who are not the player.
            if (clan.Kingdom?.Leader != clan.Leader || clan.Leader.IsHumanPlayerCharacter)
            {
                return;
            }

            var kingdom = clan.Kingdom;
            Kingdom kingdom2 = clan.Kingdom;
            if (kingdom2 == null) { return; }
            if (_lastDiplomaticEvaluation.TryGetValue(kingdom2.StringId, out var value))

                if (!_lastDiplomaticEvaluation.TryGetValue(kingdom.StringId, out var lastEvaluationTime)
                || (CampaignTime.Now - lastEvaluationTime).ToDays > DIPLOMACY_EVALUATION_COOLDOWN_DAYS)
            {
                // First, check if peace should be made in any ongoing wars.
                if (ConsiderMakingPeace(kingdom))
                {
                    // If a peace deal was made or proposed, stop further diplomatic actions for this cycle.
                    _lastDiplomaticEvaluation[kingdom.StringId] = CampaignTime.Now;
                    return;
                }

                // If not making peace, consider declaring a new war.
                ConsiderDeclaringWar(kingdom);

                // Update the last evaluation time to now
                _lastDiplomaticEvaluation[kingdom.StringId] = CampaignTime.Now;
            }
        }

        /// <summary>
        /// AI evaluates if it should sue for peace in any of its current wars.
        /// </summary>
        /// <returns>True if a peace action was taken or proposed, false otherwise.</returns>
        private bool ConsiderMakingPeace(Kingdom us)
        {
            var enemies = DiplomacyHelpers.MajorEnemies(us).ToList();
            if (!enemies.Any()) return false;

            foreach (var enemy in enemies)
            {
                if (WantsPeace(us, enemy))
                {
                    // If the enemy is the player, propose peace via an inquiry.
                    if (enemy.Leader.IsHumanPlayerCharacter)
                    {
                        KingdomPeaceAction.ApplyPeace(us, enemy);
                        return true; // A peace proposal was sent.
                    }
                    // If the enemy is another AI, check if they also want peace.
                    else if (WantsPeace(enemy, us))
                    {
                        // Both sides agree, so make peace directly without a vote.
                        MakePeaceAction.Apply(us, enemy);
                        return true; // Peace was made.
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Determines if a kingdom has sufficient desire to make peace with another.
        /// </summary>
        /// <param name="us">The kingdom evaluating peace.</param>
        /// <param name="them">The enemy kingdom.</param>
        /// <returns>True if the kingdom wants peace, false otherwise.</returns>
        private bool WantsPeace(Kingdom us, Kingdom them)
        {
            bool desperateForPeace = false;
            float warProgress = GetWarProgress(us, them);
            float exhaustion = WarExhaustionManager.Instance?.GetWarExhaustion(us, them) ?? 0f;

            // If war is going badly or exhaustion is critical, become desperate.
            if (warProgress < -30 || exhaustion > 80)
            {
                desperateForPeace = true;
            }

            // An honorable leader is more likely to accept a stalemate peace.
            float personalityModifier = us.Leader.GetTraitLevel(DefaultTraits.Honor) * 10f;

            // Calculate the desire for peace
            float peaceDesire = -warProgress + exhaustion + personalityModifier;

            // If desperate, AI will sue for peace regardless of other factors. Otherwise, requires a higher desire.
            float peaceThreshold = desperateForPeace ? 40f : 70f;

            return peaceDesire > peaceThreshold;
        }

        /// <summary>
        /// AI evaluates potential targets and decides if a new war is strategically advantageous.
        /// </summary>
        private void ConsiderDeclaringWar(Kingdom us)
        {
            // Don't start new wars if already in a difficult position.
            if (DiplomacyHelpers.MajorEnemies(us).Count() > 1) return;

            var potentialTargets = DiplomacyHelpers.MajorKingdoms()
                .Where(them => them != us && !FactionManager.IsAtWarAgainstFaction(us, them) && !FactionManager.IsAlliedWithFaction(us, them))
                .ToList();

            if (!potentialTargets.Any()) return;

            WarScoreBreakdown? bestTarget = null;
            float bestScore = float.MinValue;

            foreach (var target in potentialTargets)
            {
                var breakdown = DiplomacyHelpers.ComputeWarDesireScore(us, target);
                if (breakdown.FinalScore > bestScore)
                {
                    bestScore = breakdown.FinalScore;
                    bestTarget = breakdown;
                }
            }

            // Warlike leaders are more likely to pull the trigger.
            float warThreshold = 60f - (us.Leader.GetTraitLevel(DefaultTraits.Valor) * 15f);

            if (bestTarget != null && bestScore > warThreshold)
            {
                string reason = DiplomacyHelpers.GenerateWarReasoning(us, bestTarget);
                InformationManager.DisplayMessage(new InformationMessage(reason, Colors.Red));
                DeclareWarAction.ApplyByDefault(us, bestTarget.Target);
            }
        }

        /// <summary>
        /// A simple metric to determine how a war is going.
        /// Positive score means 'us' is winning, negative means 'them' is winning.
        /// </summary>
        private float GetWarProgress(Kingdom us, Kingdom them)
        {
            float ourCasualties = us.GetStanceWith(them).GetCasualties(us);
            float theirCasualties = them.GetStanceWith(us).GetCasualties(them);

            float casualtyScore = (theirCasualties - ourCasualties) / Math.Max(1f, theirCasualties + ourCasualties) * 50f;

            float ourFiefsLost = us.GetStanceWith(them).GetSuccessfulSieges(us);
            float theirFiefsLost = them.GetStanceWith(us).GetSuccessfulSieges(them);

            float fiefScore = (theirFiefsLost - ourFiefsLost) * 20f;

            return casualtyScore + fiefScore;
        }
    }

    #region Old Behaviors (To be removed/replaced)
    // The old WarDesireBehavior and PeaceDesireBehavior should be removed from SubModule.cs
    // and replaced with the new StrategicAIBehavior.
    public class WarDesireBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents() { }
        public override void SyncData(IDataStore s) { }
    }
    public class PeaceDesireBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents() { }
        public override void SyncData(IDataStore s) { }
        public static void NotifyWarStarted(string id) { }
        public static void SetExhaustion(string kid, float v) { }
        public static void BumpDesire(string kid, float d) { }
        public bool IsPeaceProposalAcceptable(Kingdom r, Kingdom p) => true;
    }
    #endregion

    #region Harmony Patches (No Change)
    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomWarDecision")]
    public static class Patch_DisableRandomWar { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomPeaceDecision")]
    public static class Patch_DisableRandomPeace { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

    [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnDeclarePeace")]
    public class KingdomPlayerPeacePatch
    {
        public static bool Prefix(KingdomWarItemVM item)
        {
            // Kept for player-initiated peace, but AI logic is now separate.
            return true;
        }
    }
    #endregion
}
