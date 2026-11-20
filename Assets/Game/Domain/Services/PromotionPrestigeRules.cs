using System;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class PromotionPrestigeRules
    {
        public static double SuccessScore(long attendance, int capacity, float quality)
        {
            if (attendance < 0 || capacity <= 0 || !float.IsFinite(quality) || quality < 0 || quality > 100)
                throw new InvalidOperationException("PPV 성공 평가의 관객·수용인원·쇼 점수가 올바르지 않습니다.");
            return Math.Min(100, attendance * 100d / capacity) * .5 + quality * .5;
        }

        public static int PpvBonus(ScheduledShowType type, long attendance, int capacity, float quality)
        {
            if (!Enum.IsDefined(typeof(ScheduledShowType), type)) throw new InvalidOperationException("명성을 계산할 쇼 유형이 올바르지 않습니다.");
            var score = SuccessScore(attendance, capacity, quality);
            if (type == ScheduledShowType.Regular || score < 60) return 0;
            var minimum = type == ScheduledShowType.PpvRegular ? 10 : type == ScheduledShowType.PpvMajor ? 25 : 50;
            var maximum = type == ScheduledShowType.PpvRegular ? 25 : type == ScheduledShowType.PpvMajor ? 60 : 120;
            return (int)Math.Round(minimum + (maximum - minimum) * (score - 60) / 40, MidpointRounding.AwayFromZero);
        }

        public static void Capture(ShowResultState result, ScheduledShowType type, int capacity)
        {
            if (result?.FinancialSettlement == null || result.ShowEvaluation == null)
                throw new InvalidOperationException("PPV 성공도를 기록할 쇼 결과가 없습니다.");
            var score = SuccessScore(result.FinancialSettlement.Attendance, capacity, result.ShowEvaluation.Score);
            var bonus = PpvBonus(type, result.FinancialSettlement.Attendance, capacity, result.ShowEvaluation.Score);
            result.PrestigeVenueCapacity = capacity; result.PrestigeShowType = type; result.PpvSuccessScore = score;
            result.PpvPrestigeBonus = bonus; result.HasPrestigeSnapshot = true;
        }

        public static void Validate(ShowResultState result)
        {
            if (result?.HasPrestigeSnapshot != true) return;
            if (result.FinancialSettlement == null || result.ShowEvaluation == null) throw new InvalidOperationException("단체 명성의 쇼 결과가 없습니다.");
            var score = SuccessScore(result.FinancialSettlement.Attendance, result.PrestigeVenueCapacity, result.ShowEvaluation.Score);
            var bonus = PpvBonus(result.PrestigeShowType, result.FinancialSettlement.Attendance, result.PrestigeVenueCapacity, result.ShowEvaluation.Score);
            if (!double.IsFinite(result.PpvSuccessScore) || Math.Abs(score - result.PpvSuccessScore) > .000001 || result.PpvPrestigeBonus != bonus)
                throw new InvalidOperationException("PPV 성공 보너스가 저장된 쇼 성과와 일치하지 않습니다.");
        }

        public static string PpvReason(string showId)
        {
            if (string.IsNullOrWhiteSpace(showId)) throw new InvalidOperationException("PPV 명성의 쇼 ID가 없습니다.");
            return $"ppv:{showId}";
        }
    }
}
