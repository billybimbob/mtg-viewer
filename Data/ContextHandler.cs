using System.Linq;
using System.Linq.Expressions;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using MTGViewer.Models;
using MtgApiManager.Lib.Model;


namespace MTGViewer.Data
{
    // look at how to refactor
    internal class ContextHandler
    {
        private readonly MTGCardContext _dbContext;

        private readonly Dictionary<string, Color> _colorMap;
        private readonly Dictionary<string, Type> _typeMap;
        private readonly Dictionary<string, SubType> _subMap;
        private readonly Dictionary<string, SuperType> _superMap;


        internal ContextHandler(MTGCardContext dbContext)
        {
            _dbContext = dbContext;

            _colorMap = new Dictionary<string, Color>();
            _typeMap = new Dictionary<string, Type>();

            _subMap = new Dictionary<string, SubType>();
            _superMap = new Dictionary<string, SuperType>();
        }

        internal IReadOnlyDictionary<string, Color> Colors => _colorMap;
        internal IReadOnlyDictionary<string, Type> Types => _typeMap;
        internal IReadOnlyDictionary<string, SubType> SubTypes => _subMap;
        internal IReadOnlyDictionary<string, SuperType> Supertypes => _superMap;


        internal async Task Update(IEnumerable<ICard> cards)
        {
            var newColors = cards.SelectMany(c => c.Colors);
            var newTypes = cards.SelectMany(c => c.Types);
            var newSubs = cards.SelectMany(c => c.SubTypes);
            var newSupers = cards.SelectMany(c => c.SuperTypes);

            await AddNewColorsAsync(newColors);
            await AddNewTypesAsync(newTypes);
            await AddNewSubTypesAsync(newSubs);
            await AddNewSuperTypesAsync(newSupers);

            await _dbContext.SaveChangesAsync();

            foreach(var entry in await ColorMapAsync(newColors))
            {
                _colorMap[entry.Key] = entry.Value;
            }

            foreach(var entry in await TypeMapAsync(newTypes))
            {
                _typeMap[entry.Key] = entry.Value;
            }

            foreach(var entry in await SubTypeMapAsync(newSubs))
            {
                _subMap[entry.Key] = entry.Value;
            }

            foreach(var entry in await SuperTypeMapAsync(newSupers))
            {
                _superMap[entry.Key] = entry.Value;
            }
        }


        private async Task<IReadOnlyDictionary<string, Color>> ColorMapAsync(
            IEnumerable<string> colors)
        {
            return await TableMapAsync(_dbContext.Colors, c => c.Name, colors);
        }

        private async Task<IReadOnlyDictionary<string, Type>> TypeMapAsync(
            IEnumerable<string> types)
        {
            return await TableMapAsync(_dbContext.Types, t => t.Name, types);
        }

        private async Task<IReadOnlyDictionary<string, SubType>> SubTypeMapAsync(
            IEnumerable<string> subtypes)
        {
            return await TableMapAsync(_dbContext.SubTypes, s => s.Name, subtypes);
        }

        private async Task<IReadOnlyDictionary<string, SuperType>> SuperTypeMapAsync(
            IEnumerable<string> supertypes)
        {
            return await TableMapAsync(_dbContext.SuperTypes, s => s.Name, supertypes);
        }


        private async Task<IReadOnlyDictionary<P, E>> TableMapAsync<E, P>(
            DbSet<E> table,
            System.Func<E, P> property,
            IEnumerable<P> values) where E : class
        {
            var uniques = values.Distinct().ToHashSet();
            return await table
                .Where(e => uniques.Contains(property(e)))
                .ToDictionaryAsync(property);
        }


        private async Task AddNewColorsAsync(IEnumerable<string> colors)
        {
            await AddNewAsync(
                _dbContext.Colors,
                c => c.Name,
                colors,
                c => new Color { Name = c });
        }


        private async Task AddNewTypesAsync(IEnumerable<string> types)
        {
            await AddNewAsync(
                _dbContext.Types,
                t => t.Name,
                types,
                t => new Type { Name = t });
        }


        private async Task AddNewSubTypesAsync(IEnumerable<string> subs)
        {
            await AddNewAsync(
                _dbContext.SubTypes,
                t => t.Name,
                subs,
                t => new SubType { Name = t });
        }

        private async Task AddNewSuperTypesAsync(IEnumerable<string> supers)
        {
            await AddNewAsync(
                _dbContext.SuperTypes,
                t => t.Name,
                supers,
                t => new SuperType { Name = t });
        }


        private async Task AddNewAsync<E, P>(
            DbSet<E> table,
            Expression<System.Func<E, P>> property,
            IEnumerable<P> values,
            System.Func<P, E> factory) where E : class
        {
            var dbValues = await table
                .Select(property)
                .Distinct()
                .ToListAsync();

            table.AddRange(
                values.Distinct().Except(dbValues)
                    .Select(v => factory(v)));
        }

    }

}