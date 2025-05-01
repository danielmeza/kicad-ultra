// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.Hosting;

using UltraLibrarianImporter.KiCadBindings;

internal class ServiceTest : BackgroundService
{

    public const string ClientName = "ConsoleHostedService";
    private readonly IKiCadFactory _factory;

    public ServiceTest(IKiCadFactory factory)
    {
        _factory = factory;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        var kicad = _factory.Create(ClientName);
        //var documents = await kicad.GetOpenDocuments(Kiapi.Common.Types.DocumentType.DoctypeProject);
        //var project = kicad.GetProject(documents[0]);
        //var variables = await kicad.GetOpenDocuments();
        //var test = await kicad.GetPluginSettingsPath("com.github.danielmeza.kicad-ultralibrarian-importer");
        var board = await kicad.GetBoard();
        //foreach (var varaible in variables)
        //{
        //    Console.WriteLine($"{varaible.Key}={varaible.Value}");
        //}

        var project = board.GetProject();
        var test = board.
        var variables = await project.GetTextVariables();
        var documents = kicad.GetOpenDocuments(Kiapi.Common.Types.DocumentType.DoctypeUnknown);
        var kiCadvariables = await kicad.GetTextVariables();
        var result = await project.ExpandTextVariables("${KIPRJMOD}");
        
    }
}