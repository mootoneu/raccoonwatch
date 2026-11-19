namespace Raccoonwatch.Cli;

/// <summary>Parsed command-line options shared by all CLI commands.</summary>
public sealed record CliOptions(string? Id, bool JsonOutput, string? Error)
{
    public static CliOptions Parse(string[] args)
    {
        string? id = null;
        var jsonOutput = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--id" or "-i":
                    if (++i >= args.Length)
                    {
                        return new CliOptions(null, false, "--id requires a value.");
                    }
                    id = args[i];
                    break;
                case "--json-output":
                    jsonOutput = true;
                    break;
                default:
                    return new CliOptions(null, false, $"Unknown option '{args[i]}'.");
            }
        }

        if (id is not null && !Guid.TryParse(id, out _))
        {
            return new CliOptions(null, false, $"'{id}' is not a valid GUID.");
        }

        return new CliOptions(id, jsonOutput, null);
    }
}
