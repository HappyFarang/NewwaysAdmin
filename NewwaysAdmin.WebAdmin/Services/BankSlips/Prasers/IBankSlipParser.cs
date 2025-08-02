using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers
{
    public interface IBankSlipParser
    {
        /// <summary>
        /// Determines if this parser can handle the given slip format
        /// </summary>
        bool CanParse(string text, SlipCollection collection);

        /// <summary>
        /// Parses the OCR text into structured bank slip data
        /// </summary>
        BankSlipData? Parse(string text, string imagePath, SlipCollection collection);

        /// <summary>
        /// Gets the name of this parser for logging/debugging
        /// </summary>
        string GetParserName();

        /// <summary>
        /// Gets the supported slip format
        /// </summary>
        SlipFormat GetSupportedFormat();

        /// <summary>
        /// Validates if the parsed data meets minimum requirements
        /// </summary>
        bool ValidateParsedData(BankSlipData data);
    }
}