using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mutual_fund_backend.Models
{
    [Table("tbl_amc")]
    public class AMC
    {
        [Key]
        public int id { get; set; }

        [Required]
        [MaxLength(255)]
        public string amcname { get; set; }
    }
}
