using System;

#if DEBUG
using Avalonia; // AttachDevTools(), which only exists in Debug builds
#endif
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using KiCadSharp;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using KiCadUltra.ViewModels;

namespace KiCadUltra.Views;

public partial class AboutWindow : Window
{
    private readonly AboutViewModel _viewModel;
    private readonly ILogger<AboutWindow> _logger;

    /// <summary>
    /// Parameterless constructor for the Avalonia XAML designer, which has no container. It chains to
    /// the real constructor with a factory that logs nowhere, so that every construction path leaves
    /// <c>_logger</c> and <c>_viewModel</c> assigned; the previous body left both null.
    /// </summary>
    public AboutWindow()
        : this(NullLoggerFactory.Instance)
    {
    }

    /// <param name="loggerFactory">
    /// The container's factory, so that this window and its view model log through NLog like the rest
    /// of the app (#121). Both loggers used to come from a <see cref="LoggerFactory"/> this window
    /// built itself with a console provider: that wrote to stdout, which #98 removed everywhere else,
    /// and neither factory was ever disposed.
    /// </param>
    /// <param name="kiCad">KiCad client (can be null)</param>
    public AboutWindow(ILoggerFactory loggerFactory, KiCad? kiCad = null)
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif

        _logger = loggerFactory.CreateLogger<AboutWindow>();

        // Create the view model with a typed logger and KiCad instance
        _viewModel = new AboutViewModel(loggerFactory.CreateLogger<AboutViewModel>(), kiCad);

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
