using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using Cvolcy.DelicateDust.Models.CMC;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System;

namespace Cvolcy.DelicateDust.Functions
{
    public class PriceFunction
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _httpClient;

        public PriceFunction(
            IConfiguration config,
            HttpClient httpClient)
        {
            _config = config;
            _httpClient = httpClient;
        }

        [FunctionName("Price")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "Price/{slugs}")] HttpRequest req,
            string slugs, ILogger log)
        {
            log.LogInformation($"C# HTTP trigger function processed a request.{Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME")} - {Environment.GetEnvironmentVariable("HTTPS")}");

            var value = await GetOrCreateQuoteAsync(slugs, log, async () =>
            {
                var uri = $"{_config["CMC_PRO_API_URL"]}/v2/cryptocurrency/quotes/latest?slug={slugs}";

                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Add("X-CMC_PRO_API_KEY", _config["CMC_PRO_API_KEY"]);
                request.Headers.Add("Accepts", "application/json");

                var response = await (await _httpClient.SendAsync(request)).Content.ReadAsStringAsync();
                var cmcResponse = JsonSerializer.Deserialize<CMCRequestModel<IDictionary<string, CMCCryptoCurrencyModel>>>(response);

                return MapResults(cmcResponse);
            });

            return new OkObjectResult(value);
        }

        private string MapResults(CMCRequestModel<IDictionary<string, CMCCryptoCurrencyModel>> responseModel)
        {
            var currencies = responseModel.Data
                                .Select(x =>
                                    x.Value.Quote
                                        .Where(x => x.Key == "USD")
                                        .Select(x => x.Value.Price)
                                        .FirstOrDefault()
            );

            var result = string.Join(',', currencies);

            return result;
        }

        private async Task<string> GetOrCreateQuoteAsync(string slug, ILogger log, Func<Task<string>> createFunc)
        {
            var baseUrl = $"http{(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HTTPS")) ? "" : "s")}://{Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME")}/api";

            var resp = await _httpClient.GetAsync($"{baseUrl}/Cache/cache/price:{slug}");
            var obj = await resp.Content.ReadAsAsync<reponseJSON>();

            if (obj != null) return obj.Json.ToString();

            log.LogInformation("Fetching info from cryptocurrency api");
            var value = await createFunc();
            var content = new StringContent(value);
            await _httpClient.PostAsync($"{baseUrl}/Cache/cache/price:{slug}", content);

            return value;
        }

        private class reponseJSON
        {
            public string Json { get; set; }
        }
    }
}
