namespace Tuinet.Samples.Showcase;

/// <summary>One row of the list. Edited in memory only: nothing is written to disk.</summary>
public sealed class Item
{
    public static readonly string[] Kinds = ["feature", "bugfix", "chore", "docs", "spike"];
    public static readonly string[] Priorities = ["low", "medium", "high", "critical"];

    public required string Name { get; set; }
    public required string Owner { get; set; }
    public string Description { get; set; } = "";
    public int Kind { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Notify { get; set; }

    public static Item[] Samples()
    {
        (string Name, string Owner, string Description)[] rows =
        [
            ("parse & validate intent", "ada", "typed intent, schema checks and invariant gates before anything runs"),
            ("prepare workspace", "linus", "clean git worktree and environment, prepared deterministically"),
            ("run agent loop", "grace", "state machine drives the loop; runtimes are swappable"),
            ("verify & return diff", "ken", "graders read the diff and exit codes, never the prose"),
            ("schema registry", "barbara", "typed actions, tool schemas and invariant rules"),
            ("ephemeral workspace", "dennis", "microVM sandbox per task, destroyed afterwards"),
            ("model gateway", "margaret", "token budget, cost tracking and fallback routing"),
            ("token budget", "edsger", "hard ceilings per task with early warnings"),
            ("cost tracking", "frances", "per-call cost attribution rolled up per workflow"),
            ("fallback routing", "john", "retry on a secondary backend when the primary degrades"),
            ("eval fixtures", "radia", "repo fixtures plus task specs for every eval case"),
            ("diff assertions", "donald", "required and banned lines, max files touched"),
            ("no-op consistency", "hedy", "claims must match the physical repo state"),
            ("compiler & test suite", "alan", "AST syntax pass and test runner exit codes"),
            ("atomic commit report", "katherine", "verified merge with zero state contamination"),
            ("rollback log", "leslie", "failed cases captured in a write-ahead log"),
            ("session log", "tim", "timestamped trail of every tool call"),
            ("advisor hooks", "sophie", "second opinion when an error repeats"),
            ("fork layer", "bjarne", "cheap decisions run in code, the rest go to the model"),
            ("telemetry export", "anders", "metrics and traces shipped to the collector"),
        ];

        var items = new Item[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            items[i] = new Item
            {
                Name = rows[i].Name,
                Owner = rows[i].Owner,
                Description = rows[i].Description,
                Kind = i % Kinds.Length,
                Priority = (i * 7) % Priorities.Length,
                Enabled = i % 6 != 5,
                Notify = i % 3 == 0,
            };
        }

        return items;
    }
}
