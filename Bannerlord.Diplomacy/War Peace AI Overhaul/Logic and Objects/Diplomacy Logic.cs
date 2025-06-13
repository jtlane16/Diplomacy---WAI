using Diplomacy.DiplomaticAction.WarPeace;
using Diplomacy.WarExhaustion;

using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Library;

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
        private const int DIPLOMACY_EVALUATION_COOLDOWN_DAYS = 3;

        // FIX: Initialize the dictionary at declaration to prevent it from ever being null.
        private Dictionary<string, CampaignTime> _lastDiplomaticEvaluation = new Dictionary<string, CampaignTime>();

        public StrategicAIBehavior()
        {
            // The initialization can be removed from the constructor as it's handled above.
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, DailyClanTick);
        }

        public override void SyncData(IDataStore store)
        {
            // FIX: With the dictionary always initialized, the null check is no longer needed.
            store.SyncData("_lastDiplomaticEvaluation", ref _lastDiplomaticEvaluation);
        }

        private void DailyClanTick(Clan clan)
        {
            if (clan.Kingdom == null || clan.Kingdom.Leader != clan.Leader || clan.Leader.IsHumanPlayerCharacter)
            {
                return;
            }

            var kingdom = clan.Kingdom;

            if (!_lastDiplomaticEvaluation.TryGetValue(kingdom.StringId, out var lastEvaluationTime)
                || (CampaignTime.Now - lastEvaluationTime).ToDays > DIPLOMACY_EVALUATION_COOLDOWN_DAYS)
            {
                if (ConsiderMakingPeace(kingdom))
                {
                    _lastDiplomaticEvaluation[kingdom.StringId] = CampaignTime.Now;
                    return;
                }

                ConsiderDeclaringWar(kingdom);

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
                    if (enemy.Leader != null && enemy.Leader.IsHumanPlayerCharacter)
                    {
                        // CORRECTED: Call the static Apply method which handles the player inquiry
                        KingdomPeaceAction.ApplyPeace(us, enemy);
                        return true; // A peace proposal was sent.
                    }
                    // If the enemy is another AI, check if they also want peace.
                    else if (WantsPeace(enemy, us))
                    {
                        // FIX: Add the logic to generate and display the peace reason.
                        // We need to get the "us" kingdom's perspective on why they want peace.
                        float ourExhaustion = WarExhaustionManager.Instance?.GetWarExhaustion(us, enemy) ?? 0f;
                        PeaceScoreBreakdown breakdown = DiplomacyHelpers.ComputePeaceScore(us, enemy, ourExhaustion);
                        string reason = DiplomacyHelpers.GeneratePeaceReasoning(us, breakdown);

                        InformationManager.DisplayMessage(new InformationMessage(reason, Colors.Green));

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
        public bool WantsPeace(Kingdom us, Kingdom them)
        {
            if (us?.Leader == null || them?.Leader == null)
            {
                return false;
            }

            bool desperateForPeace = false;
            float warProgress = GetWarProgress(us, them);
            float exhaustion = WarExhaustionManager.Instance?.GetWarExhaustion(us, them) ?? 0f;

            if (warProgress < -30 || exhaustion > 80)
            {
                desperateForPeace = true;
            }

            // FIX: This line was likely removed by accident, causing error CS0103 for 'personalityModifier'.
            float personalityModifier = us.Leader.GetTraitLevel(DefaultTraits.Honor) * 10f;

            float conquerorsResolve = us.Leader.GetTraitLevel(DefaultTraits.Valor) * -10f; // Negative score

            // FIX: Call CalculateEconomicBoost using its class 'DiplomacyHelpers', resolving error CS0103.
            // A negative boost (poor economy) creates positive pressure for peace.
            float economicPressure = DiplomacyHelpers.CalculateEconomicBoost(us, true) * -40f;

            // ADD conquerorsResolve to the final calculation.
            float peaceDesire = -warProgress + exhaustion + personalityModifier + economicPressure + conquerorsResolve;
            float peaceThreshold = desperateForPeace ? 40f : 70f;

            return peaceDesire > peaceThreshold;
        }

        /// <summary>
        /// AI evaluates potential targets and decides if a new war is strategically advantageous.
        /// </summary>
        private void ConsiderDeclaringWar(Kingdom us)
        {
            var potentialTargets = DiplomacyHelpers.MajorKingdoms()
                .Where(them => them != us
                               && !FactionManager.IsAlliedWithFaction(us, them)
                               // CORRECTED: Use the existing conditions instance to check for truces etc.
                               && DeclareWarConditions.Instance.CanApply(us, them))
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

            float warThreshold = 60f - (us.Leader?.GetTraitLevel(DefaultTraits.Valor) * 15f ?? 0f);

            if (bestTarget != null && bestScore > warThreshold)
            {
                // FIX: Declare war FIRST, then display the reasoning message.
                // This prevents the game's default war notification from overriding our custom message.
                DeclareWarAction.ApplyByDefault(us, bestTarget.Target);

                string reason = DiplomacyHelpers.GenerateWarReasoning(us, bestTarget);
                InformationManager.DisplayMessage(new InformationMessage(reason, Colors.Red));
            }
        }

        /// <summary>
        /// A simple metric to determine how a war is going.
        /// </summary>
        private float GetWarProgress(Kingdom us, Kingdom them)
        {
            StanceLink? stance = us.GetStanceWith(them);
            if (stance == null) return 0f;

            float ourCasualties = stance.GetCasualties(us);
            float theirCasualties = stance.GetCasualties(them);
            float casualtyScore = (theirCasualties > 0 || ourCasualties > 0) ? (theirCasualties - ourCasualties) / Math.Max(1f, theirCasualties + ourCasualties) * 50f : 0f;

            float ourFiefsLost = stance.GetSuccessfulSieges(us);
            float theirFiefsLost = stance.GetSuccessfulSieges(them);
            float fiefScore = (theirFiefsLost - ourFiefsLost) * 20f;

            return casualtyScore + fiefScore;
        }
    }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomWarDecision")]
    public static class Patch_DisableRandomWar { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomPeaceDecision")]
    public static class Patch_DisableRandomPeace { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

    [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnDeclarePeace")]
    public class KingdomPlayerPeacePatch
    {
        public static bool Prefix(KingdomWarItemVM item)
        {
            var peaceBehavior = Campaign.Current.GetCampaignBehavior<StrategicAIBehavior>();
            if (peaceBehavior == null) return true;
            var playerKingdom = Hero.MainHero.Clan.Kingdom;
            var targetKingdom = item.Faction2 as Kingdom;
            if (playerKingdom == null || targetKingdom == null) return true;
            if (peaceBehavior.WantsPeace(targetKingdom, playerKingdom)) return true;

            InformationManager.DisplayMessage(new InformationMessage($"{targetKingdom.Name} is not interested in peace at this time.", Colors.Red));
            return false;
        }
    }
}