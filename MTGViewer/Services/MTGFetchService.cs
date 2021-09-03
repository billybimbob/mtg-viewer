using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Core;
using MtgApiManager.Lib.Model;
using MtgApiManager.Lib.Service;

using MTGViewer.Data;


# nullable enable

namespace MTGViewer.Services
{
    public class MTGFetchService : IMtgQueryable<MTGFetchService, Card>
    {
        private bool _empty;
        private readonly ICardService _service;
        private readonly DataCacheService _cache;
        private readonly ILogger<MTGFetchService> _logger;


        public MTGFetchService(
            MtgServiceProvider provider,
            DataCacheService cache, 
            ILogger<MTGFetchService> logger)
        {
            _empty = true;
            _service = provider.GetCardService();
            _cache = cache;
            _logger = logger;
        }


        public void Reset()
        {
            _service.Reset();
            _empty = true;
        }


        public MTGFetchService Where<P>(Expression<Func<Card, P>> property, P value)
        {
            if (property.Body is MemberExpression expression)
            {
                QueryProperty(expression.Member.Name, value);
            }

            return this;
        }



        private void QueryProperty(string propertyName, object? objValue)
        {
            var propertyValue = ToString(objValue);

            if (string.IsNullOrEmpty(propertyValue))
            {
                return;
            }

            if (typeof(CardQueryParameter).GetProperty(propertyName) == null)
            {
                return;
            }

            var property = PropertyExpression<CardQueryParameter, string>(propertyName);

            _service.Where(property, propertyValue);
            _empty = false;
        }


        private static string? ToString(object? paramValue) => paramValue switch
        {
            null => null,
            IEnumerable<object> iter when iter.Any() => string.Join(',', iter),
            IEnumerable<object> _ => null,
            _ => paramValue.ToString()
        };


        private static Expression<Func<Q, R>> PropertyExpression<Q, R>(string propName)
        {
            var xParam = Expression.Parameter(typeof(Q), "x");
            var propExpr = Expression.Property(xParam, propName);

            return Expression.Lambda<Func<Q, R>>(propExpr, xParam);
        }



        public async Task<IReadOnlyList<Card>> SearchAsync()
        {
            if (_empty)
            {
                return new List<Card>();
            }

            var response = await _service
                .Where(c => c.PageSize, 10) // TODO: make size param
                .AllAsync();

            _empty = true;

            IEnumerable<ICard>? matches = LoggedUnwrap(response);

            matches ??= Enumerable.Empty<ICard>();

            var cards = matches
                .Select(c => c.ToCard())
                .Where(c => TestValid(c) is not null)
                .OrderBy(c => c.Name)
                .ToList();

            foreach (var card in cards)
            {
                _cache[card.MultiverseId] = card;
            }

            return cards;
        }


        public async Task<Card?> FindAsync(string id)
        {
            Reset();

            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            Card? card;

            if (_cache.TryGetValue(id, out card))
            {
                _logger.LogInformation($"using cached card for {id}");
                return card;
            }

            _logger.LogInformation($"refetching {id}");

            var match = LoggedUnwrap(await _service.FindAsync(id));

            if (match is null)
            {
                _logger.LogError("match returned null");
                return null;
            }

            card = TestValid(match.ToCard());

            if (card is not null)
            {
                _cache[card.MultiverseId] = card;
            }

            return card;
        }


        private T? LoggedUnwrap<T>(IOperationResult<T> result) where T : class
        {
            var unwrap = result.Unwrap();

            if (unwrap is null)
            {
                _logger.LogError(result.Exception.ToString());
            }

            return unwrap;
        }


        private Card? TestValid(Card card)
        {
            if (!card.IsValid())
            {
                _logger.LogError($"{card?.Id} was found, but failed validation");
                return null;
            }
            else
            {
                return card;
            }
        }



        public async Task<IReadOnlyList<Card>> MatchAsync(Card search)
        {
            if (search.MultiverseId != default)
            {
                List<Card> results = new();

                var card = await FindAsync(search.MultiverseId);

                if (card is not null)
                {
                    results.Add(card);
                }

                return results;
            }
            else
            {
                foreach (var info in search.GetType().GetProperties())
                {
                    if (info?.GetGetMethod() is not null
                        && info?.GetSetMethod() is not null)
                    {
                        QueryProperty(info.Name, info.GetValue(search));
                    }
                }

                return await SearchAsync();
            }
        }

    }


    internal static class MtgApiExtension
    {
        internal static R? Unwrap<R>(this IOperationResult<R> result) where R : class =>
            result.IsSuccess ? result.Value : null;

    
        internal static Card ToCard(this ICard card)
        {
            return new Card
            {
                Id = card.Id, // id should be valid
                MultiverseId = card.MultiverseId,
                Name = card.Name,
                Names = (card.Names?.Select(s => new Name(s))
                    ?? Enumerable.Empty<Name>())
                    .ToList(),

                Layout = card.Layout,

                Colors = (card.Colors?.Select(s => new Color(s))
                    ?? Enumerable.Empty<Color>())
                    .ToList(),

                Types = (card.Types?.Select(s => new Data.Type(s))
                    ?? Enumerable.Empty<Data.Type>())
                    .ToList(),

                SubTypes = (card.SubTypes?.Select(s => new SubType(s))
                    ?? Enumerable.Empty<SubType>())
                    .ToList(),

                SuperTypes = (card.SuperTypes?.Select(s => new SuperType(s))
                    ?? Enumerable.Empty<SuperType>())
                    .ToList(),

                ManaCost = card.ManaCost,
                Cmc = (int?)card.Cmc ?? default,

                Rarity = card.Rarity,
                SetName = card.SetName,
                Artist = card.Artist,

                Text = card.Text,
                Flavor = card.Flavor,

                Power = card.Power,
                Toughness = card.Toughness,
                Loyalty = card.Loyalty,
                ImageUrl = card.ImageUrl?.ToString()
            };
        }
    }

}