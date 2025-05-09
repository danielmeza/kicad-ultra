namespace KiCadSharp
{
    [Serializable]
    public class KiCadConnectionException : Exception
    {
        public KiCadConnectionException()
        {
        }

        public KiCadConnectionException(string? message) : base(message)
        {
        }

        public KiCadConnectionException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}