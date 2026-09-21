using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using UltraLibrarianImporter.UI.Services.Interfaces;
using UltraLibrarianImporter.UI.Services.Providers;
using UltraLibrarianImporter.UI.Services.Providers.Jlcpcb;

namespace UltraLibrarianImporter.UI.Services.Mcp;

public class McpServer
{
    private readonly IPartAggregatorService _aggregatorService;
    private readonly IComponentProviderRegistry _providerRegistry;
    private readonly IConfigService _configService;
    private readonly ILogger<McpServer> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    /// <summary>
    /// Said beside EasyEDA / LCSC results: they never come from JLCPCB's official Components API here,
    /// whatever credentials are stored, and why.
    /// </summary>
    public const string NoOfficialJlcpcbApiNote =
        "EasyEDA / LCSC results from this MCP server never come from JLCPCB's official Components API, even when API credentials are configured: " +
        "JLCPCB's API terms forbid passing data obtained through the API to third parties, and this server hands every result to the connected AI client.";

    // jlcpcbSources must be JlcpcbSourcePolicy.WebsiteEndpointOnly: every result this server returns
    // goes to a third party, the AI client, which JLCPCB's API terms forbid for data from its official
    // API (#51). Refused here as well as chosen in Program, so a change to either cannot undo it.
    public McpServer(
        IPartAggregatorService aggregatorService,
        IComponentProviderRegistry providerRegistry,
        IConfigService configService,
        JlcpcbSourcePolicy jlcpcbSources,
        ILogger<McpServer> logger)
    {
        if (jlcpcbSources.AllowsOfficialApi)
        {
            throw new InvalidOperationException(
                "The MCP server must be built with JlcpcbSourcePolicy.WebsiteEndpointOnly: it would otherwise pass data from JLCPCB's official API to the AI client.");
        }

        _aggregatorService = aggregatorService;
        _providerRegistry = providerRegistry;
        _configService = configService;
        _logger = logger;
    }

    public async Task RunStdioAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("KiCad Component Explorer MCP Server running on stdio (Protocol version 2024-11-05)");

