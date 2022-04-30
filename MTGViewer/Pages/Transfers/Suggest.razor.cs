using System;
using System.Collections.Generic;
using System.Linq;
using System.Paging;
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

namespace MTGViewer.Pages.Transfers;

[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public partial class Suggest : OwningComponentBase
{
    [Parameter]
    public string CardId { get; set; } = default!;

    [Parameter]
    [SupplyParameterFromQuery]
    public string? ReceiverId { get; set; }

    [CascadingParameter]
    protected Task<AuthenticationState> AuthState { get; set; } = default!;

    [Inject]
    protected IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected NavigationManager Nav { get; set; } = default!;

    [Inject]
    protected PageSize PageSize { get; set; } = default!;

    [Inject]
    protected PersistentComponentState ApplicationState { get; set; } = default!;

    [Inject]
    protected ILogger<Suggest> Logger { get; set; } = default!;

    internal SuggestionDto Suggestion { get; } = new();

    private readonly CancellationTokenSource _cancel = new();

    private bool _isBusy;
    private bool _isInteractive;

    private PersistingComponentStateSubscription _persistSubscription;

    private DeckCursor _cursor;

    protected override void OnInitialized()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistSuggestionData);

        _cursor = new DeckCursor
        {
            PageSize = PageSize.Current
        };

        Suggestion.ReceiverChanged += OnReceiverChange;
    }

    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            var token = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            if (Suggestion.Card?.Id != CardId)
            {
                await LoadCardAsync(dbContext, token);
            }

            if (Suggestion.Card is null)
            {
                Logger.LogError("Card {CardId} is not valid", CardId);

                Nav.NavigateTo("/Cards/", forceLoad: true, replace: true);

                return;
            }

            await LoadReceiverAsync(dbContext, token);

            if (ReceiverId is not null && Suggestion.Receiver is null)
            {
                Logger.LogError("User {ReceiverId} is not valid", ReceiverId);

                Nav.NavigateTo(
                    Nav.GetUriWithQueryParameter(nameof(ReceiverId), null as string), replace: true);
            }
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);
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

            Suggestion.ReceiverChanged -= OnReceiverChange;

            _cancel.Cancel();
            _cancel.Dispose();
        }

        base.Dispose(disposing);
    }

    private Task PersistSuggestionData()
    {
        ApplicationState.PersistAsJson(nameof(Suggestion.Card), Suggestion.Card);

        ApplicationState.PersistAsJson(nameof(Suggestion.UserOptions), Suggestion.UserOptions);

        ApplicationState.PersistAsJson(nameof(_cursor.TotalDecks), _cursor.TotalDecks);

        ApplicationState.PersistAsJson(nameof(Suggestion.DeckOptions), Suggestion.DeckOptions);

        return Task.CompletedTask;
    }

    private async Task LoadCardAsync(CardDbContext dbContext, CancellationToken cancel)
    {
        if (ApplicationState.TryGetData(nameof(Suggestion.Card), out Card? card))
        {
            Suggestion.Card = card;
            return;
        }

        Suggestion.Card = await dbContext.Cards
            .SingleOrDefaultAsync(c => c.Id == CardId, cancel);
    }

    private async Task LoadReceiverAsync(CardDbContext dbContext, CancellationToken cancel)
    {
        if (!Suggestion.UserOptions.Any())
        {
            await LoadUserOptionsAsync(dbContext, cancel);
        }

        Suggestion.Receiver = Suggestion.UserOptions
            .SingleOrDefault(u => u.Id == ReceiverId);

        await LoadReceiverDecksAsync(dbContext, cancel);
    }

    private async Task LoadUserOptionsAsync(CardDbContext dbContext, CancellationToken cancel)
    {
        if (Suggestion.Card is not Card card)
        {
            return;
        }

        const string key = nameof(Suggestion.UserOptions);

        if (ApplicationState.TryGetData(key, out IAsyncEnumerable<UserPreview>? users))
        {
            await Suggestion.AddUsersAsync(users, cancel);
            return;
        }

        string? userId = await GetUserIdAsync(cancel);

        if (userId is null)
        {
            return;
        }

        users = UsersForSuggestion(dbContext, card, userId)
            .AsAsyncEnumerable();

        await Suggestion.AddUsersAsync(users, cancel);
    }

    private async ValueTask<string?> GetUserIdAsync(CancellationToken cancel)
    {
        var authState = await AuthState;

        cancel.ThrowIfCancellationRequested();

        var userManager = ScopedServices.GetRequiredService<UserManager<CardUser>>();

        string? userId = userManager.GetUserId(authState.User);

        if (userId is null)
        {
            Logger.LogWarning("User is missing");
        }

        return userId;
    }

    private async Task LoadReceiverDecksAsync(CardDbContext dbContext, CancellationToken cancel)
    {
        if (Suggestion is not
            { Card.Name: string name, Receiver.Id: string id })
        {
            return;
        }

        if (!ApplicationState.TryGetData(nameof(_cursor.TotalDecks), out int deckCount))
        {
            deckCount = await DecksForSuggest(dbContext, name, id).CountAsync(cancel);
        }

        if (ApplicationState.TryGetData(nameof(Suggestion.DeckOptions), out IEnumerable<DeckPreview>? decks))
        {
            await Suggestion.AddDecksAsync(decks.ToAsyncEnumerable(), cancel);
        }
        else
        {
            decks = Enumerable.Empty<DeckPreview>();
        }

        _cursor = _cursor with
        {
            TotalDecks = deckCount,
            LoadedDecks = decks.Count()
        };

        _cursor = await LoadDeckPageAsync(dbContext, _cursor, Suggestion, cancel);
    }

    private void OnReceiverChange(object? sender, ReceiverEventArgs args)
    {
        if (_isBusy || Suggestion.Card is null)
        {
            return;
        }

        _isBusy = true;

        // triggers on ParameterSet, where IsBusy set to false

        Nav.NavigateTo(
            Nav.GetUriWithQueryParameter(nameof(ReceiverId), args.ReceiverId),
            replace: true);
    }

    #region View Properties

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal bool HasMore => _cursor.HasMore;

    internal bool HasDetails => Suggestion.ToId is not null;

    internal bool IsMissingUsers =>
        !_isBusy && Suggestion is { Card: Card, UserOptions.Count: 0 };

    #endregion

    internal void ViewDeckDetails()
    {
        if (_isBusy || Suggestion.ToId is not int deckId)
        {
            return;
        }

        _isBusy = true;

        // triggers on ParameterSet, where IsBusy set to false

        Nav.NavigateTo($"/Decks/Details/{deckId}", forceLoad: true);
    }

    internal async Task LoadMoreDecksAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            var token = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            _cursor = await LoadDeckPageAsync(dbContext, _cursor, Suggestion, token);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private static async Task<DeckCursor> LoadDeckPageAsync(
        CardDbContext dbContext,
        DeckCursor cursor,
        SuggestionDto suggestion,
        CancellationToken cancel)
    {
        if (!cursor.HasMore)
        {
            return cursor;
        }

        if (suggestion is not
            { Card.Name: string cardName, Receiver.Id: string receiverId })
        {
            return cursor;
        }

        var decks = DecksForSuggest(dbContext, cardName, receiverId)
            .Skip(cursor.LoadedDecks)
            .Take(cursor.PageSize)
            .AsAsyncEnumerable();

        await suggestion.AddDecksAsync(decks, cancel);

        return cursor.NextPage();
    }

    public async Task SendSuggestionAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (Suggestion is not
            { Card.Id: string cardId, Receiver.Id: string receiverId })
        {
            return;
        }

        _isBusy = true;

        try
        {
            var token = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            var suggestion = new Suggestion
            {
                CardId = cardId,
                ReceiverId = receiverId,
                ToId = Suggestion.ToId,
                Comment = Suggestion.Comment
            };

            dbContext.Suggestions.Attach(suggestion);

            await dbContext.SaveChangesAsync(token);

            Nav.NavigateTo("/Cards", forceLoad: true);
        }
        catch (DbUpdateException ex)
        {
            Logger.LogError("{Error}", ex);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private readonly struct DeckCursor
    {
        public int PageSize { get; init; }
        public int TotalDecks { get; init; }
        public int LoadedDecks { get; init; }

        public bool HasMore => LoadedDecks < TotalDecks;

        public DeckCursor NextPage() => this with
        {
            LoadedDecks = LoadedDecks + PageSize
        };
    }

    private static IQueryable<UserPreview> UsersForSuggestion(
        CardDbContext dbContext,
        Card card,
        string proposerId)
    {
        var nonProposers = dbContext.Users
            .Where(u => u.Id != proposerId)
            .OrderBy(u => u.Name)
                .ThenBy(u => u.Id);

        var cardSuggests = dbContext.Suggestions
            .Where(s => s.Card.Name == card.Name && s.ReceiverId != proposerId);

        return nonProposers
            .GroupJoin(cardSuggests,
                user => user.Id,
                suggest => suggest.ReceiverId,
                (User, Suggests) => new { User, Suggests })

            .SelectMany(
                uss => uss.Suggests.DefaultIfEmpty(),
                (uss, Suggest) => new { uss.User, Suggest })

            .Where(us => us.Suggest == default)
            .Select(us => new UserPreview
            {
                Id = us.User.Id,
                Name = us.User.Name
            });
    }

    private static IQueryable<DeckPreview> DecksForSuggest(
        CardDbContext dbContext,
        string cardName,
        string receiverId)
    {
        var userDecks = dbContext.Decks
            .Where(d => d.OwnerId == receiverId
                && !d.Holds.Any(h => h.Card.Name == cardName)
                && !d.Wants.Any(w => w.Card.Name == cardName
                && !d.TradesTo.Any(t => t.Card.Name == cardName)))

            .OrderBy(d => d.Name)
                .ThenBy(d => d.Id);

        var suggestsWithCard = dbContext.Suggestions
            .Where(s => s.Card.Name == cardName
                && s.ReceiverId == receiverId);

        return userDecks
            .GroupJoin(suggestsWithCard,
                deck => deck.Id,
                suggest => suggest.ToId,
                (Deck, Suggests) => new { Deck, Suggests })
            .SelectMany(
                dts => dts.Suggests.DefaultIfEmpty(),
                (dts, Suggest) => new { dts.Deck, Suggest })

            .Where(ds => ds.Suggest == default)
            .Select(ds => new DeckPreview
            {
                Id = ds.Deck.Id,
                Name = ds.Deck.Name,
                Color = ds.Deck.Color
            });
    }

}
