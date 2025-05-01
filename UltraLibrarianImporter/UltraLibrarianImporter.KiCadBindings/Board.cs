using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Kiapi.Common.Commands;
using Kiapi.Common.Types;
using Kiapi.Board.Commands;
using Kiapi.Board.Types;

namespace UltraLibrarianImporter.KiCadBindings
{
    /// <summary>
    /// Represents a KiCad PCB board
    /// </summary>
    public class Board : KiCadIPCProxy
    {
        private readonly DocumentSpecifier _document;
        
        /// <summary>
        /// Creates a new Board proxy
        /// </summary>
        /// <param name="client">KiCad IPC client</param>
        /// <param name="document">Document specifier for the board</param>
        public Board(KiCadIPCClient client, DocumentSpecifier document) : base(client)
        {
            _document = document;
        }
        
        /// <summary>
        /// Gets the document specifier for this board
        /// </summary>
        public DocumentSpecifier Document => _document;
        
        /// <summary>
        /// Gets the name of the board file
        /// </summary>
        public string Name => _document.BoardFilename;
        
        /// <summary>
        /// Gets a project object for this board
        /// </summary>
        /// <returns>Project object</returns>
        public Project GetProject()
        {
            return new Project(Client, _document);
        }
        
        /// <summary>
        /// Saves the board
        /// </summary>
        public async ValueTask Save()
        {
            var command = new SaveDocument
            {
                Document = _document
            };
            await Send(command);
        }
        
        /// <summary>
        /// Saves the board to a new file
        /// </summary>
        /// <param name="filename">Path to save the board to</param>
        /// <param name="overwrite">Whether to overwrite existing files</param>
        /// <param name="includeProject">Whether to include the project files</param>
        public async ValueTask SaveAs(string filename, bool overwrite = false, bool includeProject = true)
        {
            var command = new SaveCopyOfDocument
            {
                Document = _document,
                Path = filename
            };
            command.Options = new SaveOptions
            {
                Overwrite = overwrite,
                IncludeProject = includeProject
            };
            await Send(command);
        }
        
        /// <summary>
        /// Reverts the board to the last saved state
        /// </summary>
        public async ValueTask Revert()
        {
            var command = new RevertDocument
            {
                Document = _document
            };
            await Send(command);
        }
        
        /// <summary>
        /// Begins a commit transaction on the board
        /// </summary>
        /// <returns>Commit object with ID to be used in push/drop operations</returns>
        public async ValueTask<Commit> BeginCommit()
        {
            var response = await Send<BeginCommitResponse>(new BeginCommit());
            return new Commit(response.Id);
        }
        
        /// <summary>
        /// Pushes changes made during a commit transaction
        /// </summary>
        /// <param name="commit">Commit object returned from BeginCommit</param>
        /// <param name="message">Optional message describing the changes</param>
        public async ValueTask PushCommit(Commit commit, string message = "")
        {
            var command = new EndCommit
            {
                Id = commit.Id,
                Action = CommitAction.CmaCommit,
                Message = message
            };
            await Send<EndCommitResponse>(command);
        }
        
        /// <summary>
        /// Drops (cancels) changes made during a commit transaction
        /// </summary>
        /// <param name="commit">Commit object returned from BeginCommit</param>
        public async ValueTask DropCommit(Commit commit)
        {
            var command = new EndCommit
            {
                Id = commit.Id,
                Action = CommitAction.CmaDrop
            };
            await Send<EndCommitResponse>(command);
        }
        
        /// <summary>
        /// Gets items of specified types from the board
        /// </summary>
        /// <param name="types">Types of items to retrieve</param>
        /// <returns>Array of items</returns>
        public async ValueTask<IMessage[]> GetItems(params KiCadObjectType[] types)
        {
            var command = new GetItems
            {
                Header = new ItemHeader { Document = _document }
            };
            command.Types_.AddRange(types);
            
            var response = await Send<GetItemsResponse>(command);
            return response.Items.ToArray();
        }
        
