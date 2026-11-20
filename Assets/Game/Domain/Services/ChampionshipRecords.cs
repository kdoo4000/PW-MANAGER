using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class ChampionshipWinRecord
    {
        public ChampionshipReignState Reign;
        public int Count;
    }
    public sealed class ChampionshipDurationRecord
    {
        public ChampionshipReignState Reign;
        public int Days;
    }
    public sealed class ChampionshipAgeRecord
    {
        public ChampionshipReignState Reign;
        public string WrestlerId;
        public int AgeDays;
    }
    public sealed class ChampionshipRecords
    {
        public ChampionshipReignState First;
        public List<ChampionshipWinRecord> MostWins = new();
        public List<ChampionshipDurationRecord> Longest = new(), Shortest = new();
        public List<ChampionshipAgeRecord> Oldest = new(), Youngest = new();

        public static ChampionshipRecords Calculate(GameSave save, ChampionshipState title)
        {
            var reigns = (title.Reigns ?? new()).Where(r => r != null).ToList();
            var result = new ChampionshipRecords { First = reigns.FirstOrDefault() };
            var wins = reigns.GroupBy(r => r.HolderId).Select(g => new ChampionshipWinRecord { Reign = g.Last(), Count = g.Count() }).ToList();
            if (wins.Count > 0) result.MostWins = wins.Where(w => w.Count == wins.Max(x => x.Count)).ToList();
            var durations = reigns.Where(r => KnownDate(r.WonDate) && KnownDate(r.LostDate.HasValue ? r.LostDate.Value : save.CurrentDate))
                .Select(r => new ChampionshipDurationRecord { Reign = r, Days = Math.Max(0, r.WonDate.DaysUntil(r.LostDate.HasValue ? r.LostDate.Value : save.CurrentDate)) }).ToList();
            if (durations.Count > 0) result.Longest = durations.Where(r => r.Days == durations.Max(x => x.Days)).ToList();
            var completed = durations.Where(r => r.Reign.LostDate.HasValue).ToList();
            if (completed.Count > 0) result.Shortest = completed.Where(r => r.Days == completed.Min(x => x.Days)).ToList();
            var ages = new List<ChampionshipAgeRecord>();
            foreach (var reign in reigns.Where(r => KnownDate(r.WonDate)))
            foreach (var member in (reign.MemberIds ?? new()).Distinct())
            {
                var wrestler = save.Wrestlers.FirstOrDefault(w => w.Id == member);
                if (wrestler == null || !KnownDate(wrestler.Identity.BirthDate) || wrestler.Identity.BirthDate.CompareTo(reign.WonDate) > 0) continue;
                ages.Add(new ChampionshipAgeRecord { Reign = reign, WrestlerId = member, AgeDays = wrestler.Identity.BirthDate.DaysUntil(reign.WonDate) });
            }
            if (ages.Count > 0)
            {
                result.Oldest = ages.Where(a => a.AgeDays == ages.Max(x => x.AgeDays)).ToList();
                result.Youngest = ages.Where(a => a.AgeDays == ages.Min(x => x.AgeDays)).ToList();
            }
            return result;
        }

        public static bool KnownDate(GameDate date) => date.Year is >= 1 and <= 9999 && date.Month is >= 1 and <= 12 &&
            date.Day >= 1 && date.Day <= DateTime.DaysInMonth(date.Year, date.Month);
    }
}
