using System;
using System.Collections.Generic;
using System.Linq;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class PortraitPartSelection
    {
        public string Category;
        public int Index = 1;
    }

    [Serializable]
    public sealed class PortraitColorSelection
    {
        public string Category;
        public string Hex;
    }

    [Serializable]
    public sealed class PortraitAppearanceState
    {
        public int BodyType = 1;
        public int SkinBrightness = 100;
        public List<PortraitPartSelection> Parts = new();
        public List<PortraitColorSelection> Colors = new();

        public int Part(string category) => Parts?.FirstOrDefault(x => x != null && x.Category == category)?.Index ?? 1;
        public string Color(string category) => Colors?.FirstOrDefault(x => x != null && x.Category == category)?.Hex;

        public void SetPart(string category, int index)
        {
            Parts ??= new();
            var part = Parts.FirstOrDefault(x => x != null && x.Category == category);
            if (part == null) Parts.Add(new PortraitPartSelection { Category = category, Index = index });
            else part.Index = index;
        }

        public void SetColor(string category, string hex)
        {
            Colors ??= new();
            var color = Colors.FirstOrDefault(x => x != null && x.Category == category);
            if (color == null) Colors.Add(new PortraitColorSelection { Category = category, Hex = hex });
            else color.Hex = hex;
        }

        public PortraitAppearanceState Clone() => new()
        {
            BodyType = BodyType, SkinBrightness = SkinBrightness,
            Parts = (Parts ?? new()).Where(x => x != null).Select(x => new PortraitPartSelection { Category = x.Category, Index = x.Index }).ToList(),
            Colors = (Colors ?? new()).Where(x => x != null).Select(x => new PortraitColorSelection { Category = x.Category, Hex = x.Hex }).ToList()
        };
    }
}
