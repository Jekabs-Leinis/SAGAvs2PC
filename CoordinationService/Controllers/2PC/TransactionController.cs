using Microsoft.AspNetCore.Mvc;

namespace CoordinationService.Controllers._2PC;

[ApiController]
[Route("/[controller]/2pc")]
public class TransactionController(
    IHttpClientFactory httpClientFactory,
    ILogger<TransactionController> logger,
    IConfiguration configuration)
    : ControllerBase
{
    private async Task<PrepareResult> PrepareOrder(HttpClient client, string url, DistributedTransactionRequest request)
    {
        try
        {
            var orderPrepareBody = new
            {
                OrderId = (int?)null,
                request.UserId,
                request.ProductId,
                request.Quantity,
                request.Amount
            };
            var orderResp = await client.PostAsJsonAsync(url, orderPrepareBody);

            if (orderResp.IsSuccessStatusCode)
            {
                var orderResult = await orderResp.Content.ReadFromJsonAsync<OrderPrepareResponse>();
                logger.LogInformation("Order prepared, transactionId: {OrderTransactionId}",
                    orderResult?.TransactionId);
                return new PrepareResult("Prepared", orderResult?.TransactionId, orderResult?.OrderId);
            }

            logger.LogError("Order prepare failed: {StatusCode}", orderResp.StatusCode);
            return new PrepareResult($"PrepareFailed: {orderResp.StatusCode}", null, null,
                $"HTTP {orderResp.StatusCode}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Order prepare exception");
            return new PrepareResult($"PrepareException: {ex.Message}", null, null, ex.Message);
        }
    }

    private async Task<PrepareResult> PrepareInventory(HttpClient client, string url,
        DistributedTransactionRequest request)
    {
        try
        {
            var inventoryPrepareBody = new
            {
                request.ProductId,
                request.Quantity,
                OperationType = "RESERVE"
            };
            var inventoryResp = await client.PostAsJsonAsync(url, inventoryPrepareBody);

            if (inventoryResp.IsSuccessStatusCode)
            {
                var inventoryResult = await inventoryResp.Content.ReadFromJsonAsync<InventoryPrepareResponse>();
                logger.LogInformation("Inventory prepared, transactionId: {InventoryTransactionId}",
                    inventoryResult?.TransactionId);
                return new PrepareResult("Prepared", inventoryResult?.TransactionId);
            }

            logger.LogError("Inventory prepare failed: {StatusCode}", inventoryResp.StatusCode);
            return new PrepareResult($"PrepareFailed: {inventoryResp.StatusCode}", null, null,
                $"HTTP {inventoryResp.StatusCode}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Inventory prepare exception");
            return new PrepareResult($"PrepareException: {ex.Message}", null, null, ex.Message);
        }
    }

    private async Task<PrepareResult> PreparePayment(HttpClient client, string url,
        DistributedTransactionRequest request, int? orderId)
    {
        try
        {
            var paymentPrepareBody = new
            {
                PaymentId = (int?)null,
                OrderId = orderId,
                request.Amount
            };
            var paymentResp = await client.PostAsJsonAsync(url, paymentPrepareBody);

            if (paymentResp.IsSuccessStatusCode)
            {
                var paymentResult = await paymentResp.Content.ReadFromJsonAsync<PaymentPrepareResponse>();
                logger.LogInformation("Payment prepared, transactionId: {PaymentTransactionId}",
                    paymentResult?.TransactionId);
                return new PrepareResult("Prepared", paymentResult?.TransactionId);
            }

            logger.LogError("Payment prepare failed: {StatusCode}", paymentResp.StatusCode);
            return new PrepareResult($"PrepareFailed: {paymentResp.StatusCode}", null, null,
                $"HTTP {paymentResp.StatusCode}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Payment prepare exception");
            return new PrepareResult($"PrepareException: {ex.Message}", null, null, ex.Message);
        }
    }

    [HttpPost("order")]
    public async Task<IActionResult> Coordinate2PC([FromBody] DistributedTransactionRequest request)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var client = httpClientFactory.CreateClient("CoordinationClient");
        var orderServiceUrl = configuration["ServiceEndpoints:OrderService"] + "/orders/2pc";
        var inventoryServiceUrl = configuration["ServiceEndpoints:InventoryService"] + "/inventory/2pc";
        var paymentServiceUrl = configuration["ServiceEndpoints:PaymentService"] + "/payments/2pc";

        // 1. Prepare Order
        var orderResult = await PrepareOrder(client, orderServiceUrl, request);

        if (orderResult.Status != "Prepared")
        {
            stopwatch.Stop();
            return StatusCode(StatusCodes.Status500InternalServerError,
                new
                {
                    statusCode = StatusCodes.Status500InternalServerError,
                    failedAt = "OrderPrepare",
                    runtime = stopwatch.ElapsedMilliseconds
                });
        }

        // 2. Prepare Inventory
        var inventoryResult = await PrepareInventory(client, inventoryServiceUrl, request);

        if (inventoryResult.Status != "Prepared")
        {
            if (orderResult.TransactionId != null)
            {
                await client.PostAsync($"{orderServiceUrl}/abort/{orderResult.TransactionId}", null);
            }

            stopwatch.Stop();
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                statusCode = StatusCodes.Status500InternalServerError,
                failedAt = "InventoryPrepare",
                runtime = stopwatch.ElapsedMilliseconds
            });
        }

        // 3. Prepare Payment
        var paymentResult = await PreparePayment(client, paymentServiceUrl, request, orderResult.ID);

        if (paymentResult.Status != "Prepared")
        {
            if (orderResult.TransactionId != null)
            {
                await client.PostAsync($"{orderServiceUrl}/abort/{orderResult.TransactionId}", null);
            }

            if (inventoryResult.TransactionId != null)
            {
                await client.PostAsync($"{inventoryServiceUrl}/abort/{inventoryResult.TransactionId}", null);
            }

            stopwatch.Stop();
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                statusCode = StatusCodes.Status500InternalServerError,
                failedAt = "PaymentPrepare",
                runtime = stopwatch.ElapsedMilliseconds
            });
        }

        // All prepared: Commit phase
        string orderStatus = orderResult.Status,
            inventoryStatus = inventoryResult.Status,
            paymentStatus = paymentResult.Status,
            globalStatus = "Committed";

        try
        {
            var orderCommit = orderResult.TransactionId != null
                ? await client.PostAsync($"{orderServiceUrl}/commit/{orderResult.TransactionId}", null)
                : null;
            var inventoryCommit = inventoryResult.TransactionId != null
                ? await client.PostAsync($"{inventoryServiceUrl}/commit/{inventoryResult.TransactionId}", null)
                : null;
            var paymentCommit = paymentResult.TransactionId != null
                ? await client.PostAsync($"{paymentServiceUrl}/commit/{paymentResult.TransactionId}", null)
                : null;
            orderStatus = orderCommit?.IsSuccessStatusCode == true
                ? "Committed"
                : $"CommitFailed: {orderCommit?.StatusCode}";
            inventoryStatus = inventoryCommit?.IsSuccessStatusCode == true
                ? "Committed"
                : $"CommitFailed: {inventoryCommit?.StatusCode}";
            paymentStatus = paymentCommit?.IsSuccessStatusCode == true
                ? "Committed"
                : $"CommitFailed: {paymentCommit?.StatusCode}";
            logger.LogInformation("All services committed.");
        }
        catch (Exception ex)
        {
            globalStatus = "CommitException";
            logger.LogError(ex, "Commit phase exception");

            stopwatch.Stop();
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                statusCode = StatusCodes.Status500InternalServerError,
                failedAt = "CommitPhase",
                runtime = stopwatch.ElapsedMilliseconds
            });
        }

        stopwatch.Stop();
        return Ok(new
        {
            statusCode = StatusCodes.Status200OK,
            runtime = stopwatch.ElapsedMilliseconds
        });
    }

    public record DistributedTransactionRequest(
        int UserId,
        int ProductId,
        int Quantity,
        decimal Amount
    );

    public record DistributedTransactionResult(
        string OrderStatus,
        string InventoryStatus,
        string PaymentStatus,
        string GlobalStatus,
        string? OrderTransactionId = null,
        string? InventoryTransactionId = null,
        string? PaymentTransactionId = null
    );

    private record PrepareResult(string Status, string? TransactionId, int? ID = null, string? Error = null);

    private class OrderPrepareResponse
    {
        public int OrderId { get; set; }
        public string TransactionId { get; set; }
    }

    private class InventoryPrepareResponse
    {
        public int ProductId { get; set; }
        public string TransactionId { get; set; }
    }

    private class PaymentPrepareResponse
    {
        public int PaymentId { get; set; }
        public string TransactionId { get; set; }
    }
}
