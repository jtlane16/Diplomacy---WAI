﻿using Diplomacy.CivilWar.Factions;
using Diplomacy.CivilWar.Scoring;
using Diplomacy.Extensions;

using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace Diplomacy.CivilWar.Actions
{
    public class JoinFactionAction
    {
        public static void Apply(Clan clan, RebelFaction rebelFaction)
        {
            rebelFaction.AddClan(clan);
        }

        public static bool ShouldApply(Clan clan, RebelFaction rebelFaction)
        {
            if (!CanApply(clan, rebelFaction, out _))
                return false;
            var score = RebelFactionScoringModel.GetDemandScore(clan, rebelFaction);
            return score.ResultNumber >= RebelFactionScoringModel.RequiredScore;
        }

        public static bool CanApply(Clan clan, RebelFaction rebelFaction, out TextObject? reason)
        {
            IEnumerable<TextObject> exceptions;
            reason = (exceptions = CanApply(clan, rebelFaction)).FirstOrDefault();

            return !exceptions.Any();
        }

        /// <summary>
        /// Determines the set of reasons that the given faction cannot be joined by the clan.
        /// </summary>
        /// <param name="clan"></param>
        /// <param name="rebelFaction"></param>
        /// <returns>
        /// Set of reasons for which the faction cannot be joined. As the reasons are not currently used
        /// in the UI, currently supplying empty TextObjects.
        /// </returns>
        public static IEnumerable<TextObject> CanApply(Clan clan, RebelFaction rebelFaction)
        {
            // can only join a faction of a kingdom that they're in
            if (rebelFaction.ParentKingdom != clan.Kingdom)
            {
                yield return new TextObject();
            }

            // rebel kingdom members can't join factions
            if (clan.Kingdom.IsRebelKingdom())
            {
                yield return new TextObject();
            }

            // ruling clan can't join factions
            if (clan == clan.Kingdom.RulingClan)
            {
                yield return new TextObject();
            }

            // mercenaries can't join factions
            if (clan.IsUnderMercenaryService)
            {
                yield return new TextObject();
            }

            // can't join a faction during an active rebellion
            if (rebelFaction.AtWar)
            {
                yield return new TextObject();
            }

            // faction sponsors can't join another faction 
            if (clan.Kingdom.GetRebelFactions().Any(x => x.SponsorClan == clan))
            {
                yield return new TextObject();
            }

            // can't join a faction you're already a member of
            if (rebelFaction.Clans.Contains(clan))
            {
                yield return new TextObject();
            }

            // can't join a faction when member of a secession faction
            if (clan.Kingdom.GetRebelFactions().Any(x => x.Clans.Contains(clan) && x.RebelDemandType == RebelDemandType.Secession))
            {
                yield return new TextObject();
            }

            // can't join a faction when eliminated
            if (clan.IsEliminated)
            {
                yield return new TextObject();
            }
        }
    }
}