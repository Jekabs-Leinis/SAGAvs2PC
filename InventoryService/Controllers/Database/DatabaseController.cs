using InventoryService.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Controllers.Database;

[ApiController]
[Route("Inventory/[controller]")]
public class DatabaseController(InventoryDbContext context) : ControllerBase
{
    [HttpPost("execute-schema")]
    public IActionResult ExecuteSchema()
    {
        // Copy the contents of combined_schema.sql directly
        const string sql = """

                           CREATE TABLE "Inventory"
                           (
                               "ProductID"        SERIAL PRIMARY KEY,
                               "StockQuantity"    INTEGER     NOT NULL DEFAULT 0,
                               "ReservedQuantity" INTEGER     NOT NULL DEFAULT 0,
                               "Status"           VARCHAR(50) NOT NULL DEFAULT 'NONE'
                           );

                           DO
                           $$
                           BEGIN
                           FOR i IN 1..100 LOOP
                                   INSERT INTO "Inventory" ("StockQuantity", "ReservedQuantity", "Status")
                           SELECT FLOOR(50 + RANDOM() * 201), -- StockQuantity between 50 and 250
                                  FLOOR(RANDOM() * 51),       -- ReservedQuantity between 0 and 50
                                  'NONE'                      -- Example status
                           FROM generate_series(1, 10000); -- Generate 10,000 rows per batch
                           END LOOP;
                           END $$;
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
