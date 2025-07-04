using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
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
    /// <summary>
    /// Represents the main function class for submitting, retrieving, and processing tasks.
    /// It uses Azure Queue Storage for task submission and Azure Table Storage for results.
    /// </summary>
    public class SubmitTaskFunction
    {
        private readonly ILogger<SubmitTaskFunction> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly TableServiceClient _tableServiceClient;

        // Constants for Azure Storage names
        private const string TasksQueueName = "tasks";
        private const string TasksTableName = "tasks";

        public SubmitTaskFunction(
            ILogger<SubmitTaskFunction> logger,
            IHttpClientFactory httpClientFactory,
            QueueServiceClient queueServiceClient,
            TableServiceClient tableServiceClient)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _queueServiceClient = queueServiceClient;
            _tableServiceClient = tableServiceClient;
        }

        /// <summary>
        /// HTTP Trigger function to get the status or result of a submitted task.
        /// It first checks Table Storage for a completed result, then Queue Storage for a pending task.
        /// </summary>
        /// <param name="req">The HTTP request.</param>
        /// <param name="taskId">The ID of the task to retrieve.</param>
        /// <param name="type">The type of the task, used for PartitionKey in Table Storage.</param>
        /// <param name="cancellationToken">Cancellation token for async operations.</param>
        /// <returns>
        /// An <see cref="IActionResult"/> indicating the task status (processed, queued) or not found.
        /// </returns>
        [Function("GetTask")]
        public async Task<IActionResult> GetTaskStatus(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "Tasks/Get/{type}/{taskId}")] HttpRequest req,
            string taskId,
            string type,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("GetTaskStatus: C# HTTP trigger function received a request for TaskId: '{TaskId}', Type: '{Type}'.", taskId, type);

            try
            {
                var tasksTableClient = _tableServiceClient.GetTableClient(TasksTableName);
                await tasksTableClient.CreateIfNotExistsAsync(cancellationToken);

                var partitionKey = $"Results-{type}";
                var taskResultResponse = await tasksTableClient.GetEntityIfExistsAsync<TaskResult>(partitionKey, taskId, cancellationToken: cancellationToken);

                if (taskResultResponse.HasValue && taskResultResponse.Value is TaskResult taskResult)
                {

                    if (string.IsNullOrWhiteSpace(taskResult.Output))
                    {
                        _logger.LogInformation("GetTaskStatus: Task '{TaskId}' of type '{Type}' found as 'processed' in Table Storage.", taskId, type);
                        return new OkObjectResult(new
                        {
                            taskId = taskResult.RowKey,
                            type,
                            status = "processing"
                        });
                    }

                    _logger.LogInformation("GetTaskStatus: Task '{TaskId}' of type '{Type}' found as 'processed' in Table Storage.", taskId, type);
                    return new OkObjectResult(new
                    {
                        taskId = taskResult.RowKey,
                        type,
                        status = "processed"
                    });
                }

                var tasksQueueClient = _queueServiceClient.GetQueueClient(TasksQueueName);
                await tasksQueueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

                _logger.LogInformation("GetTaskStatus: Task '{TaskId}' not found in Table Storage. Peeking queue for pending status.", taskId);
                var messages = (await tasksQueueClient.PeekMessagesAsync(maxMessages: 32, cancellationToken: cancellationToken)).Value;

                foreach (var message in messages)
                {
                    try
                    {
                        var taskRequest = JsonSerializer.Deserialize<TaskRequest>(message.Body.ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (taskRequest != null && taskRequest.TaskId == taskId)
                        {
                            _logger.LogInformation("GetTaskStatus: Task '{TaskId}' of type '{Type}' found as 'queued' in Queue Storage.", taskId, type);
                            return new OkObjectResult(new
                            {
                                taskId = taskRequest.TaskId,
                                type = taskRequest.Type,
                                status = "queued"
                            });
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "GetTaskStatus: Failed to deserialize queue message body for message ID '{MessageId}'. Body: '{MessageBody}'.", message.MessageId, message.Body.ToString());
                    }
                }

                _logger.LogInformation("GetTaskStatus: Task '{TaskId}' of type '{Type}' not found in Table or Queue Storage.", taskId, type);
                return new NotFoundResult();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning(ex, "GetTaskStatus: Azure Storage resource not found during task status check for TaskId: '{TaskId}'. Error: {Message}", taskId, ex.Message);
                return new NotFoundResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetTaskStatus: An unexpected error occurred while getting task status for TaskId: '{TaskId}'. Error: {Message}", taskId, ex.Message);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// HTTP Trigger function to submit a new task to the processing queue.
        /// It generates a new TaskId, adds the task to the queue, and then polls
        /// Table Storage for a short period for an immediate result.
        /// </summary>
        /// <param name="req">The HTTP request containing the <see cref="TaskRequest"/> in the body.</param>
        /// <returns>
        /// An <see cref="IActionResult"/> with the immediate result if processed quickly,
        /// or an <see cref="AcceptedResult"/> if the task is queued for asynchronous processing.
        /// </returns>
        [Function("SubmitTask")]
        public async Task<IActionResult> SubmitNewTask(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "Tasks/Submit")] HttpRequest req)
        {
            _logger.LogInformation("SubmitNewTask: C# HTTP trigger function received a request to submit a new task.");

            try
            {
                var tasksQueueClient = _queueServiceClient.GetQueueClient(TasksQueueName);
                var tasksTableClient = _tableServiceClient.GetTableClient(TasksTableName);

                await tasksQueueClient.CreateIfNotExistsAsync();
                await tasksTableClient.CreateIfNotExistsAsync();

                string requestBody;
                using (var reader = new StreamReader(req.Body))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    _logger.LogWarning("SubmitNewTask: Request body is empty.");
                    return new BadRequestObjectResult("Request body cannot be empty.");
                }

                TaskRequest taskRequest;
                try
                {
                    taskRequest = JsonSerializer.Deserialize<TaskRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (taskRequest == null)
                    {
                        _logger.LogWarning("SubmitNewTask: Deserialized task request is null. Request body: '{RequestBody}'", requestBody);
                        return new BadRequestObjectResult("Invalid task request format.");
                    }
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, "SubmitNewTask: Failed to deserialize request body. Error: {Message}. Body: '{RequestBody}'", jsonEx.Message, requestBody);
                    return new BadRequestObjectResult($"Invalid JSON format: {jsonEx.Message}");
                }

                taskRequest.TaskId = Guid.NewGuid().ToString();

                var queueMessage = JsonSerializer.Serialize(taskRequest);
                await tasksQueueClient.SendMessageAsync(queueMessage);
                _logger.LogInformation("SubmitNewTask: Task '{TaskId}' of type '{Type}' submitted to queue.", taskRequest.TaskId, taskRequest.Type);

                var stopWatch = Stopwatch.StartNew();
                var pollingTimeout = TimeSpan.FromSeconds(3);
                var pollingInterval = TimeSpan.FromMilliseconds(500);

                while (stopWatch.Elapsed < pollingTimeout)
                {
                    var resultPartitionKey = $"Results-{taskRequest.Type}";
                    var resultResponse = await tasksTableClient.GetEntityIfExistsAsync<TaskResult>(resultPartitionKey, taskRequest.TaskId);

                    if (resultResponse.HasValue
                        && resultResponse.Value is TaskResult taskResult
                        && !string.IsNullOrWhiteSpace(taskResult.Output))
                    {
                        _logger.LogInformation("SubmitNewTask: Task '{TaskId}' processed immediately. Returning result.", taskRequest.TaskId);
                        return new OkObjectResult(new
                        {
                            taskId = taskRequest.TaskId,
                            result = JsonSerializer.Deserialize<JsonElement>(taskResult.Output)
                        });
                    }

                    await Task.Delay(pollingInterval);
                }

                _logger.LogInformation("SubmitNewTask: Task '{TaskId}' not processed immediately within {TimeoutSeconds} seconds. Returning 'Accepted' status.", taskRequest.TaskId, pollingTimeout.TotalSeconds);
                return new AcceptedResult
                {
                    Value = new
                    {
                        message = "Task is processing asynchronously",
                        taskId = taskRequest.TaskId,
                        statusUrl = $"/api/Tasks/Get/{taskRequest.Type}/{taskRequest.TaskId}"
                    }
                };
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "SubmitNewTask: Azure Storage operation failed. Error: {Message}", ex.Message);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubmitNewTask: An unexpected error occurred while submitting task. Error: {Message}", ex.Message);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Queue Trigger function that processes tasks from the 'tasks' queue.
        /// This function simulates a long-running process and then stores the result in Table Storage.
        /// It also attempts to call a callback URL if provided in the task request.
        /// </summary>
        /// <param name="message">The queue message containing the <see cref="TaskRequest"/>.</param>
        /// <param name="cancellationToken">Cancellation token for async operations.</param>
        [Function("ProcessTaskFunction")]
        public async Task ProcessTaskQueueMessage(
            [QueueTrigger(TasksQueueName, Connection = "AzureWebJobsStorage")] QueueMessage message,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("ProcessTaskQueueMessage: C# Queue trigger function received message ID: '{MessageId}'.", message.MessageId);
            
            var tasksTableClient = _tableServiceClient.GetTableClient(TasksTableName);
            await tasksTableClient.CreateIfNotExistsAsync(cancellationToken);

            TaskRequest request;
            try
            {
                request = JsonSerializer.Deserialize<TaskRequest>(message.Body.ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (request == null)
                {
                    _logger.LogError("ProcessTaskQueueMessage: Deserialized task request is null for message ID '{MessageId}'. Body: '{MessageBody}'", message.MessageId, message.Body.ToString());
                    return;
                }
                

                var entity = new TaskResult
                {
                    PartitionKey = $"Results-{request.Type}",
                    RowKey = request.TaskId,
                    Timestamp = DateTimeOffset.UtcNow
                };
                await tasksTableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "ProcessTaskQueueMessage: Failed to deserialize queue message body for message ID '{MessageId}'. Error: {Message}. Body: '{MessageBody}'", message.MessageId, jsonEx.Message, message.Body.ToString());
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProcessTaskQueueMessage: An unexpected error occurred during message deserialization for message ID '{MessageId}'. Error: {Message}", message.MessageId, ex.Message);
                return;
            }

            _logger.LogInformation("ProcessTaskQueueMessage: Processing task '{TaskId}' of type '{Type}'.", request.TaskId, request.Type);

            try
            {
                var delayInSeconds = new Random().Next(1, 10);
                _logger.LogInformation("ProcessTaskQueueMessage: Simulating task processing for '{TaskId}' with a delay of {Delay} seconds.", request.TaskId, delayInSeconds);
                await Task.Delay(TimeSpan.FromSeconds(delayInSeconds), cancellationToken);

                var resultPayload = new
                {
                    delay = delayInSeconds.ToString(),
                    processedAt = DateTimeOffset.UtcNow
                };
                var resultJson = JsonSerializer.Serialize(resultPayload);

                var entity = new TaskResult
                {
                    PartitionKey = $"Results-{request.Type}",
                    RowKey = request.TaskId,
                    Timestamp = DateTimeOffset.UtcNow,
                    Output = resultJson
                };
                await tasksTableClient.UpsertEntityAsync(entity, TableUpdateMode.Merge, cancellationToken);
                _logger.LogInformation("ProcessTaskQueueMessage: Task '{TaskId}' result saved to Table Storage.", request.TaskId);

                if (!string.IsNullOrWhiteSpace(request.CallbackUrl))
                {
                    _logger.LogInformation("ProcessTaskQueueMessage: Calling callback URL '{CallbackUrl}' for task '{TaskId}'.", request.CallbackUrl, request.TaskId);
                    using var httpClient = _httpClientFactory.CreateClient();
                    var payloadForCallback = new
                    {
                        taskId = request.TaskId,
                        result = resultPayload
                    };

                    var jsonForCallback = JsonSerializer.Serialize(payloadForCallback);
                    using var content = new StringContent(jsonForCallback, Encoding.UTF8, "application/json");

                    try
                    {
                        var callbackResponse = await httpClient.PostAsync(request.CallbackUrl, content, cancellationToken);
                        callbackResponse.EnsureSuccessStatusCode();
                        _logger.LogInformation("ProcessTaskQueueMessage: Callback for task '{TaskId}' successful. Status: {StatusCode}", request.TaskId, callbackResponse.StatusCode);
                    }
                    catch (HttpRequestException httpEx)
                    {
                        _logger.LogError(httpEx, "ProcessTaskQueueMessage: HTTP request error calling task callback URL '{CallbackUrl}' for task '{TaskId}'. Error: {Message}", request.CallbackUrl, request.TaskId, httpEx.Message);
                    }
                    catch (Exception callbackEx)
                    {
                        _logger.LogError(callbackEx, "ProcessTaskQueueMessage: An unexpected error occurred while calling task callback URL '{CallbackUrl}' for task '{TaskId}'. Error: {Message}", request.CallbackUrl, request.TaskId, callbackEx.Message);
                    }
                }

                _logger.LogInformation("ProcessTaskQueueMessage: Task '{TaskId}' processing completed.", request.TaskId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("ProcessTaskQueueMessage: Task '{TaskId}' processing was cancelled.", request.TaskId);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "ProcessTaskQueueMessage: Azure Storage operation failed for task '{TaskId}'. Error: {Message}", request.TaskId, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProcessTaskQueueMessage: An unexpected error occurred during task processing for task '{TaskId}'. Error: {Message}", request.TaskId, ex.Message);
                throw;
            }
        }
    }
}
