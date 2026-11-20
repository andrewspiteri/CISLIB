using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace CIS.RestClient.Http;

/// <summary>A custody transition for one newly owned temporary Windows TLS key.</summary>
public enum RestHttpNativeKeyTransition
{
    /// <summary>The newly imported key was identified before TLS use.</summary>
    Created,
    /// <summary>The owned certificate was disposed and its exact key file is absent.</summary>
    Removed
}

/// <summary>Non-secret native custody metadata, never private-key contents or a store inventory.</summary>
public sealed class RestHttpNativeKeyMetadata
{
    private RestHttpNativeKeyMetadata(string provider, string container, string unique, string path,
        int? providerType, KeyNumber? keyNumber)
    {
        ProviderName = provider;
        ContainerName = container;
        UniqueName = unique;
        FilePath = path;
        FileLength = new FileInfo(path).Length;
        ProviderType = providerType;
        KeyNumber = keyNumber;
    }

    /// <summary>Gets the provider of this newly owned key.</summary>
    public string ProviderName
    {
        get;
    }
    /// <summary>Gets this newly owned key's native container identifier.</summary>
    public string ContainerName
    {
        get;
    }
    /// <summary>Gets the exact newly owned key file identifier.</summary>
    public string UniqueName
    {
        get;
    }
    /// <summary>Gets the exact owned backing path for a restricted custody journal.</summary>
    public string FilePath
    {
        get;
    }
    /// <summary>Gets the observed newly owned backing length at creation.</summary>
    public long FileLength
    {
        get;
    }
    /// <summary>Gets the captured CSP provider type, or null for a CNG key.</summary>
    public int? ProviderType
    {
        get;
    }
    /// <summary>Gets the captured CSP key number, or null for a CNG key.</summary>
    public KeyNumber? KeyNumber
    {
        get;
    }

    internal void RemoveAfterCertificateDisposal()
    {
        if (!BackingExists())
        {
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("Unexpected Windows TLS key backing on another platform.");
        }
        if (ProviderType is null && KeyNumber is null)
        {
            RemoveCngKey();
        }
        else
        {
            RemoveCspKey();
        }
        if (BackingExists())
        {
            throw new IOException("The owned temporary TLS key backing remains after native key deletion.");
        }
    }

    private bool BackingExists()
    {
        try
        {
            _ = File.GetAttributes(FilePath);
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        return true;
    }

    [SupportedOSPlatform("windows")]
    private void RemoveCngKey()
    {
        using CngKey key = CngKey.Open(ContainerName, new CngProvider(ProviderName), CngKeyOpenOptions.None);
        CngProvider? provider = key.Provider;
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Crypto", "Keys");
        if (key.IsMachineKey || key.IsEphemeral || provider is null
            || !StringComparer.Ordinal.Equals(provider.Provider, CngProvider.MicrosoftSoftwareKeyStorageProvider.Provider)
            || !StringComparer.Ordinal.Equals(provider.Provider, ProviderName)
            || !StringComparer.Ordinal.Equals(key.KeyName, ContainerName)
            || !StringComparer.Ordinal.Equals(key.UniqueName, UniqueName)
            || !StringComparer.OrdinalIgnoreCase.Equals(Path.Combine(directory, UniqueName), FilePath))
        {
            throw new InvalidOperationException("The recorded TLS key does not match its newly owned user-key metadata.");
        }
        key.Delete();
    }

    [SupportedOSPlatform("windows")]
    private void RemoveCspKey()
    {
        if (ProviderType is not { } providerType || providerType <= 0 || KeyNumber is not { } keyNumber)
        {
            throw new InvalidOperationException("The recorded CSP key lacks its exact captured provider type and key number.");
        }
        var parameters = new CspParameters(providerType, ProviderName, ContainerName)
        {
            KeyNumber = (int)keyNumber,
            Flags = CspProviderFlags.UseExistingKey | CspProviderFlags.NoPrompt
        };
        using var key = new RSACryptoServiceProvider(parameters);
        CspKeyContainerInfo info = key.CspKeyContainerInfo;
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? throw new InvalidOperationException("A native user-key owner is required.");
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Crypto", "RSA", sid);
        if (info.MachineKeyStore || info.HardwareDevice
            || info.ProviderType != providerType || info.KeyNumber != keyNumber
            || !StringComparer.Ordinal.Equals(info.ProviderName, ProviderName)
            || !StringComparer.Ordinal.Equals(info.KeyContainerName, ContainerName)
            || !StringComparer.Ordinal.Equals(info.UniqueKeyContainerName, UniqueName)
            || !StringComparer.OrdinalIgnoreCase.Equals(Path.Combine(directory, UniqueName), FilePath))
        {
            throw new InvalidOperationException("The recorded TLS key does not match its newly owned CSP metadata.");
        }
        key.PersistKeyInCsp = false;
        key.Clear();
    }

    [SupportedOSPlatform("windows")]
    internal static RestHttpNativeKeyMetadata Capture(X509Certificate2 certificate)
    {
        using ECDsa? ec = certificate.GetECDsaPrivateKey();
        if (ec is ECDsaCng ecCng)
        {
            return CaptureCng(ecCng.Key);
        }
        using RSA? rsa = certificate.GetRSAPrivateKey();
        if (rsa is RSACng rsaCng)
        {
            return CaptureCng(rsaCng.Key);
        }
        if (rsa is RSACryptoServiceProvider rsaCsp)
        {
            CspKeyContainerInfo info = rsaCsp.CspKeyContainerInfo;
            if (info.MachineKeyStore || info.HardwareDevice)
            {
                throw new InvalidOperationException("Use only a newly owned software user-key backend.");
            }
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value ?? throw new InvalidOperationException("A native user-key owner is required.");
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Crypto", "RSA", sid);
            return Create(info.ProviderName, info.KeyContainerName, info.UniqueKeyContainerName, directory,
                info.ProviderType, info.KeyNumber);
        }
        throw new InvalidOperationException("The new TLS key backend cannot be identified for bounded custody.");
    }

    [SupportedOSPlatform("windows")]
    private static RestHttpNativeKeyMetadata CaptureCng(CngKey key)
    {
        CngProvider? provider = key.Provider;
        if (key.IsMachineKey || key.IsEphemeral
            || provider is null || !StringComparer.Ordinal.Equals(provider.Provider,
                CngProvider.MicrosoftSoftwareKeyStorageProvider.Provider))
        {
            throw new InvalidOperationException("Use only a newly owned software user-key backend.");
        }
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Crypto", "Keys");
        return Create(provider.Provider, key.KeyName, key.UniqueName, directory);
    }

    private static RestHttpNativeKeyMetadata Create(string? provider, string? container, string? unique, string directory,
        int? providerType = null, KeyNumber? keyNumber = null)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(container) || string.IsNullOrWhiteSpace(unique)
            || unique != Path.GetFileName(unique) || unique.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("The newly owned TLS key lacks an exact safe file identifier.");
        }
        if (providerType is { } capturedType
            && (capturedType <= 0 || keyNumber is null || !Enum.IsDefined(keyNumber.Value))
            || providerType is null && keyNumber is not null)
        {
            throw new InvalidOperationException("The newly owned CSP key lacks exact supported provider metadata.");
        }
        return new(provider, container, unique, Path.Combine(directory, unique), providerType, keyNumber);
    }
}

