using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using BimIt.Revit.Addin.CSharpQuery;
using BimIt.Revit.CompilerHost;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Compiler.Tests;

public sealed class CompilerHostSliceTests
{
    private const string Collector = "return new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount();";

    private static readonly string ReferenceApi = Path.Combine(AppContext.BaseDirectory, "revit-ref", "RevitAPI.dll");

    [Fact]
    public void Collector_compiles_under_the_test_manifest()
    {
        var response = CompileTest(Collector);
        Assert.Equal(CompilerOutcomes.Success, response.Outcome);
        Assert.NotNull(response.Pe);
        Assert.True(response.Pe!.Length > 64);
        Assert.Equal((byte)'M', response.Pe[0]);
        Assert.DoesNotContain(response.Diagnostics, diagnostic => diagnostic.Code == CompilerResultCodes.PolicyRejected);
    }

    [Fact]
    public void Unknown_member_returns_a_bounded_csharp_diagnostic_and_no_pe()
    {
        var response = CompileTest("return doc.ThisMemberDoesNotExist;");
        Assert.Null(response.Pe);
        Assert.Equal(CompilerOutcomes.CompileFailed, response.Outcome);
        var diagnostic = Assert.Single(response.Diagnostics, item => item.Code.StartsWith("CS", StringComparison.Ordinal));
        Assert.InRange(diagnostic.Message.Length, 1, CompilerProtocol.MaxDiagnosticMessageCharacters);
        Assert.DoesNotContain("\n", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ReferenceApi, diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("return new Transaction(doc, \"x\");")]
    [InlineData("doc.Save();\nreturn 0;")]
    [InlineData("doc.Export(\"\", \"\");\nreturn 0;")]
    [InlineData("doc.SaveAs(\"\");\nreturn 0;")]
    [InlineData("return Environment.ProcessorCount;")]
    [InlineData("Console.WriteLine(\"a\");\nreturn 0;")]
    [InlineData("GC.Collect();\nreturn 0;")]
    [InlineData("return AppDomain.CurrentDomain.Id;")]
    [InlineData("return typeof(Wall).GetMethods().Length;")]
    [InlineData("int Local() { return 1; }\nreturn Local();")]
    [InlineData("System.Func<int, int> f = null;\nf = x => f(x);\nreturn 0;")]
    [InlineData("return System.IO.File.Exists(\"a\");")]
    [InlineData("return System.Net.Dns.GetHostName();")]
    [InlineData("return System.Diagnostics.Process.GetCurrentProcess().Id;")]
    [InlineData("return System.Reflection.Assembly.GetExecutingAssembly().Location;")]
    [InlineData("return System.Threading.Tasks.Task.Run(() => 1).Status.ToString();")]
    public void Policy_rejects_before_emit(string source)
    {
        var response = CompileTest(source);
        Assert.Null(response.Pe);
        Assert.Equal(CompilerOutcomes.PolicyRejected, response.Outcome);
        Assert.Contains(response.Diagnostics, diagnostic => diagnostic.Code == CompilerResultCodes.PolicyRejected);
        Assert.DoesNotContain(ReferenceApi, string.Join(" ", response.Diagnostics.Select(diagnostic => diagnostic.Message)), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Autodesk.Revit.DB.Electrical.ElectricalSystem circuit = null;\ncircuit.SelectPanel(null);\nreturn 0;")]
    [InlineData("Autodesk.Revit.DB.Electrical.ElectricalSystem circuit = null;\ncircuit.AddToCircuit(null);\nreturn 0;")]
    [InlineData("Autodesk.Revit.DB.Electrical.ElectricalSystem circuit = null;\ncircuit.RemoveFromCircuit(null);\nreturn 0;")]
    [InlineData("Autodesk.Revit.DB.Electrical.ElectricalSystem circuit = null;\ncircuit.DisconnectPanel();\nreturn 0;")]
    [InlineData("Autodesk.Revit.DB.Electrical.ElectricalSystem circuit = null;\ncircuit.NewWires(null, default(Autodesk.Revit.DB.Electrical.WiringType));\nreturn 0;")]
    [InlineData("Autodesk.Revit.DB.Electrical.ElectricalSystem circuit = null;\ncircuit.SetCircuitPath(null);\nreturn 0;")]
    [InlineData("Autodesk.Revit.DB.MEPSystem system = null;\nConnectorSet connectors = null;\nsystem.Add(connectors);\nreturn 0;")]
    [InlineData("Autodesk.Revit.DB.MEPSystem system = null;\nConnectorSet connectors = null;\nsystem.Remove(connectors);\nreturn 0;")]
    [InlineData("Autodesk.Revit.DB.MEPSystem system = null;\nsystem.DivideSystem(doc);\nreturn 0;")]
    [InlineData("ElementSet set = null;\nset.Insert(null);\nreturn 0;")]
    [InlineData("ElementSet set = null;\nset.Clear();\nreturn 0;")]
    [InlineData("ElementSet set = null;\nset.Erase(null);\nreturn 0;")]
    [InlineData("Autodesk.Revit.DB.Electrical.ElectricalSystem circuit = null;\ncircuit.CircuitPathMode = circuit.CircuitPathMode;\nreturn 0;")]
    [InlineData("doc.ActiveView.Scale++;\nreturn 0;")]
    [InlineData("doc.ActiveView.Scale--;\nreturn 0;")]
    [InlineData("doc.ActiveView.Scale += 1;\nreturn 0;")]
    public void Policy_v2_rejects_qualified_revit_effects(string source)
    {
        var response = CompileTest(source);
        Assert.Null(response.Pe);
        Assert.Equal(CompilerOutcomes.PolicyRejected, response.Outcome);
        Assert.Contains(response.Diagnostics, diagnostic => diagnostic.Code == CompilerResultCodes.PolicyRejected);
    }

    [Fact]
    public void Policy_v2_compiles_the_uc2_comparison_query()
    {
        const string source =
            """
            var targetCircuitNumber = "UC2";
            var maxScan = 7500;
            var maxReturnedCircuits = 3;
            var maxMemberEnumerate = 64;
            var maxMemberRefs = 32;
            var found = new List<Autodesk.Revit.DB.Electrical.ElectricalSystem>();
            var seen = 0;
            var scanStopped = false;
            using (var collector = new FilteredElementCollector(doc))
            {
                foreach (Element element in collector.OfClass(typeof(Autodesk.Revit.DB.Electrical.ElectricalSystem)))
                {
                    if (seen >= maxScan)
                    {
                        scanStopped = true;
                        break;
                    }

                    seen++;

                    if (element is Autodesk.Revit.DB.Electrical.ElectricalSystem circuit
                        && circuit.CircuitNumber == targetCircuitNumber)
                    {
                        found.Add(circuit);
                    }
                }
            }

            found.Sort((left, right) => string.CompareOrdinal(elementRef(left), elementRef(right)));
            var take = found.Count < maxReturnedCircuits ? found.Count : maxReturnedCircuits;
            var circuits = new List<object>();
            for (var index = 0; index < take; index++)
            {
                var circuit = found[index];
                var panel = circuit.BaseEquipment;
                var memberRefs = new List<string>();
                var memberCount = 0;
                var membersTruncated = false;
                var elements = circuit.Elements;
                if (elements != null)
                {
                    memberCount = elements.Size;
                    if (memberCount > maxMemberEnumerate)
                    {
                        membersTruncated = true;
                    }
                    else
                    {
                        foreach (object item in elements)
                        {
                            if (item is Element member)
                            {
                                memberRefs.Add(elementRef(member));
                            }
                        }

                        memberRefs.Sort(StringComparer.Ordinal);
                        if (memberRefs.Count > maxMemberRefs)
                        {
                            memberRefs.RemoveRange(maxMemberRefs, memberRefs.Count - maxMemberRefs);
                            membersTruncated = true;
                        }

                        if (memberRefs.Count < memberCount)
                        {
                            membersTruncated = true;
                        }
                    }
                }

                circuits.Add(new
                {
                    circuit_ref = elementRef(circuit),
                    circuit_number = circuit.CircuitNumber,
                    panel_ref = panel == null ? null : elementRef(panel),
                    member_count = memberCount,
                    members_truncated = membersTruncated,
                    member_refs = memberRefs
                });
            }

            return new
            {
                target_circuit_number = targetCircuitNumber,
                scanned_circuit_count = seen,
                scan_truncated = scanStopped,
                matched_circuit_count = found.Count,
                returned_circuit_count = circuits.Count,
                truncated = found.Count > circuits.Count,
                circuits
            };
            """;
        var response = CompileTest(source);
        Assert.Equal(CompilerOutcomes.Success, response.Outcome);
        Assert.NotNull(response.Pe);
        Assert.True(response.Pe!.Length > 64);
        Assert.DoesNotContain(response.Diagnostics, diagnostic => diagnostic.Code == CompilerResultCodes.PolicyRejected);
    }

    [Fact]
    public void Typeof_wall_remains_a_type_token()
    {
        var response = CompileTest("return new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount();");
        Assert.Equal(CompilerOutcomes.Success, response.Outcome);
        Assert.NotNull(response.Pe);
    }

    [Fact]
    public void Reserved_host_identifier_is_rejected()
    {
        var response = CompileTest(CompilerProtocol.ChargeIdentifier + "();\nreturn 0;");
        Assert.Null(response.Pe);
        Assert.Equal(CompilerOutcomes.InvalidRequest, response.Outcome);
        Assert.Contains(response.Diagnostics, diagnostic => diagnostic.Code == CompilerResultCodes.InvalidSource);
    }

    [Fact]
    public void Tight_loop_is_instrumented_with_the_charge_call()
    {
        const string source = "var n = 0;\nwhile (n < 1) { n++; }\nreturn n;";
        var rewritten = CSharpCompilationPipeline.InstrumentForTests(source);
        Assert.Contains(CompilerProtocol.ChargeIdentifier + "();", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain(CompilerProtocol.ChargeIdentifier, source, StringComparison.Ordinal);
        var response = CompileTest(source);
        Assert.Equal(CompilerOutcomes.Success, response.Outcome);
        Assert.NotNull(response.Pe);
        Assert.Contains(CompilerProtocol.ChargeIdentifier, System.Text.Encoding.UTF8.GetString(response.Pe!), StringComparison.Ordinal);
    }

    [Fact]
    public void Executable_path_is_the_absolute_product_relative_path()
    {
        Assert.True(CompilerHostLocation.TryResolve(@"C:\product\BimIt.Revit.Addin.dll", out var path));
        Assert.Equal(@"C:\product\compiler\BimIt.Revit.CompilerHost.exe", path);
        Assert.True(Path.IsPathRooted(path));
        Assert.False(CompilerHostLocation.TryResolve("BimIt.Revit.CompilerHost.exe", out _));
        var resolver = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.Compiler", "CompilerHostLocation.cs"));
        Assert.DoesNotContain("PATH", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEnvironmentVariable", resolver, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_executable_fails_closed_without_launch()
    {
        var result = CompilerHostClient.Run(@"C:\bimit-missing\compiler\BimIt.Revit.CompilerHost.exe", SampleRequest("return 1;"), 1_000);
        Assert.False(result.Accepted);
        Assert.Equal("compiler executable is missing", result.Message);
        Assert.DoesNotContain("return 1;", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeout_fails_closed()
    {
        var exe = CopyStub("timeout");
        var watch = Stopwatch.StartNew();
        var result = CompilerHostClient.Run(exe, SampleRequest("return 1; /* UNIQUE_BODY_SHOULD_NOT_LEAK */"), 300);
        watch.Stop();
        Assert.False(result.Accepted);
        Assert.Equal("compiler host timed out", result.Message);
        Assert.InRange(watch.ElapsedMilliseconds, 0, 5_000);
        Assert.DoesNotContain("UNIQUE_BODY_SHOULD_NOT_LEAK", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nonzero_exit_fails_closed_and_discards_a_success_payload()
    {
        var result = CompilerHostClient.Run(CopyStub("nonzero"), SampleRequest("return 1;"), 5_000);
        Assert.False(result.Accepted);
        Assert.Null(result.Response);
        Assert.Equal("compiler host failed", result.Message);
    }

    [Fact]
    public void Malformed_json_fails_closed()
    {
        var result = CompilerHostClient.Run(CopyStub("malformed"), SampleRequest("return 1;"), 5_000);
        Assert.False(result.Accepted);
        Assert.Equal("compiler response is malformed", result.Message);
    }

    [Fact]
    public void Oversized_request_fails_before_launch()
    {
        var exe = CopyStub("once");
        var request = SampleRequest(new string('b', CompilerProtocol.MaxRequestBytes));
        var result = CompilerHostClient.Run(exe, request, 5_000);
        Assert.False(result.Accepted);
        Assert.Equal("compiler request exceeds the size bound", result.Message);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "requests.log")));
    }

    [Fact]
    public void Oversized_response_fails_closed()
    {
        var result = CompilerHostClient.Run(CopyStub("oversize"), SampleRequest("return 1;"), 5_000);
        Assert.False(result.Accepted);
        Assert.Equal("compiler response exceeds the size bound", result.Message);
    }

    [Fact]
    public void Client_sends_one_request_to_one_process()
    {
        var exe = CopyStub("once");
        var result = CompilerHostClient.Run(exe, SampleRequest("return 1;"), 5_000);
        Assert.True(result.Accepted);
        var log = File.ReadAllLines(Path.Combine(Path.GetDirectoryName(exe)!, "requests.log"));
        Assert.Single(log);
    }

    [Fact]
    public void Success_metadata_is_verified()
    {
        var request = SampleRequest("return 1; /* UNIQUE_BODY_SHOULD_NOT_LEAK */");
        var result = CompilerHostClient.Run(CopyStub("success"), request, 5_000);
        Assert.True(result.Accepted);
        Assert.Equal(CompilerOutcomes.Success, result.Response!.Outcome);
        Assert.Equal(CompilerHashes.SourceSha256(request.Source), result.Response.SourceSha256);
        Assert.Equal(CompilerHashes.CacheKey(request.Source, request.RevitApi, request.PolicyVersion), result.Response.CacheKey);
        Assert.Equal((byte)'M', result.Response.Pe![0]);
        Assert.DoesNotContain("UNIQUE_BODY_SHOULD_NOT_LEAK", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Inconsistent_cache_key_is_rejected()
    {
        var result = CompilerHostClient.Run(CopyStub("mismatch"), SampleRequest("return 1;"), 5_000);
        Assert.False(result.Accepted);
        Assert.Null(result.Response?.Pe);
        Assert.Equal("compiler response metadata does not match the request", result.Message);
    }

    [Fact]
    public void Unsupported_identity_does_not_launch_the_child()
    {
        var exe = CopyStub("once");
        var product = Path.Combine(Path.GetDirectoryName(exe)!, "BimIt.Revit.Addin.dll");
        File.WriteAllBytes(product, []);
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(exe)!, "compiler"));
        File.Copy(exe, Path.Combine(Path.GetDirectoryName(exe)!, "compiler", CompilerProtocol.ExecutableFileName), overwrite: true);
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(exe)!))
        {
            var name = Path.GetFileName(file);
            if (name is "compiler-host-mode.txt" or "requests.log")
            {
                continue;
            }

            var destination = Path.Combine(Path.GetDirectoryName(exe)!, "compiler", name);
            if (!File.Exists(destination))
            {
                File.Copy(file, destination);
            }
        }

        var api = new LoadedRevitApi(@"C:\unique\RevitAPI.dll", new RevitApiIdentity("RevitAPI", "26.4.10.0", "none", 10));
        var result = new CSharpQueryCompilationService().Compile("return 1;", api, product, 1_000);
        Assert.False(result.Success);
        Assert.Equal(CompilerResultCodes.ApiUnsupported, result.Code);
        Assert.Null(result.Pe);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "compiler", "requests.log")));
    }

    [Fact]
    public void Supported_identity_reaches_the_client()
    {
        var directory = Path.GetDirectoryName(CopyStub("success"))!;
        var product = Path.Combine(directory, "BimIt.Revit.Addin.dll");
        File.WriteAllBytes(product, []);
        var compilerDir = Path.Combine(directory, "compiler");
        Directory.CreateDirectory(compilerDir);
        foreach (var file in Directory.GetFiles(directory))
        {
            File.Copy(file, Path.Combine(compilerDir, Path.GetFileName(file)), overwrite: true);
        }

        File.Move(
            Path.Combine(compilerDir, "BimIt.Revit.Compiler.ProcessStub.exe"),
            Path.Combine(compilerDir, CompilerProtocol.ExecutableFileName),
            overwrite: true);
        var api = new LoadedRevitApi(@"C:\unique\RevitAPI.dll", new RevitApiIdentity("RevitAPI", "26.5.0.0", "abc", 10));
        var result = new CSharpQueryCompilationService().Compile(Collector, api, product, 5_000);
        Assert.True(result.Success);
        Assert.NotNull(result.Pe);
        Assert.Equal("26.5.0.0", result.Api!.Version);
    }

    [Fact]
    public void Cache_key_changes_with_source_policy_and_identity()
    {
        var api = new RevitApiIdentity("RevitAPI", "26.5.0.0", "abc", 10);
        var first = CompilerHashes.CacheKey("return 1;", api, 1);
        Assert.Equal(2, CompilerProtocol.PolicyVersion);
        Assert.NotEqual(first, CompilerHashes.CacheKey("return 2;", api, 1));
        Assert.NotEqual(first, CompilerHashes.CacheKey("return 1;", api, CompilerProtocol.PolicyVersion));
        Assert.NotEqual(first, CompilerHashes.CacheKey("return 1;", api, 2));
        Assert.NotEqual(first, CompilerHashes.CacheKey("return 1;", api with { Length = 11 }, 1));
        Assert.NotEqual(first, CompilerHashes.CacheKey("return 1;", api with { Version = "26.4.10.0" }, 1));
    }

    [Fact]
    public void Production_executable_rejects_an_unreviewed_identity_on_the_process_boundary()
    {
        var configuration = typeof(CompilerHostSliceTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
        var exe = Path.Combine(RepoRoot(), "src", "BimIt.Revit.CompilerHost", "bin", configuration, "net8.0", CompilerProtocol.ExecutableFileName);
        var result = CompilerHostClient.Run(exe, Request(ReferenceIdentity(), "return 1; /* UNIQUE_BODY_SHOULD_NOT_LEAK */"), 15_000);
        Assert.True(result.Accepted);
        Assert.Equal(CompilerResultCodes.ApiUnsupported, result.Code);
        Assert.Null(result.Response!.Pe);
        Assert.DoesNotContain(ReferenceApi, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNIQUE_BODY_SHOULD_NOT_LEAK", result.Response.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_host_rejects_an_unreviewed_identity_before_emit()
    {
        var response = CSharpCompilationPipeline.Compile(Request(ReferenceIdentity(), "return 1;"));
        Assert.Equal(CompilerOutcomes.ApiUnsupported, response.Outcome);
        Assert.Null(response.Pe);
        Assert.DoesNotContain(ReferenceApi, response.Diagnostics[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_package_contains_the_compiler_and_not_roslyn_at_the_root()
    {
        var configuration = typeof(CompilerHostSliceTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
        var output = Path.Combine(RepoRoot(), "src", "BimIt.Revit.Addin", "bin", "2026", configuration, "net8.0-windows");
        Assert.True(File.Exists(Path.Combine(output, "compiler", CompilerProtocol.ExecutableFileName)));
        Assert.False(File.Exists(Path.Combine(output, CompilerProtocol.ExecutableFileName)));
        Assert.False(File.Exists(Path.Combine(output, "Microsoft.CodeAnalysis.dll")));
        Assert.False(File.Exists(Path.Combine(output, "Microsoft.CodeAnalysis.CSharp.dll")));
        Assert.True(File.Exists(Path.Combine(output, "compiler", "Microsoft.CodeAnalysis.dll")));
        Assert.True(File.Exists(Path.Combine(output, "compiler", "refs", "net10.0", "System.Runtime.dll")));
        Assert.True(File.Exists(Path.Combine(output, "compiler", "refs", "net10.0", "System.Collections.dll")));
        Assert.False(File.Exists(Path.Combine(output, "refs", "net10.0", "System.Runtime.dll")));
    }

    [Fact]
    public void Compilation_references_come_only_from_the_packaged_directory()
    {
        var packaged = CompilationReferences.PackagedDirectory();
        Assert.EndsWith(Path.Combine("refs", CompilationReferences.Moniker), packaged, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppContext.BaseDirectory, packaged, StringComparison.OrdinalIgnoreCase);
        var references = CompilationReferences.Create(ReferenceApi);
        Assert.Contains(references, reference => reference.Display!.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, reference => reference.Display!.EndsWith("System.Collections.dll", StringComparison.OrdinalIgnoreCase));
        foreach (var reference in references)
        {
            var display = reference.Display ?? "";
            var packagedReference = display.StartsWith(packaged + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            var revitReference = display.Equals(Path.GetFullPath(ReferenceApi), StringComparison.OrdinalIgnoreCase)
                || display.Equals(ReferenceApi, StringComparison.OrdinalIgnoreCase);
            Assert.True(packagedReference || revitReference, display);
            Assert.DoesNotContain("Microsoft.NETCore.App.Ref", display, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"{Path.DirectorySeparatorChar}packs{Path.DirectorySeparatorChar}", display, StringComparison.OrdinalIgnoreCase);
        }

        var incomplete = Path.Combine(Path.GetTempPath(), "bimit-compiler-refs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incomplete);
        try
        {
            File.Copy(Path.Combine(packaged, "System.Collections.dll"), Path.Combine(incomplete, "System.Collections.dll"));
            Assert.Throws<ReferencePackUnavailableException>(() => CompilationReferences.Create(ReferenceApi, incomplete));
        }
        finally
        {
            Directory.Delete(incomplete, true);
        }

        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BimIt.Revit.CompilerHost", "CompilationReferences.cs"));
        Assert.DoesNotContain("Microsoft.NETCore.App.Ref", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEnvironmentVariable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DOTNET_ROOT", source, StringComparison.Ordinal);
        Assert.DoesNotContain("packs", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_projects_keep_roslyn_protocol_and_tool_count()
    {
        var root = RepoRoot();
        foreach (var project in Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(project);
            Assert.DoesNotContain("QueryRuntimeSpike", text, StringComparison.Ordinal);
            if (project.EndsWith("BimIt.Revit.CompilerHost.csproj", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("Microsoft.CodeAnalysis.CSharp", text, StringComparison.Ordinal);
                continue;
            }

            Assert.DoesNotContain("PackageReference Include=\"Microsoft.CodeAnalysis", text, StringComparison.Ordinal);
        }

        var addin = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "BimIt.Revit.Addin.csproj"));
        Assert.DoesNotContain("ProjectReference Include=\"..\\BimIt.Revit.CompilerHost", addin, StringComparison.Ordinal);
        var service = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Addin", "CSharpQuery", "CSharpQueryCompilationService.cs"));
        Assert.DoesNotContain("AssemblyLoadContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("EnqueueAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Assembly.Load", service, StringComparison.Ordinal);
        Assert.Equal(26, BridgeProtocol.CurrentVersion);
        var server = File.ReadAllText(Path.Combine(root, "src", "BimIt.Revit.Server", "ServerHost.cs"));
        Assert.Equal(23, Regex.Matches(server, @"\.With\w+Tool\(\)").Count);
    }

    private static CompilerResponse CompileTest(string source)
    {
        return CSharpCompilationPipeline.CompileForTestManifest(Request(ReferenceIdentity(), source));
    }

    private static CompilerRequest Request(RevitApiIdentity identity, string source)
    {
        return new CompilerRequest
        {
            ContractVersion = CompilerProtocol.ContractVersion,
            PolicyVersion = CompilerProtocol.PolicyVersion,
            Source = source,
            RevitApiPath = ReferenceApi,
            RevitApi = identity
        };
    }

    private static RevitApiIdentity ReferenceIdentity()
    {
        return RevitApiIdentity.FromFile(ReferenceApi);
    }

    private static CompilerRequest SampleRequest(string source)
    {
        return new CompilerRequest
        {
            ContractVersion = CompilerProtocol.ContractVersion,
            PolicyVersion = CompilerProtocol.PolicyVersion,
            Source = source,
            RevitApiPath = @"C:\unique\RevitAPI.dll",
            RevitApi = new RevitApiIdentity("RevitAPI", "26.5.0.0", "abc", 10)
        };
    }

    private static string CopyStub(string mode)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "compiler-stub");
        var destination = Path.Combine(Path.GetTempPath(), "bimit-compiler-stub", Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        File.WriteAllText(Path.Combine(destination, "compiler-host-mode.txt"), mode);
        return Path.Combine(destination, "BimIt.Revit.Compiler.ProcessStub.exe");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BimIt.Revit.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
