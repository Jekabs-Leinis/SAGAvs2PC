using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PaymentService.Models;

public class Payment
{
    public static class Status 
    {
        public const string PENDING = "PENDING";
        public const string SUCCESSFUL = "SUCCESSFUL";
        public const string CANCELLED = "CANCELLED";
        public const string REFUNDED = "REFUNDED";
    }
    
    [Key]
    public int PaymentID { get; set; }

    [Required]
    public int OrderID { get; set; }

    [Required]
    public decimal Amount { get; set; }

    [Required]
    public string PaymentStatus { get; set; }
} 