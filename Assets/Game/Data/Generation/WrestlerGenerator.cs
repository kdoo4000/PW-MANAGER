using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Definitions;
using PWManager.Data.Loading;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Data.Generation
{
    public sealed class WrestlerGenerator
    {
        private enum MatchProfile { RingGeneral, Execution, Selling, Stamina, Specialty }

        private readonly StaticContentRegistry content;
        private readonly WrestlerGenerationConfig config;
        private readonly Random random;
        private readonly Func<string> createId;

        public WrestlerGenerator(StaticContentRegistry content, int seed, Func<string> createId = null)
        {
            this.content = content ?? throw new ArgumentNullException(nameof(content));
            config = content.Catalog.WrestlerGeneration ?? throw new ArgumentException("Wrestler generation config is required.", nameof(content));
            random = new Random(seed);
            this.createId = createId ?? EntityId.CreateRuntimeId;
        }

        public IReadOnlyList<WrestlerState> GenerateInitialCandidates(GameDate createdDate)
        {
            var result = new List<WrestlerState>();
            result.AddRange(GenerateGuaranteedGroup(WrestlerGender.Male, config.InitialMaleCandidateCount, createdDate));
            result.AddRange(GenerateGuaranteedGroup(WrestlerGender.Female, config.InitialFemaleCandidateCount, createdDate));
            EnsureUniqueLegalNames(result);
            return result;
        }

        public WrestlerState GenerateCandidate(WrestlerGender gender, GameDate createdDate)
        {
            var background = Pick(new[] { 35d, 20d, 15d, 30d }, new[]
            {
                WrestlerBackground.Rookie, WrestlerBackground.Athlete,
                WrestlerBackground.Entertainer, WrestlerBackground.Veteran
            });
            var age = GenerateAge(background);
            var careerYears = GenerateCareerYears(background, age);
            var height = GenerateHeight(gender);
            var bodyType = GenerateBodyType(gender, height);
            var weight = GenerateWeight(gender, height, bodyType);
            var matchArchetype = GenerateMatchArchetype(background, bodyType);
            var promoArchetype = GeneratePromoArchetype(background);
            var matchPotential = GeneratePotential(background, true);
            var promoPotential = GeneratePotential(background, false);
            var matchOverall = GenerateCurrentOverall(background, age, matchPotential, true);
            var promoOverall = GenerateCurrentOverall(background, age, promoPotential, false);
            var attributes = GenerateAttributes(matchOverall, promoOverall, matchArchetype, promoArchetype, bodyType);
            var styleId = DetermineStyle(gender, height, attributes, matchArchetype);
            AdjustMatchAttributes(attributes, styleId, matchOverall);
            AdjustPromoAttributes(attributes, promoOverall);
            var legalName = GenerateLegalName(gender);
            var wrestler = new WrestlerState
            {
                Identity = new WrestlerIdentityState
                {
                    Id = createId(), LegalName = legalName,
                    RingName = legalName,
                    Gender = gender, Background = background, BirthDate = GenerateBirthDate(createdDate, age),
                    CreatedDate = createdDate, ExpiryDate = new OptionalGameDate(AddDays(createdDate, config.CandidateRetentionWeeks * 7)),
                    HeightCm = height, WeightKg = weight, BodyType = bodyType, CareerYears = careerYears
                },
                Attributes = attributes,
                Growth = new WrestlerGrowthState { MatchPotentialCap = matchPotential, PromoPotentialCap = promoPotential },
                Condition = new WrestlerConditionState { Condition = 100, Satisfaction = 50, Availability = WrestlerAvailability.Available },
                Roster = new WrestlerRosterState { ActivityState = RosterActivityState.Inactive },
                Presentation = new WrestlerPresentationState
                {
                    MatchArchetype = matchArchetype,
                    PromoArchetype = promoArchetype,
                    PromoDisposition = promoArchetype == PromoArchetype.Comedy ? PromoDisposition.Comic : promoArchetype == PromoArchetype.Balanced ? PromoDisposition.Balanced : PromoDisposition.Serious,
                    WrestlingStyleId = styleId
                }
            };
            wrestler.Status.IsRookie = background == WrestlerBackground.Rookie;
            wrestler.Status.DebutDate = careerYears == 0 ? createdDate : AddYears(createdDate, -careerYears);
            GenerateTraits(wrestler);
            GenerateMoves(wrestler);
            return wrestler;
        }

        private List<WrestlerState> GenerateGuaranteedGroup(WrestlerGender gender, int count, GameDate date)
        {
            var group = new List<WrestlerState>(count);
            for (var i = 0; i < count; i++) group.Add(GenerateInitialCandidate(gender, date));
            for (var attempt = 0; attempt < 5000 && !MeetsInitialGuarantees(group); attempt++)
            {
                var replacement = GenerateInitialCandidate(gender, date);
                var currentScore = GuaranteeScore(group);
                var bestIndex = -1;
                var bestScore = currentScore;
                for (var i = 0; i < group.Count; i++)
                {
                    var previous = group[i];
                    group[i] = replacement;
                    var score = GuaranteeScore(group);
                    group[i] = previous;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestIndex = i;
                }
                if (bestIndex >= 0) group[bestIndex] = replacement;
            }
            if (MeetsInitialGuarantees(group)) return group;
            throw new InvalidOperationException($"Could not generate a valid initial {gender} candidate group.");
        }

        private WrestlerState GenerateInitialCandidate(WrestlerGender gender, GameDate date)
        {
            for (var attempt = 0; attempt < 500; attempt++)
            {
                var candidate = GenerateCandidate(gender, date);
                var match = WrestlerOverallCalculator.Match(candidate);
                var promo = WrestlerOverallCalculator.Promo(candidate);
                if (match is >= 7f and <= 15.01f && promo is >= 7f and <= 15.01f) return candidate;
            }
            throw new InvalidOperationException($"Could not generate an initial {gender} candidate within the starting ability range.");
        }

        private static double GuaranteeScore(IReadOnlyCollection<WrestlerState> group)
        {
            return Math.Min(3, group.Count(x => WrestlerOverallCalculator.Match(x) >= 10f)) +
                   Math.Min(1, group.Count(x => WrestlerOverallCalculator.Match(x) >= 14f)) +
                   Math.Min(11d, group.Average(x => WrestlerOverallCalculator.Match(x))) +
                   Math.Min(10d, group.Average(x => WrestlerOverallCalculator.Promo(x))) +
                   Math.Min(2, group.Count(x => WrestlerOverallCalculator.Promo(x) >= 10f)) +
                   Math.Min(3, group.Count(x => x.Identity.Background == WrestlerBackground.Rookie)) +
                   Math.Min(2, group.Count(x => x.Identity.Background == WrestlerBackground.Veteran)) +
                   Math.Min(2, group.Count(x => x.Growth.MatchPotentialCap >= 12f)) +
                   Math.Min(2, group.Count(x => x.Growth.PromoPotentialCap >= 12f)) +
                   Math.Min(4, group.Select(x => x.Presentation.WrestlingStyleId).Distinct().Count());
        }

        private static bool MeetsInitialGuarantees(IReadOnlyCollection<WrestlerState> group)
        {
            return group.All(x => WrestlerOverallCalculator.Match(x) is >= 7f and <= 15.01f) &&
                   group.All(x => WrestlerOverallCalculator.Promo(x) is >= 7f and <= 15.01f) &&
                   group.Average(x => WrestlerOverallCalculator.Match(x)) >= 11f &&
                   group.Average(x => WrestlerOverallCalculator.Promo(x)) >= 10f &&
                   group.Count(x => WrestlerOverallCalculator.Match(x) >= 10f) >= 3 &&
                   group.Count(x => WrestlerOverallCalculator.Match(x) >= 14f) >= 1 &&
                   group.Count(x => WrestlerOverallCalculator.Promo(x) >= 10f) >= 2 &&
                   group.Count(x => x.Identity.Background == WrestlerBackground.Rookie) >= 3 &&
                   group.Count(x => x.Identity.Background == WrestlerBackground.Veteran) >= 2 &&
                   group.Count(x => x.Growth.MatchPotentialCap >= 12f) >= 2 &&
                   group.Count(x => x.Growth.PromoPotentialCap >= 12f) >= 2 &&
                   group.Select(x => x.Presentation.WrestlingStyleId).Distinct().Count() >= 4;
        }

        private void EnsureUniqueLegalNames(IList<WrestlerState> candidates)
        {
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (usedNames.Add(candidate.Identity.LegalName)) continue;

                for (var attempt = 0; attempt < 20; attempt++)
                {
                    var replacementName = GenerateLegalName(candidate.Identity.Gender);
                    if (!usedNames.Add(replacementName)) continue;
                    candidate.Identity.LegalName = replacementName;
                    candidate.Identity.RingName = replacementName;
                    break;
                }
            }
        }

        private int GenerateAge(WrestlerBackground background)
        {
            return background switch
            {
                WrestlerBackground.Rookie => TriangularInt(18, 21, 25),
                WrestlerBackground.Athlete => TriangularInt(20, 24, 31),
                WrestlerBackground.Entertainer => TriangularInt(20, 26, 35),
                _ => TriangularInt(23, 30, 40)
            };
        }

        private int GenerateCareerYears(WrestlerBackground background, int age)
        {
            if (background == WrestlerBackground.Rookie) return 0;
            if (background is WrestlerBackground.Athlete or WrestlerBackground.Entertainer) return random.NextDouble() < .7 ? 0 : 1;
            var maximum = Math.Min(15, age - 18);
            return TriangularInt(5, Math.Min(8, maximum), maximum);
        }

        private int GenerateHeight(WrestlerGender gender)
        {
            if (random.NextDouble() < .95)
            {
                var mean = gender == WrestlerGender.Male ? 183 : 166;
                var deviation = gender == WrestlerGender.Male ? 9 : 7;
                var minimum = gender == WrestlerGender.Male ? 165 : 150;
                var maximum = gender == WrestlerGender.Male ? 220 : 190;
                return Clamp((int)Math.Round(mean + StandardNormal() * deviation), minimum, maximum);
            }
            if (gender == WrestlerGender.Male)
                return PickRange(new[] { .35, .45, .20 }, new[] { (165, 171), (201, 210), (211, 220) });
            return PickRange(new[] { .40, .45, .15 }, new[] { (150, 155), (181, 186), (187, 190) });
        }

        private WrestlerBodyType GenerateBodyType(WrestlerGender gender, int height)
        {
            double[] weights;
            if (gender == WrestlerGender.Male)
                weights = height <= 174 ? new[] { 30d, 40, 20, 8, 2 } : height <= 189 ? new[] { 15d, 40, 28, 14, 3 } :
                    height <= 199 ? new[] { 7d, 28, 30, 25, 10 } : height <= 209 ? new[] { 3d, 17, 25, 35, 20 } : new[] { 1d, 9, 20, 35, 35 };
            else
                weights = height <= 157 ? new[] { 30d, 40, 20, 8, 2 } : height <= 172 ? new[] { 15d, 40, 28, 14, 3 } :
                    height <= 179 ? new[] { 8d, 30, 30, 23, 9 } : height <= 185 ? new[] { 3d, 20, 27, 32, 18 } : new[] { 1d, 12, 22, 35, 30 };
            return Pick(weights, new[] { WrestlerBodyType.Lightweight, WrestlerBodyType.Balanced, WrestlerBodyType.Muscular, WrestlerBodyType.Heavyweight, WrestlerBodyType.Giant });
        }

        private int GenerateWeight(WrestlerGender gender, int height, WrestlerBodyType bodyType)
        {
            var male = new[] { 22d, 25, 28.5, 33, 39 };
            var female = new[] { 20.5, 23, 26, 30.5, 36 };
            var bmi = (gender == WrestlerGender.Male ? male : female)[(int)bodyType] + Range(-1.5, 1.5);
            var weight = (int)Math.Round(bmi * Math.Pow(height / 100d, 2));
            return Clamp(weight, gender == WrestlerGender.Male ? 55 : 42, gender == WrestlerGender.Male ? 220 : 150);
        }

        private MatchArchetype GenerateMatchArchetype(WrestlerBackground background, WrestlerBodyType bodyType)
        {
            var baseWeights = new[] { 15d, 15, 15, 18, 15, 10, 12 };
            var body = new[]
            {
                new[] { 1d, .7, .4, 1.4, 1.8, 1.4, 1 }, new[] { 1.3, 1, .8, 1.3, 1.2, 1.1, 1.5 },
                new[] { 1d, 1.1, 1.6, .9, .8, 1, 1.1 }, new[] { .9, 1.5, 1.5, .7, .3, .6, .8 },
                new[] { .7, 1.5, 1.8, .5, .1, .3, .6 }
            }[(int)bodyType];
            var backgroundWeights = background switch
            {
                WrestlerBackground.Rookie => new[] { 1.2, 1, 1, 1, 1, 1, 1.1 },
                WrestlerBackground.Athlete => new[] { 1d, 1, 1.3, 1, 1.3, 1, 1 },
                WrestlerBackground.Entertainer => new[] { 1d, 1, 1, 1, 1, 1.25, 1 },
                _ => new[] { 1d, 1.15, 1, 1.15, 1, 1, 1 }
            };
            return Pick(Multiply(baseWeights, body, backgroundWeights), Enum.GetValues(typeof(MatchArchetype)).Cast<MatchArchetype>().ToArray());
        }

        private PromoArchetype GeneratePromoArchetype(WrestlerBackground background)
        {
            var weights = new[] { 15d, 15, 10, 10, 10, 8, 7, 15, 10 };
            var modifier = background switch
            {
                WrestlerBackground.Rookie => new[] { 1d, 1, 1, 1, 1, 1, 1, 1.15, 1.3 },
                WrestlerBackground.Athlete => new[] { 1d, 1, 1, 1, 1, 1, 1.5, 1, 1.25 },
                WrestlerBackground.Entertainer => new[] { 1.5, 1.5, 1, 1, 1, 1.4, 1, 1, 1 },
                _ => new[] { 1d, 1, 1.25, 1.15, 1.15, 1, 1, 1, 1 }
            };
            return Pick(Multiply(weights, modifier), Enum.GetValues(typeof(PromoArchetype)).Cast<PromoArchetype>().ToArray());
        }

        private float GeneratePotential(WrestlerBackground background, bool match)
        {
            var weights = new[] { 2d, 8, 18, 30, 27, 13, 2 };
            var highModifier = match ? background switch
            {
                WrestlerBackground.Rookie => 1.25, WrestlerBackground.Athlete => 1.35,
                WrestlerBackground.Entertainer => .8, _ => 1
            } : background switch
            {
                WrestlerBackground.Rookie => 1.1, WrestlerBackground.Athlete => .8,
                WrestlerBackground.Entertainer => 1.5, _ => 1
            };
            for (var i = 4; i < weights.Length; i++) weights[i] *= highModifier;
            var grade = Pick(weights, new[] { 0, 1, 2, 3, 4, 5, 6 });
            var ranges = new[] { (1d, 4.9), (5d, 7.9), (8d, 9.9), (10d, 11.9), (12d, 14.9), (15d, 17.9), (18d, 20d) };
            return RoundTenth(Range(ranges[grade].Item1, ranges[grade].Item2));
        }

        private float GenerateCurrentOverall(WrestlerBackground background, int age, float potential, bool match)
        {
            (double Min, double Mode, double Max) values = match ? background switch
            {
                WrestlerBackground.Rookie => (5d, 8d, 11.5d), WrestlerBackground.Athlete => (7d, 10.5d, 14d),
                WrestlerBackground.Entertainer => (4.5d, 7.5d, 11d), _ => (8d, 12d, 17d)
            } : background switch
            {
                WrestlerBackground.Rookie => (5d, 8.5d, 12d), WrestlerBackground.Athlete => (4d, 7d, 10.5d),
                WrestlerBackground.Entertainer => (8d, 12d, 17d), _ => (7d, 11d, 16.5d)
            };
            var maxGap = age <= 21 ? 8 : age <= 25 ? 6.5 : age <= 29 ? 5 : age <= 33 ? 3.5 : age <= 36 ? 2.5 : 1.5;
            var minimum = Math.Max(values.Item1, potential - maxGap);
            var maximum = Math.Min(values.Item3, potential);
            if (minimum >= maximum) return potential;
            return RoundTenth(Triangular(minimum, Math.Max(minimum, Math.Min(values.Item2, maximum)), maximum));
        }

        private WrestlerAttributesState GenerateAttributes(float match, float promo, MatchArchetype matchType,
            PromoArchetype promoType, WrestlerBodyType body)
        {
            var matchMods = new[,]
            {
                {2,.5,-.5,2,-1,-1,.5},{1,.5,-.5,1.5,-.5,.5,.5},{1,-1,-1,3,.5,-.5,.5},{-.5,3,1,-1,-1.5,-.5,0},
                {-.5,.5,3,-1,-2,-1,0},{-.5,-1.5,-2,.5,3,2,0},{.5,0,.5,.5,2,3,.5},{-.5,1.5,.5,-.5,0,2,0},
                {2,.5,1,1,.5,0,.5},{1.5,1.5,2,1,2,.5,.5}
            };
            var promoMods = new[,]
            {
                {3,1.5,.5,2,1,1,3,.5,-1},{.5,3,2,.5,2,1,-3,.5,-1},{.5,1.5,3,.5,1,1.5,-.5,.5,-1},
                {2,1,1,1.5,1.5,2.5,2,.5,-1},{.5,0,.5,3,-1.5,.5,.5,.5,-1},{.5,1,.5,-1.5,3,.5,.5,.5,-1},
                {0,0,1,.5,0,3,-1,.5,-1}
            };
            var profileMods = new[,]
            {
                {4d,0,0,0,0},{3,1,0,0,0},{0,2,0,0,0},{0,2,0,0,0},{0,2,0,0,0},
                {0,2,0,0,0},{0,4,0,0,0},{0,0,0,0,4},{0,0,5,0,0},{0,0,0,5,0}
            };
            var profile = Pick(new[] { 20d, 25, 20, 20, 15 }, Enum.GetValues(typeof(MatchProfile)).Cast<MatchProfile>().ToArray());
            var m = GenerateAttributes(10, match, i => matchMods[i, (int)matchType] + profileMods[i, (int)profile] + BodyModifier(body, i));
            var p = GenerateAttributes(7, promo, i => promoMods[i, (int)promoType]);
            return new WrestlerAttributesState
            {
                RingPsychology=m[0], RingImprovisation=m[1], Technical=m[2], Brawling=m[3], Power=m[4], HighFlying=m[5],
                SpotWork=m[6], SpecialtyMatches=m[7], Selling=m[8], Stamina=m[9], Charisma=p[0], MicWork=p[1],
                Improvisation=p[2], Acting=p[3], FaceWork=p[4], HeelWork=p[5], Comedy=p[6]
            };
        }

        private float[] GenerateAttributes(int count, float target, Func<int, double> modifier)
        {
            var modifiers = Enumerable.Range(0, count).Select(modifier).ToArray();
            var modifierAverage = modifiers.Average();
            var modifierScale = Math.Min(1, (target - 1) / 3);
            var spread = Math.Min(1.5, target - 1);
            return Enumerable.Range(0, count)
                .Select(i => RoundHundredth(Math.Max(1, Math.Min(20,
                    target + (modifiers[i] - modifierAverage) * modifierScale + Range(-spread, spread)))))
                .ToArray();
        }

        private static void AdjustMatchAttributes(WrestlerAttributesState a, string styleId, float target)
        {
            for (var pass = 0; pass < 5; pass++)
            {
                var shift = target - WrestlerOverallCalculator.Match(a, styleId);
                if (Math.Abs(shift) < .01f) break;
                a.RingPsychology = Shift(a.RingPsychology, shift); a.RingImprovisation = Shift(a.RingImprovisation, shift);
                a.Technical = Shift(a.Technical, shift); a.Brawling = Shift(a.Brawling, shift);
                a.Power = Shift(a.Power, shift); a.HighFlying = Shift(a.HighFlying, shift);
                a.SpotWork = Shift(a.SpotWork, shift); a.SpecialtyMatches = Shift(a.SpecialtyMatches, shift);
                a.Selling = Shift(a.Selling, shift); a.Stamina = Shift(a.Stamina, shift);
            }
        }

        private static void AdjustPromoAttributes(WrestlerAttributesState a, float target)
        {
            for (var pass = 0; pass < 5; pass++)
            {
                var shift = target - WrestlerOverallCalculator.Promo(a, KayfabeAlignment.Tweener, PromoDisposition.Balanced);
                if (Math.Abs(shift) < .01f) break;
                a.Charisma = Shift(a.Charisma, shift); a.MicWork = Shift(a.MicWork, shift);
                a.Improvisation = Shift(a.Improvisation, shift); a.Acting = Shift(a.Acting, shift);
                a.FaceWork = Shift(a.FaceWork, shift); a.HeelWork = Shift(a.HeelWork, shift); a.Comedy = Shift(a.Comedy, shift);
            }
        }

        private static float Shift(float value, float amount) => RoundHundredth(Math.Max(1, Math.Min(20, value + amount)));

        private static double BodyModifier(WrestlerBodyType body, int attribute) => body switch
        {
            WrestlerBodyType.Lightweight when attribute == 5 => .5,
            WrestlerBodyType.Lightweight when attribute == 4 => -.5,
            WrestlerBodyType.Muscular when attribute == 4 => .75,
            WrestlerBodyType.Muscular when attribute == 9 => .25,
            WrestlerBodyType.Heavyweight when attribute == 4 || attribute == 3 => .5,
            WrestlerBodyType.Heavyweight when attribute == 5 => -.75,
            WrestlerBodyType.Heavyweight when attribute == 9 => -.25,
            WrestlerBodyType.Giant when attribute == 4 => 1,
            WrestlerBodyType.Giant when attribute == 3 => .5,
            WrestlerBodyType.Giant when attribute == 5 => -1.5,
            WrestlerBodyType.Giant when attribute == 9 => -1,
            _ => 0
        };

        private string DetermineStyle(WrestlerGender gender, int height, WrestlerAttributesState a, MatchArchetype archetype)
        {
            var overall = WrestlerOverallCalculator.Match(a, null);
            if ((gender == WrestlerGender.Male ? height >= 195 : height >= 180) && a.Power >= overall + 1 && (a.Brawling >= overall || a.Selling >= overall)) return "style_006";
            if (a.HighFlying >= overall + 1.5 && a.Technical >= overall + 1 && a.SpotWork >= overall + 1) return "style_005";
            var core = new[] { a.Brawling, a.Power, a.Technical, a.HighFlying };
            if (core.Max() - core.Min() <= 2) return "style_007";
            var max = core.Max();
            if (max < overall + 1) return "style_007";
            var tied = Enumerable.Range(0, 4).Where(i => Math.Abs(core[i] - max) < .001f).ToList();
            if (tied.Count > 1)
            {
                var preferred = archetype switch { MatchArchetype.Brawler => 0, MatchArchetype.Power => 1, MatchArchetype.Technical => 2, MatchArchetype.HighFlying => 3, _ => -1 };
                if (tied.Contains(preferred)) return new[] { "style_001", "style_002", "style_003", "style_004" }[preferred];
            }
            return new[] { "style_001", "style_002", "style_003", "style_004" }[tied[random.Next(tied.Count)]];
        }

        private void GenerateTraits(WrestlerState wrestler)
        {
            var count = Pick(new[] { 25d, 45, 25, 5 }, new[] { 0, 1, 2, 3 });
            var candidates = content.Traits.Values.ToList();
            while (wrestler.Presentation.TraitIds.Count < count && candidates.Count > 0)
            {
                var selected = candidates[random.Next(candidates.Count)];
                wrestler.Presentation.TraitIds.Add(selected.Id);
                candidates.RemoveAll(x => x.Id == selected.Id || selected.ConflictingTraitIds.Contains(x.Id) || x.ConflictingTraitIds.Contains(selected.Id));
            }
        }

        private void GenerateMoves(WrestlerState wrestler)
        {
            var eligible = content.Moves.Values.Where(x => MeetsMoveRequirement(wrestler.Attributes, x)).ToList();
            if (eligible.Count < 2) throw new InvalidOperationException("At least two eligible moves are required.");
            var signature = eligible[random.Next(eligible.Count)]; eligible.Remove(signature);
            var finisher = eligible[random.Next(eligible.Count)];
            wrestler.Presentation.SignatureMoveIds.Add(signature.Id);
            wrestler.Presentation.FinisherMoveIds.Add(finisher.Id);
        }

        private static bool MeetsMoveRequirement(WrestlerAttributesState a, MoveDefinition move) => move.RequiredStat switch
        {
            MoveRequiredStat.None => true, MoveRequiredStat.Brawling => a.Brawling >= move.RequiredStatValue,
            MoveRequiredStat.Power => a.Power >= move.RequiredStatValue, MoveRequiredStat.Technical => a.Technical >= move.RequiredStatValue,
            MoveRequiredStat.HighFlying => a.HighFlying >= move.RequiredStatValue, _ => false
        };

        private string GenerateLegalName(WrestlerGender gender)
        {
            var names = content.Catalog.NamePool;
            return random.Next(10) switch
            {
                0 => CombineName(gender == WrestlerGender.Male ? names.KoreanMaleGivenNames : names.KoreanFemaleGivenNames, names.KoreanFamilyNames),
                1 => CombineName(gender == WrestlerGender.Male ? names.JapaneseMaleGivenNames : names.JapaneseFemaleGivenNames, names.JapaneseFamilyNames),
                _ => CombineName(gender == WrestlerGender.Male ? names.MaleGivenNames : names.FemaleGivenNames, names.FamilyNames)
            };
        }

        private string CombineName(IReadOnlyList<string> givenNames, IReadOnlyList<string> familyNames) =>
            givenNames[random.Next(givenNames.Count)] + " " + familyNames[random.Next(familyNames.Count)];

        private int TriangularInt(int min, int mode, int max) => Clamp((int)Math.Round(Triangular(min, mode, max)), min, max);
        private double Triangular(double min, double mode, double max)
        {
            var u = random.NextDouble(); var f = (mode - min) / (max - min);
            return u < f ? min + Math.Sqrt(u * (max - min) * (mode - min)) : max - Math.Sqrt((1 - u) * (max - min) * (max - mode));
        }
        private double StandardNormal() => Math.Sqrt(-2 * Math.Log(Math.Max(double.Epsilon, random.NextDouble()))) * Math.Cos(2 * Math.PI * random.NextDouble());
        private double Range(double min, double max) => min + random.NextDouble() * (max - min);
        private T Pick<T>(IReadOnlyList<double> weights, IReadOnlyList<T> values)
        {
            var roll = random.NextDouble() * weights.Sum();
            for (var i = 0; i < weights.Count; i++) { roll -= weights[i]; if (roll <= 0) return values[i]; }
            return values[values.Count - 1];
        }
        private int PickRange(double[] weights, (int Min, int Max)[] ranges) { var r = Pick(weights, ranges); return random.Next(r.Min, r.Max + 1); }
        private static double[] Multiply(params double[][] values) => Enumerable.Range(0, values[0].Length).Select(i => values.Aggregate(1d, (x, a) => x * a[i])).ToArray();
        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
        private static float RoundTenth(double value) => (float)Math.Round(value, 1, MidpointRounding.AwayFromZero);
        private static float RoundHundredth(double value) => (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        private static GameDate AddDays(GameDate value, int days) { var d = new DateTime(value.Year, value.Month, value.Day).AddDays(days); return new GameDate(d.Year, d.Month, d.Day); }
        private static GameDate AddYears(GameDate value, int years) { var d = new DateTime(value.Year, value.Month, value.Day).AddYears(years); return new GameDate(d.Year, d.Month, d.Day); }
        private GameDate GenerateBirthDate(GameDate current, int age)
        {
            var month = random.Next(1, 13); var day = random.Next(1, DateTime.DaysInMonth(current.Year - age, month) + 1);
            var year = current.Year - age - ((month > current.Month || month == current.Month && day > current.Day) ? 1 : 0);
            day = Math.Min(day, DateTime.DaysInMonth(year, month)); return new GameDate(year, month, day);
        }
    }
}
