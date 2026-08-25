using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class SeasonScheduleGenerationRequest
    {
        public int SeasonStartYear;
        public RegularShowFrequency RegularShowFrequency;
        public PpvFrequency PpvFrequency;
        public string RegularVenueContractId;
        public IReadOnlyList<ScheduledShowType> PpvTypes;
        public ScheduleStatus InitialStatus = ScheduleStatus.Draft;
    }

    public sealed class SeasonScheduleGenerationResult
    {
        public SeasonPolicyState Policy;
        public IReadOnlyList<ScheduleState> Schedules;
    }

    public sealed class SeasonScheduleGenerator
    {
        private readonly Func<string> createId;

        public SeasonScheduleGenerator(Func<string> createId = null)
        {
            this.createId = createId ?? EntityId.CreateRuntimeId;
        }

        public SeasonScheduleGenerationResult Generate(SeasonScheduleGenerationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.SeasonStartYear < 1 || request.SeasonStartYear >= 9999)
                throw new ArgumentOutOfRangeException(nameof(request.SeasonStartYear));
            if (string.IsNullOrWhiteSpace(request.RegularVenueContractId))
                throw new ArgumentException("A regular venue contract ID is required.", nameof(request));

            var start = new DateTime(request.SeasonStartYear, 6, 1);
            var end = new DateTime(request.SeasonStartYear + 1, 5, 31);
            var ppvCount = GetPpvCount(request.PpvFrequency);
            var ppvTypes = request.PpvTypes?.ToArray() ?? Enumerable.Repeat(ScheduledShowType.PpvRegular, ppvCount).ToArray();
            ValidatePpvTypes(ppvTypes, ppvCount);

            var schedules = new List<ScheduleState>();
            var ppvDates = GeneratePpvDates(start, request.PpvFrequency).ToArray();
            for (var i = 0; i < ppvDates.Length; i++)
                schedules.Add(CreateSchedule(ppvTypes[i], ppvDates[i], request.InitialStatus));

            foreach (var date in GenerateRegularShowDates(start, end, request.RegularShowFrequency))
                schedules.Add(CreateSchedule(ScheduledShowType.Regular, date, request.InitialStatus));

            schedules.Sort((left, right) => left.Date.CompareTo(right.Date));
            if (schedules.GroupBy(x => x.Date).Any(x => x.Count() > 1))
                throw new InvalidOperationException("Generated schedules contain a date collision.");

            var signature = schedules.SingleOrDefault(x => x.ShowType == ScheduledShowType.PpvSignature);
            var policy = new SeasonPolicyState
            {
                Id = createId(),
                StartDate = ToGameDate(start),
                EndDate = ToGameDate(end),
                RegularShowFrequency = request.RegularShowFrequency,
                PpvFrequency = request.PpvFrequency,
                RegularVenueContractId = request.RegularVenueContractId,
                SignaturePpvScheduleId = signature?.Id
            };
            return new SeasonScheduleGenerationResult { Policy = policy, Schedules = schedules };
        }

        private ScheduleState CreateSchedule(ScheduledShowType type, DateTime date, ScheduleStatus status)
        {
            var deadlineDays = type == ScheduledShowType.Regular ? 3 : 7;
            return new ScheduleState
            {
                Id = createId(),
                ShowType = type,
                Date = ToGameDate(date),
                BookingDeadline = ToGameDate(date.AddDays(-deadlineDays)),
                Status = status
            };
        }

        private static IEnumerable<DateTime> GeneratePpvDates(DateTime start, PpvFrequency frequency)
        {
            var interval = frequency switch
            {
                PpvFrequency.EveryFourMonths => 4,
                PpvFrequency.Quarterly => 3,
                PpvFrequency.Bimonthly => 2,
                PpvFrequency.Monthly => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(frequency))
            };
            for (var offset = interval - 1; offset < 12; offset += interval)
                yield return LastDayOfWeek(start.AddMonths(offset).Year, start.AddMonths(offset).Month, DayOfWeek.Sunday);
        }

        private static IEnumerable<DateTime> GenerateRegularShowDates(DateTime start, DateTime end, RegularShowFrequency frequency)
        {
            if (frequency == RegularShowFrequency.Monthly)
            {
                for (var month = start; month <= end; month = month.AddMonths(1))
                    yield return FirstDayOfWeek(month.Year, month.Month, DayOfWeek.Saturday);
                yield break;
            }

            if (frequency == RegularShowFrequency.Biweekly)
            {
                for (var date = FirstOnOrAfter(start, DayOfWeek.Saturday); date <= end; date = date.AddDays(14))
                    yield return date;
                yield break;
            }

            var days = frequency switch
            {
                RegularShowFrequency.Weekly => new[] { DayOfWeek.Saturday },
                RegularShowFrequency.TwiceWeekly => new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday },
                RegularShowFrequency.ThreeTimesWeekly => new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Saturday },
                _ => throw new ArgumentOutOfRangeException(nameof(frequency))
            };
            for (var date = start; date <= end; date = date.AddDays(1))
                if (days.Contains(date.DayOfWeek)) yield return date;
        }

        private static void ValidatePpvTypes(IReadOnlyCollection<ScheduledShowType> types, int expectedCount)
        {
            if (types.Count != expectedCount)
                throw new ArgumentException($"The PPV tier plan must contain exactly {expectedCount} entries.");
            if (types.Any(x => x == ScheduledShowType.Regular))
                throw new ArgumentException("The PPV tier plan cannot contain regular shows.");
            if (types.Count(x => x == ScheduledShowType.PpvSignature) > 1)
                throw new ArgumentException("A season can contain at most one signature PPV.");
        }

        private static int GetPpvCount(PpvFrequency frequency) => frequency switch
        {
            PpvFrequency.EveryFourMonths => 3,
            PpvFrequency.Quarterly => 4,
            PpvFrequency.Bimonthly => 6,
            PpvFrequency.Monthly => 12,
            _ => throw new ArgumentOutOfRangeException(nameof(frequency))
        };

        private static DateTime FirstOnOrAfter(DateTime date, DayOfWeek day)
        {
            return date.AddDays(((int)day - (int)date.DayOfWeek + 7) % 7);
        }

        private static DateTime FirstDayOfWeek(int year, int month, DayOfWeek day)
        {
            return FirstOnOrAfter(new DateTime(year, month, 1), day);
        }

        private static DateTime LastDayOfWeek(int year, int month, DayOfWeek day)
        {
            var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            return last.AddDays(-((int)last.DayOfWeek - (int)day + 7) % 7);
        }

        private static GameDate ToGameDate(DateTime date) => new GameDate(date.Year, date.Month, date.Day);
    }
}
