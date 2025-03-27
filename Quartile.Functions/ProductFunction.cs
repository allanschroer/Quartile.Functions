using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Quartile.Functions.Entities;

namespace Quartile.Functions
{
    public class ProductFunction
    {
        private readonly ILogger<ProductFunction> _logger;

        private readonly string _connectionString;

        public ProductFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ProductFunction>();
            _connectionString = Environment.GetEnvironmentVariable("ConnectionString");
        }

        [Function("GetProducts")]
        public async Task<HttpResponseData> GetProducts([HttpTrigger(AuthorizationLevel.Function, "get", Route = "products")] HttpRequestData req)
        {
            _logger.LogInformation("HTTP trigger function GetProducts processed a request.");

            try
            {
                string? jsonResult;

                using (var connection = new SqlConnection(_connectionString))
                {
                    var query = "SELECT dbo.GetProductsAsJson()";
                    jsonResult = await connection.QueryFirstOrDefaultAsync<string?>(query);
                }

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await response.WriteStringAsync(jsonResult ?? "[]");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving products");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving products.");
                return errorResponse;
            }
        }

        [Function("CreateProduct")]
        public async Task<HttpResponseData> CreateProduct([HttpTrigger(AuthorizationLevel.Function, "post", Route = "products")] HttpRequestData req)
        {
            _logger.LogInformation("HTTP trigger function CreateProduct processed a request.");

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var product = JsonConvert.DeserializeObject<Product>(requestBody);

                if (product == null || string.IsNullOrEmpty(product.ProductName) || product.Price <= 0)
                {
                    _logger.LogWarning("Invalid product data received");
                    var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                    await badResponse.WriteStringAsync("Invalid product data. Please check your request and try again.");
                    return badResponse;
                }

                using (var connection = new SqlConnection(_connectionString))
                {
                    var query = "EXEC InsertProduct @ProductName, @Description, @Price, @CompanyId, @StoreId";

                    var productId = await connection.ExecuteScalarAsync<int>(query, product);
                    _logger.LogInformation("Product created with ID: {productId}", productId);
                }

                var response = req.CreateResponse(System.Net.HttpStatusCode.Created);
                await response.WriteStringAsync("Product created successfully.");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating product");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while creating the product.");
                return errorResponse;
            }
        }

        [Function("UpdateProduct")]
        public async Task<HttpResponseData> UpdateProduct([HttpTrigger(AuthorizationLevel.Function, "put", Route = "products/{id:int}")] HttpRequestData req, int id)
        {
            _logger.LogInformation("C# HTTP trigger function UpdateProduct processed a request for ID: {id}", id);

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var product = JsonConvert.DeserializeObject<Product>(requestBody);

                if (product == null || string.IsNullOrEmpty(product.ProductName) || product.Price <= 0)
                {
                    _logger.LogWarning("Invalid product data received for ID: {id}", id);
                    var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                    await badResponse.WriteStringAsync("Invalid product data. Please check your request and try again.");
                    return badResponse;
                }

                product.Id = id;

                using (var connection = new SqlConnection(_connectionString))
                {
                    var checkQuery = "SELECT COUNT(1) FROM Product WHERE Id = @ProductID;";
                    var exists = await connection.ExecuteScalarAsync<int>(checkQuery, new { ProductID = id }) > 0;

                    if (!exists)
                    {
                        _logger.LogWarning("Product with ID: {id} not found", id);
                        var notFoundResponse = req.CreateResponse(System.Net.HttpStatusCode.NotFound);
                        await notFoundResponse.WriteStringAsync($"Product with ID: {id} not found.");
                        return notFoundResponse;
                    }

                    var query = @"
                        UPDATE Product
                        SET ProductName = @ProductName,
                            Description = @Description,
                            Price = @Price,
                            CompanyID = @CompanyID,
                            StoreID = @StoreID,
                            ModifiedDate = GETDATE()
                        WHERE Id = @Id;";

                    await connection.ExecuteAsync(query, product);
                    _logger.LogInformation("Product with ID: {id} updated successfully", id);
                }

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await response.WriteStringAsync("Product updated successfully.");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating product with ID: {id}", id);
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while updating the product.");
                return errorResponse;
            }
        }

        [Function("DeleteProduct")]
        public async Task<HttpResponseData> DeleteProduct([HttpTrigger(AuthorizationLevel.Function, "delete", Route = "products/{id:int}")] HttpRequestData req, int id)
        {
            _logger.LogInformation("HTTP trigger function DeleteProduct processed a request for ID: {id}", id);

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    var checkQuery = "SELECT COUNT(1) FROM Product WHERE Id = @ProductID;";
                    var exists = await connection.ExecuteScalarAsync<int>(checkQuery, new { ProductID = id }) > 0;

                    if (!exists)
                    {
                        _logger.LogWarning("Product with ID: {id} not found", id);
                        var notFoundResponse = req.CreateResponse(System.Net.HttpStatusCode.NotFound);
                        await notFoundResponse.WriteStringAsync($"Product with ID: {id} not found.");
                        return notFoundResponse;
                    }

                    var query = "DELETE FROM Product WHERE Id = @ProductID;";
                    await connection.ExecuteAsync(query, new { ProductID = id });
                    _logger.LogInformation("Product with ID: {id} deleted successfully", id);
                }

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await response.WriteStringAsync("Product deleted successfully.");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting product with ID: {id}", id);
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while deleting the product.");
                return errorResponse;
            }
        }
    }
}
