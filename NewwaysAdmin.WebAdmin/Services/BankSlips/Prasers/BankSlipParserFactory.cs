// NewwaysAdmin.WebAdmin/Services/BankSlips/Parsers/BankSlipParserFactory.cs
// 🔥 MODERN VERSION - Pattern-based, no more hardcoded nonsense!

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers
{
    /// <summary>
    /// Modern bank slip parser factory - uses pattern-based parsing for all document types
    /// No more hardcoded K-BIZ vs Original nonsense! 
    /// </summary>
    public class BankSlipParserFactory
    {
        private readonly ILogger<BankSlipParserFactory> _logger;
        private readonly IServiceProvider _serviceProvider;

        public BankSlipParserFactory(
            ILogger<BankSlipParserFactory> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// Gets the parser for any collection - now just ONE smart parser for everything!
        /// </summary>
        public IBankSlipParser GetParser(SlipCollection collection)
        {
            try
            {
                // 🎯 NEW: Only ONE parser needed - it's pattern-based and handles everything!
                var patternParser = _serviceProvider.GetRequiredService<PatternBasedBankSlipParser>();

                _logger.LogDebug("Using pattern-based parser for collection {CollectionName} (format: {Format})",
                    collection.Name, collection.IsKBizFormat ? "K-BIZ" : "Standard");

                return patternParser;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pattern-based parser for collection {CollectionName}", collection.Name);

                // Fallback to emergency parser
                _logger.LogWarning("Creating emergency fallback parser for collection {CollectionName}", collection.Name);
                return new EmergencyFallbackParser(_logger);
            }
        }

        /// <summary>
        /// Gets the best parser by analyzing OCR text - but now we only have ONE awesome parser!
        /// </summary>
        public IBankSlipParser GetBestParser(string ocrText, SlipCollection collection)
        {
            try
            {
                // 🚀 NEW: No more format detection needed - our pattern parser handles everything!
                var patternParser = _serviceProvider.GetRequiredService<PatternBasedBankSlipParser>();

                if (patternParser.CanParse(ocrText, collection))
                {
                    _logger.LogDebug("Pattern-based parser can handle text for collection {CollectionName}", collection.Name);
                    return patternParser;
                }

                _logger.LogWarning("Pattern-based parser cannot handle text for collection {CollectionName} - this shouldn't happen!", collection.Name);
                return patternParser; // Use it anyway, it's our only parser now!
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in parser selection, falling back to emergency parser");
                return new EmergencyFallbackParser(_logger);
            }
        }
    }

    /// <summary>
    /// Emergency fallback parser - only used when the main system fails catastrophically
    /// This should NEVER be used in normal operation
    /// </summary>
    internal class EmergencyFallbackParser : IBankSlipParser
    {
        private readonly ILogger _logger;

        public EmergencyFallbackParser(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool CanParse(string text, SlipCollection collection)
        {
            // Emergency parser accepts anything but will fail gracefully
            return !string.IsNullOrWhiteSpace(text);
        }

        public BankSlipData? Parse(string text, string imagePath, SlipCollection collection)
        {
            _logger.LogError("🚨 EMERGENCY FALLBACK PARSER ACTIVATED! This indicates a serious system failure!");
            _logger.LogError("Collection: {CollectionName}, File: {FilePath}", collection.Name, Path.GetFileName(imagePath));

            // Return a failed bank slip with clear error indication
            return new BankSlipData
            {
                Id = Guid.NewGuid().ToString(),
                OriginalFilePath = imagePath,
                ParserUsed = GetParserName(),
                IsKBizFormat = collection.IsKBizFormat,
                TransactionDate = File.GetLastWriteTime(imagePath),
                ReceiverName = "⚠️ EMERGENCY PARSER USED",
                Amount = 0,
                Status = BankSlipProcessingStatus.Failed,
                ErrorReason = "Emergency fallback parser activated - system failure detected",
                ProcessedAt = DateTime.UtcNow,
                ParsingNotes = new Dictionary<string, string>
                {
                    ["EmergencyMode"] = "true",
                    ["OriginalCollection"] = collection.Name,
                    ["FailureTime"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["TextLength"] = text?.Length.ToString() ?? "0",
                    ["SystemError"] = "PatternBasedBankSlipParser was not available"
                }
            };
        }

        public string GetParserName() => "🚨 Emergency Fallback Parser";

        public SlipFormat GetSupportedFormat() => SlipFormat.Original;

        public bool ValidateParsedData(BankSlipData data)
        {
            // Emergency parser always fails validation to ensure manual review
            return false;
        }
    }
}