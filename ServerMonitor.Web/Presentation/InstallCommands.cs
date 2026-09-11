namespace ServerMonitor.Web.Presentation;

/// <summary>
/// Builds the one-line install commands shown on the "Add a machine" page.
/// </summary>
/// <remarks>
/// The commands are copied from a web page and pasted into a root shell or an elevated PowerShell,
/// so every value in them is quoted for the shell it lands in. Pure functions, because a quoting
/// mistake here does not fail loudly: it produces a command that looks right and does something
/// else.
/// </remarks>
public static class InstallCommands
{
    /// <summary>The API's host port in the compose file.</summary>
    public const int DefaultApiPort = 7212;

    /// <summary>Where the install scripts are downloaded from.</summary>
    public const string ScriptsBaseUrl = "https://raw.githubusercontent.com/SoftGene/ServerMonitor/master/deploy/agent";

    /// <summary>
    /// The address agents on other machines should report to.
    /// </summary>
    /// <remarks>
    /// Taken from the address the dashboard itself was opened with — the browser reached this
    /// server there, so other machines on the same network can too — with the API's port in place
    /// of the dashboard's. A configured public URL wins, for setups this guess cannot see, such as
    /// a reverse proxy in front of both.
    /// </remarks>
    public static string ResolveApiUrl(string? configuredPublicUrl, Uri dashboardUri, int apiPort = DefaultApiPort)
    {
        if (!string.IsNullOrWhiteSpace(configuredPublicUrl))
        {
            return configuredPublicUrl.Trim().TrimEnd('/');
        }

        // Uri.Host keeps an IPv6 address in its brackets, so the result is a valid address as it is.
        return $"{dashboardUri.Scheme}://{dashboardUri.Host}:{apiPort}";
    }

    /// <summary>
    /// True when the commands would send agents to a loopback address.
    /// </summary>
    /// <remarks>
    /// That is what happens when the dashboard is opened as localhost: the guessed API address is
    /// localhost too, which on any other machine means that machine. The page warns rather than
    /// handing out a command that registers nothing.
    /// </remarks>
    public static bool PointsAtLoopback(string apiUrl) =>
        Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri) && uri.IsLoopback;

    /// <summary>For a terminal on Linux: download the installer and run it as root.</summary>
    public static string Linux(string apiUrl, string token) =>
        $"{LinuxInstaller} --url {BashQuote(apiUrl)} --token {BashQuote(token)}";

    public static string LinuxUninstall() => $"{LinuxInstaller} --uninstall";

    /// <summary>For PowerShell opened as Administrator on Windows.</summary>
    /// <remarks>
    /// The script is turned into a script block rather than saved and run as a file: a downloaded
    /// .ps1 file is blocked by the default execution policy, and asking someone to loosen that
    /// policy to install an agent would teach a worse habit than the one being avoided.
    /// </remarks>
    public static string Windows(string apiUrl, string token) =>
        $"{WindowsInstaller} -Url {PowerShellQuote(apiUrl)} -Token {PowerShellQuote(token)}";

    public static string WindowsUninstall() => $"{WindowsInstaller} -Uninstall";

    private static string LinuxInstaller =>
        $"curl -fsSL {BashQuote(ScriptsBaseUrl + "/install.sh")} | sudo bash -s --";

    private static string WindowsInstaller =>
        $"& ([scriptblock]::Create((Invoke-RestMethod {PowerShellQuote(ScriptsBaseUrl + "/install.ps1")})))";

    /// <summary>Single-quotes a value for bash: nothing inside is expanded, and a quote closes, escapes and reopens.</summary>
    public static string BashQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>Single-quotes a value for PowerShell, where a quote inside is written twice.</summary>
    public static string PowerShellQuote(string value) => "'" + value.Replace("'", "''") + "'";
}
