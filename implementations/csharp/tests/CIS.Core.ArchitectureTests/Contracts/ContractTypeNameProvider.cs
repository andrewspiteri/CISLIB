using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace CIS.Tests.ArchitectureTests.Contracts;

internal sealed class ContractTypeNameProvider : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => throw new NotSupportedException("No function pointer in the contract.");
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType == "Nullable" ? typeArguments.Single() + "?" : genericType + "<" + string.Join(", ", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Int64 => "long",
        _ => throw new NotSupportedException("Unexpected primitive type: " + typeCode)
    };
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => Name(reader.GetString(reader.GetTypeDefinition(handle).Name));
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => Name(reader.GetString(reader.GetTypeReference(handle).Name));
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    private static string Name(string name) => name.Split('`')[0];
}
