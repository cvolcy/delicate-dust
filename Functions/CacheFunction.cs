using System;
using System.IO;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cvolcy.DelicateDust.Functions
{
    public class CacheFunction(ILogger<CacheFunction> _logger)
    {
        /// <summary>
        /// Azure Function to retrieve a cached entry from Azure Table Storage.
        /// It fetches the entry by PartitionKey and RowKey and checks its freshness.
        /// </summary>
        /// <param name="req">The HTTP request object.</param>
        /// <param name="cacheTable">The Azure TableClient bound by the TableInput attribute.</param>
        /// <param name="partition">The partition key for the cache entry.</param>
        /// <param name="key">The row key for the cache entry.</param>
        /// <returns>
        /// An <see cref="IActionResult"/> representing the HTTP response:
        /// - <see cref="OkObjectResult"/> with the cached JSON if found and fresh.
        /// - <see cref="NotFoundResult"/> if the entry does not exist or is expired.
        /// - <see cref="StatusCodeResult"/> (500 Internal Server Error) on unexpected errors.
        /// </returns>
        [Function("CacheGet")]
        public async Task<IActionResult> GetCacheEntry(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "Cache/{partition}/{key}")] HttpRequest req,
            [TableInput("Cache", Connection = "AzureWebJobsStorage")] TableClient cacheTable,
            string partition,
            string key)
        {
            _logger.LogInformation("CacheGet: C# HTTP trigger function processed a request for PartitionKey: {Partition}, RowKey: {Key}.", partition, key);

            try
            {
                // Use GetEntityAsync for direct lookup by PartitionKey and RowKey, which is more efficient
                // than querying and iterating pages when a single entity is expected.
                Response<CacheEntryEntity> response = await cacheTable.GetEntityAsync<CacheEntryEntity>(partition, key);
                CacheEntryEntity entry = response.Value;

                // Check for freshness (within the last 5 minutes)
                if (entry != null && entry.Timestamp.HasValue && entry.Timestamp.Value > DateTimeOffset.UtcNow.AddMinutes(-5))
                {
                    _logger.LogInformation("CacheGet: Cache entry found and is fresh for PartitionKey: {Partition}, RowKey: {Key}.", partition, key);
                    return new OkObjectResult(entry.Json); // Return the JSON content directly
                }
                else
                {
                    _logger.LogInformation("CacheGet: Cache entry not found or expired for PartitionKey: {Partition}, RowKey: {Key}.", partition, key);
                    return new NotFoundResult(); // Return 404 Not Found if entry is not found or expired
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Entity not found is a common scenario, log it and return 404
                _logger.LogWarning("CacheGet: Cache entry not found for PartitionKey: {Partition}, RowKey: {Key}. Error: {Message}", partition, key, ex.Message);
                return new NotFoundResult();
            }
            catch (Exception ex)
            {
                // Log other unexpected errors
                _logger.LogError(ex, "CacheGet: An error occurred while retrieving cache entry for PartitionKey: {Partition}, RowKey: {Key}.", partition, key);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Azure Function to create or update a cached entry in Azure Table Storage.
        /// It uses UpsertEntityAsync for an efficient insert-or-replace operation.
        /// </summary>
        /// <param name="req">The HTTP request object, containing the JSON payload in the body.</param>
        /// <param name="cacheTable">The Azure TableClient bound by the TableInput attribute.</param>
        /// <param name="partition">The partition key for the cache entry.</param>
        /// <param name="key">The row key for the cache entry.</param>
        /// <returns>
        /// An <see cref="IActionResult"/> representing the HTTP response:
        /// - <see cref="OkResult"/> on successful creation/update.
        /// - <see cref="BadRequestResult"/> if the request body is empty.
        /// - <see cref="StatusCodeResult"/> (500 Internal Server Error) on unexpected errors.
        /// </returns>
        [Function("CacheCreate")]
        public async Task<IActionResult> CreateOrUpdateCacheEntry( // Renamed method for clarity
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "Cache/{partition}/{key}")] HttpRequest req,
            [TableInput("Cache", Connection = "AzureWebJobsStorage")] TableClient cacheTable,
            string partition,
            string key)
        {
            _logger.LogInformation("CacheCreate: C# HTTP trigger function processed a request for PartitionKey: {Partition}, RowKey: {Key}.", partition, key);

            try
            {
                await cacheTable.CreateIfNotExistsAsync();

                string requestBody;
                using (var reader = new StreamReader(req.Body))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    _logger.LogWarning("CacheCreate: Request body is empty for PartitionKey: {Partition}, RowKey: {Key}.", partition, key);
                    return new BadRequestResult(); // Return 400 Bad Request if no content
                }

                var newCacheEntry = new CacheEntryEntity
                {
                    PartitionKey = partition,
                    RowKey = key,
                    Timestamp = DateTimeOffset.UtcNow, // Use DateTimeOffset for Azure Table Storage Timestamp
                    Json = requestBody
                };

                Response response = await cacheTable.UpsertEntityAsync(newCacheEntry, TableUpdateMode.Replace);

                _logger.LogInformation("CacheCreate: Cache entry upserted successfully with status {Status} for PartitionKey: {Partition}, RowKey: {Key}.", response.Status, partition, key);
                return new OkResult(); // Return 200 OK for successful operation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CacheCreate: An error occurred while creating/updating cache entry for PartitionKey: {Partition}, RowKey: {Key}.", partition, key);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Represents a cache entry entity stored in Azure Table Storage.
        /// Implements ITableEntity for automatic serialization/deserialization by Azure.Data.Tables.
        /// </summary>
        private class CacheEntryEntity : ITableEntity
        {
            public string PartitionKey { get; set; }
            public string RowKey { get; set; }
            public DateTimeOffset? Timestamp { get; set; }
            public ETag ETag { get; set; }
            public string Json { get; set; }
        }
    }
}
