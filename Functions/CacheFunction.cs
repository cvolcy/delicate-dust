using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using Azure.Data.Tables;
using Azure;
using System.Linq;
using System.IO;

namespace Cvolcy.DelicateDust.Functions
{
    public class CacheFunction
    {
        private readonly IConfiguration _config;

        public CacheFunction(
            IConfiguration config)
        {
            _config = config;
        }

        [FunctionName("CacheGet")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "Cache/{partition}/{key}")] HttpRequest req,
            [Table("Cache", Connection = "AzureWebJobsStorage")] TableClient cacheTable, string partition, string key, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            var cacheEntries = cacheTable.QueryAsync<CacheEntryEntity>(x => x.PartitionKey == partition && x.RowKey == key && x.Timestamp > DateTime.UtcNow.AddMinutes(-5)).AsPages();

            await foreach (var page in cacheEntries)
            {
                var entries = page.Values.AsEnumerable().FirstOrDefault();
                return new OkObjectResult(entries);
            }

            return null;
        }

        [FunctionName("CacheCreate")]
        public async Task<IActionResult> RunInsert(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "Cache/{partition}/{key}")] HttpRequest req,
            [Table("Cache", Connection = "AzureWebJobsStorage")] TableClient cacheTable, string partition, string key, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            await cacheTable.CreateIfNotExistsAsync();
            await cacheTable.DeleteEntityAsync(partition, key);
            var sr = new StreamReader(req.Body);
            var res = await cacheTable.AddEntityAsync(new CacheEntryEntity
            {
                PartitionKey = partition,
                RowKey = key,
                Timestamp = DateTime.UtcNow,
                Json = await sr.ReadToEndAsync()
            });

            return new OkObjectResult(res.Status);
        }

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
