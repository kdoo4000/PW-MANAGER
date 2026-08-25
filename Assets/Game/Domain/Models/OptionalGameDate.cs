using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public struct OptionalGameDate
    {
        public bool HasValue;
        public GameDate Value;

        public OptionalGameDate(GameDate value)
        {
            HasValue = true;
            Value = value;
        }
    }
}
