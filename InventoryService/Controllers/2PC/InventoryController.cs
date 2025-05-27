using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryService.Data;
using InventoryService.Models;
using System.Data;

namespace InventoryService.Controllers._2PC;

[ApiController]
[Route("[controller]/2pc")]
public class InventoryController(InventoryDbContext context, ILogger<InventoryController> logger)
    : ControllerBase
{
    public record InventoryUpdateRequest(
        int ProductId,
        int Quantity,
        string OperationType
    );

    private record InventoryResponse(int ProductId, string TransactionId);

    [HttpPost]
    public async Task<IActionResult> PrepareInventoryChange([FromBody] InventoryUpdateRequest request)
    {
        logger.LogInformation(
            "Attempting to prepare inventory change for ProductID: {ProductID}, Operation: {OperationType}",
            request.ProductId, request.OperationType);

        if (!ModelState.IsValid)
        {
            logger.LogWarning("Inventory update request failed validation: {ModelState}", ModelState);
            return BadRequest(ModelState);
        }

        var transactionId = Guid.NewGuid().ToString("N");

        try
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            var inventoryItem = await context.Inventory
                .FromSqlRaw("""SELECT * FROM "Inventory" WHERE "ProductID" = {0} FOR UPDATE""", request.ProductId)
                .FirstOrDefaultAsync();

            if (inventoryItem == null)
            {
                logger.LogError("Inventory record for ProductID {ProductID} not found.", request.ProductId);
                await context.Database.RollbackTransactionAsync();
                await context.Database.CloseConnectionAsync();
                return StatusCode(StatusCodes.Status404NotFound,
                    $"Inventory for ProductID {request.ProductId} not found.");
            }

            switch (request.OperationType.ToUpper())
            {
                case Inventory.OperationTypes.Reserve:
                    if (inventoryItem.StockQuantity < request.Quantity)
                    {
                        logger.LogWarning(
                            "Insufficient stock for ProductID {ProductID}. Requested: {RequestedQuantity}, Available: {AvailableStock}",
                            request.ProductId, request.Quantity, inventoryItem.StockQuantity);
                        await context.Database.RollbackTransactionAsync();
                        await context.Database.CloseConnectionAsync();
                        return StatusCode(StatusCodes.Status400BadRequest, "Insufficient stock.");
                    }

                    inventoryItem.StockQuantity -= request.Quantity;
                    inventoryItem.ReservedQuantity += request.Quantity;
                    break;

                case Inventory.OperationTypes.Release:
                    if (inventoryItem.ReservedQuantity < request.Quantity)
                    {
                        logger.LogWarning(
                            "Cannot release {RequestedQuantity} for ProductID {ProductID}. Reserved: {ReservedQuantity}",
                            request.Quantity, request.ProductId, inventoryItem.ReservedQuantity);
                        await context.Database.RollbackTransactionAsync();
                        await context.Database.CloseConnectionAsync();
                        return StatusCode(StatusCodes.Status400BadRequest,
                            "Cannot release more than reserved quantity.");
                    }

                    inventoryItem.StockQuantity += request.Quantity;
                    inventoryItem.ReservedQuantity -= request.Quantity;
                    break;

                default:
                    logger.LogWarning("Invalid OperationType: {OperationType} for ProductID: {ProductID}",
                        request.OperationType, request.ProductId);
                    await context.Database.RollbackTransactionAsync();
                    await context.Database.CloseConnectionAsync();
                    return StatusCode(StatusCodes.Status400BadRequest,
                        "Invalid operation type specified for prepare phase.");
            }

            context.Inventory.Update(inventoryItem);
            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlRawAsync($"PREPARE TRANSACTION '{transactionId}'");

            logger.LogInformation(
                "Inventory change prepared for ProductID: {ProductID}, Operation: {OperationType}. TransactionId: {TransactionId}",
                request.ProductId, request.OperationType, transactionId);

            return Ok(new InventoryResponse(request.ProductId, transactionId));
        }
        catch (Exception ex)
        {
            try
            {
                if (context.Database.CurrentTransaction != null)
                {
                    await context.Database.RollbackTransactionAsync();
                }

                await RollbackPreparedTransaction(transactionId);
            }
            catch (Exception rbEx)
            {
                logger.LogError(rbEx,
                    "Error during explicit transaction rollback in PrepareInventoryChange for ProductID: {ProductID}",
                    request.ProductId);
            }

            if (context.Database.GetDbConnection().State == System.Data.ConnectionState.Open)
            {
                await context.Database.CloseConnectionAsync();
            }

            logger.LogError(ex,
                "Error in PrepareInventoryChange for ProductID: {ProductID}, Operation: {OperationType}",
                request.ProductId, request.OperationType);
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"Error processing inventory change preparation: {ex.Message}");
        }
    }

    private async Task RollbackPreparedTransaction(string transactionId)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync($"ROLLBACK PREPARED '{transactionId}'");
            logger.LogInformation("Successfully rolled back prepared transaction {TransactionId}", transactionId);
        }
        catch (Exception rollbackEx) when (
            rollbackEx.Message.Contains("prepared transaction with identifier") &&
            rollbackEx.Message.Contains("does not exist"))
        {
            logger.LogWarning("Rollback prepared transaction {TransactionId} does not exist.", transactionId);
        }
        catch (Exception rollbackEx)
        {
            logger.LogError(rollbackEx, "Error during rollback of prepared transaction {TransactionId}.",
                transactionId);
            throw;
        }
    }

    [HttpPost("commit/{transactionId}")]
    public async Task<IActionResult> CommitInventoryChange(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
        {
            return BadRequest(new { Message = "Transaction ID cannot be null or empty." });
        }

        logger.LogInformation("Attempting to commit transaction: {TransactionId} for inventory", transactionId);

        try
        {
            await context.Database.OpenConnectionAsync();

            await context.Database.ExecuteSqlRawAsync($"COMMIT PREPARED '{transactionId}'");

            logger.LogInformation("Successfully committed transaction {TransactionId} with COMMIT PREPARED.",
                transactionId);

            return Ok(new { Message = $"Transaction {transactionId} committed successfully." });
        }
        catch (Exception ex)
        {
            await RollbackPreparedTransaction(transactionId);

            logger.LogError(ex, "Error committing transaction {TransactionId} with COMMIT PREPARED.", transactionId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"Error committing inventory change: {ex.Message}. Transaction may still be in prepared state.");
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [HttpPost("abort/{transactionId}")]
    public async Task<IActionResult> AbortInventoryChange(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
        {
            return BadRequest(new { Message = "Transaction ID cannot be null or empty." });
        }

        logger.LogInformation("Attempting to abort transaction: {TransactionId} for inventory", transactionId);

        try
        {
            await context.Database.OpenConnectionAsync();


            await context.Database.ExecuteSqlRawAsync($"ROLLBACK PREPARED '{transactionId}'");
            logger.LogInformation("Successfully aborted transaction {TransactionId} with ROLLBACK PREPARED.",
                transactionId);

            return Ok(new { Message = $"Transaction {transactionId} aborted successfully." });
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("prepared transaction with identifier") && ex.Message.Contains("does not exist"))
            {
                logger.LogWarning("Attempted to abort non-existent or already finalized transaction {TransactionId}.",
                    transactionId);

                return Ok(new { Message = $"Transaction {transactionId} was already finalized or never existed." });
            }

            logger.LogError(ex, "Error aborting transaction {TransactionId} with ROLLBACK PREPARED.", transactionId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"Error aborting inventory change: {ex.Message}. Transaction may still be in prepared state.");
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}