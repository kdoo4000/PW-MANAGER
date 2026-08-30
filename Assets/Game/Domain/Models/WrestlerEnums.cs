namespace PWManager.Domain.Models
{
    public enum WrestlerGender { Male, Female }
    public enum WrestlerBackground { Rookie, Athlete, Entertainer, OtherPromotion }
    public enum WrestlerBodyType { Lightweight, Balanced, Muscular, Heavyweight, Giant }
    public enum MatchArchetype { Fundamentals, Brawler, Power, Technical, HighFlying, Spot, Balanced }
    public enum PromoArchetype { Charisma, Mic, Improvisation, Face, Heel, Comedy, Silent, Balanced, Weak }
    public enum PromoDisposition { Balanced, Serious, Comic }
    public enum InjuryStatus { None, Minor, Moderate, Severe }
    public enum WrestlerAvailability { Available, Limited, MatchUnavailable, Unavailable }
    public enum AgingState { Young, Prime, Veteran, Declining }
    public enum MomentumReleaseType { None, Full, Partial }
    public enum MomentumReleaseReason { None, Title, MainEvent, Tournament, StoryFinal }
    public enum KayfabeAlignment { Face, Heel, Tweener }
    public enum RosterActivityState { Active, Inactive, Suspended, Released }
}
