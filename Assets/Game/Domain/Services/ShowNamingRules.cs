using System;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class ShowNamingRules
    {
        public static string Compose(string promotionAbbreviation, string title, ScheduledShowType showType, int regularSequence, int year)
        {
            if (string.IsNullOrWhiteSpace(promotionAbbreviation)) throw new ArgumentException("Promotion abbreviation is required.", nameof(promotionAbbreviation));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Show title is required.", nameof(title));
            if (showType == ScheduledShowType.Regular && regularSequence <= 0) throw new ArgumentOutOfRangeException(nameof(regularSequence));
            if (showType != ScheduledShowType.Regular && year <= 0) throw new ArgumentOutOfRangeException(nameof(year));

            return showType == ScheduledShowType.Regular
                ? $"{promotionAbbreviation.Trim()} {title.Trim()} #{regularSequence}"
                : $"{promotionAbbreviation.Trim()} {title.Trim()} {year}";
        }
    }
}
