// NewwaysAdmin.Shared/IO/Binary/IBinarySerializable.cs
namespace NewwaysAdmin.Shared.IO.Binary
{
    public interface IBinarySerializable
    {
        void WriteToBinary(BinaryWriter writer);
        void ReadFromBinary(BinaryReader reader);
        int Version { get; }
    }
}
