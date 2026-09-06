using System;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class TicketPricingRules
    {
        public const decimal MinimumPlayerMultiplier = 0.60m;
        public const decimal MaximumPlayerMultiplier = 1.60m;
        private const decimal DemandSlope = 0.75m;
        public const int MinimumAttendanceVarianceBasisPoints = 9500;
        public const int MaximumAttendanceVarianceBasisPoints = 10500;

        public static decimal GetShowTypeMultiplier(ScheduledShowType showType)
        {
            return showType switch
            {
                ScheduledShowType.Regular => 1.00m,
                ScheduledShowType.PpvRegular => 1.50m,
                ScheduledShowType.PpvMajor => 2.00m,
                ScheduledShowType.PpvSignature => 3.00m,
                _ => throw new ArgumentOutOfRangeException(nameof(showType), showType, null)
            };
        }

        public static long GetReferencePrice(long venueBaseTicketPrice, ScheduledShowType showType)
        {
            if (venueBaseTicketPrice <= 0) throw new ArgumentOutOfRangeException(nameof(venueBaseTicketPrice));
            return RoundMoney(venueBaseTicketPrice * GetShowTypeMultiplier(showType));
        }

        public static long GetActualPrice(long referencePrice, int percent)
        {
            if (percent == 0) percent = 100; // Absent in legacy saves.
            if (percent < 60 || percent > 160) throw new ArgumentOutOfRangeException(nameof(percent));
            var range = GetPlayerPriceRange(referencePrice);
            return Math.Max(range.Minimum, Math.Min(range.Maximum, RoundMoney(referencePrice * percent / 100m)));
        }

        public static (long Minimum, long Maximum) GetPlayerPriceRange(long referencePrice)
        {
            if (referencePrice <= 0) throw new ArgumentOutOfRangeException(nameof(referencePrice));
            return (RoundMoney(referencePrice * MinimumPlayerMultiplier),
                RoundMoney(referencePrice * MaximumPlayerMultiplier));
        }

        public static decimal GetDemandMultiplier(long actualPrice, long referencePrice)
        {
            if (referencePrice <= 0) throw new ArgumentOutOfRangeException(nameof(referencePrice));

            var priceRatio = (decimal)actualPrice / referencePrice;
            var range = GetPlayerPriceRange(referencePrice);
            if (actualPrice < range.Minimum || actualPrice > range.Maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(actualPrice));
            }

            return priceRatio <= 1.00m
                ? 1.00m + ((1.00m - priceRatio) * DemandSlope)
                : 1.00m - ((priceRatio - 1.00m) * DemandSlope);
        }

        public static long GetPriceAdjustedDemand(long baseDemand, long actualPrice, long referencePrice)
        {
            if (baseDemand < 0) throw new ArgumentOutOfRangeException(nameof(baseDemand));

            return decimal.ToInt64(decimal.Floor(
                baseDemand * GetDemandMultiplier(actualPrice, referencePrice)));
        }

        public static (long Minimum, long Maximum) GetExpectedAttendanceRange(
            long baseDemand, long actualPrice, long referencePrice, int venueCapacity)
        {
            ValidateVenueCapacity(venueCapacity);
            var priceAdjustedDemand = GetPriceAdjustedDemand(baseDemand, actualPrice, referencePrice);
            return (
                Math.Min(ApplyAttendanceVariance(priceAdjustedDemand, MinimumAttendanceVarianceBasisPoints), venueCapacity),
                Math.Min(ApplyAttendanceVariance(priceAdjustedDemand, MaximumAttendanceVarianceBasisPoints), venueCapacity));
        }

        public static int RollAttendanceVarianceBasisPoints(Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));

            var firstRoll = random.Next(-500, 501);
            var secondRoll = random.Next(-500, 501);
            return 10000 + ((firstRoll + secondRoll) / 2);
        }

        public static long GetActualAttendance(
            long baseDemand,
            long actualPrice,
            long referencePrice,
            int venueCapacity,
            int attendanceVarianceBasisPoints)
        {
            ValidateVenueCapacity(venueCapacity);
            var priceAdjustedDemand = GetPriceAdjustedDemand(baseDemand, actualPrice, referencePrice);
            return Math.Min(ApplyAttendanceVariance(priceAdjustedDemand, attendanceVarianceBasisPoints), venueCapacity);
        }

        private static long ApplyAttendanceVariance(long demand, int attendanceVarianceBasisPoints)
        {
            if (attendanceVarianceBasisPoints < MinimumAttendanceVarianceBasisPoints ||
                attendanceVarianceBasisPoints > MaximumAttendanceVarianceBasisPoints)
            {
                throw new ArgumentOutOfRangeException(nameof(attendanceVarianceBasisPoints));
            }

            return decimal.ToInt64(decimal.Floor(demand * attendanceVarianceBasisPoints / 10000m));
        }

        private static void ValidateVenueCapacity(int venueCapacity)
        {
            if (venueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(venueCapacity));
        }

        private static long RoundMoney(decimal value) => decimal.ToInt64(decimal.Round(value, 0, MidpointRounding.AwayFromZero));
    }
}
