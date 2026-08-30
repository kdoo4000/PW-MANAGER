using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class WrestlerOverallCalculator
    {
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
