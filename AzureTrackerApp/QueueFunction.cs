using Azure.Storage.Blobs;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AzureTrackerApp;

public class QueueFunction
{
    private readonly ILogger<QueueFunction> _logger;

    public QueueFunction(ILogger<QueueFunction> logger)
    {
        _logger = logger;
    }

    [Function(nameof(QueueFunction))]
    public async System.Threading.Tasks.Task ProcessTaskQueue([QueueTrigger("task-queue", Connection = "AzureWebJobsStorage")] QueueMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.MessageText))
        {
            _logger.LogWarning("Received null or empty queue task.");
            return;
        }

        var taskMessage = JsonSerializer.Deserialize<Task>(message.MessageText);

        if (taskMessage != null)
        {
            _logger.LogInformation($"Processing task: {taskMessage.Name}, Status: {taskMessage.Status}, DueDate: {taskMessage.DueDate}");
            // Add your task processing logic here

            var storageUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");
            var blobClient = new BlobClient(storageUri, "tasks", $"{taskMessage.RowKey}.json");
            await blobClient.UploadAsync(BinaryData.FromObjectAsJson(taskMessage), overwrite: true);

            _logger.LogInformation($"Uploaded task: {taskMessage.Name}, Status: {taskMessage.Status}, DueDate: {taskMessage.DueDate}");
        }
        else
        {
            _logger.LogError("Failed to deserialize the queue message to Task object.");
        }
    }
}