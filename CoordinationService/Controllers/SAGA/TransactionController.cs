using Microsoft.AspNetCore.Mvc;

namespace CoordinationService.Controllers.SAGA;

[ApiController]
[Route("/[controller]/saga")]
public class TransactionController(
    IHttpClientFactory httpClientFactory,
    ILogger<TransactionController> logger,
    IConfiguration configuration)
    : ControllerBase
{
    // Define service URLs from configuration
    private string OrderServiceUrl => configuration["ServiceEndpoints:OrderService"] + "/orders/saga";
    private string InventoryServiceUrl => configuration["ServiceEndpoints:InventoryService"] + "/inventory/saga";
    private string PaymentServiceUrl => configuration["ServiceEndpoints:PaymentService"] + "/payments/saga";

    [HttpPost("order")]
    public async Task<IActionResult> CoordinateSaga([FromBody] DistributedTransactionRequestSAGA request)
    {
        // Start timestamp
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var client = httpClientFactory.CreateClient("SagaCoordinationClient");

        var stepResults = new List<SagaStepResult>();
        var compensatingActions = new Stack<Func<Task>>();

        int? orderId;
        int? paymentId;

        try
        {
            // 1. Create Order
            logger.LogInformation("SAGA: Attempting to create order");
            var createOrderPayload = new { request.UserId, request.ProductId, request.Quantity, request.Amount };
            var createOrderResponse =
                await client.PostAsync($"{OrderServiceUrl}/create", JsonContent.Create(createOrderPayload));

            if (createOrderResponse.IsSuccessStatusCode)
            {
                var orderResult = await createOrderResponse.Content.ReadFromJsonAsync<OrderCreateResponse>();
                orderId = orderResult?.OrderId;
                logger.LogInformation("SAGA: Order created successfully. OrderId: {OrderId}", orderId);
                stepResults.Add(new SagaStepResult("OrderCreation", "Completed", orderId?.ToString()));
                compensatingActions.Push(async () =>
                {
                    logger.LogWarning("SAGA: Compensating: Cancelling order {orderId}", orderId);
                    await client.PostAsync($"{OrderServiceUrl}/cancel/{orderId}", null);
                });
            }
            else
            {
                var errorContent = await createOrderResponse.Content.ReadAsStringAsync();
                logger.LogError(
                    $"SAGA: Order creation failed. Status: {createOrderResponse.StatusCode}, Details: {errorContent}");
                stepResults.Add(new SagaStepResult("OrderCreation", "Failed",
                    Error: $"Status: {createOrderResponse.StatusCode}, Details: {errorContent}"));
                await ExecuteCompensatingActions(compensatingActions);

                stopwatch.Stop();
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new
                    {
                        statusCode = createOrderResponse.StatusCode,
                        failedAt = "OrderCreate",
                        runtime = stopwatch.ElapsedMilliseconds
                    });
            }

            // 2. Reserve Inventory
            logger.LogInformation("SAGA: Attempting to reserve inventory...");
            var reserveInventoryPayload = new
            {
                ProductId = request.ProductId,
                Quantity = request.Quantity,
                OperationType = "RESERVE"
            };
            var reserveInventoryResponse = await client.PostAsync($"{InventoryServiceUrl}/execute",
                JsonContent.Create(reserveInventoryPayload));

            if (reserveInventoryResponse.IsSuccessStatusCode)
            {
                logger.LogInformation("SAGA: Inventory reserved successfully.");
                stepResults.Add(new SagaStepResult("InventoryReservation", "Completed"));
                compensatingActions.Push(async () =>
                {
                    logger.LogWarning("SAGA: Compensating: Releasing inventory reservation...");
                    var releaseInventoryPayload = new
                    {
                        ProductId = request.ProductId,
                        Quantity = request.Quantity,
                        OperationType = "RELEASE"
                    };
                    await client.PostAsync($"{InventoryServiceUrl}/execute",
                        JsonContent.Create(releaseInventoryPayload));
                });
            }
            else
            {
                var errorContent = await reserveInventoryResponse.Content.ReadAsStringAsync();
                logger.LogError(
                    $"SAGA: Inventory reservation failed. Status: {reserveInventoryResponse.StatusCode}, Details: {errorContent}");
                stepResults.Add(new SagaStepResult("InventoryReservation", "Failed",
                    Error: $"Status: {reserveInventoryResponse.StatusCode}, Details: {errorContent}"));
                await ExecuteCompensatingActions(compensatingActions);

                stopwatch.Stop();
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new
                    {
                        statusCode = reserveInventoryResponse.StatusCode,
                        failedAt = "InventoryReservation",
                        runtime = stopwatch.ElapsedMilliseconds
                    });
            }

            // 3. Process Payment
            logger.LogInformation("SAGA: Attempting to process payment...");
            var processPaymentPayload = new
            {
                OrderId = orderId,
                Amount = request.Amount
            };
            var processPaymentResponse =
                await client.PostAsync($"{PaymentServiceUrl}", JsonContent.Create(processPaymentPayload));

            if (processPaymentResponse.IsSuccessStatusCode)
            {
                var paymentResult = await processPaymentResponse.Content.ReadFromJsonAsync<PaymentActionResponse>();
                paymentId = paymentResult?.PaymentId;
                logger.LogInformation("SAGA: Payment processed successfully. PaymentId: {PaymentId}", paymentId);
                stepResults.Add(new SagaStepResult("PaymentProcessing", "Completed", paymentId?.ToString()));
                compensatingActions.Push(async () =>
                {
                    logger.LogWarning("SAGA: Compensating: Cancelling payment {paymentId}...", paymentId);
                    await client.PostAsync($"{PaymentServiceUrl}/cancel/{paymentId}", null);
                });
            }
            else
            {
                var errorContent = await processPaymentResponse.Content.ReadAsStringAsync();
                logger.LogError(
                    "SAGA: Payment processing failed. Status: {HttpStatusCode}, Details: {ErrorContent}",
                    processPaymentResponse.StatusCode, errorContent);
                stepResults.Add(new SagaStepResult("PaymentProcessing", "Failed",
                    Error: $"Status: {processPaymentResponse.StatusCode}, Details: {errorContent}"));
                await ExecuteCompensatingActions(compensatingActions);

                stopwatch.Stop();
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new
                    {
                        statusCode = processPaymentResponse.StatusCode,
                        failedAt = "PaymentProcessing",
                        runtime = stopwatch.ElapsedMilliseconds
                    });
            }

            // If all steps are successful
            logger.LogInformation("SAGA: All steps completed successfully. Transaction committed.");
            stopwatch.Stop();
            return Ok(new
            {
                statusCode = StatusCodes.Status200OK,
                runtime = stopwatch.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SAGA: An unexpected error occurred during the transaction.");
            await ExecuteCompensatingActions(compensatingActions);

            stopwatch.Stop();
            return StatusCode(500, new
            {
                statusCode = 500,
                failedAt = "Exception",
                runtime = stopwatch.ElapsedMilliseconds,
                error = ex.Message
            });
        }
    }

    private async Task ExecuteCompensatingActions(Stack<Func<Task>> compensatingActions)
    {
        logger.LogInformation("SAGA: Executing compensating actions...");

        while (compensatingActions.Count > 0)
        {
            var compensate = compensatingActions.Pop();

            try
            {
                await compensate();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SAGA: Error during compensating action. Manual intervention might be required.");
            }
        }

        logger.LogInformation("SAGA: Finished executing compensating actions.");
    }

    public record DistributedTransactionRequestSAGA(
        int UserId,
        int ProductId,
        int Quantity,
        decimal Amount
    );

    public record DistributedTransactionResultSAGA(
        string OrderStatus,
        string InventoryStatus,
        string PaymentStatus,
        string GlobalStatus,
        string? OrderId = null,
        string? PaymentId = null
    );

    private record SagaStepResult(string StepName, string Status, string? Id = null, string? Error = null);

    private class OrderCreateResponse
    {
        public int OrderId { get; set; }
    }

    private class PaymentActionResponse
    {
        public int PaymentId { get; set; }
    }
}
