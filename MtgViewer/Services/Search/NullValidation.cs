using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace MtgViewer.Services.Search;

internal class NullValidation<TEntity> : IValidatableObject where TEntity : notnull
{
    private readonly TEntity _entity;
    private readonly ValidationContext _nestedValidation;

    public NullValidation(TEntity entity)
    {
        _entity = entity;
        _nestedValidation = new ValidationContext(entity);
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var nestedResults = new List<ValidationResult>();

        Validator.TryValidateObject(_entity, _nestedValidation, nestedResults);

        foreach (var error in nestedResults)
        {
            yield return error;
        }

        const NullabilityState nullable = NullabilityState.Nullable;

        var nullCheck = new NullabilityInfoContext();

        foreach (var property in typeof(TEntity).GetProperties())
        {
            var nullInfo = nullCheck.Create(property);

            if (nullInfo is { ReadState: nullable, WriteState: nullable })
            {
                continue;
            }

            if (property.GetValue(_entity) is null)
            {
                yield return new ValidationResult($"{property.Name} is null");
            }
        }
    }
}
