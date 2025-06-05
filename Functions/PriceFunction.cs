using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Cvolcy.DelicateDust.Models.CMC;

namespace Cvolcy.DelicateDust.Functions
{
    public class PriceFunction(
        ILogger<PriceFunction> _logger,
        IConfiguration _config,
        IHttpClientFactory _httpClientFactory)
    {
        private const string CmcProApiUrlKey = "CMC_PRO_API_URL";
        private const string CmcProApiKey = "CMC_PRO_API_KEY";
        private const string CacheApiPath = "/api/Cache/cache"; // Path to the internal cache function

        /// <summary>
        /// Azure Function to retrieve cryptocurrency prices from CoinMarketCap API,
        /// utilizing an internal cache for efficiency.
        /// </summary>
        /// <param name="req">The HTTP request object.</param>
        /// <param name="slugs">Comma-separated list of cryptocurrency slugs (e.g., "bitcoin,ethereum").</param>
        /// <returns>
        /// An <see cref="IActionResult"/> representing the HTTP response:
        /// - <see cref="OkObjectResult"/> with the serialized price data.
        /// - <see cref="BadRequestObjectResult"/> if input slugs are invalid.
        /// - <see cref="StatusCodeResult"/> (500 Internal Server Error) on unexpected errors,
        ///   or 502 Bad Gateway for external API issues.
        /// </returns>
        [Function("Price")]
        public async Task<IActionResult> GetCryptocurrencyPrices( // Renamed method for clarity
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "Price/{slugs}")] HttpRequest req,
            string slugs)
        {
            _logger.LogInformation("Price: C# HTTP trigger function processed a request for slugs: {Slugs}.", slugs);

            if (string.IsNullOrWhiteSpace(slugs))
            {
                return new BadRequestObjectResult("Slugs parameter cannot be empty.");
            }

            try
            {
                var resultJsonString = await GetOrCreateQuoteAsync(slugs, async () =>
                {
                    var cmcApiUrl = _config[CmcProApiUrlKey];
                    var cmcApiKey = _config[CmcProApiKey];

                    if (string.IsNullOrEmpty(cmcApiUrl) || string.IsNullOrEmpty(cmcApiKey))
                    {
                        _logger.LogError("Price: Missing configuration for CMC API URL or API Key. Check '{CmcProApiUrlKey}' and '{CmcProApiKey}'.", CmcProApiUrlKey, CmcProApiKey);
                        throw new InvalidOperationException("Missing required CoinMarketCap API configuration.");
                    }

                    var uri = $"{cmcApiUrl}/v2/cryptocurrency/quotes/latest?slug={slugs}";
                    _logger.LogInformation("Price: Fetching data from external CoinMarketCap API: {Uri}", uri);

                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.Add("X-CMC_PRO_API_KEY", cmcApiKey);
                    request.Headers.Add("Accept", "application/json");

                    using var httpClient = _httpClientFactory.CreateClient();
                    var response = await httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    _logger.LogInformation("Price: CoinMarketCap API response status: {StatusCode} {ReasonPhrase}", response.StatusCode, response.ReasonPhrase);
                    var responseData = await response.Content.ReadAsStringAsync();

                    var cmcResponse = JsonSerializer.Deserialize<CMCRequestModel<IDictionary<string, CMCCryptoCurrencyModel>>>(
                        responseData,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    return MapResultsToJson(cmcResponse, slugs);
                });

                return new OkObjectResult(resultJsonString);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Price: HTTP request error calling external API or cache: {Message}", ex.Message);
                return new StatusCodeResult(StatusCodes.Status502BadGateway);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Price: JSON deserialization error processing API response or cached data: {Message}", ex.Message);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Price: Configuration or operational error: {Message}", ex.Message);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Price: An unexpected error occurred: {Message}", ex.Message);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Maps the raw CoinMarketCap API response to a standardized list of price and market cap objects,
        /// then serializes it to a JSON string.
        /// </summary>
        /// <param name="responseModel">The deserialized CMC API response model.</param>
        /// <param name="slugs">The original comma-separated slugs string, used for ordering.</param>
        /// <returns>A JSON string representing an array of price and market cap objects.</returns>
        private string MapResultsToJson(CMCRequestModel<IDictionary<string, CMCCryptoCurrencyModel>> responseModel, string slugs)
        {
            var requestedSlugs = slugs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            if (requestedSlugs.Count == 1)
            {
                var usdQuote = responseModel.Data?.Values.FirstOrDefault().Quote.FirstOrDefault(x => x.Key == "USD").Value;
                return JsonSerializer.Serialize(usdQuote.Price, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }

            var mappedResults = new List<object>();
            foreach (var requestedSlug in requestedSlugs)
            {
                var foundEntry = responseModel.Data?.Values
                                    .FirstOrDefault(d => d.Slug.Equals(requestedSlug, StringComparison.OrdinalIgnoreCase));

                if (foundEntry != null && foundEntry.Quote != null && foundEntry.Quote.TryGetValue("USD", out var usdQuote))
                {
                    mappedResults.Add(new
                    {
                        usdQuote.Price,
                        Marketcap = usdQuote.MarketCap
                    });
                }
                else
                {
                    _logger.LogWarning("MapResultsToJson: Could not find data or USD quote for requested slug: {RequestedSlug}", requestedSlug);
                    mappedResults.Add(new { Price = (double?)null, Marketcap = (double?)null });
                }
            }

            return JsonSerializer.Serialize(mappedResults, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        /// <summary>
        /// Fetches data from an internal cache or, if not found/expired,
        /// executes a provided function to get fresh data and updates the cache.
        /// </summary>
        /// <param name="slug">The slug used as a key for the cache entry.</param>
        /// <param name="createFunc">An asynchronous function that fetches and maps the data, returning a JSON string.</param>
        /// <returns>A JSON string containing the cached or newly fetched data.</returns>
        private async Task<string> GetOrCreateQuoteAsync(string slug, Func<Task<string>> createFunc)
        {
            var hostName = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
            var isHttps = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HTTPS"));
            var scheme = isHttps ? "https" : "http";
            var cacheUrl = $"{scheme}://{hostName}{CacheApiPath}/price:{slug}";

            try
            {
                _logger.LogInformation("GetOrCreateQuoteAsync: Attempting to retrieve '{Slug}' from cache at {CacheUrl}", slug, cacheUrl);

                using var httpClient = _httpClientFactory.CreateClient();
                using var cacheGetResponse = await httpClient.GetAsync(cacheUrl);

                if (cacheGetResponse.IsSuccessStatusCode)
                {
                    var cachedResponse = await cacheGetResponse.Content.ReadAsStringAsync();
                    if (!string.IsNullOrWhiteSpace(cachedResponse))
                    {
                        _logger.LogInformation("GetOrCreateQuoteAsync: Cache hit for slug: {Slug}.", slug);
                        return cachedResponse;
                    }
                }
                else if (cacheGetResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("GetOrCreateQuoteAsync: Cache miss (HTTP 404) for slug: {Slug}. Fetching from external API.", slug);
                }
                else
                {
                    _logger.LogWarning("GetOrCreateQuoteAsync: Unexpected HTTP status '{StatusCode}' from cache API for slug: {Slug}. Reason: {ReasonPhrase}", cacheGetResponse.StatusCode, slug, cacheGetResponse.ReasonPhrase);
                }

                _logger.LogInformation("GetOrCreateQuoteAsync: Cache miss or error for '{Slug}'. Calling external data source.", slug);
                var freshValue = await createFunc();

                _logger.LogInformation("GetOrCreateQuoteAsync: Updating cache for '{Slug}' with fresh data.", slug);
                using var content = new StringContent(freshValue, System.Text.Encoding.UTF8, "application/json");
                var cachePostResponse = await httpClient.PostAsync(cacheUrl, content);

                if (!cachePostResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("GetOrCreateQuoteAsync: Failed to update cache for slug: {Slug}. Status: {StatusCode} {ReasonPhrase}", slug, cachePostResponse.StatusCode, cachePostResponse.ReasonPhrase);
                }

                return freshValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetOrCreateQuoteAsync: An error occurred during cache or external API interaction for slug: {Slug}.", slug);
                throw;
            }
        }
    }
}
