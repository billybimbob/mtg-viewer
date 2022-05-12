using System;
using System.Collections.Generic;
using System.Linq;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Data.Infrastructure;
using MTGViewer.Data.Projections;
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

    private readonly List<DeckPreview> _deckOptions = new();
    private readonly List<UserPreview> _userOptions = new();

    private PersistingComponentStateSubscription _persistSubscription;

    private bool _isBusy;
    private bool _isInteractive;

    private int _maxDeckCount;

    protected override void OnInitialized()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistSuggestionData);
    }

    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            await LoadSuggestDataAsync();
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

            _cancel.Cancel();
            _cancel.Dispose();
        }

        base.Dispose(disposing);
    }

    private Task PersistSuggestionData()
    {
        ApplicationState.PersistAsJson(nameof(Suggestion.Card), Suggestion.Card);

        // keep an eye on, userOptions are unbounded
        // if there are too many users, message limit will be exceeded
        ApplicationState.PersistAsJson(nameof(_userOptions), _userOptions);

        ApplicationState.PersistAsJson(nameof(_deckOptions), _deckOptions);

        ApplicationState.PersistAsJson(nameof(_maxDeckCount), _maxDeckCount);

        return Task.CompletedTask;
    }

    private async Task LoadSuggestDataAsync()
    {
        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        await LoadCardAsync(dbContext);

        if (Suggestion.Card is null)
        {
            Logger.LogError("Card {CardId} is not valid", CardId);

            Nav.NavigateTo("/Cards/", replace: true);
            return;
        }

        await LoadReceiverAsync(dbContext);

        if (ReceiverId is not null && Suggestion.Receiver is null)
        {
            Logger.LogError("User {ReceiverId} is not valid", ReceiverId);

            Nav.NavigateTo(Nav.GetUriWithQueryParameter(nameof(ReceiverId), null as string), replace: true);
            return;
        }
    }

    private async Task LoadCardAsync(CardDbContext dbContext)
    {
        if (Suggestion.Card?.Id == CardId)
        {
            return;
        }

        if (ApplicationState.TryGetData(nameof(Suggestion.Card), out CardImage? card))
        {
            Suggestion.Card = card;
            return;
        }

        Suggestion.Card = await dbContext.Cards
            .Select(c => new CardImage
            {
                Id = c.Id,
                Name = c.Name,
                ImageUrl = c.ImageUrl
            })
            .SingleOrDefaultAsync(c => c.Id == CardId, _cancel.Token);
    }

    private async Task LoadReceiverAsync(CardDbContext dbContext)
    {
        if (Suggestion.Card?.Name is not string cardName)
        {
            Logger.LogWarning("Tried to load receiver, but is missing card");
            return;
        }

        await LoadUserOptionsAsync(dbContext, cardName);

        Suggestion.Receiver = _userOptions.SingleOrDefault(u => u.Id == ReceiverId);

        if (Suggestion.Receiver?.Id is string userId)
        {
            await LoadReceiverDecksAsync(dbContext, cardName, userId);
        }
    }

    private async Task LoadUserOptionsAsync(CardDbContext dbContext, string cardName)
    {
        if (_userOptions.Any())
        {
            return;
        }

        if (ApplicationState.TryGetData(nameof(_userOptions), out IEnumerable<UserPreview>? users))
        {
            _userOptions.AddRange(users);
            return;
        }

        string? userId = await GetUserIdAsync();

        if (userId is null)
        {
            return;
        }

        var dbUsers = UsersForSuggestionAsync(dbContext, cardName, userId);

        await foreach (var user in dbUsers.WithCancellation(_cancel.Token))
        {
            _userOptions.Add(user);
        }
    }

    private async ValueTask<string?> GetUserIdAsync()
    {
        var authState = await AuthState;

        _cancel.Token.ThrowIfCancellationRequested();

        var userManager = ScopedServices.GetRequiredService<UserManager<CardUser>>();

        string? userId = userManager.GetUserId(authState.User);

        if (userId is null)
        {
            Logger.LogWarning("User is missing");
        }

        return userId;
    }

    private async Task LoadReceiverDecksAsync(CardDbContext dbContext, string cardName, string userId)
    {
        if (_deckOptions.Any())
        {
            Logger.LogWarning("Receiver is already loaded");
            return;
        }

        if (!ApplicationState.TryGetData(nameof(_maxDeckCount), out _maxDeckCount))
        {
            _maxDeckCount = await DecksForSuggest(dbContext, cardName, userId).CountAsync(_cancel.Token);
        }

        if (ApplicationState.TryGetData(nameof(_deckOptions), out IReadOnlyList<DeckPreview>? decks))
        {
            _deckOptions.AddRange(decks);
        }

        await LoadMoreDecksAsync(dbContext);
    }

    #region View Properties

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal bool HasMore => _deckOptions.Count < _maxDeckCount;

    internal bool HasDetails => ToId is not null;

    internal bool IsMissingUsers => this is
    {
        _isBusy: true,
        _userOptions.Count: 0,
        Suggestion.Card: not null
    };

    internal int? ToId
    {
        get => Suggestion.To?.Id;
        set => Suggestion.To = _deckOptions.SingleOrDefault(d => d.Id == value);
    }

    internal IReadOnlyList<DeckPreview> DeckOptions => _deckOptions;

    internal IReadOnlyList<UserPreview> UserOptions => _userOptions;

    #endregion

    internal void ChangeReceiver(ChangeEventArgs args)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        string? receiverId = args.Value?.ToString();

        if (receiverId is null)
        {
            // clear to get an updated listed of user options
            _userOptions.Clear();
            _deckOptions.Clear();
        }

        // triggers on ParameterSet, where IsBusy set to false

        Nav.NavigateTo(Nav.GetUriWithQueryParameter(nameof(ReceiverId), receiverId), replace: true);
    }

    internal void ChangeReceiver(MouseEventArgs args) => ChangeReceiver(new ChangeEventArgs());

    internal void ViewDeckDetails()
    {
        if (_isBusy || ToId is not int deckId)
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
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await LoadMoreDecksAsync(dbContext);
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

    private async Task LoadMoreDecksAsync(CardDbContext dbContext)
    {
        if (HasMore)
        {
            return;
        }

        if (Suggestion is not { Card.Name: string name, Receiver.Id: string id })
        {
            return;
        }

        var decks = DecksForSuggest(dbContext, name, id)
            .Skip(_deckOptions.Count)
            .Take(PageSize.Current)
            .AsAsyncEnumerable()
            .WithCancellation(_cancel.Token);

        await foreach (var deck in decks)
        {
            _deckOptions.Add(deck);
        }
    }

    internal async Task SendSuggestionAsync()
    {
        if (_isBusy || ReceiverId is null)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            var suggestion = new Suggestion
            {
                CardId = CardId,
                ReceiverId = ReceiverId,
                ToId = ToId,
                Comment = Suggestion.Comment
            };

            dbContext.Suggestions.Attach(suggestion);

            await dbContext.SaveChangesAsync(_cancel.Token);

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

    private static IAsyncEnumerable<UserPreview> UsersForSuggestionAsync(
        CardDbContext dbContext,
        string cardName,
        string proposerId)
    {
        var nonProposers = dbContext.Users
            .Where(u => u.Id != proposerId)
            .OrderBy(u => u.Name)
                .ThenBy(u => u.Id);

        var cardSuggests = dbContext.Suggestions
            .Where(s => s.Card.Name == cardName && s.ReceiverId != proposerId);

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
            })
            .AsAsyncEnumerable();
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
