using System;
using System.Collections.Generic;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class ScoutAssessmentRules
    {
        public const int DetailedStatCount = 17;

        public static int RevealedStatCount(int level) => level switch { 1 => 0, 2 => 3, 3 => 7, 4 => 12, 5 => 17, _ => throw new ArgumentOutOfRangeException(nameof(level)) };
        public static double MistakeChance(int level) => level switch { 1 => .20, 2 => .15, 3 => .10, 4 => .05, 5 => 0, _ => throw new ArgumentOutOfRangeException(nameof(level)) };

        public static IReadOnlyList<(ScoutValueType Type, float Value)> ActualValues(WrestlerState wrestler)
        {
            var a = wrestler.Attributes;
            return new (ScoutValueType, float)[]
            {
                (ScoutValueType.RingPsychology, a.RingPsychology), (ScoutValueType.RingImprovisation, a.RingImprovisation),
                (ScoutValueType.Technical, a.Technical), (ScoutValueType.Brawling, a.Brawling), (ScoutValueType.Power, a.Power),
                (ScoutValueType.HighFlying, a.HighFlying), (ScoutValueType.SpotWork, a.SpotWork), (ScoutValueType.SpecialtyMatches, a.SpecialtyMatches),
                (ScoutValueType.Selling, a.Selling), (ScoutValueType.Stamina, a.Stamina), (ScoutValueType.Charisma, a.Charisma),
                (ScoutValueType.MicWork, a.MicWork), (ScoutValueType.Improvisation, a.Improvisation), (ScoutValueType.Acting, a.Acting),
                (ScoutValueType.FaceWork, a.FaceWork), (ScoutValueType.HeelWork, a.HeelWork), (ScoutValueType.Comedy, a.Comedy),
                (ScoutValueType.MatchOverall, WrestlerOverallCalculator.Match(wrestler)), (ScoutValueType.PromoOverall, WrestlerOverallCalculator.Promo(wrestler)),
                (ScoutValueType.MatchPotential, wrestler.Growth.MatchPotentialCap), (ScoutValueType.PromoPotential, wrestler.Growth.PromoPotentialCap)
            };
        }

        public static ScoutAssessment Assess(float value) => value switch
        {
            < 4 => ScoutAssessment.VeryLow, < 8 => ScoutAssessment.Low, < 12 => ScoutAssessment.Average,
            < 16 => ScoutAssessment.High, _ => ScoutAssessment.VeryHigh
        };

        public static string Label(ScoutAssessment assessment) => assessment switch
        {
            ScoutAssessment.VeryLow => "매우 낮음", ScoutAssessment.Low => "낮음", ScoutAssessment.Average => "보통",
            ScoutAssessment.High => "높음", ScoutAssessment.VeryHigh => "매우 높음", _ => throw new ArgumentOutOfRangeException(nameof(assessment))
        };

        public static ScoutValueEstimate Evaluate(ScoutValueType type, float actual, int level, bool fullyRevealed, Random random)
        {
            var assessment = Assess(actual);
            if (!fullyRevealed && random.NextDouble() < MistakeChance(level))
            {
                var direction = assessment == ScoutAssessment.VeryLow ? 1 : assessment == ScoutAssessment.VeryHigh ? -1 : random.Next(2) == 0 ? -1 : 1;
                assessment = (ScoutAssessment)((int)assessment + direction);
            }
            var minimum = assessment == ScoutAssessment.VeryLow ? 1f : (int)assessment * 4f;
            return new ScoutValueEstimate
            {
                Type = type, HasEvaluation = true, IsFullyRevealed = fullyRevealed, Assessment = assessment,
                Minimum = fullyRevealed ? actual : minimum,
                Maximum = fullyRevealed ? actual : assessment == ScoutAssessment.VeryHigh ? 20f : ((int)assessment + 1) * 4f - .01f
            };
        }

        public static string Display(ScoutValueEstimate estimate)
        {
            if (estimate == null) return "미확인";
            var exact = estimate.HasEvaluation ? estimate.IsFullyRevealed : estimate.Minimum == estimate.Maximum;
            return exact ? WrestlerOverallCalculator.Grade(estimate.Minimum) : Label(estimate.HasEvaluation ? estimate.Assessment : Assess((estimate.Minimum + estimate.Maximum) * .5f));
        }
    }
}
