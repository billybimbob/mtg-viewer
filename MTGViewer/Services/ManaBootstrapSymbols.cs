using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

#nullable enable

namespace MTGViewer.Services
{
    public class ManaBootstrapSymbols : IMTGSymbols
    {
        private const string _symbol = "symbol";
        private const string _direction = "direction";
        private const string _loyalty = "loyalty";
        private const string _saga = "saga";

        private const string _symbolPattern = 
            @"{(?<" + _symbol + @">[^}]+)}";

        private const string _loyaltyPattern = 
            @"\[(?<" + _direction + @">\+|−)?(?<" + _loyalty + @">\d+)\]";

        private const string _sagaPattern = 
            @"(?<" + _saga + @">[(?:I|V)+,? ]+)—";

        private readonly Regex _iconFinder = new(
            $"(?:(?<{nameof(_symbolPattern)}>{_symbolPattern})" 
            + $"|(?<{nameof(_loyaltyPattern)}>{_loyaltyPattern})"
            + $"|(?<{nameof(_sagaPattern)}>{_sagaPattern}))" );


        public string[] FindSymbols(string mtgText)
        {
            return Regex
                .Matches(mtgText, _symbolPattern)
                .Select(m => m.Groups[_symbol].Value)
                .ToArray();
        }


        public string JoinSymbols(IEnumerable<string> mtgSymbols)
        {
            var colorSymbols = mtgSymbols.Select(sym => $"{{{ sym.ToUpper() }}}");

            return string.Join(string.Empty, colorSymbols);
        }


        public string InjectIcons(string mtgText)
        {
            if (string.IsNullOrEmpty(mtgText))
            {
                return string.Empty;
            }

            var withIcons = new StringBuilder(mtgText.Length);

            var match = _iconFinder.Match(mtgText);
            int lastUnmatched = 0;

            while (match.Success)
            {
                var unmatchedLength = match.Index - lastUnmatched;
                var matchIcons = MatchMarkup(match);

                withIcons
                    .Append(mtgText, lastUnmatched, unmatchedLength)
                    .Append(matchIcons);

                lastUnmatched = match.Index + match.Length;
                match = match.NextMatch();
            }

            var remaining = mtgText.Length - lastUnmatched;

            if (remaining > 0)
            {
                withIcons.Append(mtgText, lastUnmatched, remaining);
            }

            return withIcons.ToString();
        }


        private string MatchMarkup(Match match)
        {
            var groups = match.Groups;

            var symbolGroup = groups[nameof(_symbolPattern)];
            var loyaltyGroup = groups[nameof(_loyaltyPattern)];
            var sagaGroup = groups[nameof(_sagaPattern)];

            return groups switch
            {
                _ when symbolGroup.Success => SymbolMarkup(groups),
                _ when loyaltyGroup.Success => LoyaltyMarkup(groups),
                _ when sagaGroup.Success => SagaMarkup(groups),
                _ => throw new ArgumentException("Unexpected group")
            };
        }


        private string SymbolMarkup(GroupCollection groups)
        {
            var cost = groups[_symbol].Value.Replace("/", "").ToLower();

            cost = cost switch
            {
                "t" => "tap",
                _ => cost
            };

            return $"<i class=\"mr-2 ms ms-{cost} ms-cost\"></i>";
        }


        private string LoyaltyMarkup(GroupCollection groups)
        {
            var directionGroup = groups[_direction];

            var num = groups[_loyalty].Value;

            var dir = directionGroup.Value switch
            {
                _ when !directionGroup.Success => "zero",
                "+" => "up",
                "−" => "down",
                _ => throw new ArgumentException("Unexpected loyalty group")
            };

            return $"<i class=\"m-1 ms ms-loyalty-{dir} ms-loyalty-{num}\"></i>";
        }


        private string SagaMarkup(GroupCollection groups)
        {
            var sagaSymbols = groups[_saga].Value
                .TrimEnd()
                .Split(", ")
                .Select(ParseRomanNumeral)
                .Select(i => 
                    $"<i class=\"ms ms-saga ms-saga-{i}\"></i>");

            return string.Join('\n', sagaSymbols);
        }


        private int ParseRomanNumeral(string romanNumeral)
        {
            var result = romanNumeral
                .ToUpper()
                .Select(RomanLetterValue)
                .Sum();

            if (romanNumeral.Contains("IV")) // || romanNumeral.Contains("IX"))
            {
                result -= 2;
            }

            // if (romanNumeral.Contains("XL") || romanNumeral.Contains("XC"))
            // {
            //     result -= 20;
            // }

            // if (romanNumeral.Contains("CD") || romanNumeral.Contains("CM"))
            // {
            //     result -= 200;
            // }

            return result;
        }


        private int RomanLetterValue(char romanLetter) => romanLetter switch
        {
            'I' => 1,
            'V' => 5,
            // 'X' => 10,
            // 'L' => 50,
            // 'C' => 100,
            // 'D' => 500,
            // 'M' => 1000,
            _ => 0
        };
    }
}