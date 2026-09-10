using System;
using System.Collections.Generic;
using PWManager.Domain.Models;

namespace PWManager.Data.Generation
{
    internal static class WweShowTestRoster
    {
        private readonly struct Profile
        {
            public readonly string Name;
            public readonly WrestlerGender Gender;
            public readonly int Overall;
            public readonly int Promo;
            public readonly MatchArchetype MatchType;
            public readonly PromoArchetype PromoType;
            public readonly KayfabeAlignment Alignment;

            public Profile(string name, WrestlerGender gender, int overall, int promo,
                MatchArchetype matchType, PromoArchetype promoType, KayfabeAlignment alignment)
            {
                Name = name; Gender = gender; Overall = overall; Promo = promo;
                MatchType = matchType; PromoType = promoType; Alignment = alignment;
            }
        }

        private static readonly Profile[] Roster =
        {
            // Raw men's division
            P("Akira Tozawa",68,70,MatchArchetype.HighFlying,PromoArchetype.Comedy,KayfabeAlignment.Face),
            P("Angelo Dawkins",78,75,MatchArchetype.Power,PromoArchetype.Balanced,KayfabeAlignment.Face),
            P("Austin Theory",82,84,MatchArchetype.Balanced,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Big Cass",82,78,MatchArchetype.Power,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Bravo Americano",76,74,MatchArchetype.Technical,PromoArchetype.Comedy,KayfabeAlignment.Heel),
            P("Bron Breakker",90,86,MatchArchetype.Power,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("Bronco Nima",76,72,MatchArchetype.Power,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Bronson Reed",88,82,MatchArchetype.Power,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Brutus Creed",77,68,MatchArchetype.Power,PromoArchetype.Silent,KayfabeAlignment.Heel),
            P("Chad Gable",81,83,MatchArchetype.Technical,PromoArchetype.Comedy,KayfabeAlignment.Heel),
            P("Cruz Del Toro",73,68,MatchArchetype.HighFlying,PromoArchetype.Silent,KayfabeAlignment.Face),
            P("Dominik Mysterio",87,90,MatchArchetype.Balanced,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Dragon Lee",79,70,MatchArchetype.HighFlying,PromoArchetype.Silent,KayfabeAlignment.Face),
            P("El Grande Americano",85,84,MatchArchetype.Technical,PromoArchetype.Comedy,KayfabeAlignment.Heel),
            P("Ethan Page",83,87,MatchArchetype.Balanced,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Jacob Fatu",88,83,MatchArchetype.Power,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("JD McDonagh",79,75,MatchArchetype.Technical,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Je'Von Evans",78,74,MatchArchetype.HighFlying,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Jey Uso",91,92,MatchArchetype.Balanced,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Jimmy Uso",86,85,MatchArchetype.Balanced,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Joaquin Wilde",72,68,MatchArchetype.HighFlying,PromoArchetype.Silent,KayfabeAlignment.Face),
            P("Joe Hendry",81,91,MatchArchetype.Balanced,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Julius Creed",78,69,MatchArchetype.Technical,PromoArchetype.Silent,KayfabeAlignment.Heel),
            P("LA Knight",89,95,MatchArchetype.Brawler,PromoArchetype.Mic,KayfabeAlignment.Face),
            P("Logan Paul",90,91,MatchArchetype.Spot,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("Lucien Price",76,71,MatchArchetype.Power,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Montez Ford",80,84,MatchArchetype.HighFlying,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Oba Femi",85,82,MatchArchetype.Power,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("Otis",75,80,MatchArchetype.Power,PromoArchetype.Comedy,KayfabeAlignment.Face),
            P("Penta",84,80,MatchArchetype.HighFlying,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Rayo Americano",76,72,MatchArchetype.Brawler,PromoArchetype.Comedy,KayfabeAlignment.Heel),
            P("Rey Mysterio",84,88,MatchArchetype.HighFlying,PromoArchetype.Face,KayfabeAlignment.Face),
            P("Roman Reigns",95,98,MatchArchetype.Power,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Royce Keys",76,72,MatchArchetype.Power,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Rusev",84,82,MatchArchetype.Power,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Seth Rollins",94,96,MatchArchetype.Balanced,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("Solo Sikoa",85,84,MatchArchetype.Power,PromoArchetype.Heel,KayfabeAlignment.Heel),

            // SmackDown men's division
            P("Angel",72,76,MatchArchetype.HighFlying,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("Axiom",75,68,MatchArchetype.HighFlying,PromoArchetype.Silent,KayfabeAlignment.Face),
            P("Baron Corbin",82,84,MatchArchetype.Brawler,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Berto",72,72,MatchArchetype.HighFlying,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Carmelo Hayes",84,86,MatchArchetype.Spot,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("CM Punk",94,98,MatchArchetype.Fundamentals,PromoArchetype.Mic,KayfabeAlignment.Tweener),
            P("Cody Rhodes",95,98,MatchArchetype.Balanced,PromoArchetype.Face,KayfabeAlignment.Face),
            P("Damian Priest",87,88,MatchArchetype.Power,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Drew McIntyre",93,94,MatchArchetype.Power,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Elton Prince",71,79,MatchArchetype.Balanced,PromoArchetype.Comedy,KayfabeAlignment.Heel),
            P("Erik",78,68,MatchArchetype.Power,PromoArchetype.Silent,KayfabeAlignment.Face),
            P("Finn Bálor",88,90,MatchArchetype.Technical,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Gunther",93,91,MatchArchetype.Technical,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Ivar",79,70,MatchArchetype.Power,PromoArchetype.Silent,KayfabeAlignment.Face),
            P("Johnny Gargano",76,79,MatchArchetype.Technical,PromoArchetype.Face,KayfabeAlignment.Face),
            P("Kevin Owens",87,93,MatchArchetype.Brawler,PromoArchetype.Mic,KayfabeAlignment.Heel),
            P("Kit Wilson",71,80,MatchArchetype.Balanced,PromoArchetype.Comedy,KayfabeAlignment.Heel),
            P("Kyoki",80,75,MatchArchetype.Brawler,PromoArchetype.Silent,KayfabeAlignment.Heel),
            P("Matt Cardona",84,87,MatchArchetype.Balanced,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("The Miz",80,94,MatchArchetype.Fundamentals,PromoArchetype.Mic,KayfabeAlignment.Heel),
            P("Nathan Frazer",76,70,MatchArchetype.HighFlying,PromoArchetype.Silent,KayfabeAlignment.Face),
            P("R-Truth",80,90,MatchArchetype.Balanced,PromoArchetype.Comedy,KayfabeAlignment.Face),
            P("Randy Orton",92,95,MatchArchetype.Fundamentals,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Rey Fenix",84,73,MatchArchetype.HighFlying,PromoArchetype.Silent,KayfabeAlignment.Face),
            P("Ricky Saints",81,88,MatchArchetype.Balanced,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Sami Zayn",89,92,MatchArchetype.Fundamentals,PromoArchetype.Face,KayfabeAlignment.Face),
            P("Shinsuke Nakamura",86,86,MatchArchetype.Brawler,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("Talla Tonga",79,70,MatchArchetype.Power,PromoArchetype.Silent,KayfabeAlignment.Heel),
            P("Tama Tonga",82,77,MatchArchetype.Brawler,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Trick Williams",84,91,MatchArchetype.Balanced,PromoArchetype.Charisma,KayfabeAlignment.Face),

            // Raw women's division
            P("Asuka",WrestlerGender.Female,91,86,MatchArchetype.Technical,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("Bayley",WrestlerGender.Female,89,91,MatchArchetype.Fundamentals,PromoArchetype.Mic,KayfabeAlignment.Face),
            P("Becky Lynch",WrestlerGender.Female,94,97,MatchArchetype.Brawler,PromoArchetype.Mic,KayfabeAlignment.Heel),
            P("Ivy Nile",WrestlerGender.Female,76,70,MatchArchetype.Technical,PromoArchetype.Silent,KayfabeAlignment.Heel),
            P("IYO SKY",WrestlerGender.Female,93,84,MatchArchetype.HighFlying,PromoArchetype.Face,KayfabeAlignment.Face),
            P("Liv Morgan",WrestlerGender.Female,92,94,MatchArchetype.Spot,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Lola Vice",WrestlerGender.Female,78,82,MatchArchetype.Brawler,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Lyra Valkyria",WrestlerGender.Female,84,82,MatchArchetype.Technical,PromoArchetype.Face,KayfabeAlignment.Face),
            P("Maxxine Dupri",WrestlerGender.Female,78,83,MatchArchetype.Balanced,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("Naomi",WrestlerGender.Female,92,88,MatchArchetype.HighFlying,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Raquel Rodriguez",WrestlerGender.Female,86,82,MatchArchetype.Power,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Roxanne Perez",WrestlerGender.Female,85,87,MatchArchetype.Technical,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Sol Ruca",WrestlerGender.Female,77,75,MatchArchetype.Spot,PromoArchetype.Face,KayfabeAlignment.Face),
            P("Stephanie Vaquer",WrestlerGender.Female,92,87,MatchArchetype.Technical,PromoArchetype.Charisma,KayfabeAlignment.Face),

            // SmackDown women's division
            P("Alexa Bliss",WrestlerGender.Female,87,92,MatchArchetype.Balanced,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("B-Fab",WrestlerGender.Female,67,78,MatchArchetype.Brawler,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Bianca Belair",WrestlerGender.Female,94,93,MatchArchetype.Power,PromoArchetype.Face,KayfabeAlignment.Face),
            P("Blake Monroe",WrestlerGender.Female,78,84,MatchArchetype.Technical,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("Brie Bella",WrestlerGender.Female,84,87,MatchArchetype.Balanced,PromoArchetype.Face,KayfabeAlignment.Face),
            P("Candice LeRae",WrestlerGender.Female,71,75,MatchArchetype.Technical,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Charlotte Flair",WrestlerGender.Female,93,95,MatchArchetype.Technical,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Chelsea Green",WrestlerGender.Female,85,93,MatchArchetype.Balanced,PromoArchetype.Comedy,KayfabeAlignment.Heel),
            P("Fallon Henley",WrestlerGender.Female,78,82,MatchArchetype.Brawler,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Giulia",WrestlerGender.Female,86,82,MatchArchetype.Brawler,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("Jacy Jayne",WrestlerGender.Female,80,85,MatchArchetype.Brawler,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Jade Cargill",WrestlerGender.Female,88,87,MatchArchetype.Power,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Jordynne Grace",WrestlerGender.Female,81,81,MatchArchetype.Power,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Kiana James",WrestlerGender.Female,80,86,MatchArchetype.Balanced,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Lainey Reid",WrestlerGender.Female,75,80,MatchArchetype.Brawler,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Lash Legend",WrestlerGender.Female,81,84,MatchArchetype.Power,PromoArchetype.Charisma,KayfabeAlignment.Heel),
            P("Michin",WrestlerGender.Female,82,79,MatchArchetype.Brawler,PromoArchetype.Face,KayfabeAlignment.Face),
            P("Nia Jax",WrestlerGender.Female,88,87,MatchArchetype.Power,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Nikki Bella",WrestlerGender.Female,88,91,MatchArchetype.Balanced,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Paige",WrestlerGender.Female,88,92,MatchArchetype.Brawler,PromoArchetype.Mic,KayfabeAlignment.Face),
            P("Piper Niven",WrestlerGender.Female,82,78,MatchArchetype.Power,PromoArchetype.Heel,KayfabeAlignment.Heel),
            P("Rhea Ripley",WrestlerGender.Female,96,95,MatchArchetype.Power,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Tatum Paxley",WrestlerGender.Female,78,83,MatchArchetype.Spot,PromoArchetype.Charisma,KayfabeAlignment.Face),
            P("Tiffany Stratton",WrestlerGender.Female,91,91,MatchArchetype.Spot,PromoArchetype.Charisma,KayfabeAlignment.Face)
        };

        public static List<WrestlerState> Create(WrestlerGenerator generator, GameDate date)
        {
            var wrestlers = new List<WrestlerState>(Roster.Length);
            foreach (var profile in Roster)
            {
                var wrestler = generator.GenerateCandidate(profile.Gender, date);
                wrestler.Identity.LegalName = wrestler.Identity.RingName = profile.Name;
                wrestler.Identity.Background = WrestlerBackground.Veteran;
                wrestler.Roster.Alignment = profile.Alignment;
                wrestler.Presentation.MatchArchetype = profile.MatchType;
                wrestler.Presentation.PromoArchetype = profile.PromoType;
                wrestler.Presentation.PromoDisposition = profile.PromoType == PromoArchetype.Comedy ? PromoDisposition.Comic : PromoDisposition.Serious;
                wrestler.Status.StatusValue = profile.Overall;
                wrestler.Momentum.Momentum = profile.Overall;
                SetRatings(wrestler.Attributes, Rating(profile.Overall), Rating(profile.Promo), profile.MatchType, profile.PromoType);
                wrestler.Growth.MatchPotentialCap = 20;
                wrestler.Growth.PromoPotentialCap = 20;
                wrestler.FanReaction.UsesTwoAxes = true;
                wrestler.FanReaction.Mark = wrestler.FanReaction.Casual = wrestler.FanReaction.Hardcore = new FanResponseState
                {
                    Interest = profile.Overall,
                    Preference = profile.Alignment == KayfabeAlignment.Face ? 75 : profile.Alignment == KayfabeAlignment.Heel ? 25 : 50
                };
                wrestlers.Add(wrestler);
            }
            return wrestlers;
        }

        private static Profile P(string name, int overall, int promo, MatchArchetype matchType, PromoArchetype promoType, KayfabeAlignment alignment) =>
            P(name, WrestlerGender.Male, overall, promo, matchType, promoType, alignment);
        private static Profile P(string name, WrestlerGender gender, int overall, int promo, MatchArchetype matchType, PromoArchetype promoType, KayfabeAlignment alignment) =>
            new(name, gender, overall, promo, matchType, promoType, alignment);
        private static float Rating(int value) => 1 + (value - 60) * 19f / 40f;

        private static void SetRatings(WrestlerAttributesState a, float match, float promo, MatchArchetype matchType, PromoArchetype promoType)
        {
            a.RingPsychology=Limit(match+(matchType is MatchArchetype.Fundamentals or MatchArchetype.Technical?1.5f:0));
            a.RingImprovisation=Limit(match+(matchType==MatchArchetype.Balanced?1:0));
            a.Technical=Limit(match+(matchType==MatchArchetype.Technical?2:-1));
            a.Brawling=Limit(match+(matchType==MatchArchetype.Brawler?2:0));
            a.Power=Limit(match+(matchType==MatchArchetype.Power?2:-1));
            a.HighFlying=Limit(match+(matchType==MatchArchetype.HighFlying?2:-1));
            a.SpotWork=Limit(match+(matchType==MatchArchetype.Spot?2:0));
            a.SpecialtyMatches=Limit(match+(matchType is MatchArchetype.Brawler or MatchArchetype.Spot?1:0));
            a.Selling=Limit(match+(matchType==MatchArchetype.Fundamentals?1:0));
            a.Stamina=Limit(match+(matchType is MatchArchetype.HighFlying or MatchArchetype.Technical?1:0));
            a.Charisma=Limit(promo+(promoType==PromoArchetype.Charisma?2:0));
            a.MicWork=Limit(promo+(promoType==PromoArchetype.Mic?2:0));
            a.Improvisation=Limit(promo+(promoType==PromoArchetype.Improvisation?2:0));
            a.Acting=Limit(promo+(promoType is PromoArchetype.Charisma or PromoArchetype.Heel?1:0));
            a.FaceWork=Limit(promo+(promoType==PromoArchetype.Face?2:-1));
            a.HeelWork=Limit(promo+(promoType==PromoArchetype.Heel?2:-1));
            a.Comedy=Limit(promo+(promoType==PromoArchetype.Comedy?2:-2));
        }

        private static float Limit(float value) => Math.Max(1, Math.Min(20, value));
    }
}
