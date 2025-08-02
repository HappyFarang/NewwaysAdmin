// NewwaysAdmin.WebAdmin/Services/BankSlips/Parsers/BankSlipParserFactory.cs
// Simplified factory that works with existing code structure

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers
{
    public class BankSlipParserFactory
    {
        private readonly ILogger<BankSlipParserFactory> _logger;
        private readonly IServiceProvider _serviceProvider;

        public BankSlipParserFactory(
            ILogger<BankSlipParserFactory> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Gets the appropriate parser for the given collection format
        /// </summary>
        public IBankSlipParser GetParser(SlipCollection collection)
        {
            try
            {
                if (collection.IsKBizFormat)
                {
                    var kbizParser = _serviceProvider.GetService<KBizSlipParser>();
                    if (kbizParser != null)
                    {
                        _logger.LogDebug("Selected K-BIZ parser for collection {CollectionName}", collection.Name);
                        return kbizParser;
                    }
                }

                // Default to original parser
                var originalParser = _serviceProvider.GetService<OriginalSlipParser>();
                if (originalParser != null)
                {
                    _logger.LogDebug("Selected original parser for collection {CollectionName}", collection.Name);
                    return originalParser;
                }

                // Fallback - create a default parser
                _logger.LogWarning("No parser service found, creating fallback parser");
                return new FallbackParser(_logger);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting parser for collection {CollectionName}", collection.Name);
                return new FallbackParser(_logger);
            }
        }

        /// <summary>
        /// Gets the best parser by analyzing the OCR text content
        /// </summary>
        public IBankSlipParser GetBestParser(string ocrText, SlipCollection collection)
        {
            try
            {
                // Try K-BIZ parser first if it can handle the text
                var kbizParser = _serviceProvider.GetService<KBizSlipParser>();
                if (kbizParser != null && kbizParser.CanParse(ocrText, collection))
                {
                    _logger.LogInformation("Auto-detected K-BIZ format for collection {CollectionName}", collection.Name);
                    return kbizParser;
                }

                // Try original parser
                var originalParser = _serviceProvider.GetService<OriginalSlipParser>();
                if (originalParser != null && originalParser.CanParse(ocrText, collection))
                {
                    _logger.LogDebug("Auto-detected original format for collection {CollectionName}", collection.Name);
                    return originalParser;
                }

                // Fallback to collection's designated format
                return GetParser(collection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in auto-detection, falling back to designated parser");
                return GetParser(collection);
            }
        }
    }

    /// <summary>
    /// Fallback parser that uses existing logic when services aren't available
    /// </summary>
    internal class FallbackParser : IBankSlipParser
    {
        private readonly ILogger _logger;

        public FallbackParser(ILogger logger)
        {
            _logger = logger;
        }

        public bool CanParse(string text, SlipCollection collection)
        {
            return !string.IsNullOrWhiteSpace(text);
        }

        public BankSlipData? Parse(string text, string imagePath, SlipCollection collection)
        {
            _logger.LogWarning("Using fallback parser - consider implementing proper parser services");

            // Return basic slip data with minimal parsing
            return new BankSlipData
            {
                OriginalFilePath = imagePath,
                ParserUsed = GetParserName(),
                IsKBizFormat = collection.IsKBizFormat,
                TransactionDate = File.GetLastWriteTime(imagePath).AddYears(543), // Use file timestamp
                ReceiverName = "Could not resolve receiver",
                Status = BankSlipProcessingStatus.Failed,
                ErrorReason = "Fallback parser used - parsing incomplete",
                ParsingNotes = new Dictionary<string, string>
                {
                    ["Warning"] = "Fallback parser used due to missing parser services"
                }
            };
        }

        public string GetParserName() => "Fallback Parser";

        public SlipFormat GetSupportedFormat() => SlipFormat.Original;

        public bool ValidateParsedData(BankSlipData data) => false; // Always fails validation
    }
}