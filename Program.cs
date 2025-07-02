using System.Collections;
using Azure.AI.Inference;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddAzureClients(builder =>
        {
            builder.AddQueueServiceClient(context.Configuration.GetSection("AzureWebJobsStorage"))
                .ConfigureOptions(c => c.MessageEncoding = QueueMessageEncoding.Base64);
            builder.AddTableServiceClient(context.Configuration.GetSection("AzureWebJobsStorage"));
        });

        services.AddHttpClient();
        services.AddChatClient(new ChatCompletionsClient(
            endpoint: new System.Uri("https://models.inference.ai.azure.com"),
            new Azure.AzureKeyCredential(context.Configuration.GetValue<string>("GITHUB_INFERENCE_TOKEN"))
        ).AsIChatClient("gpt-4o-mini"));
    })
    .Build();

host.Run();