/// <summary>A finite native-key custody owner shared by related sources or qualification fixtures.</summary>
/// <remarks>Construction performs no I/O. Only active or unresolved entries are retained. The controller
/// accounts the scope's conservative storage allowance and supplies its restricted journal callback.</remarks>
public sealed class RestHttpNativeKeyScope : IDisposable
{
    private readonly object gate = new();
    private readonly int maximumLiveKeys;
    private readonly Action<RestHttpNativeKeyMetadata, RestHttpNativeKeyTransition>? custody;
    private readonly HashSet<Lease> leases = [];
    private bool closed;

    /// <summary>Captures a one-to-sixteen-key allowance and optional explicit custody sink.</summary>
    public RestHttpNativeKeyScope(int maximumLiveKeys = 16,
        Action<RestHttpNativeKeyMetadata, RestHttpNativeKeyTransition>? custody = null)
    {
        if (maximumLiveKeys is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLiveKeys));
        }
        this.maximumLiveKeys = maximumLiveKeys;
        this.custody = custody;
    }

    /// <summary>Gets active imports/reservations plus unresolved cleanup entries.</summary>
    public int LiveKeyCount
    {
        get
        {
            lock (gate)
            {
                return leases.Count;
            }
        }
    }

    internal Lease Reserve()
    {
        lock (gate)
        {
            if (closed || leases.Count >= maximumLiveKeys)
            {
                throw new InvalidOperationException("The native TLS key scope is closed or its finite allowance is occupied.");
            }
            var lease = new Lease(this);
            leases.Add(lease);
            return lease;
        }
    }

    /// <summary>Closes new imports and requires all actual certificate owners to have settled.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            closed = true;
            if (leases.Count != 0)
            {
                throw new InvalidOperationException("Native TLS certificates or unresolved cleanup still own this scope.");
            }
        }
    }

    internal sealed class Lease(RestHttpNativeKeyScope owner)
    {
        private RestHttpNativeKeyMetadata? metadata;
        private bool imported;

        internal void Imported() => imported = true;

        internal void Created(RestHttpNativeKeyMetadata value)
        {
            metadata = value;
            try
            {
                owner.custody?.Invoke(value, RestHttpNativeKeyTransition.Created);
            }
            catch
            {
                lock (owner.gate)
                {
                    owner.closed = true;
                }
                throw;
            }
        }

        internal void Release(bool certificateDisposed)
        {
            try
            {
                if (!certificateDisposed)
                {
                    throw new InvalidOperationException("The native TLS certificate did not settle.");
                }
                if (imported && metadata is null)
                {
                    throw new InvalidOperationException("The imported native TLS key could not be identified for cleanup proof.");
                }
                if (metadata is not null)
                {
                    metadata.RemoveAfterCertificateDisposal();
                    owner.custody?.Invoke(metadata, RestHttpNativeKeyTransition.Removed);
                }
                lock (owner.gate)
                {
                    owner.leases.Remove(this);
                }
            }
            catch
            {
                lock (owner.gate)
                {
                    owner.closed = true;
                }
                throw;
            }
        }
    }
}

