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

    public class MTGFetchService : IMtgQueryable<MTGFetchService, CardQueryParameter>
    {
        private readonly ILogger<MTGFetchService> _logger;
        private readonly ICardService _service;
        private readonly DataCacheService _cache;


        public MTGFetchService(
            MtgServiceProvider provider,
            DataCacheService cache, 
            ILogger<MTGFetchService> logger)
        {
            _logger = logger;
            _service = provider.GetCardService();
            _cache = cache;
        }


        public void Reset()
        {
            _service.Reset();
        }


        public MTGFetchService Where<U>(
            Expression<Func<CardQueryParameter, U>> property, U value)
        {
            _service.Where(property, value);
            return this;
        }


        public async Task<IReadOnlyList<Card>> SearchAsync()
        {
            var response = await _service
                .Where(c => c.PageSize, 10) // TODO: make size param
                .AllAsync();

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


        public async Task<Card?> GetIdAsync(string id)
        {
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
            if (search.MultiverseId != default 
                && _cache.TryGetValue(search.MultiverseId, out Card card))
            {
                return new List<Card> { card };
            }

            foreach (var info in search.GetType().GetProperties())
            {
                QueryProperty(info, info.GetValue(search));
            }

            return await SearchAsync();
        }


        private void QueryProperty(PropertyInfo info, object? objValue)
        {
            if (info.GetSetMethod() is null || info.GetGetMethod() is null)
            {
                return;
            }

            if (typeof(CardQueryParameter).GetProperty(info.Name) is null)
            {
                return;
            }

            var propertyValue = ToString(objValue);
            if (string.IsNullOrEmpty(propertyValue))
            {
                return;
            }

            Where(
                PropertyExpression<CardQueryParameter, string>(info.Name),
                propertyValue);
        }


        private static string? ToString(object? paramValue) => paramValue switch
        {
            IEnumerable<object> iter1 when !iter1.Any() => null,
            IEnumerable<object> iter2 => string.Join(',', iter2),
            null => null,
            _ => paramValue.ToString()
        };


        private static Expression<Func<Q, R>> PropertyExpression<Q, R>(string propName)
        {
            var xParam = Expression.Parameter(typeof(Q), "x");
            var propExpr = Expression.Property(xParam, propName);

            return Expression.Lambda<Func<Q, R>>(propExpr, xParam);
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
                Names = card.Names
                    ?.Select(s => new Name(s))
                    .ToHashSet(),

                Layout = card.Layout,

                Colors = card.Colors
                    ?.Select(s => new Color(s))
                    .ToHashSet(),

                Types = card.Types
                    ?.Select(s => new Data.Type(s))
                    .ToHashSet(),
                SubTypes = card.SubTypes
                    ?.Select(s => new SubType(s))
                    .ToHashSet(),
                SuperTypes = card.SuperTypes
                    ?.Select(s => new SuperType(s))
                    .ToHashSet(),

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