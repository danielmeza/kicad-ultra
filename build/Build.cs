using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[ShutdownDotNetAfterServerBuild]
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Package);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;

    AbsolutePath SourceDirectory => RootDirectory / "UltraLibrarianImporter";
    AbsolutePath PythonDirectory => RootDirectory / "kicad-ultralibrarian-importer";
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath PluginDirectory => OutputDirectory / "kicad-ultralibrarian-importer";
    AbsolutePath BinDirectory => PluginDirectory / "bin";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(OutputDirectory);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });

    Target Publish => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            // Get all runtimes we need to publish for
            var runtimes = new[] { "win-x64", "linux-x64", "osx-x64" };
            var uiProject = Solution.GetProject("UltraLibrarianImporter.UI");

            foreach (var runtime in runtimes)
            {
                DotNetPublish(s => s
                    .SetProject(uiProject)
                    .SetConfiguration(Configuration)
                    .SetRuntime(runtime)
                    .SetSelfContained(true)
                    .SetPublishSingleFile(true)
                    .SetPublishTrimmed(true)
                    .SetOutput(BinDirectory / runtime));
            }
        });

    Target Package => _ => _
        .DependsOn(Publish)
        .Executes(() =>
        {
            // Create the KiCad plugin structure according to the documentation
            
            // 1. Copy the Python plugin files
            CopyDirectoryRecursively(PythonDirectory, PluginDirectory, DirectoryExistsPolicy.Merge);

            // 2. Ensure the plugin directory structure
            EnsureExistingDirectory(PluginDirectory / "resources");
            
            // 3. Create metadata.json if it doesn't exist already
            var metadataPath = PluginDirectory / "metadata.json";
            if (!File.Exists(metadataPath))
            {
                File.WriteAllText(metadataPath, @"{
  ""name"": ""UltraLibrarian Importer"",
  ""description"": ""Import components from UltraLibrarian into KiCad"",
  ""keywords"": [""ultralibrarian"", ""component"", ""library"", ""import""],
  ""version"": ""0.1.0"",
  ""author"": {
    ""name"": ""Your Name"",
    ""contact"": {
      ""web"": ""https://yourwebsite.com""
    }
  },
  ""maintainer"": {
    ""name"": ""Your Name"",
    ""contact"": {
      ""web"": ""https://yourwebsite.com""
    }
  },
  ""license"": ""MIT"",
  ""resources"": {
    ""homepage"": ""https://github.com/yourusername/kicad-ultralibrarian-importer""
  },
  ""versions_compatibility"": {
    ""min_version"": ""9.0.0"",
    ""max_version"": ""9.99.99""
  }
}");
            }

            // 4. Create the ZIP package
            var zipFile = OutputDirectory / "kicad-ultralibrarian-importer.zip";
            if (File.Exists(zipFile))
                File.Delete(zipFile);
            
            ZipFile.CreateFromDirectory(PluginDirectory, zipFile);
            
            Logger.Info($"Plugin package created at: {zipFile}");
        });
}