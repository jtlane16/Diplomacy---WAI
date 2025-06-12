using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace WarAndAiTweaks
{
    #region Data Structures
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

    public class WarDesireBehavior : CampaignBehaviorBase
    {
        #region Fields & Constants
        private const float DESIRE_GAIN_AT_PEACE = 0.6f;
        private const float DESIRE_LOSS_AT_WAR = -1f;
        private const float EXH_GAIN_AT_WAR = 1f;
        private const float EXH_DECAY_AT_PEACE = -0.5f;

        private Dictionary<string, float> _desire = new Dictionary<string, float>();
        private Dictionary<string, float> _bias = new Dictionary<string, float>();
        private Dictionary<string, int> _period = new Dictionary<string, int>();
        private Dictionary<string, int> _timer = new Dictionary<string, int>();
        private Dictionary<string, Dictionary<string, float>> _perWarExh = new Dictionary<string, Dictionary<string, float>>();
        private int _daysEveryoneAtPeace = 0;
        #endregion

        #region Core Loop & Events
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, DailyClanTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, DailyGlobalTick);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnFiefSwing);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
        }

        public override void SyncData(IDataStore store)
        {
            store.SyncData("_desire", ref _desire);
            store.SyncData("_bias", ref _bias);
            store.SyncData("_period", ref _period);
            store.SyncData("_timer", ref _timer);
            store.SyncData("_perWarExh", ref _perWarExh);
            store.SyncData("_daysEveryoneAtPeace", ref _daysEveryoneAtPeace);
        }

        private void DailyClanTick(Clan clan)
        {
            if (clan == Clan.PlayerClan || clan.Kingdom == null || clan.Kingdom.RulingClan != clan) return;
            if (clan.Kingdom.UnresolvedDecisions.Any(d => d is DeclareWarDecision || d is MakePeaceKingdomDecision)) return;

            var kingdom = clan.Kingdom;
            string id = kingdom.StringId;
            Ensure(id);

            bool atWar = DiplomacyHelpers.MajorEnemies(kingdom).Any();
            UpdateWarExhaustion(kingdom, id, atWar);
            UpdateWarDesireAndTimer(kingdom, id, atWar);

            float avgExhaustion = ComputeAverageExhaustion(kingdom, id);
            PeaceDesireBehavior.SetExhaustion(id, avgExhaustion);

            TryDeclareWar(kingdom);
        }

        private void DailyGlobalTick()
        {
            bool anyWar = DiplomacyHelpers.MajorKingdoms().Any(a => DiplomacyHelpers.MajorEnemies(a).Any());
            _daysEveryoneAtPeace = anyWar ? 0 : _daysEveryoneAtPeace + 1;
        }

        private void OnFiefSwing(Settlement s, bool _, Hero newOwner, Hero oldOwner, Hero __, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail ____)
        {
            Kingdom winner = newOwner?.Clan?.Kingdom, loser = oldOwner?.Clan?.Kingdom;
            if (winner == null || loser == null || winner == loser) return;
            Bump(winner.StringId, +2f);
            PeaceDesireBehavior.BumpDesire(loser.StringId, +8f);
        }

        private void OnWarDeclared(IFaction a, IFaction b, DeclareWarAction.DeclareWarDetail _)
        {
            if (!(a is Kingdom k1) || !(b is Kingdom k2)) return;
            _daysEveryoneAtPeace = 0;
            Ensure(k1.StringId);
            Ensure(k2.StringId);
            _desire[k1.StringId] = 0f;
            _desire[k2.StringId] = 0f;
            if (!_perWarExh[k1.StringId].ContainsKey(k2.StringId)) _perWarExh[k1.StringId][k2.StringId] = 0f;
            if (!_perWarExh[k2.StringId].ContainsKey(k1.StringId)) _perWarExh[k2.StringId][k1.StringId] = 0f;
            _perWarExh[k1.StringId][k2.StringId] = 5f;
            _perWarExh[k2.StringId][k1.StringId] = 5f;
            PeaceDesireBehavior.NotifyWarStarted(k1.StringId);
            PeaceDesireBehavior.NotifyWarStarted(k2.StringId);
        }
        #endregion

        #region AI Action Triggers
        private void TryDeclareWar(Kingdom k)
        {
            string id = k.StringId;
            float threshold = DiplomacyHelpers.ComputeDynamicWarThreshold(k);
            if (_timer[id] < _period[id] || _desire[id] < threshold) return;

            _timer[id] = 0;
            _period[id] = MBRandom.RandomInt(5, 10);

            var candidates = DiplomacyHelpers.MajorKingdoms().Where(o => o != k && !DiplomacyHelpers.MajorEnemies(k).Contains(o));
            var scored = candidates.Select(o => DiplomacyHelpers.ComputeWarDesireScore(k, o)).Where(x => x.FinalScore > threshold).OrderByDescending(x => x.FinalScore).ToList();

            if (scored.Any())
            {
                var winningChoice = scored.First();
                string reason = DiplomacyHelpers.GenerateWarReasoning(k, winningChoice);
                InformationManager.DisplayMessage(new InformationMessage(reason, Colors.Red));
                DeclareWarAction.ApplyByDefault(k, winningChoice.Target);
            }
        }
        #endregion

        #region Internal State Management
        private void Ensure(string id)
        {
            if (!_desire.ContainsKey(id)) _desire[id] = MBRandom.RandomFloatRanged(0f, DiplomacyHelpers.ComputeDynamicWarThreshold(Kingdom.All.First(k => k.StringId == id)) * 0.4f);
            if (!_bias.ContainsKey(id)) _bias[id] = MBRandom.RandomFloatRanged(0.8f, 1.2f);
            if (!_period.ContainsKey(id)) _period[id] = MBRandom.RandomInt(5, 10);
            if (!_timer.ContainsKey(id)) _timer[id] = MBRandom.RandomInt(0, _period[id]);
            if (!_perWarExh.ContainsKey(id)) _perWarExh[id] = new Dictionary<string, float>();
        }

        private void UpdateWarDesireAndTimer(Kingdom k, string id, bool atWar)
        {
            float delta = (atWar ? DESIRE_LOSS_AT_WAR : DESIRE_GAIN_AT_PEACE) * _bias[id] + DiplomacyHelpers.CalculateEconomicBoost(k, atWar) + MBRandom.RandomFloatRanged(-0.1f, 0.1f);
            _desire[id] = MBMath.ClampFloat(_desire[id] + delta, 0f, 100f);
            _timer[id]++;
        }

        private void UpdateWarExhaustion(Kingdom k, string id, bool atWar)
        {
            if (!_perWarExh.TryGetValue(id, out var exhaustionDict))
            {
                exhaustionDict = new Dictionary<string, float>();
                _perWarExh[id] = exhaustionDict;
            }

            var relevantKingdoms = DiplomacyHelpers.MajorKingdoms().Where(other => other != k && (DiplomacyHelpers.MajorEnemies(k).Contains(other) || DiplomacyHelpers.AreNeighbors(k, other)));
            foreach (var other in relevantKingdoms)
            {
                exhaustionDict.TryGetValue(other.StringId, out float currentExhaustion);
                float delta = DiplomacyHelpers.MajorEnemies(k).Contains(other) ? EXH_GAIN_AT_WAR : EXH_DECAY_AT_PEACE;
                exhaustionDict[other.StringId] = MBMath.ClampFloat(currentExhaustion + delta, 0f, 100f);
            }
        }

        private float ComputeAverageExhaustion(Kingdom k, string id) => DiplomacyHelpers.MajorEnemies(k).Select(o => _perWarExh[id].TryGetValue(o.StringId, out var ex) ? ex : 0f).DefaultIfEmpty(0f).Average();
        private void Bump(string id, float delta) { if (_desire.ContainsKey(id)) _desire[id] = MBMath.ClampFloat(_desire[id] + delta, 0f, 100f); }
        public float GetExhaustionForWar(Kingdom k1, Kingdom k2) => _perWarExh.TryGetValue(k1.StringId, out var d) && d.TryGetValue(k2.StringId, out float e) ? e : 0f;
        #endregion
    }

    public class PeaceDesireBehavior : CampaignBehaviorBase
    {
        #region Fields & Constants
        private const float BASE_GROWTH = 0.5f, EXH_BOOST = 0.2f, THRESHOLD_BASE = 55f, THRESHOLD_PER_ENEMY = 5f, THRESHOLD_MIN = 30f;
        private static readonly Dictionary<string, float> _externExh = new Dictionary<string, float>();
        private Dictionary<string, float> _desire = new Dictionary<string, float>();
        private Dictionary<string, int> _period = new Dictionary<string, int>(), _timer = new Dictionary<string, int>();
        private static PeaceDesireBehavior _inst;
        #endregion

        public PeaceDesireBehavior() { _inst = this; }

        #region Core Loop & Events
        public override void RegisterEvents() { CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, DailyClanTick); }
        public override void SyncData(IDataStore s) { s.SyncData("_desire", ref _desire); s.SyncData("_period", ref _period); s.SyncData("_timer", ref _timer); }

        private void DailyClanTick(Clan clan)
        {
            if (clan.Kingdom == null || clan.Kingdom.RulingClan != clan || clan == Clan.PlayerClan) return;
            var us = clan.Kingdom;
            var enemies = DiplomacyHelpers.MajorEnemies(us).ToList();
            if (!enemies.Any()) { if (_desire.ContainsKey(us.StringId)) { _desire[us.StringId] = Math.Max(0f, _desire[us.StringId] - 1f); } return; }
            if (us.UnresolvedDecisions.Any(d => d is MakePeaceKingdomDecision)) return;

            Ensure(us.StringId);
            UpdatePeaceDesire(us, enemies);

            if (_timer[us.StringId] < _period[us.StringId] || _desire[us.StringId] < GetPeaceThreshold(us)) return;

            _timer[us.StringId] = 0;
            _period[us.StringId] = MBRandom.RandomInt(5, 10);

            var warDesireBehavior = Campaign.Current.GetCampaignBehavior<WarDesireBehavior>();
            if (warDesireBehavior == null) return;

            var scoredPeaceCandidates = enemies.Select(e => DiplomacyHelpers.ComputePeaceScore(us, e, warDesireBehavior.GetExhaustionForWar(us, e))).OrderByDescending(s => s.FinalScore).ToList();
            if (!scoredPeaceCandidates.Any()) return;

            var winningChoice = scoredPeaceCandidates.First();
            string reason = DiplomacyHelpers.GeneratePeaceReasoning(us, winningChoice);
            InformationManager.DisplayMessage(new InformationMessage(reason, Colors.Green));

            // [REMOVED] Inquiry for player. Now directly applies peace.
            MakePeaceAction.ApplyByKingdomDecision(us, winningChoice.Target, winningChoice.TributeAmount);

            _desire[us.StringId] *= (1.0f - (1.0f / (enemies.Count + 1.0f)));
        }
        #endregion

        #region Public API & State Accessors
        public static void NotifyWarStarted(string id) { if (_inst == null) return; _inst.Ensure(id); _inst._timer[id] = 0; _inst._period[id] = MBRandom.RandomInt(5, 10); _inst._desire[id] = 0f; }
        public static void SetExhaustion(string kid, float v) => _externExh[kid] = v;
        public static void BumpDesire(string kid, float d) { if (_inst?._desire.ContainsKey(kid) ?? false) _inst._desire[kid] = MBMath.ClampFloat(_inst._desire[kid] + d, 0f, 100f); }
        public bool IsPeaceProposalAcceptable(Kingdom recipient, Kingdom proposer)
        {
            if (recipient == null || proposer == null || !FactionManager.IsAtWarAgainstFaction(recipient, proposer)) return false;
            Ensure(recipient.StringId);
            return _desire[recipient.StringId] >= GetPeaceThreshold(recipient);
        }
        #endregion

        #region Internal State Management
        private void Ensure(string id) { if (!_desire.ContainsKey(id)) _desire[id] = MBRandom.RandomFloatRanged(0f, THRESHOLD_BASE * 0.4f); if (!_period.ContainsKey(id)) _period[id] = MBRandom.RandomInt(5, 10); if (!_timer.ContainsKey(id)) _timer[id] = MBRandom.RandomInt(0, _period[id]); }
        private float GetPeaceThreshold(Kingdom k) => Math.Max(THRESHOLD_BASE - (DiplomacyHelpers.MajorEnemies(k).Count() - 1) * THRESHOLD_PER_ENEMY, THRESHOLD_MIN);

        private void UpdatePeaceDesire(Kingdom us, List<Kingdom> enemies)
        {
            string id = us.StringId;
            float exhaustion = _externExh.TryGetValue(id, out var exh) ? exh : 0f;
            float delta = (BASE_GROWTH * (float)Math.Pow(1.5, enemies.Count - 1)) + (exhaustion / 100f * EXH_BOOST);

            // [REMOVED] Alliance strength calculation
            _desire[id] = MBMath.ClampFloat(_desire[id] + delta, 0f, 100f);
            _timer[id]++;
        }
        #endregion
    }

    #region Harmony Patches
    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomWarDecision")]
    public static class Patch_DisableRandomWar { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomPeaceDecision")]
    public static class Patch_DisableRandomPeace { private static bool Prefix(ref KingdomDecision __result) { __result = null; return false; } }

    [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnDeclarePeace")]
    public class KingdomPlayerPeacePatch
    {
        public static bool Prefix(KingdomWarItemVM item)
        {
            var peaceBehavior = Campaign.Current.GetCampaignBehavior<PeaceDesireBehavior>();
            if (peaceBehavior == null) return true;
            var playerKingdom = Hero.MainHero.Clan.Kingdom;
            var targetKingdom = item.Faction2 as Kingdom;
            if (playerKingdom == null || targetKingdom == null) return true;
            if (peaceBehavior.IsPeaceProposalAcceptable(targetKingdom, playerKingdom)) return true;

            InformationManager.DisplayMessage(new InformationMessage($"{targetKingdom.Name} is not interested in peace at this time.", Colors.Red));
            return false;
        }
    }
    #endregion
}