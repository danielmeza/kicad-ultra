using Kiapi.Common.Commands;
using Kiapi.Common.Project;
using Kiapi.Common.Types;

namespace KiCadSharp
{
    /// <summary>
    /// Represents a connection to a running KiCad instance
    /// </summary>
    public class KiCad : KiCadIPCProxy
    {
        /// <summary>
        /// Creates a new KiCad connection with the specified client
        /// </summary>
        /// <param name="client">KiCad IPC client for communication with KiCad</param>
        public KiCad(KiCadIPCClient client) : base(client)
        {

        }

        /// <summary>
        /// Pings the KiCad instance to check if the connection is alive
        /// </summary>
        public async ValueTask Ping()
        {
            await Send(new Ping());
        }

        /// <summary>
        /// Gets the version of the connected KiCad instance
        /// </summary>
        /// <returns>KiCad version information</returns>
        public async ValueTask<KiCadVersion> GetVersion()
        {
            var response = await Send<GetVersionResponse>(new GetVersion());
            return new KiCadVersion(response.Version);
        }

        /// <summary>
        /// Gets the path to a KiCad binary
        /// </summary>
        /// <param name="binaryName">Name of the binary (e.g., "kicad-cli")</param>
        /// <returns>Full path to the binary</returns>
        public async ValueTask<string> GetKiCadBinaryPath(string binaryName)
        {
            var command = new GetKiCadBinaryPath
            {
                BinaryName = binaryName
            };
            var response = await Send<PathResponse>(command);
            return response.Path;
        }

        /// <summary>
        /// Gets a writable path for plugin settings
        /// </summary>
        /// <param name="identifier">Plugin identifier</param>
        /// <returns>Path where plugin settings can be stored</returns>
        public async ValueTask<string> GetPluginSettingsPath(string identifier)
        {
            var command = new GetPluginSettingsPath
            {
                Identifier = identifier
            };
            var response = await Send<StringResponse>(command);
            return response.Response;
        }

        /// <summary>
        /// Gets all open documents of the specified type
        /// </summary>
        /// <param name="documentType">Type of documents to retrieve</param>
        /// <returns>List of document specifiers</returns>
        public async ValueTask<DocumentSpecifier[]> GetOpenDocuments(DocumentType documentType)
        {
            var command = new GetOpenDocuments
            {

                Type = documentType
            };
            var response = await Send<GetOpenDocumentsResponse>(command);
            return response.Documents.ToArray();
        }

        /// <summary>
        /// Gets a board object for the first open board
        /// </summary>
        /// <returns>Board object</returns>
        /// <exception cref="ApiException">Thrown if no board is open</exception>
        public async ValueTask<Board> GetBoard()
        {
            var docs = await GetOpenDocuments(DocumentType.DoctypePcb);
            if (docs.Length == 0)
            {
                throw new ApiException("Expected to be able to retrieve at least one board");
            }
            return new Board(Client, docs[0]);
        }

        /// <summary>
        /// Gets a project object for the specified document
        /// </summary>
        /// <param name="document">Document specifier</param>
        /// <returns>Project object</returns>
        public Project GetProject(DocumentSpecifier document)
        {
            return new Project(Client, document);
        }

        public async ValueTask<Project> GetProject()
        {
            var board = await GetBoard();
            return board.GetProject();
        }

        /// <summary>
        /// Runs a KiCad tool action
        /// </summary>
        /// <param name="actionName">Name of the action to run</param>
        /// <returns>Status of the action</returns>
        public async ValueTask<RunActionResponse> RunAction(string actionName)
        {
            var command = new RunAction
            {
                Action = actionName
            };
            return await Send<RunActionResponse>(command);
        }

        /// <summary>
        /// Refreshes the specified KiCad frame
        /// </summary>
        /// <param name="frameType">Type of frame to refresh</param>
        public async ValueTask RefreshEditor(FrameType frameType)
        {
            var command = new RefreshEditor
            {
                Frame = frameType
            };
            await Send(command);
        }

        /// <summary>
        /// Refreshes KiCad configuration paths
        /// </summary>
        /// <remarks>
        /// This is particularly useful after modifying library files or 3D models
        /// </remarks>
        public ValueTask RefreshPaths()
        {
            //await Send(new RefreshPaths()); this type doest exist in kicad IPC 
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Imports a library into KiCad
        /// </summary>
        /// <param name="path">Path to the library file or directory</param>
        /// <param name="name">Name to assign to the library</param>
        /// <param name="addToTable">Whether to add the library to the global table</param>
        public ValueTask ImportLibrary(string path, string name, bool addToTable = true)
        {
            //var command = new ImportLibrary this type does't exist in kicad
            //{
            //    Path = path,
            //    Name = name,
            //    AddToTable = addToTable
            //};
            //await Send(command); 
            return ValueTask.CompletedTask;
        }

        public async ValueTask<IDictionary<string, string>> GetTextVariables()
        {
            var response = await Send<TextVariables>(new GetTextVariables()
            {
                Document = new DocumentSpecifier()
                {
                    Type = DocumentType.DoctypeProject,
                }
            });
            return response.Variables.ToDictionary();
        }

        /// <summary>
        /// Gets a path from KiCad
        /// </summary>
        /// <param name="pathType">Type of path to retrieve</param>
        /// <returns>The requested path</returns>
        //public async ValueTask<string> GetPath(PathType pathType) type does't exist in kicad
        //{
        //    var command = new GetPath
        //    {
        //        Type = pathType
        //    };
        //    var response = await Send<PathResponse>(command);
        //    return response.Path;
        //}
    }

    /// <summary>
    /// Represents KiCad version information
    /// </summary>
    public class KiCadVersion
    {
        /// <summary>
        /// Major version number
        /// </summary>
        public uint Major { get; }

        /// <summary>
        /// Minor version number
        /// </summary>
        public uint Minor { get; }

        /// <summary>
        /// Patch version number
        /// </summary>
        public uint Patch { get; }

        /// <summary>
        /// Full version string
        /// </summary>
        public string FullVersion { get; }

        /// <summary>
        /// Creates a new KiCad version from the proto version
        /// </summary>
        /// <param name="version">Proto version object</param>
        public KiCadVersion(Kiapi.Common.Types.KiCadVersion version)
        {
            Major = version.Major;
            Minor = version.Minor;
            Patch = version.Patch;
            FullVersion = version.FullVersion;
        }

        /// <summary>
        /// Returns a string representation of the version
        /// </summary>
        public override string ToString()
        {
            return $"{Major}.{Minor}.{Patch} ({FullVersion})";
        }
    }
}
