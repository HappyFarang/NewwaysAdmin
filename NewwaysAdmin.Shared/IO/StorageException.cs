// NewwaysAdmin.Shared/IO/StorageException.cs
namespace NewwaysAdmin.Shared.IO
{
    public class StorageException : Exception
    {
        public string Identifier { get; }
        public StorageOperation Operation { get; }

        public StorageException(string message, string identifier, StorageOperation operation, Exception? innerException = null)
            : base(message, innerException)
        {
            Identifier = identifier;
            Operation = operation;
        }
    }

    public enum StorageOperation
    {
        Load,
        Save,
        Delete,
        List,
        Validate
    }
}
