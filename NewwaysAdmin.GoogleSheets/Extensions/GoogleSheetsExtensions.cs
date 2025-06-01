using NewwaysAdmin.GoogleSheets.Models;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.GoogleSheets.Extensions
{
    public static class GoogleSheetsExtensions
    {
        /// <summary>
        /// Create a SheetExportRequest from bank slip processing result
        /// </summary>
        public static SheetExportRequest ToSheetExportRequest(
            this BankSlipProcessingResult result,
            string username,
            string googleEmail,
            string collectionName,
            DateTime startDate,
            DateTime endDate,
            List<CheckboxColumn> checkboxColumns)
        {
            return new SheetExportRequest
            {
                Username = username,
                GoogleEmail = googleEmail,
                BankSlips = result.ProcessedSlips,
                StartDate = startDate,
                EndDate = endDate,
                CollectionName = collectionName,
                CheckboxColumns = checkboxColumns
            };
        }

        /// <summary>
        /// Create a basic checkbox column
        /// </summary>
        public static CheckboxColumn CreateCheckboxColumn(string title, int order = 0)
        {
            return new CheckboxColumn
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Title = title,
                Order = order,
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Get enabled checkbox columns in order
        /// </summary>
        public static List<CheckboxColumn> GetEnabledColumns(this UserCheckboxConfig config)
        {
            return config.Columns
                .Where(c => c.IsEnabled)
                .OrderBy(c => c.Order)
                .ToList();
        }

        /// <summary>
        /// Validate Google email format
        /// </summary>
        public static bool IsValidGoogleEmail(this string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
    }
}