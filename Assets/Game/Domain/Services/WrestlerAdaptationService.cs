using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class WrestlerAdaptationService
    {
        public static void InitializeCandidate(WrestlerState person, GameDate? date = null)
        {
            person.Adaptation = new() { IsInitialized = true, StyleId = person.Presentation.WrestlingStyleId, PromotionId = person.PromotionId,
                Alignment = person.Roster.Alignment, LastProcessedDate = date.HasValue ? new(date.Value) : default,
                Role = person.Status.StatusValue >= 75 ? ExpectedRole.MainEventer : person.Status.StatusValue >= 40 ? ExpectedRole.Midcarder : ExpectedRole.Jobber };
        }
        public static void InitializeSave(GameSave save, bool initialGame = false)
        {
            foreach (var person in save.Wrestlers ?? new())
            {
                if (person == null || person.Adaptation?.IsInitialized == true && !(initialGame && person.PromotionId == save.Promotion?.Id)) continue;
                InitializeCandidate(person, save.CurrentDate);
                if (person.PromotionId == save.Promotion?.Id) person.Adaptation.Role = WrestlerBehaviorService.ExpectedRoleAt(save, person, save.CurrentDate);
            }
        }
        public static float Penalty(GameSave save, WrestlerState person, GameDate date)
        {
            var state = person.Adaptation; if (state?.IsInitialized != true) return 0;
            var role = WrestlerBehaviorService.ExpectedRoleAt(save, person, date);
            var styleReady = state.StyleId == person.Presentation.WrestlingStyleId ? state.StyleReadiness : 0;
            var roleReady = state.Role == role && state.Alignment == person.Roster.Alignment ? state.RoleReadiness : 0;
            var environmentReady = state.PromotionId == person.PromotionId ? state.EnvironmentReadiness : 0;
            return .5f * (1 - styleReady) + .3f * (1 - roleReady) + .2f * (1 - environmentReady);
        }
        public static Dictionary<string, WrestlerAdaptationState> PrepareDay(GameSave save, GameDate date)
        {
            Validate(save); var result = new Dictionary<string, WrestlerAdaptationState>(StringComparer.Ordinal);
            foreach (var person in save.Wrestlers.Where(w => w.Adaptation?.IsInitialized == true))
            {
                var previous = person.Adaptation;
                if (previous.LastProcessedDate.HasValue && date.CompareTo(previous.LastProcessedDate.Value) <= 0) continue;
                var employed = person.PromotionId == save.Promotion.Id && (person.Id == save.Player?.WrestlerId ||
                    save.OwnContracts.Any(c => c.PersonId == person.Id && c.Type == ContractType.Wrestler && c.Covers(date)));
                if (!employed)
                {
                    if (previous.PromotionId == save.Promotion.Id)
                    {
                        var departed = previous.Copy(); departed.PromotionId = null; departed.EnvironmentReadiness = 0;
                        departed.LastProcessedDate = new(date); result.Add(person.Id, departed);
                    }
                    continue;
                }
                var next = previous.Copy(); var role = WrestlerBehaviorService.ExpectedRoleAt(save, person, date);
                if (next.StyleId != person.Presentation.WrestlingStyleId) { next.StyleId = person.Presentation.WrestlingStyleId; next.StyleReadiness = 0; }
                if (next.Role != role || next.Alignment != person.Roster.Alignment) { next.Role = role; next.Alignment = person.Roster.Alignment; next.RoleReadiness = 0; }
                if (next.PromotionId != save.Promotion.Id) { next.PromotionId = save.Promotion.Id; next.EnvironmentReadiness = 0; }
                var step = .015f + .003f * (person.Hidden?.Adaptability ?? 10);
                var projected = person.CopyForGroupSimulation();
                MatchHealth.Recover(projected, date);
                if (projected.Condition.InjuryStatus is InjuryStatus.None or InjuryStatus.Minor && projected.Condition.Availability != WrestlerAvailability.Unavailable)
                    next.StyleReadiness = Math.Min(1, next.StyleReadiness + step);
                next.RoleReadiness = Math.Min(1, next.RoleReadiness + step);
                next.EnvironmentReadiness = Math.Min(1, next.EnvironmentReadiness + step);
                next.LastProcessedDate = new(date); result.Add(person.Id, next);
            }
            return result;
        }
        public static void Validate(GameSave save)
        {
            foreach (var person in save.Wrestlers ?? new())
            {
                var state = person?.Adaptation; if (state?.IsInitialized != true) continue;
                if (!Enum.IsDefined(typeof(ExpectedRole), state.Role) || !Enum.IsDefined(typeof(KayfabeAlignment), state.Alignment) ||
                    new[] { state.StyleReadiness, state.RoleReadiness, state.EnvironmentReadiness }.Any(x => !float.IsFinite(x) || x < 0 || x > 1))
                    throw new InvalidOperationException("선수 적응 상태가 올바르지 않습니다.");
                if (state.LastProcessedDate.HasValue)
                    try { var date = state.LastProcessedDate.Value; _ = new DateTime(date.Year, date.Month, date.Day);
                        if (date.CompareTo(save.CurrentDate) > 0) throw new InvalidOperationException("선수 적응 날짜가 미래입니다."); }
                    catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("선수 적응 날짜가 올바르지 않습니다."); }
            }
        }
    }
}
