namespace Avalloy.Commands.Keymaps;

/// <summary>
/// The bindings in force: each trigger sequence bound to at most one keybind, as a trie — a
/// sequence is either a leaf (it runs something) or a prefix of longer ones (a chord leader), never both.
/// </summary>
public sealed class Keymap
{
    private readonly Dictionary<string, KeymapBinding> _leaves = new(StringComparer.Ordinal);
    private readonly HashSet<string> _prefixes = new(StringComparer.Ordinal);

    internal Keymap(IReadOnlyList<KeymapBinding> bindings)
    {
        Bindings = Array.AsReadOnly(bindings.ToArray());
        foreach (var kb in bindings)
        {
            var key = KeyOf(kb.Sequence);
            _leaves[key] = kb;
            for (var i = 1; i < kb.Sequence.Count; i++)
                _prefixes.Add(KeyOf(kb.Sequence.Take(i)));
        }
    }

    /// <summary>An empty keymap: every key reaches the terminal.</summary>
    public static Keymap Empty { get; } = new([]);

    /// <summary>The bindings in the order they were made, last one winning for display.</summary>
    public IReadOnlyList<KeymapBinding> Bindings { get; }

    /// <summary>The binding for exactly this sequence, if it is a leaf.</summary>
    public KeymapBinding? Find(string sequenceKey) => _leaves.GetValueOrDefault(sequenceKey);

    /// <summary>Whether this sequence is the start of a longer binding.</summary>
    public bool IsPrefix(string sequenceKey) => _prefixes.Contains(sequenceKey);

    /// <summary>
    /// The binding to show beside an action: the last one made whose first action is
    /// <paramref name="action"/> with <paramref name="parameter"/>, as the host's menus pick it.
    /// </summary>
    public KeymapBinding? BindingFor(string action, string? parameter) =>
        LastWhere(a => a.Name == action && a.Parameter == parameter);

    /// <summary>The last binding made for <paramref name="action"/>, whatever its parameter.</summary>
    public KeymapBinding? BindingFor(string action) => LastWhere(a => a.Name == action);

    /// <summary>
    /// Every binding whose first action is <paramref name="action"/> with <paramref name="parameter"/>,
    /// the last made first.
    /// </summary>
    public IEnumerable<KeymapBinding> BindingsFor(string action, string? parameter) =>
        AllWhere(a => a.Name == action && a.Parameter == parameter);

    /// <summary>Every binding for <paramref name="action"/>, whatever its parameter, the last made first.</summary>
    public IEnumerable<KeymapBinding> BindingsFor(string action) => AllWhere(a => a.Name == action);

    private IEnumerable<KeymapBinding> AllWhere(Func<CommandInvocation, bool> match)
    {
        for (var i = Bindings.Count - 1; i >= 0; i--)
        {
            if (Bindings[i].Action is { } a && match(a))
                yield return Bindings[i];
        }
    }

    private KeymapBinding? LastWhere(Func<CommandInvocation, bool> match)
    {
        for (var i = Bindings.Count - 1; i >= 0; i--)
        {
            if (Bindings[i].Action is { } a && match(a))
                return Bindings[i];
        }
        return null;
    }

    /// <summary>An opaque lookup key for a trigger sequence, joined by <c>&gt;</c>.</summary>
    public static string KeyOf(IEnumerable<KeyTrigger> sequence) => string.Join('>', sequence);
}

/// <summary>Resolves exact and prefix conflicts in insertion order, newest binding winning.</summary>
public sealed class KeymapBuilder
{
    private readonly Trie _trie = new();

    public KeymapBuilder Bind(KeymapBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Validate(binding.Sequence);
        var snapshot = binding with
        {
            Sequence = Array.AsReadOnly(binding.Sequence.ToArray()),
            Actions = Array.AsReadOnly(binding.Actions.ToArray()),
        };
        _trie.Apply(snapshot);
        return this;
    }

    /// <summary>Remove this sequence and all longer sequences beneath it.</summary>
    public KeymapBuilder Unbind(IReadOnlyList<KeyTrigger> sequence)
    {
        Validate(sequence);
        _trie.Apply(new KeymapBinding(sequence, []), remove: true);
        return this;
    }

    public Keymap Build() => new(_trie.Bindings());

    /// <summary>
    /// Existing bindings looked up or visited so far. Placing an n-key binding costs n lookups plus one per
    /// binding it displaces, so building stays linear in the bindings made; the tests hold it to that.
    /// </summary>
    internal long Steps => _trie.Steps;

    private static void Validate(IReadOnlyList<KeyTrigger> sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (sequence.Count == 0) throw new ArgumentException("A binding needs a trigger.", nameof(sequence));
        foreach (var trigger in sequence)
            if (trigger is null || string.IsNullOrEmpty(trigger.Key) || !Enum.IsDefined(trigger.Kind))
                throw new ArgumentException("Invalid trigger.", nameof(sequence));
    }

    private sealed class Trie
    {
        // In the order they were made; a binding taken out leaves a hole, skipped at the end.
        private readonly List<KeymapBinding?> _made = [];
        // Each leaf's sequence key: where it sits in _made, and the keys of its proper prefixes.
        private readonly Dictionary<string, (int Slot, string[] Prefixes)> _leaves = new(StringComparer.Ordinal);
        // Each prefix key: the leaves under it.
        private readonly Dictionary<string, HashSet<string>> _under = new(StringComparer.Ordinal);

        // the host's trie rules: a new binding replaces the one on its sequence, a leaf it extends (which
        // becomes a leader), and everything under it (which it ends). unbind removes the sequence and
        // everything under it and adds nothing.
        public void Apply(KeymapBinding kb, bool remove = false)
        {
            var keys = new string[kb.Sequence.Count];
            for (var i = 0; i < keys.Length; i++)
                keys[i] = i == 0 ? kb.Sequence[0].ToString() : keys[i - 1] + ">" + kb.Sequence[i];
            var key = keys[^1];
            var prefixes = keys[..^1];

            Remove(key);
            if (_under.TryGetValue(key, out var below))
                foreach (var leaf in Visit(below.ToList()))
                    Remove(leaf);
            if (remove)
                return;
            foreach (var prefix in prefixes)
                Remove(prefix);

            _leaves[key] = (_made.Count, prefixes);
            _made.Add(kb);
            foreach (var prefix in prefixes)
            {
                if (!_under.TryGetValue(prefix, out var set))
                    _under[prefix] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(key);
            }
        }

        public long Steps { get; private set; }

        // Every existing leaf the build walks past goes through here, so the count sees it.
        private IEnumerable<string> Visit(IEnumerable<string> leaves)
        {
            foreach (var leaf in leaves)
            {
                Steps++;
                yield return leaf;
            }
        }

        private void Remove(string key)
        {
            Steps++;
            if (!_leaves.Remove(key, out var leaf))
                return;
            _made[leaf.Slot] = null;
            foreach (var prefix in leaf.Prefixes)
                _under[prefix].Remove(key);
        }

        public List<KeymapBinding> Bindings() => _made.OfType<KeymapBinding>().ToList();
    }
}
