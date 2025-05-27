using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Models;
using System.Data;

namespace OrderService.Controllers._2PC;

[ApiController]
[Route("[controller]/2pc")]
public class OrdersController(OrderDbContext context, ILogger<OrdersController> logger) : ControllerBase
{
    // Combined Create and Prepare Order endpoint
    // If orderId is provided, prepare an existing order
    // If no orderId is provided, create a new order in prepared state
    public record OrderRequest(
        int? OrderId,
        int UserId,
        int ProductId,
        int Quantity,
        decimal Amount,
        string OrderStatus = OrderStatuses.Pending);

    public record OrderResponse(int OrderId, string TransactionId);

    [HttpPost]
    public async Task<IActionResult> CreateOrPrepareOrder([FromBody] OrderRequest request)
    {
        // Case 1: Prepare an existing order (Update)
        if (request.OrderId.HasValue)
        {
            return await PrepareExistingOrder(request);
        }
        // Case 2: Create a new order (Insert + Prepare)
        else
        {
            return await CreateAndPrepareOrder(request);
        }
    }

    private async Task<IActionResult> PrepareExistingOrder(OrderRequest orderRequest)
    {
        logger.LogInformation("Attempting to prepare existing order with OrderID: {OrderID}", orderRequest.OrderId);

        var transactionId = Guid.NewGuid().ToString("N");
        // Order ID will always have a value here, but null check is required
        var orderResponse = new OrderResponse(orderRequest.OrderId ?? 0, transactionId);

        try
        {
            // Open connection and begin transaction
            await context.Database.OpenConnectionAsync();
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            logger.LogInformation(
                "Order {OrderID} successfully prepared with PostgreSQL PREPARE TRANSACTION. TransactionId: {TransactionId}",
                orderRequest.OrderId, transactionId);

            var existingOrder = await context.Orders
                .FromSqlRaw("""SELECT * FROM "Orders"  WHERE "OrderID" = {0} FOR UPDATE""", orderRequest.OrderId)
                .FirstOrDefaultAsync();

            if (existingOrder == null)
            {
                logger.LogError("Order with ID {OrderID} not found.", orderRequest.OrderId);

                await context.Database.RollbackTransactionAsync();
                await context.Database.CloseConnectionAsync();

                return StatusCode(StatusCodes.Status400BadRequest, $"Order with ID {orderRequest.OrderId} not found.");
            }

            context.Orders.Update(existingOrder);

            existingOrder.UserID = orderRequest.UserId;
            existingOrder.ProductID = orderRequest.ProductId;
            existingOrder.Quantity = orderRequest.Quantity;
            existingOrder.Amount = orderRequest.Amount;
            existingOrder.OrderStatus = orderRequest.OrderStatus;

            await context.SaveChangesAsync();

            // Prepare the transaction
            await context.Database.ExecuteSqlRawAsync($"PREPARE TRANSACTION '{transactionId}'");
            
            logger.LogInformation(
                "Order {OrderID} prepared for update with PostgreSQL PREPARE TRANSACTION. TransactionId: {TransactionId}",
                orderRequest.OrderId, transactionId);

            return Ok(orderResponse);
        }
        catch (Exception ex)
        {
            await RollbackPreparedTransaction(transactionId);
            await context.Database.CloseConnectionAsync();

            logger.LogError(ex, "Error in PrepareExistingOrder operation for OrderID: {OrderID}",
                orderRequest.OrderId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"Error processing order preparation: {ex.Message}");
        }
    }

