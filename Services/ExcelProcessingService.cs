using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using mutual_fund_backend.Data;
using mutual_fund_backend.Models;
using mutual_fund_backend.Services;
using Newtonsoft.Json;

public class ExcelProcessingService
{
    private readonly AppDbContext _context;

    public ExcelProcessingService(AppDbContext context)
    {
        _context = context;
    }
    public async Task ProcessExcel(string filePath, int amc_Id, int portfolio_Type_Id)
    {
        if (!File.Exists(filePath)) return;

        // 1. Create Upload Record
        var uploadEntry = new UploadEntry
        {
            fileName = Path.GetFileName(filePath),
            created_at = DateTime.Now
        };
        _context.Uploads.Add(uploadEntry);
        await _context.SaveChangesAsync();

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        // === STATE VARIABLES ===
        int currentUploadId = uploadEntry.id;
        Fund currentFund = null;
        PortfolioSnapshot currentSnapshot = null;
        InstrumentHeader currentSection = null;
        DateTime? currentAsOnDate = null;
        ColumnMapping currentMap = null;

        do // Loop Sheets
        {
            // Reset per sheet (except Upload ID)
            currentFund = null;
            currentSnapshot = null;
            currentSection = null;
            currentAsOnDate = null;
            currentMap = null;

            string sheetName = reader.Name;

            while (reader.Read()) // Loop Rows
            {
                var rowValues = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                    rowValues.Add(ReadCellAsDisplayed(reader, i));

                Console.WriteLine(rowValues.ToString());
                // Analyze semantic type
                var result = ExcelSemanticStructureDetector.Analyze(rowValues);

                switch (result.Type)
                {
                    case SemanticRowType.AmcName:
                        // Logic: Create or Get Fund
                        if (currentFund == null)
                        {
                            if (result.LooksLikeFund)
                            {
                                currentFund = await GetOrCreateFund(amc_Id, result.Value);
                            }
                        }
                        // 🟠 CASE 2: Already have a fund? -> Check if it's Data or Section
                        else
                        {
                            // Check: Is it actually a Data Row? (User Requirement)
                            if (ExcelSemanticStructureDetector.IsDataRow(rowValues))
                            {
                                if (currentSnapshot != null && currentMap != null)
                                {
                                    await ProcessDataRow(rowValues, currentMap, currentSnapshot.id, currentSection.id);
                                }
                            }
                            // Check: If not Data, treat as SECTION (e.g. "Mutual Fund Units")
                            else
                            {
                                if (currentSnapshot != null)
                                {
                                    currentSection = await _context.InstrumentHeaders
        .FirstOrDefaultAsync(h => h.instrument_header_name == result.Value);
                                    if (currentSection == null)
                                    {
                                        currentSection = new InstrumentHeader
                                        {
                                            // snapshot_id = currentSnapshot.id, // <--- REMOVE THIS LINE
                                            instrument_header_name = result.Value
                                        };
                                        _context.InstrumentHeaders.Add(currentSection);
                                        await _context.SaveChangesAsync();
                                    }
                                }
                            }
                        }
                        //if (currentFund==null && result.LooksLikeFund)
                        //{
                        //    string fundName = result.Value;
                        //    currentFund = await GetOrCreateFund(amc_Id, fundName);
                        //}
                        break;

                    case SemanticRowType.AsOnDate:
                        // Logic: Create Snapshot
                        if(currentAsOnDate == null)
                        {
                            if (DateTime.TryParse(result.Value.Replace("Monthly Portfolio Statement as on", "").Trim(), out DateTime dt))
                            {
                                currentAsOnDate = dt;
                            }
                            else
                            {
                                // Fallback parser if the semantic detector extracted raw text
                                ExcelSemanticStructureDetector.TryParseAsOnDate(result.Value, out dt);
                                currentAsOnDate = dt;
                            }
                            if (currentFund != null && currentAsOnDate != null)
                            {
                                currentSnapshot = new PortfolioSnapshot
                                {
                                    fund_id = currentFund.id,
                                    upload_id = currentUploadId,
                                    as_on_date = currentAsOnDate.Value,
                                    sheet_name = sheetName
                                };
                                _context.PortfolioSnapshots.Add(currentSnapshot);
                                await _context.SaveChangesAsync();
                            }
                        }
                        break;

                    case SemanticRowType.Section:
                        // Logic: Create Instrument Header (e.g., "Equity & Equity Related")
                        if (currentSnapshot != null)
                        {

                            //currentSection = new InstrumentHeader
                            //{
                            //    snapshot_id = currentSnapshot.id,
                            //    instrument_header_name = result.Value
                            //};
                            //_context.InstrumentHeaders.Add(currentSection);
                            //await _context.SaveChangesAsync();

                            currentSection = await _context.InstrumentHeaders
        .FirstOrDefaultAsync(h => h.instrument_header_name == result.Value);

                            if (currentSection == null)
                            {
                                currentSection = new InstrumentHeader
                                {
                                    // snapshot_id = currentSnapshot.id, // <--- REMOVE THIS LINE
                                    instrument_header_name = result.Value
                                };
                                _context.InstrumentHeaders.Add(currentSection);
                                await _context.SaveChangesAsync();
                            }
                        }
                        break;

                    case SemanticRowType.Header:
                        // Logic: Map columns based on names found in this row
                        currentMap = MapColumns(rowValues);
                        break;

                    case SemanticRowType.Data:
                        // Logic: Insert Holding
                        if (currentSnapshot != null && currentMap != null)
                        {
                            await ProcessDataRow(rowValues, currentMap, currentSnapshot.id, currentSection?.id);
                        }
                        break;
                    case SemanticRowType.Grand_Total:
                        if (currentSnapshot != null && currentMap != null)
                        {
                            // 1. Identify which column has the Market Value
                            // (Usually Grand Total is in the Market Value column)
                            string valStr = GetValue(rowValues, currentMap.MarketValIdx);

                            // 2. Parse it
                            double? totalVal = ParseDouble(valStr);

                            if (totalVal.HasValue)
                            {
                                // 3. Update the EXISTING snapshot object
                                // EF Core is tracking 'currentSnapshot', so we just set the property 
                                // and call SaveChangesAsync() to perform an UPDATE query.
                                currentSnapshot.grand_total = totalVal.Value;

                                await _context.SaveChangesAsync();
                            }
                        }
                        break;
                }
            }

        } while (reader.NextResult());
    }