        using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line == null)
            {
                // EOF reached
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                McpMessage? request = JsonSerializer.Deserialize<McpMessage>(line, _jsonOptions);
                if (request == null)
                {
                    continue;
                }

                // Notifications do not have an ID and expect no response
                if (request.Id == null)
                {
                    HandleNotification(request);
                    continue;
                }

                McpMessage response = await HandleRequestAsync(request, cancellationToken);
                var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                await writer.WriteLineAsync(responseJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MCP message");
                var errResponse = new McpMessage
                {
                    JsonRpc = "2.0",
                    Error = new McpError
                    {
                        Code = -32603,
                        Message = $"Internal error: {ex.Message}"
                    }
                };
                var errJson = JsonSerializer.Serialize(errResponse, _jsonOptions);
                await writer.WriteLineAsync(errJson);
            }
        }

        _logger.LogInformation("MCP Server stdio loop terminated.");
    }

    private void HandleNotification(McpMessage message)
    {
        if (message.Method == "notifications/initialized")
        {
            _logger.LogInformation("MCP client initialized successfully.");
        }
    }

    private async Task<McpMessage> HandleRequestAsync(McpMessage request, CancellationToken cancellationToken)
    {
        var response = new McpMessage
        {
            JsonRpc = "2.0",
            Id = request.Id
        };

        switch (request.Method)
        {
            case "initialize":
                response.Result = new McpInitializeResult
                {
                    ProtocolVersion = "2024-11-05",
                    Capabilities = new McpServerCapabilities(),
                    ServerInfo = new McpImplementation
                    {
                        Name = "kicad-component-explorer",
                        Version = "1.0.0"
                    }
                };
                break;

            case "ping":
                response.Result = new Dictionary<string, object>();
                break;

            case "tools/list":
                response.Result = new
                {
                    tools = GetRegisteredTools()
                };
                break;

            case "tools/call":
                response.Result = await HandleToolCallAsync(request.Params, cancellationToken);
                break;

            default:
                response.Error = new McpError
                {
                    Code = -32601,
                    Message = $"Method not found: '{request.Method}'"
                };
                break;
        }

        return response;
    }

    private static List<McpToolDefinition> GetRegisteredTools()
    {
        return
        [
            new()
            {
                Name = "search_components",
                Description = "Search electronic components across providers (UltraLibrarian, EasyEDA / LCSC, Octopart, etc.) with real-time stock, pricing, and CAD model availability for KiCad. A result's Attribution, when set, names where its data actually comes from, including when that source is unofficial and may be unreliable; cite it when presenting that result.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["query"] = new
                        {
                            type = "string",
                            description = "Part number, keyword or description to search (e.g. 'STM32F401', 'NE555', '10k resistor 0805')"
                        },
                        ["in_stock_only"] = new
                        {
                            type = "boolean",
                            description = "If true, only returns components currently in stock at suppliers"
                        },
                        ["has_cad_only"] = new
                        {
                            type = "boolean",
                            description = "If true, only returns components that have KiCad symbols, footprints, or 3D models available"
                        },
                        ["max_results"] = new
                        {
                            type = "integer",
                            description = "Maximum number of results to return (default: 20)"
                        }
                    },
                    required = new[] { "query" }
                }
            },
            new()
            {
                Name = "list_providers",
                Description = "List all configured component providers, their status (enabled/disabled), and whether they support direct API search.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>()
                }
            },
            new()
            {
                Name = "get_component_details",
                Description = "Get detailed information about a specific electronic component (datasheet, symbols, footprints, 3D model status, seller stock/prices).",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["query"] = new
                        {
                            type = "string",
                            description = "Exact part number or MPN (e.g. 'C2040', 'STM32F401CEU6')"
                        }
                    },
                    required = new[] { "query" }
                }
            }
        ];
    }

    private async Task<McpToolCallResult> HandleToolCallAsync(JsonElement? paramsElement, CancellationToken cancellationToken)
    {
        if (paramsElement == null || !paramsElement.Value.TryGetProperty("name", out JsonElement nameProp))
        {
            return new McpToolCallResult
            {
                IsError = true,
                Content = [new() { Text = "Missing tool 'name' parameter." }]
            };
        }

        var toolName = nameProp.GetString() ?? string.Empty;
        JsonElement args = paramsElement.Value.TryGetProperty("arguments", out JsonElement argsProp) ? argsProp : default;

        try
        {
            switch (toolName)
            {
                case "search_components":
                    return await ExecuteSearchComponentsAsync(args, cancellationToken);

                case "list_providers":
                    return ExecuteListProviders();

                case "get_component_details":
                    return await ExecuteGetComponentDetailsAsync(args, cancellationToken);

                default:
                    return new McpToolCallResult
                    {
                        IsError = true,
                        Content = [new() { Text = $"Unknown tool: '{toolName}'" }]
                    };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed for {Tool}", toolName);
            return new McpToolCallResult
            {
                IsError = true,
                Content = [new() { Text = $"Error executing tool {toolName}: {ex.Message}" }]
            };
        }
    }

    private async Task<McpToolCallResult> ExecuteSearchComponentsAsync(JsonElement args, CancellationToken cancellationToken)
    {
        if (!args.TryGetProperty("query", out JsonElement queryProp) || string.IsNullOrWhiteSpace(queryProp.GetString()))
        {
            return new McpToolCallResult
            {
                IsError = true,
                Content = [new() { Text = "Missing required argument 'query'." }]
            };
        }

        var query = queryProp.GetString()!.Trim();
        var inStockOnly = args.TryGetProperty("in_stock_only", out JsonElement inStockProp) && inStockProp.GetBoolean();
        var hasCadOnly = args.TryGetProperty("has_cad_only", out JsonElement hasCadProp) && hasCadProp.GetBoolean();
        var maxResults = args.TryGetProperty("max_results", out JsonElement maxResultsProp) ? maxResultsProp.GetInt32() : 20;

        IReadOnlyList<PartSearchResult> results = await _aggregatorService.SearchAllProvidersAsync(query, cancellationToken);

        IEnumerable<PartSearchResult> filtered = results.AsEnumerable();
        if (inStockOnly)
        {
            filtered = filtered.Where(r => (r.Stock ?? 0) > 0);
        }
        if (hasCadOnly)
        {
            filtered = filtered.Where(r => r.HasSymbol || r.HasFootprint || r.Has3DModel);
        }

        var finalResults = filtered.Take(maxResults).ToList();

        var json = JsonSerializer.Serialize(finalResults, new JsonSerializerOptions { WriteIndented = true });
        return new McpToolCallResult
        {
            IsError = false,
            Content =
            [
                new()
                {
                    Text = $"Found {finalResults.Count} component(s) matching '{query}':\n\n{DescribeSources(finalResults)}{json}"
                }
            ]
        };
    }

    // The distinct Attributions ahead of the JSON, so a client reads where the data comes from before
    // the data itself - above all when a source is unofficial and can break without notice (#52) -
    // and, with EasyEDA / LCSC results, that this server never uses JLCPCB's official API (#51).
    private static string DescribeSources(IReadOnlyCollection<PartSearchResult> results)
    {
        // EasyEDA / LCSC results always carry an Attribution, so the note never appears without a list.
        var sources = results.Select(r => r.Attribution).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var note = results.Any(r => r.ProviderId == EasyEdaProvider.ProviderId) ? NoOfficialJlcpcbApiNote + "\n" : string.Empty;
        return sources.Count == 0
            ? string.Empty
            : "Data sources (each result's Attribution says which one applies to it; cite it with the result):\n" +
              string.Concat(sources.Select(source => $"- {source}\n")) + note + "\n";
    }

    private McpToolCallResult ExecuteListProviders()
    {
        var providerList = _providerRegistry.AllProviders.Select(p => new
        {
            id = p.Id,
            displayName = p.DisplayName,
            isEnabled = _configService.IsProviderEnabled(p.Id),
            isDefault = string.Equals(p.Id, _configService.DefaultProviderId, StringComparison.OrdinalIgnoreCase),
            supportsDirectApi = p.SupportsDirectApi,
            searchUrl = p.SearchUrl
        }).ToList();

        var json = JsonSerializer.Serialize(providerList, new JsonSerializerOptions { WriteIndented = true });
        return new McpToolCallResult
        {
            IsError = false,
            Content = [new() { Text = json }]
        };
    }

    private async Task<McpToolCallResult> ExecuteGetComponentDetailsAsync(JsonElement args, CancellationToken cancellationToken)
    {
        if (!args.TryGetProperty("query", out JsonElement queryProp) || string.IsNullOrWhiteSpace(queryProp.GetString()))
        {
            return new McpToolCallResult
            {
                IsError = true,
                Content = [new() { Text = "Missing required argument 'query'." }]
            };
        }

        var query = queryProp.GetString()!.Trim();
        IReadOnlyList<PartSearchResult> results = await _aggregatorService.SearchAllProvidersAsync(query, cancellationToken);

        PartSearchResult? bestMatch = results.FirstOrDefault(r =>
            string.Equals(r.PartNumber, query, StringComparison.OrdinalIgnoreCase)) ?? results.FirstOrDefault();

        if (bestMatch == null)
        {
            return new McpToolCallResult
            {
                IsError = false,
                Content = [new() { Text = $"No component details found for query: '{query}'." }]
            };
        }

        var json = JsonSerializer.Serialize(bestMatch, new JsonSerializerOptions { WriteIndented = true });
        return new McpToolCallResult
        {
            IsError = false,
            Content = [new() { Text = DescribeSources([bestMatch]) + json }]
        };
    }
}
