namespace EasyExile.Radar.Settings;

/// <summary>
/// A setting that lives for one run and is never written to disk.
/// </summary>
/// <remarks>
/// The store persists every option it can read, which is the right default —
/// a setting that exists is a setting that persists, with no third place to
/// remember. This is the exception, and it is for one kind of option: something
/// that arms behaviour and should never be armed by a config file the user has
/// forgotten about.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TransientAttribute : Attribute;
