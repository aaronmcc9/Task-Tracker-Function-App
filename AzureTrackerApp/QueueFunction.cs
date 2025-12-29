using Azure.Identity;
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

        var blobStorageUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");

        if (string.IsNullOrEmpty(blobStorageUri))
        {
            throw new InvalidOperationException("Blobs storage URI is not configured.");
        }

        var blobServiceClient = new BlobServiceClient(new Uri(blobStorageUri), new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            // only needed if using user assigned managed identity
            ManagedIdentityClientId = Environment.GetEnvironmentVariable("AzureWebJobsStorage__clientId")
        }));

        blobContainerClient = blobServiceClient.GetBlobContainerClient("tasks");
        blobContainerClient.CreateIfNotExists();

    }

    [Function(nameof(QueueFunction))]
    public async Task ProcessTaskQueue([QueueTrigger("task-queue", Connection = "AzureWebJobsStorage")] string message)
    {
        try
        {
            if (message == null || string.IsNullOrWhiteSpace(message))
            {
                _logger.LogWarning("Received null or empty queue task.");
                return;
            }

            var taskMessage = JsonSerializer.Deserialize<TaskEntity>(message);

            if (taskMessage != null)
            {
                _logger.LogInformation($"Processing task: {taskMessage.Name}, Status: {taskMessage.Status}, DueDate: {taskMessage.DueDate}");

                var blobClient = blobContainerClient.GetBlobClient($"{taskMessage.RowKey}.json");
                await blobClient.UploadAsync(BinaryData.FromObjectAsJson(taskMessage), overwrite: true);

                _logger.LogInformation($"Uploaded task: {taskMessage.Name}, Status: {taskMessage.Status}, DueDate: {taskMessage.DueDate}");
            }
            else
            {
                _logger.LogError("Failed to deserialize the queue message to Task object.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error processing queue message: {ex.Message}");
        }
    }

    #region Members

    private readonly BlobContainerClient blobContainerClient;

    #endregion
}