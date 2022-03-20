using System;
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
    protected PageSizes PageSizes { get; set; } = default!;

    [Inject]
    protected ILogger<Suggest> Logger { get; set; } = default!;

    internal bool HasMore => _cursor.HasMore;

    internal bool HasDetails => Suggestion.ToId is not null;

    internal SuggestionDto Suggestion { get; } = new();

    internal bool IsBusy { get; private set; }

    private readonly CancellationTokenSource _cancel = new();
    private DeckCursor _cursor;


    protected override void OnInitialized()
    {
        int pageSize = PageSizes.GetComponentSize<Suggest>();

        _cursor = new DeckCursor(pageSize);

        Suggestion.ReceiverChanged += OnReceiverChange;
    }


    protected override async Task OnParametersSetAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var token = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            if (Suggestion.Card?.Id != CardId)
            {
                Suggestion.Card = await dbContext.Cards
                    .SingleOrDefaultAsync(c => c.Id == CardId, token);
            }

            if (Suggestion.Card is null)
            {
                Logger.LogError("Card {CardId} is not valid", CardId);

                Nav.NavigateTo("/Cards/Index", forceLoad: true, replace: true);
                return;
            }

            await LoadReceiverAsync(dbContext, token);

            if ((ReceiverId, Suggestion) is (not null, { Receiver: null }))
            {
                Logger.LogError("User {ReceiverId} is not valid", ReceiverId);

                Nav.NavigateTo(
                    Nav.GetUriWithQueryParameter(nameof(ReceiverId), null as string), replace: true);
                return;
            }
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Suggestion.ReceiverChanged -= OnReceiverChange;

            _cancel.Cancel();
            _cancel.Dispose();
        }

        base.Dispose(disposing);
    }


    private async ValueTask<string?> GetUserIdAsync(CancellationToken cancel)
    {
        var authState = await AuthState;

        cancel.ThrowIfCancellationRequested();

        var userManager = ScopedServices.GetRequiredService<UserManager<CardUser>>();
        var userId = userManager.GetUserId(authState.User);

        if (userId is null)
        {
            Logger.LogWarning("User is missing");
        }

        return userId;
    }


    private async Task LoadReceiverAsync(
        CardDbContext dbContext,
        CancellationToken cancel)
    {
        if (!Suggestion.UserOptions.Any())
        {
            var userId = await GetUserIdAsync(cancel);
            await LoadUserOptionsAsync(dbContext, userId, Suggestion, cancel);
        }

        Suggestion.Receiver = Suggestion.UserOptions
            .SingleOrDefault(u => u.Id == ReceiverId);

        if (Suggestion is not
            {
                Card.Name: string name,
                Receiver.Id: string id
            })
        {
            return;
        }

        int deckCount = await DecksForSuggest(dbContext, name, id).CountAsync(cancel);

        _cursor = _cursor.WithTotal(deckCount);

        _cursor = await LoadDeckPageAsync(dbContext, _cursor, Suggestion, cancel);
    }


    private static async Task LoadUserOptionsAsync(
        CardDbContext dbContext,
        string? userId,
        SuggestionDto suggestion,
        CancellationToken cancel)
    {
        if (suggestion.Card is not Card card)
        {
            return;
        }

        if (userId is null)
        {
            return;
        }

        var users = UsersForSuggestion(dbContext, card, userId)
            .AsAsyncEnumerable();

        await suggestion.AddUsersAsync(users, cancel);
    }


    private async void OnReceiverChange(object? sender, ReceiverEventArgs args)
    {
        if (IsBusy || Suggestion.Card is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            // rerender should trigger at the Yield
            // NavigateTo should trigger the OnParametersSet event
            // and another render will occur after OnParameterSet

            await Task.Yield();

            Nav.NavigateTo(
                Nav.GetUriWithQueryParameter(nameof(ReceiverId), args.ReceiverId),
                replace: true);
        }
        catch (Exception ex)
        {
            Logger.LogError("{Error}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }


    public void ViewDeckDetails()
    {
        if (IsBusy || Suggestion.ToId is not int deckId)
        {
            return;
        }

        IsBusy = true;

        try
        {
            Nav.NavigateTo($"/Decks/Details/{deckId}", forceLoad: true);
        }
        finally
        {
            IsBusy = false;
        }
    }


    public async Task LoadDeckPageAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

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
            IsBusy = false;
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
            {
                Card.Name: string cardName,
                Receiver.Id: string receiverId
            })
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
        if (IsBusy)
        {
            return;
        }

        if (Suggestion
            is not { Card: not null, Receiver: not null })
        {
            return;
        }

        IsBusy = true;

        try
        {
            var token = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            var suggestion = new Suggestion
            {
                Card = Suggestion.Card,
                Receiver = Suggestion.Receiver,
                To = Suggestion.To,
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
            IsBusy = false;
        }
    }



    private readonly struct DeckCursor
    {
        public int PageSize { get; }
        public int TotalDecks { get; private init; }
        public int LoadedDecks { get; private init; }

        public bool HasMore => LoadedDecks < TotalDecks;

        public DeckCursor(int pageSize)
        {
            PageSize = pageSize;
            TotalDecks = 0;
            LoadedDecks = 0;
        }

        public DeckCursor NextPage()
        {
            return this with { LoadedDecks = LoadedDecks + PageSize };
        }

        public DeckCursor WithTotal(int total)
        {
            return this with
            {
                TotalDecks = total,
                LoadedDecks = 0
            };
        }
    }



    private static IQueryable<UserRef> UsersForSuggestion(
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
            .Select(us => us.User)
            .AsNoTracking();
    }


    private static IQueryable<Deck> DecksForSuggest(
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
            .Select(ds => ds.Deck)
            .AsNoTracking();
    }

}
