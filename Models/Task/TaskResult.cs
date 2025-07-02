using System;
using Azure;
using Azure.Data.Tables;

namespace Cvolcy.DelicateDust.Models.Task
{
    internal class TaskResult : ITableEntity
    {
        public string PartitionKey { get; set; } = "Result";
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
        public string Output { get; set; }
    }
}