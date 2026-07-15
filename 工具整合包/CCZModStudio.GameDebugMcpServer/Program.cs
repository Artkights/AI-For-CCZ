using System.Text;
using CCZModStudio.GameDebugMcpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

if (args.Contains("--effect-validation-host", StringComparer.OrdinalIgnoreCase))
{
    static string RequireArgument(string[] values, string name)
    {
        var index = Array.FindIndex(values, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= values.Length || string.IsNullOrWhiteSpace(values[index + 1]))
            throw new ArgumentException("缺少验证宿主参数：" + name);
        return values[index + 1];
    }

    var pipe = RequireArgument(args, "--pipe");
    var token = RequireArgument(args, "--token");
    var parent = int.Parse(RequireArgument(args, "--parent-pid"), System.Globalization.CultureInfo.InvariantCulture);
    await new EffectValidationPipeHost(new GameDebugRuntime(), pipe, token, parent).RunAsync();
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddSingleton<GameDebugRuntime>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
