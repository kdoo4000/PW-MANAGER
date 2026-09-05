using System;
using PWManager.Domain.Models;

namespace PWManager.Presentation
{
    internal static class DashboardText
    {
        public static string Date(GameDate value) => $"{value.Year}년 {value.Month}월 {value.Day}일";
        public static string Money(long value) => $"${value:N0}";
        public static string ShowName(ScheduledShowType value) => value switch
        {
            ScheduledShowType.Regular => "다음 정규 쇼", ScheduledShowType.PpvRegular => "다음 PPV",
            ScheduledShowType.PpvMajor => "다음 메이저 PPV", ScheduledShowType.PpvSignature => "다음 시그니처 PPV", _ => "다음 쇼"
        };
        public static string ShowType(ScheduledShowType value) => value switch
        {
            ScheduledShowType.Regular => "정규 쇼", ScheduledShowType.PpvRegular => "PPV",
            ScheduledShowType.PpvMajor => "메이저 PPV", ScheduledShowType.PpvSignature => "시그니처 PPV", _ => value.ToString()
        };
        public static string ShowStatus(ShowStatus value) => value switch
        {
            PWManager.Domain.Models.ShowStatus.Draft => "초안", PWManager.Domain.Models.ShowStatus.Preparing => "준비 중",
            PWManager.Domain.Models.ShowStatus.Review => "검토 중", PWManager.Domain.Models.ShowStatus.Confirmed => "확정",
            PWManager.Domain.Models.ShowStatus.InProgress => "진행 중", PWManager.Domain.Models.ShowStatus.ResultReview => "결과 확인",
            PWManager.Domain.Models.ShowStatus.ResultsReviewed => "일정 종료 대기", PWManager.Domain.Models.ShowStatus.Completed => "완료",
            _ => value.ToString()
        };
    }
}
