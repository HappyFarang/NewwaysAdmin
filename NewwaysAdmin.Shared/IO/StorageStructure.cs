namespace NewwaysAdmin.Shared.IO
{
    public class StorageStructure
    {
        public required string Name { get; set; }
        public string Description { get; set; } = string.Empty;
        public required StorageNode RootNode { get; set; }

        public StorageNode? FindNode(params string[] path)
        {
            if (path == null || path.Length == 0)
                return RootNode;
            var current = RootNode;
            foreach (var segment in path)
            {
                current = current.Children.FirstOrDefault(c => c.Name == segment);
                if (current == null)
                    return null;
            }
            return current;
        }

        public bool ValidatePath(params string[] path)
        {
            return FindNode(path) != null;
        }
    }
}