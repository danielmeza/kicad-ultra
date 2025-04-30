using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.ViewModels;

namespace UltraLibrarianImporter.UI.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _viewModel;
        private readonly ILogger<SettingsWindow> _logger;

        public SettingsWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public SettingsWindow(IConfigService configService, ILogger logger)
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            
            // Convert the generic logger to a typed logger
            _logger = logger as ILogger<SettingsWindow> ?? 
                     LoggerFactory.Create(builder => builder.AddConsole())
                     .CreateLogger<SettingsWindow>();
            
            // Create the view model with a typed logger
            _viewModel = new SettingsViewModel(configService, 
                LoggerFactory.Create(builder => builder.AddConsole())
                .CreateLogger<SettingsViewModel>());
                
            DataContext = _viewModel;
            
            // Subscribe to the browse folder event
            _viewModel.BrowseForFolderRequested += OnBrowseForFolderRequested;
            
            // Subscribe to save and cancel events for dialog closing
            _viewModel.SaveRequested += OnSaveRequested;
            _viewModel.CancelRequested += OnCancelRequested;
            
            _logger.LogInformation("Settings window initialized");
        }
        
        private async void OnBrowseForFolderRequested(object? sender, EventArgs e)
        {
            try
            {
                // Use newer StorageProvider API instead of obsolete OpenFolderDialog
                var folderDialog = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
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

        private void OnSaveRequested(object? sender, EventArgs e)
        {
            // Close dialog with success
            _logger.LogInformation("Settings saved");
            Close(true);
        }

        private void OnCancelRequested(object? sender, EventArgs e)
        {
            // Close dialog without saving
            _logger.LogInformation("Settings canceled");
            Close(false);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}