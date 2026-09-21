using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using KiCadSharp;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using UltraLibrarianImporter.UI.Services;
using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.ViewModels;

namespace UltraLibrarianImporter.UI.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly ILogger<SettingsWindow> _logger;
    private readonly TaskCompletionSource<bool> _resultCompletionSource;

    /// <summary>
    /// Fallback constructor used when no dependency-injection container is available (and by the
    /// Avalonia XAML designer).
    /// </summary>
    /// <remarks>
    /// The previous body only created the completion source, leaving <c>_viewModel</c> and
    /// <c>_logger</c> null: the window opened, but the first Browse or Save raised a
    /// NullReferenceException. It now chains to the real constructor with default settings and a
    /// no-op logger, so the fallback window is genuinely usable instead of merely constructible.
    /// </remarks>
    public SettingsWindow()
        : this(new ConfigService(NullLogger<ConfigService>.Instance),
               NullLogger<SettingsWindow>.Instance,
               DefaultSettingsMonitor())
    {
    }

    /// <summary>
    /// Builds an <see cref="IOptionsMonitor{TOptions}"/> carrying default
    /// <see cref="KiCadClientSettings"/>, for the fallback path above.
    /// </summary>
    private static IOptionsMonitor<KiCadClientSettings> DefaultSettingsMonitor() =>
        new ServiceCollection()
            .AddOptions()
            .Configure<KiCadClientSettings>(_ => { })
            .BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<KiCadClientSettings>>();

    public SettingsWindow(IConfigService configService, ILogger logger, IOptionsMonitor<KiCadClientSettings> kicadSettings)
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif

        // Set up the task completion source for the dialog result
        _resultCompletionSource = new TaskCompletionSource<bool>();

        // Convert the generic logger to a typed logger
        _logger = logger as ILogger<SettingsWindow> ??
                 LoggerFactory.Create(builder => builder.AddConsole())
                 .CreateLogger<SettingsWindow>();

        // Create the view model with dependencies
        _viewModel = new SettingsViewModel(
            configService,
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SettingsViewModel>(),
            kicadSettings);

        DataContext = _viewModel;

        // Subscribe to the browse folder event
        _viewModel.BrowseForFolderRequested += OnBrowseForFolderRequested;

        // Subscribe to the browse target path event
        _viewModel.BrowseForTargetPathRequested += OnBrowseForTargetPathRequested;

        // Subscribe to settings saved event (handles both save and cancel)
        _viewModel.SettingsSaved += OnSettingsSaved;

        _logger.LogInformation("Settings window initialized");
    }

    private async void OnBrowseForFolderRequested(object? sender, EventArgs e)
    {
        try
        {
            // Use StorageProvider API to open folder picker
            IReadOnlyList<IStorageFolder> folderDialog = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Download Directory",
                AllowMultiple = false
            });

            if (folderDialog.Count > 0)
            {
                _viewModel.DownloadDirectory = folderDialog[0].Path.LocalPath;
                _logger.LogInformation($"User selected directory: {_viewModel.DownloadDirectory}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error browsing for folder");
        }
    }

    private async void OnBrowseForTargetPathRequested(object? sender, EventArgs e)
    {
        try
        {
            // Use StorageProvider API to open folder picker
            IReadOnlyList<IStorageFolder> folderDialog = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Target Path for Libraries",
                AllowMultiple = false
            });

            if (folderDialog.Count > 0)
            {
                _viewModel.TargetPath = folderDialog[0].Path.LocalPath;
                _logger.LogInformation($"User selected target path: {_viewModel.TargetPath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error browsing for target path");
        }
    }

    private void OnSettingsSaved(object? sender, bool result)
    {
        // Close dialog with the result (true for save, false for cancel)
        _logger.LogInformation(result ? "Settings saved" : "Settings canceled");
        _resultCompletionSource.SetResult(result);
        Close(result);
    }

    /// <summary>
    /// Shows the dialog and returns a Task that completes when the dialog is closed
    /// </summary>
    /// <param name="owner">The owner window</param>
    /// <returns>A Task that completes with the dialog result (true for save, false for cancel)</returns>
    public new Task<bool> ShowDialog(Window owner)
    {
        _ = base.ShowDialog(owner);
        return _resultCompletionSource.Task;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