        /// <summary>
        /// Gets the selection on the board
        /// </summary>
        /// <param name="types">Optional filter for item types</param>
        /// <returns>Array of selected items</returns>
        public async ValueTask<IMessage[]> GetSelection(params KiCadObjectType[] types)
        {
            var command = new GetSelection
            {
                Header = new ItemHeader { Document = _document }
            };
            if (types != null && types.Length > 0)
            {
                command.Types_.AddRange(types);
            }
            
            var response = await Send<SelectionResponse>(command);
            return response.Items.ToArray();
        }
        
        /// <summary>
        /// Clears the current selection on the board
        /// </summary>
        public async ValueTask ClearSelection()
        {
            var command = new ClearSelection
            {
                Header = new ItemHeader { Document = _document }
            };
            await Send(command);
        }
        
        /// <summary>
        /// Gets the active layer on the board
        /// </summary>
        /// <returns>Active layer</returns>
        public async ValueTask<BoardLayer> GetActiveLayer()
        {
            var command = new GetActiveLayer
            {
                Board = _document
            };
            var response = await Send<BoardLayerResponse>(command);
            return response.Layer;
        }
        
        /// <summary>
        /// Sets the active layer on the board
        /// </summary>
        /// <param name="layer">Layer to set as active</param>
        public async ValueTask SetActiveLayer(BoardLayer layer)
        {
            var command = new SetActiveLayer
            {
                Board = _document,
                Layer = layer
            };
            await Send(command);
        }
        
        /// <summary>
        /// Gets the board as a string in KiCad's board file format
        /// </summary>
        /// <returns>Board file content as string</returns>
        public async ValueTask<string> GetAsString()
        {
            var command = new SaveDocumentToString
            {
                Document = _document
            };
            var response = await Send<SavedDocumentResponse>(command);
            return response.Contents;
        }
        
        /// <summary>
        /// Refills all zones on the board
        /// </summary>
        public async ValueTask RefillZones()
        {
            var command = new RefillZones
            {
                Board = _document
            };
            await Send(command);
        }
        
        /// <summary>
        /// Creates items on the board
        /// </summary>
        /// <param name="items">Items to create</param>
        /// <returns>Response containing the created items</returns>
        public async ValueTask<CreateItemsResponse> CreateItems(params IMessage[] items)
        {
            var command = new CreateItems
            {
                Header = new ItemHeader { Document = _document }
            };
            
            foreach (var item in items)
            {
                command.Items.Add(Any.Pack(item));
            }
            
            return await Send<CreateItemsResponse>(command);
        }
        
        /// <summary>
        /// Updates items on the board
        /// </summary>
        /// <param name="items">Items to update</param>
        /// <returns>Response containing the updated items</returns>
        public async ValueTask<UpdateItemsResponse> UpdateItems(params IMessage[] items)
        {
            var command = new UpdateItems
            {
                Header = new ItemHeader { Document = _document }
            };
            
            foreach (var item in items)
            {
                command.Items.Add(Any.Pack(item));
            }
            
            return await Send<UpdateItemsResponse>(command);
        }
        
        /// <summary>
        /// Deletes items from the board
        /// </summary>
        /// <param name="itemIds">IDs of items to delete</param>
        /// <returns>Response containing the result of the deletion</returns>
        public async ValueTask<DeleteItemsResponse> DeleteItems(params KIID[] itemIds)
        {
            var command = new DeleteItems
            {
                Header = new ItemHeader { Document = _document }
            };
            command.ItemIds.AddRange(itemIds);
            
            return await Send<DeleteItemsResponse>(command);
        }
    }
    
    /// <summary>
    /// Represents a commit transaction on a board
    /// </summary>
    public class Commit
    {
        /// <summary>
        /// Creates a new commit with the specified ID
        /// </summary>
        /// <param name="id">Commit ID from BeginCommit</param>
        public Commit(KIID id)
        {
            Id = id;
        }
        
        /// <summary>
        /// Gets the ID of this commit
        /// </summary>
        public KIID Id { get; }
    }
}