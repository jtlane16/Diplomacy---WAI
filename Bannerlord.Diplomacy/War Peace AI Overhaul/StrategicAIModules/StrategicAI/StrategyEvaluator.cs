using Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI;

using System;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

using MathF = TaleWorlds.Library.MathF;

namespace WarAndAiTweaks.WarPeaceAI
{
    /// <summary>
    /// Calculates daily stance changes based on core factors only
    /// </summary>
    public static class StrategyEvaluator
    {
        public static float CalculateStanceChange(Kingdom self, Kingdom target)
        {
            if (self == null || target == null || self == target) return 0f;

            float change = 0f;
            bool atWar = self.IsAtWarWith(target);

            // 1. MILITARY FACTORS (±3 points) - Core strength comparison
            change += EvaluateMilitaryFactors(self, target);

            // 2. GEOGRAPHIC FACTORS (±2 points) - Simplified proximity
            change += EvaluateGeographicFactors(self, target);

            // 3. WAR/PEACE MOMENTUM (±15 points) - Core objectives #3, #4
            change += EvaluateWarPeaceMomentum(self, target, atWar);

            // 4. DECISION COMMITMENT (±6 points) - Core objective #6  
            change += EvaluateDecisionCommitment(self, target, atWar);

            // 5. COALITION PRESSURE (±10 points) - Supports objective #2
            change += CoalitionSystem.GetCoalitionStanceAdjustment(self, target);

            // 6. SAFEGUARDS (±30 points) - Supports objectives #6, #9
            change += StrategicSafeguards.GetSafeguardStanceAdjustment(self, target);

            return change;
        }

        private static float EvaluateMilitaryFactors(Kingdom self, Kingdom target)
        {
            float change = 0f;

            // Simple relative strength comparison
            float powerRatio = self.TotalStrength / Math.Max(target.TotalStrength, 1f);

            if (powerRatio > 1.3f)
                change += 2f; // Feeling strong, more aggressive
            else if (powerRatio < 0.7f)
                change -= 2f; // Feeling weak, less aggressive

            // Multiple wars penalty for self
            int selfWars = KingdomLogicHelpers.GetEnemyKingdoms(self).Count;
            if (selfWars > 1)
                change -= 2f; // Supports objective #9

            // Vulnerable target bonus
            int targetWars = KingdomLogicHelpers.GetEnemyKingdoms(target).Count;
            if (targetWars >= 2)
                change += 1f; // Target is distracted

            return MathF.Clamp(change, -3f, 3f);
        }

        private static float EvaluateGeographicFactors(Kingdom self, Kingdom target)
        {
            if (KingdomLogicHelpers.AreBordering(self, target))
                return 1f;

            float distance = GetKingdomDistance(self, target);
            if (distance > 200f)
            {
                // Penalty scales from -1 at 200 to -4 at 400 (adjust as needed)
                float penalty = -1f - ((distance - 200f) / 200f) * 3f;
                return MathF.Clamp(penalty, -4f, -1f);
            }

            return 0f;
        }

        private static float GetKingdomDistance(Kingdom self, Kingdom target)
        {
            var posA = self.FactionMidSettlement?.Position2D ?? Vec2.Zero;
            var posB = target.FactionMidSettlement?.Position2D ?? Vec2.Zero;
            return posA.Distance(posB);
        }

        private static float EvaluateWarPeaceMomentum(Kingdom self, Kingdom target, bool atWar)
        {
            float change = 0f;

            if (atWar)
            {
                var warDuration = GetWarDuration(self, target);

                // Natural gravity toward 50 days (was 30)
                if (warDuration < 5) change += 2f;
                else if (warDuration < 15) change += 1f;
                else if (warDuration > 35) change -= 6f;  // Strong pull toward peace (was 20)
                else if (warDuration > 50) change -= 12f; // Very strong after 50 days (was 30)
            }
            else
            {
                var peaceDuration = GetPeaceDuration(self, target);

                // Natural gravity toward 50 days (was 30)
                if (peaceDuration < 10) change -= 2f;
                else if (peaceDuration < 20) change -= 1f;
                else if (peaceDuration > 40) change += 4f;  // Build tension (was 25)
                else if (peaceDuration > 50) change += 8f;  // Strong tension after 50 days (was 30)
            }

            return MathF.Clamp(change, -15f, 15f);
        }

