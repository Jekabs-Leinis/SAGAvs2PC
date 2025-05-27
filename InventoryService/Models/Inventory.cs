using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryService.Models;

public class Inventory
{
    public static class OperationTypes 
    {
        public const string Reserve = "RESERVE";
        public const string Release = "RELEASE";
    }
    
    [Key]
    public int ProductID { get; set; }

    [Required]
    public int StockQuantity { get; set; }

    [Required]
    public int ReservedQuantity { get; set; }
} 