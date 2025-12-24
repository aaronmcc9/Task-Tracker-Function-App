using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using System.Text.Json;
using Azure.Storage.Queues;

namespace AzureTrackerApp;

public class TaskFunction
{
    private readonly ILogger<TaskFunction> _logger;

    public TaskFunction(ILogger<TaskFunction> logger)
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
    public async Task<IActionResult> CreateTaskAsync([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
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

        queueClient.SendMessage(JsonSerializer.Serialize(task));

        return new OkObjectResult("Task has been created and Queued!");
    }

    [Function("updateTask")]
    public async Task<IActionResult> UpdateTaskAsync([HttpTrigger(AuthorizationLevel.Function, "put")] HttpRequest req)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var data = JsonSerializer.Deserialize<TaskUpdate>(body);
        if (data == null)
        {
            _logger.LogWarning("Request body is empty.");
            throw new InvalidOperationException("Request body is empty.");
        }

        if(string.IsNullOrEmpty(data.Name) && data.Status == null && data.DueDate == null)
        {
            _logger.LogWarning("No fields to update.");
            return new BadRequestObjectResult("No fields provided to update.");
        }


        var storageUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__tableServiceUri");
        var tableClient = new TableClient(storageUri, "Tasks");

        var existingTask = await tableClient.GetEntityAsync<Task>(data.PartitionKey, data.RowKey);

        if(!string.IsNullOrEmpty(data.Name))
        {
            existingTask.Value.Name = data.Name;
        }   

        if(data.Status != null)
        {
            existingTask.Value.Status = data.Status.Value;
        }   

        if(data.DueDate != null)
        {
            existingTask.Value.DueDate = data.DueDate;
        }   

        await tableClient.UpdateEntityAsync(existingTask.Value, existingTask.Value.ETag);

        var queueStorageUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__queueServiceUri");
        var queueClient = new QueueClient(queueStorageUri, "task-queue");

        await queueClient.SendMessageAsync(JsonSerializer.Serialize(existingTask.Value));

        return new OkObjectResult("Task has been updated and Queued!");

    }

    [Function("deleteTask")]
    public async Task<IActionResult> DeleteTaskAsync([HttpTrigger(AuthorizationLevel.Function, "delete")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}