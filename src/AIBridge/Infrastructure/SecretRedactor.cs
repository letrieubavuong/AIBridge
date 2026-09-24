using System.Text.RegularExpressions;

namespace AIBridge.Infrastructure;

public static class SecretRedactor
{
    private static readonly (Regex Pattern, string Replacement)[] RedactionRules = new[]
    {
        // 1. Authorization Bearer header/tokens
        (new Regex(@"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Bearer [REDACTED]"),
        
        // 2. GitHub Personal Access Tokens & OAuth Tokens
        (new Regex(@"(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{36,}", RegexOptions.Compiled), "[REDACTED_GITHUB_TOKEN]"),
        (new Regex(@"github_pat_[A-Za-z0-9_]{82,}", RegexOptions.Compiled), "[REDACTED_GITHUB_TOKEN]"),
        
        // 3. Common AI / Cloud API Keys
        (new Regex(@"sk-[A-Za-z0-9_-]{20,}", RegexOptions.Compiled), "[REDACTED_API_KEY]"),
        (new Regex(@"AIzaSy[A-Za-z0-9_-]{33}", RegexOptions.Compiled), "[REDACTED_API_KEY]"),
        
        // 4. Credentials embedded in URLs (https://user:password@host)
        (new Regex(@"https?://([^:\s]+):([^@\s]+)@", RegexOptions.IgnoreCase | RegexOptions.Compiled), "https://$1:[REDACTED]@"),
        
        // 5. Explicit assignments like api_key = "..." or password: "..."
        (new Regex(@"(?i)(api_key|access_token|secret_key|password|client_secret)\s*[:=]\s*[""']?([A-Za-z0-9\-\._~\+\/]{16,})[""']?", RegexOptions.Compiled), "$1 = [REDACTED]")
    };

    public static (string RedactedText, bool ContainsRedactions) Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return (input, false);
        }

        string result = input;
        bool modified = false;

        foreach (var (pattern, replacement) in RedactionRules)
        {
            if (pattern.IsMatch(result))
            {
                result = pattern.Replace(result, replacement);
                modified = true;
            }
        }

        return (result, modified);
    }

    public static string RedactUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var redacted = Regex.Replace(url, @"https?://([^:\s]+):([^@\s]+)@", "https://$1:[REDACTED]@");
        return redacted;
    }
}
