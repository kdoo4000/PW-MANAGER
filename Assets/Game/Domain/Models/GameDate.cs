using System;

namespace PWManager.Domain.Models
{
    [Serializable]
    public struct GameDate : IEquatable<GameDate>, IComparable<GameDate>
    {
        public int Year;
        public int Month;
        public int Day;

        public GameDate(int year, int month, int day)
        {
            _ = new DateTime(year, month, day);

            Year = year;
            Month = month;
            Day = day;
        }

        public bool Equals(GameDate other)
        {
            return Year == other.Year && Month == other.Month && Day == other.Day;
        }

        public override bool Equals(object obj)
        {
            return obj is GameDate other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Year, Month, Day);
        }

        public int CompareTo(GameDate other)
        {
            var yearComparison = Year.CompareTo(other.Year);
            if (yearComparison != 0) return yearComparison;

            var monthComparison = Month.CompareTo(other.Month);
            return monthComparison != 0 ? monthComparison : Day.CompareTo(other.Day);
        }

        public GameDate AddDays(int days)
        {
            var value = new DateTime(Year, Month, Day).AddDays(days);
            return new GameDate(value.Year, value.Month, value.Day);
        }

        public int DaysUntil(GameDate other)
        {
            var current = new DateTime(Year, Month, Day);
            var target = new DateTime(other.Year, other.Month, other.Day);
            return (target - current).Days;
        }
    }
}
