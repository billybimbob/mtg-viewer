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

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;
using MtgViewer.Services;
using MtgViewer.Utils;

namespace MtgViewer.Pages.Transfers;

[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public partial class Suggest : OwningComponentBase
{
    [Parameter]
    public required string CardId { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public string? ReceiverId { get; set; }

    [CascadingParameter]
    public required Task<AuthenticationState> AuthState { get; set; }

    [Inject]
    public required IDbContextFactory<CardDbContext> DbFactory { get; set; }

    [Inject]
    public required NavigationManager Nav { get; set; }

    [Inject]
    public required PageSize PageSize { get; set; }

    [Inject]
    public required PersistentComponentState ApplicationState { get; set; }

    [Inject]
    public required ILogger<Suggest> Logger { get; set; }

    internal SaveResult Result { get; set; }

    internal SuggestionDto Suggestion { get; } = new();

    private readonly CancellationTokenSource _cancel = new();

    private readonly List<PlayerPreview> _playerOptions = new();
    private readonly List<DeckPreview> _deckOptions = new();

    private int _totalPlayers;
    private int _totalDecks;

    private PersistingComponentStateSubscription _persistSubscription;

    private bool _isBusy;
    private bool _isInteractive;

    protected override void OnInitialized() =>
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistSuggestionData);

    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            if (await LoadCardAsync(dbContext) is false)
            {
                Nav.NavigateTo("/Cards/", replace: true);
                return;
            }

            if (await LoadReceiverAsync(dbContext) is false)
            {
                Nav.NavigateTo(
                    Nav.GetUriWithQueryParameter(nameof(ReceiverId), null as string), replace: true);
                return;
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

            _cancel.Cancel();
            _cancel.Dispose();
        }

        base.Dispose(disposing);
    }

    private Task PersistSuggestionData()
    {
        ApplicationState.PersistAsJson(nameof(Suggestion.Card), Suggestion.Card);
        ApplicationState.PersistAsJson(nameof(Suggestion.Receiver), Suggestion.Receiver);

        ApplicationState.PersistAsJson(nameof(_playerOptions), _playerOptions);
        ApplicationState.PersistAsJson(nameof(_deckOptions), _deckOptions);

        ApplicationState.PersistAsJson(nameof(_totalPlayers), _totalPlayers);
        ApplicationState.PersistAsJson(nameof(_totalDecks), _totalDecks);

        return Task.CompletedTask;
    }

    private async Task<bool> LoadCardAsync(CardDbContext dbContext)
    {
        if (Suggestion.Card?.Id == CardId)
        {
            return true;
        }

        if (ApplicationState.TryGetData(nameof(Suggestion.Card), out CardImage? card))
        {
            Suggestion.Card = card;
            return true;
        }

        Suggestion.Card = await dbContext.Cards
            .Select(c => new CardImage
            {
                Id = c.Id,
                Name = c.Name,
                ImageUrl = c.ImageUrl
            })
            .SingleOrDefaultAsync(c => c.Id == CardId, _cancel.Token);

        if (Suggestion.Card is null)
        {
            Logger.LogError("Card {CardId} is not valid", CardId);
            return false;
        }

        return true;
    }

    private async Task<bool> LoadReceiverAsync(CardDbContext dbContext)
    {
        if (Suggestion.Card?.Name is not string cardName)
        {
            Logger.LogError("Suggested card is missing");
            return false;
        }

        if (await GetUserIdAsync() is not string proposerId)
        {
            Logger.LogError("User is missing");
            return false;
        }

        await LoadPlayersAsync(dbContext, cardName, proposerId);

        if (await LoadReceiverAsync(dbContext, cardName, proposerId) is false)
        {
            return false;
        }

        if (Suggestion.Receiver?.Id is string receiverId)
        {
            await LoadDecksAsync(dbContext, cardName, receiverId);
        }

        return true;
    }

    private async Task LoadPlayersAsync(CardDbContext dbContext, string cardName, string proposerId)
    {
        if (ReceiverId is not null)
        {
            return;
        }

        if (!ApplicationState.TryGetData(nameof(_totalPlayers), out _totalPlayers))
        {
            _totalPlayers = await PossibleReceivers(dbContext, cardName, proposerId)
                .CountAsync(_cancel.Token);
        }

        _playerOptions.Clear();

        if (ApplicationState.TryGetData(nameof(_playerOptions), out IReadOnlyList<PlayerPreview>? players))
        {
            _playerOptions.AddRange(players);
            return;
        }

        await LoadMorePlayersAsync(dbContext, cardName, proposerId);
    }

    private async Task<bool> LoadReceiverAsync(CardDbContext dbContext, string cardName, string proposerId)
    {
        if (Suggestion.Receiver?.Id == ReceiverId)
        {
            return true;
        }

        if (ReceiverId is null)
        {
            Suggestion.Receiver = null;
            return true;
        }

        if (proposerId == ReceiverId)
        {
            Logger.LogError("Specified receiver is the same as the current user");
            return false;
        }

        if (ApplicationState.TryGetData(nameof(Suggestion.Receiver), out PlayerPreview? receiver))
        {
            Suggestion.Receiver = receiver;
            return true;
        }

        Suggestion.Receiver = await PossibleReceivers(dbContext, cardName, proposerId)
            .SingleOrDefaultAsync(u => u.Id == ReceiverId, _cancel.Token);

        if (Suggestion.Receiver is null)
        {
            Logger.LogWarning("Cannot find  receiver {ReceiverId}", ReceiverId);
            return false;
        }

        return true;
    }

    private async Task LoadDecksAsync(CardDbContext dbContext, string cardName, string receiverId)
    {
        if (!ApplicationState.TryGetData(nameof(_totalDecks), out _totalDecks))
        {
            _totalDecks = await ReceiverDecks(dbContext, cardName, receiverId)
                .CountAsync(_cancel.Token);
        }

        _deckOptions.Clear();

        if (ApplicationState.TryGetData(nameof(_deckOptions), out IReadOnlyList<DeckPreview>? decks))
        {
            _deckOptions.AddRange(decks);
            return;
        }

        await LoadMoreDecksAsync(dbContext, cardName, receiverId);
    }

    private async ValueTask<string?> GetUserIdAsync()
    {
        var authState = await AuthState;

        _cancel.Token.ThrowIfCancellationRequested();

        var userManager = ScopedServices.GetRequiredService<UserManager<CardUser>>();

        return userManager.GetUserId(authState.User);
    }

    #region View Properties

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal bool AllDecksLoaded => _deckOptions.Count >= _totalDecks;

    internal bool AllPlayersLoaded => _playerOptions.Count >= _totalPlayers;

    internal bool HasDetails => ToId is not null;

    internal bool IsMissingPlayers => this is
    {
        IsLoading: false,
        Suggestion.Card: not null,
        _playerOptions.Count: 0
    };

    internal IReadOnlyList<DeckPreview> DeckOptions => _deckOptions;

    internal IReadOnlyList<PlayerPreview> PlayerOptions => _playerOptions;

    internal int? ToId
    {
        get => Suggestion.To?.Id;
        set => Suggestion.To = _deckOptions.SingleOrDefault(d => d.Id == value);
    }

    #endregion

    internal void ChangeReceiver(string? receiverId)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        // triggers on ParameterSet, where IsBusy set to false

        Nav.NavigateTo(
            Nav.GetUriWithQueryParameter(nameof(ReceiverId), receiverId));
    }

    internal void ChangeReceiver() => ChangeReceiver(null);

    internal void ViewDeckDetails()
    {
        if (_isBusy || ToId is not int deckId)
        {
            return;
        }

        _isBusy = true;

        Nav.NavigateTo($"/Decks/Details/{deckId}", forceLoad: true);
    }

    internal async Task LoadMoreDecksAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (Suggestion is not { Card.Name: string name, Receiver.Id: string id })
        {
            return;
        }

        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await LoadMoreDecksAsync(dbContext, name, id);
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

    private async Task LoadMoreDecksAsync(CardDbContext dbContext, string cardName, string receiverId)
    {
        if (AllDecksLoaded)
        {
            return;
        }

        var decks = ReceiverDecks(dbContext, cardName, receiverId)
            .Skip(_deckOptions.Count)
            .Take(PageSize.Current)
            .AsAsyncEnumerable()
            .WithCancellation(_cancel.Token);

        await foreach (var deck in decks)
        {
            _deckOptions.Add(deck);
        }
    }

    private async Task LoadMorePlayersAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (Suggestion.Card?.Name is not string name)
        {
            return;
        }

        _isBusy = true;

        try
        {
            if (await GetUserIdAsync() is not string id)
            {
                Logger.LogError("User is missing");
                return;
            }

            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await LoadMorePlayersAsync(dbContext, name, id);
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

    private async Task LoadMorePlayersAsync(CardDbContext dbContext, string cardName, string proposerId)
    {
        if (AllPlayersLoaded)
        {
            return;
        }

        var players = PossibleReceivers(dbContext, cardName, proposerId)
            .Skip(_playerOptions.Count)
            .Take(PageSize.Current)
            .AsAsyncEnumerable()
            .WithCancellation(_cancel.Token);

        await foreach (var player in players)
        {
            _playerOptions.Add(player);
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

            Nav.NavigateTo(
                Nav.GetUriWithQueryParameter(nameof(ReceiverId), null as string));
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

    private static IQueryable<DeckPreview> ReceiverDecks(
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

    private static IQueryable<PlayerPreview> PossibleReceivers(
        CardDbContext dbContext,
        string cardName,
        string proposerId)
    {
        var nonProposers = dbContext.Players
            .Where(p => p.Id != proposerId)
            .OrderBy(p => p.Name)
                .ThenBy(p => p.Id);

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
            .Select(us => new PlayerPreview
            {
                Id = us.User.Id,
                Name = us.User.Name
            });
    }

}
