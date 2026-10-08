using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Xml.Linq;

/// <summary>
/// Packaging build entrypoint for Window Switcher.
/// Produces a Windows NSIS installer on Windows and an AppImage on Linux.
/// </summary>
sealed class Build
{
    private const string AppImageUpdateInformation =
        "gh-releases-zsync|SebastienDuruz|Window-Switcher|latest|WindowSwitcher-*-x86_64.AppImage.zsync";

    readonly BuildOptions options;
    readonly IReadOnlyList<BuildTarget> targets;
    readonly HashSet<string> visitedTargets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs the requested target and its dependencies.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = BuildOptions.Parse(args);
            var build = new Build(options);

            if (options.ShowHelp)
            {
                build.WriteHelp();
                return 0;
            }

            await build.RunAsync(options.Target);
            Console.WriteLine("Build succeeded.");
            return 0;
        }
        catch (BuildFailedException exception)
        {
            Console.Error.WriteLine($"Build failed: {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Build failed: {exception}");
            return 1;
        }
    }

    Build(BuildOptions options)
    {
        this.options = options;
        RootDirectory = options.RootDirectory ?? FindRootDirectory();
        targets =
        [
            new("ValidateParameters", "Validates the supported runtime identifiers.", TargetHost.Any, [], Synchronous(ValidateParameters)),
            new("Restore", "Restores the application dependencies.", TargetHost.Any, ["ValidateParameters"], Synchronous(Restore)),
            new("Compile", "Builds the application project.", TargetHost.Any, ["Restore"], Synchronous(Compile)),
            new("PublishWindows", "Publishes the application for the Windows runtime.", TargetHost.Windows, ["Compile"], Synchronous(PublishWindows)),
            new("Installer", "Builds the NSIS installer from the Windows publish output.", TargetHost.Windows, ["PublishWindows"], Synchronous(BuildInstaller)),
            new("PublishLinux", "Publishes the application for the Linux runtime.", TargetHost.Linux, ["Compile"], Synchronous(PublishLinux)),
            new("AppImage", "Packages the AppImage and its zsync file from the Linux publish output.", TargetHost.Linux, ["PublishLinux"], PackageAppImageAsync),
            new("WindowsArtifacts", "Produces the Windows artifacts.", TargetHost.Windows, ["Installer"], null),
            new("LinuxArtifacts", "Produces the Linux artifacts.", TargetHost.Linux, ["AppImage"], null),
            new("Artifacts", "Produces the artifacts for the current host (default).", TargetHost.Any, ["WindowsArtifacts", "LinuxArtifacts"], null),
        ];
    }

    string RootDirectory { get; }

    string AppProjectPath => Path.Combine(RootDirectory, "src", "WindowSwitcher", "WindowSwitcher.csproj");
    string VersionPropsPath => Path.Combine(RootDirectory, "Directory.Build.props");

    string AssetsDirectory => Path.Combine(RootDirectory, "build", "assets");
    string InstallerAssetsDirectory => Path.Combine(AssetsDirectory, "installer");
    string LinuxPackagingDirectory => Path.Combine(AssetsDirectory, "packaging", "linux");

    string ArtifactsDirectory => Path.Combine(RootDirectory, "build", "artifacts");
    string ToolsDirectory => Path.Combine(ArtifactsDirectory, "tools");

    string InstallerNsiPath => Path.Combine(InstallerAssetsDirectory, "WindowSwitcher.nsi");

    string EffectiveVersion => options.Version ?? ReadVersionFromProps(VersionPropsPath);
    string EnableSentryTelemetryProperty => options.EnableSentryTelemetry ? "true" : "false";
    string TelemetryBuildProperties =>
        $"-p:EnableSentryTelemetry={EnableSentryTelemetryProperty} " +
        $"-p:TelemetryDistributionChannel={NormalizeTelemetryBuildLabel(options.DistributionChannel)} " +
        $"-p:TelemetryPackageKind={NormalizeTelemetryBuildLabel(options.PackageKind)}";

    string EffectiveWindowsPublishDir => options.WindowsPublishDir ?? Path.Combine(ArtifactsDirectory, "publish", options.WindowsRuntime);
    string EffectiveLinuxPublishDir => options.LinuxPublishDir ?? Path.Combine(ArtifactsDirectory, "publish", options.LinuxRuntime);
    string EffectiveInstallerOutDir => options.InstallerOutDir ?? Path.Combine(ArtifactsDirectory, "installer");
    string EffectiveAppImageOutDir => options.AppImageOutDir ?? Path.Combine(ArtifactsDirectory, "appimage");

    /// <summary>
    /// Runs the dependencies of a target, then the target itself when it supports the current host.
    /// Each target runs at most once per build.
    /// </summary>
    async Task RunAsync(string targetName)
    {
        var target = targets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, targetName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            throw new BuildFailedException($"Unknown target: {targetName}. Run with --help to list targets.");

        if (!visitedTargets.Add(target.Name))
            return;

        foreach (var dependency in target.DependsOn)
            await RunAsync(dependency);

        if (target.Execute is null)
            return;

        if (!target.SupportsCurrentHost)
        {
            Console.WriteLine($"> {target.Name}: skipped, requires a {target.Host} host");
            return;
        }

        Console.WriteLine($"> {target.Name}");
        await target.Execute();
    }

    /// <summary>
    /// Writes the available targets and options to the console.
    /// </summary>
    void WriteHelp()
    {
        Console.WriteLine("Usage: build/build.sh | build\\build.cmd [--target <name>] [options]");
        Console.WriteLine();
        Console.WriteLine("Targets:");
        foreach (var target in targets)
            Console.WriteLine($"  {target.Name,-20}{target.Description}");

        Console.WriteLine();
        Console.WriteLine(BuildOptions.HelpText);
    }

    /// <summary>
    /// Validates supported runtime identifiers.
    /// </summary>
    void ValidateParameters()
    {
        EnsureWindowsRuntimeSupported(options.WindowsRuntime);
        EnsureLinuxRuntimeSupported(options.LinuxRuntime);
    }

    /// <summary>
    /// Restores project dependencies.
    /// </summary>
    void Restore() => RunDotNet($"restore \"{AppProjectPath}\" {TelemetryBuildProperties}");

    /// <summary>
    /// Builds the application project.
    /// </summary>
    void Compile() => RunDotNet($"build \"{AppProjectPath}\" -c {options.Configuration} {TelemetryBuildProperties}");

    /// <summary>
    /// Publishes the application for the configured Windows runtime.
    /// </summary>
    void PublishWindows()
    {
        PublishForRuntime(options.WindowsRuntime, EffectiveWindowsPublishDir);
        AssertPublishOutputNotEmpty(EffectiveWindowsPublishDir);
    }

    /// <summary>
    /// Builds the Windows NSIS installer from Windows publish output.
    /// </summary>
    void BuildInstaller()
    {
        Ensure(File.Exists(InstallerNsiPath), $"NSIS script not found: {InstallerNsiPath}");

        Directory.CreateDirectory(EffectiveInstallerOutDir);

        var installerFile = Path.Combine(
            EffectiveInstallerOutDir,
            $"WindowSwitcher-setup-{EffectiveVersion}-{ToWindowsInstallerArchitecture(options.WindowsRuntime)}.exe");
        var publishGlob = Path.Combine(EffectiveWindowsPublishDir, "*");

        var makensisArguments =
            $"-DAPP_VERSION={EffectiveVersion} " +
            $"-DPUBLISH_DIR=\"{EffectiveWindowsPublishDir}\" " +
            $"-DPUBLISH_GLOB=\"{publishGlob}\" " +
            $"-DOUT_FILE=\"{installerFile}\" " +
            $"\"{InstallerNsiPath}\"";

        RunProcess(ResolveMakensis(), makensisArguments);
        Ensure(File.Exists(installerFile), $"Installer was not created at: {installerFile}");
    }

    /// <summary>
    /// Publishes the application for the configured Linux runtime.
    /// </summary>
    void PublishLinux()
    {
        PublishForRuntime(options.LinuxRuntime, EffectiveLinuxPublishDir);

        var publishedExecutable = Path.Combine(EffectiveLinuxPublishDir, "WindowSwitcher");
        var publishedPipeWireLibrary = Path.Combine(EffectiveLinuxPublishDir, "libwindowswitcher-pipewire.so");
        Ensure(File.Exists(publishedExecutable), $"Published binary not found at: {publishedExecutable}");
        Ensure(
            File.Exists(publishedPipeWireLibrary),
            $"Published PipeWire library not found at: {publishedPipeWireLibrary}");
        AssertElfX64(publishedExecutable);
        AssertElfX64(publishedPipeWireLibrary);
    }

    /// <summary>
    /// Packages a Linux AppImage from Linux publish output.
    /// </summary>
    async Task PackageAppImageAsync()
    {
        Directory.CreateDirectory(EffectiveAppImageOutDir);

        var appDir = Path.Combine(EffectiveAppImageOutDir, "WindowSwitcher.AppDir");
        RecreateDirectory(appDir);

        var appDirBin = Path.Combine(appDir, "usr", "bin");
        var appDirApplications = Path.Combine(appDir, "usr", "share", "applications");
        var appDirIcons = Path.Combine(appDir, "usr", "share", "icons", "hicolor", "256x256", "apps");
        var appDirLicenses = Path.Combine(appDir, "usr", "share", "licenses", "windowswitcher");
        var appDirMetainfo = Path.Combine(appDir, "usr", "share", "metainfo");

        Directory.CreateDirectory(appDirBin);
        Directory.CreateDirectory(appDirApplications);
        Directory.CreateDirectory(appDirIcons);
        Directory.CreateDirectory(appDirLicenses);
        Directory.CreateDirectory(appDirMetainfo);

        CopyPublishOutputToAppDir(EffectiveLinuxPublishDir, appDirBin);
        AssertBundledDotNetHost(appDirBin);
        File.Copy(Path.Combine(RootDirectory, "LICENSE"), Path.Combine(appDirLicenses, "LICENSE"), overwrite: true);

        var iconSource = Path.Combine(RootDirectory, "src", "WindowSwitcher", "Assets", "WS_logo.png");
        var appDirIcon = Path.Combine(appDir, "windowswitcher.png");
        File.Copy(iconSource, appDirIcon, overwrite: true);
        File.Copy(appDirIcon, Path.Combine(appDir, ".DirIcon"), overwrite: true);
        File.Copy(appDirIcon, Path.Combine(appDirIcons, "windowswitcher.png"), overwrite: true);

        var desktopSource = Path.Combine(LinuxPackagingDirectory, "windowswitcher.desktop");
        File.Copy(desktopSource, Path.Combine(appDir, "windowswitcher.desktop"), overwrite: true);
        File.Copy(desktopSource, Path.Combine(appDirApplications, "windowswitcher.desktop"), overwrite: true);
        File.Copy(
            Path.Combine(LinuxPackagingDirectory, "io.github.SebastienDuruz.WindowSwitcher.metainfo.xml"),
            Path.Combine(appDirMetainfo, "io.github.SebastienDuruz.WindowSwitcher.metainfo.xml"),
            overwrite: true);
        File.Copy(Path.Combine(LinuxPackagingDirectory, "AppRun"), Path.Combine(appDir, "AppRun"), overwrite: true);

        MakeExecutable(Path.Combine(appDir, "AppRun"));

        var appImageTool = await ResolveAppImageToolAsync();
        var outputFileName = $"WindowSwitcher-{EffectiveVersion}-{ToAppImageArchitecture(options.LinuxRuntime)}.AppImage";
        var outputFile = Path.Combine(EffectiveAppImageOutDir, outputFileName);
        var zsyncFile = Path.Combine(EffectiveAppImageOutDir, $"{outputFileName}.zsync");
        var generatedZsyncFile = Path.Combine(RootDirectory, $"{outputFileName}.zsync");
        var toolArguments = appImageTool.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)
            ? $"--appimage-extract-and-run -u \"{AppImageUpdateInformation}\" \"{appDir}\" \"{outputFile}\""
            : $"-u \"{AppImageUpdateInformation}\" \"{appDir}\" \"{outputFile}\"";

        RunProcess(
            appImageTool,
            toolArguments,
            new Dictionary<string, string>
            {
                ["ARCH"] = ToAppImageArchitecture(options.LinuxRuntime),
                ["VERSION"] = EffectiveVersion
            });

        Ensure(
            File.Exists(generatedZsyncFile),
            $"AppImage zsync file was not generated at: {generatedZsyncFile}");
        File.Move(generatedZsyncFile, zsyncFile, overwrite: true);

        MakeExecutable(outputFile);
        Ensure(File.Exists(outputFile), $"AppImage was not created at: {outputFile}");
        Ensure(File.Exists(zsyncFile), $"AppImage zsync file was not created at: {zsyncFile}");
        AssertElfX64(outputFile);
    }

    /// <summary>
    /// Resolves makensis from an explicit path or from PATH.
    /// </summary>
    string ResolveMakensis()
    {
        if (!string.IsNullOrWhiteSpace(options.MakensisPath))
        {
            Ensure(File.Exists(options.MakensisPath), $"makensis not found: {options.MakensisPath}");
            return options.MakensisPath;
        }

        var discovered = FindExecutableOnPath("makensis");
        if (!string.IsNullOrWhiteSpace(discovered))
            return discovered;

        var nuGetTool = FindNuGetPackageTool("NSIS", "makensis.exe", AppProjectPath);
        Ensure(
            !string.IsNullOrWhiteSpace(nuGetTool),
            "Missing dependency: 'makensis'. Restore the NSIS NuGet package, install NSIS, or pass --makensis-path.");
        return nuGetTool;
    }

    /// <summary>
    /// Resolves appimagetool from an explicit path or the modern release stream.
    /// </summary>
    async Task<string> ResolveAppImageToolAsync()
    {
        if (!string.IsNullOrWhiteSpace(options.AppImageToolPath))
        {
            Ensure(File.Exists(options.AppImageToolPath), $"appimagetool not found: {options.AppImageToolPath}");
            return options.AppImageToolPath;
        }

        Directory.CreateDirectory(ToolsDirectory);

        var arch = ToAppImageArchitecture(options.LinuxRuntime);
        var outputPath = Path.Combine(ToolsDirectory, $"appimagetool-modern-{arch}.AppImage");
        if (File.Exists(outputPath))
            return outputPath;

        var url = GetAppImageToolDownloadUrl(arch);

        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        await using (var outputStream = File.OpenWrite(outputPath))
        {
            await response.Content.CopyToAsync(outputStream);
        }

        MakeExecutable(outputPath);
        return outputPath;
    }

    /// <summary>
    /// Publishes the app project for the requested runtime.
    /// </summary>
    void PublishForRuntime(string runtime, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var selfContainedValue = options.SelfContained ? "true" : "false";
        RunDotNet(
            $"publish \"{AppProjectPath}\" -c {options.Configuration} -r {runtime} " +
            $"-o \"{outputDirectory}\" --self-contained {selfContainedValue} " +
            $"-p:UsedAvaloniaProducts= {TelemetryBuildProperties} " +
            $"-p:Version={EffectiveVersion} -p:PackageVersion={EffectiveVersion} -p:InformationalVersion={EffectiveVersion}");
    }

    /// <summary>
    /// Runs a dotnet command from the repository root without the shared build servers.
    /// </summary>
    void RunDotNet(string arguments) => RunProcess("dotnet", $"{arguments} --disable-build-servers");

    /// <summary>
    /// Runs a process from the repository root and fails the build on a non-zero exit code.
    /// Output is streamed to the current console; extra environment variables are added
    /// on top of the current environment.
    /// </summary>
    void RunProcess(string fileName, string arguments, IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        var processStartInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = RootDirectory,
            UseShellExecute = false
        };

        if (environmentVariables is not null)
        {
            foreach (var (name, value) in environmentVariables)
                processStartInfo.Environment[name] = value;
        }

        using var process = Process.Start(processStartInfo);
        Ensure(process is not null, $"Unable to start process: {fileName} {arguments}");

        process.WaitForExit();
        Ensure(
            process.ExitCode == 0,
            $"Command failed with exit code {process.ExitCode}: {fileName} {arguments}");
    }

    /// <summary>
    /// Fails the build with the given message when the condition is false.
    /// </summary>
    static void Ensure([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
            throw new BuildFailedException(message);
    }

    /// <summary>
    /// Finds the repository root by walking up from the current directory.
    /// </summary>
    static string FindRootDirectory()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Window-Switcher.slnx")))
                return directory.FullName;
        }

        throw new BuildFailedException("Repository root not found. Run from the repository or pass --root <path>.");
    }

    /// <summary>
    /// Adapts a synchronous target action to the asynchronous target signature.
    /// </summary>
    static Func<Task> Synchronous(Action action) => () =>
    {
        action();
        return Task.CompletedTask;
    };

    /// <summary>
    /// Normalizes build labels before they become MSBuild property values.
    /// </summary>
    static string NormalizeTelemetryBuildLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        return value.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    /// <summary>
    /// Asserts that the publish directory exists and contains files.
    /// </summary>
    static void AssertPublishOutputNotEmpty(string publishDirectory)
    {
        Ensure(Directory.Exists(publishDirectory), $"Publish directory not found: {publishDirectory}");
        Ensure(Directory.EnumerateFileSystemEntries(publishDirectory).Any(), $"dotnet publish produced no files in: {publishDirectory}");
    }

    /// <summary>
    /// Asserts that the .NET host resolver is bundled, proving the output is self-contained.
    /// Without it, the app cannot start on machines lacking a system-wide .NET runtime.
    /// </summary>
    static void AssertBundledDotNetHost(string binaryDirectory)
    {
        var hostResolver = Path.Combine(binaryDirectory, "libhostfxr.so");
        Ensure(
            File.Exists(hostResolver),
            $"Bundled .NET host not found at: {hostResolver}. The AppImage must be published self-contained; do not pass --self-contained false.");
    }

    /// <summary>
    /// Ensures the directory is deleted and recreated.
    /// </summary>
    static void RecreateDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// Maps Windows RID to installer architecture string.
    /// </summary>
    static string ToWindowsInstallerArchitecture(string runtime)
        => runtime switch
        {
            "win-x64" => "x86_64",
            "win-arm64" => "arm64",
            _ => throw new BuildFailedException($"Unsupported Windows runtime: {runtime}")
        };

    /// <summary>
    /// Maps Linux RID to AppImage architecture string.
    /// </summary>
    static string ToAppImageArchitecture(string runtime)
        => runtime switch
        {
            "linux-x64" => "x86_64",
            _ => throw new BuildFailedException($"Unsupported Linux runtime: {runtime}")
        };

    /// <summary>
    /// Returns appimagetool download URL for an architecture.
    /// Respects APPIMAGETOOL_URL override when provided.
    /// </summary>
    static string GetAppImageToolDownloadUrl(string architecture)
    {
        var overrideUrl = Environment.GetEnvironmentVariable("APPIMAGETOOL_URL");
        if (!string.IsNullOrWhiteSpace(overrideUrl))
            return overrideUrl;

        return architecture switch
        {
            "x86_64" => "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage",
            _ => throw new BuildFailedException($"Unsupported AppImage architecture: {architecture}")
        };
    }

    /// <summary>
    /// Verifies that a file is a little-endian 64-bit ELF for the AMD64 architecture.
    /// </summary>
    static void AssertElfX64(string filePath)
    {
        const int ElfHeaderSize = 20;
        const ushort ElfMachineX64 = 62;

        Ensure(File.Exists(filePath), $"ELF file not found: {filePath}");

        Span<byte> header = stackalloc byte[ElfHeaderSize];
        using var stream = File.OpenRead(filePath);
        var bytesRead = stream.Read(header);

        Ensure(bytesRead == ElfHeaderSize, $"Invalid ELF header in: {filePath}");
        Ensure(
            header[0] == 0x7f && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F',
            $"File is not an ELF executable: {filePath}");
        Ensure(header[4] == 2, $"ELF file is not 64-bit: {filePath}");
        Ensure(header[5] == 1, $"ELF file is not little-endian: {filePath}");

        var machine = (ushort)(header[18] | (header[19] << 8));
        Ensure(machine == ElfMachineX64, $"ELF file is not x86-64: {filePath}");
    }

    /// <summary>
    /// Reads WindowSwitcherVersion from Directory.Build.props.
    /// </summary>
    static string ReadVersionFromProps(string propsPath)
    {
        Ensure(File.Exists(propsPath), $"Version file not found: {propsPath}");

        var xmlDocument = XDocument.Load(propsPath);
        var version = xmlDocument.Descendants("WindowSwitcherVersion").FirstOrDefault()?.Value?.Trim();

        Ensure(!string.IsNullOrWhiteSpace(version), $"Unable to read WindowSwitcherVersion from: {propsPath}");
        return version;
    }

    /// <summary>
    /// Recursively copies publish output into an AppDir, excluding NativeAOT debug symbols.
    /// </summary>
    static void CopyPublishOutputToAppDir(string sourceDirectory, string destinationDirectory)
    {
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);

            if (Directory.Exists(sourcePath))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            if (Path.GetExtension(sourcePath).Equals(".dbg", StringComparison.OrdinalIgnoreCase))
                continue;

            var destinationParent = Path.GetDirectoryName(destinationPath) ?? destinationDirectory;
            Directory.CreateDirectory(destinationParent);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    /// <summary>
    /// Marks a file executable for user, group and others on Unix-like hosts.
    /// </summary>
    static void MakeExecutable(string filePath)
    {
        if (OperatingSystem.IsWindows())
            return;

        const UnixFileMode ExecuteBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(filePath, File.GetUnixFileMode(filePath) | ExecuteBits);
    }

    /// <summary>
    /// Finds an executable in PATH.
    /// </summary>
    static string? FindExecutableOnPath(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return null;

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
                return candidate;

            if (OperatingSystem.IsWindows())
            {
                var windowsCandidate = candidate + ".exe";
                if (File.Exists(windowsCandidate))
                    return windowsCandidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds an executable from a restored NuGet package in the global package cache.
    /// </summary>
    static string? FindNuGetPackageTool(string packageId, string executableName, string projectPath)
    {
        var packageVersion = ReadPackageReferenceVersion(projectPath, packageId);
        foreach (var packageRoot in GetNuGetPackageRoots())
        {
            var packageDirectory = Path.Combine(packageRoot, packageId.ToLowerInvariant());
            if (!Directory.Exists(packageDirectory))
                continue;

            if (!string.IsNullOrWhiteSpace(packageVersion))
            {
                var exactCandidate = Path.Combine(packageDirectory, packageVersion, "tools", executableName);
                if (File.Exists(exactCandidate))
                    return exactCandidate;
            }

            var latestCandidate = Directory
                .EnumerateDirectories(packageDirectory)
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(versionDirectory => Path.Combine(versionDirectory, "tools", executableName))
                .FirstOrDefault(File.Exists);

            if (!string.IsNullOrWhiteSpace(latestCandidate))
                return latestCandidate;
        }

        return null;
    }

    /// <summary>
    /// Reads a package reference version from a project file.
    /// </summary>
    static string? ReadPackageReferenceVersion(string projectPath, string packageId)
    {
        if (!File.Exists(projectPath))
            return null;

        var xmlDocument = XDocument.Load(projectPath);
        return xmlDocument
            .Descendants("PackageReference")
            .FirstOrDefault(reference =>
                string.Equals(
                    reference.Attribute("Include")?.Value,
                    packageId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            ?.Attribute("Version")
            ?.Value
            ?.Trim();
    }

    /// <summary>
    /// Returns NuGet global package cache roots, including common environment overrides.
    /// </summary>
    static IEnumerable<string> GetNuGetPackageRoots()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
            yield return configuredRoot;

        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(userProfile))
            yield return Path.Combine(userProfile, ".nuget", "packages");

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
            yield return Path.Combine(home, ".nuget", "packages");
    }

    /// <summary>
    /// Validates supported Windows runtime identifiers.
    /// </summary>
    static void EnsureWindowsRuntimeSupported(string runtime)
    {
        Ensure(runtime is "win-x64" or "win-arm64", $"Unsupported Windows runtime: {runtime}");
    }

    /// <summary>
    /// Validates supported Linux runtime identifiers.
    /// </summary>
    static void EnsureLinuxRuntimeSupported(string runtime)
    {
        Ensure(runtime == "linux-x64", $"Unsupported Linux runtime: {runtime}. Window Switcher supports linux-x64 only.");
    }
}