        private static float EvaluateDecisionCommitment(Kingdom self, Kingdom target, bool atWar)
        {
            float change = 0f;

            if (atWar)
            {
                var stance = self.GetStanceWith(target);
                if (stance?.IsAtWar == true)
                {
                    float warDuration = (float) (CampaignTime.Now - stance.WarStartDate).ToDays;

                    // Commitment tapers off to allow natural momentum (objective #6)
                    if (warDuration <= 10)
                        change += MathF.Lerp(6f, 3f, warDuration / 10f);
                    else if (warDuration <= 20)
                        change += MathF.Lerp(3f, 1f, (warDuration - 10f) / 10f);
                    else if (warDuration <= 30)
                        change += MathF.Lerp(1f, 0f, (warDuration - 20f) / 10f);
                    // After 30 days: No commitment bonus
                }
            }
            else
            {
                var stance = self.GetStanceWith(target);
                if (stance?.IsAtWar == false)
                {
                    float peaceDuration = (float) (CampaignTime.Now - stance.PeaceDeclarationDate).ToDays;

                    // Same pattern for peace
                    if (peaceDuration <= 10)
                        change -= MathF.Lerp(6f, 3f, peaceDuration / 10f);
                    else if (peaceDuration <= 20)
                        change -= MathF.Lerp(3f, 1f, (peaceDuration - 10f) / 10f);
                    else if (peaceDuration <= 30)
                        change -= MathF.Lerp(1f, 0f, (peaceDuration - 20f) / 10f);
                }
            }

            return change;
        }

        private static float GetWarDuration(Kingdom self, Kingdom target)
        {
            var stance = self.GetStanceWith(target);
            if (stance == null || !stance.IsAtWar) return 0f;
            return (float) (CampaignTime.Now - stance.WarStartDate).ToDays;
        }

        private static float GetPeaceDuration(Kingdom self, Kingdom target)
        {
            var stance = self.GetStanceWith(target);
            if (stance == null || stance.IsAtWar) return 0f;
            return (float) (CampaignTime.Now - stance.PeaceDeclarationDate).ToDays;
        }
        public static string GetWarReason(Kingdom self, Kingdom target)
        {
            float military = EvaluateMilitaryFactors(self, target);
            float geo = EvaluateGeographicFactors(self, target);
            float momentum = EvaluateWarPeaceMomentum(self, target, false);
            float commitment = EvaluateDecisionCommitment(self, target, false);
            float coalition = CoalitionSystem.GetCoalitionStanceAdjustment(self, target);
            float safeguard = StrategicSafeguards.GetSafeguardStanceAdjustment(self, target);

            float[] values = { military, geo, momentum, commitment, coalition, safeguard };
            string[] reasons =
            {
                // MILITARY
                military > 0
                    ? $"Town criers proclaim that {self.Name} now marches against {target.Name}, certain their banners gather the stronger host."
                    : $"Low whispers seep through taverns that {self.Name} strikes first against {target.Name}, alarmed by the rival’s swelling ranks.",

                // GEOGRAPHY
                geo > 0
                    ? $"Swift riders report blood spilled along the marches, and {self.Name} sets upon {target.Name} over disputed borderlands."
                    : $"Wayfarers marvel as {self.Name} reaches beyond distant horizons to bring war upon far-flung {target.Name}.",

                // MOMENTUM
                momentum > 0
                    ? $"Rumour holds that brittle truces have frayed, and {self.Name} once more lifts the sword against {target.Name}."
                    : $"Though the treaty’s ink is scarcely dry, {self.Name} has already cried for war upon {target.Name}.",

                // COMMITMENT
                commitment > 0
                    ? $"Envoys announce that {self.Name}, hungry for new dominions, declares war upon {target.Name} with stern resolve."
                    : $"Court scribes observe that {self.Name}, heavy-hearted yet compelled, takes up arms against {target.Name}.",

                // COALITION
                coalition > 0
                    ? $"Heralds cry that sworn allies press {self.Name} to stand with them against the power of {target.Name}."
                    : $"It is whispered that tangled pacts draw {self.Name} into war with {target.Name}.",

                // SAFEGUARD
                safeguard < 0
                    ? $"Counsel once urged restraint yet {self.Name} now casts caution aside and calls for war upon {target.Name}."
                    : $""
            };

            int idx = 0;
            float max = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                if (Math.Abs(values[i]) > Math.Abs(max))
                {
                    max = values[i];
                    idx = i;
                }
            }
            return reasons[idx];
        }

