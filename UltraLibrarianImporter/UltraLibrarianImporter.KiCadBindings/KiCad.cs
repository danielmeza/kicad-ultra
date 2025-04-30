using Google.Protobuf.WellKnownTypes;

using Kiapi.Common.Commands;

namespace UltraLibrarianImporter.KiCadBindings
{
    public class KiCad : KiCadIPCProxy
    {
        public KiCad(KiCadIPCClient client) : base(client)
        {

        }

        public async ValueTask Ping()
        {
            await Client.Send(new Ping());
        }
    }
}
