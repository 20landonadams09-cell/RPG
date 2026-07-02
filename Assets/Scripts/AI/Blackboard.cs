using System.Collections.Generic;

namespace BasicRPG.AI
{
    /// <summary>
    /// A small typed key/value store shared between behavior-tree nodes — the AI's "short-term
    /// memory". Nodes read world state (player position, own health, home waypoint, last-seen
    /// location) and write decisions/progress through here instead of through scattered fields.
    ///
    /// Patterned after the Blackboard concept in NPBehave and fluid-behavior-tree (see the
    /// `Reference/` archives), but written from scratch for BasicRPG: no allocation per access
    /// beyond the dictionary, no boxing beyond the stored object, no dependencies. Strings are
    /// keys (readability + zero coupling to a generated key table); for hot loops, cache the
    /// value in a node field after the first read.
    /// </summary>
    public class Blackboard
    {
        private readonly Dictionary<string, object> values = new Dictionary<string, object>();

        /// <summary>Store `value` under `key` (null removes the key so <see cref="Has"/> returns false).</summary>
        public void Set(string key, object value)
        {
            if (value == null) { values.Remove(key); return; }
            values[key] = value;
        }

        public bool Has(string key) => values.ContainsKey(key);

        /// <summary>Get the value as `T`, or `default` if absent / wrong type. Never throws.</summary>
        public T Get<T>(string key)
        {
            if (values.TryGetValue(key, out object v) && v is T t) return t;
            return default;
        }

        /// <summary>Get as `T`, or `fallback` when absent/wrong-type (the common "sensed player or null" case).</summary>
        public T Get<T>(string key, T fallback)
        {
            if (values.TryGetValue(key, out object v) && v is T t) return t;
            return fallback;
        }

        public void Clear() => values.Clear();
    }
}