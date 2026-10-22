using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using UnityEditor;
using UnityEngine;

public static class MatchScenarioAudit
{
    public const string SourceFolder = "artifacts/match-scenario-audit-20261008";
    public const string Folder = "artifacts/match-scenario-improved-20261009";
    private static StaticContentRegistry content;
    private static List<WrestlerState> initial, pool;
    private static readonly HashSet<string> used = new();
    private static StaticContentRegistry noRopeRegistry;

    private static StaticContentRegistry Registry(bool noRope = false)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset");
        if (!noRope) return new StaticContentRegistry(catalog);
        if (noRopeRegistry != null) return noRopeRegistry;
        var copy = UnityEngine.Object.Instantiate(catalog); copy.hideFlags = HideFlags.HideAndDontSave;
        var standard = UnityEngine.Object.Instantiate(copy.MatchGimmicks.Single(g => g.Id == "gimmick_000"));
        standard.hideFlags = HideFlags.HideAndDontSave; standard.RopeBreakRule = MatchRuleOverride.Disabled;
        copy.MatchGimmicks[copy.MatchGimmicks.FindIndex(g => g.Id == "gimmick_000")] = standard;
        return noRopeRegistry = new StaticContentRegistry(copy);
    }

    [Serializable] public sealed class Finding
    { public string code, message; public int eventIndex, seconds; }
    [Serializable] public sealed class Count
    { public string name; public int events, seconds; }
    [Serializable] public sealed class Profile
    {
        public string id, name, gender, background, style;
        public float overall, psychology, stamina, technical, power, flying;
        public List<string> signatures, finishers;
    }
    [Serializable] public sealed class CaseResult
    {
        public int index, seed, plannedMinutes, actualSeconds, eventCount, distinctTechniques, successfulHits;
        public int maxIdleSeconds, maxRepeatedActions, firstFinisherSeconds = -1, motionSamples;
        public string label, format, requestedFinish, actualFinish, winner, exception, playback;
        public bool finished, deterministic, roundTrip, settled;
        public float maxFatigue, ringTopShare, weightedMeanX, weightedMeanZ, centerShare, edgeShare;
        public int centerLineCrossings;
        public bool noRopeBreak;
        public List<Profile> participants = new();
        public List<Count> actions = new(), phases = new(), techniques = new();
        public List<Finding> errors = new(), observations = new();
    }

    public static string Initialize()
    {
        Directory.CreateDirectory(Folder);
        content = new StaticContentRegistry(AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset"));
        MoveTable.Initialize(); used.Clear();
        int id = 0;
        var generator = new WrestlerGenerator(content, 20261008, () => $"20261008-1000-4000-8000-{++id:000000000000}");
        initial = generator.GenerateInitialCandidates(new GameDate(2026, 6, 1)).ToList();
        pool = new List<WrestlerState>(initial);
        for (int i = 0; i < 1200; i++) pool.Add(generator.GenerateCandidate(i % 2 == 0 ? WrestlerGender.Male : WrestlerGender.Female, new GameDate(2026, 6, 1)));
        File.WriteAllText(Path.Combine(Folder, "generated-pool.json"), JsonUtility.ToJson(new Roster { wrestlers = pool }, true));
        return string.Join("; ", pool.GroupBy(w => w.Presentation.WrestlingStyleId).Select(g => content.Styles[g.Key].DisplayName + ": " + g.Count()));
    }

    [Serializable] private sealed class Roster { public List<WrestlerState> wrestlers; }

    public static string Run(int index)
    {
        if (pool == null) throw new InvalidOperationException("Initialize the audit first.");
        var r = new CaseResult { index = index, seed = 20261008 + index * 97 };
        string path = Path.Combine(Folder, $"case-{index + 1:00}"); Directory.CreateDirectory(path);
        try
        {
            int mode = index % 4, group = index / 4;
            r.plannedMinutes = index < 24 ? new[] { 5, 10, 15, 20, 30, 45 }[group] : new[] { 10, 15, 20, 30, 20, 30, 15, 20 }[index - 24];
            var finish = index < 24 ? mode == 1 && group % 2 == 1 ? MatchFinishType.Submission : MatchFinishType.Pinfall :
                new[] { MatchFinishType.RollUp, MatchFinishType.CountOut, MatchFinishType.Disqualification, MatchFinishType.Draw,
                    MatchFinishType.Pinfall, MatchFinishType.Pinfall, MatchFinishType.Pinfall, MatchFinishType.Submission }[index - 24];
            bool tag = index is 28 or 29; int count = index < 28 ? 2 : index == 30 ? 3 : 4;
            r.format = tag ? index == 29 ? "tornado-tag" : "tag" : count == 2 ? "singles" : count + "-way";
            r.requestedFinish = finish.ToString();
            r.label = index < 24 ? new[] { "initial-roster", "high-psychology", "women-low-stamina", "veteran-vs-rookie" }[mode] : r.format + "/" + finish;
            var selected = new List<WrestlerState>();
            for (int j = 0; j < count; j++)
            {
                string style = "style_" + (((index * 2 + j * 3) % 7) + 1).ToString("000");
                IEnumerable<WrestlerState> choices = (index < 24 && mode == 0 ? initial : pool).Where(w => !used.Contains(w.Id));
                if (index < 24 && mode == 2) choices = choices.Where(w => w.Identity.Gender == WrestlerGender.Female);
                else choices = choices.Where(w => w.Identity.Gender == WrestlerGender.Male);
                if (index < 24 && mode == 3)
                {
                    var background = j == 0 ? WrestlerBackground.Veteran : WrestlerBackground.Rookie;
                    choices = choices.Where(w => w.Identity.Background == background);
                }
                var styled = choices.Where(w => w.Presentation.WrestlingStyleId == style);
                if (styled.Any()) choices = styled;
                var w = index < 24 && mode == 1 ? choices.OrderByDescending(w => w.Attributes.RingPsychology).First() :
                    index < 24 && mode == 2 ? choices.OrderBy(w => w.Attributes.Stamina).First() :
                    index < 24 && mode == 3 && j == 0 ? choices.OrderByDescending(w => WrestlerOverallCalculator.Match(w)).First() :
                    choices.OrderBy(w => WrestlerOverallCalculator.Match(w)).First();
                selected.Add(w); used.Add(w.Id);
            }
            int serial = 0;
            string Id() => $"20261008-{index + 1:0000}-4000-8000-{++serial:000000000000}";
            var roster = selected.Concat(initial.Where(w => !selected.Any(s => s.Id == w.Id))).Take(8).ToList();
            var save = new GameStartService(Id).CreateInitialSave(new GameStartRequest
            {
                PromotionName = "Match Scenario Audit", PromotionAbbreviation = "MSA", InitialCash = 1000000,
                WorldSeed = r.seed, UtcNow = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
                WrestlerContracts = roster.Select(w => new InitialWrestlerContractInput { Wrestler = w, ContractEndYear = 2027, MonthlySalary = 500 }).ToList(),
                RegularVenueId = "venue_002", RegularVenueProductionCost = 1000,
                RegularShowFrequency = RegularShowFrequency.Biweekly, PpvFrequency = PpvFrequency.Quarterly
            });
            foreach (string issue in GameSaveValidator.Validate(save)) Error(r, "initial-save", -1, 0, issue);
            var fighters = selected.Select(w => save.Wrestlers.Single(s => s.Id == w.Id)).ToList();
            for (int i = 0; i < fighters.Count; i++) fighters[i].Roster.Alignment = i % 2 == 0 ? KayfabeAlignment.Face : KayfabeAlignment.Heel;
            r.participants = fighters.Select(w => new Profile { id = w.Id, name = w.Identity.RingName, gender = w.Identity.Gender.ToString(),
                background = w.Identity.Background.ToString(), style = w.Presentation.WrestlingStyleId, overall = WrestlerOverallCalculator.Match(w),
                psychology = w.Attributes.RingPsychology, stamina = w.Attributes.Stamina, technical = w.Attributes.Technical,
                power = w.Attributes.Power, flying = w.Attributes.HighFlying, signatures = w.Presentation.SignatureMoveIds,
                finishers = w.Presentation.FinisherMoveIds }).ToList();
            foreach (var w in fighters)
            foreach (string moveId in w.Presentation.SignatureMoveIds.Concat(w.Presentation.FinisherMoveIds))
            {
                var move = MatchTechniques.Find(moveId);
                if (move == null || !move.Enabled || !move.AllowsStyle(w.Presentation.WrestlingStyleId) || !move.MeetsRequirement(w.Attributes))
                    Error(r, "generated-moveset", -1, 0, w.Id + "/" + moveId);
            }
            var planning = new ShowPlanningService(content, Id, r.seed);
            var show = save.Shows.First(s => s.ShowType == ScheduledShowType.Regular); show.DurationLimit = r.plannedMinutes;
            var plan = new MatchPlanState { MatchTypeId = tag ? "matchtype_002" : "matchtype_001", MatchGimmickId = "gimmick_000",
                ParticipantIds = fighters.Select(w => w.Id).ToList(), WinnerId = finish == MatchFinishType.Draw ? null : fighters[0].Id,
                LoserTargetId = fighters[tag ? 2 : 1].Id, FinishType = finish, Tornado = index == 29,
                TeamCount = tag ? 2 : 0, MembersPerTeam = tag ? 2 : 0 };
            planning.AddMatch(save, show.Id, plan, r.plannedMinutes); planning.Confirm(save, show.Id);
            save.CurrentDate = show.Date; StorylineService.StartShow(save, show);
            string fixture = JsonUtility.ToJson(save); File.WriteAllText(Path.Combine(path, "fixture.json"), fixture);
            ShowExecutionService Service() => new(planning, new MatchEvaluator(content, content, Id, content.GetMoveRules), new PromoEvaluator(Id), Id);
            var venue = content.Venues["venue_002"];
            var showResult = Service().Execute(save, show.Id, r.seed, venue.Capacity, venue.BaseTicketPrice);
            var result = save.MatchResults.Single(); var engine = result.EngineState;
            if (engine == null) throw new InvalidOperationException("The actual execution did not store an engine timeline.");
            r.finished = engine.IsFinished && !engine.IsAborted; r.actualSeconds = engine.ElapsedSeconds;
            r.actualFinish = engine.ActualFinish.ToString(); r.winner = engine.WinnerId; r.eventCount = engine.Events.Count;
            if (!r.finished) Error(r, "engine-finish", -1, r.actualSeconds, engine.FailureReason);
            if (engine.ActualFinish != finish || engine.WinnerId != plan.WinnerId) Error(r, "booking", -1, r.actualSeconds, "Booked outcome did not survive execution.");
            File.WriteAllText(Path.Combine(path, "result.json"), JsonUtility.ToJson(result, true));
            WriteMatchLog(save, result, r, path);
            File.WriteAllText(Path.Combine(path, "replay-save.json"), JsonUtility.ToJson(save));
            AuditTimeline(r, engine);
            var repeat = JsonUtility.FromJson<GameSave>(fixture);
            Service().Execute(repeat, show.Id, r.seed, venue.Capacity, venue.BaseTicketPrice);
            r.deterministic = JsonUtility.ToJson(engine) == JsonUtility.ToJson(repeat.MatchResults.Single().EngineState);
            if (!r.deterministic) Error(r, "determinism", -1, 0, "The same fixture and seed produced a different engine timeline.");
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            r.roundTrip = JsonUtility.ToJson(engine) == JsonUtility.ToJson(restored.MatchResults.Single().EngineState);
            if (!r.roundTrip) Error(r, "save-roundtrip", -1, 0, "The saved timeline changed on reload.");
            foreach (string issue in GameSaveValidator.Validate(restored)) Error(r, "execution-save", -1, 0, issue);
            show.Status = ShowStatus.ResultsReviewed; new ShowResultApplicationService(Id).Apply(save, showResult.Id);
            r.settled = show.Status == ShowStatus.Completed && fighters.All(w => w.Status.OfficialMatchCount == 1);
            if (!r.settled) Error(r, "settlement", -1, 0, "Result application did not complete the show and record appearances.");
            foreach (string issue in GameSaveValidator.Validate(save)) Error(r, "settled-save", -1, 0, issue);
            File.WriteAllText(Path.Combine(path, "settled-save.json"), JsonUtility.ToJson(save));
            File.WriteAllText(Path.Combine(path, "timeline.tsv"), "index\tseconds\tphase\taction\tresult\tactor\ttarget\ttechnique\tdetail\timpact\n" +
                string.Join("\n", engine.Events.Select((e, i) => $"{i}\t{e.MatchTimeSeconds}\t{e.Phase}\t{e.Type}\t{e.Result}\t{e.PerformerId}\t{e.ReceiverId}\t{e.TechniqueId}\t{e.Detail}\t{e.Impact}")));
        }
        catch (Exception ex) { r.exception = ex.ToString(); Error(r, "exception", -1, 0, ex.Message); }
        File.WriteAllText(Path.Combine(path, "audit.json"), JsonUtility.ToJson(r, true));
        return $"{index + 1:00} {r.label} {r.plannedMinutes}m: {r.eventCount} events, {r.actualSeconds}s, {r.actualFinish}, errors {r.errors.Count}, observations {r.observations.Count}";
    }

    public static string ReplayFixture(int index)
    {
        var input = Path.Combine(index < 32 ? SourceFolder : Folder, $"case-{index + 1:00}");
        var output = Path.Combine(Folder, $"case-{index + 1:00}"); Directory.CreateDirectory(output);
        var r = JsonUtility.FromJson<CaseResult>(File.ReadAllText(Path.Combine(input, "audit.json")));
        r.errors.Clear(); r.observations.Clear(); r.motionSamples = 0; r.exception = null;
        string fixture = File.ReadAllText(Path.Combine(input, "fixture.json"));
        File.WriteAllText(Path.Combine(output, "fixture.json"), fixture);
        try
        {
            var registry = Registry(r.noRopeBreak);
            MoveTable.Initialize();
            string Id() => Guid.NewGuid().ToString();
            var planning = new ShowPlanningService(registry, Id, r.seed);
            ShowExecutionService Service() => new(planning, new MatchEvaluator(registry, registry, Id, registry.GetMoveRules), new PromoEvaluator(Id), Id);
            var save = JsonUtility.FromJson<GameSave>(fixture); var show = save.Shows.Single(s => s.Status == ShowStatus.InProgress);
            foreach (string issue in GameSaveValidator.Validate(save)) Error(r, "fixture", -1, 0, issue);
            var venue = registry.Venues["venue_002"];
            var showResult = Service().Execute(save, show.Id, r.seed, venue.Capacity, venue.BaseTicketPrice);
            var result = save.MatchResults.Single(); var engine = result.EngineState;
            r.finished = engine.IsFinished && !engine.IsAborted; r.actualSeconds = engine.ElapsedSeconds;
            r.actualFinish = engine.ActualFinish.ToString(); r.winner = engine.WinnerId; r.eventCount = engine.Events.Count;
            if (!r.finished) Error(r, "finish", -1, 0, engine.FailureReason);
            if (r.actualFinish != r.requestedFinish || engine.ActualFinish != MatchFinishType.Draw && engine.WinnerId != save.MatchPlans.Single().WinnerId) Error(r, "booking", -1, 0, "Outcome differs from booking.");
            AuditTimeline(r, engine);
            var repeat = JsonUtility.FromJson<GameSave>(fixture); var repeatShow = repeat.Shows.Single(s => s.Status == ShowStatus.InProgress);
            Service().Execute(repeat, repeatShow.Id, r.seed, venue.Capacity, venue.BaseTicketPrice);
            r.deterministic = JsonUtility.ToJson(engine) == JsonUtility.ToJson(repeat.MatchResults.Single().EngineState);
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            r.roundTrip = JsonUtility.ToJson(engine) == JsonUtility.ToJson(restored.MatchResults.Single().EngineState);
            if (!r.deterministic || !r.roundTrip) Error(r, "replay", -1, 0, "Seed or serialization differs.");
            foreach (string issue in GameSaveValidator.Validate(restored)) Error(r, "result-save", -1, 0, issue);
            File.WriteAllText(Path.Combine(output, "result.json"), JsonUtility.ToJson(result, true));
            WriteMatchLog(save, result, r, output);
            File.WriteAllText(Path.Combine(output, "replay-save.json"), JsonUtility.ToJson(save));
            show.Status = ShowStatus.ResultsReviewed; new ShowResultApplicationService(Id).Apply(save, showResult.Id);
            r.settled = show.Status == ShowStatus.Completed;
            foreach (string issue in GameSaveValidator.Validate(save)) Error(r, "settled-save", -1, 0, issue);
            File.WriteAllText(Path.Combine(output, "settled-save.json"), JsonUtility.ToJson(save));
            File.WriteAllText(Path.Combine(output, "timeline.tsv"), "seconds\taction\tresult\tactor\tmove\tdetail\tduration\n" +
                string.Join("\n", engine.Events.Select(e => $"{e.MatchTimeSeconds}\t{e.Type}\t{e.Result}\t{e.PerformerId}\t{e.TechniqueId}\t{e.Detail}\t{e.DurationSeconds}")));
        }
        catch (Exception ex) { r.exception = ex.ToString(); Error(r, "exception", -1, 0, ex.Message); }
        File.WriteAllText(Path.Combine(output, "audit.json"), JsonUtility.ToJson(r, true));
        return $"{index + 1:00}: {r.eventCount} events, {r.actualSeconds}s, {r.actualFinish}, errors {r.errors.Count}";
    }

    public static string CreateExtendedFixture(int index)
    {
        if (index < 32 || index >= 44) throw new ArgumentOutOfRangeException(nameof(index));
        MoveTable.Initialize();
        var candidates = JsonUtility.FromJson<Roster>(File.ReadAllText(Path.Combine(SourceFolder, "generated-pool.json"))).wrestlers
            .Where(w => w.Identity.Gender == WrestlerGender.Male).ToList();
        System.Collections.Generic.IEnumerable<MatchTechnique> Eligible(WrestlerState w) => MatchTechniques.All.Where(m =>
            m.Enabled && m.CanBeFinisher && m.AllowsStyle(w.Presentation.WrestlingStyleId) && m.MeetsRequirement(w.Attributes));
        var a = candidates.First(w => index switch
        {
            36 => Eligible(w).Any(m => m.Rules.TargetRegion == BodyRegion.Arms) && Eligible(w).Any(m => m.Rules.TargetRegion == BodyRegion.Legs),
            37 => Eligible(w).Any(m => m.Rules.Kind == MoveKind.Aerial) && Eligible(w).Any(m => m.Rules.Kind == MoveKind.Submission),
            38 => Eligible(w).Any(m => m.Rules.Kind == MoveKind.Submission),
            39 => Eligible(w).Any(m => m.Rules.Kind == MoveKind.Strike), _ => w.Presentation.FinisherMoveIds.Count > 0
        });
        var b = candidates.First(w => w.Id != a.Id && (index != 37 || Eligible(w).Any(m => m.Rules.TargetRegion == BodyRegion.Legs)));
        if (index is 36 or 37)
            a.Presentation.FinisherMoveIds = index == 36 ? new[] { BodyRegion.Arms, BodyRegion.Legs }.Select(region => Eligible(a).First(m => m.Rules.TargetRegion == region).Id).ToList() :
                new[] { MoveKind.Aerial, MoveKind.Submission }.Select(kind => Eligible(a).First(m => m.Rules.Kind == kind).Id).ToList();
        if (index is 38 or 39) a.Presentation.FinisherMoveIds = new() { Eligible(a).First(m => m.Rules.Kind == (index == 38 ? MoveKind.Submission : MoveKind.Strike)).Id };
        if (index is 38 or 39) a.Presentation.SignatureMoveIds.Clear();
        if (index == 37) { b.Presentation.FinisherMoveIds = new() { Eligible(b).First(m => m.Rules.TargetRegion == BodyRegion.Legs).Id }; b.Attributes.RingPsychology = 18; a.Condition.Condition = 35; }
        if (index == 32) { a.Status.StatusValue = 80; b.Status.StatusValue = 10; }
        if (index == 34) a.Condition.Condition = 20;
        if (index == 35) { a.Condition.InjuryStatus = InjuryStatus.Minor; a.Condition.RecoveryDate = new(new GameDate(2026, 6, 20)); }
        var roster = new[] { a, b }.Concat(candidates.Where(w => w.Id != a.Id && w.Id != b.Id)).Take(8).ToList();
        int offset = index - 32;
        var r = new CaseResult { index = index, seed = 20261009 + index * 97, noRopeBreak = index == 40,
            plannedMinutes = new[] { 5, 10, 15, 20, 30, 45, 10, 15, 20, 30, 15, 10 }[offset],
            label = new[] { "status-gap", "grudge", "low-condition", "existing-injury", "arm-leg-finishers", "injury-goal-transition",
                "submission-only-pin", "strike-only-submission", "no-rope-break", "ironman", "elimination", "cage-escape" }[offset] };
        r.format = index == 42 ? "4-way-elimination" : "singles";
        var registry = Registry(r.noRopeBreak); string Id() => Guid.NewGuid().ToString();
        var save = new GameStartService(Id).CreateInitialSave(new GameStartRequest { PromotionName = "Match Improvements", PromotionAbbreviation = "MAI",
            InitialCash = 1000000, WorldSeed = r.seed, UtcNow = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc),
            WrestlerContracts = roster.Select(w => new InitialWrestlerContractInput { Wrestler = w, ContractEndYear = 2027, MonthlySalary = 500 }).ToList(),
            RegularVenueId = "venue_002", RegularVenueProductionCost = 1000, RegularShowFrequency = RegularShowFrequency.Biweekly, PpvFrequency = PpvFrequency.Quarterly });
        var ids = roster.Take(index == 42 ? 4 : 2).Select(w => w.Id).ToList();
        if (index == 33) save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = a.Id, SecondWrestlerId = b.Id, Value = -95 });
        var finish = index == 39 ? MatchFinishType.Submission : index == 43 ? MatchFinishType.Escape : MatchFinishType.Pinfall;
        r.requestedFinish = finish.ToString();
        var planning = new ShowPlanningService(registry, Id, r.seed); var show = save.Shows.First(s => s.ShowType == ScheduledShowType.Regular);
        show.DurationLimit = r.plannedMinutes;
        planning.AddMatch(save, show.Id, new MatchPlanState { MatchTypeId = "matchtype_001", MatchGimmickId = index == 43 ? "gimmick_001" : "gimmick_000",
            ParticipantIds = ids, WinnerId = a.Id, LoserTargetId = b.Id, FinishType = finish, IronMan = index == 41, Elimination = index == 42 }, r.plannedMinutes);
        planning.Confirm(save, show.Id); save.CurrentDate = show.Date; StorylineService.StartShow(save, show);
        r.participants = ids.Select(id => save.Wrestlers.Single(w => w.Id == id)).Select(w => new Profile { id = w.Id, name = w.Identity.RingName,
            gender = w.Identity.Gender.ToString(), background = w.Identity.Background.ToString(), style = w.Presentation.WrestlingStyleId,
            psychology = w.Attributes.RingPsychology, stamina = w.Attributes.Stamina, technical = w.Attributes.Technical, power = w.Attributes.Power,
            flying = w.Attributes.HighFlying, signatures = w.Presentation.SignatureMoveIds, finishers = w.Presentation.FinisherMoveIds }).ToList();
        var path = Path.Combine(Folder, $"case-{index + 1:00}"); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "fixture.json"), JsonUtility.ToJson(save));
        File.WriteAllText(Path.Combine(path, "audit.json"), JsonUtility.ToJson(r, true));
        return $"{index + 1:00}: {r.label}, {r.plannedMinutes}m fixture ready";
    }

    private static void AuditTimeline(CaseResult r, MatchEngineState engine)
    {
        double weight = 0, xSum = 0, zSum = 0, center = 0, edge = 0;
        var priorPoints = new Dictionary<string, Vector2>(); r.centerLineCrossings = 0;
        foreach (var e in engine.Events)
        {
            if (e.Motion == null) continue;
            foreach (var p in e.Before.Participants.Where(p => !p.IsEliminated && (!engine.Rules.Tag || engine.Rules.Tornado || p.IsLegal)))
            for (int sample = 0; sample < 20; sample++)
            {
                var pose = MatchMovement.Sample(e, p.Id, (sample + .5f) / 20);
                if (Math.Max(Math.Abs(pose.X), Math.Abs(pose.Z)) > 1) continue;
                double seconds = e.DurationSeconds / 20d;
                weight += seconds; xSum += pose.X * seconds; zSum += pose.Z * seconds;
                float radius = Math.Max(Math.Abs(pose.X), Math.Abs(pose.Z));
                if (radius <= .35f) center += seconds;
                if (radius >= .75f) edge += seconds;
                if (priorPoints.TryGetValue(p.Id, out var prior) && (prior.x * pose.X < 0 || prior.y * pose.Z < 0)) r.centerLineCrossings++;
                priorPoints[p.Id] = new Vector2(pose.X, pose.Z);
            }
        }
        r.weightedMeanX = weight == 0 ? 0 : (float)(xSum / weight); r.weightedMeanZ = weight == 0 ? 0 : (float)(zSum / weight);
        r.centerShare = weight == 0 ? 0 : (float)(center / weight); r.edgeShare = weight == 0 ? 0 : (float)(edge / weight);
        r.actions = engine.Events.GroupBy(e => e.Type).Select(g => new Count { name = g.Key.ToString(), events = g.Count(), seconds = g.Sum(e => e.DurationSeconds) }).ToList();
        r.phases = engine.Events.GroupBy(e => e.Phase).Select(g => new Count { name = g.Key.ToString(), events = g.Count(), seconds = g.Sum(e => e.DurationSeconds) }).ToList();
        r.techniques = engine.Events.Where(e => !string.IsNullOrEmpty(e.TechniqueId) || !string.IsNullOrEmpty(e.MoveId)).GroupBy(e => string.IsNullOrEmpty(e.TechniqueId) ? e.MoveId : e.TechniqueId).Select(g => new Count { name = g.Key, events = g.Count() }).ToList();
        r.distinctTechniques = r.techniques.Count; r.successfulHits = engine.Events.Count(e => e.Impact > 0 && e.Result is MatchActionResult.FullSuccess or MatchActionResult.PartialSuccess or MatchActionResult.ReceptionFailure);
        r.firstFinisherSeconds = engine.Events.FirstOrDefault(e => e.Type == MatchActionType.Finisher)?.MatchTimeSeconds ?? -1;
        int expectedTime = 0, idle = 0, repeated = 0, top = 0, positions = 0; string previous = null;
        for (int i = 0; i < engine.Events.Count; i++)
        {
            var e = engine.Events[i];
            if (e.MatchTimeSeconds != expectedTime || e.DurationSeconds <= 0) Error(r, "timeline", i, e.MatchTimeSeconds, "Gap, overlap or invalid duration.");
            expectedTime = e.MatchTimeSeconds + e.DurationSeconds;
            if (e.CauseEventIndex >= i || e.CauseEventIndex < -1) Error(r, "cause", i, e.MatchTimeSeconds, "Invalid preceding event reference.");
            if (e.Before == null || e.After == null || e.Motion == null) { Error(r, "missing-state", i, e.MatchTimeSeconds, "Snapshots or recorded motion missing."); continue; }
            foreach (var p in e.Before.Participants)
            {
                r.maxFatigue = Math.Max(r.maxFatigue, e.After.Participants.Single(x => x.Id == p.Id).Fatigue);
                if (i > 0)
                {
                    var last = engine.Events[i - 1].After.Participants.Single(x => x.Id == p.Id);
                    if (Math.Abs(last.RingX - p.RingX) + Math.Abs(last.RingZ - p.RingZ) + Math.Abs(last.Height - p.Height) > .0001f)
                        Error(r, "spatial-continuity", i, e.MatchTimeSeconds, p.Id + " jumped at the event boundary.");
                }
                var start = MatchMovement.Sample(e, p.Id, 0); var end = MatchMovement.Sample(e, p.Id, 1);
                var after = e.After.Participants.Single(x => x.Id == p.Id);
                if (Math.Abs(start.X - p.RingX) + Math.Abs(start.Z - p.RingZ) + Math.Abs(start.Height - p.Height) > .0001f ||
                    Math.Abs(end.X - after.RingX) + Math.Abs(end.Z - after.RingZ) + Math.Abs(end.Height - after.Height) > .0001f)
                    Error(r, "motion-endpoint", i, e.MatchTimeSeconds, p.Id + " motion disagrees with saved snapshots.");
                if (p.IsLegal && !p.IsEliminated) { positions++; if (p.RingZ > .15f) top++; }
                for (int sample = 0; sample <= 20; sample++)
                {
                    var pose = MatchMovement.Sample(e, p.Id, sample / 20f); r.motionSamples++;
                    if (!float.IsFinite(pose.X + pose.Z + pose.Height + pose.FacingX + pose.FacingZ) || Math.Max(Math.Abs(pose.X), Math.Abs(pose.Z)) > 1.401f)
                        Error(r, "motion-bounds", i, e.MatchTimeSeconds, p.Id + " invalid pose at " + sample / 20f);
                    if (!string.IsNullOrEmpty(pose.GripTargetId) && (pose.GripTargetId == p.Id || !engine.Participants.Any(x => x.Id == pose.GripTargetId)))
                        Error(r, "grip", i, e.MatchTimeSeconds, "Invalid controlled participant.");
                }
            }
            var technique = MatchTechniques.Find(string.IsNullOrEmpty(e.TechniqueId) ? e.MoveId : e.TechniqueId);
            if (technique != null && e.PerformerId != null)
            {
                var actor = engine.GetParticipant(e.PerformerId);
                if (!technique.Enabled || !technique.AllowsStyle(actor.WrestlingStyleId) || !technique.MeetsRequirement(actor.Attributes))
                    Error(r, "technique-pool", i, e.MatchTimeSeconds, e.TechniqueId + " violates the performer's move pool.");
                if (e.Type == MatchActionType.Signature && !actor.SignatureMoveIds.Contains(technique.Id) ||
                    e.Type == MatchActionType.Finisher && !actor.FinisherMoveIds.Contains(technique.Id))
                    Error(r, "personal-move", i, e.MatchTimeSeconds, "Assigned signature/finisher mismatch.");
            }
            idle = e.Type is MatchActionType.Rest or MatchActionType.Recover or MatchActionType.Reposition or MatchActionType.Retreat ? idle + e.DurationSeconds : 0;
            r.maxIdleSeconds = Math.Max(r.maxIdleSeconds, idle);
            string action = e.PerformerId + "/" + e.Type + "/" + e.TechniqueId;
            repeated = action == previous ? repeated + 1 : 1; previous = action; r.maxRepeatedActions = Math.Max(r.maxRepeatedActions, repeated);
        }
        if (expectedTime != engine.ElapsedSeconds || engine.Events.Count(e => e.Type == MatchActionType.Finish) != 1 || engine.Events.Last().Type != MatchActionType.Finish)
            Error(r, "ending", -1, expectedTime, "Timeline has a missing or duplicate final ending.");
        r.ringTopShare = positions > 0 ? (float)top / positions : 0;
        if (r.maxIdleSeconds >= 45) Observe(r, "idle-run", "Consecutive recovery/reposition/rest reached " + r.maxIdleSeconds + " seconds.");
        if (r.maxRepeatedActions >= 5) Observe(r, "repetition", "The same performer repeated the same action/technique " + r.maxRepeatedActions + " times.");
        if (r.ringTopShare > .8f) Observe(r, "ring-top", "Over 80% of active start positions stayed in the upper ring.");
        if (Math.Abs(r.actualSeconds - r.plannedMinutes * 60) > r.plannedMinutes * 3) Error(r, "duration", -1, r.actualSeconds, "Actual duration differs from the plan by over five percent.");
    }

    public static string CheckPlayback(int index)
    {
        string path = Path.Combine(Folder, $"case-{index + 1:00}");
        var r = JsonUtility.FromJson<CaseResult>(File.ReadAllText(Path.Combine(path, "audit.json")));
        var save = JsonUtility.FromJson<GameSave>(File.ReadAllText(Path.Combine(path, "replay-save.json")));
        var engine = save.MatchResults.Single().EngineState;
        {
            var arena = UnityEngine.Object.FindAnyObjectByType<TopViewArenaTest>();
            var oldEngine = arena.Engine; var oldWrestlers = arena.Wrestlers; float clock = arena.clock; bool paused = arena.paused;
            try
            {
                arena.paused = true;
                typeof(TopViewArenaTest).GetProperty("Wrestlers").SetValue(arena, engine.Participants.Select(p => save.Wrestlers.Single(w => w.Id == p.Id)).ToArray());
                arena.LoadMatch(engine); TopViewArenaTestBuilder.CheckMatchPlayback(); r.playback = "passed";
            }
            catch (Exception ex) { r.playback = ex.Message; Error(r, "playback", -1, 0, ex.Message); }
            finally
            {
                typeof(TopViewArenaTest).GetProperty("Wrestlers").SetValue(arena, oldWrestlers);
                arena.LoadMatch(oldEngine); arena.Seek(clock); arena.paused = paused;
            }
        }
        File.WriteAllText(Path.Combine(path, "audit.json"), JsonUtility.ToJson(r, true));
        return $"{index + 1:00}: {r.playback}";
    }

    [Serializable] public sealed class ReplayDifference
    {
        public int eventIndex, seconds;
        public string action, moveId, moveName, liveKind, loadedKind;
        public bool liveHit, loadedHit;
        public float maxMarkerDifference, liveMaxSubmission, loadedMaxSubmission, maxRateDifference;
    }

    public static string CheckSavedSemantics(int index)
    {
        string path = Path.Combine(Folder, $"case-{index + 1:00}");
        MoveTable.Initialize();
        var r = JsonUtility.FromJson<CaseResult>(File.ReadAllText(Path.Combine(path, "audit.json")));
        var registry = Registry(r.noRopeBreak);
        var save = JsonUtility.FromJson<GameSave>(File.ReadAllText(Path.Combine(path, "fixture.json")));
        string Id() => Guid.NewGuid().ToString();
        var planning = new ShowPlanningService(registry, Id, r.seed);
        var execution = new ShowExecutionService(planning, new MatchEvaluator(registry, registry, Id, registry.GetMoveRules), new PromoEvaluator(Id), Id);
        var show = save.Shows.Single(s => s.Status == ShowStatus.InProgress);
        var venue = registry.Venues["venue_002"];
        execution.Execute(save, show.Id, r.seed, venue.Capacity, venue.BaseTicketPrice);
        var live = save.MatchResults.Single().EngineState;
        var loaded = JsonUtility.FromJson<MatchEngineState>(JsonUtility.ToJson(live));
        r.errors.Clear(); r.observations.Clear(); r.motionSamples = 0;
        AuditTimeline(r, loaded);
        File.WriteAllText(Path.Combine(path, "audit.json"), JsonUtility.ToJson(r, true));
        var differences = new List<ReplayDifference>();
        var arena = UnityEngine.Object.FindAnyObjectByType<TopViewArenaTest>();
        var oldEngine = arena.Engine; var oldWrestlers = arena.Wrestlers; float clock = arena.clock; bool paused = arena.paused;
        try
        {
            arena.paused = true;
            for (int i = 0; i < live.Events.Count; i++)
            {
                var original = live.Events[i]; var restored = loaded.Events[i];
                var originalMove = MatchTechniques.Find(original);
                var restoredMove = MatchTechniques.Find(restored);
                if (originalMove?.Id == restoredMove?.Id && original.Type is not (MatchActionType.Signature or MatchActionType.Finisher)) continue;
                var d = new ReplayDifference { eventIndex = i, seconds = original.MatchTimeSeconds, action = original.Type.ToString(),
                    moveId = originalMove?.Id, moveName = originalMove?.Rules.Name, liveKind = originalMove?.Rules.Kind.ToString(),
                    loadedKind = restoredMove?.Rules.Kind.ToString(), liveHit = TopViewArenaTest.IsHit(original), loadedHit = TopViewArenaTest.IsHit(restored) };
                if (live.Participants.Count >= 2)
                {
                    typeof(TopViewArenaTest).GetProperty("Wrestlers").SetValue(arena, live.Participants.Select(p => save.Wrestlers.Single(w => w.Id == p.Id)).ToArray());
                    var positions = new Vector3[live.Participants.Count, 41]; var rates = new float[41];
                    arena.LoadMatch(live);
                    for (int s = 0; s <= 40; s++)
                    {
                        arena.Seek(original.MatchTimeSeconds + original.DurationSeconds * s / 40f);
                        for (int p = 0; p < live.Participants.Count; p++) positions[p, s] = arena.MarkerFor(live.Participants[p].Id).position;
                        rates[s] = arena.PlaybackRate(original);
                        d.liveMaxSubmission = Math.Max(d.liveMaxSubmission, arena.SubmissionEnvelope);
                    }
                    arena.LoadMatch(loaded);
                    for (int s = 0; s <= 40; s++)
                    {
                        arena.Seek(restored.MatchTimeSeconds + restored.DurationSeconds * s / 40f);
                        for (int p = 0; p < live.Participants.Count; p++) d.maxMarkerDifference = Math.Max(d.maxMarkerDifference,
                            Vector3.Distance(positions[p, s], arena.MarkerFor(live.Participants[p].Id).position));
                        d.maxRateDifference = Math.Max(d.maxRateDifference, Math.Abs(rates[s] - arena.PlaybackRate(restored)));
                        d.loadedMaxSubmission = Math.Max(d.loadedMaxSubmission, arena.SubmissionEnvelope);
                    }
                }
                differences.Add(d);
                if (originalMove?.Id != restoredMove?.Id || d.maxMarkerDifference > .001f || d.maxRateDifference > .001f ||
                    Math.Abs(d.liveMaxSubmission - d.loadedMaxSubmission) > .001f || d.liveHit != d.loadedHit)
                    Error(r, "saved-semantics", i, original.MatchTimeSeconds, "Saved playback or rule classification differs.");
            }
        }
        finally
        {
            typeof(TopViewArenaTest).GetProperty("Wrestlers").SetValue(arena, oldWrestlers);
            arena.LoadMatch(oldEngine); arena.Seek(clock); arena.paused = paused;
        }
        File.WriteAllText(Path.Combine(path, "replay-semantics.json"), JsonUtility.ToJson(new ReplayDifferences { events = differences }, true));
        File.WriteAllText(Path.Combine(path, "audit.json"), JsonUtility.ToJson(r, true));
        int ruleDifferences = live.Events.Zip(loaded.Events, (a, b) => MatchTechniques.Find(a)?.Id != MatchTechniques.Find(b)?.Id).Count(x => x);
        return $"{index + 1:00}: {differences.Count} assigned events compared, {ruleDifferences} rule differences, {differences.Count(d => d.maxMarkerDifference > .001f)} motion differences, errors {r.errors.Count}";
    }
    [Serializable] private sealed class ReplayDifferences { public List<ReplayDifference> events; }

    public static string CheckContexts(int index)
    {
        MoveTable.Initialize();
        string path = Path.Combine(Folder, $"case-{index + 1:00}");
        var engine = JsonUtility.FromJson<MatchResultState>(File.ReadAllText(Path.Combine(path, "result.json"))).EngineState;
        var findings = new List<Finding>(); int checkedAttacks = 0, charges = 0;
        for (int i = 0; i < engine.Events.Count; i++)
        {
            var e = engine.Events[i];
            var move = MatchTechniques.Find(string.IsNullOrEmpty(e.TechniqueId) ? e.MoveId : e.TechniqueId);
            if (move == null || e.ReceiverId == null || e.Type is MatchActionType.PrepareAttack or MatchActionType.Climb) continue;
            var a = e.Before.Participants.FirstOrDefault(p => p.Id == e.PerformerId);
            var b = e.Before.Participants.FirstOrDefault(p => p.Id == e.ReceiverId);
            if (a == null || b == null) continue;
            checkedAttacks++;
            MatchEngineParticipant Participant(MatchParticipantSnapshot p) => new() { Id = p.Id, IsDowned = p.IsDowned,
                Posture = p.Posture, RingX = p.RingX, RingZ = p.RingZ, FacingX = p.FacingX, FacingZ = p.FacingZ };
            if (!MatchTechniques.FitsSituation(move.Rules.Situation, Participant(a), Participant(b)))
                findings.Add(new Finding { code = "target-posture", eventIndex = i, seconds = e.MatchTimeSeconds, message = move.Id + "/" + move.Rules.Situation + " target " + b.Posture });
            if (engine.Rules.Tag && !engine.Rules.Tornado && (!a.IsLegal || !b.IsLegal))
                findings.Add(new Finding { code = "tag-legal", eventIndex = i, seconds = e.MatchTimeSeconds, message = move.Id });
            if (move.Rules.ExecutionStyle == MoveExecutionStyle.Running && e.Impact > 0 && TopViewArenaTest.IsHit(e))
            {
                var contact = e.Motion.FirstOrDefault(f => f.Progress == .65f)?.Participants.FirstOrDefault(p => p.Id == a.Id && p.HasVelocity);
                if (contact != null)
                {
                    charges++;
                    if (contact.VelocityX * contact.VelocityX + contact.VelocityZ * contact.VelocityZ < .0001f)
                        findings.Add(new Finding { code = "charge-stop", eventIndex = i, seconds = e.MatchTimeSeconds, message = move.Id });
                }
            }
        }
        File.WriteAllText(Path.Combine(path, "contexts.json"), JsonUtility.ToJson(new ContextResult { checkedAttacks = checkedAttacks, charges = charges, findings = findings }, true));
        return $"{index + 1:00}: {checkedAttacks} move contexts, {charges} moving impacts, {findings.Count} findings";
    }
    [Serializable] private sealed class ContextResult { public int checkedAttacks, charges; public List<Finding> findings; }

    [Serializable] public sealed class StatSample
    {
        public string stat; public int seed, value, attacks, limbPreparations, covers, finisherAttempts, finisherHits, receptionFailures, actionKinds;
        public float fatigue, execution, validCoverRatio, preparationTransitionRatio, contextlessRepeatRatio, observedQuality, sellingQuality;
    }
    [Serializable] private sealed class StatSamples { public List<StatSample> samples = new(); }

    private static void WriteMatchLog(GameSave save, MatchResultState result, CaseResult r, string path)
    {
        var log = new System.Text.StringBuilder();
        string Name(string id) => save.Wrestlers.FirstOrDefault(w => w.Id == id)?.Identity.RingName ?? "선수";
        log.AppendLine($"# 경기 {r.index + 1:00}: {r.label}");
        log.AppendLine($"\n{r.format}, 예약 {r.plannedMinutes}분 / {r.requestedFinish}, 실행 시드 {r.seed}, 엔진 시드 {result.ResultSeed}");
        foreach (var p in result.EngineState.Participants)
        {
            var w = save.Wrestlers.Single(w => w.Id == p.Id); var a = w.Attributes;
            log.AppendLine($"\n{Name(p.Id)} ({p.Id}): 스타일 {w.Presentation.WrestlingStyleId}, 타격 {a.Brawling:F1}, 힘 {a.Power:F1}, 테크닉 {a.Technical:F1}, 공중기 {a.HighFlying:F1}, 체력 {a.Stamina:F1}, 경기 운영 {a.RingPsychology:F1}, 셀링 {a.Selling:F1}. 피니셔: {string.Join(", ", w.Presentation.FinisherMoveIds.Select(id => MatchTechniques.Find(id)?.Rules.Name ?? id))}");
        }
        log.AppendLine("\n## 시간순 로그\n");
        for (int i = 0; i < result.EngineState.Events.Count; i++)
        {
            var e = result.EngineState.Events[i];
            if (!MatchEventNarration.ShouldNarrate(e, result)) continue;
            int end = i;
            while (end + 1 < result.EngineState.Events.Count && MatchEventNarration.CommentaryIndex(result, end + 1) == i) end++;
            var index = end > i ? $"{i + 1:000}~{end + 1:000}" : $"{i + 1:000}";
            log.AppendLine($"{index} | {e.MatchTimeSeconds / 60:00}:{e.MatchTimeSeconds % 60:00} | 공방 {e.ExchangeId} | {e.Type}/{e.Result} | {PWManager.Domain.Services.MatchEventNarration.Text(e, result, Name)}");
            log.AppendLine();
        }
        log.AppendLine($"결과: {(result.WinnerId == null ? "무승부" : Name(result.WinnerId))}, {result.FinishType}, {result.EngineState.ElapsedSeconds}초, 평가 {result.FinalMatchQuality:F2}/20");
        log.AppendLine("\n<details><summary>이벤트별 자세·피로·부위 손상</summary>\n");
        foreach (var e in result.EngineState.Events)
        foreach (var p in e.After.Participants)
            log.AppendLine($"{e.MatchTimeSeconds / 60:00}:{e.MatchTimeSeconds % 60:00} {Name(p.Id)}: {p.Position}/{p.Posture}, 피로 {p.Fatigue:F1}, 머리 {p.HeadDamage:F1}, 몸통 {p.BodyDamage:F1}, 팔 {p.ArmDamage:F1}, 다리 {p.LegDamage:F1}, 목표 {p.PlannedFinisherMoveId ?? "없음"}\n");
        log.AppendLine("</details>");
        File.WriteAllText(Path.Combine(path, "match-log.md"), log.ToString(), new System.Text.UTF8Encoding(false));
    }

    public static string CompareStat(string stat, string outputFolder = null)
    {
        outputFolder ??= Folder;
        Directory.CreateDirectory(outputFolder);
        MoveTable.Initialize(); var registry = Registry(); var rows = new StatSamples();
        string fixture = File.ReadAllText(Path.Combine(Folder, "case-37", "fixture.json"));
        for (int seed = 0; seed < 20; seed++)
        foreach (int value in new[] { 3, 18 })
        {
            var save = JsonUtility.FromJson<GameSave>(fixture); var plan = save.MatchPlans.Single();
            var subject = save.Wrestlers.Single(w => w.Id == (stat == "Selling" ? plan.LoserTargetId : plan.WinnerId));
            typeof(WrestlerAttributesState).GetField(stat).SetValue(subject.Attributes, (float)value);
            string Id() => Guid.NewGuid().ToString();
            var planning = new ShowPlanningService(registry, Id, seed);
            var service = new ShowExecutionService(planning, new MatchEvaluator(registry, registry, Id, registry.GetMoveRules,
                accidentsEnabled: false), new PromoEvaluator(Id), Id);
            var show = save.Shows.Single(s => s.Status == ShowStatus.InProgress); var venue = registry.Venues["venue_002"];
            service.Execute(save, show.Id, 71000 + seed, venue.Capacity, venue.BaseTicketPrice);
            var match = save.MatchResults.Single(); var engine = match.EngineState;
            string logPath = Path.Combine(outputFolder, "stat-logs", stat, $"seed-{seed:00}", $"value-{value:00}");
            Directory.CreateDirectory(logPath);
            WriteMatchLog(save, match, new CaseResult { index = seed, seed = 71000 + seed, label = $"{stat}={value}",
                format = "singles", plannedMinutes = save.ShowEvents.Single(e => e.DetailId == plan.Id).PlannedDuration,
                requestedFinish = plan.FinishType.ToString() }, logPath);
            if (!engine.IsFinished || engine.IsAborted || Math.Abs(engine.ElapsedSeconds - engine.TargetDurationSeconds) > engine.TargetDurationSeconds / 20)
                throw new InvalidOperationException($"{stat}/{seed}/{value}: match did not finish on time.");
            var events = engine.Events.Where(e => e.PerformerId == subject.Id).ToList();
            var attacks = events.Where(e => e.Impact > 0).ToList();
            var received = engine.Events.Where(e => e.ReceiverId == subject.Id && e.Impact > 0).ToList();
            rows.samples.Add(new StatSample { stat = stat, seed = seed, value = value, attacks = attacks.Count,
                fatigue = engine.Events.Average(e => e.After.Participants.Single(p => p.Id == subject.Id).Fatigue),
                execution = attacks.Count == 0 ? 0 : attacks.Average(e => e.ExecutionQuality),
                limbPreparations = events.Count(e => e.Type == MatchActionType.LimbAttack && !string.IsNullOrEmpty(e.After.Participants.Single(p => p.Id == subject.Id).PlannedFinisherMoveId) &&
                    registry.GetMoveRules(e.After.Participants.Single(p => p.Id == subject.Id).PlannedFinisherMoveId)?.TargetRegion == e.TargetRegion),
                covers = events.Count(e => e.Type == MatchActionType.Cover && events.Any(hit => hit.Type == MatchActionType.Finisher && hit.Impact > 0 &&
                    hit.ReceiverId == e.ReceiverId && hit.MatchTimeSeconds < e.MatchTimeSeconds && e.MatchTimeSeconds - hit.MatchTimeSeconds <= 15)),
                finisherAttempts = events.Count(e => e.Type == MatchActionType.Finisher),
                finisherHits = events.Count(e => e.Type == MatchActionType.Finisher && e.Impact > 0),
                receptionFailures = received.Count(e => e.Result == MatchActionResult.ReceptionFailure),
                actionKinds = events.Select(e => e.Type).Distinct().Count(),
                validCoverRatio = Ratio(events.Where(e => e.Type is MatchActionType.Pin or MatchActionType.Cover), e => e.ValidCover),
                preparationTransitionRatio = Ratio(events.Where(e => e.TargetCompleted && e.PreparationAvailable && e.GoalMoveId != null),
                    e => events.Where(next => next.MatchTimeSeconds > e.MatchTimeSeconds).Take(3).Any(next => next.IsPreparation && next.GoalMoveId == e.GoalMoveId)),
                contextlessRepeatRatio = Ratio(events.Where(e => e.HasDecisionEvidence && (e.TechniqueId ?? e.MoveId) != null), e => !e.ContextualChoice),
                observedQuality = match.TechnicalEvaluation.ObservedExecution * .35f + match.TechnicalEvaluation.ObservedStructure * .25f +
                    match.TechnicalEvaluation.ObservedDecisions * .25f + match.TechnicalEvaluation.ObservedSelling * .15f,
                sellingQuality = engine.Events.Where(e => e.SellingWrestlerId == subject.Id).Select(e => e.SellingQuality).DefaultIfEmpty(.5f).Average() });
        }
        File.WriteAllText(Path.Combine(outputFolder, $"stat-{stat}.json"), JsonUtility.ToJson(rows, true));
        return stat + ": 20 pairs completed";
        static float Ratio(IEnumerable<MatchEngineEvent> source, Func<MatchEngineEvent, bool> eligible)
        { var values = source.ToList(); return values.Count == 0 ? 0 : (float)values.Count(eligible) / values.Count; }
    }

    public static string CompareRecordedDecisions()
    {
        PWManager.Data.Loading.MoveTable.Initialize();
        var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<PWManager.Data.Catalogs.StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset");
        var registry = new PWManager.Data.Loading.StaticContentRegistry(catalog);
        var rows = new System.Collections.Generic.List<string> { "version,case,validCovers,preparationTransitions,contextlessRepeats,eligibleCovers,eligibleTransitions,eligibleTechniques" };
        foreach (var path in System.IO.Directory.GetFiles("artifacts/match-health-audit", "result.json", System.IO.SearchOption.AllDirectories))
        foreach (bool baseline in new[] { true, false })
        {
            var caseName = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
            var source = baseline ? System.IO.Path.Combine("artifacts/match-scenario-improved-20261009", caseName, "result.json") : path;
            var result = UnityEngine.JsonUtility.FromJson<PWManager.Domain.Models.MatchResultState>(System.IO.File.ReadAllText(source));
            var state = result.EngineState;
            foreach (var e in state.Events)
            {
                if (string.IsNullOrEmpty(e.TechniqueId)) e.TechniqueId = null;
                if (string.IsNullOrEmpty(e.MoveId)) e.MoveId = null;
            }
            if (baseline)
            {
                foreach (var e in state.Events)
                {
                    if (e.Before == null || e.After == null) continue;
                    var snapshot = new PWManager.Domain.Services.MatchEngineState { Rules = state.Rules, Booking = state.Booking,
                        ElapsedSeconds = e.MatchTimeSeconds, TargetDurationSeconds = state.TargetDurationSeconds, FindMove = registry.GetMoveRules,
                        Events = state.Events.TakeWhile(prior => prior != e).ToList() };
                    snapshot.Participants = state.Participants.Select(p => UnityEngine.JsonUtility.FromJson<PWManager.Domain.Services.MatchEngineParticipant>(UnityEngine.JsonUtility.ToJson(p))).ToList();
                    foreach (var p in snapshot.Participants)
                    {
                        var after = e.After.Participants.Single(s => s.Id == p.Id);
                        foreach (var field in typeof(PWManager.Domain.Services.MatchParticipantSnapshot).GetFields())
                        {
                            var destination = typeof(PWManager.Domain.Services.MatchEngineParticipant).GetField(field.Name);
                            if (destination != null && destination.FieldType == field.FieldType) destination.SetValue(p, field.GetValue(after));
                        }
                    }
                    e.IntentionalCombination = e.Detail?.Contains("combo") == true || e.Detail == "hold" ||
                        e.Type is PWManager.Domain.Services.MatchActionType.Chain or PWManager.Domain.Services.MatchActionType.Hold;
                    PWManager.Domain.Services.MatchHealth.RecordEvidence(snapshot, e, e.Before);
                }
            }
            var evidence = state.Events.Where(e => e.HasDecisionEvidence).ToList();
            var covers = evidence.Where(e => e.Type is PWManager.Domain.Services.MatchActionType.Cover or PWManager.Domain.Services.MatchActionType.Pin).ToList();
            var opportunities = evidence.Where(e => e.TargetCompleted && e.PreparationAvailable && !string.IsNullOrEmpty(e.GoalMoveId)).ToList();
            var techniques = evidence.Where(e => (e.TechniqueId ?? e.MoveId) != null).ToList();
            float Ratio(int numerator, int denominator) => denominator == 0 ? 0 : (float)numerator / denominator;
            var prepared = opportunities.Count(e => evidence.Where(next => next.PerformerId == e.PerformerId && next.MatchTimeSeconds > e.MatchTimeSeconds)
                .Take(3).Any(next => next.IsPreparation && next.GoalMoveId == e.GoalMoveId));
            rows.Add(System.FormattableString.Invariant($"{(baseline ? 3 : 4)},{caseName},{Ratio(covers.Count(e => e.ValidCover), covers.Count)},{Ratio(prepared, opportunities.Count)},{Ratio(techniques.Count(e => !e.ContextualChoice), techniques.Count)},{covers.Count},{opportunities.Count},{techniques.Count}"));
        }
        System.IO.File.WriteAllLines("artifacts/match-health-recorded-decisions.csv", rows);
        return "44 recorded baseline/current pairs compared using the same evidence rules";
        
    }

    private static void Error(CaseResult r, string code, int index, int seconds, string message) =>
        r.errors.Add(new Finding { code = code, eventIndex = index, seconds = seconds, message = message });
    private static void Observe(CaseResult r, string code, string message) =>
        r.observations.Add(new Finding { code = code, eventIndex = -1, message = message });
}
