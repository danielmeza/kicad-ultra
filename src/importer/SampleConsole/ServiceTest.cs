// See https://aka.ms/new-console-template for more information

using KiCadSharp;
using Microsoft.Extensions.Hosting;

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

        KiCad kicad = _factory.Create(ClientName);
        //var documents = await kicad.GetOpenDocuments(Kiapi.Common.Types.DocumentType.DoctypeProject);
        //var project = kicad.GetProject(documents[0]);
        //var variables = await kicad.GetOpenDocuments();
        //var test = await kicad.GetPluginSettingsPath("com.github.danielmeza.kicad-ultralibrarian-importer");
        Board board = await kicad.GetBoard();
        //foreach (var varaible in variables)
        //{
        //    Console.WriteLine($"{varaible.Key}={varaible.Value}");
        //}

        Project project = board.GetProject();
        _ = await project.GetTextVariables();
        _ = kicad.GetOpenDocuments(Kiapi.Common.Types.DocumentType.DoctypeUnknown);
        _ = await kicad.GetTextVariables();
        _ = await project.ExpandTextVariables("${KIPRJMOD}");

    }
}
