using Azure;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace AzureTrackerApp;

public class TaskFunction
{
    private readonly ILogger<TaskFunction> _logger;

    public TaskFunction(ILogger<TaskFunction> logger)
    {
        _logger = logger;

        var tableStorageAccountUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__tableServiceUri");

        if (string.IsNullOrEmpty(tableStorageAccountUri))
        {
            throw new InvalidOperationException("Table storage URI is not configured.");
        }

        var client = new TableServiceClient(new Uri(tableStorageAccountUri), new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            // only needed if using user assigned managed identity
            ManagedIdentityClientId = Environment.GetEnvironmentVariable("AzureWebJobsStorage__clientId")
        }));
        tableClient = client.GetTableClient("Tasks");
        tableClient.CreateIfNotExists();

        var blobStorageAccountUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");

        if (string.IsNullOrEmpty(tableStorageAccountUri))
        {
            throw new InvalidOperationException("Blobs storage URI is not configured.");
        }

        var blobServiceClient = new BlobServiceClient(new Uri(blobStorageAccountUri), new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            // only needed if using user assigned managed identity
            ManagedIdentityClientId = Environment.GetEnvironmentVariable("AzureWebJobsStorage__clientId")
        }));

        blobContainerClient = blobServiceClient.GetBlobContainerClient("tasks");
        blobContainerClient.CreateIfNotExists();
    }

    [Function("getTask")]
    public async Task<FunctionResult<TaskEntity>> GetTaskAsync([HttpTrigger(AuthorizationLevel.Function, "get", Route = "tasks/{partitionKey}/{rowKey}")] HttpRequestData req,
        string partitionKey, string rowKey)
    {
        var result = new FunctionResult<TaskEntity>();
        try
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogError("PartitionKey or RowKey is null.");
                result.HttpResponse = await CreateResponse(req, HttpStatusCode.BadRequest, "PartitionKey and RowKey are required.");
                return result;
            }

            var task = await FetchTaskEntityAsync(partitionKey, rowKey);
            if (task == null)
            {
                result.HttpResponse = await CreateResponse(req, HttpStatusCode.NotFound, "Task not found.");
                return result;
            }

            result.Data = task;
            result.HttpResponse = await CreateResponse(req, HttpStatusCode.OK, "Task retrieved successfully.", task);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error retrieving task: {ex.Message}");
            result.HttpResponse = await CreateResponse(req, HttpStatusCode.BadRequest, "Error retrieving task.");
        }

        return result;
    }

    [Function("createTask")]
    public async Task<FunctionResult<TaskEntity>> CreateTaskAsync([HttpTrigger(AuthorizationLevel.Function, "post")]
        HttpRequestData req)
    {
        var result = new FunctionResult<TaskEntity>();
        try
        {
            var data = await req.ReadFromJsonAsync<TaskCreate>();

            if (data == null || string.IsNullOrEmpty(data.Name))
            {
                throw new InvalidOperationException("Task name is required.");
            }

            var task = new TaskEntity
            {
                PartitionKey = "TasksPartition",
                RowKey = Guid.NewGuid().ToString(),
                Name = data.Name,
                Status = TaskStatus.ToDo,
                DueDate = data.DueDate
            };

            await tableClient.AddEntityAsync(task);

            result.Data = task;

            //handles queue output binding, see comment in FunctionResult.cs
            result.QueueMessage = JsonSerializer.Serialize(task);
            result.HttpResponse = await CreateResponse(req, HttpStatusCode.Created, "Task has been created and queued!", task);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating task: {ex.Message}");
            result.HttpResponse = await CreateResponse(req, HttpStatusCode.BadRequest, "Error creating task.");
        }

        return result;
    }

    [Function("updateTask")]
    public async Task<FunctionResult<TaskEntity>> UpdateTaskAsync([HttpTrigger(AuthorizationLevel.Function, "put", Route = "tasks/{partitionKey}/{rowKey}")] HttpRequestData  req,
        string partitionKey, string rowKey)
    {
        var result = new FunctionResult<TaskEntity>();

        try
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogError("PartitionKey or RowKey is null.");
                result.HttpResponse = await CreateResponse(req, HttpStatusCode.BadRequest, "PartitionKey and RowKey are required.");
                return result;
            }

            var existingTask = await FetchTaskEntityAsync(partitionKey, rowKey);
            if (existingTask == null)
            {
                result.HttpResponse = await CreateResponse(req, HttpStatusCode.NotFound, "Task not found.");
                return result;
            }

            var data = await req.ReadFromJsonAsync<TaskUpdate>();

            if (data == null)
            {
                result.HttpResponse = await CreateResponse(req, HttpStatusCode.BadRequest, "Invalid task data.");
                return result;
            }

            if (!string.IsNullOrEmpty(data.Name) && existingTask.Name != data.Name)
            {
                existingTask.Name = data.Name;
            }

            if (data.Status.HasValue && existingTask.Status != data.Status.Value)
            {
                existingTask.Status = data.Status.Value;
            }

            if (data.DueDate.HasValue && existingTask.DueDate != data.DueDate.Value)
            {
                existingTask.DueDate = data.DueDate.Value;
            }

            await tableClient.UpdateEntityAsync(existingTask, existingTask.ETag);

            result.Data = existingTask;

            //handles queue output binding, see comment in FunctionResult.cs
            result.QueueMessage = JsonSerializer.Serialize(existingTask);
            result.HttpResponse = await CreateResponse(req, HttpStatusCode.OK, "Task has been updated and Queued!", existingTask);

        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating task: {ex.Message}");
            result.HttpResponse = await CreateResponse(req, HttpStatusCode.BadRequest, "Error updating task.");
        }


        return result;
    }

    [Function("deleteTask")]
    public async Task<HttpResponseData> DeleteTaskAsync([HttpTrigger(AuthorizationLevel.Function, "delete", Route = "tasks/{partitionKey}/{rowKey}")] HttpRequestData  req, string partitionKey, string rowKey)
    {
        try
        {
            if (string.IsNullOrEmpty(partitionKey) || string.IsNullOrEmpty(rowKey))
            {
                _logger.LogError("PartitionKey or RowKey is null.");
                return await CreateResponse(req, HttpStatusCode.BadRequest, "PartitionKey and RowKey are required.");
            }

            var entity = await FetchTaskEntityAsync(partitionKey, rowKey);
            if (entity == null)
            {
                return await CreateResponse(req, HttpStatusCode.NotFound, "Task not found.");
            }

            await tableClient.DeleteEntityAsync(partitionKey, rowKey);

            var blobStorageClient = blobContainerClient.GetBlobClient($"{rowKey}.json");
            await blobStorageClient.DeleteIfExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error deleting task: {ex.Message}");
            return await CreateResponse(req, HttpStatusCode.BadRequest, "Error deleting task.");
        }

        _logger.LogInformation("Task has been deleted.");
        return await CreateResponse(req, HttpStatusCode.OK, "Task has been deleted.");
    }

    private async Task<TaskEntity?> FetchTaskEntityAsync(string partitionKey, string rowKey)
    {
        try
        {
            var entity = await tableClient.GetEntityAsync<TaskEntity>(partitionKey, rowKey);
            return entity.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<HttpResponseData> CreateResponse(HttpRequestData req, HttpStatusCode statusCode, string message, TaskEntity? task = null)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteStringAsync(message); // this is just a message

        if(task != null)
            await response.WriteAsJsonAsync(task); // this is what client receives, no data passed without

        return response;
    }

    #region Members

    private readonly TableClient tableClient;
    private readonly BlobContainerClient blobContainerClient;

    #endregion
}