/// <summary>Owns one leaf and its explicitly prepared native TLS key backend.</summary>
/// <remarks>The constructor only takes disposal ownership. Prepare inside an admitted material factory,
/// after transport generation reservation. Borrow Certificate for native TLS and retain this owner until
/// the handler/server and every actual response body drain. Never dispose a borrowed Certificate.</remarks>
public sealed class RestHttpNativeCertificate : IDisposable
{
    private readonly object gate = new();
    private X509Certificate2 certificate;
    private RestHttpNativeKeyScope.Lease? lease;
    private bool disposed;
    private bool prepared;
    private bool failed;

    /// <summary>Takes disposal ownership without exporting, importing or reading key files.</summary>
    public RestHttpNativeCertificate(X509Certificate2 ownedLeaf)
    {
        ArgumentNullException.ThrowIfNull(ownedLeaf);
        certificate = ownedLeaf;
    }

    /// <summary>Gets the borrowed native certificate; this owner retains its disposal responsibility.</summary>
    public X509Certificate2 Certificate
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return certificate;
            }
        }
    }
    /// <summary>Gets exact new Windows backing metadata after successful preparation, otherwise null.</summary>
    public RestHttpNativeKeyMetadata? WindowsKey
    {
        get; private set;
    }
    internal bool IsPrepared => prepared && !disposed && !failed;

    /// <summary>Explicitly prepares Windows Schannel user-key backing, or retains native memory ownership elsewhere.</summary>
    /// <remarks>Uses memory-only PKCS#12, UserKeySet without persistence/machine/ephemeral flags, and zeroes
    /// the export buffer. No certificate store is opened or populated. A failure is terminal for this owner.</remarks>
    public void PrepareForTls(RestHttpNativeKeyScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failed)
            {
                throw new InvalidOperationException("Native TLS certificate preparation previously failed.");
            }
            if (prepared)
            {
                return;
            }
            if (!certificate.HasPrivateKey)
            {
                failed = true;
                throw new InvalidOperationException("Native TLS material must carry its owned private key.");
            }
            if (!OperatingSystem.IsWindows())
            {
                prepared = true;
                return;
            }
            RestHttpNativeKeyScope.Lease reservation = scope.Reserve();
            X509Certificate2? imported = null;
            byte[]? buffer = null;
            try
            {
                buffer = certificate.Export(X509ContentType.Pkcs12);
                imported = X509CertificateLoader.LoadPkcs12(buffer, password: null, X509KeyStorageFlags.UserKeySet);
                reservation.Imported();
                RestHttpNativeKeyMetadata metadata = RestHttpNativeKeyMetadata.Capture(imported);
                reservation.Created(metadata);
                ObjectDisposedException.ThrowIf(disposed, this);
                if (metadata.FileLength is <= 0 or > 524288)
                {
                    throw new InvalidOperationException("The owned TLS key backing exceeds its finite allowance.");
                }
                certificate.Dispose();
                certificate = imported;
                imported = null;
                WindowsKey = metadata;
                lease = reservation;
                prepared = true;
            }
            catch (Exception original)
            {
                failed = true;
                Exception? cleanup = null;
                bool certificateDisposed = false;
                try
                {
                    imported?.Dispose();
                    certificateDisposed = true;
                }
                catch (Exception error) { cleanup = error; }
                try
                {
                    reservation.Release(certificateDisposed);
                }
                catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
                if (cleanup is not null)
                {
                    throw new AggregateException("Native TLS preparation and owned cleanup failed.", original, cleanup);
                }
                ExceptionDispatchInfo.Capture(original).Throw();
                throw;
            }
            finally
            {
                if (buffer is not null)
                {
                    CryptographicOperations.ZeroMemory(buffer);
                }
            }
        }
    }

    /// <summary>Disposes the owned certificate, verifies its exact backing removal and releases its finite slot.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Exception? failure = null;
            bool certificateDisposed = false;
            try
            {
                certificate.Dispose();
                certificateDisposed = true;
            }
            catch (Exception error) { failure = error; }
            try
            {
                lease?.Release(certificateDisposed);
            }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }
}
