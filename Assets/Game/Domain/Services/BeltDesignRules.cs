using System;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class BeltDesignRules
    {
        public const int TemplateCount = 10;
        public const int EmblemCount = 7;
        public const int MaximumTitleLength = 32;
        public const int MaximumAbbreviationLength = 8;

        public static BeltDesignState Default(ChampionshipType type, bool world) => new()
        {
            Template = type == ChampionshipType.TagTeam ? 3 : world ? 1 : 2,
            MainEmblem = world ? 3 : 1
        };

        public static void Validate(BeltDesignState design)
        {
            if (design == null) return;
            if (design.Template < 1 || design.Template > TemplateCount || design.MainEmblem < 0 || design.MainEmblem > EmblemCount ||
                design.SideEmblem < 0 || design.SideEmblem > EmblemCount || !Enum.IsDefined(typeof(BeltStrapColor), design.StrapColor) ||
                !Enum.IsDefined(typeof(BeltMetalColor), design.MetalColor) || !Enum.IsDefined(typeof(BeltLettering), design.Lettering) ||
                design.NameplateText == null || design.NameplateText.Length > MaximumTitleLength ||
                design.Abbreviation == null || design.Abbreviation.Length > MaximumAbbreviationLength ||
                HasControl(design.NameplateText) || HasControl(design.Abbreviation))
                throw new InvalidOperationException("벨트 디자인 설정이 올바르지 않습니다.");
        }

        private static bool HasControl(string text)
        {
            foreach (var c in text) if (char.IsControl(c)) return true;
            return false;
        }
    }
}
