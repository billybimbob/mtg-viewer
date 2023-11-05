using System.Collections.Generic;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Services.Search;

namespace MtgViewer.Pages.Cards;

[Authorize]
[Authorize(CardPolicies.ChangeTreasury)]
public sealed partial class Search : ComponentBase
{
    [Parameter]
    [SupplyParameterFromQuery]
    public string? ReturnUrl { get; set; }

    [Inject]
    internal NavigationManager Nav { get; set; } = default!;

    internal bool CanSearch => !CardSearch.IsEmpty;

    internal CardSearch CardSearch { get; } = new();

    internal void SubmitSearch()
    {
        var parameters = new Dictionary<string, object?>
        {
            [nameof(Create.Name)] = CardSearch.Name,
            [nameof(Create.Cmc)] = CardSearch.ManaValue,
            [nameof(Create.Colors)] = CardSearch.Colors is Color.None ? null : (int)CardSearch.Colors,

            [nameof(Create.Rarity)] = (int?)CardSearch.Rarity,
            [nameof(Create.Set)] = CardSearch.SetName,
            [nameof(Create.Types)] = CardSearch.Types,

            [nameof(Create.Power)] = CardSearch.Power,
            [nameof(Create.Loyalty)] = CardSearch.Loyalty,

            [nameof(Create.Artist)] = CardSearch.Artist,
            [nameof(Create.Text)] = CardSearch.Text,
            [nameof(Create.Flavor)] = CardSearch.Flavor,

            [nameof(Create.ReturnUrl)] = ReturnUrl
        };

        Nav.NavigateTo(
            Nav.GetUriWithQueryParameters("Cards/Create", parameters));
    }
}
