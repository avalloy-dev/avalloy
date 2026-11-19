namespace Avalloy.Preferences;

/// <summary>One directory to watch, and the names in it whose appearance or change matters.</summary>
/// <param name="Path">The directory; it exists.</param>
/// <param name="Names">
/// Child names to react to: a wanted file itself, when its directory exists, the next missing
/// directory on the way down to one, or a watched directory (see <paramref name="Subdirectories"/>).
/// </param>
/// <param name="Subdirectories">
/// The names here that are themselves watched directories. A watcher cannot see its own directory
/// deleted, renamed or replaced, so its parent watches for that; when one of these changes, every
/// watcher is rebuilt rather than left on a directory that is gone.
/// </param>
internal sealed record WatchedDirectory(string Path, IReadOnlySet<string> Names, IReadOnlySet<string> Subdirectories);

/// <summary>
/// What to watch so that a save to any of a set of files is seen, including files that do not
/// exist yet and whose directories do not exist either — as a bounded number of directories.
/// </summary>
/// <remarks>
/// <para>Directories are watched, not files: one watcher per directory, whatever number of wanted
/// names it holds, so a config listing thousands of includes costs one watcher per directory they
/// live in, not one per file. Events are filtered to the names that matter there, so a busy
/// directory like the home costs nothing until the one name on the way down appears.</para>
/// <para>A file whose directory exists is covered by that directory. One whose directory does not
/// exist yet — on a first run there is no <c>~/.config/myapp/</c> — is covered by its nearest
/// existing ancestor, waiting for the next segment; when it appears the plan is rebuilt one level
/// deeper.</para>
/// <para>Each directory that holds a wanted file is also watched from its parent, for its own name,
/// so a config directory that is replaced (renamed away and recreated, say) is noticed — unless the
/// parent is shared with other users, such as <c>/tmp</c>.</para>
/// <para>At most <see cref="MaxDirectories"/> are watched, taken in target order (the config
/// locations and loaded files first), by two rules that hold whatever the order: a directory that
/// is watched is watched for all its wanted names, and a directory holding a wanted file is watched
/// only together with its parent. Taking one directory can so take a chain up the tree — its parent,
/// and that parent's parent if the parent holds wanted files too — up to a directory already
/// watched. The chain counts against the cap as a whole and is taken whole or not at all, so the cap
/// never leaves a file watched in a directory whose replacement nobody would see; one that does not
/// fit is skipped, and later ones may still fit. The directories skipped are counted in
/// <see cref="Dropped"/>.</para>
/// </remarks>
/// <param name="Directories">The directories to watch, in target order, each followed by the parents it needs the first time they are needed.</param>
/// <param name="Dropped">Directories with wanted names that were not watched, for the cap.</param>
internal sealed record WatchPlan(IReadOnlyList<WatchedDirectory> Directories, int Dropped)
{
    /// <summary>The most directories watched at once.</summary>
    public const int MaxDirectories = 64;

    /// <summary>
    /// How child names are compared: without case, on every platform, rather than by each disk's own
    /// rules (Linux mounts case-insensitive disks too, and macOS case-sensitive ones). Matching too
    /// little would miss a save; matching too much costs only a read: a save to some other file whose
    /// name differs from a watched one only by case reads the config again, and since the reload
    /// raises the consumer's change event only when what it read differs, nothing is
    /// re-themed. A write to a file whose name differs from a watched directory's only by case is told
    /// apart and reloads; creating, deleting or renaming one rebuilds the watchers first. See
    /// the watcher's event filtering.
    /// </summary>
    public static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>Whether a save to (or creation of) <paramref name="path"/> is seen directly.</summary>
    public bool Covers(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(full);
        return Directories.Any(d => d.Path == directory && d.Names.Contains(System.IO.Path.GetFileName(full)));
    }

    /// <summary>The names watched in <paramref name="directory"/>, or null when it is not watched.</summary>
    public IReadOnlySet<string>? NamesIn(string directory) =>
        Directories.FirstOrDefault(d => d.Path == System.IO.Path.GetFullPath(directory))?.Names;

    /// <param name="shared">
    /// Whether other users can create entries in a directory (see <see cref="SharedDirectory"/>); none
    /// by default. A shared parent is not watched for a config directory inside it being replaced, so
    /// they cannot rebuild the watchers at will. It is still watched for a file in it, or for the next
    /// missing directory on the way down to one.
    /// </param>
    public static WatchPlan For(IEnumerable<string> paths, Func<string, bool> directoryExists, int maxDirectories = MaxDirectories,
        Func<string, bool>? shared = null)
    {
        shared ??= _ => false;
        var order = new List<string>();
        var names = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var holdingNames = new HashSet<string>(StringComparer.Ordinal);

        HashSet<string> NamesFor(string directory)
        {
            if (!names.TryGetValue(directory, out var set))
            {
                order.Add(directory);
                names[directory] = set = new HashSet<string>(NameComparer);
            }
            return set;
        }
        foreach (var path in paths.Select(System.IO.Path.GetFullPath))
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (directory is null)
                continue;

            // The file's own directory if it exists, else the nearest existing ancestor and the
            // next segment down towards the file.
            var child = path;
            while (directory is not null && !directoryExists(directory))
            {
                child = directory;
                directory = System.IO.Path.GetDirectoryName(directory);
            }
            if (directory is null)
                continue;

            NamesFor(directory).Add(System.IO.Path.GetFileName(child));
            holdingNames.Add(directory);
        }

        // In target order, each directory with the chain of parents it needs, while the chain fits.
        var kept = new List<string>();
        var keptNames = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var subdirectories = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var dropped = 0;
        HashSet<string> Keep(string directory)
        {
            if (!keptNames.TryGetValue(directory, out var set))
            {
                kept.Add(directory);
                keptNames[directory] = set = new HashSet<string>(NameComparer);
            }
            return set;
        }
        string? ParentNeeded(string directory) =>
            holdingNames.Contains(directory) && System.IO.Path.GetDirectoryName(directory) is { } p && directoryExists(p) && !shared(p) ? p : null;
        foreach (var directory in order)
        {
            // Already watched, for all its names and with its parent: nothing more to take.
            if (keptNames.ContainsKey(directory))
                continue;
            var chain = new List<string>();
            for (var d = directory; d is not null && !keptNames.ContainsKey(d); d = ParentNeeded(d))
                chain.Add(d);
            if (kept.Count + chain.Count > maxDirectories)
            {
                dropped++;
                continue;
            }
            foreach (var d in chain)
            {
                if (names.TryGetValue(d, out var wanted))
                    Keep(d).UnionWith(wanted);
                else
                    Keep(d);
                if (ParentNeeded(d) is not { } parent)
                    continue;
                var name = System.IO.Path.GetFileName(d);
                Keep(parent).Add(name);
                if (!subdirectories.TryGetValue(parent, out var subs))
                    subdirectories[parent] = subs = new HashSet<string>(NameComparer);
                subs.Add(name);
            }
        }

        IReadOnlySet<string> none = new HashSet<string>(NameComparer);
        return new WatchPlan(
            kept.Select(d => new WatchedDirectory(d, keptNames[d], subdirectories.TryGetValue(d, out var s) ? s : none)).ToList(),
            dropped);
    }
}
