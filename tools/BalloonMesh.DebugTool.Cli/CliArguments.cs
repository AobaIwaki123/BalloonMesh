namespace BalloonMesh.DebugTool.Cli;

/// <summary>
/// Minimal, dependency-free option parser. Options are kebab-case and either
/// <c>--name value</c> or <c>--name=value</c>. Only the names in
/// <see cref="Flags"/> never consume the following token.
/// </summary>
public sealed class CliArguments
{
    public static readonly IReadOnlySet<string> Flags = new HashSet<string>(StringComparer.Ordinal)
    {
        "help",
        "json",
        "verbose",
        "set-now",
        "no-provisioned"
    };

    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);
    private readonly List<string> _words = [];
    private readonly List<string> _errors = [];

    private CliArguments()
    {
    }

    /// <summary>Positional tokens (the command path) in order.</summary>
    public IReadOnlyList<string> Words => _words;

    public IReadOnlyList<string> Errors => _errors;

    public static CliArguments Parse(IEnumerable<string> arguments)
    {
        var result = new CliArguments();
        var tokens = arguments.ToArray();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token == "--")
            {
                for (var j = i + 1; j < tokens.Length; j++)
                {
                    result._words.Add(tokens[j]);
                }
                break;
            }
            if (token == "-h")
            {
                result._flags.Add("help");
                continue;
            }
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var body = token[2..];
                if (body.Length == 0)
                {
                    result._errors.Add("An option name cannot be empty.");
                    continue;
                }
                var separator = body.IndexOf('=');
                var name = (separator < 0 ? body : body[..separator]).ToLowerInvariant();
                var inline = separator < 0 ? null : body[(separator + 1)..];
                if (name.Length == 0)
                {
                    result._errors.Add("An option name cannot be empty.");
                    continue;
                }
                if (Flags.Contains(name))
                {
                    result._flags.Add(name);
                    if (inline is not null)
                    {
                        result._errors.Add($"Option '--{name}' does not take a value.");
                    }
                    continue;
                }
                if (inline is not null)
                {
                    result.AddValue(name, inline);
                    continue;
                }
                if (i + 1 < tokens.Length && !LooksLikeOption(tokens[i + 1]))
                {
                    result.AddValue(name, tokens[++i]);
                    continue;
                }
                result._errors.Add($"Option '--{name}' requires a value.");
                continue;
            }
            if (token.StartsWith('-'))
            {
                result._errors.Add($"Unknown option '{token}'.");
                continue;
            }
            result._words.Add(token);
        }
        return result;
    }

    public bool Flag(string name) => _flags.Contains(name);

    /// <summary>Last value for an option, or null when it was not supplied.</summary>
    public string? Value(string name) =>
        _values.TryGetValue(name, out var list) ? list[^1] : null;

    /// <summary>All values for a repeatable option.</summary>
    public IReadOnlyList<string> All(string name) =>
        _values.TryGetValue(name, out var list) ? list : [];

    public string RequireValue(string name)
    {
        var value = Value(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliUsageException($"Option '--{name}' is required.");
        }
        return value;
    }

    /// <summary>
    /// Rejects options the command does not understand and throws once if any
    /// parse or validation error was collected.
    /// </summary>
    public void ValidateOptions(params string[] allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var name in _values.Keys.Concat(_flags))
        {
            if (!allowedSet.Contains(name))
            {
                _errors.Add($"Unknown option '--{name}'.");
            }
        }
        if (_errors.Count > 0)
        {
            throw new CliUsageException(
                string.Join(Environment.NewLine, _errors.Distinct(StringComparer.Ordinal)));
        }
    }

    private void AddValue(string name, string value)
    {
        if (!_values.TryGetValue(name, out var list))
        {
            list = [];
            _values[name] = list;
        }
        list.Add(value);
    }

    private static bool LooksLikeOption(string token) =>
        token.Length > 1 && token[0] == '-' && !char.IsDigit(token[1]);
}
