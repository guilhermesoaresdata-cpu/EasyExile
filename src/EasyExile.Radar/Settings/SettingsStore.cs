using System.Globalization;
using System.Reflection;

namespace EasyExile.Radar.Settings;

/// <summary>
/// Remembers the panel's settings between runs.
/// </summary>
/// <remarks>
/// A flat <c>key=value</c> file, written next to the executable. Not JSON: the
/// contract-isolation guard forbids a JSON parser in the product projects,
/// because the one thing that must never be loadable at runtime is the offset
/// contract. Settings are booleans, numbers and nothing else, so a serializer
/// would be a dependency bought for no benefit and an exemption argued for no
/// reason.
///
/// Reflection over the category records keeps this from needing an edit every
/// time an option is added — a setting that exists is a setting that persists,
/// with no third place to remember to update.
///
/// Unknown or malformed keys are skipped rather than fatal. A settings file from
/// an older build should cost you the options that moved, not the ones that did
/// not.
/// </remarks>
public static class SettingsStore
{
    public static string Path { get; } =
        System.IO.Path.Combine(AppContext.BaseDirectory, "settings.txt");

    /// <summary>The category properties on the root, each a record of options.</summary>
    private static PropertyInfo[] Categories =>
        typeof(RadarSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.PropertyType.IsClass && p.PropertyType != typeof(string))
            .ToArray();

    /// <summary>
    /// What was last written to each file, so a check that changes nothing
    /// touches no disk. Keyed by path rather than held as one string: the app
    /// uses one file, but nothing should break when something uses two.
    /// </summary>
    private static readonly Dictionary<string, string> LastWritten = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Writes only when something actually changed.
    /// </summary>
    /// <remarks>
    /// Called from the render loop, which is why it compares before touching the
    /// disk. Saving only on exit was the original design and it lost everything:
    /// an overlay gets its console closed or its process killed far more often
    /// than it is shut down politely, and none of those paths run an exit hook.
    /// </remarks>
    public static void SaveIfChanged(RadarSettings settings, string? path = null)
    {
        path ??= Path;

        var text = Serialise(settings);

        if (LastWritten.TryGetValue(path, out var previous) && text == previous) return;

        Write(text, path);
    }

    public static void Save(RadarSettings settings, string? path = null) =>
        Write(Serialise(settings), path ?? Path);

    private static void Write(string text, string path)
    {
        try
        {
            File.WriteAllText(path, text);

            lock (LastWritten) LastWritten[path] = text;
        }
        catch (IOException)
        {
            // Losing the settings file is not worth taking the overlay down for.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Serialise(RadarSettings settings)
    {
        var lines = new List<string>();

        foreach (var category in Categories)
        {
            var value = category.GetValue(settings);
            if (value is null) continue;

            foreach (var option in Options(category.PropertyType))
            {
                if (Format(option.GetValue(value)) is not { } text) continue;

                lines.Add($"{category.Name}.{option.Name}={text}");
            }
        }

        lines.Sort(StringComparer.Ordinal);

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Applies the saved file over the defaults, in place.</summary>
    public static void Load(RadarSettings settings, string? path = null)
    {
        path ??= Path;

        string[] lines;

        try
        {
            if (!File.Exists(path)) return;

            lines = File.ReadAllLines(path);

            lock (LastWritten) LastWritten[path] = string.Join(Environment.NewLine, lines);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var category in Categories)
        {
            var current = category.GetValue(settings);
            if (current is null) continue;

            var options = Options(category.PropertyType);
            var changed = false;

            foreach (var line in lines)
            {
                var split = line.IndexOf('=');
                if (split <= 0) continue;

                var key = line[..split];
                var text = line[(split + 1)..];

                var dot = key.IndexOf('.');
                if (dot <= 0) continue;

                if (!string.Equals(key[..dot], category.Name, StringComparison.Ordinal)) continue;

                var name = key[(dot + 1)..];
                var option = options.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.Ordinal));

                if (option is null || Parse(option.PropertyType, text) is not { } parsed) continue;

                // The categories are records with init-only properties, so the
                // value is rebuilt rather than mutated — the same way the panel
                // changes a setting.
                current = With(current, option, parsed);
                changed = true;
            }

            if (changed) category.SetValue(settings, current);
        }
    }

    private static PropertyInfo[] Options(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && Supported(p.PropertyType) &&
                        p.GetCustomAttribute<TransientAttribute>() is null)
            .ToArray();

    private static bool Supported(Type type) =>
        type == typeof(bool) || type == typeof(int) || type == typeof(float) || type.IsEnum;

    private static string? Format(object? value) => value switch
    {
        bool flag => flag ? "1" : "0",
        int number => number.ToString(CultureInfo.InvariantCulture),
        float number => number.ToString("0.###", CultureInfo.InvariantCulture),

        // By name, not by number. A reordered enum would otherwise silently turn
        // one saved choice into a different one.
        Enum choice => choice.ToString(),

        _ => null,
    };

    private static object? Parse(Type type, string text)
    {
        if (type == typeof(bool)) return text is "1" or "true" or "True";

        if (type == typeof(int))
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

        if (type == typeof(float))
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;

        // A name that no longer exists leaves the default in place, which is the
        // same rule every other unreadable value follows here.
        if (type.IsEnum) return Enum.TryParse(type, text, ignoreCase: true, out var choice) ? choice : null;

        return null;
    }

    /// <summary>
    /// A record's <c>with</c>, reached through reflection: copy, then set the
    /// one property through its backing setter.
    /// </summary>
    private static object With(object record, PropertyInfo option, object value)
    {
        var clone = record.GetType()
            .GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance)
            ?.Invoke(record, null) ?? record;

        option.SetValue(clone, value);

        return clone;
    }
}
