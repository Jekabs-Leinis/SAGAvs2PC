using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderService.Data;

namespace OrderService.Controllers.Database;

[ApiController]
[Route("Orders/[controller]")]
public class DatabaseController(OrderDbContext context) : ControllerBase
{
    [HttpPost("execute-schema")]
    public IActionResult ExecuteSchema()
    {
        // Copy the contents of combined_schema.sql directly
        const string sql = """
                           CREATE TABLE "Orders"
                           (
                               "OrderID"     SERIAL PRIMARY KEY,
                               "UserID"      INTEGER        NOT NULL,
                               "ProductID"   INTEGER        NOT NULL,
                               "Quantity"    INTEGER        NOT NULL,
                               "Amount"      DECIMAL(10, 2) NOT NULL,
                               "OrderStatus" VARCHAR(50)    NOT NULL DEFAULT 'PENDING'
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
