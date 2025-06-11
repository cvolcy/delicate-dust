using System;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Cvolcy.DelicateDust.Functions
{
    /// <summary>
    /// Azure Function to serve static files from a designated 'static' directory.
    /// It includes security measures to prevent directory traversal and proper error handling.
    /// </summary>
    public class StaticFilesFunction(
        ILogger<StaticFilesFunction> _logger)
    {
        private static readonly string StaticFilesBaseDirectory = Path.Combine(AppContext.BaseDirectory, "static");
        private static readonly FileExtensionContentTypeProvider _contentTypeProvider = new FileExtensionContentTypeProvider();

        /// <summary>
        /// Retrieves a static file based on the requested file name in the route.
        /// </summary>
        /// <param name="req">The HTTP request object.</param>
        /// <param name="file">The name of the file to retrieve (e.g., "index.html", "style.css").</param>
        /// <returns>
        /// An <see cref="IActionResult"/> representing the HTTP response:
        /// - <see cref="FileStreamResult"/> with the file content and determined MIME type on success.
        /// - <see cref="NotFoundResult"/> (HTTP 404) if the file does not exist or path is invalid.
        /// - <see cref="StatusCodeResult"/> (HTTP 500 Internal Server Error) on other unexpected errors.
        /// </returns>
        [Function("StaticFile")] // Renamed function for clarity
        public IActionResult GetStaticFile( // Renamed method for clarity
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "Static/{file}")] HttpRequest req,
            string file)
        {
            _logger.LogInformation("StaticFile: C# HTTP trigger function received request for file '{FileName}' at URL '{RequestPath}'.", file, req.Path);

            try
            {
                var fullFilePath = Path.GetFullPath(Path.Combine(StaticFilesBaseDirectory, file));
                if (!fullFilePath.StartsWith(StaticFilesBaseDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("StaticFile: Attempted directory traversal detected for file '{FileName}'. Path was '{FullFilePath}'.", file, fullFilePath);
                    return new NotFoundResult();
                }

                if (!File.Exists(fullFilePath))
                {
                    _logger.LogWarning("StaticFile: Requested file not found at '{FullFilePath}'.", fullFilePath);
                    return new NotFoundResult();
                }

                var fileStream = File.OpenRead(fullFilePath);
                string mimeType;
                if (!_contentTypeProvider.TryGetContentType(fullFilePath, out mimeType))
                {
                    mimeType = "application/octet-stream";
                    _logger.LogInformation("StaticFile: Could not determine MIME type for '{FileName}'. Defaulting to '{MimeType}'.", file, mimeType);
                }

                _logger.LogInformation("StaticFile: Successfully serving file '{FileName}' with MIME type '{MimeType}'.", file, mimeType);

                return new FileStreamResult(fileStream, mimeType);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "StaticFile: File not found for request '{FileName}'. Error: {Message}", file, ex.Message);
                return new NotFoundResult();
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogWarning(ex, "StaticFile: Directory not found or invalid path for request '{FileName}'. Error: {Message}", file, ex.Message);
                return new NotFoundResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StaticFile: An unexpected error occurred while processing file '{FileName}'. Error: {Message}", file, ex.Message);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
