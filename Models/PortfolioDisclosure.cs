using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mutual_fund_backend.Models
{
    [Table("tbl_disclosure_portfolio")]
    public class PortfolioDisclosure
    {
        [Key]
        public int id { get; set; }

        [Required]
        [MaxLength(255)]
        public string portfolio_type { get; set; }
    }
}
