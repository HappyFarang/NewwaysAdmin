// NewwaysAdmin.GoogleSheets/Exceptions/GoogleSheetsException.cs
using System;

namespace NewwaysAdmin.GoogleSheets.Exceptions
{
    public class GoogleSheetsException : Exception
    {
        public string? SheetId { get; }
        public string? Operation { get; }

        public GoogleSheetsException(string message) : base(message)
        {
        }

        public GoogleSheetsException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public GoogleSheetsException(string message, string? sheetId, string? operation) : base(message)
        {
            SheetId = sheetId;
            Operation = operation;
        }

        public GoogleSheetsException(string message, string? sheetId, string? operation, Exception innerException)
            : base(message, innerException)
        {
            SheetId = sheetId;
            Operation = operation;
        }
    }

    public class GoogleSheetsAuthenticationException : GoogleSheetsException
    {
        public GoogleSheetsAuthenticationException(string message) : base(message)
        {
        }

        public GoogleSheetsAuthenticationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public class GoogleSheetsPermissionException : GoogleSheetsException
    {
        public GoogleSheetsPermissionException(string message, string? sheetId) : base(message, sheetId, "Permission")
        {
        }

        public GoogleSheetsPermissionException(string message, string? sheetId, Exception innerException)
            : base(message, sheetId, "Permission", innerException)
        {
        }
    }
}