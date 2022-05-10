using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Utils;

namespace MTGViewer.Pages.Decks;

[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public partial class Craft : OwningComponentBase
{
    [Parameter]
    public int DeckId { get; set; }

    [CascadingParameter]
    protected Task<AuthenticationState> AuthState { get; set; } = default!;

    [Inject]
    protected IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected PersistentComponentState ApplicationState { get; set; } = default!;

    [Inject]
    protected ParseTextFilter ParseTextFilter { get; set; } = default!;

    [Inject]
    protected PageSize PageSize { get; set; } = default!;

    [Inject]
    protected NavigationManager Nav { get; set; } = default!;

    [Inject]
    protected ILogger<Craft> Logger { get; set; } = default!;

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal BuildType BuildOption { get; private set; }

    internal SaveResult Result { get; set; }

    private readonly CancellationTokenSource _cancel = new();
    private readonly SortedSet<Card> _cards = new(CardNameComparer.Instance);

    private PersistingComponentStateSubscription _persistSubscription;

    private bool _isBusy;
    private bool _isInteractive;

    protected override void OnInitialized()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistCardDataAsync);
    }

    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            await LoadDeckDataAsync(_cancel.Token);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Warning}", ex);
        }
        catch (NavigationException ex)
        {
            Logger.LogWarning("Navigation {Warning}", ex);
        }
        finally
        {
            _isBusy = false;
        }
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            _isInteractive = true;

            StateHasChanged();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _persistSubscription.Dispose();

            _cancel.Cancel();
            _cancel.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task PersistCardDataAsync()
    {
        if (_deckContext is null)
        {
            return;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        var deckData = new DeckDto(dbContext, _deckContext);

        ApplicationState.PersistAsJson(nameof(_deckContext), deckData);

        ApplicationState.PersistAsJson(nameof(_counts), _counts);

        ApplicationState.PersistAsJson(nameof(_cards), _cards);
    }

    private async Task LoadDeckDataAsync(CancellationToken cancel)
    {
        await using var dbContext = await DbFactory.CreateDbContextAsync(cancel);

        if (ApplicationState.TryGetData(nameof(_counts), out DeckCounts? counts)
            && ApplicationState.TryGetData(nameof(_deckContext), out DeckDto? deckData)
            && ApplicationState.TryGetData(nameof(_cards), out IReadOnlyCollection<Card>? cards))
        {
            _counts = counts;
            _deckContext = deckData.ToDeckContext(dbContext);

            dbContext.Cards.AttachRange(cards);
            dbContext.Decks.Attach(_deckContext.Deck);

            _cards.UnionWith(cards);
            return;
        }

        string? userId = await GetUserIdAsync(cancel);

        if (DeckId == default)
        {
            await CreateDeckOrRedirectAsync(dbContext, userId, cancel);
        }
        else
        {
            await FetchDeckOrRedirectAsync(dbContext, userId, cancel);
        }

        await LoadInitialBuildAsync(dbContext, BuildType.Holds);
    }

    private async ValueTask<string?> GetUserIdAsync(CancellationToken cancel)
    {
        var authState = await AuthState;

        cancel.ThrowIfCancellationRequested();

        var userManager = ScopedServices.GetRequiredService<UserManager<CardUser>>();
        string? userId = userManager.GetUserId(authState.User);

        if (userId is null)
        {
            Logger.LogWarning("User {User} is missing", authState.User);
            return null;
        }

        return userId;
    }

    private async Task FetchDeckOrRedirectAsync(
        CardDbContext dbContext,
        string? userId,
        CancellationToken cancel)
    {
        if (userId is null)
        {
            Nav.NavigateTo("/Decks", forceLoad: true, replace: true);
            return;
        }

        var preview = await DeckCountsAsync.Invoke(dbContext, DeckId, cancel);

        if (preview is null || preview.OwnerId != userId || preview.HasTrades)
        {
            Nav.NavigateTo(
                Nav.GetUriWithQueryParameter(nameof(DeckId), null as int?), replace: true);
            return;
        }

        var deck = await dbContext.Decks.SingleOrDefaultAsync(d => d.Id == DeckId, cancel);

        if (deck is null)
        {
            Nav.NavigateTo(
                Nav.GetUriWithQueryParameter(nameof(DeckId), null as int?), replace: true);
            return;
        }

        _counts = preview;

        _deckContext = new DeckContext(deck);
    }

    private async Task CreateDeckOrRedirectAsync(
        CardDbContext dbContext,
        string? userId,
        CancellationToken cancel)
    {
        if (userId is null)
        {
            Nav.NavigateTo("/Decks", forceLoad: true, replace: true);
            return;
        }

        bool userExists = await dbContext.Users
            .AnyAsync(u => u.Id == userId, cancel);

        if (!userExists)
        {
            Nav.NavigateTo("/Decks", forceLoad: true, replace: true);
            return;
        }

        int userDeckCount = await dbContext.Decks
            .CountAsync(d => d.OwnerId == userId, cancel);

        var newDeck = new Deck
        {
            Name = $"Deck #{userDeckCount + 1}",
            OwnerId = userId
        };

        _counts = new DeckCounts { OwnerId = userId };
        _deckContext = new DeckContext(newDeck);
    }

    private static readonly Func<CardDbContext, int, CancellationToken, Task<DeckCounts?>> DeckCountsAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, CancellationToken _) =>
            dbContext.Decks
                .Where(d => d.Id == deckId)
                .Select(d => new DeckCounts
                {
                    OwnerId = d.OwnerId,

                    HeldCopies = d.Holds.Sum(h => h.Copies),
                    WantCopies = d.Wants.Sum(w => w.Copies),
                    ReturnCopies = d.Givebacks.Sum(g => g.Copies),

                    HeldCount = d.Holds.Count,
                    WantCount = d.Wants.Count,
                    ReturnCount = d.Givebacks.Count,

                    HasTrades = d.TradesTo.Any()
                })
                .SingleOrDefault());

    internal async Task UpdateBuildTypeAsync(ChangeEventArgs args)
    {
        if (_isBusy || !Enum.TryParse(args.Value?.ToString(), out BuildType value))
        {
            return;
        }

        if (IsBuildTypeLoaded(value))
        {
            BuildOption = value;
            return;
        }

        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await LoadInitialBuildAsync(dbContext, value);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Warning}", ex);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private bool IsBuildTypeLoaded(BuildType buildType)
    {
        return _deckContext is null
            || RequiredLoads(buildType).All(_deckContext.LoadedPages.Contains);

        static IEnumerable<PageLoad> RequiredLoads(BuildType buildType)
        {
            if (buildType is BuildType.Holds)
            {
                yield return new PageLoad(QuantityType.Hold, 0);

                yield return new PageLoad(QuantityType.Giveback, 0);
            }
            else
            {
                yield return new PageLoad(QuantityType.Want, 0);
            }
        }
    }

    private async Task LoadInitialBuildAsync(CardDbContext dbContext, BuildType build)
    {
        if (build is BuildType.Holds)
        {
            await ChangeHoldPageAsync(dbContext);

            await ChangeReturnPageAsync(dbContext);

            BuildOption = BuildType.Holds;
        }
        else
        {
            await ChangeWantPageAsync(dbContext);

            await ApplyFiltersAsync(dbContext);

            BuildOption = BuildType.Theorycrafting;
        }
    }
}
