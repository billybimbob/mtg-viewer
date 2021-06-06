using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

using MtgApiManager.Lib.Core;
using MtgApiManager.Lib.Model;
using MtgApiManager.Lib.Service;

using MTGViewer.Models;


namespace MTGViewer.Services
{
    public class MTGFetchService : IMtgQueryable<MTGFetchService, CardQueryParameter>
    {
        private ICardService _service;

        public MTGFetchService()
        {
            var provider = new MtgServiceProvider();
            _service = provider.GetCardService();
        }

        public void Reset()
        {
            _service.Reset();
        }

        public MTGFetchService Where<U>(Expression<Func<CardQueryParameter, U>> property, U value)
        {
            _service.Where(property, value);
            return this;
        }

        public async Task<Card> GetIdAsync(string id)
        {
            return (await _service.FindAsync(id))
                .Unwrap()
                .ToCard();
        }

        public async Task<IReadOnlyList<Card>> FindAsync()
        {
            return (await _service.AllAsync())
                .Unwrap()
                .Select(c => c.ToCard())
                .ToList();
        }

        public async Task<IReadOnlyList<Card>> MatchAsync(Card card)
        {
            if (card.Id != null){
                return new List<Card>{ card };
            }

            foreach (var info in card.GetType().GetProperties())
            {
                QueryProperty(info, info.GetValue(card));
            }

            return await FindAsync();
        }

        private void QueryProperty(PropertyInfo info, object value)
        {
            if (info.GetSetMethod() == null)
            {
                return;
            }

            if (typeof(CardQueryParameter).GetProperty(info.Name) == null)
            {
                return;
            }

            var strVal = StringParam(value);
            if (!string.IsNullOrEmpty(strVal))
            {
                Where(
                    PropertyExpression<CardQueryParameter, string>(info.Name), 
                    strVal
                );
            }
        }

        private string StringParam(object paramValue) => paramValue switch
        {
            IEnumerable<string> iter => string.Join(',', iter),
            null => null,
            _ => paramValue.ToString()
        };

        private Expression<Func<Q, R>> PropertyExpression<Q, R>(string propName)
        {
            var xParam = Expression.Parameter(typeof(Q), "x");
            var propExpr = Expression.Property(xParam, propName);

            return Expression.Lambda<Func<Q, R>>(propExpr, xParam);
        }

    }


    public static class MtgApiExtension
    {
        public static T Unwrap<T>(this IOperationResult<T> result) where T : class
        {
            if (result.IsSuccess)
            {
                return result.Value;
            }
            else
            {
                throw result.Exception;
            }
        }

        public static Card ToCard(this ICard card)
        {
            return new Card
            {
                Id = card.Id,
                Name = card.Name,
                Names = card.Names?
                    .Select(s => new Name{ Value = s })
                    .ToList(),

                Layout = card.Layout,

                ManaCost = card.ManaCost,
                Cmc = (int)card.Cmc,
                Colors = card.Colors?
                    .Select(s => new Color{ Value = s })
                    .ToList(),

                SuperTypes = card.SuperTypes?
                    .Select(s => new SuperType{ Value = s })
                    .ToList(),
                Types = card.Types?
                    .Select(s => new Models.Type{ Value = s })
                    .ToList(),
                SubTypes = card.SubTypes?
                    .Select(s => new SubType{ Value = s })
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