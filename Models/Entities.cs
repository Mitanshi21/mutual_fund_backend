using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mutual_fund_backend.Models
{
    [Table("tbl_amc")]
    public class Amc
    {
        public int id { get; set; }
        public string amcname { get; set; }
    }

    [Table("tbl_fund")]
    public class Fund
    {
        public int id { get; set; }
        public int amc_id { get; set; }
        public string fund_name { get; set; }
    }

    [Table("tbl_portfolio_snapshot")]
    public class PortfolioSnapshot
    {
        public int id { get; set; }
        public int fund_id { get; set; }
        public int upload_id { get; set; }
        public DateTime as_on_date { get; set; }
        public string sheet_name { get; set; }
        public double grand_total { get; set; }
    }

    [Table("tbl_instrument_header")]
    public class InstrumentHeader
    {
        public int id { get; set; }
        public string instrument_header_name { get; set; } // This is your "Section"
    }

    [Table("tbl_industry")]
    public class Industry
    {
        public int id { get; set; }
        public string industry_name { get; set; }
    }

    [Table("tbl_instrument_master")]
    public class InstrumentMaster
    {
        [Key] // ✅ Marks this as the Primary Key
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int id { get; set; }
        public string instrument_name { get; set; }
        public string? isin { get; set; }
        public int? industry_id { get; set; }
        public string? rating { get; set; }
    }

    // Note: You might need a sequence or logic to generate InstrumentMaster IDs 
    // since your SQL script didn't set IDENTITY(1,1) for this table. 
    // For this code, I will assume you fix the SQL to make ID IDENTITY(1,1) 
    // OR we generate a random one.

    [Table("tbl_portfolio_holding")]
    public class PortfolioHolding
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int id { get; set; }
        public int? snapshot_id { get; set; }
        public int? instrument_header_id { get; set; }
        public int? instrument_master_id { get; set; }
        public double? qty { get; set; }
        public double? market_fair_net_asset { get; set; }
        public double? rounded_per_to_net_asset { get; set; }
        public double? ytm { get; set; }
        public double? ytc { get; set; }
        public string raw_row_json { get; set; }
    }

    [Table("tbl_upload")]
    public class UploadEntry
    {
        public int id { get; set; }
        public string fileName { get; set; }
        public DateTime created_at { get; set; }
    }
}
