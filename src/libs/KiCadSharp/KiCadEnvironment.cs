namespace KiCadSharp
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
        /// <returns>Socket or pipe path from environment, or null if not set</returns>
        public static string? GetApiSocket()
        {
            return Environment.GetEnvironmentVariable("KICAD_API_SOCKET");
        }

        /// <summary>
        /// A token that uniquely identifies a KiCad instance
        /// </summary>
        /// <returns>API token from environment, or null if not set</returns>
        public static string? GetApiToken()
        {
            return Environment.GetEnvironmentVariable("KICAD_API_TOKEN");
        }

        public static string? GetProjectDirectory()
        {
            return Environment.GetEnvironmentVariable("KIPRJMOD");
        }

        public static string? GetUserTemplateDirectory()
        {
            return Environment.GetEnvironmentVariable("KICAD_USER_TEMPLATE_DIR");
        }

        public static string? GetModelsDirectory()
        {
            return Environment.GetEnvironmentVariable("KICAD9_3DMODEL_DIR");
        }

        public static string? GetFootprintDirectory()
        {
            return Environment.GetEnvironmentVariable("KICAD9_FOOTPRINT_DIR");
        }

        public static string? GetSymbolDirectory()
        {
            return Environment.GetEnvironmentVariable("KICAD9_SYMBOL_DIR");
        }

        public static string? GetDesignBlockDirectory()
        {
            return Environment.GetEnvironmentVariable("KICAD9_DESIGN_BLOCK_DIR");
        }

        public static string? GetPythonVirtualEnvironment()
        {
            return Environment.GetEnvironmentVariable("VIRTUAL_ENV");
        }


        /// <summary>
        /// Gets the default socket path based on the operating system
        /// </summary>
        /// <returns>Default socket path for the current platform</returns>
        public static string GetDefaultSocketPath()
        {
            string? path = GetApiSocket();
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }

            // Use the same logic as the Python implementation
            if (OperatingSystem.IsWindows())
            {
                return $"ipc://{Path.GetTempPath()}\\kicad\\api.sock";
            }
            else
            {
                return "ipc:///tmp/kicad/api.sock";
            }
        }

        /// <summary>
        /// Generates a random client name for KiCad API connections
        /// </summary>
        /// <returns>Random client name</returns>
        public static string GenerateRandomClientName()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var suffix = new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());

            return $"anonymous-{suffix}";
        }


        public static bool IsRunningOnKiCad() => !string.IsNullOrWhiteSpace(GetApiToken()) && !string.IsNullOrWhiteSpace(GetApiSocket());
    }
}
