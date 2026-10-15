using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace CIS.Verification;

internal static class AssemblyInspection
{
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);

    internal static void Write(string path, string output)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) { throw new BadImageFormatException("Expected managed metadata."); }
        MetadataReader reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) { throw new BadImageFormatException("Expected an assembly."); }
        AssemblyDefinition assembly = reader.GetAssemblyDefinition();
        string name = reader.GetString(assembly.Name), site = "assembly";
        var uses = new HashSet<TypeUse>();
        var visitedAttributes = new HashSet<CustomAttributeHandle>();
        var provider = new InspectionTypeProvider(reader, name, (scope, space, type) => uses.Add(new(site, scope, space, type)));
        var types = new List<TypeFact>();
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            var identity = provider.Identity(handle);
            string full = string.IsNullOrEmpty(identity.Namespace) ? identity.Name : identity.Namespace + "." + identity.Name;
            site = full + ":type";
            bool visible = Visible(reader, handle);
            string baseType = provider.Type(type.BaseType);
            string[] interfaces = type.GetInterfaceImplementations().Select(h =>
            {
                InterfaceImplementation implementation = reader.GetInterfaceImplementation(h);
                Attributes(implementation.GetCustomAttributes());
                return provider.Type(implementation.Interface);
            }).ToArray();
            foreach (MethodImplementationHandle implementationHandle in type.GetMethodImplementations())
            {
                MethodImplementation implementation = reader.GetMethodImplementation(implementationHandle);
                Touch(implementation.MethodDeclaration);
            }
            Attributes(type.GetCustomAttributes());
            Constraints(type.GetGenericParameters());
            var members = new List<object>();
            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                string fieldName = reader.GetString(field.Name);
                site = full + ":field:" + fieldName;
                string signature = field.DecodeSignature(provider, null);
                Attributes(field.GetCustomAttributes());
                members.Add(new { kind = "field", name = fieldName, signature, attributes = field.Attributes.ToString(), publicApi = visible && FieldVisible(field.Attributes) });
            }
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                string methodName = reader.GetString(method.Name);
                site = full + ":method:" + methodName + ":0x" + MetadataTokens.GetToken(methodHandle).ToString("x8");
                string signature = InspectionTypeProvider.Signature(method.DecodeSignature(provider, null));
                Attributes(method.GetCustomAttributes());
                Constraints(method.GetGenericParameters());
                foreach (ParameterHandle parameter in method.GetParameters()) { Attributes(reader.GetParameter(parameter).GetCustomAttributes()); }
                if (method.RelativeVirtualAddress != 0)
                {
                    MethodBodyBlock body = pe.GetMethodBody(method.RelativeVirtualAddress);
                    if (!body.LocalSignature.IsNil) { reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(provider, null); }
                    foreach (ExceptionRegion region in body.ExceptionRegions) { if (region.Kind == ExceptionRegionKind.Catch) { provider.Type(region.CatchType); } }
                    ScanBody(body.GetILBytes() ?? throw new BadImageFormatException("Missing IL."), Touch);
                }
                members.Add(new { kind = "method", name = methodName, signature, attributes = method.Attributes.ToString(), publicApi = visible && MethodVisible(method.Attributes) });
            }
            foreach (PropertyDefinitionHandle propertyHandle in type.GetProperties())
            {
                PropertyDefinition property = reader.GetPropertyDefinition(propertyHandle);
                string propertyName = reader.GetString(property.Name);
                site = full + ":property:" + propertyName;
                string signature = InspectionTypeProvider.Signature(property.DecodeSignature(provider, null));
                PropertyAccessors access = property.GetAccessors();
                Attributes(property.GetCustomAttributes());
                members.Add(new { kind = "property", name = propertyName, signature, attributes = property.Attributes.ToString(), publicApi = visible && (AccessorVisible(access.Getter) || AccessorVisible(access.Setter)) });
            }
            foreach (EventDefinitionHandle eventHandle in type.GetEvents())
            {
                EventDefinition item = reader.GetEventDefinition(eventHandle);
                string eventName = reader.GetString(item.Name);
                site = full + ":event:" + eventName;
                string signature = provider.Type(item.Type);
                EventAccessors access = item.GetAccessors();
                Attributes(item.GetCustomAttributes());
                members.Add(new { kind = "event", name = eventName, signature, attributes = item.Attributes.ToString(), publicApi = visible && (AccessorVisible(access.Adder) || AccessorVisible(access.Remover)) });
            }
            types.Add(new(full, identity.Namespace, type.Attributes.ToString(), visible, baseType, interfaces, members));
        }
        site = "assembly";
        Attributes(assembly.GetCustomAttributes());
        site = "<Module>:module";
        Attributes(reader.GetModuleDefinition().GetCustomAttributes());
        if (visitedAttributes.Count != reader.CustomAttributes.Count) { throw new InvalidDataException("Unvisited custom attribute metadata parents."); }
        var references = reader.AssemblyReferences.Select(handle =>
        {
            AssemblyReference reference = reader.GetAssemblyReference(handle);
            return new { name = reader.GetString(reference.Name), version = reference.Version.ToString(), culture = reader.GetString(reference.Culture), publicKeyOrToken = Convert.ToHexStringLower(reader.GetBlobBytes(reference.PublicKeyOrToken)), flags = reference.Flags.ToString() };
        }).OrderBy(item => item.name, StringComparer.Ordinal).ToArray();
        var spaces = types.ToDictionary(type => type.name, type => type.@namespace, StringComparer.Ordinal);
        var internalDependencies = uses.Where(use => use.Assembly == name && spaces.ContainsKey(use.Site.Split(':')[0]))
            .Select(use => new { sourceType = use.Site.Split(':')[0], sourceNamespace = spaces[use.Site.Split(':')[0]], targetType = use.Type, targetNamespace = use.Namespace, site = use.Site })
            .OrderBy(use => use.sourceType, StringComparer.Ordinal).ThenBy(use => use.targetType, StringComparer.Ordinal).ThenBy(use => use.site, StringComparer.Ordinal).ToArray();
        var namespaceDependencies = internalDependencies.GroupBy(use => (use.sourceNamespace, use.targetNamespace))
            .Select(group => new { source = group.Key.sourceNamespace, target = group.Key.targetNamespace, useSites = group.Count() })
            .OrderBy(edge => edge.source, StringComparer.Ordinal).ThenBy(edge => edge.target, StringComparer.Ordinal).ToArray();
        var report = new
        {
            schema = "cis.assembly-inspection/v1",
            path = Path.GetFullPath(path),
            sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
            name,
            version = assembly.Version.ToString(),
            mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid),
            references,
            types,
            internalDependencies,
            namespaceDependencies,
            customAttributes = new { total = reader.CustomAttributes.Count, visited = visitedAttributes.Count },
            typeUses = uses.OrderBy(item => item.Site, StringComparer.Ordinal).ThenBy(item => item.Assembly, StringComparer.Ordinal).ThenBy(item => item.Type, StringComparer.Ordinal).ToArray(),
            limits = "Static metadata/signature/IL inspection only; no inspected assembly is loaded. Reflection strings, serialized attribute arguments and runtime-selected dependencies are not inferred. Public member shape is an inspection inventory, not cumulative API compatibility or ownership-policy approval."
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        bool AccessorVisible(MethodDefinitionHandle handle) => !handle.IsNil && MethodVisible(reader.GetMethodDefinition(handle).Attributes);
        void Constraints(GenericParameterHandleCollection handles)
        {
            foreach (GenericParameterHandle parameter in handles)
            {
                GenericParameter item = reader.GetGenericParameter(parameter);
                Attributes(item.GetCustomAttributes());
                foreach (GenericParameterConstraintHandle constraint in item.GetConstraints())
                {
                    GenericParameterConstraint constrained = reader.GetGenericParameterConstraint(constraint);
                    Attributes(constrained.GetCustomAttributes());
                    provider.Type(constrained.Type);
                }
            }
        }
        void Attributes(CustomAttributeHandleCollection handles)
        {
            foreach (CustomAttributeHandle attribute in handles)
            {
                if (visitedAttributes.Add(attribute)) { Touch(reader.GetCustomAttribute(attribute).Constructor); }
            }
        }
        void Touch(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                case HandleKind.TypeReference:
                case HandleKind.TypeSpecification: provider.Type(handle); break;
                case HandleKind.MethodDefinition:
                    MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    provider.Type(method.GetDeclaringType()); method.DecodeSignature(provider, null); break;
                case HandleKind.FieldDefinition:
                    FieldDefinition field = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                    provider.Type(field.GetDeclaringType()); field.DecodeSignature(provider, null); break;
                case HandleKind.MemberReference:
                    MemberReference member = reader.GetMemberReference((MemberReferenceHandle)handle);
                    Touch(member.Parent);
                    if (member.GetKind() == MemberReferenceKind.Field) { member.DecodeFieldSignature(provider, null); }
                    else { member.DecodeMethodSignature(provider, null); }
                    break;
                case HandleKind.MethodSpecification:
                    MethodSpecification specification = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                    Touch(specification.Method); specification.DecodeSignature(provider, null); break;
                case HandleKind.StandaloneSignature: reader.GetStandaloneSignature((StandaloneSignatureHandle)handle).DecodeMethodSignature(provider, null); break;
                case HandleKind.ModuleReference: break;
                default: throw new BadImageFormatException("Unsupported IL metadata handle: " + handle.Kind);
            }
        }
    }

    private static bool Visible(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        TypeAttributes visibility = type.Attributes & TypeAttributes.VisibilityMask;
        return visibility == TypeAttributes.Public || (visibility is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem) && Visible(reader, type.GetDeclaringType());
    }
    private static bool MethodVisible(MethodAttributes attributes) => (attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;
    private static bool FieldVisible(FieldAttributes attributes) => (attributes & FieldAttributes.FieldAccessMask) is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem;
    private static void ScanBody(byte[] bytes, Action<EntityHandle> touch)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            short value = bytes[offset++] == 0xfe ? (short)(0xfe00 | ReadByte()) : bytes[offset - 1];
            if (!Codes.TryGetValue(value, out OpCode code)) { throw new BadImageFormatException("Unknown IL opcode."); }
            int size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => SwitchSize(),
                _ => 4
            };
            if (size < 0 || size > bytes.Length - offset) { throw new BadImageFormatException("Truncated IL operand."); }
            if (code.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok or OperandType.InlineSig) { touch(MetadataTokens.EntityHandle(BitConverter.ToInt32(bytes, offset))); }
            offset += size;
        }
        return;
        byte ReadByte() => offset < bytes.Length ? bytes[offset++] : throw new BadImageFormatException("Truncated IL opcode.");
        int SwitchSize()
        {
            if (bytes.Length - offset < 4) { throw new BadImageFormatException("Truncated switch."); }
            int count = BitConverter.ToInt32(bytes, offset);
            if (count < 0 || count > (bytes.Length - offset - 4) / 4) { throw new BadImageFormatException("Invalid switch count."); }
            return 4 + count * 4;
        }
    }
    private sealed record TypeFact(string name, string @namespace, string attributes, bool publicApi, string baseType, string[] interfaces, List<object> members);
    private sealed record TypeUse(string Site, string Assembly, string Namespace, string Type);
}
