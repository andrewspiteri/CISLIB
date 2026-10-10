// WP02-S01 compile-only contract specification. Never include this file in CIS.Core.
// InternalCall declarations carry signatures into a reference-only assembly; no implementation is supplied.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CIS.Logging;

namespace CIS
{
    public enum EnvironmentKind : int
    {
        Unknown = 0,
        Console = 1,
        Service = 2,
        Web = 3,
        Function = 4,
    }

    public sealed class EnvironmentSetup
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern EnvironmentSetup();
        public extern Assembly? ApplicationAssembly { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern string? ApplicationName { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern string? BuildVersion { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern string? ServiceVersion { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern string? EnvironmentName { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern EnvironmentKind EnvironmentKind { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern string? CommitId { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern string? BuildId { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern LoggingSetup? Logging { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
    }

    public sealed class EnvironmentSnapshot
    {
        private EnvironmentSnapshot() { }
        public extern string ApplicationName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string BuildVersion { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string ServiceVersion { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string EnvironmentName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern EnvironmentKind EnvironmentKind { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string InstanceId { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern DateTimeOffset? ProcessStartedAtUtc { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern DateTimeOffset InitializedAtUtc { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? HostName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string OperatingSystem { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? OperatingSystemDescription { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? OSArchitecture { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? ProcessArchitecture { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? RuntimeDescription { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern int? ProcessId { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? CommitId { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? BuildId { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern LoggingOptions Logging { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern EventMetadata EventMetadata { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    }

    public static class EnvironmentOptions
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern EnvironmentSnapshot Initialize(EnvironmentSetup setup);
        public static extern EnvironmentSnapshot Current { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public static extern bool IsInitialized { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public static extern string ApplicationName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public static extern string BuildVersion { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public static extern string ServiceVersion { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public static extern string EnvironmentName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public static extern string? HostName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public static extern string OperatingSystem { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public static extern LoggingOptions Logging { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public static extern EventMetadata EventMetadata { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    }
}

namespace CIS.Logging
{
    public enum CoreLogLevel : int
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
        None = 6,
    }

    public enum LogOutputFormat : int
    {
        Human = 0,
        JsonLines = 1,
    }

    public enum ConsoleOutputStream : int
    {
        StandardError = 0,
        StandardOutput = 1,
    }

    public enum HumanOutputDetail : int
    {
        Normal = 0,
        Verbose = 1,
    }

    public sealed class LoggingSetup
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern LoggingSetup();
        public extern CoreLogLevel? Level { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern LogOutputFormat? ConsoleFormat { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern CoreLogLevel? ConsoleLevel { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern ConsoleOutputStream? ConsoleStream { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern HumanOutputDetail? HumanDetail { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern string? FilePath { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern LogOutputFormat? FileFormat { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern CoreLogLevel? FileLevel { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern int? QueueCapacity { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern int? FlushIntervalMilliseconds { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern int? ShutdownTimeoutMilliseconds { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern long? FileMaxBytes { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern int? FileRetainedFiles { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
        public extern int? SchemaVersion { [MethodImpl(MethodImplOptions.InternalCall)] get; [MethodImpl(MethodImplOptions.InternalCall)] set; }
    }

    public sealed class LoggingOptions
    {
        private LoggingOptions() { }
        public extern CoreLogLevel Level { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern LogOutputFormat ConsoleFormat { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern CoreLogLevel ConsoleLevel { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern ConsoleOutputStream ConsoleStream { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern HumanOutputDetail HumanDetail { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? FilePath { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern LogOutputFormat FileFormat { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern CoreLogLevel FileLevel { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern int QueueCapacity { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern TimeSpan FlushInterval { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern TimeSpan ShutdownTimeout { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern long FileMaxBytes { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern int FileRetainedFiles { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern int SchemaVersion { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern bool ConsoleEnabled { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern bool FileEnabled { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    }

    public sealed class EventMetadata
    {
        private EventMetadata() { }
        public extern string ApplicationName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string BuildVersion { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string ServiceVersion { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string EnvironmentName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern EnvironmentKind EnvironmentKind { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string InstanceId { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern DateTimeOffset? ProcessStartedAtUtc { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern DateTimeOffset InitializedAtUtc { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? HostName { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string OperatingSystem { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? OperatingSystemDescription { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? OSArchitecture { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? ProcessArchitecture { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? RuntimeDescription { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern int? ProcessId { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? CommitId { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string? BuildId { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    }

}

namespace CIS.Configuration
{
    public sealed class ConfigurationIssue
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern ConfigurationIssue(string code, string key, string explanation);
        public extern string Code { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string Key { [MethodImpl(MethodImplOptions.InternalCall)] get; }
        public extern string Explanation { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    }

    public sealed class CoreConfigurationException : Exception
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern CoreConfigurationException(IEnumerable<ConfigurationIssue> issues);
        public extern IReadOnlyList<ConfigurationIssue> Issues { [MethodImpl(MethodImplOptions.InternalCall)] get; }
    }

    public static class ConfigurationIssueCodes
    {
        public const string InvalidToken = "InvalidToken";
        public const string OutOfRange = "OutOfRange";
        public const string UnsupportedSchemaVersion = "UnsupportedSchemaVersion";
        public const string InvalidPath = "InvalidPath";
        public const string InvalidIdentity = "InvalidIdentity";
        public const string InvalidEnvironmentKind = "InvalidEnvironmentKind";
        public const string MissingApplicationName = "MissingApplicationName";
    }
}
