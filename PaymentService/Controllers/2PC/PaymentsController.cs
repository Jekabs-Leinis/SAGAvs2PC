using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentService.Data;
using PaymentService.Models;
using System.Collections.Concurrent;
using System.Data.Common;

namespace PaymentService.Controllers._2PC;

[ApiController]
[Route("[controller]/2pc")]
public class PaymentsController(PaymentDbContext context, ILogger<PaymentsController> logger)
    : ControllerBase
{
    public record PaymentRequest(
        int? PaymentId,
        int OrderId,
        decimal Amount,
        string PaymentStatus = Payment.Status.PENDING);

    public record PaymentResponse(int PaymentId, string TransactionId);

    [HttpPost]
    public async Task<IActionResult> CreateOrPreparePayment([FromBody] PaymentRequest request)
    {
        if (request.PaymentId.HasValue)
        {
            return await PrepareExistingPayment(request);
        }
        else
        {
            return await CreateAndPreparePayment(request);
        }
    }

    private async Task<IActionResult> PrepareExistingPayment(PaymentRequest paymentRequest)
    {
        logger.LogInformation("Attempting to prepare existing payment with PaymentID: {PaymentID}",
            paymentRequest.PaymentId);

        var transactionId = Guid.NewGuid().ToString("N");
        var paymentResponse = new PaymentResponse(paymentRequest.PaymentId ?? 0, transactionId);

        try
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

            logger.LogInformation(
                "Payment {PaymentID} successfully prepared with PostgreSQL PREPARE TRANSACTION. TransactionId: {TransactionId}",
                paymentRequest.PaymentId, transactionId);

            var existingPayment = await context.Payments
                .FromSqlRaw("""SELECT * FROM "Payments" WHERE "PaymentID" = {0} FOR UPDATE""", paymentRequest.PaymentId)
                .FirstOrDefaultAsync();

            if (existingPayment == null)
            {
                logger.LogError("Payment with ID {PaymentID} not found.", paymentRequest.PaymentId);

                await context.Database.RollbackTransactionAsync();
                await context.Database.CloseConnectionAsync();

                return StatusCode(StatusCodes.Status400BadRequest,
                    $"Payment with ID {paymentRequest.PaymentId} not found.");
            }

            context.Payments.Update(existingPayment);

            existingPayment.OrderID = paymentRequest.OrderId;
            existingPayment.Amount = paymentRequest.Amount;
            existingPayment.PaymentStatus = paymentRequest.PaymentStatus;

            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlRawAsync($"PREPARE TRANSACTION '{transactionId}'");

            logger.LogInformation(
                "Payment {PaymentID} prepared for update with PostgreSQL PREPARE TRANSACTION. TransactionId: {TransactionId}",
                paymentRequest.PaymentId, transactionId);

            return Ok(paymentResponse);
        }
        catch (Exception ex)
        {
            await RollbackPreparedTransaction(transactionId);
            await context.Database.CloseConnectionAsync();

            logger.LogError(ex, "Error in PrepareExistingPayment operation for PaymentID: {PaymentID}",
                paymentRequest.PaymentId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                $"Error processing payment preparation: {ex.Message}");
        }
    }

    private async Task<IActionResult> CreateAndPreparePayment(PaymentRequest request)
    {
        bool transactionCompleted = false;
        
        logger.LogInformation("Attempting to create and prepare payment for OrderID: {OrderID}", request.OrderId);

        if (!ModelState.IsValid)
        {
            logger.LogWarning("Create payment request failed validation: {ModelState}", ModelState);
            return BadRequest(ModelState);
        }

        var transactionId = Guid.NewGuid().ToString("N");

        try
        {
            var payment = new Payment
            {
                OrderID = request.OrderId,
                Amount = request.Amount,
                PaymentStatus = Payment.Status.PENDING
            };

            await context.Database.OpenConnectionAsync();
            await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

            context.Payments.Add(payment);
            await context.SaveChangesAsync();
            
            // Simulate failure 15% of the time
            if (new Random(request.OrderId).Next(100) < 15)
            {
                throw new Exception("Simulated payment failure for testing purposes.");
            }

            var result =await context.Database.ExecuteSqlRawAsync($"PREPARE TRANSACTION '{transactionId}'");

            logger.LogInformation(
                "Payment {PaymentID} prepared for creation with PostgreSQL PREPARE TRANSACTION. TransactionId: {TransactionId} result={result}",
                payment.PaymentID, transactionId, result);
            
            
            transactionCompleted = true;

            return Ok(new PaymentResponse(payment.PaymentID, transactionId));
        }
        catch (Exception ex)
        {
            await RollbackPreparedTransaction(transactionId);
            await context.Database.CloseConnectionAsync();
            
            logger.LogError(ex,
                "Error in CreateAndPreparePayment operation for OrderID: {OrderID}, {message}",
                request.OrderId, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError,
                "Error processing payment creation: " + ex.Message);
        }
        finally
        {
            if (!transactionCompleted)
            {
                await RollbackPreparedTransaction(transactionId);
            }
        }
    }

    private async Task RollbackPreparedTransaction(string transactionId)
    {
        try
        {
            logger.LogInformation("Rolling back prepared transaction: {TransactionId}", transactionId);
            await context.Database.ExecuteSqlRawAsync($"ROLLBACK TRANSACTION '{transactionId}'");
        }
        catch (Exception rollbackEx) when (
            (rollbackEx.Message.Contains("prepared transaction with identifier") &&
             rollbackEx.Message.Contains("does not exist")) ||
            (rollbackEx.Message.Contains("syntax error at or near") &&
             rollbackEx.Message.Contains(transactionId))
        )
        {
            logger.LogWarning("Rollback transaction {TransactionId} could not be found.", transactionId);
        }
        catch (Exception rollbackEx)
        {
            logger.LogError(rollbackEx, "Error during rollback of transaction {TransactionId}.", transactionId);
            throw;
        }
    }

    [HttpPost("commit/{transactionId}")]
    public async Task<IActionResult> CommitPayment(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
        {
            return BadRequest(new { Message = "Transaction ID cannot be null or empty." });
        }

        logger.LogInformation("Attempting to commit transaction : {transactionId}", transactionId);

        try
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync($"COMMIT PREPARED '{transactionId}'");
            await context.Database.CloseConnectionAsync();

            logger.LogInformation(
                "Successfully committed with PostgreSQL COMMIT PREPARED. TransactionId: {TransactionId}",
                transactionId);

            return Ok(new { Message = $"Transaction {transactionId} committed successfully." });
        }
        catch (Exception ex)
        {
            await RollbackPreparedTransaction(transactionId);
            await context.Database.CloseConnectionAsync();

            logger.LogError(ex,
                "CommitPayment: Error committing transaction {transactionID} with PostgreSQL COMMIT PREPARED",
                transactionId);

            return StatusCode(StatusCodes.Status500InternalServerError, $"Error committing payment: {ex.Message}");
        }
    }

    [HttpPost("abort/{transactionId}")]
    public async Task<IActionResult> AbortPayment(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
        {
            return BadRequest(new { Message = "Transaction ID cannot be null or empty." });
        }

        logger.LogInformation("Attempting to abort transaction : {transactionId}", transactionId);

        try
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync($"ROLLBACK PREPARED '{transactionId}'");

            logger.LogInformation(
                "Successfully aborted with PostgreSQL ROLLBACK PREPARED. TransactionId: {TransactionId}",
                transactionId);

            return Ok(new { Message = $"Transaction {transactionId} aborted successfully." });
        }
        catch (Exception ex)
        {
            await RollbackPreparedTransaction(transactionId);
            await context.Database.CloseConnectionAsync();

            logger.LogError(ex,
                "AbortPayment: Error aborting transaction {transactionId} with PostgreSQL ROLLBACK PREPARED",
                transactionId);

            return StatusCode(StatusCodes.Status500InternalServerError, $"Error aborting payment: {ex.Message}");
        }
    }
}