using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class WrestlerHiddenState
    {
        public int Professionalism = 10;
        public int Ambition = 10;
        public int Loyalty = 10;
        public int InjuryProneness = 10;
        public int Consistency = 10;
        public int Collegiality = 10;
        public int BigMatchPerformance = 10;
        public int Assertiveness = 10;
        public int Adaptability = 10;
        public int RiskTaking = 10;
        public int MoneyOrientation = 10;
        public int MediaExpression = 10;
        public int Leadership = 10;
        public int Sociability = 10;
        public int ConflictProneness = 10;

        public WrestlerHiddenState Copy() => (WrestlerHiddenState)MemberwiseClone();
    }
}
