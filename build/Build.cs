using System.Diagnostics;
using System.Net.Http;
using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using static Nuke.Common.Assert;

/// <summary>
/// Nuke build entrypoint for Window Switcher packaging.
/// Produces a Windows NSIS installer on Windows and an AppImage on Linux.
/// </summary>
sealed class Build : NukeBuild
{
    /// <summary>
    /// Executes the default target graph.
    /// </summary>
    public static int Main() => Execute<Build>(x => x.Artifacts);

    /// <summary>Build configuration used by compile/publish steps.</summary>
    [Parameter] readonly string Configuration = "Release";

    /// <summary>Optional version override. Defaults to Directory.Build.props.</summary>
    [Parameter] readonly string? Version;

    /// <summary>Whether published binaries should be self-contained.</summary>
    [Parameter] readonly bool SelfContained;

    /// <summary>Whether Sentry telemetry is compiled into the application.</summary>
    [Parameter] readonly bool EnableSentryTelemetry = true;

    /// <summary>Distribution channel label included in telemetry metadata.</summary>
    [Parameter] readonly string DistributionChannel = "source";

    /// <summary>Package kind label included in telemetry metadata.</summary>
    [Parameter] readonly string PackageKind = "unpackaged";

    /// <summary>Windows RID used for installer publish output.</summary>
    [Parameter] readonly string WindowsRuntime = "win-x64";

    /// <summary>Linux RID used for AppImage publish output.</summary>
    [Parameter] readonly string LinuxRuntime = "linux-x64";

    /// <summary>Optional override for Windows publish directory.</summary>
    [Parameter] readonly AbsolutePath? WindowsPublishDir;

    /// <summary>Optional override for Linux publish directory.</summary>
    [Parameter] readonly AbsolutePath? LinuxPublishDir;

    /// <summary>Optional override for installer output directory.</summary>
    [Parameter] readonly AbsolutePath? InstallerOutDir;

    /// <summary>Optional override for AppImage output directory.</summary>
    [Parameter] readonly AbsolutePath? AppImageOutDir;

    /// <summary>Optional explicit path to makensis.</summary>
    [Parameter] readonly string? MakensisPath;

    /// <summary>Optional explicit path to appimagetool.</summary>
    [Parameter] readonly string? AppImageToolPath;

    AbsolutePath AppProjectPath => RootDirectory / "src/WindowSwitcher/WindowSwitcher.csproj";
    AbsolutePath VersionPropsPath => RootDirectory / "Directory.Build.props";

    AbsolutePath AssetsDirectory => RootDirectory / "build/assets";
    AbsolutePath InstallerAssetsDirectory => AssetsDirectory / "installer";
    AbsolutePath LinuxPackagingDirectory => AssetsDirectory / "packaging/linux";

    AbsolutePath ArtifactsDirectory => RootDirectory / "build/artifacts";
    AbsolutePath ToolsDirectory => ArtifactsDirectory / "tools";

    AbsolutePath InstallerNsiPath => InstallerAssetsDirectory / "WindowSwitcher.nsi";

    string EffectiveVersion => Version ?? ReadVersionFromProps(VersionPropsPath);
    string EnableSentryTelemetryProperty => EnableSentryTelemetry ? "true" : "false";
    string TelemetryBuildProperties =>
        $"-p:EnableSentryTelemetry={EnableSentryTelemetryProperty} " +
        $"-p:TelemetryDistributionChannel={NormalizeTelemetryBuildLabel(DistributionChannel)} " +
        $"-p:TelemetryPackageKind={NormalizeTelemetryBuildLabel(PackageKind)}";

    AbsolutePath EffectiveWindowsPublishDir => WindowsPublishDir ?? ArtifactsDirectory / "publish" / WindowsRuntime;
    AbsolutePath EffectiveLinuxPublishDir => LinuxPublishDir ?? ArtifactsDirectory / "publish" / LinuxRuntime;
    AbsolutePath EffectiveInstallerOutDir => InstallerOutDir ?? ArtifactsDirectory / "installer";
    AbsolutePath EffectiveAppImageOutDir => AppImageOutDir ?? ArtifactsDirectory / "appimage";

