using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentService.Data;

namespace PaymentService.Controllers.Database;

[ApiController]
[Route("Payments/[controller]")]
public class DatabaseController(PaymentDbContext context) : ControllerBase
{
    [HttpPost("execute-schema")]
    public IActionResult ExecuteSchema()
    {
        // Copy the contents of combined_schema.sql directly
        const string sql = """
                           CREATE TABLE "Payments"
                           (
                               "PaymentID"     SERIAL PRIMARY KEY,
                               "OrderID"       INTEGER        NOT NULL,
                               "Amount"        DECIMAL(10, 2) NOT NULL,
                               "PaymentStatus" VARCHAR(50)    NOT NULL DEFAULT 'PENDING'
                           );
                           """;

        try
        {
            context.Database.OpenConnection();
            context.Database.ExecuteSqlRaw(sql);
            context.Database.CloseConnection();
            return Ok("Schema executed successfully.");
        }
        catch (Exception ex)
        {
            return BadRequest($"Error executing schema: {ex.Message}");
        }
    }
}
