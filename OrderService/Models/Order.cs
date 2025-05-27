using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OrderService.Models;

public static class OrderStatuses
{
    public const string Pending = "PENDING";
    public const string Confirmed = "CONFIRMED";
    public const string Cancelled = "CANCELLED";
    public const string Paid = "PAID";
}

public class Order
{
    [Key]
    public int OrderID { get; set; }

    [Required]
    public int UserID { get; set; }

    [Required]
    public int ProductID { get; set; }

    [Required]
    public int Quantity { get; set; }

    [Required]
    [Column(TypeName = "decimal(10, 2)")]
    public decimal Amount { get; set; }

    [Required]
    public string OrderStatus { get; set; }
} 