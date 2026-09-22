using System;
using System.Collections.Generic;
using System.Threading.Tasks;

#if DEBUG
using Avalonia; // AttachDevTools(), which only exists in Debug builds
#endif
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using KiCadSharp;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using UltraLibrarianImporter.UI.Services;
using UltraLibrarianImporter.UI.Services.Secrets;
using UltraLibrarianImporter.UI.ViewModels;

namespace UltraLibrarianImporter.UI.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly ILogger<SettingsWindow> _logger;
    private readonly TaskCompletionSource<bool> _resultCompletionSource;

    /// <summary>
    /// Parameterless constructor for the Avalonia XAML designer, which has no container. It chains to
    /// the real constructor with a view model of its own and a factory that logs nowhere, so that
    /// every construction path leaves <c>_logger</c> and <c>_viewModel</c> assigned; an earlier body
    /// left both null, and the first Browse or Save raised a NullReferenceException.
    /// </summary>
    public SettingsWindow()
        : this(DesignerViewModel(), NullLoggerFactory.Instance)
    {
    }

    /// <summary>
    /// The view model for the fallback above, and the only one this window builds: every other path
    /// resolves it from the container, so that it shares the app's single <see cref="ConfigService"/>
    /// rather than a second instance of a singleton. This one has no container to take it from, so it
    /// constructs one — with no credential store, so that the designer neither reads nor writes the
    /// developer's keyring and <see cref="ConfigService"/> keeps its secrets in memory.
    /// </summary>
    private static SettingsViewModel DesignerViewModel() =>
        new(new ConfigService(
                NullLogger<ConfigService>.Instance,
                new UnavailableSecretStore("The XAML designer has no credential store.")),
            NullLogger<SettingsViewModel>.Instance,
            DefaultSettingsMonitor());

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

    /// <param name="viewModel">
    /// The view model, resolved from the container by the caller so that it holds the app's
    /// <see cref="ConfigService"/> singleton and logs through the container's factory.
    /// </param>
    /// <param name="loggerFactory">
    /// The container's factory, so that this window logs through NLog like the rest of the app (#123).
    /// Its logger came from a <see cref="LoggerFactory"/> this window built itself with a console
    /// provider, as did the view model's and the config service's on the constructor removed with it:
    /// that wrote to stdout, which #98 removed everywhere else, and none of the three was disposed.
    /// </param>
    public SettingsWindow(SettingsViewModel viewModel, ILoggerFactory loggerFactory)
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        // Set up the task completion source for the dialog result
        _resultCompletionSource = new TaskCompletionSource<bool>();
        _logger = loggerFactory.CreateLogger<SettingsWindow>();
        _viewModel = viewModel;
        DataContext = _viewModel;
        BindViewModelEvents();
    }

    private void BindViewModelEvents()
    {
        _viewModel.BrowseForFolderRequested += OnBrowseForFolderRequested;
        _viewModel.BrowseForTargetPathRequested += OnBrowseForTargetPathRequested;
        _viewModel.BrowseForEasyEda2KiCadRequested += OnBrowseForEasyEda2KiCadRequested;
        _viewModel.SettingsSaved += OnSettingsSaved;
        _viewModel.CopyToClipboardRequested += async (s, text) =>
        {
            try
            {
                if (Clipboard != null)
                {
                    await Clipboard.SetTextAsync(text);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying to clipboard");
            }
        };

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

    private async void OnBrowseForEasyEda2KiCadRequested(object? sender, EventArgs e)
    {
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select easyeda2kicad, or a Python interpreter that has it installed",
                AllowMultiple = false
            });

            if (files.Count > 0)
            {
                _viewModel.EasyEda2KiCadPath = files[0].Path.LocalPath;
                _logger.LogInformation("User selected easyeda2kicad path: {Path}", _viewModel.EasyEda2KiCadPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error browsing for easyeda2kicad");
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
