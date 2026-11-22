using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using RabbitMQ.Client;

namespace CIS.Messaging.RabbitMQ;

internal sealed class RabbitMqConnectionSettings : IDisposable
{
    private readonly X509Certificate2? root;

    internal RabbitMqConnectionSettings(RabbitMqSettings settings, int maximumBodyBytes)
    {
        if (settings.TrustedRootCertificatePem is { } pem)
        {
            root = X509Certificate2.CreateFromPem(pem);
            if (root.HasPrivateKey)
            {
                root.Dispose();
                throw new ArgumentException("A broker trust root must contain public certificate material only.");
            }
        }
        Factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            VirtualHost = settings.VirtualHost,
            UserName = settings.UserName,
            Password = settings.Password,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(2),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
            RequestedHeartbeat = TimeSpan.FromSeconds(15),
            ContinuationTimeout = TimeSpan.FromSeconds(10),
            HandshakeContinuationTimeout = TimeSpan.FromSeconds(10),
            RequestedChannelMax = 64,
            MaxInboundMessageBodySize = checked((uint)maximumBodyBytes),
            ConsumerDispatchConcurrency = 1,
            Ssl = new SslOption
            {
                Enabled = true,
                ServerName = settings.ServerName,
                AcceptablePolicyErrors = SslPolicyErrors.None,
                CertificateValidationCallback = root is null ? null : ValidateServer
            }
        };
    }

    internal ConnectionFactory Factory { get; }

    private bool ValidateServer(object sender, X509Certificate? certificate, X509Chain? suppliedChain, SslPolicyErrors errors)
    {
        if (root is null || certificate is null
            || (errors & (SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch)) != 0)
        {
            return false;
        }
        using X509Certificate2 leaf = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.ApplicationPolicy.Add(new("1.3.6.1.5.5.7.3.1"));
        if (suppliedChain is not null)
        {
            foreach (X509ChainElement element in suppliedChain.ChainElements)
            {
                if (!element.Certificate.RawDataMemory.Span.SequenceEqual(leaf.RawDataMemory.Span))
                {
                    chain.ChainPolicy.ExtraStore.Add(element.Certificate);
                }
            }
        }
        return chain.Build(leaf);
    }

    public void Dispose()
    {
        root?.Dispose();
    }
}
