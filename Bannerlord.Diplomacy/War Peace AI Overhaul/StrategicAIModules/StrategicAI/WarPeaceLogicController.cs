using Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.SaveSystem;

namespace WarAndAiTweaks.WarPeaceAI
{
    public class WarPeaceLogicController : CampaignBehaviorBase
    {
        public const int PeaceScoreThreshold = 90;
        public const int WarScoreThreshold = 120;

        // War commitment trackers and constants as saveable fields
        [SaveableField(1)]
        public Dictionary<string, float> WarWeariness = new();

        [SaveableField(2)]
        public Dictionary<string, float> WarEagerness = new();

        [SaveableField(3)]
        public float WarWearinessMax = 100f;

        [SaveableField(4)]
        public float WarWearinessStep = 3f;

        [SaveableField(5)]
        public float WarEagernessMax = 100f;

        [SaveableField(6)]
        public float WarEagernessStep = 5f;

        // Key: (AI Kingdom StringId, Player Kingdom StringId), Value: Last request time
        internal static readonly Dictionary<(string, string), CampaignTime> PeaceRequestCooldowns = new();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, DailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("WarWeariness", ref WarWeariness);
            dataStore.SyncData("WarEagerness", ref WarEagerness);
            dataStore.SyncData("WarWearinessMax", ref WarWearinessMax);
            dataStore.SyncData("WarWearinessStep", ref WarWearinessStep);
            dataStore.SyncData("WarEagernessMax", ref WarEagernessMax);
            dataStore.SyncData("WarEagernessStep", ref WarEagernessStep);

            // PeaceRequestCooldowns sync logic
            List<string> keys = null;
            List<double> values = null;
            if (dataStore.IsSaving)
            {
                keys = PeaceRequestCooldowns.Keys.Select(k => $"{k.Item1}|{k.Item2}").ToList();
                values = PeaceRequestCooldowns.Values.Select(v => v.ToHours).ToList();
            }
            dataStore.SyncData("WarPeace_PeaceRequestCooldownKeys", ref keys);
            dataStore.SyncData("WarPeace_PeaceRequestCooldownValues", ref values);
            if (dataStore.IsLoading && keys != null && values != null && keys.Count == values.Count)
            {
                PeaceRequestCooldowns.Clear();
                for (int i = 0; i < keys.Count; i++)
                {
                    var parts = keys[i].Split('|');
                    if (parts.Length == 2)
                    {
                        var key = (parts[0], parts[1]);
                        var time = CampaignTime.Hours((float) values[i]);
                        PeaceRequestCooldowns[key] = time;
                    }
                }
            }

            // Ensure initialization if loading and values are not set (fallbacks)
            if (dataStore.IsLoading)
            {
                if (WarWearinessMax <= 0f) WarWearinessMax = 50f;
                if (WarWearinessStep <= 0f) WarWearinessStep = 1f;
                if (WarEagernessMax <= 0f) WarEagernessMax = 50f;
                if (WarEagernessStep <= 0f) WarEagernessStep = 1f;
                if (WarWeariness == null) WarWeariness = new Dictionary<string, float>();
                if (WarEagerness == null) WarEagerness = new Dictionary<string, float>();
            }
        }

        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI.WarPeaceLogger.Clear();
        }

        private void DailyTick()
        {
            Diplomacy.War_Peace_AI_Overhaul.StrategicAIModules.StrategicAI.WarPeaceLogicHelpers.UpdateAllKingdomWarStates(this);

            // Get all valid AI kingdoms (exclude only if player is ruler)
            var aiKingdoms = Kingdom.All
                .Where(k => k != null
                    && !k.IsEliminated
                    && !k.IsMinorFaction
                    && k.Leader != null
                    && k.Leader != Hero.MainHero)
                .ToList();

            int kingdomCount = aiKingdoms.Count;
            if (kingdomCount == 0)
                return;

            // Pick a random number of kingdoms (at least 1)
            int numToThink = MBRandom.RandomInt(1, kingdomCount + 1); // upper bound exclusive

            // Shuffle and pick that many
            var thinkingKingdoms = aiKingdoms.OrderBy(_ => MBRandom.RandomFloat).Take(numToThink).ToList();

            foreach (var selectedKingdom in thinkingKingdoms)
            {
                // Gather info for logging
                var enemies = WarPeaceLogicHelpers.GetEnemyKingdoms(selectedKingdom)
                    .Where(k => k.Leader != Hero.MainHero)
                    .ToList();

                if (Clan.PlayerClan?.Kingdom != null
                    && Clan.PlayerClan.Kingdom != selectedKingdom
                    && selectedKingdom.IsAtWarWith(Clan.PlayerClan.Kingdom))
                {
                    enemies.Add(Clan.PlayerClan.Kingdom);
                }

                // Log kingdom state
                WarPeaceLogicHelpers.LogKingdomThinking(selectedKingdom, enemies);

                if (enemies.Count > 0)
                {
                    var sortedEnemies = enemies
                        .Select(enemy => new
                        {
                            Enemy = enemy,
                            PeaceReasonA = WarPeaceLogicHelpers.GetPeaceDecisionReason(selectedKingdom, enemy),
                            PeaceReasonB = enemy.Leader == Hero.MainHero ? default : WarPeaceLogicHelpers.GetPeaceDecisionReason(enemy, selectedKingdom)
                        })
                        .OrderByDescending(x => x.PeaceReasonA.Score)
                        .ToList();

                    foreach (var enemyInfo in sortedEnemies)
                    {
                        float scoreA = enemyInfo.PeaceReasonA.Score;
                        float scoreB = enemyInfo.Enemy.Leader == Hero.MainHero ? 0 : enemyInfo.PeaceReasonB.Score;
                        var enemy = enemyInfo.Enemy;

                        if (enemy.Leader == Hero.MainHero)
                        {
                            if (scoreA >= PeaceScoreThreshold)
                            {
                                WarPeaceLogicHelpers.SendAIRequestToPlayerKingdom(selectedKingdom, enemy, "peace", scoreA);
                            }
                        }
                        else
                        {
                            if (scoreA >= PeaceScoreThreshold && scoreB >= PeaceScoreThreshold)
                            {
                                int dailyTribute = WarPeaceLogicHelpers.GetPeaceTribute(
                                    selectedKingdom.Leader.Clan,
                                    enemy.Leader.Clan,
                                    selectedKingdom,
                                    enemy
                                );
                                MakePeaceAction.Apply(selectedKingdom, enemy, dailyTribute);
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{selectedKingdom.Name} made peace with {enemy.Name}. {enemyInfo.PeaceReasonA.Reasons.FirstOrDefault()}",
                                    Colors.Green
                                ));
                            }
                        }
                    }
                }
                else
                {
                    var warTarget = WarScoring.GetBestWarTarget(selectedKingdom);
                    var warReason = WarPeaceLogicHelpers.GetWarDecisionReason(selectedKingdom, warTarget.kingdom);
                    if (warTarget.score > WarScoreThreshold)
                    {
                        DeclareWarAction.ApplyByDefault(selectedKingdom, warTarget.kingdom);
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{selectedKingdom.Name} declared war on {warTarget.kingdom.Name}. {warReason.Reasons.FirstOrDefault()}",
                            Colors.Red
                        ));
                    }
                }
            }
        }
    }
}