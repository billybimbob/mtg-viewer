using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

#nullable enable

namespace MTGViewer.Services
{
    public class MTGSymbols
    {
        private const string _mana = 
            @"{(?<" + nameof(_mana) + @">[^}]+)}";

        private const string _direction = "direction";
        private const string _loyalty = 
            @"\[(?<" + _direction + @">[+−])?(?<" + nameof(_loyalty) + @">\d+)\]";

        private const string _saga = 
            @"(?<" + nameof(_saga) + @">(?:[IV]+(?:, )?)+) —";


        private readonly Regex _symbolFinder;
        private readonly IServiceProvider _serviceProvider;

        public MTGSymbols(IServiceProvider serviceProvider)
        {
            _symbolFinder = new($@"(?:{_mana}|{_loyalty}|{_saga})");
            _serviceProvider = serviceProvider;
        }


        public ISymbolTranslator GetTranslator<T>() where T : ISymbolTranslator
        {
            return _serviceProvider.GetRequiredService<T>();
        }


        public string[] FindSymbols(string mtgText)
        {
            return _symbolFinder
                .Matches(mtgText)
                .SelectMany(MatchValue)
                .ToArray();
        }


        private IEnumerable<string> MatchValue(Match match)
        {
            var groups = match.Groups;

            var mana = groups[nameof(_mana)];
            var loyalty = groups[nameof(_loyalty)];
            var saga = groups[nameof(_saga)];

            string[] Mana() => new []{ mana.Value };

            string[] Loyalty()
            {
                var direction = groups[_direction];
                return new [] { direction.Value + loyalty.Value };
            }

            string[] Sagas() => saga.Value.Split(", ");

            return groups switch
            {
                _ when mana.Success => Mana(),
                _ when loyalty.Success => Loyalty(),
                _ when saga.Success => Sagas(),
                _ => throw new ArgumentException("Unexpected group")
            };
        }



        public string Format<T>(string mtgText) where T : ISymbolTranslator
        {
            var translator = _serviceProvider.GetService<T>();

            if (string.IsNullOrEmpty(mtgText) || translator is null)
            {
                return string.Empty;
            }

            var translation = new StringBuilder();

            var match = _symbolFinder.Match(mtgText);
            int lastUnmatched = 0;

            while (match.Success)
            {
                var unmatchedLength = match.Index - lastUnmatched;
                var matchTranslated = TranslatedMatch(match, translator);

                translation
                    .Append(mtgText, lastUnmatched, unmatchedLength)
                    .Append(matchTranslated);

                lastUnmatched = match.Index + match.Length;
                match = match.NextMatch();
            }

            var remaining = mtgText.Length - lastUnmatched;

            if (remaining > 0)
            {
                translation.Append(mtgText, lastUnmatched, remaining);
            }

            return translation.ToString();
        }


        private string TranslatedMatch(Match match, ISymbolTranslator translator)
        {
            var groups = match.Groups;

            var mana = groups[nameof(_mana)];
            var loyalty = groups[nameof(_loyalty)];
            var saga = groups[nameof(_saga)];

            string Mana() => 
                translator.TranslateMana(mana.Value);

            string Loyalty()
            {
                var direction = groups[_direction];
                var directionValue = direction.Success ? direction.Value : null;

                return translator.TranslateLoyalty(directionValue, loyalty.Value);
            }

            string Saga()
            {
                var sagaSymbols = saga.Value.Split(", ");
                var translatedSagas = sagaSymbols.Select((s, i) => 
                    translator.TranslateSaga(s, i == sagaSymbols.Length - 1));

                return string.Join(string.Empty, translatedSagas);
            }

            return groups switch
            {
                _ when mana.Success => Mana(),
                _ when loyalty.Success => Loyalty(),
                _ when saga.Success => Saga(),
                _ => throw new ArgumentException("Unexpected group")
            };
        }
    }



    public class CardTextTranslator : ISymbolTranslator
    {
        public string TranslateMana(string mana)
        {
            return $"{{{mana}}}";
        }

        public string TranslateLoyalty(string? direction, string loyalty)
        {
            return $"[{direction}{loyalty}]";
        }

        public string TranslateSaga(string saga, bool isFinal)
        {
            return !isFinal ? $"{saga}, " : $"{saga} —";
        }
    }
}