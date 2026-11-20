using System;
using System.Collections.Generic;
using System.Linq;

namespace PWManager.Domain.Models
{
    public enum WrestlerConcernStatus { Watching, AwaitingResponse, PromiseActive, Resolved }
    public enum WrestlerConcernKind { AppearanceOpportunity, CardPlacement }
    public enum WrestlerPromiseStatus { Active, Kept, Broken, Unassessable }
    public enum WrestlerBehaviorEventKind { Observation, Request, PromiseAgreed, PromiseRejected, Declined, Ignored, PromiseKept, PromiseBroken, PromiseUnassessable, Resolved, EmploymentClosed, PublicComplaint }
    public enum WrestlerPublicChannel { Private, Press, SocialMedia }
    public enum WrestlerConcernResponse { OfferAppearancePromise, Decline }
    public enum WrestlerInteractionStance { None, Agree, Reserve, Disagree }
    public enum WrestlerBehaviorTone { Calm, Firm }

    [Serializable]
    public sealed class WrestlerAppearanceObservation
    {
        public string ShowId;
        public int ShowVersion;
        public GameDate Date;
        public bool Appeared;
        public WrestlerAppearanceObservation Copy() => (WrestlerAppearanceObservation)MemberwiseClone();
    }

    [Serializable]
    public sealed class WrestlerAppearancePromise
    {
        public string Id;
        public GameDate StartDate, DueDate;
        public int EligibleShows;
        public WrestlerPromiseStatus Status;
        public ExpectedRole RequiredCardRole;
        public WrestlerAppearancePromise Copy() => (WrestlerAppearancePromise)MemberwiseClone();
    }

    [Serializable]
    public sealed class WrestlerConcernState
    {
        public string Id;
        public WrestlerConcernKind Kind;
        public ExpectedRole RequestedCardRole;
        public GameDate StartedDate;
        public float Severity;
        public WrestlerConcernStatus Status;
        public string RequestId;
        public string LastPublicCauseId;
        public OptionalGameDate ResponseDueDate;
        public List<WrestlerAppearancePromise> Promises = new();
        public WrestlerConcernState Copy()
        {
            var copy = (WrestlerConcernState)MemberwiseClone(); copy.Promises = Promises.Select(x => x.Copy()).ToList(); return copy;
        }
    }

    [Serializable]
    public sealed class WrestlerCardObservation
    {
        public string ShowId;
        public int ShowVersion;
        public GameDate Date;
        public ExpectedRole ExpectedRole;
        public bool UpperCard, MainEvent;
        public WrestlerCardObservation Copy() => (WrestlerCardObservation)MemberwiseClone();
    }

    [Serializable]
    public sealed class WrestlerBehaviorEventState
    {
        public string Id, ConcernId, ShowId;
        public GameDate Date;
        public WrestlerBehaviorEventKind Kind;
        public WrestlerInteractionStance Stance;
        public WrestlerBehaviorTone Tone;
        public string Description;
        public bool IncludedInWeeklyReport;
        public WrestlerPublicChannel PublicChannel;
        public string OriginBehaviorEventId;
        public WrestlerBehaviorEventState Copy() => (WrestlerBehaviorEventState)MemberwiseClone();
    }

    [Serializable]
    public sealed class WrestlerBehaviorState
    {
        public string PromotionId, WrestlerId;
        public float Trust = 50;
        public int MissedStreak, IgnoredRequests, BrokenPromises;
        public OptionalGameDate NextActionDate;
        public List<WrestlerAppearanceObservation> Observations = new();
        public List<WrestlerCardObservation> CardObservations = new();
        public List<WrestlerConcernState> Concerns = new();
        public List<WrestlerBehaviorEventState> Events = new();
        public WrestlerBehaviorState Copy()
        {
            var copy = (WrestlerBehaviorState)MemberwiseClone();
            copy.Observations = Observations.Select(x => x.Copy()).ToList();
            copy.CardObservations = CardObservations.Select(x => x.Copy()).ToList();
            copy.Concerns = Concerns.Select(x => x.Copy()).ToList(); copy.Events = Events.Select(x => x.Copy()).ToList(); return copy;
        }
    }
}
