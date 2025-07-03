namespace Cvolcy.DelicateDust.Models.Task
{
    internal class TaskRequest
    {
        public string TaskId { get; set; }
        public string Payload { get; set; }
        public TaskRequestType Type { get; set; }
        public string CallbackUrl { get; set; }

        public string PartitionKey { get => $"Results-{Type}"; }
    }

    public enum TaskRequestType
    {
        DEBUG = 0,
    }
}