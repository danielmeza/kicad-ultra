using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiCadSharp;
using KiCadUltra.Services;
using KiCadUltra.Services.EasyEda2KiCad;
using KiCadUltra.Services.Interfaces;
using KiCadUltra.Services.Providers;
using KiCadUltra.Services.Providers.Jlcpcb;
using KiCadUltra.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace KiCadUltra.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;

    // The About window logs under its own categories, so it is handed the factory rather than this
    // view model's logger (#121).
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfigService _configService;
    private readonly KiCad _kiCad;
    private readonly IKiCadImportEngine _importEngine;
    private readonly IComponentProviderRegistry _providerRegistry;
    private readonly IPartAggregatorService _aggregatorService;
    private readonly EasyEda2KiCadLocator _easyEda2KiCadLocator;
    private readonly JlcpcbOfficialApiAccess _jlcpcbOfficialApiAccess;

    // Held only to hand to the About window, which says there whether the running KiCad is one this
    // build supports (#138).
    private readonly IKiCadCompatibility _compatibility;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private bool _canImport;

    [ObservableProperty]
    private bool _webViewLoaded;

    [ObservableProperty]
    private ImportType _selectedImportType = ImportType.All;

    public IReadOnlyList<ImportType> ImportTypes { get; } = new[]
    {
        ImportType.Symbol,
        ImportType.Footprint,
        ImportType.Model3D,
        ImportType.All
    };

    [ObservableProperty]
    private IComponentProvider _selectedProvider;

    // The provider last switched to, whose start page the browser was sent to. SelectedProvider
    // can briefly be null while the provider ComboBox refreshes its items; this cannot.
    private IComponentProvider _activeProvider;

    [ObservableProperty]
    private string _webviewUrl = string.Empty;

    // Part Explorer Properties
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnBrowserTab))]
    private int _selectedTabIndex;

    public bool IsOnBrowserTab => SelectedTabIndex == 1;

    public event Action? RequestBrowserBack;
    public event Action? RequestBrowserForward;
    public event Action? RequestBrowserReload;

    [RelayCommand]
    private void BackToExplorer()
    {
        SelectedTabIndex = 0;
    }

    [RelayCommand]
    private void BrowserBack()
    {
        RequestBrowserBack?.Invoke();
    }

    [RelayCommand]
    private void BrowserForward()
    {
        RequestBrowserForward?.Invoke();
    }

    [RelayCommand]
    private void BrowserReload()
    {
        RequestBrowserReload?.Invoke();
    }

    [ObservableProperty]
    private PartSearchResult? _selectedSearchResult;

    public enum SortField { Stock, Price, CadAssets }
    public enum SortOrder { Ascending, Descending }

    [ObservableProperty]
    private SortField _activeSortField = SortField.Stock;

    [ObservableProperty]
    private SortOrder _activeSortOrder = SortOrder.Descending;

    [ObservableProperty]
    private string _stockSortIndicator = "▼";

    [ObservableProperty]
    private string _priceSortIndicator = "";

    [ObservableProperty]
    private string _cadSortIndicator = "";

    [ObservableProperty]
    private string _sortStatusSummary = "Sorted: Stock (High → Low)";

    public ObservableCollection<PartSearchResult> SearchResults { get; } = [];

    /// <summary>
    /// The Search button (#49). Executing it only decides whether the click starts a search: it
    /// yields the normalised query if it does, and nothing if it does not. The search itself is the
    /// pipeline built in the constructor, which turns each query into a stream of results and
    /// switches to the newest one.
    /// </summary>
    /// <remarks>
    /// The execution is deliberately synchronous. ReactiveCommand never runs concurrently with
    /// itself: its CanExecute is <c>canExecute &amp;&amp; !IsExecuting</c>. If the stream were the
    /// execution, the button would stay disabled until the slowest provider answered, and a new
    /// query could not replace the one in flight.
    /// </remarks>
    public ReactiveCommand<Unit, string> SearchPartsCommand { get; }

    // The query of the search that currently owns SearchResults, IsSearching and the status line, or
    // null when no search is running. Only touched on the UI thread, by the search pipeline.
    private string? _activeSearchQuery;

    // The providers the current search left out because they were rate-limiting (#110), for its status
    // line. Only touched on the UI thread, by the search pipeline.
    private readonly List<ProviderRateLimited> _rateLimitedInSearch = [];

    private string? _downloadedFilePath;

    public IReadOnlyList<IComponentProvider> AvailableProviders => _providerRegistry.Providers;

    /// <summary>
    /// Names the enabled providers the Part Explorer does not search, so that their absence from the
    /// results says why: UltraLibrarian is browser-only by design, and SnapEDA (#56) and SamacSys (#57)
    /// have no search yet. Empty when every enabled provider is searched.
    /// </summary>
    public string BrowserOnlyProviders
    {
        get
        {
            var names = AvailableProviders.Where(p => !p.SupportsDirectApi).Select(p => p.DisplayName).ToList();
            return names.Count == 0
                ? string.Empty
                : $"Not searched here, only in the Web Browser tab: {string.Join(", ", names)}.";
        }
    }

    public string DownloadDirectory => _configService.DownloadDirectory;

    public ObservableCollection<string> ImportMessages { get; } = [];

    public MainViewModel(
        ILogger<MainViewModel> logger,
        ILoggerFactory loggerFactory,
        IConfigService configService,
        KiCad kiCad,
        IKiCadImportEngine importEngine,
        IComponentProviderRegistry providerRegistry,
        IPartAggregatorService aggregatorService,
        EasyEda2KiCadLocator easyEda2KiCadLocator,
        JlcpcbOfficialApiAccess jlcpcbOfficialApiAccess,
        IKiCadCompatibility compatibility)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _configService = configService;
        _kiCad = kiCad;
        _importEngine = importEngine;
        _providerRegistry = providerRegistry;
        _aggregatorService = aggregatorService;
        _easyEda2KiCadLocator = easyEda2KiCadLocator;
        _jlcpcbOfficialApiAccess = jlcpcbOfficialApiAccess;
        _compatibility = compatibility;

        _selectedProvider = _providerRegistry.SelectedProvider;
        _activeProvider = _selectedProvider;
        _webviewUrl = SelectedProvider.SearchUrl;

        _configService.EnsureDownloadDirectoryExists();
        _logger.LogInformation("MainViewModel initialized for provider {Provider}. Watching for downloads in {Dir}",
            SelectedProvider.DisplayName, _configService.DownloadDirectory);

        _providerRegistry.RegistryUpdated += () =>
        {
            OnPropertyChanged(nameof(AvailableProviders));
            OnPropertyChanged(nameof(BrowserOnlyProviders));
            OnPropertyChanged(nameof(FindOnUltraLibrarianToolTip));
            FindOnUltraLibrarianCommand.NotifyCanExecuteChanged();
            if (!AvailableProviders.Contains(SelectedProvider))
            {
                SelectedProvider = _providerRegistry.SelectedProvider;
            }

            // Raised when Settings are saved, which is where providers and credentials change.
            UpdateJlcpcbNotice();
        };
        UpdateJlcpcbNotice();

        // Avalonia's dispatcher: UseReactiveUI (Program.BuildAvaloniaApp) sets it during platform setup,
        // before App resolves this view model. Both the command's output and every search's results
        // are observed here, so the pipeline below, and every change it makes to SearchResults, runs
        // on the UI thread.
        IScheduler uiThread = RxSchedulers.MainThreadScheduler;
        SearchPartsCommand = ReactiveCommand.CreateFromObservable(SubmitSearch, outputScheduler: uiThread);
        _ = SearchPartsCommand.ThrownExceptions.Subscribe(ex => _logger.LogError(ex, "Search command failed"));

        // Each query becomes its own stream of updates, and Switch() forwards only the newest one.
        // Subscribing to a new search disposes the previous one, which cancels the token its
        // StreamAllProvidersAsync call was given and with it that search's provider calls. The
        // subscription lives as long as the view model, which the app resolves once.
        //
        // Only System.Reactive operators here. ReactiveUI 23's own extension methods (WhereNotNull,
        // WhenAnyValue, ...) throw from their type initialiser unless ReactiveUI has been initialised
        // through its builder, which in this app only UseReactiveUI does; the search does not need them.
        _ = SearchPartsCommand
            .Where(query => !IsRunning(query))
            .Select(query => SearchProviders(query, uiThread))
            .Switch()
            .Subscribe(ApplySearchUpdate, ex => _logger.LogError(ex, "The Part Explorer search pipeline stopped"));

        // Posted rather than run here, so that it starts once the dispatcher is running and every
        // continuation of the check comes back to the UI thread.
        Dispatcher.UIThread.Post(() => CheckEasyEda2KiCadCommand.Execute(null));
    }

    partial void OnSelectedProviderChanged(IComponentProvider value)
    {
        // A different provider switches, and sends the browser to its start page. The provider
        // ComboBox writes null and then the same provider again whenever AvailableProviders is
        // replaced, which every Settings save does, and that is not a change (#112).
        if (value is null || ReferenceEquals(value, _activeProvider))
        {
            return;
        }

        _activeProvider = value;
        _providerRegistry.SelectedProvider = value;
        WebviewUrl = value.SearchUrl;
        StatusMessage = $"Active provider: {value.DisplayName}";
        _logger.LogInformation("Switched component provider to {Provider} ({Url})", value.DisplayName, value.SearchUrl);
    }

    public void SetWebViewLoaded(bool isLoaded)
    {
        WebViewLoaded = isLoaded;
    }

    // The Search button's execution: the query to search for, or nothing when the click starts no search.
    private IObservable<string> SubmitSearch()
    {
        var query = SearchQueryNormalizer.Normalize(SearchQuery);
        if (query.Length == 0)
        {
            StatusMessage = "Please enter a part number or keyword to search.";
            return Observable.Empty<string>();
        }

        return Observable.Return(query);
    }

    // A second click on the query already running would only cancel it and ask every provider again.
    // Checked in the pipeline rather than in SubmitSearch, so that it sees the state left by every
    // earlier click's search even when the command's output is delivered later than the click.
    private bool IsRunning(string query) =>
        _activeSearchQuery is not null && string.Equals(query, _activeSearchQuery, StringComparison.OrdinalIgnoreCase);

    // One search, as the updates the UI thread applies in order: SearchStarted, a PartFound per part as
    // its provider answers or a ProviderLeftOut for a provider that was rate-limiting (#110), then
    // SearchCompleted or SearchFailed.
    //
    // Why a superseded search's result cannot land in the new list: the list is cleared by
    // SearchStarted, an update in the same ordered stream as the results, emitted synchronously when
    // Switch() subscribes to this search - the moment the previous search stops being current - and
    // not as a side effect of the click. The results are moved to the UI thread before Switch(), so
    // the switch and every update happen in one order on one thread. A result the previous search had
    // already queued on the UI thread then either reaches Switch() before the new query does, and
    // SearchStarted clears it with the rest, or after, and is dropped: the switch has disposed that
    // search's subscription, and Switch() forwards only the search it is subscribed to. Clearing the
    // list at the click instead would let that queued result land after the clear.
    private IObservable<SearchUpdate> SearchProviders(string query, IScheduler uiThread) =>
        _aggregatorService.StreamAllProvidersAsync(query)
            // A superseded search is cancelled by Switch() disposing it, which ToObservable never
            // reports. Any other cancellation stopped this search early, so it fails rather than
            // completing: "Found N results" would claim a finished search. ApplySearchUpdate reports it
            // as cancelled, not as an error.
            .ToObservable(whenCancelled: AsyncStreamCancellation.Error)
            .SelectMany(outcome => ToSearchUpdates(query, outcome))
            .Append(new SearchCompleted(query))
            // A provider that fails is already left out by the aggregator, so an error here means the
            // search as a whole failed. It must not look like "no results".
            .Catch((Exception ex) => Observable.Return<SearchUpdate>(new SearchFailed(query, ex)))
            .ObserveOn(uiThread)
            .StartWith(new SearchStarted(query));

    private static IEnumerable<SearchUpdate> ToSearchUpdates(string query, ProviderSearchOutcome outcome) => outcome switch
    {
        ProviderResults answer => answer.Parts.Select<PartSearchResult, SearchUpdate>(part => new PartFound(query, part)),
        ProviderRateLimited rateLimited => [new ProviderLeftOut(query, rateLimited)],
        _ => throw new System.Diagnostics.UnreachableException($"Unknown provider outcome {outcome.GetType().Name}"),
    };

    // Runs on the UI thread, and only for the current search.
    private void ApplySearchUpdate(SearchUpdate update)
    {
        switch (update)
        {
            case SearchStarted:
                _activeSearchQuery = update.Query;
                _rateLimitedInSearch.Clear();
                IsSearching = true;
                SearchResults.Clear();
                StatusMessage = $"Searching all component providers for '{update.Query}'...";
                break;

            case PartFound found:
                InsertSorted(found.Part);
                StatusMessage = $"Searching all component providers for '{update.Query}'... {SearchResults.Count} result(s) so far.";
                break;

            case ProviderLeftOut leftOut:
                _rateLimitedInSearch.Add(leftOut.RateLimited);
                if (leftOut.RateLimited.ProviderId == EasyEdaProvider.ProviderId)
                {
                    _jlcpcbRateLimit = (update.Query, leftOut.RateLimited);
                    UpdateJlcpcbNotice();
                }

                break;

            case SearchCompleted:
                // A provider that was rate-limiting is named, so that its missing results do not read as
                // "no matches" (#110).
                var leftOutNote = _rateLimitedInSearch.Count == 0
                    ? string.Empty
                    : $" Left out: {string.Join("; ", _rateLimitedInSearch.Select(l => $"{l.ProviderName} ({l.Reason})"))}.";
                StatusMessage = $"Found {SearchResults.Count} results across providers for '{update.Query}'.{leftOutNote} ({SortStatusSummary})";
                _logger.LogInformation("Aggregated search completed for query: {Query}, found: {Count}", update.Query, SearchResults.Count);
                if (_jlcpcbRateLimit is not null && !_rateLimitedInSearch.Any(l => l.ProviderId == EasyEdaProvider.ProviderId))
                {
                    // EasyEDA / LCSC was not turned down this time, so the notice about it no longer applies.
                    _jlcpcbRateLimit = null;
                }

                // The search itself is where JLCPCB refuses this application the official API (#126),
                // so the notice is worked out again whatever the search found.
                UpdateJlcpcbNotice();
                EndSearch();
                break;

            case SearchFailed { Error: OperationCanceledException }:
                _logger.LogInformation("Search for {Query} was cancelled before it finished", update.Query);
                StatusMessage = $"Search for '{update.Query}' was cancelled. {SearchResults.Count} result(s) found before it stopped.";
                EndSearch();
                break;

            case SearchFailed failed:
                _logger.LogError(failed.Error, "Error searching components across providers");
                StatusMessage = $"Search error: {failed.Error.Message}";
                EndSearch();
                break;

            default:
                throw new System.Diagnostics.UnreachableException($"Unknown search update {update.GetType().Name}");
        }
    }

    private void EndSearch()
    {
        _activeSearchQuery = null;
        IsSearching = false;
    }

    private abstract record SearchUpdate(string Query);

    private sealed record SearchStarted(string Query) : SearchUpdate(Query);

    private sealed record PartFound(string Query, PartSearchResult Part) : SearchUpdate(Query);

    private sealed record ProviderLeftOut(string Query, ProviderRateLimited RateLimited) : SearchUpdate(Query);

    private sealed record SearchCompleted(string Query) : SearchUpdate(Query);

    private sealed record SearchFailed(string Query, Exception Error) : SearchUpdate(Query);

    [RelayCommand]
    private void Sort(string? column)
    {
        if (string.IsNullOrEmpty(column)) return;

        if (string.Equals(column, "Stock", StringComparison.OrdinalIgnoreCase))
        {
            if (ActiveSortField == SortField.Stock)
            {
                ActiveSortOrder = ActiveSortOrder == SortOrder.Descending
                    ? SortOrder.Ascending
                    : SortOrder.Descending;
            }
            else
            {
                ActiveSortField = SortField.Stock;
                ActiveSortOrder = SortOrder.Descending;
            }
        }
        else if (string.Equals(column, "Price", StringComparison.OrdinalIgnoreCase))
        {
            if (ActiveSortField == SortField.Price)
            {
                ActiveSortOrder = ActiveSortOrder == SortOrder.Ascending
                    ? SortOrder.Descending
                    : SortOrder.Ascending;
            }
            else
            {
                ActiveSortField = SortField.Price;
                ActiveSortOrder = SortOrder.Ascending;
            }
        }
        else if (string.Equals(column, "CAD", StringComparison.OrdinalIgnoreCase))
        {
            if (ActiveSortField == SortField.CadAssets)
            {
                ActiveSortOrder = ActiveSortOrder == SortOrder.Descending
                    ? SortOrder.Ascending
                    : SortOrder.Descending;
            }
            else
            {
                ActiveSortField = SortField.CadAssets;
                ActiveSortOrder = SortOrder.Descending;
            }
        }

        UpdateSortVisuals();
        ApplySort();
    }

    private void UpdateSortVisuals()
    {
        var arrow = ActiveSortOrder == SortOrder.Ascending ? "▲" : "▼";
        StockSortIndicator = ActiveSortField == SortField.Stock ? arrow : "";
        PriceSortIndicator = ActiveSortField == SortField.Price ? arrow : "";
        CadSortIndicator = ActiveSortField == SortField.CadAssets ? arrow : "";

        var fieldName = ActiveSortField switch
        {
            SortField.Stock => "Stock",
            SortField.Price => "Price",
            SortField.CadAssets => "CAD Assets",
            _ => ""
        };
        var direction = ActiveSortOrder == SortOrder.Ascending ? "Low → High" : "High → Low";
        SortStatusSummary = $"Sorted: {fieldName} ({direction})";
    }

    private void ApplySort()
    {
        if (SearchResults.Count <= 1) return;

        // Order is a stable sort, so ties keep their current order, as they do in InsertSorted.
        var sorted = SearchResults.Order(Comparer<PartSearchResult>.Create(GetSortComparison())).ToList();

        SearchResults.Clear();
        foreach (PartSearchResult item in sorted)
        {
            SearchResults.Add(item);
        }
    }

    // Results stream in one provider at a time, so each one is placed where the active sort puts it
    // instead of re-sorting the whole list: rows already on screen stay put and the selection
    // survives. It goes after every row it ties with, which keeps ties in arrival order.
    private void InsertSorted(PartSearchResult part)
    {
        Comparison<PartSearchResult> compare = GetSortComparison();
        var index = SearchResults.Count;
        while (index > 0 && compare(SearchResults[index - 1], part) > 0)
        {
            index--;
        }

        SearchResults.Insert(index, part);
    }

    // The orders ApplySort has always produced. Rows without a price or a stock figure sort last in
    // either direction; CAD sorts by asset score, then by stock.
    private Comparison<PartSearchResult> GetSortComparison()
    {
        var descending = ActiveSortOrder == SortOrder.Descending;
        return ActiveSortField switch
        {
            SortField.Price => (a, b) => CompareMissingLast(a.BestPrice, b.BestPrice, descending),
            SortField.Stock => (a, b) => CompareMissingLast(a.Stock, b.Stock, descending),
            SortField.CadAssets => (a, b) => CompareCadAssets(a, b, descending),
            _ => static (_, _) => 0,
        };
    }

    private static int CompareCadAssets(PartSearchResult a, PartSearchResult b, bool descending)
    {
        var byScore = GetCadScore(a).CompareTo(GetCadScore(b));
        var result = byScore != 0 ? byScore : (a.Stock ?? 0).CompareTo(b.Stock ?? 0);
        return descending ? -result : result;
    }

    private static int CompareMissingLast<T>(T? a, T? b, bool descending) where T : struct, IComparable<T>
    {
        if (a is null)
        {
            return b is null ? 0 : 1;
        }

        if (b is null)
        {
            return -1;
        }

        var result = a.Value.CompareTo(b.Value);
        return descending ? -result : result;
    }

    // Assets the provider reports come first, weighted 3D model, footprint, symbol. Among rows that
    // report the same ones, an asset nobody reported on ranks above one reported missing (#48): the
    // part may still have it. Unknown never counts as available.
    private static int GetCadScore(PartSearchResult part)
    {
        var reported = 0;
        if (part.Has3DModel == CadAvailability.Available) reported += 4;
        if (part.HasFootprint == CadAvailability.Available) reported += 2;
        if (part.HasSymbol == CadAvailability.Available) reported += 1;

        var unknown = 0;
        if (part.Has3DModel == CadAvailability.Unknown) unknown++;
        if (part.HasFootprint == CadAvailability.Unknown) unknown++;
        if (part.HasSymbol == CadAvailability.Unknown) unknown++;

        // unknown is at most 3, so it only breaks ties between equal reported scores.
        return (reported * 4) + unknown;
    }

    [RelayCommand]
    private void OpenInWebBrowser(PartSearchResult? part)
    {
        if (part == null)
        {
            return;
        }

        // Find provider in registry and switch to it
        IComponentProvider? provider = _providerRegistry.GetProvider(part.ProviderId);
        if (provider != null)
        {
            SelectedProvider = provider;
        }

        if (!string.IsNullOrEmpty(part.DatasheetUrl))
        {
            WebviewUrl = part.DatasheetUrl;
        }
        else if (provider != null)
        {
            WebviewUrl = provider.SearchUrl;
        }

        // Switch to the Web Browser tab
        SelectedTabIndex = 1;
        StatusMessage = $"Navigating to {part.PartNumber} on {part.ProviderName}...";
    }

    // EasyEDA / LCSC import through the user-installed easyeda2kicad (#76). Ordinary CommunityToolkit
    // commands: an import is one awaited operation, not a stream like the search above.

    /// <summary>True when the last check found a working easyeda2kicad.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportPartCommand))]
    [NotifyPropertyChangedFor(nameof(ImportPartToolTip))]
    private bool _isEasyEda2KiCadAvailable;

    /// <summary>
    /// True when the last check found no easyeda2kicad, which shows the install instructions. False
    /// until a check has finished, so they do not flash up at start-up.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportPartToolTip))]
    private bool _isEasyEda2KiCadMissing;

    /// <summary>A row's Import button tip, which also says why the button is disabled.</summary>
    public string ImportPartToolTip =>
        IsEasyEda2KiCadAvailable ? "Convert this part with easyeda2kicad and add it to your KiCad libraries"
        : IsEasyEda2KiCadMissing ? "Unavailable: easyeda2kicad is not installed. The notice above says how to install it."
        : "Looking for easyeda2kicad...";

    /// <summary>True while a part is being imported with easyeda2kicad; shows the Cancel button.</summary>
    [ObservableProperty]
    private bool _isImportingPart;

    /// <summary>The command that installs easyeda2kicad on this system.</summary>
    public string EasyEda2KiCadInstallCommand => EasyEda2KiCadLocator.InstallHelp.Command;

    public string EasyEda2KiCadInstallNote => EasyEda2KiCadLocator.InstallHelp.Note;

    /// <summary>
    /// Looks for easyeda2kicad again: at start-up, after Settings are saved, and from the "Check again"
    /// button once the user has installed it.
    /// </summary>
    [RelayCommand]
    private async Task CheckEasyEda2KiCad()
    {
        EasyEda2KiCadDetection detection;
        try
        {
            detection = await _easyEda2KiCadLocator.LocateAsync(_configService.EasyEda2KiCadPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking for easyeda2kicad");
            IsEasyEda2KiCadAvailable = false;
            IsEasyEda2KiCadMissing = true;
            ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Could not look for easyeda2kicad: {ex.Message}");
            return;
        }

        IsEasyEda2KiCadAvailable = detection.IsAvailable;
        IsEasyEda2KiCadMissing = !detection.IsAvailable;
        var status = detection.Command is { } command
            ? $"EasyEDA / LCSC import: using {command.Description}."
            : "EasyEDA / LCSC parts cannot be imported: easyeda2kicad was not found.";

        ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] {status}");
        if (!detection.IsAvailable)
        {
            foreach (var attempt in detection.Attempts)
            {
                ImportMessages.Add($"  - {attempt}");
            }

            ImportMessages.Add($"  - Install it with: {EasyEda2KiCadInstallCommand}");
        }
    }

    /// <summary>
    /// A row's Import button: converts the part with easyeda2kicad and registers the library. Enabled
    /// for results that carry an LCSC part number, once easyeda2kicad has been found.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImportPart), IncludeCancelCommand = true)]
    private async Task ImportPart(PartSearchResult? part, CancellationToken cancellationToken)
    {
        if (part?.LcscPartNumber is not { } lcscPartNumber)
        {
            return;
        }

        // easyeda2kicad converts EasyEDA's data whichever search found the LCSC code, so the part goes into
        // the EasyEDA library, also when it is an Octopart result (#47).
        IComponentProvider? provider = _providerRegistry.AllProviders.OfType<EasyEdaProvider>().FirstOrDefault()
            ?? _providerRegistry.GetProvider(part.ProviderId);
        if (provider is null)
        {
            StatusMessage = $"Cannot import {part.PartNumber}: {part.ProviderName} is not available.";
            return;
        }

        var label = $"{part.PartNumber} ({lcscPartNumber})";
        StatusMessage = $"Importing {label} with easyeda2kicad...";
        ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] [{part.ProviderName}] Importing {label} with easyeda2kicad...");
        IsImportingPart = true;
        IsProgressIndeterminate = true;
        IsProgressVisible = true;

        try
        {
            ImportResult result = await _importEngine.ImportLcscPartAsync(
                provider, lcscPartNumber, SelectedImportType, _configService.GetImportOptions(), cancellationToken);

            var outcome = DescribeOutcome(result);
            StatusMessage = $"Import of {label} {outcome}";
            ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Import of {label} {outcome}");
            foreach (var detail in result.Details)
            {
                ImportMessages.Add($"  - {detail}");
            }

            ShowImportedAssets(part, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing {Part}", label);
            StatusMessage = $"Error: {ex.Message}";
            ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}");
        }
        finally
        {
            IsImportingPart = false;
            IsProgressIndeterminate = false;
            IsProgressVisible = false;
        }
    }

    private bool CanImportPart(PartSearchResult? part) => IsEasyEda2KiCadAvailable && part?.LcscPartNumber is not null;

    // Any result with a manufacturer part number, from whichever provider, can also be imported the way
    // the Web Browser tab always could (#47): open Ultra Librarian's search for it there, and the KiCad
    // model the user downloads is intercepted and imported like any other Ultra Librarian download.

    /// <summary>
    /// UltraLibrarian's provider, or <c>null</c> when it is turned off in Settings. Its downloads are
    /// the ones Find on Ultra Librarian imports.
    /// </summary>
    private UltraLibrarianProvider? UltraLibrarian => AvailableProviders.OfType<UltraLibrarianProvider>().FirstOrDefault();

    /// <summary>A row's Find on Ultra Librarian button tip, which also says why the button is disabled.</summary>
    public string FindOnUltraLibrarianToolTip => UltraLibrarian is null
        ? "Unavailable: UltraLibrarian is turned off in Settings."
        : "Search Ultra Librarian for this part number in the Web Browser tab. Download its KiCad model there to import it.";

    /// <summary>
    /// A row's Find on Ultra Librarian button: selects the UltraLibrarian provider and opens its search
    /// for the part number in the Web Browser tab. The import itself is the browser's download flow
    /// (<see cref="LibraryDownloaded"/>).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFindOnUltraLibrarian))]
    private void FindOnUltraLibrarian(PartSearchResult? part)
    {
        if (part is null || string.IsNullOrWhiteSpace(part.PartNumber) || UltraLibrarian is not { } ultraLibrarian)
        {
            return;
        }

        var partNumber = part.PartNumber.Trim();
        var searchUrl = UltraLibrarianProvider.PartSearchUrl(partNumber);
        _logger.LogInformation("Finding {Part} ({Provider} result) on Ultra Librarian: {Url}", partNumber, part.ProviderName, searchUrl);

        // A finished download is imported with the selected provider, so it must be UltraLibrarian's by
        // then. Switching to it also sends the browser to its start page, which the search replaces at once.
        SelectedProvider = ultraLibrarian;
        WebviewUrl = searchUrl;
        SelectedTabIndex = 1;
        StatusMessage = $"Searching Ultra Librarian for {partNumber}. Download its KiCad model there to import it.";
        ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] [{ultraLibrarian.DisplayName}] Searching for {partNumber} ({part.ProviderName} result). Download its KiCad model to import it.");
    }

    private bool CanFindOnUltraLibrarian(PartSearchResult? part) =>
        UltraLibrarian is not null && !string.IsNullOrWhiteSpace(part?.PartNumber);

    // Which JLCPCB source EasyEDA / LCSC search uses (#51, #52). A notice that blocks nothing, shown
    // while that provider is enabled, because every state still sends at least some searches to the
    // unofficial endpoint: it says so, links to JLCPCB's API guide and says where the credentials go.

    /// <summary>True while the JLCPCB source notice is shown.</summary>
    [ObservableProperty]
    private bool _isJlcpcbNoticeVisible;

    [ObservableProperty]
    private string _jlcpcbNoticeTitle = string.Empty;

    [ObservableProperty]
    private string _jlcpcbNoticeText = string.Empty;

    [ObservableProperty]
    private string _jlcpcbNoticeHideToolTip = string.Empty;

    // What the notice said when it was hidden: the credential state, and what JLCPCB has refused those
    // credentials (#126). Hiding lasts until the app closes or one of those changes, so neither a
    // notice about new credentials nor one about a refusal is swallowed by an old dismissal.
    private (JlcpcbApiCredentialState State, JlcpcbApiRefusal? Refusal)? _hiddenJlcpcbNotice;

    // The last search's EasyEDA / LCSC refusal (#110), with that search's query, while it applies: until
    // a search completes without one, or the notice is hidden.
    private (string Query, ProviderRateLimited RateLimited)? _jlcpcbRateLimit;

    /// <summary>JLCPCB's guide to applying for API access.</summary>
    public Uri JlcpcbApiGuideUri { get; } = new(JlcpcbApiCredentials.ApiGuideUrl);

    [RelayCommand]
    private void HideJlcpcbNotice()
    {
        _hiddenJlcpcbNotice = (JlcpcbApiCredentials.GetState(_configService), _jlcpcbOfficialApiAccess.GetRefusal(_configService));
        _jlcpcbRateLimit = null;
        IsJlcpcbNoticeVisible = false;
    }

    private void UpdateJlcpcbNotice()
    {
        var easyEdaEnabled = _providerRegistry.Providers.Any(p => p.Id == EasyEdaProvider.ProviderId);

        // A refusal takes the notice over, even where it was hidden: results are missing from the search
        // just made, and a failure must not look like "no results" (#110).
        if (_jlcpcbRateLimit is { } rateLimit && easyEdaEnabled)
        {
            JlcpcbNoticeTitle = rateLimit.RateLimited.Reason;
            JlcpcbNoticeText =
                $"The search for '{rateLimit.Query}' has no EasyEDA / LCSC results because JLCPCB turned a request down as too frequent, not because nothing matched. " +
                $"The app leaves JLCPCB alone until {rateLimit.RateLimited.RetryAt.ToLocalTime():T}; search again after that. The other providers are not affected.";
            JlcpcbNoticeHideToolTip = "Hide this notice until a later search is turned down too";
            IsJlcpcbNoticeVisible = true;
            return;
        }

        JlcpcbNoticeHideToolTip = "Hide this notice until the app restarts or the JLCPCB API settings change";
        JlcpcbApiCredentialState state = JlcpcbApiCredentials.GetState(_configService);

        // JLCPCB refusing this application the official API is the thing to say while it stands (#126):
        // the credentials are stored, and every EasyEDA / LCSC search uses the website endpoint anyway.
        JlcpcbApiRefusal? refusal = _jlcpcbOfficialApiAccess.GetRefusal(_configService);
        (JlcpcbNoticeTitle, JlcpcbNoticeText) = refusal is { } denied ? Describe(denied) : DescribeCredentials(state);

        IsJlcpcbNoticeVisible = (state, refusal) != _hiddenJlcpcbNotice && easyEdaEnabled;
    }

    // What JLCPCB refused, and what the app does about it. It names what the user can act on - the
    // Parts permission, or the three values in Settings - and never a credential value itself.
    private static (string Title, string Text) Describe(JlcpcbApiRefusal refusal) => refusal switch
    {
        JlcpcbApiRefusal.NotApproved => (
            "JLCPCB has not approved this application for its Components API",
            "Your JLCPCB API credentials are stored, but JLCPCB refused the call. Its API access is granted per service, and the Components API needs " +
            "your application's Parts permission approved; an IP whitelist on the application can refuse it the same way. Until that is sorted out, LCSC " +
            "part numbers are looked up through an internal endpoint of JLCPCB's website, which is not a published API and can change or stop working at " +
            "any time without notice. The app stops calling the official API until the credentials change in Settings > Component Providers."),
        JlcpcbApiRefusal.CredentialsRejected => (
            "JLCPCB did not accept these API credentials",
            "JLCPCB rejected the stored App ID, Access Key or Secret Key, so LCSC part numbers cannot be looked up through its official Components API. " +
            "Check all three against your application in JLCPCB's API platform, in Settings > Component Providers. Until they change, part numbers are " +
            "looked up through an internal endpoint of JLCPCB's website, which is not a published API and can change or stop working at any time without notice."),
        _ => throw new System.Diagnostics.UnreachableException($"Unknown JLCPCB refusal {refusal}"),
    };

    private (string Title, string Text) DescribeCredentials(JlcpcbApiCredentialState state) => state switch
    {
        JlcpcbApiCredentialState.None => (
            "EasyEDA / LCSC search uses an unofficial source",
            "Parts are looked up through an internal endpoint of JLCPCB's website. It is not a published API and can change or stop working at any time without notice. " +
            "For a supported source, apply for access to JLCPCB's official API (guide below), then enter your App ID, Access Key and Secret Key in Settings > Component Providers."),
        JlcpcbApiCredentialState.Incomplete => (
            "JLCPCB API credentials are incomplete",
            $"Missing: {string.Join(", ", JlcpcbApiCredentials.GetMissing(_configService))}. Until all three are entered in Settings > Component Providers, " +
            "EasyEDA / LCSC search uses an unofficial JLCPCB website endpoint, which can change or stop working at any time without notice."),
        JlcpcbApiCredentialState.Complete => (
            "EasyEDA / LCSC keyword searches use an unofficial source",
            "LCSC part numbers such as C2040 are looked up through JLCPCB's official API with your credentials, if JLCPCB has approved this application's " +
            "Parts permission. That API has no keyword search, so other searches use an internal endpoint of JLCPCB's website, which can change or stop " +
            "working at any time without notice."),
        _ => throw new System.Diagnostics.UnreachableException($"Unknown JLCPCB credential state {state}"),
    };

    // Search cannot tell whether an EasyEDA part has a symbol, footprint or 3D model, but an import
    // shows it (#48): an asset easyeda2kicad produced for the part exists, whichever search found its
    // LCSC code (#47). Only those change, to Available. One it did not produce keeps its flag: the
    // import messages say why, and a failed step does not show that the part has none. The row keeps
    // its place in the list, as rows do while results stream in; the next sort puts it where it belongs.
    private void ShowImportedAssets(PartSearchResult part, ImportResult result)
    {
        PartSearchResult imported = part with
        {
            HasSymbol = result.SymbolImportSuccess ? CadAvailability.Available : part.HasSymbol,
            HasFootprint = result.FootprintImportSuccess ? CadAvailability.Available : part.HasFootprint,
            Has3DModel = result.Model3DImportSuccess ? CadAvailability.Available : part.Has3DModel,
        };

        // A search started since may have replaced the list; then there is no row to update.
        var index = SearchResults.IndexOf(part);
        if (imported == part || index < 0)
        {
            return;
        }

        var wasSelected = SelectedSearchResult == part;
        SearchResults[index] = imported;
        if (wasSelected)
        {
            SelectedSearchResult = imported;
        }
    }

    public void LibraryDownloaded(string filePath)
    {
        if (!SelectedProvider.CanHandleDownload(filePath) && !filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IsProgressVisible = false;
        _downloadedFilePath = filePath;
        var fileName = Path.GetFileName(filePath);
        StatusMessage = $"Downloaded ({SelectedProvider.DisplayName}): {fileName}";
        CanImport = true;

        ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] [{SelectedProvider.DisplayName}] Downloaded: {fileName}");
        _logger.LogInformation("Detected download for {Provider}: {File}", SelectedProvider.DisplayName, fileName);

        if (_configService.AutoImportWhenDownloaded)
        {
            _logger.LogInformation("Auto-import enabled. Initiating import...");
            ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Starting auto-import...");
            _ = ImportComponent();
        }
        else
        {
            ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Ready to import. Click 'Import Component' to proceed.");
        }
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        try
        {
            _logger.LogInformation("Opening settings dialog");

            IServiceProvider? serviceProvider = (Avalonia.Application.Current as App)?._serviceProvider;
            SettingsWindow settingsWindow;

            if (serviceProvider != null)
            {
                SettingsViewModel viewModel = serviceProvider.GetRequiredService<SettingsViewModel>();
                settingsWindow = new SettingsWindow(viewModel, _loggerFactory);
            }
            else
            {
                settingsWindow = new SettingsWindow();
            }

            MainWindow owner = App.MainWindow
                ?? throw new InvalidOperationException("Cannot open settings dialog before main window exists.");

            var result = await settingsWindow.ShowDialog<bool>(owner);
            if (result)
            {
                _logger.LogInformation("Settings saved successfully.");

                // The easyeda2kicad path may have changed.
                await CheckEasyEda2KiCadCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening settings dialog");
        }
    }

    [RelayCommand]
    private async Task ImportComponent()
    {
        if (string.IsNullOrEmpty(_downloadedFilePath) || !File.Exists(_downloadedFilePath))
        {
            StatusMessage = "No component package available to import.";
            return;
        }

        try
        {
            IsProgressVisible = true;
            ProgressValue = 0;
            StatusMessage = $"Importing component from {SelectedProvider.DisplayName}...";
            CanImport = false;

            var progressTimer = new System.Timers.Timer(100);
            progressTimer.Elapsed += (s, e) =>
            {
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ProgressValue = (ProgressValue + 1) % 100;
                });
            };
            progressTimer.Start();

            ImportOptions options = _configService.GetImportOptions();
            ImportResult result = await _importEngine.ImportAsync(SelectedProvider, _downloadedFilePath, SelectedImportType, options);

            progressTimer.Stop();

            var outcome = DescribeOutcome(result);
            StatusMessage = $"Import from {SelectedProvider.DisplayName} {outcome}";
            ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Import from {SelectedProvider.DisplayName} {outcome}");
            foreach (var detail in result.Details)
            {
                ImportMessages.Add($"  - {detail}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing component");
            StatusMessage = $"Error: {ex.Message}";
            ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}");
        }
        finally
        {
            IsProgressVisible = false;
            ProgressValue = 0;
        }
    }

    /// <summary>
    /// How an import ended, for the status line and the import log (#102). A partial import names the
    /// requested steps that failed; the details logged under it say why.
    /// </summary>
    private static string DescribeOutcome(ImportResult result) => result.Outcome switch
    {
        ImportOutcome.Succeeded => "succeeded",
        ImportOutcome.PartiallySucceeded => $"partially succeeded: {DescribeSteps(result.FailedSteps)} failed",
        ImportOutcome.Failed => "failed",
        ImportOutcome.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null),
    };

    // At most two: a partial import has at least one step that worked.
    private static string DescribeSteps(ImportType steps) => string.Join(" and ", new[]
    {
        steps.HasFlag(ImportType.Symbol) ? "symbol" : null,
        steps.HasFlag(ImportType.Footprint) ? "footprint" : null,
        steps.HasFlag(ImportType.Model3D) ? "3D model" : null,
    }.OfType<string>());

    [RelayCommand]
    private void OpenDownloadsFolder()
    {
        try
        {
            if (Directory.Exists(_configService.DownloadDirectory))
            {
                if (OperatingSystem.IsWindows())
                {
                    _ = System.Diagnostics.Process.Start("explorer.exe", _configService.DownloadDirectory);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    _ = System.Diagnostics.Process.Start("open", _configService.DownloadDirectory);
                }
                else if (OperatingSystem.IsLinux())
                {
                    _ = System.Diagnostics.Process.Start("xdg-open", _configService.DownloadDirectory);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening downloads folder");
        }
    }

    [RelayCommand]
    private void ShowAbout()
    {
        try
        {
            _logger.LogInformation("Showing about dialog");
            var aboutWindow = new AboutWindow(_loggerFactory, _kiCad, _compatibility);

            MainWindow aboutOwner = App.MainWindow
                ?? throw new InvalidOperationException("Cannot open about dialog before main window exists.");
            _ = aboutWindow.ShowDialog(aboutOwner);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing about dialog");
        }
    }

    internal void DownloadCancelled(string fullPath)
    {
        StatusMessage = $"Download canceled: {Path.GetFileName(fullPath)}";
        IsProgressVisible = false;
    }

    // A browser download the download handler would not let through. The reason is the handler's own
    // short text and never carries the server-supplied file name.
    internal void DownloadRefused(string reason)
    {
        _logger.LogWarning("Browser download refused: {Reason}", reason);
        StatusMessage = $"Download refused: {reason}.";
        ImportMessages.Add($"[{DateTime.Now:HH:mm:ss}] Download refused: {reason}.");
        IsProgressVisible = false;
    }

    internal void ReportDownloadProgressChanged(string fullPath, long receivedBytes, long totalBytes, int percentComplete)
    {
        StatusMessage =
            $"Downloading: {Path.GetFileName(fullPath)} ({percentComplete}%, " +
            $"{receivedBytes:N0}/{totalBytes:N0} bytes)...";
        ProgressValue = percentComplete;
    }

    internal void DownloadStarted(string filePath)
    {
        StatusMessage = $"Starting download: {Path.GetFileName(filePath)}";
        IsProgressVisible = true;
    }
}
