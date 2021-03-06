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
using Microsoft.Extensions.Options;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;
using MtgViewer.Services;
using MtgViewer.Utils;

namespace MtgViewer.Pages.Decks;

[Authorize]
public partial class Mulligan : OwningComponentBase
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
    protected NavigationManager Nav { get; set; } = default!;

    [Inject]
    protected PageSize PageSize { get; set; } = default!;

    [Inject]
    protected IOptions<MulliganOptions> Options { get; set; } = default!;

    [Inject]
    protected ILogger<Mulligan> Logger { get; set; } = default!;

    private readonly CancellationTokenSource _cancel = new();
    private readonly HashSet<string> _loadedImages = new();

    private PersistingComponentStateSubscription _persistSubscription;

    private MulliganTarget? _target;
    private DeckMulligan _deckMulligan;
    private DrawSimulation? _shuffledDeck;

    private bool _isBusy;
    private bool _isInteractive;

    protected override void OnInitialized() =>
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistDeckData);

    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            string? userId = await GetUserIdAsync(_cancel.Token);

            if (userId is null)
            {
                Nav.NavigateTo("/Cards", forceLoad: true, replace: true);
                return;
            }

            _target = await GetMulliganDataAsync(userId, _cancel.Token);

            if (_target is null)
            {
                Nav.NavigateTo("/Decks", forceLoad: true, replace: true);
                return;
            }

            if (!_target.Cards.Any())
            {
                Nav.NavigateTo($"/Decks/Details/{DeckId}", forceLoad: true);
            }

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
            _cancel.Cancel();
            _cancel.Dispose();

            _persistSubscription.Dispose();

            _shuffledDeck?.Dispose();
        }

        base.Dispose(disposing);
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

    private Task PersistDeckData()
    {
        ApplicationState.PersistAsJson(nameof(_target), _target);

        return Task.CompletedTask;
    }

    private async Task<MulliganTarget?> GetMulliganDataAsync(string userId, CancellationToken cancel)
    {
        if (ApplicationState.TryGetData(nameof(_target), out MulliganTarget? target))
        {
            return target;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(cancel);

        string? name = await dbContext.Decks
            .Where(d => d.Id == DeckId && d.OwnerId == userId)
            .Select(d => d.Name)
            .SingleOrDefaultAsync(cancel);

        if (name is null)
        {
            return null;
        }

        return new MulliganTarget
        {
            Name = name,
            Cards = await DeckCardsAsync
                .Invoke(dbContext, DeckId, PageSize.Limit)
                .ToListAsync(cancel)
        };
    }

    private static readonly Func<CardDbContext, int, int, IAsyncEnumerable<DeckCopy>> DeckCardsAsync
        = EF.CompileAsyncQuery((CardDbContext db, int deck, int limit)
            => db.Cards
                .Where(c => c.Holds.Any(h => h.LocationId == deck)
                    || c.Wants.Any(w => w.LocationId == deck)
                    || c.Givebacks.Any(g => g.LocationId == deck))

                .OrderBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id)

                .Take(limit)
                .Select(c => new DeckCopy
                {
                    Id = c.Id,
                    Name = c.Name,
                    ManaCost = c.ManaCost,

                    SetName = c.SetName,
                    Rarity = c.Rarity,
                    ImageUrl = c.ImageUrl,

                    Held = c.Holds
                        .Where(h => h.LocationId == deck)
                        .Sum(h => h.Copies),

                    Want = c.Wants
                        .Where(w => w.LocationId == deck)
                        .Sum(w => w.Copies),

                    Returning = c.Givebacks
                        .Where(g => g.LocationId == deck)
                        .Sum(g => g.Copies)
                }));

    #region View Properties

    internal string DeckName => _target?.Name ?? "Deck Mulligan";

    internal IReadOnlyList<CardPreview> Hand => _shuffledDeck?.Hand ?? Array.Empty<CardPreview>();

    internal bool CanDraw => _shuffledDeck?.CanDraw ?? false;

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal DeckMulligan DeckMulligan
    {
        get => _deckMulligan;
        set
        {
            if (!_isBusy && _isInteractive && _deckMulligan != value)
            {
                _deckMulligan = value;

                _ = NewHandAsync();
            }
        }
    }

    #endregion

    internal async Task NewHandAsync()
    {
        if (_isBusy || !_isInteractive || _target is null)
        {
            return;
        }

        _isBusy = true;

        try
        {
            _shuffledDeck?.Dispose();

            if (_deckMulligan is DeckMulligan.None)
            {
                _shuffledDeck = null;
                return;
            }

            _shuffledDeck = new DrawSimulation(_target.Cards, _deckMulligan);

            int requiredCards = Options.Value.HandSize;

            while (requiredCards > 0 && _shuffledDeck.CanDraw)
            {
                await Task.Delay(Options.Value.DrawInterval, _cancel.Token);

                _shuffledDeck.DrawCard();

                requiredCards -= 1;

                StateHasChanged();
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    internal void DrawCard()
    {
        if (!_isBusy
            && _isInteractive
            && _shuffledDeck is { CanDraw: true })
        {
            _shuffledDeck.DrawCard();
        }
    }

    internal bool IsImageLoaded(CardPreview card) => _loadedImages.Contains(card.Id);

    internal void OnImageLoad(CardPreview card) => _loadedImages.Add(card.Id);
}
