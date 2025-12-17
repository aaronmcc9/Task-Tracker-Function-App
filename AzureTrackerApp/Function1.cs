using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using System.Text.Json;
using Azure.Storage.Queues;

namespace AzureTrackerApp;

public class Function1
{
    private readonly ILogger<Function1> _logger;

    public Function1(ILogger<Function1> logger)
    {
        _logger = logger;
    }

    [Function("getTask")]
    public IActionResult GetTask([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }

    [Function("createTask")]
    public async Task<IActionResult> CreateTask([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync();
        var data = JsonSerializer.Deserialize<TaskRequest>(body);

        if (data == null || string.IsNullOrEmpty(data.Name))
        {
            throw new InvalidOperationException("Task name is required.");
        }

        var tableStorageAccountUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__tableServiceUri");
        var client = new TableServiceClient(tableStorageAccountUri);
        var table = client.GetTableClient("Tasks");

        await table.CreateIfNotExistsAsync();

        var task = new Task
        {
            PartitionKey = "TasksPartition",
            RowKey = Guid.NewGuid().ToString(),
            Name = data.Name,
            Status = TaskStatus.ToDo,
            DueDate = data.DueDate
        };
        await table.AddEntityAsync(task);

        var queueStorageAccountUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__queueServiceUri");
        var queueClient = new QueueClient(queueStorageAccountUri, "task-queue");

        await queueClient.CreateIfNotExistsAsync();

        var queueMessage = new
        {
            taskId = task.RowKey,
            partitionKey = task.PartitionKey,
            action = "process"
        };

        queueClient.SendMessage(JsonSerializer.Serialize(queueMessage));

        return new OkObjectResult("Task has been created and Queued!");
    }

    [Function("updateTask")]
    public IActionResult UpdateTask([HttpTrigger(AuthorizationLevel.Function, "put")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }

    [Function("deleteTask")]
    public IActionResult DeleteTask([HttpTrigger(AuthorizationLevel.Function, "delete")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}