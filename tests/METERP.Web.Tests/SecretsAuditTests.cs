using System.Text.RegularExpressions;
using Xunit;

namespace METERP.Web.Tests;

/// <summary>
/// Lightweight static checks that secrets stay out of source control and obvious hardcoded keys.
/// </summary>
public class SecretsAuditTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void GitIgnore_Excludes_EnvFile()
    {
        var gitignorePath = Path.Combine(RepoRoot, ".gitignore");
        Assert.True(File.Exists(gitignorePath));

        var gitignore = File.ReadAllText(gitignorePath);
        Assert.Contains(".env", gitignore, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvExample_Exists_ForDockerCompose()
    {
        var envExample = Path.Combine(RepoRoot, ".env.example");
        Assert.True(File.Exists(envExample));

        var content = File.ReadAllText(envExample);
        Assert.Contains("ConnectionStrings", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceCode_HasNoHardcodedStripeOrOpenAiKeys()
    {
        var srcRoot = Path.Combine(RepoRoot, "src");
        var forbidden = new Regex(@"(sk_(live|test)_[A-Za-z0-9]{20,}|sk-proj-[A-Za-z0-9]{20,}|xai-[A-Za-z0-9]{20,})", RegexOptions.Compiled);

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            var ext = Path.GetExtension(file);
            if (ext is not (".cs" or ".razor" or ".json"))
                continue;

            var text = File.ReadAllText(file);
            if (forbidden.IsMatch(text))
                violations.Add(Path.GetRelativePath(RepoRoot, file));
        }

        Assert.True(violations.Count == 0, "Possible hardcoded API key(s): " + string.Join(", ", violations));
    }

    [Fact]
    public void WebProject_HasUserSecretsId()
    {
        var csproj = Path.Combine(RepoRoot, "src", "METERP.Web", "METERP.Web.csproj");
        var content = File.ReadAllText(csproj);
        Assert.Contains("<UserSecretsId>", content, StringComparison.Ordinal);
    }
}