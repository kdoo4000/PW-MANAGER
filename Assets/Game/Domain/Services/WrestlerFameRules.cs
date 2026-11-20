using System;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class WrestlerFameRules
    {
        public static string Label(long value) => value >= 15000 ? "전설적인 선수" : value >= 5000 ? "세계적인 선수" :
            value >= 2000 ? "전국구 스타" : value >= 500 ? "유명 선수" : value >= 100 ? "지역 유명 선수" : "무명 선수";

        public static long InitialForCareer(int years) => Math.Max(0L, years) * 10;
        public static long InitialForPlayer(PlayerReputation background) => background switch
        {
            PlayerReputation.Local => 100, PlayerReputation.Regional => 500, PlayerReputation.National => 2000,
            PlayerReputation.Star => 5000, PlayerReputation.Legend => 15000,
            _ => throw new InvalidOperationException("시작 명성 배경이 올바르지 않습니다.")
        };
        public static int Milestone(long value) => value >= 5000 ? 2 : value >= 500 ? 1 : 0;
        public static WrestlerFameState Create(long value)
        {
            var state = new WrestlerFameState { Value = value, HighestMilestone = Milestone(value) };
            Validate(state); return state;
        }

        public static double Activity(float score, double minutes)
        {
            if (!float.IsFinite(score) || score < 0 || score > 105 || !double.IsFinite(minutes) || minutes < 0)
                throw new InvalidOperationException("명성 활동 평가가 올바르지 않습니다.");
            return (score >= 90 ? 3 : score >= 75 ? 2 : score >= 50 ? 1 : 0) * Math.Min(1, minutes / 10);
        }

        public static double CalculateGain(long current, long reach, double activity, double title)
        {
            if (current < 0 || reach < 0 || !double.IsFinite(activity) || activity < 0 || activity > 3 ||
                !double.IsFinite(title) || title < 0 || title > 4)
                throw new InvalidOperationException("명성 계산 입력이 올바르지 않습니다.");
            if (reach == 0) return 0;
            var exposure = Math.Sqrt(reach / 100d); var scale = exposure * 100;
            return exposure * (activity + title) * (scale / (scale + current));
        }

        public static void Accumulate(WrestlerFameState state, double gain)
        {
            Validate(state);
            if (!double.IsFinite(gain) || gain < 0) throw new InvalidOperationException("명성 증가량이 올바르지 않습니다.");
            var total = (decimal)state.Remainder + (decimal)gain;
            var whole = decimal.ToInt64(decimal.Floor(total));
            var value = checked(state.Value + whole);
            state.Value = value; state.Remainder = (double)(total - whole);
            state.HighestMilestone = Math.Max(state.HighestMilestone, Milestone(value));
        }

        public static void Validate(WrestlerFameState state)
        {
            if (state == null || state.Value < 0 || !double.IsFinite(state.Remainder) || state.Remainder < 0 || state.Remainder >= 1 ||
                state.HighestMilestone != Milestone(state.Value))
                throw new InvalidOperationException("선수 명성 상태가 올바르지 않습니다.");
        }
    }
}
