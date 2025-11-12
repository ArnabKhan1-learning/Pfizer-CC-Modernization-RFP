using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;

namespace Pfizer.EmpInfoUpdate
{
    /// <summary>
    /// Helper to deserialize and validate request bodies using DataAnnotations
    /// without consuming the HttpRequestData.Body stream (position is reset when possible).
    /// </summary>
    public static class RequestValidation
    {
        public static async Task<(T? Model, List<ValidationResult> Errors)> ReadAndValidateAsync<T>(
            HttpRequestData req,
            JsonSerializerOptions? options = null,
            bool requiredBody = true)
        {
            options ??= new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            string bodyText;
            using (var reader = new StreamReader(req.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
            {
                bodyText = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            // Reset stream position for downstream code that may also read the body
            if (req.Body.CanSeek)
            {
                req.Body.Seek(0, SeekOrigin.Begin);
            }

            if (string.IsNullOrWhiteSpace(bodyText))
            {
                if (requiredBody)
                {
                    return (default, new List<ValidationResult> { new ValidationResult("Request body is required.") });
                }

                // No body and not required
                return (default, new List<ValidationResult>());
            }

            T? model;
            try
            {
                model = JsonSerializer.Deserialize<T>(bodyText, options);
            }
            catch (JsonException)
            {
                return (default, new List<ValidationResult> { new ValidationResult("Invalid JSON payload.") });
            }

            if (model is null)
            {
                return (default, new List<ValidationResult> { new ValidationResult("Unable to deserialize request body.") });
            }

            var validationResults = new List<ValidationResult>();
            var context = new ValidationContext(model, serviceProvider: null, items: null);
            // validateAllProperties: true ensures property-level attributes are evaluated
            Validator.TryValidateObject(model, context, validationResults, validateAllProperties: true);

            return (model, validationResults);
        }
    }
}
