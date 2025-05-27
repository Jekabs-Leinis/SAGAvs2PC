using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryService.Data;
using InventoryService.Models;

namespace InventoryService.Controllers.SAGA;

[ApiController]
[Route("[controller]/saga")]
public class InventoryController(InventoryDbContext context, ILogger<InventoryController> logger)
    : ControllerBase
{
    public record InventoryUpdateRequestSAGA(
        int ProductId,
        int Quantity,
        string OperationType
    );

    [HttpPost("execute")]
    public async Task<IActionResult> ExecuteInventoryChange([FromBody] InventoryUpdateRequestSAGA request)
    {
        logger.LogInformation(
            "Executing inventory change for ProductID: {ProductID}, Operation: {OperationType}",
            request.ProductId, request.OperationType);

        if (!ModelState.IsValid)
        {
            logger.LogWarning("Inventory update request failed validation: {ModelState}", ModelState);
            return BadRequest(ModelState);
        }

        try
        {
            var inventoryItem = await context.Inventory
                .FirstOrDefaultAsync(i => i.ProductID == request.ProductId);

            if (inventoryItem == null)
            {
                logger.LogError("Inventory record for ProductID {ProductID} not found.", request.ProductId);
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
                        return StatusCode(StatusCodes.Status400BadRequest,
                            "Cannot release more than reserved quantity.");
                    }

                    inventoryItem.StockQuantity += request.Quantity;
                    inventoryItem.ReservedQuantity -= request.Quantity;
                    break;

                default:
                    logger.LogWarning("Invalid OperationType: {OperationType} for ProductID: {ProductID}",
                        request.OperationType, request.ProductId);
                    return StatusCode(StatusCodes.Status400BadRequest,
                        "Invalid operation type specified.");
            }

            context.Inventory.Update(inventoryItem);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "Inventory change executed for ProductID: {ProductID}, Operation: {OperationType}",
                request.ProductId, request.OperationType);

            return Ok(new { Message = "Operation executed successfully." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error executing inventory change for ProductID: {ProductID}, Operation: {OperationType}",
                request.ProductId, request.OperationType);
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"Error processing inventory change: {ex.Message}");
        }
    }
}