using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Kiapi.Common;

using Microsoft.Extensions.Logging;

using nng;
namespace KiCadSharp
{

    public class KiCadClientSettings
    {
        public string? PipeName { get; set; }

        public string? Token { get; set; }

        public string? ClientName { get; internal set; }

        public const string DefaultClientName = "kicad.client";

    }
    public class KiCadIPCClient : IDisposable
    {
        private readonly ILogger<KiCadIPCClient> _logger;
        private readonly IAPIFactory<INngMsg> _messageFactory;
        private KiCadClientSettings _settings;
        private EventWaitHandle sync = new EventWaitHandle(false, EventResetMode.ManualReset);

        private IReqSocket _socket;
        private CancellationTokenSource _connectionCancellationSource;
        public KiCadIPCClient(IAPIFactory<INngMsg> messageFactory, KiCadClientSettings settings, ILogger<KiCadIPCClient> logger)
        {
            _messageFactory = messageFactory;
            _settings = settings;
            _logger = logger;
            _connectionCancellationSource = new CancellationTokenSource();
        }

        public bool IsConnected
        {
            get; private set;

        }
        public ValueTask Connect(CancellationToken cancellationToken = default)
        {

            if (IsConnected)
            {
                Disconnect();
            }

            if (string.IsNullOrWhiteSpace(_settings.PipeName))
            {
                throw new KiCadConnectionException("Pipename not provided");
            }

            _socket = _messageFactory.RequesterOpen()
                .ThenDial(_settings.PipeName, nng.Native.Defines.NngFlag.NNG_FLAG_ALLOC)
                .Unwrap();

            _connectionCancellationSource = new CancellationTokenSource();
            IsConnected = true;
            return ValueTask.CompletedTask;
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
            envelope.Header = new ApiRequestHeader()
            {
                KicadToken = _settings.Token,
                ClientName = _settings.PipeName,
            };


            try
            {
                var request = _messageFactory.CreateMessage();
                request.Append(envelope.ToByteArray());
                _socket.SendMsg(request).Unwrap();
            }
            catch (Exception ex)
            {
                throw new KiCadConnectionException($"Failed to send command to KiCad: {ex.Message}", ex);
            }

            ApiResponse? reply;
            try
            {
                var response = _socket.RecvMsg().Unwrap();
                reply = ApiResponse.Parser.ParseFrom(response.AsSpan());
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

        public void Disconnect()
        {
            _connectionCancellationSource.Cancel();
            IsConnected = false;
            _socket?.Dispose();
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
