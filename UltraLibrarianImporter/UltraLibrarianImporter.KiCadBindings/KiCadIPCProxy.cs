using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Kiapi.Common;
using Kiapi.Common.Commands;
using Kiapi.Common.Types;

namespace UltraLibrarianImporter.KiCadBindings
{
    /// <summary>
    /// Base class for all KiCad IPC proxy classes
    /// </summary>
    public abstract class KiCadIPCProxy
    {
        /// <summary>
        /// Creates a new KiCad IPC proxy with the given client
        /// </summary>
        /// <param name="client">KiCad IPC client for communication with KiCad</param>
        protected KiCadIPCProxy(KiCadIPCClient client)
        {
            Client = client;
        }

        /// <summary>
        /// The KiCad IPC client used for communication
        /// </summary>
        protected KiCadIPCClient Client { get; }
        
        /// <summary>
        /// Sends a command to KiCad and returns a result of the specified type
        /// </summary>
        /// <typeparam name="TResult">Type of result to return</typeparam>
        /// <param name="command">Command to send</param>
        /// <returns>Result of the command</returns>
        protected async ValueTask<TResult> Send<TResult>(IMessage command) where TResult : IMessage, new()
        {
            return await Client.Send<TResult>(command);
        }
        
        /// <summary>
        /// Sends a command to KiCad with no result
        /// </summary>
        /// <param name="command">Command to send</param>
        protected async ValueTask Send(IMessage command)
        {
            await Client.Send(command);
        }
    }
}