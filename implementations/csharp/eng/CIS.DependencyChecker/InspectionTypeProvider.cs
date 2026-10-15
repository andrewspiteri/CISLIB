using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace CIS.Verification;

internal sealed class InspectionTypeProvider(MetadataReader reader, string assembly, Action<string, string, string> use)
    : ISignatureTypeProvider<string, object?>
{
    internal (string Assembly, string Namespace, string Name) Identity(EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            TypeDefinition type = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            TypeDefinitionHandle parent = type.GetDeclaringType();
            return parent.IsNil ? (assembly, reader.GetString(type.Namespace), reader.GetString(type.Name)) :
                Nested(Identity(parent), reader.GetString(type.Name));
        }
        if (handle.Kind == HandleKind.TypeReference)
        {
            TypeReference type = reader.GetTypeReference((TypeReferenceHandle)handle);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference) { return Nested(Identity(type.ResolutionScope), reader.GetString(type.Name)); }
            string scope = type.ResolutionScope.Kind == HandleKind.AssemblyReference ?
                reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name) : assembly;
            return (scope, reader.GetString(type.Namespace), reader.GetString(type.Name));
        }
        throw new BadImageFormatException("Expected a defined or referenced type.");
    }

    internal string Type(EntityHandle handle)
    {
        if (handle.IsNil) { return ""; }
        if (handle.Kind == HandleKind.TypeSpecification) { return reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null); }
        (string scope, string space, string name) = Identity(handle);
        string full = string.IsNullOrEmpty(space) ? name : space + "." + name;
        use(scope, space, full);
        return "[" + scope + "]" + full;
    }

    internal static string Signature(MethodSignature<string> signature) =>
        signature.Header.CallingConvention + " generic=" + signature.GenericParameterCount + " (" + string.Join(", ", signature.ParameterTypes) + ") -> " + signature.ReturnType;
    private static (string Assembly, string Namespace, string Name) Nested((string Assembly, string Namespace, string Name) parent, string name) =>
        (parent.Assembly, parent.Namespace, parent.Name + "+" + name);
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[rank=" + shape.Rank + ";sizes=" + string.Join(",", shape.Sizes) + ";lower=" + string.Join(",", shape.LowerBounds) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr " + Signature(signature);
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(", ", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";
    public string GetPinnedType(string elementType) => elementType + " pinned";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader metadata, TypeDefinitionHandle handle, byte rawTypeKind) => Type(handle);
    public string GetTypeFromReference(MetadataReader metadata, TypeReferenceHandle handle, byte rawTypeKind) => Type(handle);
    public string GetTypeFromSpecification(MetadataReader metadata, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => Type(handle);
}
