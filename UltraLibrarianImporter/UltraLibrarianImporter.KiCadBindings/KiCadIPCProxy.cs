namespace UltraLibrarianImporter.KiCadBindings
{
    public abstract class KiCadIPCProxy
    {

        public KiCadIPCProxy(KiCadIPCClient client)
        {
            Client = client;
        }

        protected KiCadIPCClient Client { get; }
        
    }
}