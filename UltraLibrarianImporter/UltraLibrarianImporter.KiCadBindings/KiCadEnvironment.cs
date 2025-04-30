namespace UltraLibrarianImporter.KiCadBindings
{
    /// <summary>
    /// When KiCad launches API plugins (see below), it will set several environment variables that can be used by the client to know where to connect to the IPC API. 
    /// https://dev-docs.kicad.org/en/apis-and-binding/ipc-api/for-addon-developers/index.html#_connecting_to_kicad
    /// </summary>
    public static class KiCadEnvironment
    {

        /// <summary>
        /// The full path to the socket or pipe that the client should connect to.
        /// </summary>
        /// <returns></returns>
        public static string? GetApiSocket()
        {
            return Environment.GetEnvironmentVariable("KICAD_API_SOCKET");
        }
        public static string? GetApiToken() => Environment.GetEnvironmentVariable("KICAD_API_TOKEN");
    }
}
