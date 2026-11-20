using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class PromotionProgressionRules
    {
        private static readonly long[] Thresholds = { 0, 300, 1000, 2500, 5500, 11000, 20000, 34000, 55000 };
        public static int Tier(long prestige)
        { var tier = 0; while (tier < 8 && prestige >= Thresholds[tier + 1]) tier++; return tier; }
        public static string TierId(int tier) => $"progression.tier.{tier}";
        public static int ActiveTier(GameSave save)
        {
            var owned = Enumerable.Range(1, 8).Where(x => save.Promotion.UnlockedIds.Contains(TierId(x))).DefaultIfEmpty(0).Max();
            return save.Promotion.QuarterlyGrowth?.IsInitialized == true ? owned : Math.Max(owned, LegacyTier(save));
        }
        public static long ActivePrestige(GameSave save) => Thresholds[ActiveTier(save)];
        public static List<string> Unlocks(GameSave save, long prestige)
        {
            var ids = new List<string>(save.Promotion.UnlockedIds);
            var tier = Math.Max(ActiveTier(save), Tier(prestige));
            for (var i = 1; i <= tier; i++) if (!ids.Contains(TierId(i))) ids.Add(TierId(i));
            return ids;
        }
        public static int LegacyTier(GameSave save)
        {
            var p = save.SeasonPolicy; var tier = Tier(save.Promotion.PromotionPrestige);
            if (p != null)
            {
                tier = Math.Max(tier, p.RegularShowFrequency switch { RegularShowFrequency.Weekly => 1, RegularShowFrequency.TwiceWeekly => 3, RegularShowFrequency.ThreeTimesWeekly => 6, _ => 0 });
                tier = Math.Max(tier, p.PpvFrequency == PpvFrequency.Monthly ? 4 : p.PpvFrequency == PpvFrequency.Bimonthly ? 2 : 0);
            }
            foreach (var s in save.Shows)
                tier = Math.Max(tier, s.ShowType == ScheduledShowType.PpvMajor ? 4 : s.ShowType == ScheduledShowType.PpvSignature ? 2 : 0);
            return tier;
        }
        public static int MaxRegular(int tier) => tier >= 6 ? 4 : tier >= 3 ? 3 : tier >= 1 ? 2 : 1;
        public static int MaxPpvFrequency(int tier) => tier >= 4 ? 3 : tier >= 2 ? 2 : 1;
        public static List<ScheduledShowType> PpvTypes(int tier)
        {
            var types = new List<ScheduledShowType> { ScheduledShowType.PpvRegular };
            if (tier >= 4) types.Add(ScheduledShowType.PpvMajor);
            if (tier >= 2) types.Add(ScheduledShowType.PpvSignature);
            return types;
        }
        public static void ValidateOperations(int tier, RegularShowFrequency regular, PpvFrequency frequency, IEnumerable<ScheduledShowType> types)
        {
            if (!Enum.IsDefined(typeof(RegularShowFrequency), regular) || !Enum.IsDefined(typeof(PpvFrequency), frequency) ||
                (int)regular > MaxRegular(tier) || (int)frequency > MaxPpvFrequency(tier) || types.Any(x => !PpvTypes(tier).Contains(x)))
                throw new InvalidOperationException("아직 시즌 해금 조건을 충족하지 않은 운영 설정입니다.");
        }
        public static long NextThreshold(GameSave save) => ActiveTier(save) >= 8 ? 0 : Thresholds[ActiveTier(save) + 1];
        public static IEnumerable<string> NewOptions(GameSave save, long closingPrestige)
        {
            for (var i = ActiveTier(save) + 1; i <= Tier(closingPrestige); i++)
            {
                yield return i switch
                {
                    1 => "정기 쇼 주 1회 · 시설과 스카우트 범위 확대",
                    2 => "PPV 격월 · 대표 PPV",
                    3 => "정기 쇼 주 2회 · 스카우트 범위 확대",
                    4 => "PPV 매월 · 대형 PPV",
                    5 => "스카우트 선수 범위 확대",
                    6 => "정기 쇼 주 3회",
                    7 => "스카우트 선수 범위 확대",
                    _ => "최상위 선수 범위"
                };
            }
        }
    }
}
