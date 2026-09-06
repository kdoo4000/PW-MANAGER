using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum ShowStatus { Draft, Preparing, Review, Confirmed, InProgress, Completed, ResultReview, ResultsReviewed }

    [Serializable]
    public sealed class ShowState
    {
        public string Id;
        public string ScheduleId;
        public string Name;
        public ScheduledShowType ShowType;
        public GameDate Date;
        public string VenueContractId;
        public int DurationLimit;
        public List<string> TimelineEventIds = new();
        public string OpeningEventId;
        public string MainEventId;
        public long EstimatedCost;
        public int ShowVersion;
        public int AttendanceVarianceBasisPoints = 10000;
        public int TicketPricePercent = 100;
        public ShowStatus Status;

        public int CalculatePlannedDuration(IEnumerable<ShowEventState> events)
        {
            var durations = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var showEvent in events ?? Array.Empty<ShowEventState>())
                if (showEvent != null) durations[showEvent.Id] = showEvent.PlannedDuration;
            var total = 0;
            foreach (var id in TimelineEventIds)
                if (durations.TryGetValue(id, out var duration)) total += duration;
            return total;
        }
    }
}
