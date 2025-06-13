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
        public float ConquestScore { get; set; }
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

        private Dictionary<string, CampaignTime> _lastDiplomaticEvaluation = new Dictionary<string, CampaignTime>();

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
                // Refactored logic to get the breakdown first for logging
                bool isDesperate = IsDesperateForPeace(us, enemy);
                float peaceThreshold = isDesperate ? 40f : 70f;
                PeaceScoreBreakdown breakdown = EvaluatePeaceDesire(us, enemy);
                bool wantsPeace = breakdown.FinalScore > peaceThreshold;

                // Log every evaluation
                DiplomacyLogHelper.LogPeaceEvaluation(us, breakdown, peaceThreshold, wantsPeace);

                if (wantsPeace)
                {
                    if (enemy.Leader != null && enemy.Leader.IsHumanPlayerCharacter)
                    {
                        KingdomPeaceAction.ApplyPeace(us, enemy);
                        return true;
                    }
                    else
                    {
                        // Check if the other kingdom also wants peace
                        PeaceScoreBreakdown enemyBreakdown = EvaluatePeaceDesire(enemy, us);
                        bool enemyWantsPeace = enemyBreakdown.FinalScore > (IsDesperateForPeace(enemy, us) ? 40f : 70f);
                        if (enemyWantsPeace)
                        {
                            string reason = DiplomacyHelpers.GeneratePeaceReasoning(us, breakdown);
                            InformationManager.DisplayMessage(new InformationMessage(reason, Colors.Green));
                            MakePeaceAction.Apply(us, enemy);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Calculates the peace desire score and its components.
        /// </summary>
        private PeaceScoreBreakdown EvaluatePeaceDesire(Kingdom us, Kingdom them)
        {
            var breakdown = new PeaceScoreBreakdown(them);

            float warProgress = GetWarProgress(us, them);
            float exhaustion = WarExhaustionManager.Instance?.GetWarExhaustion(us, them) ?? 0f;
            float personalityModifier = (us.Leader?.GetTraitLevel(DefaultTraits.Honor) ?? 0) * 10f;
            float conquerorsResolve = (us.Leader?.GetTraitLevel(DefaultTraits.Valor) ?? 0) * -10f;
            float economicPressure = DiplomacyHelpers.CalculateEconomicBoost(us, true) * -40f;

            breakdown.ExhaustionScore = exhaustion;
            // Simplified Danger score for logging
            breakdown.DangerScore = -warProgress + personalityModifier + economicPressure + conquerorsResolve;
            breakdown.FinalScore = breakdown.ExhaustionScore + breakdown.DangerScore;

            // Adding tribute factor to breakdown but not final score yet, as it's complex
            var tempPeaceDecision = new MakePeaceKingdomDecision(us.RulingClan, them);
            breakdown.TributeAmount = tempPeaceDecision.DailyTributeToBePaid;
            breakdown.TributeFactor = breakdown.TributeAmount * -0.01f;
            // The final decision might need to weigh tribute separately
            // For now, logging will show its potential impact
            // finalScore += tributeFactor;

            return breakdown;
        }

        /// <summary>
        /// Determines if a kingdom is in a desperate situation in a war.
        /// </summary>
        private bool IsDesperateForPeace(Kingdom us, Kingdom them)
        {
            float warProgress = GetWarProgress(us, them);
            float exhaustion = WarExhaustionManager.Instance?.GetWarExhaustion(us, them) ?? 0f;
            return warProgress < -30 || exhaustion > 80;
        }


        /// <summary>
        /// AI evaluates potential targets and decides if a new war is strategically advantageous.
        /// </summary>
        private void ConsiderDeclaringWar(Kingdom us)
        {
            var potentialTargets = DiplomacyHelpers.MajorKingdoms()
                .Where(them =>
                {
                    if (them == us || FactionManager.IsAlliedWithFaction(us, them))
                        return false;

                    // LOGGING: Check each condition individually to find the problem
                    if (!DeclareWarConditions.Instance.CanApply(us, them, out var failedReason))
                    {
                        DiplomacyLogHelper.LogWarConditionCheck(us, them, false, failedReason.ToString());
                        return false;
                    }
                    DiplomacyLogHelper.LogWarConditionCheck(us, them, true, "All conditions met");
                    return true;
                })
                .ToList();

            if (!potentialTargets.Any()) return;

            WarScoreBreakdown? bestTargetBreakdown = null;
            float bestScore = float.MinValue;

            foreach (var target in potentialTargets)
            {
                var breakdown = DiplomacyHelpers.ComputeWarDesireScore(us, target);
                if (breakdown.FinalScore > bestScore)
                {
                    bestScore = breakdown.FinalScore;
                    bestTargetBreakdown = breakdown;
                }
            }

            // NEW: Calculate War Urgency
            int daysAtPeace = DiplomacyBehavior.Instance.GetDaysAtPeace(us);
            // Lower the threshold by 1 for each day at peace, capping at 30 days.
            float urgencyDiscount = Math.Min(daysAtPeace, 30);

            // Original Threshold
            float baseWarThreshold = 60f - (us.Leader?.GetTraitLevel(DefaultTraits.Valor) * 15f ?? 0f);

            // Modified Threshold
            float finalWarThreshold = baseWarThreshold - urgencyDiscount;

            // Log the evaluation with the new threshold
            if (bestTargetBreakdown != null)
            {
                // Make sure to pass the 'urgencyDiscount' to the logging function
                DiplomacyLogHelper.LogWarEvaluation(us, bestTargetBreakdown, finalWarThreshold, urgencyDiscount);
            }

            if (bestTargetBreakdown != null && bestScore > finalWarThreshold)
            {
                DeclareWarAction.ApplyByDefault(us, bestTargetBreakdown.Target);
                string reason = DiplomacyHelpers.GenerateWarReasoning(us, bestTargetBreakdown);
                InformationManager.DisplayMessage(new InformationMessage(reason, Colors.Red));
            }
        }

        /// <summary>
        /// A simple metric to determine how a war is going. Positive is good for "us", negative is bad.
        /// </summary>
        private float GetWarProgress(Kingdom us, Kingdom them)
        {
            StanceLink? stance = us.GetStanceWith(them);
            if (stance == null) return 0f;

            float ourCasualties = stance.GetCasualties(us);
            float theirCasualties = stance.GetCasualties(them);
            float casualtyScore = (theirCasualties > 0 || ourCasualties > 0) ? ((theirCasualties - ourCasualties) / Math.Max(1f, theirCasualties + ourCasualties)) * 50f : 0f;

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
        // We need to get an instance of the behavior to call its public method
        private static bool WantsPeace(Kingdom us, Kingdom them)
        {
            // This is a simplified version of the logic in StrategicAIBehavior for the player's perspective.
            // You might want to expose the main behavior's method publicly if more complexity is needed.
            var warProgress = (us.GetStanceWith(them)?.GetSuccessfulSieges(us) ?? 0) - (us.GetStanceWith(them)?.GetSuccessfulSieges(them) ?? 0);
            var exhaustion = WarExhaustionManager.Instance?.GetWarExhaustion(us, them) ?? 0f;
            return (exhaustion - (warProgress * 10)) > 60f; // Simplified threshold
        }

        public static bool Prefix(KingdomWarItemVM item)
        {
            var playerKingdom = Hero.MainHero.Clan.Kingdom;
            var targetKingdom = item.Faction2 as Kingdom;
            if (playerKingdom == null || targetKingdom == null) return true;

            // We check if the AI kingdom wants peace with the player kingdom.
            if (WantsPeace(targetKingdom, playerKingdom))
            {
                return true;
            }

            InformationManager.DisplayMessage(new InformationMessage($"{targetKingdom.Name} is not interested in peace at this time.", Colors.Red));
            return false;
        }
    }
}
