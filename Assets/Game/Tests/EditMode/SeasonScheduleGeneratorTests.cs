using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class SeasonScheduleGeneratorTests
    {
        [Test]
        public void QuarterlyAndBiweekly_GenerateConfirmedSeasonStructure()
        {
            var result = CreateGenerator().Generate(new SeasonScheduleGenerationRequest
            {
                SeasonStartYear = 2026,
                RegularShowFrequency = RegularShowFrequency.Biweekly,
                PpvFrequency = PpvFrequency.Quarterly,
                RegularVenueContractId = "venue-contract",
                PpvTypes = new[]
                {
                    ScheduledShowType.PpvRegular, ScheduledShowType.PpvRegular,
                    ScheduledShowType.PpvRegular, ScheduledShowType.PpvSignature
                }
            });

            var ppvs = result.Schedules.Where(x => x.ShowType != ScheduledShowType.Regular).ToList();
            Assert.That(ppvs, Has.Count.EqualTo(4));
            Assert.That(ppvs.Select(x => Format(x.Date)), Is.EqualTo(new[] { "2026-08-30", "2026-11-29", "2027-02-28", "2027-05-30" }));
            Assert.That(result.Schedules.Count(x => x.ShowType == ScheduledShowType.Regular), Is.EqualTo(26));
            Assert.That(result.Schedules.Select(x => x.Date).Distinct().Count(), Is.EqualTo(result.Schedules.Count));
            Assert.That(result.Policy.SignaturePpvScheduleId, Is.EqualTo(ppvs[3].Id));
            Assert.That(result.Policy.StartDate, Is.EqualTo(new GameDate(2026, 6, 1)));
            Assert.That(result.Policy.EndDate, Is.EqualTo(new GameDate(2027, 5, 31)));
        }

        [TestCase(PpvFrequency.EveryFourMonths, 3)]
        [TestCase(PpvFrequency.Quarterly, 4)]
        [TestCase(PpvFrequency.Bimonthly, 6)]
        [TestCase(PpvFrequency.Monthly, 12)]
        public void PpvFrequency_GeneratesConfirmedCount(PpvFrequency frequency, int expected)
        {
            var result = CreateGenerator().Generate(new SeasonScheduleGenerationRequest
            {
                SeasonStartYear = 2026,
                RegularShowFrequency = RegularShowFrequency.Monthly,
                PpvFrequency = frequency,
                RegularVenueContractId = "venue-contract"
            });

            Assert.That(result.Schedules.Count(x => x.ShowType != ScheduledShowType.Regular), Is.EqualTo(expected));
        }

        [Test]
        public void BookingDeadlines_UseThreeAndSevenDays()
        {
            var result = CreateGenerator().Generate(new SeasonScheduleGenerationRequest
            {
                SeasonStartYear = 2026,
                RegularShowFrequency = RegularShowFrequency.Monthly,
                PpvFrequency = PpvFrequency.EveryFourMonths,
                RegularVenueContractId = "venue-contract"
            });

            foreach (var schedule in result.Schedules)
            {
                var expected = schedule.ShowType == ScheduledShowType.Regular ? 3 : 7;
                Assert.That(DaysBetween(schedule.BookingDeadline, schedule.Date), Is.EqualTo(expected));
            }
        }

        [Test]
        public void InvalidPpvTierPlan_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => CreateGenerator().Generate(new SeasonScheduleGenerationRequest
            {
                SeasonStartYear = 2026,
                RegularShowFrequency = RegularShowFrequency.Monthly,
                PpvFrequency = PpvFrequency.Quarterly,
                RegularVenueContractId = "venue-contract",
                PpvTypes = new[] { ScheduledShowType.PpvRegular }
            }));
        }

        private static SeasonScheduleGenerator CreateGenerator()
        {
            var id = 0;
            return new SeasonScheduleGenerator(() => $"schedule-test-{++id}");
        }

        private static string Format(GameDate date) => $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}";
        private static int DaysBetween(GameDate first, GameDate second)
        {
            return (new DateTime(second.Year, second.Month, second.Day) - new DateTime(first.Year, first.Month, first.Day)).Days;
        }
    }
}
