using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Models;
using System.Data;

namespace OrderService.Controllers.SAGA;

[ApiController]
[Route("[controller]/saga")]
public class OrdersController(OrderDbContext context, ILogger<OrdersController> logger) : ControllerBase
{
    public record OrderRequestSAGA(
        int UserId,
        int ProductId,
        int Quantity,
        decimal Amount
    );

    public record CreateResponse(int OrderId, string OrderStatus);

    [HttpPost("create")]
    public async Task<IActionResult> CreateOrderSaga([FromBody] OrderRequestSAGA request)
    {
        logger.LogInformation("SAGA: Attempting to create order for UserID: {UserID}, ProductID: {ProductID}",
            request.UserId, request.ProductId);

        if (!ModelState.IsValid)
        {
            logger.LogWarning("SAGA: Create order request failed validation: {ModelState}", ModelState);
            return BadRequest(ModelState);
        }

        try
        {
            var order = new Order
            {
                UserID = request.UserId,
                ProductID = request.ProductId,
                Quantity = request.Quantity,
                Amount = request.Amount,
                OrderStatus = OrderStatuses.Confirmed // Or a new status like OrderStatuses.SagaConfirmed
            };

            context.Orders.Add(order);
            await context.SaveChangesAsync();

            logger.LogInformation("SAGA: Order {OrderID} created successfully with status {OrderStatus}", order.OrderID,
                order.OrderStatus);
            return Ok(new CreateResponse(order.OrderID, order.OrderStatus));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SAGA: Error in CreateOrderSaga for UserID: {UserID}, ProductID: {ProductID}",
                request.UserId, request.ProductId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                "SAGA: Error processing order creation: " + ex.Message);
        }
    }

    [HttpPost("cancel/{orderId:int}")]
    public async Task<IActionResult> CancelOrderSaga(int orderId)
    {
        logger.LogInformation("SAGA: Attempting to cancel order with OrderID: {OrderID}", orderId);

        try
        {
            var order = await context.Orders.FindAsync(orderId);

            if (order == null)
            {
                logger.LogWarning("SAGA: Order with OrderID: {OrderID} not found for cancellation.", orderId);

                return Ok(new { Message = $"Order {orderId} not found, presumed cancelled or never existed." });
            }

            if (order.OrderStatus == OrderStatuses.Cancelled)
            {
                logger.LogInformation("SAGA: Order {OrderID} is already cancelled.", orderId);
                
                return Ok(new { Message = $"Order {orderId} was already cancelled." });
            }

            order.OrderStatus = OrderStatuses.Cancelled;
            await context.SaveChangesAsync();

            logger.LogInformation("SAGA: Order {OrderID} cancelled successfully.", orderId);
            
            return Ok(new { Message = $"Order {orderId} cancelled successfully." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SAGA: Error in CancelOrderSaga for OrderID: {OrderID}", orderId);

            return StatusCode(StatusCodes.Status500InternalServerError,
                $"SAGA: Error processing order cancellation for {orderId}: {ex.Message}");
        }
    }
}