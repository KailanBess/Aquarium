using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Aquarium;

public static class WorkspaceCapability
{
    public static bool SupportsHosting(string executable)
    {
        try
        {
            // .NET apphosts carry metadata in their adjacent managed DLL.
            var file = executable;
            using (var stream = File.OpenRead(file))
            using (var pe = new PEReader(stream))
                if (!pe.HasMetadata) file = Path.ChangeExtension(file, ".dll");
            using var input = File.OpenRead(file); using var image = new PEReader(input);
            if (!image.HasMetadata) return false;
            var reader = image.GetMetadataReader();
            foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (member.Parent.Kind != HandleKind.TypeReference) continue;
                var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                if (reader.GetString(type.Namespace) != "System.Reflection" || reader.GetString(type.Name) != "AssemblyMetadataAttribute") continue;
                var value = reader.GetBlobReader(attribute.Value);
                if (value.ReadUInt16() == 1 && value.ReadSerializedString() == "AquariumWorkspace" && value.ReadSerializedString() == "1") return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException) { }
        return false;
    }
}
