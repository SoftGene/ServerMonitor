// The Web project is referenced under an alias: it and the API both have a Program class from
// their top-level statements, and without this the name would be ambiguous in this assembly.
extern alias WebApp;

using WebApp::ServerMonitor.Web.Presentation;

namespace ServerMonitor.Tests.Web;

/// <summary>
/// The one-line install commands on the "Add a machine" page.
/// </summary>
/// <remarks>
/// These get pasted into a root shell or an elevated PowerShell. A quoting mistake does not fail
/// loudly — it produces a command that looks right and does something else — so the quoting is
/// what is tested hardest.
/// </remarks>
public class InstallCommandsTests
{
    [Fact]
    public void TheApiAddress_IsTheDashboardHostWithTheApiPort()
    {
        var url = InstallCommands.ResolveApiUrl(null, new Uri("http://192.168.1.20:5298/add-machine"));

        // The browser reached this server at 192.168.1.20, so other machines on the network can too.
        Assert.Equal("http://192.168.1.20:7212", url);
    }

    [Fact]
    public void AHostName_IsKeptAsItIs()
    {
        Assert.Equal("http://monitor.lan:7212", InstallCommands.ResolveApiUrl(null, new Uri("http://monitor.lan:5298/")));
    }

    [Fact]
    public void AnIpv6Host_KeepsItsBrackets()
    {
        Assert.Equal("http://[fd00::20]:7212", InstallCommands.ResolveApiUrl(null, new Uri("http://[fd00::20]:5298/")));
    }

    [Fact]
    public void AConfiguredPublicUrl_Wins()
    {
        // For what the guess cannot see, such as a reverse proxy in front of both.
        var url = InstallCommands.ResolveApiUrl(" https://monitor.example.com/api/ ", new Uri("http://10.0.0.5:5298/"));

        Assert.Equal("https://monitor.example.com/api", url);
    }

    [Theory]
    [InlineData("http://localhost:7212", true)]
    [InlineData("http://127.0.0.1:7212", true)]
    [InlineData("http://[::1]:7212", true)]
    [InlineData("http://192.168.1.20:7212", false)]
    [InlineData("http://monitor.lan:7212", false)]
    public void ALoopbackAddress_IsRecognised(string apiUrl, bool expected)
    {
        // A dashboard opened as localhost guesses localhost for the API too, which on any other
        // machine means that machine.
        Assert.Equal(expected, InstallCommands.PointsAtLoopback(apiUrl));
    }

    [Fact]
    public void TheLinuxCommand_PipesTheInstallerIntoSudoBashWithQuotedArguments()
    {
        Assert.Equal(
            "curl -fsSL 'https://raw.githubusercontent.com/SoftGene/ServerMonitor/master/deploy/agent/install.sh' " +
            "| sudo bash -s -- --url 'http://192.168.1.20:7212' --token 'abc123'",
            InstallCommands.Linux("http://192.168.1.20:7212", "abc123"));
    }

    [Fact]
    public void TheWindowsCommand_RunsTheInstallerAsAScriptBlock()
    {
        Assert.Equal(
            "& ([scriptblock]::Create((Invoke-RestMethod " +
            "'https://raw.githubusercontent.com/SoftGene/ServerMonitor/master/deploy/agent/install.ps1'))) " +
            "-Url 'http://192.168.1.20:7212' -Token 'abc123'",
            InstallCommands.Windows("http://192.168.1.20:7212", "abc123"));
    }

    [Fact]
    public void TheUninstallCommands_RunTheSameInstallers()
    {
        Assert.Equal(
            "curl -fsSL 'https://raw.githubusercontent.com/SoftGene/ServerMonitor/master/deploy/agent/install.sh' " +
            "| sudo bash -s -- --uninstall",
            InstallCommands.LinuxUninstall());

        Assert.Equal(
            "& ([scriptblock]::Create((Invoke-RestMethod " +
            "'https://raw.githubusercontent.com/SoftGene/ServerMonitor/master/deploy/agent/install.ps1'))) -Uninstall",
            InstallCommands.WindowsUninstall());
    }

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("$(rm -rf /)", "'$(rm -rf /)'")]
    [InlineData("a\"b`c", "'a\"b`c'")]
    public void BashQuoting_LeavesNothingToExpand(string value, string expected)
    {
        // Inside single quotes bash expands nothing, so $(...) and backticks stay text. The only
        // character that needs care is the quote itself: close, escaped quote, reopen.
        Assert.Equal(expected, InstallCommands.BashQuote(value));
    }

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("it's", "'it''s'")]
    [InlineData("$(Remove-Item C:\\)", "'$(Remove-Item C:\\)'")]
    public void PowerShellQuoting_LeavesNothingToExpand(string value, string expected)
    {
        // Single-quoted strings in PowerShell are literal; a quote inside is written twice.
        Assert.Equal(expected, InstallCommands.PowerShellQuote(value));
    }

    [Fact]
    public void AHostileToken_StaysOneArgument()
    {
        // Whatever the token holds, it reaches the installer as the value of --token and nothing
        // else: no second command, no extra option.
        var command = InstallCommands.Linux("http://10.0.0.5:7212", "x' --uninstall; echo 'y");

        Assert.EndsWith("--token 'x'\\'' --uninstall; echo '\\''y'", command);
    }
}
