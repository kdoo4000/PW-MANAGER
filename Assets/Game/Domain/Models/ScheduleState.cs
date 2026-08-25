using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class ScheduleState
    {
        public string Id;
        public ScheduledShowType ShowType;
        public GameDate Date;
        public GameDate BookingDeadline;
        public ScheduleStatus Status;
        public int AttendanceVarianceBasisPoints = 10000;
    }
}