    private async Task RollbackPreparedTransaction(string transactionId)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync($"ROLLBACK TRANSACTION '{transactionId}'");
        }
        catch (Exception rollbackEx) when (
            rollbackEx.Message.Contains("prepared transaction with identifier") &&
            rollbackEx.Message.Contains("does not exist"))
        {
            logger.LogWarning("Rollback transaction {TransactionId} does not exist.", transactionId);
        }
        catch (Exception rollbackEx)
        {
            logger.LogError(rollbackEx, "Error during rollback of transaction {TransactionId}.", transactionId);

            throw;
        }
    }

    private async Task<IActionResult> CreateAndPrepareOrder(OrderRequest request)
    {
        logger.LogInformation("Attempting to create and prepare order for UserID: {UserID}, ProductID: {ProductID}",
            request.UserId, request.ProductId);

        if (!ModelState.IsValid)
        {
            logger.LogWarning("Create order request failed validation: {ModelState}", ModelState);
            return BadRequest(ModelState);
        }

        var transactionId = Guid.NewGuid().ToString("N");

        try
        {
            // Create the new order
            var order = new Order
            {
                UserID = request.UserId,
                ProductID = request.ProductId,
                Quantity = request.Quantity,
                Amount = request.Amount,
                OrderStatus = OrderStatuses.Pending
            };
            
            // Open connection and begin transaction
            await context.Database.OpenConnectionAsync();
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            context.Orders.Add(order);
            await context.SaveChangesAsync();
            
            // Prepare the transaction
            await context.Database.ExecuteSqlRawAsync($"PREPARE TRANSACTION '{transactionId}'");

            logger.LogInformation(
                "Order {OrderID} prepared for creation with PostgreSQL PREPARE TRANSACTION. TransactionId: {TransactionId}",
                order.OrderID, transactionId);

            return Ok(new OrderResponse(order.OrderID, transactionId));
        }
        catch (Exception ex)
        {
            await RollbackPreparedTransaction(transactionId);
            await context.Database.CloseConnectionAsync();

            logger.LogError(ex,
                "Error in CreateAndPrepareOrder operation for UserID: {UserID}, ProductID: {ProductID}",
                request.UserId, request.ProductId);
            
            return StatusCode(StatusCodes.Status500InternalServerError,
                "Error processing order creation: " + ex.Message);
        }
    }

    // GET /2pc/orders/{orderId}
    [HttpGet("{orderId}")]
    public async Task<IActionResult> GetOrder(int orderId)
    {
        logger.LogInformation("Attempting to retrieve order with OrderID: {OrderID}", orderId);

        try
        {
            var order = await context.Orders
                .FromSqlRaw("SELECT * FROM \"Orders\" WHERE \"OrderID\" = {0}", orderId)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                logger.LogWarning("Order with OrderID: {OrderID} not found.", orderId);
                return NotFound();
            }

            logger.LogInformation("Successfully retrieved order {OrderID} with status {OrderStatus}", order.OrderID,
                order.OrderStatus);
            return Ok(order);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving order {OrderID} from database.", orderId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving order data.");
        }
    }

    // POST /2pc/orders/{orderId}/commit
    [HttpPost("commit/{transactionId}")]
    public async Task<IActionResult> CommitOrder(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
        {
            return BadRequest(new { Message = "Transaction ID cannot be null or empty." });
        }
        
        logger.LogInformation("Attempting to commit transaction : {transactionId}", transactionId);
        
        try
        {
            // Execute PostgreSQL's COMMIT PREPARED command
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync($"COMMIT PREPARED '{transactionId}'");
            await context.Database.CloseConnectionAsync();

            logger.LogInformation(
                "Successfully committed with PostgreSQL COMMIT PREPARED. TransactionId: {TransactionId}", transactionId);

            return Ok( new { Message = $"Transaction {transactionId} committed successfully." });
        }
        catch (Exception ex)
        {
            await RollbackPreparedTransaction(transactionId);
            await context.Database.CloseConnectionAsync();
            
            logger.LogError(ex, "CommitOrder: Error committing transaction {transactionID} with PostgreSQL COMMIT PREPARED",
                transactionId);
            
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error committing order: {ex.Message}");
        }
    }

    // POST /2pc/orders/{orderId}/abort
    [HttpPost("abort/{transactionId}")]
    public async Task<IActionResult> AbortOrder(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
        {
            return BadRequest(new { Message = "Transaction ID cannot be null or empty." });
        }
        
        logger.LogInformation("Attempting to abort transaction : {transactionId}", transactionId);
        
        try
        {
            // Execute PostgreSQL's ROLLBACK PREPARED command
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync($"ROLLBACK PREPARED '{transactionId}'");
            
            logger.LogInformation(
                "successfully aborted with PostgreSQL ROLLBACK PREPARED. TransactionId: {TransactionId}",
                 transactionId);

            return Ok( new { Message = $"Transaction {transactionId} aborted successfully." });
        }
        catch (Exception ex)
        {
            await RollbackPreparedTransaction(transactionId);
            await context.Database.CloseConnectionAsync();

            logger.LogError(ex, "AbortOrder: Error aborting transaction {transactionId} with PostgreSQL ROLLBACK PREPARED",
                transactionId);
            
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error aborting order: {ex.Message}");
        }
    }
}