    /// <summary>
    /// Validates supported runtime identifiers.
    /// </summary>
    Target ValidateParameters => _ => _
        .Executes(() =>
        {
            EnsureWindowsRuntimeSupported(WindowsRuntime);
            EnsureLinuxRuntimeSupported(LinuxRuntime);
        });

    /// <summary>
    /// Restores project dependencies.
    /// </summary>
    Target Restore => _ => _
        .DependsOn(ValidateParameters)
        .Executes(() =>
        {
            RunDotNet($"restore \"{AppProjectPath}\" {TelemetryBuildProperties}");
        });

    /// <summary>
    /// Builds the application project.
    /// </summary>
    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            RunDotNet($"build \"{AppProjectPath}\" -c {Configuration} {TelemetryBuildProperties}");
        });

    /// <summary>
    /// Publishes the application for the configured Windows runtime.
    /// </summary>
    Target PublishWindows => _ => _
        .DependsOn(Compile)
        .OnlyWhenDynamic(() => OperatingSystem.IsWindows())
        .Executes(() =>
        {
            PublishForRuntime(WindowsRuntime, EffectiveWindowsPublishDir);
            AssertPublishOutputNotEmpty(EffectiveWindowsPublishDir);
        });

    /// <summary>
    /// Builds the Windows NSIS installer from Windows publish output.
    /// </summary>
    Target Installer => _ => _
        .DependsOn(PublishWindows)
        .OnlyWhenDynamic(() => OperatingSystem.IsWindows())
        .Executes(() =>
        {
            True(File.Exists(InstallerNsiPath), $"NSIS script not found: {InstallerNsiPath}");

            Directory.CreateDirectory(EffectiveInstallerOutDir);

            var installerFile = EffectiveInstallerOutDir / $"WindowSwitcher-setup-{EffectiveVersion}-{WindowsRuntime}.exe";
            var publishGlob = EffectiveWindowsPublishDir / "*";

            var makensisArguments =
                $"-DAPP_VERSION={EffectiveVersion} " +
                $"-DPUBLISH_DIR=\"{EffectiveWindowsPublishDir}\" " +
                $"-DPUBLISH_GLOB=\"{publishGlob}\" " +
                $"-DOUT_FILE=\"{installerFile}\" " +
                $"\"{InstallerNsiPath}\"";

            ProcessTasks.StartProcess(ResolveMakensis(), makensisArguments).AssertZeroExitCode();
            True(File.Exists(installerFile), $"Installer was not created at: {installerFile}");
        });

    /// <summary>
    /// Publishes the application for the configured Linux runtime.
    /// </summary>
    Target PublishLinux => _ => _
        .DependsOn(Compile)
        .OnlyWhenDynamic(() => OperatingSystem.IsLinux())
        .Executes(() =>
        {
            PublishForRuntime(LinuxRuntime, EffectiveLinuxPublishDir);

            var publishedExecutable = EffectiveLinuxPublishDir / "WindowSwitcher";
            True(File.Exists(publishedExecutable), $"Published binary not found at: {publishedExecutable}");
        });

    /// <summary>
    /// Packages a Linux AppImage from Linux publish output.
    /// </summary>
    Target AppImage => _ => _
        .DependsOn(PublishLinux)
        .OnlyWhenDynamic(() => OperatingSystem.IsLinux())
        .Executes(async () =>
        {
            Directory.CreateDirectory(EffectiveAppImageOutDir);

            var appDir = EffectiveAppImageOutDir / "WindowSwitcher.AppDir";
            RecreateDirectory(appDir);

            var appDirBin = appDir / "usr/bin";
            var appDirApplications = appDir / "usr/share/applications";
            var appDirIcons = appDir / "usr/share/icons/hicolor/256x256/apps";
            var appDirLicenses = appDir / "usr/share/licenses/windowswitcher";

            Directory.CreateDirectory(appDirBin);
            Directory.CreateDirectory(appDirApplications);
            Directory.CreateDirectory(appDirIcons);
            Directory.CreateDirectory(appDirLicenses);

            CopyDirectoryContents(EffectiveLinuxPublishDir, appDirBin);
            File.Copy(RootDirectory / "LICENSE", appDirLicenses / "LICENSE", overwrite: true);

            var iconSource = RootDirectory / "src/WindowSwitcher/Assets/WS_logo.png";
            File.Copy(iconSource, appDir / "windowswitcher.png", overwrite: true);
            File.Copy(appDir / "windowswitcher.png", appDir / ".DirIcon", overwrite: true);
            File.Copy(appDir / "windowswitcher.png", appDirIcons / "windowswitcher.png", overwrite: true);

            var desktopSource = LinuxPackagingDirectory / "windowswitcher.desktop";
            File.Copy(desktopSource, appDir / "windowswitcher.desktop", overwrite: true);
            File.Copy(desktopSource, appDirApplications / "windowswitcher.desktop", overwrite: true);
            File.Copy(LinuxPackagingDirectory / "AppRun", appDir / "AppRun", overwrite: true);

            MakeExecutable(appDir / "AppRun");

            var appImageTool = await ResolveAppImageToolAsync();
            var outputFile = EffectiveAppImageOutDir / $"WindowSwitcher-{EffectiveVersion}-{LinuxRuntime}.AppImage";
            var toolArguments = appImageTool.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)
                ? $"--appimage-extract-and-run {appDir} {outputFile}"
                : $"{appDir} {outputFile}";

            ProcessTasks.StartProcess(
                    appImageTool,
                    toolArguments,
                    environmentVariables: new Dictionary<string, string>
                    {
                        ["ARCH"] = ToAppImageArchitecture(LinuxRuntime),
                        ["VERSION"] = EffectiveVersion
                    })
                .AssertZeroExitCode();

            MakeExecutable(outputFile);
            True(File.Exists(outputFile), $"AppImage was not created at: {outputFile}");
        });

    /// <summary>
    /// Produces host-compatible artifact targets for Windows.
    /// </summary>
    Target WindowsArtifacts => _ => _
        .DependsOn(Installer)
        .OnlyWhenDynamic(() => OperatingSystem.IsWindows());

    /// <summary>
    /// Produces host-compatible artifact targets for Linux.
    /// </summary>
    Target LinuxArtifacts => _ => _
        .DependsOn(AppImage)
        .OnlyWhenDynamic(() => OperatingSystem.IsLinux());

    /// <summary>
    /// Default target that builds artifacts for the current host OS.
    /// </summary>
    Target Artifacts => _ => _
        .DependsOn(WindowsArtifacts, LinuxArtifacts);

    /// <summary>
    /// Resolves makensis from an explicit path or from PATH.
    /// </summary>
    string ResolveMakensis()
    {
        if (!string.IsNullOrWhiteSpace(MakensisPath))
        {
            True(File.Exists(MakensisPath), $"makensis not found: {MakensisPath}");
            return MakensisPath;
        }

        var discovered = FindExecutableOnPath("makensis");
        if (!string.IsNullOrWhiteSpace(discovered))
            return discovered;

        var nuGetTool = FindNuGetPackageTool("NSIS", "makensis.exe", AppProjectPath);
        True(
            !string.IsNullOrWhiteSpace(nuGetTool),
            "Missing dependency: 'makensis'. Restore the NSIS NuGet package, install NSIS, or pass --makensis-path.");
        return nuGetTool!;
    }

    /// <summary>
    /// Resolves appimagetool from explicit path, PATH, or auto-download.
    /// </summary>
    async Task<string> ResolveAppImageToolAsync()
    {
        if (!string.IsNullOrWhiteSpace(AppImageToolPath))
        {
            True(File.Exists(AppImageToolPath), $"appimagetool not found: {AppImageToolPath}");
            return AppImageToolPath;
        }

        var discovered = FindExecutableOnPath("appimagetool");
        if (!string.IsNullOrWhiteSpace(discovered))
            return discovered;

        Directory.CreateDirectory(ToolsDirectory);

        var arch = ToAppImageArchitecture(LinuxRuntime);
        var outputPath = ToolsDirectory / $"appimagetool-{arch}.AppImage";
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
    void PublishForRuntime(string runtime, AbsolutePath outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var selfContainedValue = SelfContained ? "true" : "false";
        RunDotNet(
            $"publish \"{AppProjectPath}\" -c {Configuration} -r {runtime} " +
            $"-o \"{outputDirectory}\" --self-contained {selfContainedValue} " +
            $"-p:UsedAvaloniaProducts= {TelemetryBuildProperties} " +
            $"-p:Version={EffectiveVersion} -p:PackageVersion={EffectiveVersion} -p:InformationalVersion={EffectiveVersion}");
    }

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
    static void AssertPublishOutputNotEmpty(AbsolutePath publishDirectory)
    {
        True(Directory.Exists(publishDirectory), $"Publish directory not found: {publishDirectory}");
        True(Directory.EnumerateFileSystemEntries(publishDirectory).Any(), $"dotnet publish produced no files in: {publishDirectory}");
    }

    /// <summary>
    /// Ensures the directory is deleted and recreated.
    /// </summary>
    static void RecreateDirectory(AbsolutePath directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// Maps Linux RID to AppImage architecture string.
    /// </summary>
    static string ToAppImageArchitecture(string runtime)
        => runtime switch
        {
            "linux-x64" => "x86_64",
            "linux-arm64" => "aarch64",
            _ => throw new Exception($"Unsupported Linux runtime: {runtime}")
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
            "x86_64" => "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage",
            "aarch64" => "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-aarch64.AppImage",
            _ => throw new Exception($"Unsupported AppImage architecture: {architecture}")
        };
    }

    /// <summary>
    /// Reads WindowSwitcherVersion from Directory.Build.props.
    /// </summary>
    static string ReadVersionFromProps(AbsolutePath propsPath)
    {
        True(File.Exists(propsPath), $"Version file not found: {propsPath}");

        var xmlDocument = XDocument.Load(propsPath);
        var version = xmlDocument.Descendants("WindowSwitcherVersion").FirstOrDefault()?.Value?.Trim();

        True(!string.IsNullOrWhiteSpace(version), $"Unable to read WindowSwitcherVersion from: {propsPath}");
        return version!;
    }

    /// <summary>
    /// Recursively copies a directory tree into another directory.
    /// </summary>
    static void CopyDirectoryContents(AbsolutePath sourceDirectory, AbsolutePath destinationDirectory)
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

            var destinationParent = Path.GetDirectoryName(destinationPath) ?? destinationDirectory.ToString();
            Directory.CreateDirectory(destinationParent);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    /// <summary>
    /// Marks a file executable on Unix-like hosts.
    /// </summary>
    static void MakeExecutable(AbsolutePath filePath)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        ProcessTasks.StartProcess("chmod", $"+x {filePath}").AssertZeroExitCode();
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
    static string? FindNuGetPackageTool(string packageId, string executableName, AbsolutePath projectPath)
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
    static string? ReadPackageReferenceVersion(AbsolutePath projectPath, string packageId)
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
    /// Runs a dotnet command from the repository root.
    /// Standard output and error are forwarded to the current process.
    /// </summary>
    void RunDotNet(string arguments)
    {
        var fullArguments = arguments.Contains("--disable-build-servers", StringComparison.Ordinal)
            ? arguments
            : $"{arguments} --disable-build-servers";

        var processStartInfo = new ProcessStartInfo("dotnet", fullArguments)
        {
            WorkingDirectory = RootDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(processStartInfo);
        True(process is not null, $"Unable to start dotnet process for arguments: {arguments}");

        var runningProcess = process!;
        var standardOutput = runningProcess.StandardOutput.ReadToEnd();
        var standardError = runningProcess.StandardError.ReadToEnd();
        runningProcess.WaitForExit();

        if (!string.IsNullOrWhiteSpace(standardOutput))
            Console.WriteLine(standardOutput.TrimEnd());

        if (!string.IsNullOrWhiteSpace(standardError))
            Console.Error.WriteLine(standardError.TrimEnd());

        True(
            runningProcess.ExitCode == 0,
            $"dotnet command failed with exit code {runningProcess.ExitCode}: dotnet {fullArguments}");
    }

    /// <summary>
    /// Validates supported Windows runtime identifiers.
    /// </summary>
    static void EnsureWindowsRuntimeSupported(string runtime)
    {
        True(runtime is "win-x64" or "win-arm64", $"Unsupported Windows runtime: {runtime}");
    }

    /// <summary>
    /// Validates supported Linux runtime identifiers.
    /// </summary>
    static void EnsureLinuxRuntimeSupported(string runtime)
    {
        True(runtime is "linux-x64" or "linux-arm64", $"Unsupported Linux runtime: {runtime}");
    }
}
