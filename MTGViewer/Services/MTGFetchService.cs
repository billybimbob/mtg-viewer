using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Core;
using MtgApiManager.Lib.Model;
using MtgApiManager.Lib.Service;

using MTGViewer.Data;

# nullable enable

namespace MTGViewer.Services
{
    public class CardQuery : Card, IQueryParameter { }


    public class MTGFetchService : IMtgQueryable<MTGFetchService, CardQuery>
    {
        private readonly ICardService _service;
        private readonly DataCacheService _cache;

        private readonly int _pageSize;
        private readonly ILogger<MTGFetchService> _logger;

        private bool _empty;


        public MTGFetchService(
            ICardService service,
            DataCacheService cache, 
            PageSizes pageSizes,
            ILogger<MTGFetchService> logger)
        {
            _service = service;
            _cache = cache;

            _pageSize = pageSizes.Default;
            _logger = logger;

            _empty = true;
        }


        public void Reset()
        {
            _service.Reset();
            _empty = true;
        }


        public MTGFetchService Where<P>(Expression<Func<CardQuery, P>> property, P value)
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
            IEnumerable<object> iter when iter.Any() => string.Join(',', iter),
            IEnumerable<object> _ => null,
            _ => paramValue?.ToString()
        };


        private static Expression<Func<Q, R>> PropertyExpression<Q, R>(string propName)
        {
            var xParam = Expression.Parameter(typeof(Q), "x");
            var propExpr = Expression.Property(xParam, propName);

            return Expression.Lambda<Func<Q, R>>(propExpr, xParam);
        }



        public async Task<PagedList<Card>> SearchAsync(int page = 0)
        {
            if (_empty)
            {
                return PagedList<Card>.Empty;
            }

            page = Math.Max(page, 0);

            var response = await _service
                .Where(c => c.PageSize, _pageSize)
                .Where(c => c.Page, page + 1)
                // .Where(c => c.OrderBy, "name") get error code 500 with this
                .AllAsync();

            _empty = true;

            var matches = LoggedUnwrap(response) ?? Enumerable.Empty<ICard>();

            if (!matches.Any())
            {
                return PagedList<Card>.Empty;
            }

            var pages = new Data.Pages(page, response.PagingInfo.TotalPages);

            var cards = matches
                .Select(c => c.ToCard())
                .Where(c => TestValid(c) is not null)
                .GroupBy(c => c.MultiverseId, (_, cards) => cards.First())
                .ToList();

            // adventure cards have multiple entries with the same multiId

            foreach (var card in cards)
            {
                _cache[card.MultiverseId] = card;
            }

            return new PagedList<Card>(pages, cards);
        }


        public async Task<Card?> FindAsync(string id)
        {
            Reset();

            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            if (_cache.TryGetValue(id, out Card? card))
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



        public async Task<PagedList<Card>> MatchAsync(Card search, int page = 0)
        {
            if (search.MultiverseId is not null)
            {
                var card = await FindAsync(search.MultiverseId);

                return card is null
                    ? PagedList<Card>.Empty

                    : new PagedList<Card>(
                        new Data.Pages(0, 1), new List<Card>{ card });
            }
            else
            {
                foreach (var info in typeof(Card).GetProperties())
                {
                    if (info?.GetGetMethod() is not null
                        && info?.GetSetMethod() is not null)
                    {
                        QueryProperty(info.Name, info.GetValue(search));
                    }
                }

                return await SearchAsync(page);
            }
        }

    }


    internal static class MtgApiExtension
    {
        internal static R? Unwrap<R>(this IOperationResult<R> result) where R : class =>
            result.IsSuccess ? result.Value : null;

    
        internal static Card ToCard(this ICard card) => new Card
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

            Subtypes = (card.SubTypes?.Select(s => new Subtype(s))
                ?? Enumerable.Empty<Subtype>())
                .ToList(),

            Supertypes = (card.SuperTypes?.Select(s => new Supertype(s))
                ?? Enumerable.Empty<Supertype>())
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