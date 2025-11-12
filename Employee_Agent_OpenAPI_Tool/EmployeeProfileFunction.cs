using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Pfizer.EmpInfoUpdate.Model;
using System.Data;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;

namespace Pfizer.EmpInfoUpdate
{
    public class EmployeeProfileFunction
    {
        private readonly ILogger<EmployeeProfileFunction> _logger;
        private readonly JsonSerializerOptions _json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public EmployeeProfileFunction(ILogger<EmployeeProfileFunction> logger)
        {
            _logger = logger;
        }

        [Function("ValidateEmployeeProfile")]
        [OpenApiOperation(operationId: "ValidateEmployeeProfile", tags: new[] { "Employee" }, Summary = "Validate employee profile", Description = "Validates employee identity information against the database")]
        [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ValidateEmployeeProfileRequest), Required = true, Description = "Employee identity information to validate")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(ValidateEmployeeResponse), Description = "Validation successful")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Validation failed or invalid request")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Internal server error")]
        public async Task<HttpResponseData> ValidateEmployeeProfile([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Employee profile validation request received.");

                // Read connection string from Azure Function App settings
                var connStr = Environment.GetEnvironmentVariable("SqlConnectionString");
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    _logger.LogError("SqlConnectionString not configured in application settings");
                    return await CreateErrorResponse(req, "Database configuration error", 1001, HttpStatusCode.InternalServerError);
                }
                // Deserialize & DataAnnotations validation
                var (body, validationErrors) = await RequestValidation.ReadAndValidateAsync<ValidateEmployeeProfileRequest>(req, _json, requiredBody: true);
                if (validationErrors.Count > 0 || body is null)
                {
                    var combined = string.Join(" | ", validationErrors.Select(e => e.ErrorMessage));
                    _logger.LogWarning("Validation failed for ValidateEmployeeProfile: {Errors}", combined);
                    return await CreateErrorResponse(req, combined, 2001, HttpStatusCode.BadRequest);
                }

                bool isValid;
                string validationMessage;

                try
                {
                    await using var conn = new SqlConnection(connStr);
                    await conn.OpenAsync(req.FunctionContext.CancellationToken);

                    await using var cmd = new SqlCommand("dbo.ValidateEmployeeProfile", conn)
                    {
                        CommandType = CommandType.StoredProcedure,
                        CommandTimeout = 60
                    };

                    // Input parameters
                    cmd.Parameters.Add(new SqlParameter("@employee_id", SqlDbType.VarChar, 32) { Value = body.employee_id });
                    cmd.Parameters.Add(new SqlParameter("@first_name", SqlDbType.NVarChar, 100) { Value = body.first_name });
                    cmd.Parameters.Add(new SqlParameter("@last_name", SqlDbType.NVarChar, 100) { Value = body.last_name });

                    // Output parameters
                    var outIsValid = new SqlParameter("@IsValid", SqlDbType.Bit) { Direction = ParameterDirection.Output };
                    var outValidationMessage = new SqlParameter("@ValidationMessage", SqlDbType.NVarChar, 4000) { Direction = ParameterDirection.Output };
                    cmd.Parameters.Add(outIsValid);
                    cmd.Parameters.Add(outValidationMessage);

                    await cmd.ExecuteNonQueryAsync(req.FunctionContext.CancellationToken);

                    isValid = outIsValid.Value is bool b && b;
                    validationMessage = (outValidationMessage.Value as string) ?? string.Empty;
                }
                catch (SqlException ex)
                {
                    _logger.LogError(ex, "Database error occurred while validating employee profile. Employee ID: {EmployeeId}, SQL Error Number: {ErrorNumber}, State: {State}, Severity: {Class}",
                        body.employee_id, ex.Number, ex.State, ex.Class);

                    return ex.Number switch
                    {
                        2 => await CreateErrorResponse(req, "Database connection timeout", 3001, HttpStatusCode.InternalServerError),
                        18456 => await CreateErrorResponse(req, "Database authentication failed", 3002, HttpStatusCode.InternalServerError),
                        208 => await CreateErrorResponse(req, "Database schema error: Required table or stored procedure not found", 3006, HttpStatusCode.InternalServerError),
                        2812 => await CreateErrorResponse(req, "Database schema error: Stored procedure 'dbo.ValidateEmployeeProfile' not found", 3007, HttpStatusCode.InternalServerError),
                        -2 => await CreateErrorResponse(req, "Database command timeout", 3005, HttpStatusCode.RequestTimeout),
                        _ => await CreateErrorResponse(req, $"Database operation failed (Error {ex.Number})", 3000, HttpStatusCode.InternalServerError)
                    };
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "Invalid database operation. Employee ID: {EmployeeId}", body.employee_id);
                    return await CreateErrorResponse(req, "Invalid database operation", 3004, HttpStatusCode.InternalServerError);
                }
                catch (TimeoutException ex)
                {
                    _logger.LogError(ex, "Database operation timeout. Employee ID: {EmployeeId}", body.employee_id);
                    return await CreateErrorResponse(req, "Database operation timeout", 3005, HttpStatusCode.RequestTimeout);
                }

                // Create response
                var validationResponse = new ValidateEmployeeResponse
                {
                    IsValid = isValid,
                    ValidationMessage = validationMessage
                };

                // Determine status code based on validation result
                //var statusCode = isValid ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
                var statusCode = HttpStatusCode.OK;

                // Log the result
                if (isValid)
                {
                    _logger.LogInformation("Employee profile validation successful. Employee ID: {EmployeeId}, Message: {Message}",
                        body.employee_id, validationMessage);
                }
                else
                {
                    _logger.LogWarning("Employee profile validation failed. Employee ID: {EmployeeId}, Message: {Message}",
                        body.employee_id, validationMessage);
                }

                var response = req.CreateResponse(statusCode);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                var responsePayload = JsonSerializer.Serialize(validationResponse, _json);
                await response.WriteStringAsync(responsePayload);

                return response;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Request was cancelled");
                return await CreateErrorResponse(req, "Request was cancelled", 4001, HttpStatusCode.RequestTimeout);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while processing employee validation request");
                return await CreateErrorResponse(req, "An unexpected error occurred", 5000, HttpStatusCode.InternalServerError);
            }
        }

        [Function("UpdateEmployeeProfile")]
        [OpenApiOperation(operationId: "UpdateEmployeeProfile", tags: new[] { "Employee" }, Summary = "Update employee profile", Description = "Updates employee information in the database")]
        [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpdateEmployeeProfileRequest), Required = true, Description = "Employee update information")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(UpdateEmployeeResponse), Description = "Update successful")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Update failed or invalid request")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Internal server error")]
        public async Task<HttpResponseData> UpdateEmployeeProfile([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Employee profile update request received.");

                // Read connection string from Azure Function App settings
                var connStr = Environment.GetEnvironmentVariable("SqlConnectionString");
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    _logger.LogError("SqlConnectionString not configured in application settings");
                    return await CreateErrorResponse(req, "Database configuration error", 1001, HttpStatusCode.InternalServerError);
                }

                // Deserialize & DataAnnotations validation
                var (body, validationErrors) = await RequestValidation.ReadAndValidateAsync<UpdateEmployeeProfileRequest>(req, _json, requiredBody: true);
                if (validationErrors.Count > 0 || body is null)
                {
                    var combined = string.Join(" | ", validationErrors.Select(e => e.ErrorMessage));
                    _logger.LogWarning("Validation failed for UpdateEmployeeProfile: {Errors}", combined);
                    return await CreateErrorResponse(req, combined, 2001, HttpStatusCode.BadRequest);
                }

                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync(req.FunctionContext.CancellationToken);

                // Step 2: Proceed with update if validation passes
                string updateMessage;
                int rowsUpdated;

                try
                {
                    await using var updateCmd = new SqlCommand("dbo.UpdateEmployeeProfile", conn)
                    {
                        CommandType = CommandType.StoredProcedure,
                        CommandTimeout = 60
                    };

                    // Input parameters for update
                    updateCmd.Parameters.Add(new SqlParameter("@employee_id", SqlDbType.VarChar, 32) { Value = body.employee_id });

                    // Optional update fields (pass NULL when not provided)
                    updateCmd.Parameters.Add(new SqlParameter("@new_department", SqlDbType.NVarChar, 100)
                    { Value = (object?)body.department ?? DBNull.Value, IsNullable = true });

                    updateCmd.Parameters.Add(new SqlParameter("@new_job_title", SqlDbType.NVarChar, 100)
                    { Value = (object?)body.job_title ?? DBNull.Value, IsNullable = true });

                    updateCmd.Parameters.Add(new SqlParameter("@new_address", SqlDbType.NVarChar, 4000)
                    { Value = (object?)body.address ?? DBNull.Value, IsNullable = true });

                    // Output parameters for update
                    var outUpdateMessage = new SqlParameter("@UpdateMessage", SqlDbType.NVarChar, 4000) { Direction = ParameterDirection.Output };
                    var outRowsUpdated = new SqlParameter("@RowsUpdated", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    updateCmd.Parameters.Add(outUpdateMessage);
                    updateCmd.Parameters.Add(outRowsUpdated);

                    await updateCmd.ExecuteNonQueryAsync(req.FunctionContext.CancellationToken);

                    updateMessage = (outUpdateMessage.Value as string) ?? string.Empty;
                    rowsUpdated = outRowsUpdated.Value is int i ? i : 0;
                }
                catch (SqlException ex)
                {
                    _logger.LogError(ex, "Database error occurred during employee update. Employee ID: {EmployeeId}, SQL Error Number: {ErrorNumber}",
                        body.employee_id, ex.Number);

                    return ex.Number switch
                    {
                        2 => await CreateErrorResponse(req, "Database connection timeout", 3001, HttpStatusCode.InternalServerError),
                        18456 => await CreateErrorResponse(req, "Database authentication failed", 3002, HttpStatusCode.InternalServerError),
                        208 => await CreateErrorResponse(req, "Database schema error: Required table or stored procedure not found", 3006, HttpStatusCode.InternalServerError),
                        2812 => await CreateErrorResponse(req, "Database schema error: Stored procedure 'dbo.UpdateEmployeeProfile' not found", 3007, HttpStatusCode.InternalServerError),
                        547 => await CreateErrorResponse(req, "Data constraint violation", 3003, HttpStatusCode.BadRequest),
                        515 => await CreateErrorResponse(req, "Required field cannot be null", 3008, HttpStatusCode.BadRequest),
                        8152 => await CreateErrorResponse(req, "Data too long for database field", 3009, HttpStatusCode.BadRequest),
                        -2 => await CreateErrorResponse(req, "Database command timeout", 3005, HttpStatusCode.RequestTimeout),
                        _ => await CreateErrorResponse(req, $"Database update failed (Error {ex.Number})", 3000, HttpStatusCode.InternalServerError)
                    };
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "Invalid database operation during update. Employee ID: {EmployeeId}", body.employee_id);
                    return await CreateErrorResponse(req, "Invalid database operation", 3004, HttpStatusCode.InternalServerError);
                }
                catch (TimeoutException ex)
                {
                    _logger.LogError(ex, "Database operation timeout during update. Employee ID: {EmployeeId}", body.employee_id);
                    return await CreateErrorResponse(req, "Database operation timeout", 3005, HttpStatusCode.RequestTimeout);
                }

                // Create success response
                var successResponse = new UpdateEmployeeResponse
                {
                    Message = updateMessage,
                    RowsUpdated = rowsUpdated
                };

                var statusCode = HttpStatusCode.OK;

                // Log the result for monitoring
                if (rowsUpdated > 0)
                {
                    _logger.LogInformation("Successfully updated employee profile. Employee ID: {EmployeeId}, Rows Updated: {RowsUpdated}, Message: {Message}",
                        body.employee_id, rowsUpdated, updateMessage);
                }
                else
                {
                    _logger.LogInformation("Employee profile update completed - no changes required. Employee ID: {EmployeeId}, Message: {Message}",
                        body.employee_id, updateMessage);
                }

                var response = req.CreateResponse(statusCode);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                var responsePayload = JsonSerializer.Serialize(successResponse, _json);
                await response.WriteStringAsync(responsePayload);

                return response;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Request was cancelled");
                return await CreateErrorResponse(req, "Request was cancelled", 4001, HttpStatusCode.RequestTimeout);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while processing employee update request");
                return await CreateErrorResponse(req, "An unexpected error occurred", 5000, HttpStatusCode.InternalServerError);
            }
        }

        [Function("HealthCheck")]
        [OpenApiOperation(operationId: "HealthCheck", tags: new[] { "System" }, Summary = "Health check", Description = "Checks the health status of the service with optional extended diagnostics")]
        [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(HealthCheckRequest), Required = false, Description = "Optional health check configuration parameters")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Service is healthy")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Health check failed")]
        public async Task<HttpResponseData> HealthCheck([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            _logger.LogInformation("Health check request received.");

            try
            {
                // Optional body validation (tolerate invalid JSON like previous behavior)
                var (body, bodyErrors) = await RequestValidation.ReadAndValidateAsync<HealthCheckRequest>(req, _json, requiredBody: false);
                if (bodyErrors.Count > 0)
                {
                    // Only log, don't fail to preserve previous lenient behavior
                    _logger.LogDebug("HealthCheck optional body validation issues: {Errors}", string.Join(", ", bodyErrors.Select(e => e.ErrorMessage)));
                }

                var healthResponse = new
                {
                    Status = "Healthy",
                    Timestamp = DateTime.UtcNow,
                    Service = "Employee Profile Service",
                    Version = "1.0.0",
                    Environment = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Unknown",
                    RequestDetails = body != null ? new
                    {
                        body.CheckDatabase,
                        body.IncludeMetrics,
                        body.ClientIdentifier
                    } : null
                };

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                var responsePayload = JsonSerializer.Serialize(healthResponse, _json);
                await response.WriteStringAsync(responsePayload);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during health check");
                return await CreateErrorResponse(req, "Health check failed", 6001, HttpStatusCode.InternalServerError);
            }
        }

        [Function("Echo")]
        [OpenApiOperation(operationId: "Echo", tags: new[] { "Testing" }, Summary = "Echo test endpoint", Description = "Returns the received payload for testing purposes")]
        [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(EchoRequest), Required = false, Description = "Test payload")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Echo response successful")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Echo test failed")]
        public async Task<HttpResponseData> Echo([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            _logger.LogInformation("Echo test request received.");

            try
            {
                var ct = req.FunctionContext.CancellationToken;
                // Optional payload validation
                var (payload, payloadErrors) = await RequestValidation.ReadAndValidateAsync<EchoRequest>(req, _json, requiredBody: false);
                if (payloadErrors.Count > 0)
                {
                    _logger.LogDebug("Echo optional body validation issues: {Errors}", string.Join(", ", payloadErrors.Select(e => e.ErrorMessage)));
                }
                payload ??= new EchoRequest();

                // Create echo response
                var echoResponse = new
                {
                    Message = "Echo test successful",
                    ReceivedAt = DateTime.UtcNow,
                    Input = new
                    {
                        payload.Name,
                        payload.Value,
                        payload.Description
                    },
                    ContentType = req.Headers.TryGetValues("Content-Type", out var contentType)
                        ? contentType.FirstOrDefault()
                        : "Not specified"
                };

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                var responsePayload = JsonSerializer.Serialize(echoResponse, _json);
                await response.WriteStringAsync(responsePayload, ct);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing echo request");
                return await CreateErrorResponse(req, "Echo test failed", 9999, HttpStatusCode.InternalServerError);
            }
        }

        private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string errorMessage, int errorCode, HttpStatusCode statusCode)
        {
            var errorResponse = new ErrorResponse
            {
                ErrorMessage = errorMessage,
                ErrorCode = errorCode
            };

            var response = req.CreateResponse(statusCode);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            var errorPayload = JsonSerializer.Serialize(errorResponse, _json);
            await response.WriteStringAsync(errorPayload);

            return response;
        }
    }
}