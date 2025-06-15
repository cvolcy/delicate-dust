using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Net.Http;
using Microsoft.Extensions.AI;

namespace Cvolcy.DelicateDust.Functions
{
    /// <summary>
    /// Azure Function to generate a random word and its definitions in French and English
    /// by querying an AI chat client.
    /// </summary>
    public class RandomWordFunction(
        ILogger<RandomWordFunction> _logger,
        IChatClient _chatClient)
    {
        [Function("RandomWord")]
        public async Task<IActionResult> GetRandomWord(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "RandomWord")] HttpRequest req,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("RandomWord: C# HTTP trigger function received a request to generate a random word.");

            try
            {
                var prompt = $"Choose a random **sophisticated and uncommon** word. (random seed {new Random().Next()})" +
                             "Provide a concise, clear definition. " +
                             "Return it in the format of " +
                             "{ \"fr\": { \"word\": \"random_word_in_french\", \"definition\": \"definition_in_french\", \"example\": \"phrase_example_in_french\" }, " +
                             "\"en\": { \"word\": \"random_word_in_english\", \"definition\": \"definition_in_english\", \"example\": \"phrase_example_in_english\" } } " +
                             "and nothing else. Ensure all values are valid JSON strings and strictly adhere to this format.";

                var chatResponse = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);

                var jsonString = chatResponse?.ToString();

                if (string.IsNullOrWhiteSpace(jsonString))
                {
                    _logger.LogWarning("RandomWord: AI chat client returned empty or null content. This may indicate a problem with the AI service or an overly restrictive prompt.");
                    return new StatusCodeResult(StatusCodes.Status502BadGateway);
                }

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                var wordResponse = JsonSerializer.Deserialize<WordResponseModel>(jsonString, jsonOptions);

                if (wordResponse == null ||
                    wordResponse.Fr == null || string.IsNullOrWhiteSpace(wordResponse.Fr.Word) || string.IsNullOrWhiteSpace(wordResponse.Fr.Definition) ||
                    wordResponse.En == null || string.IsNullOrWhiteSpace(wordResponse.En.Word) || string.IsNullOrWhiteSpace(wordResponse.En.Definition))
                {
                    _logger.LogError("RandomWord: AI response deserialized to an invalid or incomplete object. Raw response: {RawResponse}", jsonString);
                    return new StatusCodeResult(StatusCodes.Status502BadGateway);
                }

                _logger.LogInformation("RandomWord: Successfully generated and parsed a random word. French: '{FrenchWord}', English: '{EnglishWord}'.", wordResponse.Fr.Word, wordResponse.En.Word);

                return new OkObjectResult(wordResponse);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "RandomWord: JSON deserialization error from AI response. Check AI output format. Error: {Message}", ex.Message);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError); // Indicates a problem with our parsing or AI output
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "RandomWord: HTTP request error when communicating with the AI service. Error: {Message}", ex.Message);
                return new StatusCodeResult(StatusCodes.Status502BadGateway); // Bad Gateway if AI service is unreachable/unresponsive
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RandomWord: An unexpected error occurred. Error: {Message}", ex.Message);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
    
    public class LanguageEntry
    {
        public string Word { get; set; }
        public string Definition { get; set; }
        public string Example { get; set; }
    }

    public class WordResponseModel
    {
        public LanguageEntry Fr { get; set; }
        public LanguageEntry En { get; set; }
    }
}
