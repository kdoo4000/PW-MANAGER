using System;

namespace PWManager.Domain.Models
{
    public enum PlayerCareerRole { WrestlerManager, ProfessionalManager }
    public enum PlayerReputation { Local, Regional, National, Star, Legend }
    public enum PlayerWrestlingType { Worker, Balanced, Showman }

    [Serializable]
    public sealed class PlayerCharacterState
    {
        public string Name;
        public PlayerCareerRole Role;
        public PlayerReputation Reputation;
        public PlayerWrestlingType WrestlingType;
        public string WrestlingStyleId;
        public string WrestlerId;
        public string ManagerId;
        public WrestlerGender Gender;
        public GameDate BirthDate;
        [Obsolete("Legacy save migration only. Use BirthDate.")] public int Age;
        public int HeightCm;
        public int WeightKg;
        public float MatchAbility;
        public float PromoAbility;
    }
}