        public static string GetPeaceReason(Kingdom self, Kingdom target)
        {
            float military = EvaluateMilitaryFactors(self, target);
            float geo = EvaluateGeographicFactors(self, target);
            float momentum = EvaluateWarPeaceMomentum(self, target, true);
            float commitment = EvaluateDecisionCommitment(self, target, true);
            float coalition = CoalitionSystem.GetCoalitionStanceAdjustment(self, target);
            float safeguard = StrategicSafeguards.GetSafeguardStanceAdjustment(self, target);

            float[] values = { military, geo, momentum, commitment, coalition, safeguard };
            string[] reasons =
            {
                // MILITARY
                military < 0
                    ? $"Word reaches the realm that {self.Name}, mindful of {target.Name}’s iron-clad legions, has bowed to prudence and made peace."
                    : $"Minstrels sing that {self.Name}, stout of heart and strong of arm, has nonetheless offered the olive branch to {target.Name}.",

                // GEOGRAPHY
                geo < 0
                    ? $"Couriers tell how the perilous roads between {self.Name} and {target.Name} cooled the bloodlust, and peace now binds the two realms."
                    : $"Border steads ring with cheer, for {self.Name} and {target.Name} have at last laid down their arms.",

                // MOMENTUM
                momentum < 0
                    ? $"Every hearth echoes with tales of weary spearmen and empty granaries; thus {self.Name} has gladly welcomed peace with {target.Name}."
                    : $"Steel rang long enough, and {self.Name} has now brought the struggle with {target.Name} to a swift close.",

                // COMMITMENT
                commitment < 0
                    ? $"Envoys recount how {self.Name} stood firm in seeking concord and at last secured peace with {target.Name}."
                    : $"Rumour holds that {self.Name} wavered, yet the scrolls of peace now bear the seals of both {self.Name} and {target.Name}.",

                // COALITION
                coalition < 0
                    ? $"Heralds declare that loyal allies urged {self.Name} to sheathe the sword, and peace is duly sworn with {target.Name}."
                    : $"Though comrades urged the war to linger, {self.Name} has chosen the path of peace with {target.Name}.",

                // SAFEGUARD
                safeguard < 0
                    ? $"Royal sages persuaded {self.Name} to set aside hostilities, and so peace now holds with {target.Name}."
                    : $""
            };

            int idx = 0;
            float max = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                if (Math.Abs(values[i]) > Math.Abs(max))
                {
                    max = values[i];
                    idx = i;
                }
            }
            return reasons[idx];
        }


        public static string GetPeaceRejectionReason(Kingdom requester, Kingdom rejecter)
        {
            // Narrative motive for seeking peace
            string peaceReason = GetPeaceReason(requester, rejecter);

            // Main cause for rejection
            float stance = Campaign.Current
                                .GetCampaignBehavior<WarAndAiTweaks.WarPeaceAI.KingdomLogicController>()?
                                .GetKingdomStance(rejecter, requester) ?? 50f;
            float military = CalculateStanceChange(rejecter, requester);
            bool tooSoon = StrategicSafeguards.IsDecisionTooSoon(rejecter, requester, false);

            string rejectionReason;
            if (stance > KingdomStrategy.NEUTRAL_THRESHOLD)
                rejectionReason = $"{rejecter.Name} still nurses old grudges and will not yet be mollified.";
            else if (military > 0)
                rejectionReason = $"{rejecter.Name} is flushed with confidence in their muster of arms.";
            else if (tooSoon)
                rejectionReason = $"the wounds of war are yet raw within the halls of {rejecter.Name}.";
            else
                rejectionReason = $"{rejecter.Name} sees scant merit in laying down arms this day.";

            // Compose the final chronicle
            return $"Thus the chroniclers write that {requester.Name} sought peace with {rejecter.Name} because {peaceReason.TrimEnd('.')}, yet {rejectionReason}";
        }

    }
}