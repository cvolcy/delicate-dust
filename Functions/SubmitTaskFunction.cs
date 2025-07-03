using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Cvolcy.DelicateDust.Models.Task;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Cvolcy.DelicateDust.Functions
{
    public class SubmitTaskFunction(
        ILogger<SubmitTaskFunction> logger,
        IHttpClientFactory httpClientFactory,
        QueueServiceClient queueServiceClient,
        TableServiceClient tableServiceClient)
    {
        private readonly ILogger<SubmitTaskFunction> _logger = logger;
        private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
        private readonly QueueServiceClient _queueServiceClient = queueServiceClient;
        private readonly TableServiceClient _tableServiceClient = tableServiceClient;

        [Function("GetTask")]
        public async Task<IActionResult> GetTask(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "Tasks/Get/{type}/{taskId}")] HttpRequest req,
            string taskId,
            TaskRequestType type,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("GetTask: C# HTTP trigger function processed a request.");

            var tasksTable = _tableServiceClient.GetTableClient("tasks");
            _ = await tasksTable.CreateIfNotExistsAsync();

            var taskResult = await tasksTable.GetEntityIfExistsAsync<TaskResult>($"Results-{type}", taskId);

            if (taskResult.HasValue)
            {
                return new OkObjectResult(new
                {
                    taskId = taskResult.Value.RowKey,
                    type,
                    status = "processed"
                });
            }

            var tasksQueue = _queueServiceClient.GetQueueClient("tasks");
            _ = await tasksQueue.CreateIfNotExistsAsync();

            var messages = await tasksQueue.PeekMessagesAsync(maxMessages: 100, cancellationToken: cancellationToken);

            foreach (var message in messages.Value)
            {
                var taskRequest = JsonSerializer.Deserialize<TaskRequest>(message.Body);

                if (taskRequest.TaskId == taskId)
                    return new OkObjectResult(new
                    {
                        taskId = taskRequest.TaskId,
                        type = taskRequest.Type,
                        status = "queued"
                    });
            }

            return new NotFoundResult();
        }

        [Function("SubmitTask")]
        public async Task<IActionResult> SubmitTask(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "Tasks/Submit")] HttpRequest req)
        {
            _logger.LogInformation("SubmitTask: C# HTTP trigger function processed a request.");

            var tasksQueue = _queueServiceClient.GetQueueClient("tasks");
            var tasksTable = _tableServiceClient.GetTableClient("tasks");

            _ = tasksQueue.CreateIfNotExistsAsync();
            _ = tasksTable.CreateIfNotExistsAsync();

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var taskRequest = JsonSerializer.Deserialize<TaskRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            taskRequest.TaskId = Guid.NewGuid().ToString();

            var queueMessage = JsonSerializer.Serialize(taskRequest);
            await tasksQueue.SendMessageAsync(queueMessage);

            var stopWatch = Stopwatch.StartNew();
            while (stopWatch.Elapsed < TimeSpan.FromSeconds(5))
            {
                var result = await tasksTable.GetEntityIfExistsAsync<TaskResult>(taskRequest.PartitionKey, taskRequest.TaskId);

                if (result.HasValue && result.Value is TaskResult taskResult)
                {
                    return new OkObjectResult(new
                    {
                        taskId = taskRequest.TaskId,
                        result = taskResult.Output
                    });
                }

                await Task.Delay(500);

            }

            return new AcceptedResult
            {
                Value = new
                {
                    message = "Task is processing",
                    taskId = taskRequest.TaskId
                }
            };
        }

        [Function("ProcessTaskFunction")]
        public async Task RunProcessQueue(
            [QueueTrigger("tasks", Connection = "AzureWebJobsStorage")] QueueMessage message)
        {
            _logger.LogInformation("RunProcessQueue: C# Queue trigger function starts processing task.");
            var request = JsonSerializer.Deserialize<TaskRequest>(message.Body);

            // Fake long process
            var delayInSeconds = new Random().Next(1, 10);
            await Task.Delay(TimeSpan.FromSeconds(delayInSeconds));
            var result = new
            {
                delay = delayInSeconds.ToString()
            };

            var entity = new TaskResult
            {
                PartitionKey = $"Results-{request.Type}",
                RowKey = request.TaskId,
                Timestamp = DateTimeOffset.UtcNow,
                Output = JsonSerializer.Serialize(result)
            };

            var tasksTable = _tableServiceClient.GetTableClient("tasks");
            await tasksTable.CreateIfNotExistsAsync();

            await tasksTable.UpsertEntityAsync(entity, TableUpdateMode.Replace);

            if (!string.IsNullOrWhiteSpace(request.CallbackUrl))
            {
                using var client = _httpClientFactory.CreateClient();

                var payload = new
                {
                    taskId = request.TaskId,
                    result
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                try
                {
                    await client.PostAsync(request.CallbackUrl, content);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while calling task callback url {callback}", request.CallbackUrl);
                }
            }
        }
    }
}