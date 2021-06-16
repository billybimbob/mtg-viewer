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
            MTGCardContext dbContext,
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
            var matches = LoggedUnwrap(await _service.AllAsync());
            if (matches == null)
            {
                return Enumerable.Empty<Card>().ToList();
            }

            foreach (var match in matches)
            {
                _cache[match.Id] = match;
            }

            var cards = matches
                .Select(c => c.ToCard())
                .Where(c => TestValid(c) != null)
                .ToList();

            return cards;
        }


        public async Task<Card?> GetIdAsync(string id)
        {
            // Card card;

            if (_cache.TryGetValue(id, out ICard icard))
            {
                _logger.LogInformation($"using cached card for {id}");
                // card = await _convert.DbCard(icard);

                return TestValid(icard.ToCard());
            }

            _logger.LogInformation($"refetching {id}");

            var match = LoggedUnwrap(await _service.FindAsync(id));

            if (match == null)
            {
                _logger.LogError("match returned null");
                return null;
            }

            _cache[match.Id] = match;

            // card = await _convert.DbCard(match);

            return TestValid(match.ToCard());
        }


        private T? LoggedUnwrap<T>(IOperationResult<T> result) where T : class
        {
            var unwrap = result.Unwrap();
            if (unwrap == null)
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



        public async Task<IReadOnlyList<Card>> MatchAsync(Card card)
        {
            if (card.Id != null && _cache.TryGetValue(card.Id, out ICard icard))
            {
                return new List<Card> { card };
            }

            foreach (var info in card.GetType().GetProperties())
            {
                QueryProperty(info, info.GetValue(card));
            }

            return await SearchAsync();
        }


        private void QueryProperty(PropertyInfo info, object? objValue)
        {
            if (info.GetSetMethod() == null || info.GetGetMethod() == null)
            {
                return;
            }

            if (typeof(CardQueryParameter).GetProperty(info.Name) == null)
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
            IEnumerable<string> iter => string.Join(',', iter),
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
                Name = card.Name,
                Names = card.Names
                    ?.Select(s => new Name { Value = s })
                    .ToList(),

                Layout = card.Layout,

                Colors = card.Colors
                    ?.Select(s => new Color { Name = s })
                    .ToList(),

                Types = card.Types
                    ?.Select(s => new Data.Type { Name = s })
                    .ToList(),
                SubTypes = card.SubTypes
                    ?.Select(s => new SubType { Name = s })
                    .ToList(),
                SuperTypes = card.SuperTypes
                    ?.Select(s => new SuperType { Name = s })
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