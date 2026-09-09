using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class WrestlerOverallCalculator
    {
        public static float MatchTotal(WrestlerAttributesState a) => a.RingPsychology + a.RingImprovisation
            + a.Technical + a.Brawling + a.Power + a.HighFlying + a.SpotWork + a.SpecialtyMatches + a.Selling + a.Stamina;

        public static float PromoTotal(WrestlerAttributesState a) => a.Charisma + a.MicWork + a.Improvisation
            + a.Acting + a.FaceWork + a.HeelWork + a.Comedy;

        public static float Match(WrestlerState wrestler, string styleId = null)
        {
            if (wrestler?.Attributes == null) return 0f;
            return Match(wrestler.Attributes, styleId ?? wrestler.Presentation?.WrestlingStyleId);
        }

        public static float Match(WrestlerAttributesState a, string styleId)
        {
            var weights = StyleWeights(styleId);
            var fixedTotal = a.RingPsychology + a.RingImprovisation + a.SpotWork + a.SpecialtyMatches + a.Selling + a.Stamina;
            var styleTotal = 4f * (a.Brawling * weights.Brawling + a.Power * weights.Power + a.HighFlying * weights.HighFlying + a.Technical * weights.Technical);
            return (fixedTotal + styleTotal) / 10f;
        }

        public static float Promo(WrestlerState wrestler, KayfabeAlignment? alignment = null, PromoDisposition? disposition = null)
        {
            if (wrestler?.Attributes == null) return 0f;
            return Promo(wrestler.Attributes, alignment ?? wrestler.Roster.Alignment, disposition ?? wrestler.Presentation.PromoDisposition);
        }

        public static float Promo(WrestlerAttributesState a, KayfabeAlignment alignment, PromoDisposition disposition)
        {
            var comedyWeight = disposition == PromoDisposition.Serious ? .2f : disposition == PromoDisposition.Comic ? 1.3f : .7f;
            var faceRatio = alignment == KayfabeAlignment.Face ? .9f : alignment == KayfabeAlignment.Heel ? .1f : .5f;
            var roleWeight = 3f - comedyWeight;
            var fixedTotal = a.Charisma + a.MicWork + a.Improvisation + a.Acting;
            return (fixedTotal + a.FaceWork * roleWeight * faceRatio + a.HeelWork * roleWeight * (1f - faceRatio) + a.Comedy * comedyWeight) / 7f;
        }

        public static string Grade(float value)
        {
            value = (float)System.Math.Round(value, 2, System.MidpointRounding.AwayFromZero);
            return value >= 19f ? "SS" : value >= 18f ? "S+" : value >= 17f ? "S" :
                value >= 16f ? "A+" : value >= 15f ? "A" : value >= 14f ? "B+" : value >= 13f ? "B" :
                value >= 12f ? "C+" : value >= 11f ? "C" : value >= 10f ? "D+" : value >= 9f ? "D" :
                value >= 8f ? "E+" : value >= 7f ? "E" : value >= 6f ? "F+" : value >= 5f ? "F" :
                value >= 4f ? "G+" : "G";
        }

        public static (string[] Primary, string[] Secondary) MatchStylePriorities(string styleId) => styleId switch
        {
            "style_001" => (new[] { "브롤링" }, new[] { "파워" }),
            "style_002" => (new[] { "파워" }, new[] { "테크니컬" }),
            "style_003" => (new[] { "테크니컬" }, new[] { "하이플라잉" }),
            "style_004" => (new[] { "하이플라잉" }, new[] { "테크니컬" }),
            "style_005" => (new[] { "하이플라잉" }, new[] { "테크니컬" }),
            "style_006" => (new[] { "파워" }, new[] { "브롤링" }),
            _ => (System.Array.Empty<string>(), new[] { "브롤링", "파워", "하이플라잉", "테크니컬" })
        };

        private static (float Brawling, float Power, float HighFlying, float Technical) StyleWeights(string id) => id switch
        {
            "style_001" => (.55f, .20f, .10f, .15f),
            "style_002" => (.15f, .55f, .05f, .25f),
            "style_003" => (.10f, .15f, .20f, .55f),
            "style_004" => (.10f, .05f, .55f, .30f),
            "style_005" => (.05f, .05f, .50f, .40f),
            "style_006" => (.30f, .55f, 0f, .15f),
            _ => (.25f, .25f, .25f, .25f)
        };
    }
}
