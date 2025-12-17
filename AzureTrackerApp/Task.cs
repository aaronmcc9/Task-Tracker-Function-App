using Azure;
using Azure.Data.Tables;

namespace AzureTrackerApp
{
    public class Task : ITableEntity
    {
        public string PartitionKey { get; set; } = "TaskPartition";
        public string RowKey { get; set; } = string.Empty; // TaskId
        public string Name { get; set; } = string.Empty;
        public TaskStatus Status { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

    public class TaskRequest
    {
        public string Name { get; set; } = string.Empty;
        public DateTime? DueDate { get; set; }
    }
}