    private async Task ProcessDataRow(List<string> row, ColumnMapping map, int snapshotId, int? headerId)
    {
        // 1. Extract Values safely
        string name = GetValue(row, map.NameIdx);
        string isin = GetValue(row, map.IsinIdx);
        string industryOrRating = GetValue(row, map.IndustryRatingIdx);
        string qtyStr = GetValue(row, map.QtyIdx);
        string marketValStr = GetValue(row, map.MarketValIdx);
        string pctStr = GetValue(row, map.PctIdx);
        string ytmStr = GetValue(row, map.YtmIdx);

        if (string.IsNullOrEmpty(name)) return;

        // 2. Handle Industry (Only if valid text)
        int? industryId = null;
        if (!string.IsNullOrEmpty(industryOrRating) && industryOrRating.Length > 2)
        {
            // Simple heuristic: If it's a rating (AAA, A1+), store in rating field, else industry
            // For now, we store in Industry table if it looks like text
            var industry = await _context.Industries.FirstOrDefaultAsync(i => i.industry_name == industryOrRating);
            if (industry == null)
            {
                industry = new Industry { industry_name = industryOrRating };
                _context.Industries.Add(industry);
                await _context.SaveChangesAsync();
            }
            industryId = industry.id;
        }

        // 3. Handle Instrument Master (Upsert based on ISIN + Name)
        // Note: If ISIN is empty, we might use Name as unique key
        var instrument = await _context.Instruments
            .FirstOrDefaultAsync(i => i.isin == isin && i.instrument_name == name);

        if (instrument == null)
        {
            instrument = new InstrumentMaster
            {
                instrument_name = name,
                isin = isin,
                industry_id = industryId,
                rating = (industryOrRating != null && industryOrRating.Contains("AA")) ? industryOrRating : null
            };
            _context.Instruments.Add(instrument);
            await _context.SaveChangesAsync();
        }

        // 4. Insert Holding
        var holding = new PortfolioHolding
        {
            snapshot_id = snapshotId,
            instrument_header_id = headerId,
            instrument_master_id = instrument.id,
            qty = ParseDouble(qtyStr),
            market_fair_net_asset = ParseDouble(marketValStr),
            rounded_per_to_net_asset = ParseDouble(pctStr),
            ytm = ParseDouble(ytmStr),
            raw_row_json = JsonConvert.SerializeObject(row) // Save raw data just in case
        };

        _context.PortfolioHoldings.Add(holding);
        // Batch saving is better for performance, but line-by-line is safer for debugging logic now
        await _context.SaveChangesAsync();
    }

