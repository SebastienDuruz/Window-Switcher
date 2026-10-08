/// <summary>
/// Command-line options of the packaging build.
/// Options use kebab-case (<c>--enable-sentry-telemetry false</c>); a boolean option given
/// without a value means <c>true</c>.
/// </summary>
sealed class BuildOptions
{
    /// <summary>Option list printed by <c>--help</c>.</summary>
    public const string HelpText = """
        Options:
          --target <name>                     Target to run with its dependencies (default: Artifacts)
          --root <path>                       Repository root (default: searched from the current directory)
          --configuration <name>              Build configuration (default: Release)
          --version <x.y.z>                   Version override (default: WindowSwitcherVersion in Directory.Build.props)
          --self-contained [true|false]       Embed the .NET runtime (default: true, required by AppImage)
          --enable-sentry-telemetry [true|false]
                                              Compile Sentry telemetry into the application (default: true)
          --distribution-channel <name>       Distribution channel telemetry label (default: source)
          --package-kind <name>               Package kind telemetry label (default: unpackaged)
          --windows-runtime <rid>             win-x64 or win-arm64 (default: win-x64)
          --linux-runtime <rid>               linux-x64 (default: linux-x64)
          --windows-publish-dir <path>        Windows publish output directory
          --linux-publish-dir <path>          Linux publish output directory
          --installer-out-dir <path>          Installer output directory
          --app-image-out-dir <path>          AppImage output directory
          --makensis-path <path>              Explicit makensis executable
          --app-image-tool-path <path>        Explicit appimagetool executable
          -h, --help                          Show this help
        """;

    /// <summary>Whether only the help text is requested.</summary>
    public bool ShowHelp { get; init; }

    /// <summary>Target to run with its dependencies.</summary>
    public required string Target { get; init; }

    /// <summary>Explicit repository root, or <c>null</c> to search from the current directory.</summary>
    public string? RootDirectory { get; init; }

    /// <summary>Build configuration used by compile/publish steps.</summary>
    public required string Configuration { get; init; }

    /// <summary>Optional version override. Defaults to Directory.Build.props.</summary>
    public string? Version { get; init; }

    /// <summary>
    /// Whether published binaries embed the .NET runtime. Enabled by default so packaged
    /// builds start on machines without .NET installed; the AppImage target requires it.
    /// </summary>
    public bool SelfContained { get; init; }

    /// <summary>Whether Sentry telemetry is compiled into the application.</summary>
    public bool EnableSentryTelemetry { get; init; }

    /// <summary>Distribution channel label included in telemetry metadata.</summary>
    public required string DistributionChannel { get; init; }

    /// <summary>Package kind label included in telemetry metadata.</summary>
    public required string PackageKind { get; init; }

    /// <summary>Windows RID used for installer publish output.</summary>
    public required string WindowsRuntime { get; init; }

    /// <summary>Linux RID used for AppImage publish output.</summary>
    public required string LinuxRuntime { get; init; }

    /// <summary>Optional override for Windows publish directory.</summary>
    public string? WindowsPublishDir { get; init; }

    /// <summary>Optional override for Linux publish directory.</summary>
    public string? LinuxPublishDir { get; init; }

    /// <summary>Optional override for installer output directory.</summary>
    public string? InstallerOutDir { get; init; }

    /// <summary>Optional override for AppImage output directory.</summary>
    public string? AppImageOutDir { get; init; }

    /// <summary>Optional explicit path to makensis.</summary>
    public string? MakensisPath { get; init; }

    /// <summary>Optional explicit path to appimagetool.</summary>
    public string? AppImageToolPath { get; init; }

    /// <summary>
    /// Parses command-line arguments and applies the defaults of every missing option.
    /// </summary>
    /// <exception cref="BuildFailedException">An argument is malformed or unknown.</exception>
    public static BuildOptions Parse(IReadOnlyList<string> arguments)
    {
        var values = ReadValues(arguments);
        string? Take(string name) => values.Remove(name, out var value) ? value : null;
        bool? TakeBool(string name) => ParseBool(name, Take(name));
        string? TakePath(string name) => Take(name) is { } path ? Path.GetFullPath(path) : null;

        var options = new BuildOptions
        {
            ShowHelp = TakeBool("help") ?? false,
            Target = Take("target") ?? "Artifacts",
            RootDirectory = TakePath("root"),
            Configuration = Take("configuration") ?? "Release",
            Version = Take("version"),
            SelfContained = TakeBool("self-contained") ?? true,
            EnableSentryTelemetry = TakeBool("enable-sentry-telemetry") ?? true,
            DistributionChannel = Take("distribution-channel") ?? "source",
            PackageKind = Take("package-kind") ?? "unpackaged",
            WindowsRuntime = Take("windows-runtime") ?? "win-x64",
            LinuxRuntime = Take("linux-runtime") ?? "linux-x64",
            WindowsPublishDir = TakePath("windows-publish-dir"),
            LinuxPublishDir = TakePath("linux-publish-dir"),
            InstallerOutDir = TakePath("installer-out-dir"),
            AppImageOutDir = TakePath("app-image-out-dir"),
            MakensisPath = TakePath("makensis-path"),
            AppImageToolPath = TakePath("app-image-tool-path"),
        };

        if (values.Count > 0)
        {
            var unknownOptions = string.Join(", ", values.Keys.Select(name => $"--{name}"));
            throw new BuildFailedException(
                $"Unknown option(s): {unknownOptions}. Run with --help to list options."
            );
        }

        return options;
    }

    /// <summary>
    /// Reads <c>--name value</c> pairs. An option followed by another option or by nothing gets <c>true</c>.
    /// </summary>
    static Dictionary<string, string> ReadValues(IReadOnlyList<string> arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            var name = argument switch
            {
                "-h" => "help",
                _ when argument.StartsWith("--", StringComparison.Ordinal) && argument.Length > 2 =>
                    argument[2..],
                _ => throw new BuildFailedException(
                    $"Unexpected argument: {argument}. Options must start with '--'."
                ),
            };

            var hasValue = index + 1 < arguments.Count && !arguments[index + 1].StartsWith('-');
            values[name] = hasValue ? arguments[++index] : "true";
        }

        return values;
    }

    /// <summary>
    /// Parses an optional boolean option value.
    /// </summary>
    static bool? ParseBool(string name, string? value)
    {
        if (value is null)
            return null;

        if (bool.TryParse(value, out var result))
            return result;

        throw new BuildFailedException($"Option --{name} expects true or false, got: {value}");
    }
}
