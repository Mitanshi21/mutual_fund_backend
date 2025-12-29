using ExcelDataReader;
using mutual_fund_backend.Services;

public class ExcelProcessingService
{
    public async Task ProcessExcel(string filePath, int amc_Id, int portfolio_Type_Id)
    {
        string currentAmc = null;
        string currentSection = null;

        if (!File.Exists(filePath))
            return;

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        do
        {
            Console.WriteLine($"===== Sheet: {reader.Name} =====");

            while (reader.Read())
            {
                var rowValues = new List<string>();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    rowValues.Add(ReadCellAsDisplayed(reader, i));
                }

                // 🔹 Now plug into your semantic detector
                var result = ExcelSemanticStructureDetector.Analyze(rowValues);

                switch (result.Type)
                {
                    case SemanticRowType.AmcName:

                        if (result.LooksLikeFund)
                        {
                            if (string.IsNullOrEmpty(currentAmc))
                            {
                                // ✅ FIRST fund = AMC
                                currentAmc = result.Value;
                                Console.WriteLine($"[AMC] => {currentAmc}");
                            }
                            else
                            {
                                // ✅ Later fund = SECTION
                                currentSection = result.Value;
                                Console.WriteLine($"[Section] => {currentSection}");
                            }
                        }
                        break;

                    case SemanticRowType.Section:
                        currentSection = result.Value;
                        Console.WriteLine($"[Section] => {currentSection}");
                        break;

                    case SemanticRowType.Data:
                        Console.WriteLine($"[Data] => {string.Join(" | ", rowValues)}");
                        break;

                    case SemanticRowType.AsOnDate:
                        Console.WriteLine($"[As On Date] => {result.Value}");
                        break;

                    case SemanticRowType.Header:
                        Console.WriteLine($"[Header] => {string.Join(" | ", rowValues)}");
                        break;

                    case SemanticRowType.Skip:
                        Console.WriteLine($"[Skip]=>{string.Join(" | ", rowValues)}");
                        break;
                }


                await Task.Yield();

            }

        } while (reader.NextResult());
    }

    // =============================
    // PRESERVE EXCEL DISPLAY VALUE
    // =============================
    private string ReadCellAsDisplayed(IExcelDataReader reader, int index)
    {
        var value = reader.GetValue(index);
        if (value == null)
            return null;

        string format = reader.GetNumberFormatString(index)?.ToLower();

        // ✔ Percentage
        if (value is double d && format != null && format.Contains("%"))
        {
            return (d * 100).ToString("0.##") + "%";
        }

        // ✔ Date
        if (value is double dateVal && format != null &&
            (format.Contains("yy") || format.Contains("dd")))
        {
            return DateTime.FromOADate(dateVal).ToString("dd-MM-yyyy");
        }

        // ✔ Number (keep commas if Excel had them visually)
        return value.ToString();
    }
}
