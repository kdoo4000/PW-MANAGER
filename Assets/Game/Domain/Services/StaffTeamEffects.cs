using System;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public readonly struct StaffTeamModifiers
    {
        public readonly float Recovery, Aggravation, TicketDemand, NewFans, LiveReaction, BroadcastReaction;
        public StaffTeamModifiers(float recovery = 1, float aggravation = 1, float ticketDemand = 1,
            float newFans = 1, float liveReaction = 1, float broadcastReaction = 1)
        { Recovery = recovery; Aggravation = aggravation; TicketDemand = ticketDemand; NewFans = newFans; LiveReaction = liveReaction; BroadcastReaction = broadcastReaction; }
    }

    public static class StaffTeamEffects
    {
        private static Func<StaffDepartmentType, int, StaffTeamModifiers> lookup;
        public static void Configure(Func<StaffDepartmentType, int, StaffTeamModifiers> rules) => lookup = rules ?? throw new ArgumentNullException(nameof(rules));
        public static StaffTeamModifiers For(GameSave save, StaffDepartmentType type)
        {
            var level = save.StaffDepartments.FirstOrDefault(x => x.DepartmentType == type)?.CurrentLevel ?? 0;
            return level <= 0 ? new StaffTeamModifiers(1) : lookup?.Invoke(type, level) ?? Default(type, level);
        }
        // Domain-only callers use the built-in levels until a content catalog is loaded.
        public static StaffTeamModifiers Default(StaffDepartmentType type, int level)
        {
            level = Math.Clamp(level, 1, 5);
            return type switch
            {
                StaffDepartmentType.Medical => new StaffTeamModifiers(recovery: 1 - (level - 1) * .05f, aggravation: 1 - (level - 1) * .1f),
                StaffDepartmentType.Promotion => new StaffTeamModifiers(ticketDemand: 1 + (level - 1) * .05f, newFans: 1 + (level - 1) * .03f),
                StaffDepartmentType.Commentary => new StaffTeamModifiers(liveReaction: 1 + level * .02f, broadcastReaction: 1 + level * .03f),
                _ => new StaffTeamModifiers(1)
            };
        }
    }
}
