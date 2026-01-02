using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using mutual_fund_backend.Data;
using mutual_fund_backend.Models;
using mutual_fund_backend.Services;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

public class ExcelProcessingService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ExcelProcessingService> _logger;
    public ExcelProcessingService(AppDbContext context, ILogger<ExcelProcessingService> logger)
    {
        _context = context;
        _logger = logger;
    }
    public async Task ProcessExcel(string filePath, int amc_Id, int portfolio_Type_Id)
    {
        int rowCount = 0;
        //var warnings = new List<string>();
        if (!File.Exists(filePath))
        {
            _logger.LogError("File not found at path: {FilePath}", filePath);
            return;
        }

        // 1. Start a Database Transaction
        // This ensures that if we find a duplicate file halfway through, 
        // we can rollback any partial data inserted (e.g. Holdings/Snapshots).
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {


            var dbFunds = await _context.Funds
            .Where(f => f.amc_id == amc_Id)
            .Select(f => f.scheme_name)
            .ToListAsync();

            // Normalize them immediately for matching
            var validFundNames = dbFunds
                .Select(n => NormalizeNameAggressive(n))
                .ToHashSet();
            // Fetch AMC Name and Portfolio Type Name for the filename
            var amcName = await _context.AMCs
                .Where(a => a.id == amc_Id)
                .Select(a => a.amcname)
                .FirstOrDefaultAsync() ?? "UnknownAMC";

            var portfolioTypeName = await _context.PortfolioDisclosures
                .Where(p => p.id == portfolio_Type_Id)
                .Select(p => p.portfolio_type)
                .FirstOrDefaultAsync() ?? "UnknownType";
            // 1. Create Upload Record
            var uploadEntry = new UploadEntry
            {
                fileName = Path.GetFileName(filePath),
                disclosure_portfolio_id = portfolio_Type_Id,
                created_at = DateTime.Now
            };
            _context.Uploads.Add(uploadEntry);
            await _context.SaveChangesAsync();

            DateTime? finalDateForFilename = null;
            bool isDuplicateFile = false;

            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read);
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                // === STATE VARIABLES ===
                int currentUploadId = uploadEntry.id;
                Fund currentFund = null;
                PortfolioSnapshot currentSnapshot = null;
                InstrumentHeader currentSection = null;
                DateTime? currentAsOnDate = null;
                ColumnMapping currentMap = null;

                do // Loop Sheets
                {
                    if (isDuplicateFile) break;
                    // Reset per sheet (except Upload ID)
                    currentFund = null;
                    currentSnapshot = null;
                    currentSection = null;
                    currentAsOnDate = null;
                    currentMap = null;

                    string sheetName = reader.Name;
                    // Flag to prevent adding the same warning 100 times inside the row loop if you wanted to check there
                    bool hasLoggedDataError = false;

                    while (reader.Read()) // Loop Rows
                    {
                        if (isDuplicateFile) break;
                        rowCount++;
                        // Print a "heartbeat" every 100 rows so you know it's alive
                        if (rowCount % 20 == 0) Console.WriteLine($"[Processing] Row {rowCount}...");
                        var rowValues = new List<string>();
                        for (int i = 0; i < reader.FieldCount; i++)
                            rowValues.Add(ReadCellAsDisplayed(reader, i));

                        //Console.WriteLine(rowValues.ToString());
                        // Analyze semantic type
                        var result = ExcelSemanticStructureDetector.Analyze(rowValues, validFundNames);

                        switch (result.Type)
                        {
                            case SemanticRowType.AmcName:
                                // Logic: Create or Get Fund
                                if (currentFund == null)
                                {
                                    //if (result.LooksLikeFund)
                                    //{
                                    currentFund = await GetOrCreateFund(amc_Id, result.Value);
                                    if (currentFund == null)
                                    {

                                        Console.WriteLine($"Skipping sheet because fund '{sheetName}' is not in Master Table.");
                                        _logger.LogWarning($"Sheet '{sheetName}': Fund '{result.Value}' was not found in Master DB.");
                                    }

                                    //}
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
                                Console.WriteLine("AMC Name:" + currentFund);
                                break;

                            case SemanticRowType.AsOnDate:
                                // Logic: Create Snapshot
                                if (currentAsOnDate == null)
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
                                    if (finalDateForFilename == null && currentAsOnDate != null && currentAsOnDate != DateTime.MinValue)
                                    {
                                        finalDateForFilename = currentAsOnDate;
                                        // Construct the Target Filename
                                        string dateStr = finalDateForFilename.Value.ToString("yyyy-MM-dd");
                                        string sanitizedAmc = SanitizeFileName(amcName);
                                        string sanitizedType = SanitizeFileName(portfolioTypeName);
                                        string extension = Path.GetExtension(filePath);
                                        string targetFileName = $"{sanitizedAmc}_{dateStr}_{sanitizedType}{extension}";

                                        // Check DB for Duplicate
                                        // We ignore the ID of the current upload we just inserted
                                        bool exists = await _context.Uploads
                                            .AnyAsync(u => u.fileName == targetFileName && u.id != currentUploadId);

                                        if (exists)
                                        {
                                            isDuplicateFile = true;
                                            _logger.LogWarning($"[Duplicate Abort] A file named '{targetFileName}' already exists in the system. Processing stopped.");
                                            break; // Break the Switch
                                        }
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
                                Console.WriteLine("AsOnDate:" + currentAsOnDate);
                                break;

                            case SemanticRowType.Section:
                                // Logic: Create Instrument Header (e.g., "Equity & Equity Related")
                                //                if (currentSnapshot != null)
                                //                {

                                //                    //currentSection = new InstrumentHeader
                                //                    //{
                                //                    //    snapshot_id = currentSnapshot.id,
                                //                    //    instrument_header_name = result.Value
                                //                    //};
                                //                    //_context.InstrumentHeaders.Add(currentSection);
                                //                    //await _context.SaveChangesAsync();

                                //                    currentSection = await _context.InstrumentHeaders
                                //.FirstOrDefaultAsync(h => h.instrument_header_name == result.Value);

                                //                    if (currentSection == null)
                                //                    {
                                //                        currentSection = new InstrumentHeader
                                //                        {
                                //                            // snapshot_id = currentSnapshot.id, // <--- REMOVE THIS LINE
                                //                            instrument_header_name = result.Value
                                //                        };
                                //                        _context.InstrumentHeaders.Add(currentSection);
                                //                        await _context.SaveChangesAsync();
                                //                    }
                                //                }
                                //                Console.WriteLine("Current Section:" + currentSection);

                                if (currentSnapshot != null)
                                {
                                    // 1. Try to find the header in the DB (Case-insensitive check is safer)
                                    // We use local variable first to avoid confusion
                                    var dbSection = await _context.InstrumentHeaders
                                        .FirstOrDefaultAsync(h => h.instrument_header_name == result.Value);

                                    if (dbSection != null)
                                    {
                                        // ✅ FOUND: Use this section for upcoming data rows
                                        currentSection = dbSection;
                                        // Optional: Log success if needed
                                        // _logger.LogInformation($"Using Header: {dbSection.instrument_header_name}"); 
                                    }
                                    else
                                    {
                                        // ❌ NOT FOUND: Do not create. Log the warning.
                                        _logger.LogWarning($"Sheet '{sheetName}': Unknown Instrument Header skipped: '{result.Value}'. Data rows under this section will have NULL header_id.");

                                        // ⚠️ IMPORTANT: Reset currentSection to null. 
                                        // This ensures that data rows don't accidentally get attached 
                                        // to the *previous* valid section found in the Excel file.
                                        currentSection = null;
                                    }
                                }
                                break;

                            case SemanticRowType.Header:
                                // Logic: Map columns based on names found in this row
                                currentMap = MapColumns(rowValues);
                                Console.WriteLine("Header:" + currentMap);
                                break;

                            case SemanticRowType.Data:
                                // Logic: Insert Holding
                                if (currentSnapshot != null && currentMap != null && currentSection != null)
                                {
                                    await ProcessDataRow(rowValues, currentMap, currentSnapshot.id, currentSection?.id);

                                    if (rowCount % 20 == 0)
                                    {
                                        await _context.SaveChangesAsync();
                                        Console.WriteLine($"[Saved] {rowCount} rows commit to DB.");
                                    }
                                }
                                else if (currentSnapshot == null && currentFund != null && !hasLoggedDataError)
                                {
                                    // Optional: Detect if we are hitting data rows but haven't found a date yet.
                                    // This catches cases where Date is at the bottom (Footer) which is rare but possible,
                                    // or completely missing.
                                    hasLoggedDataError = true; // Mark true so we don't spam the warning list
                                }
                                Console.WriteLine("Data Row:" + rowValues);
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
                                Console.WriteLine("Grand_Total:" + rowValues);
                                break;
                        }
                    }
                    // 1. If we found a valid Fund, but never found the Date
                    if (currentFund != null && currentAsOnDate == null)
                    {
                        _logger.LogWarning($"Sheet '{sheetName}': Skipped. 'As On Date' was not found.");
                    }
                    // 2. Edge case: Found Fund AND Date, but Snapshot creation failed (unlikely, but safe)
                    else if (currentFund != null && currentAsOnDate != null && currentSnapshot == null)
                    {
                        _logger.LogWarning($"Sheet '{sheetName}': Error. Fund and Date found, but Snapshot could not be created.");
                    }
                    await _context.SaveChangesAsync();
                } while (reader.NextResult());
            }
            // =========================================================
            // 4. RENAME FILE LOGIC (Safe to do now that file is closed)
            // =========================================================
            if (isDuplicateFile)
            {
                // ROLLBACK: Undo database changes (UploadEntry, Snapshots, Holdings)
                await transaction.RollbackAsync();

                // DELETE: Remove the temporary file from the server
                try { File.Delete(filePath); } catch { /* Ignore file lock issues */ }

                return; // Return the specific duplicate warning
            }
            else
            {
                if (finalDateForFilename.HasValue)
                {
                    try
                    {
                        string directory = Path.GetDirectoryName(filePath);
                        string extension = Path.GetExtension(filePath);

                        // Format: AMCName_AsOnDate_Portfolio_Type
                        string dateStr = finalDateForFilename.Value.ToString("yyyy-MM-dd");
                        string sanitizedAmc = SanitizeFileName(amcName);
                        string sanitizedType = SanitizeFileName(portfolioTypeName);

                        string newFileName = $"{sanitizedAmc}_{dateStr}_{sanitizedType}{extension}";
                        string newFilePath = Path.Combine(directory, newFileName);

                        // Check if file exists, append counter if needed to prevent crash
                        if (File.Exists(newFilePath))
                        {
                            // Option 1: Overwrite (Delete old, move new)
                            File.Delete(newFilePath);

                            // Option 2: Append timestamp to make unique (if you prefer not to overwrite)
                            // newFileName = $"{sanitizedAmc}_{dateStr}_{sanitizedType}_{DateTime.Now.Ticks}{extension}";
                            // newFilePath = Path.Combine(directory, newFileName);
                        }

                        // Perform the Rename (Move)
                        File.Move(filePath, newFilePath);

                        // Update Database Record
                        uploadEntry.fileName = newFileName;
                        _context.Uploads.Update(uploadEntry);
                        await _context.SaveChangesAsync();

                        Console.WriteLine($"[Success] File renamed to: {newFileName}");
                    }
                    catch (Exception ex)
                    {
                        // If renaming fails, we log it but don't fail the whole process
                        _logger.LogInformation($"Processing successful, but failed to rename file: {ex.Message}");
                        Console.WriteLine($"[Error] Could not rename file: {ex.Message}");
                    }
                }

                else
                {
                    _logger.LogError("Could not rename file because no 'As On Date' was found in the Excel data.");
                }
            }
            // COMMIT: Persist all data
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError($"Critical Error: {ex.Message}");
            // Cleanup temp file on error
            if (File.Exists(filePath)) File.Delete(filePath);
        }

        return;
    }
    // Helper for sanitizing filenames (Duplicate from controller or make this static public in a utility class)
    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrEmpty(value)) return "Unknown";
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value.Replace(" ", "_");
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
        string ytcStr = GetValue(row, map.YtcIdx);

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
        //await _context.SaveChangesAsync();
    }

    //private async Task<Fund> GetOrCreateFund(int amcId, string fundName)
    //{
    //    // 1. Fix Syntax: Get Index of Hyphen first
    //    string cleanExcelName = fundName;
    //    int hyphenIndex = fundName.IndexOf('-');

    //    // Safety: Only substring if hyphen exists
    //    if (hyphenIndex > 0)
    //    {
    //        cleanExcelName = fundName.Substring(0, hyphenIndex);
    //    }

    //    // 2. Normalization: Essential for matching Excel to DB
    //    cleanExcelName = cleanExcelName.Trim();
    //    Console.WriteLine($"[INFO] Looking up Fund: '{fundName}' (Cleaned: '{cleanExcelName}')");

    //    // 3. Database Query
    //    // Note: We use .FirstOrDefaultAsync() (Async version)
    //    var match = await _context.Funds
    //        .Where(f => f.amc_id == amcId) // 🛑 IMPORTANT: Filter by AMC first!
    //        .Where(f => f.scheme_name.Contains(cleanExcelName) || cleanExcelName.Contains(f.scheme_name))
    //        .FirstOrDefaultAsync();

    //    if (match != null)
    //    {
    //        return match;
    //    }

    //    Console.WriteLine($"[WARNING] Could not find a matching fund in DB for: {fundName} (Cleaned: {cleanExcelName})");
    //    //string warning = $"[WARNING] Could not find a matching fund in DB for: {fundName} (Cleaned: {cleanExcelName})";
    //    //warnings.Add(warning);
    //    return null;
    //}
    //

    private async Task<Fund> GetOrCreateFund(int amcId, string fundName)
    {
        // 1. RAW CLEANING: First, aggressive clean to handle "CRISIL-IBX" vs "CRISIL IBX"
        string cleanExcelName = NormalizeNameAggressive(fundName);

        // 2. Fetch ALL funds for this AMC (Filtering in memory is safer here)
        var amcFunds = await _context.Funds
            .Where(f => f.amc_id == amcId)
            .ToListAsync();

        // 3. FIND MATCH: Compare aggressive clean versions
        var match = amcFunds
            .Select(f => new { Fund = f, CleanDbName = NormalizeNameAggressive(f.scheme_name) })
            // Match if one contains the other
            .Where(x => x.CleanDbName.Contains(cleanExcelName) || cleanExcelName.Contains(x.CleanDbName))
            // Order by length to get the most specific match (avoids generic "Axis Bond" matching specific funds)
            .OrderByDescending(x => x.CleanDbName.Length)
            .Select(x => x.Fund)
            .FirstOrDefault();

        if (match != null) return match;

        Console.WriteLine($"[WARNING] Could not find a matching fund in DB for: {fundName} (Cleaned: {cleanExcelName})");
        return null;
    }

    // 🟢 HELPER: Aggressive Normalization
    // Removes hyphens, dots, special chars so "CRISIL-IBX" == "CRISIL IBX"
    private string NormalizeNameAggressive(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        string str = input.ToLower();

        // 1. Standardize Months (Full Name -> 3 Letters)
        // This solves "September 2027" vs "Sep 2027"
        str = str.Replace("january", "jan")
                 .Replace("february", "feb")
                 .Replace("march", "mar")
                 .Replace("april", "apr")
                 .Replace("june", "jun")
                 .Replace("july", "jul")
                 .Replace("august", "aug")
                 .Replace("september", "sep").Replace("sept", "sep") // Handle "Sept" too
                 .Replace("october", "oct")
                 .Replace("november", "nov")
                 .Replace("december", "dec");

        // 2. Replace separators with spaces
        str = str.Replace('-', ' ').Replace('_', ' ').Replace('.', ' ').Replace(':', ' ');

        // 3. Force space between Letters and Numbers ("IBX50" -> "IBX 50")
        str = System.Text.RegularExpressions.Regex.Replace(str, @"([a-z])([0-9])", "$1 $2");
        str = System.Text.RegularExpressions.Regex.Replace(str, @"([0-9])([a-z])", "$1 $2");

        // 4. Remove non-alphanumeric characters
        str = System.Text.RegularExpressions.Regex.Replace(str, @"[^a-z0-9\s]", "");

        // 5. Collapse multiple spaces
        str = System.Text.RegularExpressions.Regex.Replace(str, @"\s+", " ");

        return str.Trim();
    }
    //var fund = await _context.Funds.FirstOrDefaultAsync(f => f.scheme_name == fundName && f.amc_id == amcId);
    //if (fund == null)
    //{
    //    fund = new Fund { amc_id = amcId, scheme_name = fundName };
    //    _context.Funds.Add(fund);
    //    await _context.SaveChangesAsync();
    //}
    //return fund;

    private ColumnMapping MapColumns(List<string> row)
    {
        var map = new ColumnMapping();
        for (int i = 0; i < row.Count; i++)
        {
            string val = row[i]?.ToLower() ?? "";
            if (val.Contains("name of the instrument")) map.NameIdx = i;
            else if (val.Contains("isin")) map.IsinIdx = i;
            else if (val.Contains("industry") || val.Contains("rating")) map.IndustryRatingIdx = i;
            else if (val.Contains("quantity") || val.Contains("qty")) map.QtyIdx = i;
            else if (val.Contains("market") || val.Contains("fair value")) map.MarketValIdx = i;
            else if (val.Contains("% to net assets")) map.PctIdx = i;
            else if (val.Contains("ytm")) map.YtmIdx = i;
            else if (val.Contains("ytc")) map.YtcIdx = i;
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

        public int YtcIdx { get; set; } = -1;
    }
}
