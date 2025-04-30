using System.Buffers;
using System.IO.Pipes;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Kiapi.Common;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UltraLibrarianImporter.KiCadBindings
{

    public class KiCadClientSettings
    {
        public string? PipeName { get; set; }

        public string? Token { get; set; }

        public string? ClientName { get; set; }

    }
    public class KiCadIPCClient : IDisposable
    {
        private NamedPipeClientStream _client;
        private readonly ILogger<KiCadIPCClient> _logger;
        private KiCadClientSettings _settings;

        CancellationTokenSource _connectionCancellationSource;

        public KiCadIPCClient(IOptionsMonitor<KiCadClientSettings> settingsMonitor, ILogger<KiCadIPCClient> logger)
        {
            settingsMonitor.OnChange(SettingsChanged);
            _settings = settingsMonitor.CurrentValue;
            _logger = logger;
            _connectionCancellationSource = new CancellationTokenSource();
        }

        private void SettingsChanged(KiCadClientSettings settings, string arg2)
        {
            _settings = settings;
            if (_client?.IsConnected == true)
            {
                Disconnect();
            }
        }

        public bool IsConnected => _client.IsConnected;

        private string GetDefaultPipeName()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Local/Temp/kicad/api.sock");
        }

        public async ValueTask Connect(CancellationToken cancellationToken = default)
        {

            if (_client?.IsConnected == true)
            {
                return;
            }

            _client = new NamedPipeClientStream(string.IsNullOrWhiteSpace(_settings.PipeName) ? _settings.PipeName : GetDefaultPipeName());
            await _client.ConnectAsync(cancellationToken);
            _connectionCancellationSource = new CancellationTokenSource();
        }

        public void Disconnect()
        {
            _connectionCancellationSource.Cancel();
            _client?.Dispose();
        }

        public async ValueTask<TResult> Send<TResult>(IMessage command, CancellationToken cancellationToken = default)
            where TResult : IMessage, new()
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var token = CancellationTokenSource.CreateLinkedTokenSource(_connectionCancellationSource.Token, cancellationToken).Token;

            if (!IsConnected)
            {
                await Connect(cancellationToken);
            }

            var envelope = new ApiRequest();
            envelope.Message = Any.Pack(command);
            envelope.Header.KicadToken = _settings.Token;
            envelope.Header.ClientName = _settings.PipeName;
            try
            {
                var data = envelope.ToByteArray().AsMemory();
                await _client.WriteAsync(data, token);
            }
            catch (Exception ex)
            {
                throw new KiCadConnectionException($"Failed to send command to KiCad: {ex.Message}", ex);
            }

            ApiResponse? reply = null;

            try
            {
                using var owner = MemoryPool<byte>.Shared.Rent(4 * 1000 * 1024);
                var readBytes = await _client.ReadAsync(owner.Memory, token);
                reply = ApiResponse.Parser.ParseFrom(owner.Memory.Span);


            }
            catch (Exception ex)
            {
                throw new KiCadConnectionException($"Error receiving reply from KiCad: {ex.Message}", ex);
            }

            if (reply.Status.Status != ApiStatusCode.AsOk)
            {
                throw new ApiException($"KiCad returned error: {reply.Status}");
            }

            if (!reply.Message.TryUnpack<TResult>(out var result))
            {
                throw new ApiException($"Failed to unpack {typeof(TResult).FullName} from the response to {command.GetType().FullName}");
            }

            if (string.IsNullOrWhiteSpace(_settings.Token))
            {
                _settings.Token = reply.Header.KicadToken;
            }
            return result;
        }

        public async ValueTask Send(IMessage command, CancellationToken cancellationToken = default)
        {
            await Send<Empty>(command, cancellationToken);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
