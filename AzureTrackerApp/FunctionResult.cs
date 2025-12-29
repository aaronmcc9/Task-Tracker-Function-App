using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace AzureTrackerApp
{
    public class FunctionResult<T>
    {
        public HttpResponseData HttpResponse { get; set; } = default!;

        /// <summary>
        /// this is used to output messages to a queue
        /// usually it would be above method but since we are using this class the attribute goes here
        /// if it was set on the method the queue output would be tied to the method not the result
        /// for the method you would need to use ICollector or IAsyncCollector and add messages to it manually
        /// </summary>
        [QueueOutput("task-queue", Connection = "AzureWebJobsStorage")]
        public string? QueueMessage { get; set; }

        public T? Data { get; set; }
    }
}
