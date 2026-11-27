using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CIS.Messaging;
using CIS.Operations;

namespace CIS.Jobs;

/// <summary>Passive exact declarations of the job semantics that one registered handler can start or recover.</summary>
public sealed class JobExecutionContract
{
    /// <summary>Copies bounded compatibility declarations without selecting a handler, journal or runtime.</summary>
    public JobExecutionContract(MessageContract currentContract, IEnumerable<MessageContract> supportedStarts,
        IEnumerable<MessageContract> supportedRecoveries, MessageContract? checkpointContract = null, string? operationName = null)
    {
        ArgumentNullException.ThrowIfNull(currentContract);
        CurrentContract = currentContract;
        SupportedStarts = CopyContracts(supportedStarts, nameof(supportedStarts));
        SupportedRecoveries = CopyContracts(supportedRecoveries, nameof(supportedRecoveries));
        if (!SupportedStarts.Contains(currentContract))
        {
            throw new ArgumentException("The current execution contract must be explicitly supported for new starts.", nameof(supportedStarts));
        }

        CheckpointContract = checkpointContract;
        OperationName = operationName is null ? null : OperationText.Alias(operationName, nameof(operationName));
    }

    /// <summary>Gets the current versioned execution contract, separate from the scheduled payload contract.</summary>
    public MessageContract CurrentContract
    {
        get;
    }

    /// <summary>Gets one through sixteen exact distinct contracts explicitly accepted for new execution.</summary>
    public IReadOnlyList<MessageContract> SupportedStarts
    {
        get;
    }

    /// <summary>Gets zero through sixteen exact distinct contracts explicitly accepted for original-result recovery.</summary>
    public IReadOnlyList<MessageContract> SupportedRecoveries
    {
        get;
    }

    /// <summary>Gets the application-owned checkpoint contract, or null when continuation is not declared.</summary>
    public MessageContract? CheckpointContract
    {
        get;
    }

    /// <summary>Gets the registered business-operation alias, or null for passive inspection without reliable activation.</summary>
    public string? OperationName
    {
        get;
    }

    /// <summary>Tests exact contract identity and version, without inferring compatibility from release numbers.</summary>
    public bool SupportsStart(MessageContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return SupportedStarts.Contains(contract);
    }

    /// <summary>Tests explicitly declared original-result recovery compatibility.</summary>
    public bool SupportsRecovery(MessageContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return SupportedRecoveries.Contains(contract);
    }

    private static IReadOnlyList<MessageContract> CopyContracts(IEnumerable<MessageContract> contracts, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(contracts, parameterName);
        var captured = new List<MessageContract>();
        foreach (MessageContract contract in contracts)
        {
            ArgumentNullException.ThrowIfNull(contract, parameterName);
            if (captured.Count == 16 || captured.Contains(contract))
            {
                throw new ArgumentException("Declare at most sixteen exact distinct execution contracts.", parameterName);
            }

            captured.Add(contract);
        }

        return captured.AsReadOnly();
    }
}

/// <summary>Bounded worker release/build/handler observations; these values are not binary attestation or authorization.</summary>
public sealed record JobWorkerBuildIdentity
{
    /// <summary>Copies independently selected immutable worker metadata without inspecting files or constructing handlers.</summary>
    public JobWorkerBuildIdentity(string applicationVersion, string buildId, string handlerId, string? sourceRevision = null)
    {
        ApplicationVersion = JobText.Text(applicationVersion, 512, nameof(applicationVersion));
        BuildId = JobText.Text(buildId, 512, nameof(buildId));
        HandlerId = JobText.Text(handlerId, 512, nameof(handlerId));
        SourceRevision = sourceRevision is null ? null : JobText.Text(sourceRevision, 128, nameof(sourceRevision));
    }

    /// <summary>Gets the exact human-readable application release label, bounded to 512 UTF-8 bytes.</summary>
    public string ApplicationVersion
    {
        get;
    }

    /// <summary>Gets the selected immutable build identifier, bounded to 512 UTF-8 bytes.</summary>
    public string BuildId
    {
        get;
    }

    /// <summary>Gets the exact handler implementation identifier, bounded to 512 UTF-8 bytes.</summary>
    public string HandlerId
    {
        get;
    }

    /// <summary>Gets optional supplemental source revision text, bounded to 128 UTF-8 bytes.</summary>
    public string? SourceRevision
    {
        get;
    }

