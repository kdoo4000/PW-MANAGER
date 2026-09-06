using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class LocalizedNameState
    {
        public string Id;
        public string KoreanName;
        public string EnglishName;

        public LocalizedNameState() { }

        public LocalizedNameState(string id, string koreanName, string englishName)
        {
            Id = id;
            KoreanName = koreanName;
            EnglishName = englishName;
        }
    }

    public static class SystemNames
    {
        public static List<LocalizedNameState> CreateDefaults() => new()
        {
            new("attribute.ring_psychology", "경기 운영", "Ring Psychology"),
            new("attribute.ring_improvisation", "임기응변", "Ring Improvisation"),
            new("attribute.technical", "테크니컬", "Technical"),
            new("attribute.brawling", "브롤링", "Brawling"),
            new("attribute.power", "파워", "Power"),
            new("attribute.high_flying", "하이플라잉", "High Flying"),
            new("attribute.spot_work", "스팟 수행력", "Spot Work"),
            new("attribute.specialty_matches", "특수 경기", "Specialty Matches"),
            new("attribute.selling", "접수력", "Selling"),
            new("attribute.stamina", "스태미나", "Stamina"),
            new("attribute.charisma", "카리스마", "Charisma"),
            new("attribute.mic_work", "마이크", "Mic Work"),
            new("attribute.improvisation", "즉흥성", "Improvisation"),
            new("attribute.acting", "캐릭터 표현력", "Acting"),
            new("attribute.face_work", "페이스 연기", "Face Work"),
            new("attribute.heel_work", "힐 연기", "Heel Work"),
            new("attribute.comedy", "코미디", "Comedy"),
            new("player.role.wrestler_manager", "선수 겸 단장", "Wrestler-Manager"),
            new("player.role.professional_manager", "전문 단장", "Professional Manager"),
            new("player.type.worker", "워커", "Worker"),
            new("player.type.balanced", "균형", "Balanced"),
            new("player.type.showman", "쇼맨", "Showman"),
            new("player.reputation.local", "지역급", "Local"),
            new("player.reputation.regional", "권역급", "Regional"),
            new("player.reputation.national", "전국급", "National"),
            new("player.reputation.star", "스타", "Star"),
            new("player.reputation.legend", "레전드", "Legend")
        };
    }
}