    private async Task<Fund> GetOrCreateFund(int amcId, string fundName)
    {
        var fund = await _context.Funds.FirstOrDefaultAsync(f => f.fund_name == fundName && f.amc_id == amcId);
        if (fund == null)
        {
            fund = new Fund { amc_id = amcId, fund_name = fundName };
            _context.Funds.Add(fund);
            await _context.SaveChangesAsync();
        }
        return fund;
    }

    private ColumnMapping MapColumns(List<string> row)
    {
        var map = new ColumnMapping();
        for (int i = 0; i < row.Count; i++)
        {
            string val = row[i]?.ToLower() ?? "";
            if (val.Contains("name of the instrument")) map.NameIdx = i;
            else if (val.Contains("isin")) map.IsinIdx = i;
            else if (val.Contains("industry") || val.Contains("rating")) map.IndustryRatingIdx = i;
            else if (val.Contains("quantity")) map.QtyIdx = i;
            else if (val.Contains("market") || val.Contains("fair value")) map.MarketValIdx = i;
            else if (val.Contains("% to net assets")) map.PctIdx = i;
            else if (val.Contains("ytm")) map.YtmIdx = i;
        }
        return map;
    }

    private double? ParseDouble(string val)
    {
        if (string.IsNullOrWhiteSpace(val)) return null;
        val = val.Replace("%", "").Replace(",", "").Trim();
        if (double.TryParse(val, out double result)) return result;
        return null;
    }

    private string GetValue(List<string> row, int idx)
    {
        if (idx >= 0 && idx < row.Count) return row[idx];
        return null;
    }

    private string ReadCellAsDisplayed(IExcelDataReader reader, int index)
    {
        var value = reader.GetValue(index);
        if (value == null) return null;
        string format = reader.GetNumberFormatString(index)?.ToLower();

        if (value is double d && format != null && format.Contains("%"))
            return (d * 100).ToString("0.##") + "%";

        if (value is double dateVal && format != null && (format.Contains("yy") || format.Contains("dd")))
            return DateTime.FromOADate(dateVal).ToString("dd-MM-yyyy");

        return value.ToString();
    }

    // Helper class for dynamic column mapping
    private class ColumnMapping
    {
        public int NameIdx { get; set; } = -1;
        public int IsinIdx { get; set; } = -1;
        public int IndustryRatingIdx { get; set; } = -1; // Acts as both depending on section
        public int QtyIdx { get; set; } = -1;
        public int MarketValIdx { get; set; } = -1;
        public int PctIdx { get; set; } = -1;
        public int YtmIdx { get; set; } = -1;
    }
}
