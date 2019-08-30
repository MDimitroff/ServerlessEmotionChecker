using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Demo
{
    public static class Functions
    {
        private static readonly string SubscriptionKey =
            Environment.GetEnvironmentVariable("SubscriptionKey", EnvironmentVariableTarget.Process);

        private static readonly string FaceApiUrl =
            Environment.GetEnvironmentVariable("FaceApiUrl", EnvironmentVariableTarget.Process);

        private static readonly string StaticFilesPath =
         Environment.GetEnvironmentVariable("StaticFilesPath", EnvironmentVariableTarget.Process);

        [FunctionName("emotions-detector")]
        public static HttpResponseMessage Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            var stream = new FileStream(StaticFilesPath, FileMode.Open);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StreamContent(stream);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");

            return response;
        }

        [FunctionName("upload")]
        public static async Task<HttpResponseMessage> UploadImage(
           [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestMessage request,
           [OrchestrationClient] DurableOrchestrationClient client,
           ILogger log)
        {
            var serializedHttpContent = await (new HttpMessageContent(request).ReadAsByteArrayAsync());
            var instanceId = await client.StartNewAsync("StartOrchestrator", serializedHttpContent);

            return await client.WaitForCompletionOrCreateCheckStatusResponseAsync(request, instanceId, TimeSpan.FromSeconds(30), TimeSpan.Zero); ;
        }

        [FunctionName("StartOrchestrator")]
        public static async Task<string> StartOrchestrator([OrchestrationTrigger] DurableOrchestrationContext context)
        {
            var serializedHttpContent = context.GetInput<byte[]>();

            var imageAsByteArray = await context.CallActivityAsync<byte[]>("GetFilesBytes", serializedHttpContent);
            var result = await context.CallActivityAsync<string>("ReadFaceEmotions", imageAsByteArray);

            return result;
        }

        [FunctionName("GetFilesBytes")]
        public static async Task<byte[]> GetFilesBytes([ActivityTrigger] byte[] serializedHttpContent)
        {
            var tmpRequest = new HttpRequestMessage();
            tmpRequest.Content = new ByteArrayContent(serializedHttpContent);
            tmpRequest.Content.Headers.Add("Content-Type", "application/http;msgtype=request");
            var deserializedHttpRequest = await tmpRequest.Content.ReadAsHttpRequestMessageAsync(); 

            var provider = new MultipartMemoryStreamProvider();
            await deserializedHttpRequest.Content.ReadAsMultipartAsync(provider);
            var file = provider.Contents.First();
            var fileData = await file.ReadAsByteArrayAsync();

            return fileData;
        }

        [FunctionName("ReadFaceEmotions")]
        public static async Task<string> ReadFaceEmotions([ActivityTrigger] byte[] imageAsByteArray)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", SubscriptionKey);

            string requestParameters = "returnFaceId=true&returnFaceLandmarks=false" +
                "&returnFaceAttributes=age,gender,smile,facialHair,glasses,emotion";

            string uri = $"{FaceApiUrl}?{requestParameters}";
            string jsonResult = string.Empty;

            using (var content = new ByteArrayContent(imageAsByteArray))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var response = await client.PostAsync(uri, content);
                jsonResult = await response.Content.ReadAsStringAsync();
            }

            return jsonResult;
        }
    }
}