using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace FuelPro.Tests;

public class ReleaseBinaryVerificationTests
{
    private static readonly byte[] LdcR8_10 = new byte[] { 0x23, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x24, 0x40 };

    [Fact]
    public void Verify_ReleaseBinariesAndInstaller_ExistWithVersion1333()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var uiDllPath = Path.Combine(repoRoot, "publish_output", "FuelPro.UI.dll");
        var coreDllPath = Path.Combine(repoRoot, "publish_output", "FuelPro.Core.dll");
        var installerPath = Path.Combine(repoRoot, "installer_output", "PyroSync_Setup_v1.3.33.exe");

        Assert.True(File.Exists(uiDllPath), $"FuelPro.UI.dll missing in publish_output: {uiDllPath}");
        Assert.True(File.Exists(coreDllPath), $"FuelPro.Core.dll missing in publish_output: {coreDllPath}");
        Assert.True(File.Exists(installerPath), $"Installer missing in installer_output: {installerPath}");

        var uiVer = FileVersionInfo.GetVersionInfo(uiDllPath);
        Assert.Equal("1.3.33.0", uiVer.FileVersion);

        var installerFileInfo = new FileInfo(installerPath);
        Assert.True(installerFileInfo.Length > 50_000_000, "Installer size seems too small");
    }

    [Fact]
    public void Verify_PublishOutput_ILBytecode_NoToleranceInDsmLossMethods()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var uiDllPath = Path.Combine(repoRoot, "publish_output", "FuelPro.UI.dll");
        var coreDllPath = Path.Combine(repoRoot, "publish_output", "FuelPro.Core.dll");

        VerifyDllMethodsDoNotContainLdcR8_10(uiDllPath, new[] { "DsmApprovalQueueViewModel" });
        VerifyDllMethodsDoNotContainLdcR8_10(coreDllPath, new[] { "DsmEntryService" });
    }

    private static void VerifyDllMethodsDoNotContainLdcR8_10(string dllPath, string[] targetTypeSubstrings)
    {
        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);
        var mdReader = peReader.GetMetadataReader();

        int inspectedMethodCount = 0;

        foreach (var methodHandle in mdReader.MethodDefinitions)
        {
            var method = mdReader.GetMethodDefinition(methodHandle);
            var declaringType = mdReader.GetTypeDefinition(method.GetDeclaringType());
            var typeName = mdReader.GetString(declaringType.Name);
            var methodName = mdReader.GetString(method.Name);

            // Check declaring type or enclosing type if nested (for async state machines)
            bool isTargetType = targetTypeSubstrings.Any(t => typeName.Contains(t));
            if (!isTargetType && declaringType.IsNested)
            {
                var declaringEnclosingType = mdReader.GetTypeDefinition(declaringType.GetDeclaringType());
                var enclosingTypeName = mdReader.GetString(declaringEnclosingType.Name);
                isTargetType = targetTypeSubstrings.Any(t => enclosingTypeName.Contains(t));
            }

            if (!isTargetType)
                continue;

            if (method.RelativeVirtualAddress == 0)
                continue;

            var methodBody = peReader.GetMethodBody(method.RelativeVirtualAddress);
            var ilBytes = methodBody.GetILBytes();

            inspectedMethodCount++;

            // Verify that this method in the target service/viewmodel does not load 10.0 (ldc.r8 10.0)
            // for shortage/loss calculation.
            // If ldc.r8 10.0 is present in the IL bytes, fail immediately.
            bool containsLdcR8_10 = ContainsPattern(ilBytes, LdcR8_10);
            Assert.False(containsLdcR8_10,
                $"Method {typeName}.{methodName} in {Path.GetFileName(dllPath)} contains IL opcode ldc.r8 10.0!");
        }

        Assert.True(inspectedMethodCount > 0, $"Expected to inspect methods in {Path.GetFileName(dllPath)}, but found none.");
    }

    private static bool ContainsPattern(byte[] source, byte[] pattern)
    {
        if (pattern.Length == 0 || source.Length < pattern.Length)
            return false;

        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return true;
        }

        return false;
    }
}
