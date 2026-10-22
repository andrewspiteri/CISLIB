using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

// Reviewed native-source reruns. Source-removal/JIT diagnostics and unrelated
// finite runs cannot enter this reader by supplying an approval flag or new ID.
internal static class HealthcheckMutationFiniteAssertionCatalog
{
    internal const string CancellationStaleTestPdbSha256 = "1a81a413d6fdfc1f1dbe527fc61b543b5321d405dabf70fb8f62535c1646a071";
    internal const string CancellationCleanCorePdbSha256 = "1fc3b2a5a27159285ee65700f563a98148808d127ade958a10b4da7fa40bf8c9";
    internal static string CancellationPhaseSha256(string name) => name switch
    {
        "initialTrx" => "3ba1e734ad5ec0fd137b49596b2fed68c96db453dc1a8d6514c6647853c4acad",
        "initialProcess" => "7390911462476ed5cc73f94c1338f3bb5c0d4b2b031e20d05a2f8103279d8f97",
        "freshTrx" => "8230d3e740f6024f91d23f4ea82a7f764fb9f4ce153d6866ddf6bda29de446f1",
        "freshProcess" => "b9a1434efed9c3c64c1f74b89b2ca588949622b24c5d0c6a8016ad4a0e1d50b8",
        "freshBuildProcess" => "d817adc17743f046ab72fd11ee8377a612f170b2a73d17dce6bf85c8d8b58578",
        "backupAbsence" => "9d49e02836f7f2fe7c8794cfc353902808e6ca22042d55c3eed123330f1f1229",
        "sourceAfterNative" => "5d62d369ab002be858f683aea23f0ccd7de8e9f9c6794b9ea0091b729641180f",
        "sourceAfterFresh" => "359eff377b5a8acf114adf764de7094ccc7e3ff8adb4d532a267951302a431a5",
        "marker" => "00f42942b47e0d3a191fe4146edd9c5abc2266dc093d2afc6735aaa130dc5d20",
        _ => throw new InvalidDataException("Unknown reviewed finite cancellation phase.")
    };
    internal const string CancellationFilter = "FullyQualifiedName=CIS.Tests.UnitTests.Healthcheck.HealthCancellationObservationBoundaryTests.ACurrentCancellationObserverFaultCannotPublishAPartialDiagnosticOrEscapeStopping|FullyQualifiedName=CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode";
    internal const string CancellationRoot = TaskRoot + "/unit-finite-29";
    internal const string CancellationNativeRoot = CancellationRoot + "/source/implementations/csharp/src/CIS.Core";
    internal const string CancellationConfigSha256 = "98da5bdb2d6911a4092eba6f65311a0a9685ff86f3ae7ebaa6a473985cd520e5";
    internal const string CancellationCaptureSha256 = "776d602d66941c0da1584b28cead30dd7eec28c114c8d3cf5127186e7b5fa5ae";
    internal const string CancellationProcessSha256 = "d417f7701e6c19ed950cc20525db569a9204930c9a104495b2e39fef220ba23f";
    internal const string CancellationControllerSha256 = "880eba94cea4558255744d791de488d3e3bfd54b41a57629ec37ad3b14b494f3";
    internal const string CancellationControllerAssemblySha256 = "f18fb2b76892f16b9515cd0bc8f3dd20231a038dcb60f65139360912f7125cf1";
    internal const string CancellationObjectModelSha256 = "ff730c68bebe827f74e2bdcfb571400ab328c00be11c7fe3a1fe01143b483bc6";
    internal const string CancellationPostFreshSha256 = "61e83421c91be1c02bb54f450951c827584b6432711c15a182ad00d13634e84f";
    internal const string CancellationKernelInventorySha256 = "6ed8e24009f97b7e052b546dff3ff41b60a513929e7f90c3f70b14a5591ae981";
    internal const string CancellationLogInventorySha256 = "95646c41ba9a99a5e4108109e809c067d15eecd70d79496771b4797f3c11622f";
    internal static HealthcheckMutationFiniteAssertionValidation.Spec CurrentCancellation { get; } = new(
        new Definition("1904", "Healthcheck/Internal/State/HealthStateCoordinator.Collection.cs",
            "a4d9049a2a1304a40d487584c708e85749f2e6a9f9305855bb690afa48315240", 11973, 11991,
            "LogicalNotExpression to un-LogicalNotExpression mutation", "!attachment.Closed", "attachment.Closed", "Killed",
            ["1760a109-d3b3-86cd-8afb-033bc69612e2", "10a20c34-018f-83f1-a72d-a8fe98437e3f",
            "150cb59c-8a3d-8aad-89d9-f034ec82f2d8", "185e4a86-88e9-80b4-b48f-ff7cdde3fa30", "1fd70a80-f519-89b3-adc6-4d253e51113c",
            "1dfbae71-d3ae-8185-a80f-f52c49d982c5", "1a3170c7-adf6-87eb-9808-9f3d98121e6d", "1ba12cb7-9ad3-8ca2-bee3-d312b8deffb7",
            "14290b6c-18ce-8b45-acf6-6f51eadbf913", "1bb8b977-82cf-8a05-a867-fc1e20473532", "18a7c251-5a42-87d1-8352-19628f7aaf48",
            "1cdaa459-a257-8314-a09e-84cfd04b219b", "1ca1c923-5210-8114-903f-55d2ccdfdd6f", "17df07d8-1485-89ed-b03f-c5dff9082ee7",
            "1b6bcdec-2be8-8650-b4d3-9facd5b6771d", "1fd74e89-27c8-8221-8a0e-cdddd78b7041", "1a9bae23-8be2-8e6b-9196-c28b606dd208",
            "14d0fd3b-7645-88fc-a0a5-7c6866e4122d", "13341170-0319-8208-84b3-8a51f4ae71e3", "135d8276-2233-80b6-95dc-3924ed266a02",
            "19d3aacb-c36a-8b2a-b38f-eb73d530b070", "133f4533-6c12-87a7-8d1d-e0cf27879c89", "1a8ba6bb-4b3c-89d9-9bbb-3138481b04a2",
            "1f3cd994-e51f-8b19-a406-b44dfe0ad4cd", "10d4757c-2fad-8f85-9132-0c26c748c3a0", "122c90eb-7af0-8d62-a2f1-759d963efd70",
            "1f3efce1-c0d4-8a74-bd7f-6e9b40f83854", "1b785b65-8059-8b44-8acc-5547f0e8f9fa", "111a4de5-103b-8239-80f3-9866349f426f",
            "14aeed81-e2ea-845a-8f65-d30024e54958", "1525e67c-0adf-88a8-b30c-53bf38c1f6b5", "1023aec1-5830-8e80-b907-723f4b7c16c3",
            "14302517-b6ec-8980-a51e-9df833387849", "12a13050-3662-84cc-851e-af2324d07230", "123ab3f8-50be-8713-aa67-c0b5b83b89e4",
            "12bca052-00fb-87da-885f-c60906e10946", "1119547f-ce82-8771-8693-c3d74281b838", "1b44bbaa-b3ae-8ab7-90db-7a4362ecfd66",
            "17d48b3a-66f2-85c0-be10-c3c1502c93ac", "1c27ff89-f3d9-83bc-a993-38de4d99f56a", "1997a45d-173c-8dbb-9a66-54180145cb43",
            "17016c3f-8a90-8550-86fa-6ebb4bc28516", "1aa1827a-69f3-8ef6-a7b7-2cf5b1b9d069"]),
        "056cc405715ac814bbad9ff45e276e77708c56463beea24ad76e23d19f3bf89d",
        "ddeab9dc073b89fe0e7120c0bffa58216053fb3f5b74bb02bdaa3a6bcbc8c12a",
        "3aec60743af897759d4f417c484e6adbc2dc35579b5c32bc694ee2bb733a2929",
        "ee45fb9e069bce6cbf8912596673d0a9d9861eab4e83079bc17284c955f445d5", 1442,
        "implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Collection/HealthCancellationObservationBoundaryTests.cs",
        "1fc94234ceaf5674cb97d355c04060a1120a74c412e775da6348f93d63dfc753",
        "1760a109-d3b3-86cd-8afb-033bc69612e2",
        "CIS.Tests.UnitTests.Healthcheck.HealthCancellationObservationBoundaryTests.ACurrentCancellationObserverFaultCannotPublishAPartialDiagnosticOrEscapeStopping",
        "13bace67-4994-8cc8-a842-fea7095eb28a",
        "Assertion failed. Expected values to be equal.\n\nexpected: Reserved\nactual:   Retired\n\nAssert.AreEqual(HealthCollectionReservationDisposition.Reserved, reserved.Disposition)",
        AdditionalTargetGuids: ["10a20c34-018f-83f1-a72d-a8fe98437e3f"]);
    internal const string Filter = "FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck.HealthCollectionIdleWithdrawalReceivingTests.|FullyQualifiedName=CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode";
    internal const string TaskRoot = "/data/cis-healthcheck-20261021-01a15a72";
    internal const string NativeRoot = TaskRoot + "/finite-unit-22/source/implementations/csharp/src/CIS.Core";
    internal const string ParentConfigSha256 = "331bb05cfce76e8bd5d861daf7f6f15c5566c3bc88a6a9d603e8c61e04de2118";
    internal const string FiniteConfigSha256 = "4ae175c3cb97e30074b6410d32479adc2b8f9f415dcfbfe85be6b5541402eddd";
    internal const string CaptureSha256 = "0216a0a795346a27913322e599f0ea02a2f520cc2f1611ba5e1fca777eac2ea3";
    internal const string ObjectModelSha256 = "436ddc36a43ffd9d44e5cded13c5513907699871c735d8cc529f983891c1c669";
    internal const string StrykerCoreSha256 = "bf02f78d3725e1243edb7a32d88dc1a02c88f1f9f74b60b18730636073f234d7";
    internal const string StrykerCliSha256 = "1d5fbd3df14f3ab0425421c597a36a5cbdd474ad9804a0bd4e212e5b0320eba9";
    internal const string ProcessTraceSha256 = "d57dfc3f963b48b49b490b196ce231c4d6766e826c305eb32139a22e77c96a5b";
    internal static HealthcheckMutationFiniteAssertionValidation.Spec IdleWithdrawal { get; } = new(
        new Definition("1896", "Healthcheck/Internal/State/HealthStateCoordinator.Collection.cs",
            "a4d9049a2a1304a40d487584c708e85749f2e6a9f9305855bb690afa48315240", 11504, 11547,
            "Statement mutation", "CloseAttachmentLocked(attachment, effects);", ";", "Timeout", []),
        "056cc405715ac814bbad9ff45e276e77708c56463beea24ad76e23d19f3bf89d",
        "9595873ded6a422d1854023581bb9273de1d676b491ad40e340dc627a3bcf8c4",
        "d85f5f82ba6bec45ba55f8f8d8750eb0c06af793b4cc16b85f7b8a2cc03e5364",
        "696a15367757c098194d997e76782248d52f760278e7a1338880667ced019ec6", 1440,
        "implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Collection/HealthCollectionIdleWithdrawalReceivingTests.cs",
        "dbf86117345343b7ec1cbac7d3984156080907b7fdf061c1453df06b03a3bd46",
        "17ba1d61-9933-8658-b061-d35fe94ccbdf",
        "CIS.Tests.UnitTests.Healthcheck.HealthCollectionIdleWithdrawalReceivingTests.WithdrawingAnIdleCollectionRejectsNewReservationsAndPreservesItsExternalMonitor",
        "13bace67-4994-8cc8-a842-fea7095eb28a",
        "Assertion failed. Expected values to be equal.\nWithdrawal must close admission even when there was no active operation to cancel.\n\nexpected: Retired\nactual:   Reserved\n\nAssert.AreEqual(HealthCollectionReservationDisposition.Retired, denied.Disposition)",
        "f37236dbfcfa0ce39c731506db6d8acb4d1bbc7fad8c9d9f3c48d48e6804794b",
        ["run_withdrawal22b()", "--config-file \"$hc_task/finite-unit-22/unit.json\"", "native_exit=$?", "cp \"$(cat \"$originals/actual-backup-paths.txt\")\" \"$originals/CIS.Core.dll.stryker-unchanged\"",
            "cp \"$hc_task/finite-unit-22/source/implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll\" \"$originals/source-CIS.Core.dll\"",
            "cp \"$hc_task/finite-unit-22/source/implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.dll\" \"$originals/test-CIS.Core.dll\"",
            "CIS.Core.UnitTests.runtimeconfig.json\" \"$originals/\"", "observed-before-fresh.txt", "sha256sum \"$originals\"/*", "}; run_withdrawal22b"]);
}
