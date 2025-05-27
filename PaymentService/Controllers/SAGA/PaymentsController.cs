using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentService.Data;
using PaymentService.Models;
using System.Collections.Concurrent;
using System.Data.Common;

namespace PaymentService.Controllers.SAGA;

[ApiController]
[Route("[controller]/saga")]
public class PaymentsController(PaymentDbContext context, ILogger<PaymentsController> logger)
    : ControllerBase
{
    public record PaymentRequestSAGA(
        int? PaymentId,
        int OrderId,
        decimal Amount,
        string PaymentStatus = Payment.Status.PENDING);

    [HttpPost]
    public async Task<IActionResult> ExecutePayment([FromBody] PaymentRequestSAGA request)
    {
        if (!ModelState.IsValid)
        {
            logger.LogWarning("Payment request failed validation: {ModelState}", ModelState);
            return BadRequest(ModelState);
        }

        try
        {
            Payment? payment;
            if (request.PaymentId.HasValue)
            {
                payment = await context.Payments.FindAsync(request.PaymentId);

                if (payment == null)
                {
                    logger.LogError("Payment with ID {PaymentID} not found.", request.PaymentId);
                    return StatusCode(StatusCodes.Status404NotFound, $"Payment with ID {request.PaymentId} not found.");
                }

                payment.OrderID = request.OrderId;
                payment.Amount = request.Amount;
                payment.PaymentStatus = request.PaymentStatus;
                context.Payments.Update(payment);
            }
            else
            {
                payment = new Payment
                {
                    OrderID = request.OrderId,
                    Amount = request.Amount,
                    PaymentStatus = Payment.Status.PENDING
                };
                context.Payments.Add(payment);
            }
            
            // Simulate failure 15% of the time
            if (new Random(request.OrderId).Next(100) < 15)
            {
                throw new Exception("Simulated payment failure for testing purposes.");
            }

            await context.SaveChangesAsync();

            logger.LogInformation("Payment executed for OrderID: {OrderID}, PaymentID: {PaymentID}", request.OrderId, payment.PaymentID);

            return Ok( new { paymentId = payment.PaymentID });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing payment for OrderID: {OrderID}", request.OrderId);
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error processing payment: {ex.Message}");
        }
    }

    [HttpPost("cancel/{paymentId:int}")]
    public async Task<IActionResult> CancelPayment(int paymentId)
    {
        try
        {
            var payment = await context.Payments.FindAsync(paymentId);

            if (payment == null)
            {
                logger.LogWarning("Payment with ID {PaymentID} not found for cancellation.", paymentId);
                return Ok(new { Message = "Nothing to cancel." });
            }
            
            payment.PaymentStatus = Payment.Status.CANCELLED;
            context.Payments.Update(payment);
            await context.SaveChangesAsync();

            logger.LogInformation("Payment cancelled for PaymentID: {PaymentID}", paymentId);

            return Ok(new { Message = "Payment cancelled successfully." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error compensating payment for PaymentID: {PaymentID}", paymentId);
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error processing cancellation: {ex.Message}");
        }
    }
}
