using System;

namespace PWManager.Domain.Models
{
    public enum ShowEventType { Match, Promo }

    [Serializable]
    public sealed class ShowEventState
    {
        public string Id;
        public string ShowId;
        public ShowEventType EventType;
        public string DetailId;
        public int PlannedDuration;
    }
}
