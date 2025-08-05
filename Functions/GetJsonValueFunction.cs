using System;
using System.Net.Http;
using System.Text.Json; 
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Cvolcy.DelicateDust.Functions
{
    /// <summary>
    /// Azure Function to fetch JSON from a given URL and extract a value based on a provided JSON path.
    /// Supports dot-notation for object properties and array indices.
    /// </summary>
    public class GetJsonValueFunction(
        ILogger<GetJsonValueFunction> _logger,
        IHttpClientFactory _httpClientFactory)
    {
        /// <summary>
        /// HTTP Trigger function to retrieve a specific JSON value from an external URL.
        /// </summary>
        /// <param name="req">The HTTP request object.</param>
        /// <param name="url">The URL of the JSON resource to fetch.</param>
        /// <param name="path">The dot-separated path to the desired JSON value (e.g., "data.items[0].name").</param>
        /// <returns>
        /// An <see cref="IActionResult"/> representing the HTTP response:
        /// - <see cref="OkObjectResult"/> with the extracted JSON value on success.
        /// - <see cref="BadRequestObjectResult"/> if input parameters (url, path) are invalid.
        /// - <see cref="NotFoundResult"/> if the value is not found at the specified path.
        /// - <see cref="StatusCodeResult"/> (502 Bad Gateway) if the external URL cannot be reached or returns an error.
        /// - <see cref="StatusCodeResult"/> (500 Internal Server Error) on JSON parsing errors or other unexpected issues.
        /// </returns>
        [Function("JsonValue")]
        public async Task<IActionResult> GetJsonValue( // Renamed method for clarity
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "GetJsonValue")] HttpRequest req,
            [FromQuery] string url, 
            [FromQuery] string path)
        {
            _logger.LogInformation("GetJsonValue: C# HTTP trigger function processed a request for URL: '{Url}', Path: '{Path}'.", url, path);

            if (string.IsNullOrWhiteSpace(url))
            {
                _logger.LogWarning("GetJsonValue: 'url' parameter is missing or empty.");
                return new BadRequestObjectResult("The 'url' query parameter is required.");
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger.LogWarning("GetJsonValue: 'path' parameter is missing or empty.");
                return new BadRequestObjectResult("The 'path' query parameter is required.");
            }

            try
            {
                using var httpClient = _httpClientFactory.CreateClient();

                _logger.LogInformation("GetJsonValue: Attempting to fetch JSON from URL: '{Url}'.", url);
                string json;
                try
                {
                    json = await httpClient.GetStringAsync(url);
                }
                catch (HttpRequestException httpEx)
                {
                    _logger.LogError(httpEx, "GetJsonValue: HTTP request failed when fetching JSON from '{Url}'. Error: {Message}", url, httpEx.Message);
                    return new StatusCodeResult(StatusCodes.Status502BadGateway); 
                }

                _logger.LogInformation("GetJsonValue: Successfully fetched JSON. Attempting to parse and extract value from path: '{Path}'.", path);
                JsonDocument jsonDoc;
                try
                {
                    jsonDoc = JsonDocument.Parse(json);
                }
                catch (JsonException jsonParseEx)
                {
                    _logger.LogError(jsonParseEx, "GetJsonValue: Failed to parse JSON from '{Url}'. Error: {Message}", url, jsonParseEx.Message);
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }

                JsonElement? resultElement = GetValueFromPath(jsonDoc.RootElement, path);

                if (resultElement.HasValue)
                {
                    _logger.LogInformation("GetJsonValue: Value successfully extracted for path '{Path}'.", path);
                    return new OkObjectResult(resultElement.Value);
                }
                else
                {
                    _logger.LogWarning("GetJsonValue: No value found at path '{Path}' in JSON from '{Url}'.", path, url);
                    return new NotFoundResult();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetJsonValue: An unexpected error occurred. Error: {Message}", ex.Message);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Recursively navigates a <see cref="JsonElement"/> using a dot-separated path.
        /// Supports object properties and array indices.
        /// </summary>
        /// <param name="element">The current <see cref="JsonElement"/> to navigate from.</param>
        /// <param name="path">The dot-separated path string (e.g., "prop.subprop[0].value").</param>
        /// <returns>The <see cref="JsonElement"/> found at the end of the path, or null if not found.</returns>
        private JsonElement? GetValueFromPath(JsonElement element, string path)
        {
            // Split the path into segments, handling array notation (e.g., "prop[0]" -> "prop", "0")
            // This regex splits by '.' or by '[' followed by digits and ']'
            string[] segments = path.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

            JsonElement currentElement = element;

            foreach (string segment in segments)
            {
                // Handle array access (e.g., "items[0]")
                if (segment.Contains("[") && segment.EndsWith("]"))
                {
                    var arraySegmentParts = segment.Split(new char[] { '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
                    if (arraySegmentParts.Length == 2 && int.TryParse(arraySegmentParts[1], out int index))
                    {
                        string propertyName = arraySegmentParts[0];

                        if (currentElement.ValueKind == JsonValueKind.Object && currentElement.TryGetProperty(propertyName, out var arrayPropElement))
                        {
                            currentElement = arrayPropElement;
                        }
                        else
                        {
                            _logger.LogDebug("GetValueFromPath: Property '{PropertyName}' not found or not an object for array access in path segment '{Segment}'.", propertyName, segment);
                            return null;
                        }

                        if (currentElement.ValueKind == JsonValueKind.Array && index >= 0 && index < currentElement.GetArrayLength())
                        {
                            currentElement = currentElement[index];
                        }
                        else
                        {
                            _logger.LogDebug("GetValueFromPath: Array index '{Index}' out of bounds or element is not an array for path segment '{Segment}'.", index, segment);
                            return null;
                        }
                    }
                    else
                    {
                        _logger.LogDebug("GetValueFromPath: Invalid array path segment format: '{Segment}'.", segment);
                        return null;
                    }
                }
                else if (currentElement.ValueKind == JsonValueKind.Object && currentElement.TryGetProperty(segment, out var prop))
                {
                    currentElement = prop;
                }
                else
                {
                    _logger.LogDebug("GetValueFromPath: Property '{Segment}' not found or element is not an object.", segment);
                    return null;
                }
            }

            return currentElement;
        }
    }
}
