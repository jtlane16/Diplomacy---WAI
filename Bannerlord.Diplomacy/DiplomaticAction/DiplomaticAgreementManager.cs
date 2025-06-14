﻿using JetBrains.Annotations;

using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace Diplomacy.DiplomaticAction
{
    class DiplomaticAgreementManager
    {
        public static DiplomaticAgreementManager? Instance { get; internal set; }

        [SaveableField(1)]
        [UsedImplicitly]
        private Dictionary<FactionPair, List<DiplomaticAgreement>> _agreements;

        public DiplomaticAgreementManager()
        {
            _agreements = new Dictionary<FactionPair, List<DiplomaticAgreement>>();
            Instance = this;
        }

        public Dictionary<FactionPair, List<DiplomaticAgreement>> Agreements => _agreements;

        public static bool HasNonAggressionPact(Kingdom kingdom, Kingdom otherKingdom, out NonAggressionPactAgreement? pactAgreement)
        {
            if (Instance!.Agreements.TryGetValue(new FactionPair(kingdom, otherKingdom), out var agreements))
            {
                var enumerable = agreements.Where(agreement => agreement.GetAgreementType() == AgreementType.NonAggressionPact && !agreement.IsExpired());
                pactAgreement = enumerable.OfType<NonAggressionPactAgreement>().FirstOrDefault();
                return pactAgreement != default;
            }
            else
            {
                pactAgreement = null;
                return false;
            }
        }

        public static IEnumerable<NonAggressionPactAgreement> GetPacts(Kingdom kingdom)
        {
            if (Instance == null)
            {
                yield break;
            }

            foreach (var kvp in Instance.Agreements)
            {
                if (kvp.Key.Faction1 == kingdom || kvp.Key.Faction2 == kingdom)
                {
                    foreach (var pact in kvp.Value.OfType<NonAggressionPactAgreement>().Where(p => !p.IsExpired()))
                    {
                        yield return pact;
                    }
                }
            }
        }

        public static void RegisterAgreement(Kingdom kingdom, Kingdom otherKingdom, DiplomaticAgreement diplomaticAgreement)
        {
            var factionMapping = new FactionPair(kingdom, otherKingdom);
            if (Instance!.Agreements.TryGetValue(factionMapping, out var agreements))
            {
                agreements.Add(diplomaticAgreement);
            }
            else
            {
                Instance!.Agreements[factionMapping] = new List<DiplomaticAgreement> { diplomaticAgreement };
            }
        }
        public void Sync()
        {
            Instance = this;
        }
    }
}
