using Google.Protobuf.WellKnownTypes;

using Kiapi.Common.Commands;
using Kiapi.Common.Project;
using Kiapi.Common.Types;

namespace UltraLibrarianImporter.KiCadBindings
{
    /// <summary>
    /// Represents a KiCad project
    /// </summary>
    public class Project : KiCadIPCProxy
    {
        private readonly DocumentSpecifier _document;
        
        /// <summary>
        /// Creates a new Project proxy
        /// </summary>
        /// <param name="client">KiCad IPC client</param>
        /// <param name="document">Document specifier for the project</param>
        public Project(KiCadIPCClient client, DocumentSpecifier document) : base(client)
        {
            _document = document;
            
            // Ensure the document type is set correctly
            if (_document.Type != DocumentType.DoctypeProject)
            {
                _document.Type = DocumentType.DoctypeProject;
            }
        }
        
        /// <summary>
        /// Gets the document specifier for this project
        /// </summary>
        public DocumentSpecifier Document => _document;
        
        /// <summary>
        /// Gets the name of the project
        /// </summary>
        public string Name => _document.Project.Name;
        
        /// <summary>
        /// Gets the path of the project
        /// </summary>
        public string Path => _document.Project.Path;
        
        /// <summary>
        /// Gets the net classes defined in the project
        /// </summary>
        /// <returns>Array of net classes</returns>
        public async ValueTask<NetClass[]> GetNetClasses()
        {
            var command = new GetNetClasses();
            var response = await Send<NetClassesResponse>(command);
            return [.. response.NetClasses];
        }
        
        /// <summary>
        /// Sets the net classes in the project
        /// </summary>
        /// <param name="netClasses">Net classes to set</param>
        /// <param name="mergeMode">How to merge with existing net classes</param>
        public async ValueTask SetNetClasses(NetClass[] netClasses, MapMergeMode mergeMode = MapMergeMode.MmmMerge)
        {
            var command = new SetNetClasses
            {
                MergeMode = mergeMode
            };
            command.NetClasses.AddRange(netClasses);
            await Send(command);
        }
        
        /// <summary>
        /// Expands text variables in a string
        /// </summary>
        /// <param name="text">Text containing variables to expand</param>
        /// <returns>Text with variables expanded</returns>
        public async ValueTask<string> ExpandTextVariables(string text)
        {
            var command = new ExpandTextVariables
            {
                Document = _document
            };
            command.Text.Add(text);
            
            var response = await Send<ExpandTextVariablesResponse>(command);
            return response.Text.Count > 0 ? response.Text[0] : string.Empty;
        }
        
        /// <summary>
        /// Expands text variables in multiple strings
        /// </summary>
        /// <param name="texts">Array of texts containing variables to expand</param>
        /// <returns>Array of texts with variables expanded</returns>
        public async ValueTask<string[]> ExpandTextVariables(string[] texts)
        {
            var command = new ExpandTextVariables
            {
                Document = _document
            };
            command.Text.AddRange(texts);
            
            var response = await Send<ExpandTextVariablesResponse>(command);
            return response.Text.ToArray();
        }
        
        /// <summary>
        /// Gets the text variables defined in the project
        /// </summary>
        /// <returns>Text variables object</returns>
        public async ValueTask<TextVariables> GetTextVariables()
        {
            var command = new GetTextVariables
            {
                Document = _document
            };
            return await Send<TextVariables>(command);
        }
        
        /// <summary>
        /// Sets the text variables in the project
        /// </summary>
        /// <param name="variables">Text variables to set</param>
        /// <param name="mergeMode">How to merge with existing variables</param>
        public async ValueTask SetTextVariables(TextVariables variables, MapMergeMode mergeMode = MapMergeMode.MmmMerge)
        {
            var command = new SetTextVariables
            {
                Document = _document,
                Variables = variables,
                MergeMode = mergeMode
            };
            await Send(command);
        }
    }
}