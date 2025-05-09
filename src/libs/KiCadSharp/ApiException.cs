namespace KiCadSharp
{
    [Serializable]
    internal class ApiException : Exception
    {
        private string _v;
        private object _ex;

        public ApiException()
        {
        }

        public ApiException(string? message) : base(message)
        {
        }

        public ApiException(string v, object ex)
        {
            _v = v;
            _ex = ex;
        }

        public ApiException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}