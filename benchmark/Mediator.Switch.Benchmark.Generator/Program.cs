using System.CommandLine;
using System.Text;

namespace Mediator.Switch.Benchmark.Generator;

public static class Program
{
    private const string TargetNamespace = "Mediator.Switch.Benchmark.Generated";
    private const int NotificationsPerRequestRatio = 5;
    private const int HandlersPerNotification = 3;

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Generates parallel C# code for MediatR and SwitchMediator benchmarks.");

        var numberOption = new Option<int>(
            name: "--number",
            description: "Number of parallel request/response/handler types to generate.",
            getDefaultValue: () => 100);
        numberOption.AddAlias("-n");

        var behaviorCountOption = new Option<int>(
            name: "--behaviors",
            description: "Number of open behaviors to generate per mediator.",
            getDefaultValue: () => 3); // Default to 3 behaviors
        behaviorCountOption.AddAlias("-b");

        var outputOption = new Option<DirectoryInfo>(
            name: "--output",
            description: "Output directory for generated C# files.",
            getDefaultValue: () => new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "GeneratedCode")));
        outputOption.AddAlias("-o");

        rootCommand.AddOption(numberOption);
        rootCommand.AddOption(behaviorCountOption);
        rootCommand.AddOption(outputOption);

        rootCommand.SetHandler(GenerateBenchmarkCode, numberOption, behaviorCountOption, outputOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static void GenerateBenchmarkCode(int numberOfRequests, int behaviorCount, DirectoryInfo outputDir)
    {
        Console.WriteLine($"Generating {numberOfRequests} parallel requests/handlers...");
        Console.WriteLine($"Output directory: {outputDir.FullName}");

        if (outputDir.Exists) {
            Console.WriteLine("Output directory exists. Clearing contents...");
            try {
                foreach (var file in outputDir.GetFiles("*.cs")) file.Delete();
                foreach (var dir in outputDir.GetDirectories()) dir.Delete(true);
            } catch (Exception ex) {
                Console.Error.WriteLine($"Error clearing output directory: {ex.Message}");
            }
        }
        outputDir.Create();

        // Generate combined files for simplicity, clearly separating types by name
        GenerateRequestsAndResponses(numberOfRequests, outputDir.FullName);
        GenerateRequestHandlers(numberOfRequests, outputDir.FullName);
        GenerateNotificationsAndHandlers(numberOfRequests, outputDir.FullName);
        GenerateOpenBehaviors(behaviorCount, outputDir.FullName);

        Console.WriteLine("Generation complete.");
    }

    private static void GenerateRequestsAndResponses(int count, string outputDir)
    {
        var sb = new StringBuilder();
        AddFileHeader(sb);

        for (int i = 1; i <= count; i++)
        {
            // MediatR Types
            sb.AppendLine($"public class Ping{i}Request_MediatR : MediatR.IRequest<Pong{i}Response_MediatR> {{ public int Id {{ get; set; }} }}");
            sb.AppendLine($"public class Pong{i}Response_MediatR {{ public int Id {{ get; set; }} }}");
            sb.AppendLine();
            // SwitchMediator Types
            sb.AppendLine($"public class Ping{i}Request_Switch : global::Mediator.Switch.IRequest<Pong{i}Response_Switch> {{ public int Id {{ get; set; }} }}");
            sb.AppendLine($"public class Pong{i}Response_Switch {{ public int Id {{ get; set; }} }}");
            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(outputDir, "GeneratedRequestsAndResponses.cs"), sb.ToString());
        Console.WriteLine("Generated GeneratedRequestsAndResponses.cs");
    }

    private static void GenerateRequestHandlers(int count, string outputDir)
    {
        var sb = new StringBuilder();
        AddFileHeader(sb);

        for (int i = 1; i <= count; i++)
        {
            // MediatR Handler
            sb.AppendLine($"public class Ping{i}RequestHandler_MediatR : MediatR.IRequestHandler<Ping{i}Request_MediatR, Pong{i}Response_MediatR>");
            sb.AppendLine("{");
            sb.AppendLine($"    public Task<Pong{i}Response_MediatR> Handle(Ping{i}Request_MediatR request, CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine($"        return Task.FromResult(new Pong{i}Response_MediatR {{ Id = request.Id }});");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            // SwitchMediator Handler
            sb.AppendLine($"public class Ping{i}RequestHandler_Switch : global::Mediator.Switch.IRequestHandler<Ping{i}Request_Switch, Pong{i}Response_Switch>");
            sb.AppendLine("{");
            sb.AppendLine($"    public Task<Pong{i}Response_Switch> Handle(Ping{i}Request_Switch request, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine($"        return Task.FromResult(new Pong{i}Response_Switch {{ Id = request.Id }});");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(outputDir, "GeneratedRequestHandlers.cs"), sb.ToString());
        Console.WriteLine("Generated GeneratedRequestHandlers.cs");
    }

    private static void GenerateNotificationsAndHandlers(int requestCount, string outputDir)
    {
        var sbNotifications = new StringBuilder();
        var sbHandlers = new StringBuilder();

        AddFileHeader(sbNotifications);
        AddFileHeader(sbHandlers);

        int notificationCount = Math.Max(1, requestCount / NotificationsPerRequestRatio);
        int totalMediatRHandlers = 0;
        int totalSwitchHandlers = 0;

        for (int i = 1; i <= notificationCount; i++)
        {
            // MediatR Notification
            sbNotifications.AppendLine($"public class Notify{i}Event_MediatR : MediatR.INotification {{ public string Message {{ get; set; }} = \"Event {i}\"; }}");
            // SwitchMediator Notification
            sbNotifications.AppendLine($"public class Notify{i}Event_Switch : global::Mediator.Switch.INotification {{ public string Message {{ get; set; }} = \"Event {i}\"; }}");
            sbNotifications.AppendLine();

            // Handlers
            for (int j = 1; j <= HandlersPerNotification; j++)
            {
                // MediatR Handler
                sbHandlers.AppendLine($"public class Notify{i}EventHandler{j}_MediatR : MediatR.INotificationHandler<Notify{i}Event_MediatR>");
                sbHandlers.AppendLine("{");
                sbHandlers.AppendLine($"    public Task Handle(Notify{i}Event_MediatR notification, CancellationToken cancellationToken)");
                sbHandlers.AppendLine("    {");
                sbHandlers.AppendLine("        return Task.CompletedTask;");
                sbHandlers.AppendLine("    }");
                sbHandlers.AppendLine("}");
                sbHandlers.AppendLine();
                totalMediatRHandlers++;

                // SwitchMediator Handler
                sbHandlers.AppendLine($"public class Notify{i}EventHandler{j}_Switch : global::Mediator.Switch.INotificationHandler<Notify{i}Event_Switch>");
                sbHandlers.AppendLine("{");
                sbHandlers.AppendLine($"    public Task Handle(Notify{i}Event_Switch notification, CancellationToken cancellationToken = default)");
                sbHandlers.AppendLine("    {");
                sbHandlers.AppendLine("        return Task.CompletedTask;");
                sbHandlers.AppendLine("    }");
                sbHandlers.AppendLine("}");
                sbHandlers.AppendLine();
                totalSwitchHandlers++;
            }
        }

        File.WriteAllText(Path.Combine(outputDir, "GeneratedNotifications.cs"), sbNotifications.ToString());
        File.WriteAllText(Path.Combine(outputDir, "GeneratedNotificationHandlers.cs"), sbHandlers.ToString());
        Console.WriteLine($"Generated GeneratedNotifications.cs and GeneratedNotificationHandlers.cs ({totalMediatRHandlers} MediatR, {totalSwitchHandlers} Switch handlers)");
    }

    private static void AddFileHeader(StringBuilder sb)
    {
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {TargetNamespace};");
        sb.AppendLine();
        // Add both using statements
        sb.AppendLine("using MediatR;");
        sb.AppendLine("using Mediator.Switch;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
    }

    private static void GenerateOpenBehaviors(int behaviorCount, string outputDir)
    {
        var sb = new StringBuilder();
        AddFileHeader(sb);

        for (int i = 1; i <= behaviorCount; i++)
        {
            // MediatR Behavior
            sb.AppendLine($"public class OpenBehavior{i}_MediatR<TRequest, TResponse> : MediatR.IPipelineBehavior<TRequest, TResponse> where TRequest : notnull");
            sb.AppendLine("{");
            sb.AppendLine("    public async Task<TResponse> Handle(TRequest request, MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine("        // Simulate minimal behavior work (e.g., logging or telemetry)");
            sb.AppendLine("        await Task.Yield();");
            sb.AppendLine("        var response = await next();");
            sb.AppendLine("        return response;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();

            // SwitchMediator Behavior
            sb.AppendLine($"public class OpenBehavior{i}_Switch<TRequest, TResponse> : global::Mediator.Switch.IPipelineBehavior<TRequest, TResponse> where TRequest : notnull");
            sb.AppendLine("{");
            sb.AppendLine("    public async Task<TResponse> Handle(TRequest request, global::Mediator.Switch.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        // Simulate minimal behavior work (e.g., logging or telemetry)");
            sb.AppendLine("        await Task.Yield();");
            sb.AppendLine("        var response = await next();");
            sb.AppendLine("        return response;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(outputDir, "GeneratedOpenBehaviors.cs"), sb.ToString());
        Console.WriteLine($"Generated GeneratedOpenBehaviors.cs with {behaviorCount} behaviors per mediator");
    }
}