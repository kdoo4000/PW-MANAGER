using System;

namespace PWManager.Domain.Models
{
    public enum LockerRoomEventKind { Contact, Conflict, Mediation, OpinionShared }
    public enum LockerRoomWorkKind { Show, Training }

    [Serializable]
    public sealed class LockerRoomRelationshipState
    {
        public string FirstWrestlerId, SecondWrestlerId;
        public float Affinity;
        public int Interactions;
        public OptionalGameDate LastInteractionDate;
        public LockerRoomRelationshipState Copy() => (LockerRoomRelationshipState)MemberwiseClone();
    }
    [Serializable]
    public sealed class LockerRoomEventState
    {
        public string Id, PromotionId, SourceKey, ShowId, ActorId, PartnerId, MediatorId, OriginBehaviorEventId;
        public GameDate Date;
        public LockerRoomWorkKind WorkKind;
        public LockerRoomEventKind Kind;
        public string Description;
        public bool IncludedInWeeklyReport;
        public bool Significant;
        public LockerRoomEventState Copy() => (LockerRoomEventState)MemberwiseClone();
    }
}
