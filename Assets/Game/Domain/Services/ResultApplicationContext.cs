using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class ResultApplicationContext
    {
        public GameSave Save { get; }
        public ShowState Show { get; }
        public ShowResultState ShowResult { get; }
        public IReadOnlyList<MatchResultState> MatchResults { get; }
        public IReadOnlyList<PromoResultState> PromoResults { get; }
        public string ApplicationKey { get; }

        private ResultApplicationContext(
            GameSave save, ShowState show, ShowResultState showResult,
            IReadOnlyList<MatchResultState> matchResults, IReadOnlyList<PromoResultState> promoResults)
        {
            Save = save;
            Show = show;
            ShowResult = showResult;
            MatchResults = matchResults;
            PromoResults = promoResults;
            ApplicationKey = CreateApplicationKey(show.Id, showResult.ShowVersion);
        }

        public static ResultApplicationContext Create(GameSave save, string showResultId)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            var showResult = (save.ShowResults ?? new List<ShowResultState>()).SingleOrDefault(x => x?.Id == showResultId)
                ?? throw new ArgumentException("Show result was not found.", nameof(showResultId));
            var show = (save.Shows ?? new List<ShowState>()).SingleOrDefault(x => x?.Id == showResult.ShowId)
                ?? throw new InvalidOperationException("Show result references a missing show.");
            if (showResult.ShowVersion < 1 || show.ShowVersion != showResult.ShowVersion)
                throw new InvalidOperationException("Show result version does not match the show snapshot.");

            EnsureUniqueIds(showResult.MatchResultIds, "match result");
            EnsureUniqueIds(showResult.PromoResultIds, "promo result");
            EnsureUniqueIds(showResult.TimelineResultIds, "timeline result");
            var matchResults = ResolveResults(save.MatchResults, showResult.MatchResultIds, "Match result");
            var promoResults = ResolveResults(save.PromoResults, showResult.PromoResultIds, "Promo result");
            ValidateResultOwnership(showResult, matchResults, promoResults);
            ValidateEventCoverage(save, show, showResult.TimelineResultIds, matchResults, promoResults);
            ValidateSettlement(showResult.FinancialSettlement);

            var context = new ResultApplicationContext(save, show, showResult, matchResults, promoResults);
            context.EnsureNotApplied();
            return context;
        }

        public void EnsureNotApplied()
        {
            if ((Save.ProcessedIds ?? new List<string>()).Contains(ApplicationKey))
                throw new InvalidOperationException("This show version result has already been applied.");
        }

        public static string CreateApplicationKey(string showId, int showVersion)
        {
            if (string.IsNullOrWhiteSpace(showId)) throw new ArgumentException("Show ID is required.", nameof(showId));
            if (showVersion < 1) throw new ArgumentOutOfRangeException(nameof(showVersion));
            return $"show-result:{showId}:{showVersion}";
        }

        private static List<T> ResolveResults<T>(IEnumerable<T> source, IEnumerable<string> ids, string name) where T : class
        {
            var values = (source ?? Array.Empty<T>()).ToList();
            var result = new List<T>();
            foreach (var id in ids ?? Array.Empty<string>())
            {
                var matches = values.Where(x => GetId(x) == id).ToList();
                if (matches.Count != 1) throw new InvalidOperationException($"{name} {id} is missing or duplicated.");
                result.Add(matches[0]);
            }
            return result;
        }

        private static string GetId<T>(T value) where T : class => value switch
        {
            MatchResultState match => match.Id,
            PromoResultState promo => promo.Id,
            _ => throw new ArgumentException("Unsupported result type.", nameof(value))
        };

        private static void ValidateResultOwnership(
            ShowResultState showResult, IEnumerable<MatchResultState> matchResults, IEnumerable<PromoResultState> promoResults)
        {
            foreach (var result in matchResults)
                if (result.ShowId != showResult.ShowId || result.ShowVersion != showResult.ShowVersion)
                    throw new InvalidOperationException("Match result belongs to another show version.");
            foreach (var result in promoResults)
                if (result.ShowId != showResult.ShowId || result.ShowVersion != showResult.ShowVersion)
                    throw new InvalidOperationException("Promo result belongs to another show version.");
        }

        private static void ValidateEventCoverage(
            GameSave save, ShowState show, IReadOnlyList<string> timelineResultIds,
            IEnumerable<MatchResultState> matchResults, IEnumerable<PromoResultState> promoResults)
        {
            var expected = show.TimelineEventIds ?? new List<string>();
            var allResults = matchResults.Cast<object>().Concat(promoResults).ToList();
            var actual = matchResults.Select(x => x.ShowEventId).Concat(promoResults.Select(x => x.ShowEventId)).ToList();
            if (actual.Count != expected.Count || actual.Distinct(StringComparer.Ordinal).Count() != actual.Count ||
                !new HashSet<string>(actual, StringComparer.Ordinal).SetEquals(expected))
                throw new InvalidOperationException("Show result does not cover every timeline event exactly once.");

            if (timelineResultIds != null && timelineResultIds.Count > 0)
            {
                if (timelineResultIds.Count != expected.Count)
                    throw new InvalidOperationException("Show result timeline count does not match the show timeline.");
                for (var index = 0; index < expected.Count; index++)
                {
                    var result = allResults.SingleOrDefault(x => GetIdFromObject(x) == timelineResultIds[index]);
                    if (result == null || GetShowEventId(result) != expected[index])
                        throw new InvalidOperationException("Show result timeline order does not match the show timeline.");
                }
            }

            var events = (save.ShowEvents ?? new List<ShowEventState>()).Where(x => x != null).ToDictionary(x => x.Id, StringComparer.Ordinal);
            foreach (var result in matchResults)
                if (!events.TryGetValue(result.ShowEventId, out var showEvent) || showEvent.ShowId != show.Id ||
                    showEvent.EventType != ShowEventType.Match || showEvent.DetailId != result.MatchPlanId)
                    throw new InvalidOperationException("Match result does not match its show event.");
            foreach (var result in promoResults)
                if (!events.TryGetValue(result.ShowEventId, out var showEvent) || showEvent.ShowId != show.Id ||
                    showEvent.EventType != ShowEventType.Promo || showEvent.DetailId != result.PromoPlanId)
                    throw new InvalidOperationException("Promo result does not match its show event.");
        }

        private static string GetIdFromObject(object value) => value switch
        {
            MatchResultState match => match.Id,
            PromoResultState promo => promo.Id,
            _ => null
        };

        private static string GetShowEventId(object value) => value switch
        {
            MatchResultState match => match.ShowEventId,
            PromoResultState promo => promo.ShowEventId,
            _ => null
        };

        private static void ValidateSettlement(FinancialSettlementState settlement)
        {
            if (settlement == null) throw new InvalidOperationException("Financial settlement is required.");
            if (settlement.Revenue < 0 || settlement.Cost < 0)
                throw new InvalidOperationException("Financial settlement cannot contain negative revenue or cost.");
            if (settlement.NetIncome != settlement.Revenue - settlement.Cost)
                throw new InvalidOperationException("Financial settlement net income is inconsistent.");
        }

        private static void EnsureUniqueIds(IEnumerable<string> ids, string name)
        {
            var values = (ids ?? Array.Empty<string>()).ToList();
            if (values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Count)
                throw new InvalidOperationException($"Show result contains invalid or duplicate {name} IDs.");
        }
    }
}
