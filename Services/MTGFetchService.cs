using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Core;
using MtgApiManager.Lib.Model;
using MtgApiManager.Lib.Service;

using MTGViewer.Data;
using MTGViewer.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;


# nullable enable

namespace MTGViewer.Services
{

    public class MTGFetchService : IMtgQueryable<MTGFetchService, CardQueryParameter>
    {
        private readonly ILogger<MTGFetchService> _logger;
        private readonly ICardService _service;
        private readonly DataCacheService _cache;
        private readonly ContextHandler _context;


        public MTGFetchService(
            MtgServiceProvider provider,
            DataCacheService cache, 
            MTGCardContext dbContext,
            ILogger<MTGFetchService> logger)
        {
            _logger = logger;
            _service = provider.GetCardService();
            _cache = cache;
            _context = new ContextHandler(dbContext);
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

            await _context.Update(matches);

            var cards = matches
                .Select(c => DbCard(c))
                .Where(c => c.IsValid())
                .ToList();

            foreach (var match in matches)
            {
                _cache[match.Id] = match;
            }

            return cards;
        }

        public async Task<Card?> GetIdAsync(string id)
        {
            Card? card;

            if (_cache.TryGetValue(id, out card))
            {
                _logger.LogInformation($"using cached card for {id}");
                return card;
            }

            _logger.LogInformation($"refetching {id}");

            var match = LoggedUnwrap(await _service.FindAsync(id));
            if (match == null)
            {
                _logger.LogError("match returned null");
                return null;
            }

            await _context.Update(new List<ICard> { match });
            card = DbCard(match);

            if (card == null || !card.IsValid())
            {
                _logger.LogError($"{id} was found, but failed validation");
                return null;
            }

            _cache[match.Id] = match;

            return card;
        }


        public async Task<IReadOnlyList<Card>> MatchAsync(Card card)
        {
            if (card.Id != null && _cache.TryGetValue(card.Id, out card))
            {
                return new List<Card> { card };
            }

            foreach (var info in card.GetType().GetProperties())
            {
                QueryProperty(info, info.GetValue(card));
            }

            return await SearchAsync();
        }



        private T? LoggedUnwrap<T>(IOperationResult<T> result) where T : class
        {
            if (result.IsSuccess)
            {
                return result.Value;
            }
            else
            {
                _logger.LogError(result.Exception.ToString());
                return null;
            }
        }


        private void QueryProperty(PropertyInfo info, object? value)
        {
            if (info.GetSetMethod() == null || info.GetGetMethod() == null)
            {
                return;
            }

            if (typeof(CardQueryParameter).GetProperty(info.Name) == null)
            {
                return;
            }

            var strVal = StringParam(value);
            if (string.IsNullOrEmpty(strVal))
            {
                return;
            }

            Where(
                PropertyExpression<CardQueryParameter, string>(info.Name),
                strVal);
        }


        private static string? StringParam(object? paramValue) => paramValue switch
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

        public Card DbCard(ICard card)
        {
            return new Card
            {
                Id = card.Id, // id should be valid
                Name = card.Name,
                Names = card.Names?
                    .Select(s => new Name { Value = s })
                    .ToList(),

                Layout = card.Layout,

                ManaCost = card.ManaCost,
                Cmc = (int?)card.Cmc ?? default,
                Colors = card.Colors?
                    .Select(s => _context.Colors[s])
                    .ToList(),

                SuperTypes = card.SuperTypes?
                    .Select(s => _context.Supertypes[s])
                    .ToList(),
                Types = card.Types?
                    .Select(s => _context.Types[s])
                    .ToList(),
                SubTypes = card.SubTypes? 
                    .Select(s => _context.SubTypes[s])
                    .ToList(),

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