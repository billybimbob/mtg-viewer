using System.Linq;
using System.Linq.Expressions;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using MTGViewer.Models;
using MTGViewer.Services;

using MtgApiManager.Lib.Model;


#nullable enable

namespace MTGViewer.Data
{
    // look at how to refactor
    internal class ContextHandler
    {
        private readonly MTGCardContext _dbContext;

        internal ContextHandler(MTGCardContext dbContext)
        {
            _dbContext = dbContext;
        }


        internal async Task<Card> DbCard(ICard icard)
        {
            var colors = icard.Colors?.Distinct().ToArray()
                ?? Enumerable.Empty<string>();

            var types = icard.Types?.Distinct().ToArray()
                ?? Enumerable.Empty<string>();

            var subs = icard.SubTypes?.Distinct()
                ?? Enumerable.Empty<string>();

            var sups = icard.SuperTypes?.Distinct()
                ?? Enumerable.Empty<string>();

            bool colorChange = await AddNewColorsAsync(colors);
            bool typeChange = await AddNewTypesAsync(types);
            bool subChange = await AddNewSubTypesAsync(subs);
            bool superChange = await AddNewSuperTypesAsync(sups);

            if (colorChange || typeChange || subChange || superChange)
            {
                await _dbContext.SaveChangesAsync();
            }

            var card = icard.ToCard();
            card.Colors = await GetColorsAsync(colors);
            card.Types = await GetTypesAsync(types);
            card.SubTypes = await GetSubTypesAsync(subs);
            card.SuperTypes = await GetSuperTypesAsync(sups);

            return card;
        }


        private async Task<bool> AddNewColorsAsync(IEnumerable<string> name)
        {
            return await AddNewAsync(
                _dbContext.Colors,
                c => c.Name,
                name,
                c => new Color { Name = c });
        }

        private async Task<bool> AddNewTypesAsync(IEnumerable<string> names)
        {
            return await AddNewAsync(
                _dbContext.Types,
                t => t.Name,
                names,
                t => new Type { Name = t });
        }

        private async Task<bool> AddNewSubTypesAsync(IEnumerable<string> names)
        {
            return await AddNewAsync(
                _dbContext.SubTypes,
                t => t.Name,
                names,
                t => new SubType { Name = t });
        }

        private async Task<bool> AddNewSuperTypesAsync(IEnumerable<string> names)
        {
            return await AddNewAsync(
                _dbContext.SuperTypes,
                t => t.Name,
                names,
                t => new SuperType { Name = t });
        }

        private async Task<bool> AddNewAsync<E, V>(
            DbSet<E> table,
            Expression<System.Func<E, V>> property,
            IEnumerable<V> values,
            System.Func<V, E> factory) where E : class
        {
            if (values == null || !values.Any())
            {
                return false;
            }

            var dbValues = await table
                .Select(property)
                .Where(p => values.Contains(p))
                .Distinct()
                .ToListAsync();

            var newEntities = values
                .Except(dbValues)
                .Select(v => factory(v));

            if (!newEntities.Any())
            {
                return false;
            }

            table.AddRange(newEntities);
            return true;
        }


        private async Task<IList<Color>> GetColorsAsync(IEnumerable<string> names)
        {
            return await GetEntitiesAsync(
                _dbContext.Colors,
                names,
                c => names.Contains(c.Name));
        }

        private async Task<IList<Type>> GetTypesAsync(IEnumerable<string> names)
        {
            return await GetEntitiesAsync(
                _dbContext.Types,
                names,
                t => names.Contains(t.Name));
        }

        private async Task<IList<SubType>> GetSubTypesAsync(IEnumerable<string> names)
        {
            return await GetEntitiesAsync(
                _dbContext.SubTypes,
                names,
                t => names.Contains(t.Name));
        }

        private async Task<IList<SuperType>> GetSuperTypesAsync(IEnumerable<string> names)
        {
            return await GetEntitiesAsync(
                _dbContext.SuperTypes,
                names,
                t => names.Contains(t.Name));
        }


        private async Task<IList<E>> GetEntitiesAsync<V, E>(
            DbSet<E> table,
            IEnumerable<V> values,
            Expression<System.Func<E, bool>> predicate) where E : class
        {
            if (!values.Any())
            {
                return Enumerable.Empty<E>().ToList();
            }

            return await table.Where(predicate).ToListAsync();
        }
    }

}