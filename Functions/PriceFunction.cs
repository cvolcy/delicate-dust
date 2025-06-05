using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using Cvolcy.DelicateDust.Models.CMC;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System;
using Microsoft.Azure.Functions.Worker;
using System.Net.Http.Json;

namespace Cvolcy.DelicateDust.Functions
{
    public class PriceFunction
    {
        private readonly ILogger<PriceFunction> log;
        private readonly IConfiguration _config;
        private readonly HttpClient _httpClient;

        public PriceFunction(
            ILogger<PriceFunction> log,
            IConfiguration config,
            HttpClient httpClient)
        {
            this.log = log;
            _config = config;
            _httpClient = httpClient;
        }

        [Function ("Price")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "Price/{slugs}")] HttpRequest req,
            string slugs)
        {
            log.LogInformation($"C# HTTP trigger function processed a request.{Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME")} - {Environment.GetEnvironmentVariable("HTTPS")}");

            var value = await GetOrCreateQuoteAsync(slugs, log, async () =>
            {
                var uri = $"{_config["CMC_PRO_API_URL"]}/v2/cryptocurrency/quotes/latest?slug={slugs}";

                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Add("X-CMC_PRO_API_KEY", _config["CMC_PRO_API_KEY"]);
                request.Headers.Add("Accepts", "application/json");

                var response = await _httpClient.SendAsync(request);
                log.LogInformation($"{response.StatusCode.ToString()} {response.ReasonPhrase}");
                var responseData = await response.Content.ReadAsStringAsync();
                var cmcResponse = JsonSerializer.Deserialize<CMCRequestModel<IDictionary<string, CMCCryptoCurrencyModel>>>(responseData);

                return MapResults(cmcResponse, slugs);
            });

            return new OkObjectResult(value);
        }

        private string MapResults(CMCRequestModel<IDictionary<string, CMCCryptoCurrencyModel>> responseModel, string slugs)
        {
            var orderBy = slugs.Split(',').ToList();
            var currencies = responseModel.Data
                                .OrderBy(x => orderBy.IndexOf(x.Value.Slug))
                                .Select(x =>
                                    x.Value.Quote
                                        .Where(x => x.Key == "USD")
                                        .Select(x => new double[] { x.Value.Price, x.Value.MarketCap })
                                        .FirstOrDefault()
            );

            if (orderBy.Count > 1)
            {
                return JsonSerializer.Serialize(currencies.Select(x => new {
                    Price = x[0],
                    Marketcap = x[1]
                }).ToArray());
            }

            return currencies.ElementAt(0)[0].ToString();
        }

        private async Task<string> GetOrCreateQuoteAsync(string slug, ILogger log, Func<Task<string>> createFunc)
        {
            var baseUrl = $"http{(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HTTPS")) ? "" : "s")}://{Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME")}/api";

            var resp = await _httpClient.GetAsync($"{baseUrl}/Cache/cache/price:{slug}");
            var obj = await resp.Content.ReadFromJsonAsync<reponseJSON>();

            if (obj != null) return obj.Json.ToString();

            log.LogInformation("Fetching info from cryptocurrency api");
            var value = await createFunc();
            var content = new StringContent(value);
            log.LogInformation($"Updating Cache for {slug} with {value}");
            await _httpClient.PostAsync($"{baseUrl}/Cache/cache/price:{slug}", content);

            return value;
        }

        private class reponseJSON
        {
            public string Json { get; set; }
        }
    }
}
