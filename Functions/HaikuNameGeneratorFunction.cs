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
    public class HaikuNameGeneratorFunction
    {
        private const string ADJS = "aged ancient autumn billowing bitter black blue bold broken cold cool crimson damp dark dawn delicate divine dry empty falling floral fragrant frosty green hidden holy icy late lingering little lively long misty morning muddy nameless old patient polished proud purple quiet red restless rough shy silent small snowy solitary sparkling spring still summer thrumming twilight wandering weathered white wild winter wispy withered young";
        private const string NOUNS = "bird breeze brook bush butterfly cherry cloud darkness dawn dew dream dust feather field fire firefly flower fog forest frog frost glade glitter grass haze hill lake leaf log meadow moon morning mountain night paper pine pond rain resonance river sea shadow shape silence sky smoke snow snowflake sound star sun sun sunset surf thunder tree violet voice water water waterfall wave wildflower wind";

        [FunctionName("HaikuName")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "Haiku/New")] HttpRequest req, ILogger log)
        {
            log.LogInformation($"C# HTTP trigger function processed a request.");

            var rng = new Random();

            log.LogInformation(JsonSerializer.Serialize(rng.Next(0, ADJS.Length - 1)));

            string adj = Sample(ADJS);
            string noun = Sample(NOUNS);
            int token = rng.Next(0, 9999);

            return new OkObjectResult(new
            {
                adj,
                noun,
                token,
                gen = $"{adj}-{noun}",
                gen2 = $"{adj}-{noun}-{token}"
            });
        }

        private string Sample(string words)
        {
            var rng = new Random();

            var wordList = words.Split(" ");
            var value = wordList[rng.Next(0, wordList.Length)];

            return value;
        }
    }
}
