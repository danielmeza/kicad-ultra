using Xilium.CefGlue.Avalonia;

namespace UltraLibrarianImporter.UI.Views;

/// <summary>
/// CefGlue's browser control, declared in <c>MainWindow.axaml</c>. It exists only for its implicit
/// parameterless constructor: <see cref="AvaloniaCefBrowser"/>'s one constructor takes an optional
/// request-context factory, and the XAML compiler does not accept that as parameterless (AVLN3000).
/// WebViewControl's internal <c>ChromiumBrowser</c> subclass did the same job until #67.
/// </summary>
public sealed class EmbeddedBrowser : AvaloniaCefBrowser
{
}
