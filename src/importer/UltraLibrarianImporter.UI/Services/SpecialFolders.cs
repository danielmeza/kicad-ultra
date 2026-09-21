using System;
using System.IO;

namespace UltraLibrarianImporter.UI.Services;

/// <summary>
/// The absolute path of a special folder, whether or not it exists yet. Use this instead of
/// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> everywhere in the app (#70).
/// </summary>
/// <remarks>
/// <para>
/// On Linux and macOS, <c>Environment.GetFolderPath(folder)</c> returns <c>""</c> for a folder that
/// does not exist yet - <c>~/.config</c> on a fresh account, <c>~/Documents</c> where no desktop
/// created it - and <c>Path.Combine("", "X")</c> is the relative path <c>X</c>, which then lands in
/// whatever the working directory happens to be. With
/// <see cref="Environment.SpecialFolderOption.DoNotVerify"/> the runtime returns the path the folder
/// would have instead. On Linux that is <c>$XDG_CONFIG_HOME</c>, else <c>~/.config</c>, for
/// <c>ApplicationData</c>; <c>XDG_DOCUMENTS_DIR</c> from <c>user-dirs.dirs</c>, else
/// <c>~/Documents</c>, for <c>MyDocuments</c>; and <c>$HOME</c> for <c>UserProfile</c>.
/// </para>
/// <para>
/// Nothing is created here. Each caller creates the directory it writes into, which creates the
/// special folder along with it, and only when it writes: working out the default download directory
/// must not create <c>~/Documents</c>.
/// </para>
/// </remarks>
public static class SpecialFolders
{
    /// <summary>
    /// The absolute path of <paramref name="folder"/>. It may not exist yet.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The folder has no absolute path here: the platform does not define it, or it is derived from a
    /// <c>HOME</c> that is not an absolute path.
    /// </exception>
    public static string GetPath(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
        return Path.IsPathFullyQualified(path)
            ? path
            : throw new InvalidOperationException(
                $"The {folder} folder has no absolute path on this system (the runtime returned \"{path}\"). "
                + "On Linux and macOS it is derived from HOME, which must be set to an absolute path.");
    }
}
