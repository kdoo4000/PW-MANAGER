using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum PromoPurpose
    {
        CharacterIntroduction, ChampionStatement, Rivalry, AlignmentChange,
        TeamFormation, TeamBreakup, Challenge, MatchBuild, SponsorAdvertisement
    }

    public enum PromoPresentation
    {
        InRingMic, Interview, BackstageConversation, InterruptionAttack,
        RescueBetrayal, VideoPackage
    }

    [Serializable]
    public sealed class PromoPlanState
    {
        public string Id;
        public PromoPurpose Purpose;
        public PromoPresentation Presentation;
        public List<string> ParticipantIds = new();
        public List<string> TargetIds = new();
        public string SponsorRequirementId;
    }
}
