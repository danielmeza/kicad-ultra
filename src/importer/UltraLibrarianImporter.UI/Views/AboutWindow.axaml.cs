using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using KiCadSharp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using UltraLibrarianImporter.UI.ViewModels;

namespace UltraLibrarianImporter.UI.Views
{
    public partial class AboutWindow : Window
    {
        private readonly AboutViewModel _viewModel;
        private readonly ILogger<AboutWindow> _logger;

        /// <summary>
        /// Parameterless constructor for the Avalonia XAML designer. It chains to the real
        /// constructor with a no-op logger so that every construction path leaves
        /// <c>_logger</c> and <c>_viewModel</c> assigned; the previous body left both null.
        /// </summary>
        public AboutWindow()
            : this(NullLogger.Instance)
        {
        }

        public AboutWindow(ILogger logger, KiCad? kiCad = null)
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            
            // Convert the generic logger to a typed logger
            _logger = logger as ILogger<AboutWindow> ?? 
                     LoggerFactory.Create(builder => builder.AddConsole())
                     .CreateLogger<AboutWindow>();
            
            // Create the view model with a typed logger and KiCad instance
            _viewModel = new AboutViewModel(
                LoggerFactory.Create(builder => builder.AddConsole())
                .CreateLogger<AboutViewModel>(),
                kiCad);
                
            DataContext = _viewModel;
            
            // Subscribe to close event
            _viewModel.CloseRequested += OnCloseRequested;
            
            _logger.LogInformation("About window initialized");
        }
        
        private void OnCloseRequested(object? sender, EventArgs e)
        {
            // Close the window when requested by the view model
            Close();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}