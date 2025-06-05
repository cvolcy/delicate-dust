using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Cvolcy.DelicateDust.Functions
{
    public class HaikuNameGeneratorFunction(ILogger<HaikuNameGeneratorFunction> _logger)
    {
        private static readonly string[] ADJS_ARRAY = "aged ancient autumn billowing bitter black blue bold broken cold cool crimson damp dark dawn delicate divine dry empty falling floral fragrant frosty green hidden holy icy late lingering little lively long misty morning muddy nameless old patient polished proud purple quiet red restless rough shy silent small snowy solitary sparkling spring still summer thrumming twilight wandering weathered white wild winter wispy withered young".Split(' ');
        private static readonly string[] NOUNS_ARRAY = "bird breeze brook bush butterfly cherry cloud darkness dawn dew dream dust feather field fire firefly flower fog forest frog frost glade glitter grass haze hill lake leaf log meadow moon morning mountain night paper pine pond rain resonance river sea shadow shape silence sky smoke snow snowflake sound star sun sun sunset surf thunder tree violet voice water water waterfall wave wildflower wind".Split(' ');

        private static readonly Random _rng = new Random();

        /// <summary>
        /// Azure Function to generate a haiku-style name.
        /// It randomly selects an adjective, a noun, and generates a random token.
        /// </summary>
        /// <param name="req">The HTTP request object.</param>
        /// <returns>
        /// An <see cref="IActionResult"/> representing the HTTP response with the generated name components.
        /// - <see cref="OkObjectResult"/> with the generated name details.
        /// - <see cref="StatusCodeResult"/> (500 Internal Server Error) on unexpected errors.
        /// </returns>
        [Function("HaikuName")]
        public IActionResult GenerateHaikuName(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "Haiku/New")] HttpRequest req)
        {
            _logger.LogInformation("HaikuName: C# HTTP trigger function processed a request.");

            try
            {
                string adj = Sample(ADJS_ARRAY);
                string noun = Sample(NOUNS_ARRAY);
                int token = _rng.Next(0, 9999); // Generate a 4-digit token (0000-9999)

                return new OkObjectResult(new
                {
                    adj,
                    noun,
                    token,
                    gen = $"{adj}-{noun}",
                    gen2 = $"{adj}-{noun}-{token:D4}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HaikuName: An error occurred while generating a haiku name.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Selects a random word from a given array of words.
        /// </summary>
        /// <param name="wordList">The array of words to sample from.</param>
        /// <returns>A randomly selected word.</returns>
        private string Sample(string[] wordList)
        {
            var value = wordList[_rng.Next(0, wordList.Length)];
            return value;
        }
    }
}