    /// <summary>Captures the supplied loaded handler's module/type metadata without disk, attribute construction or provider I/O.</summary>
    /// <remarks>The build ID identifies the handler's loaded module MVID. Handler identity also includes the exact
    /// metadata token, a bounded closed-type graph digest and actual Jobs interface-map target methods,
    /// so constructed generic handlers and inherited or overridden callback implementations remain distinct.
    /// Per-attempt use must supply the actual resolved runtime class after checking its registered binding;
    /// inspecting a registered type alone does not establish that a callback entered or that this code executed.</remarks>
    public static JobWorkerBuildIdentity Capture(string applicationVersion, Type handlerType, string? sourceRevision = null)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        if (!handlerType.IsClass || handlerType.IsAbstract || handlerType.ContainsGenericParameters || handlerType.Assembly.IsDynamic)
        {
            throw new ArgumentException("Capture a concrete closed handler class from an already loaded non-dynamic module.", nameof(handlerType));
        }

        string version = JobText.Text(applicationVersion, 512, nameof(applicationVersion));
        string? revision = sourceRevision is null ? null : JobText.Text(sourceRevision, 128, nameof(sourceRevision));
        try
        {
            Guid module = handlerType.Module.ModuleVersionId;
            if (module == Guid.Empty)
            {
                throw new ArgumentException("The loaded handler requires an established module version ID.", nameof(handlerType));
            }

            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int remainingTypes = 64;
            AppendType(hash, handlerType, ref remainingTypes, 0);
            string buildId = "module-mvid:" + module.ToString("D", CultureInfo.InvariantCulture);
            string token = handlerType.MetadataToken.ToString("x8", CultureInfo.InvariantCulture);
            string digest = Convert.ToHexStringLower(hash.GetHashAndReset());
            string callbacks = CaptureCallbacks(handlerType);
            return new(version, buildId, buildId + "/type:" + token + "/signature-sha256:" + digest
                + "/callbacks-sha256:" + callbacks, revision);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            throw new ArgumentException("The supplied handler has no supported loaded module/type metadata.", nameof(handlerType), error);
        }
    }

    private static string CaptureCallbacks(Type handlerType)
    {
        var methods = new List<string>();
        foreach (Type port in handlerType.GetInterfaces())
        {
            if (port != typeof(IContinuousJobHandler)
                && (!port.IsConstructedGenericType || port.GetGenericTypeDefinition() != typeof(IScheduledJobHandler<>)))
            {
                continue;
            }

            foreach (System.Reflection.MethodInfo method in handlerType.GetInterfaceMap(port).TargetMethods)
            {
                if (methods.Count == 16 || method.Module.Assembly.IsDynamic || method.Module.ModuleVersionId == Guid.Empty)
                {
                    throw new ArgumentException("Capture at most sixteen Jobs callback targets from established loaded modules.", nameof(handlerType));
                }

                using IncrementalHash signature = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int remainingTypes = 64;
                AppendType(signature, port, ref remainingTypes, 0);
                AppendType(signature, method.DeclaringType!, ref remainingTypes, 0);
                Append(signature, "method:" + method.Module.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture)
                    + ":" + method.MetadataToken.ToString("x8", CultureInfo.InvariantCulture));
                methods.Add(Convert.ToHexStringLower(signature.GetHashAndReset()));
            }
        }

        if (methods.Count == 0)
        {
            throw new ArgumentException("Capture a loaded handler that implements an existing Jobs callback port.", nameof(handlerType));
        }

        methods.Sort(StringComparer.Ordinal);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string method in methods)
        {
            Append(hash, method);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendType(IncrementalHash hash, Type type, ref int remainingTypes, int depth)
    {
        if (depth > 16 || remainingTypes-- == 0 || type.ContainsGenericParameters || type.Assembly.IsDynamic)
        {
            throw new ArgumentException("Capture at most sixty-four loaded type nodes within sixteen generic/element levels.", "handlerType");
        }

        if (type.HasElementType)
        {
            string kind;
            if (type.IsArray)
            {
                kind = type.IsSZArray ? "szarray" : "array:" + type.GetArrayRank().ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                kind = type.IsByRef ? "byref" : "pointer";
            }

            Append(hash, kind + "[");
            AppendType(hash, type.GetElementType()!, ref remainingTypes, depth + 1);
            Append(hash, "]");
            return;
        }

        Guid module = type.Module.ModuleVersionId;
        if (module == Guid.Empty)
        {
            throw new ArgumentException("Every captured type requires an established module version ID.", "handlerType");
        }

        Append(hash, "type:" + module.ToString("D", CultureInfo.InvariantCulture) + ":"
            + type.MetadataToken.ToString("x8", CultureInfo.InvariantCulture) + "[");
        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                AppendType(hash, argument, ref remainingTypes, depth + 1);
            }
        }

        Append(hash, "]");
    }

    private static void Append(IncrementalHash hash, string value) => hash.AppendData(Encoding.UTF8.GetBytes(value));
}
