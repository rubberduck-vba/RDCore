namespace RDCore.SDK.Server.Configuration;

/// <summary>
/// When a change of a setting takes effect, which decides whether the platform lets a client change it at all.
/// </summary>
public enum SettingReach
{
    /// <summary>
    /// The setting takes effect when it changes, in the components that are running.
    /// </summary>
    Live = 0,

    /// <summary>
    /// The setting is read when a component starts, or when a session does: a change takes effect the next time it does.
    /// </summary>
    NextStart = 1,

    /// <summary>
    /// The setting is read before there is a client to ask - which component a process is, how it connects - and only the deployed configuration sets it: a
    /// client never changes it, because a bad value would keep the platform from starting, and with it the client that could put it back.
    /// </summary>
    Deployment = 2,
}

/// <summary>
/// Says when a change of a property of a settings type takes effect (<see cref="SettingsSchema"/>).
/// </summary>
/// <remarks>
/// A property that is not marked is <see cref="SettingReach.Live"/>, unless the property of the settings it is part of says otherwise: what a property says of a
/// section, it says of every setting in it that does not say something else.
/// </remarks>
/// <param name="reach">When a change of the setting takes effect.</param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SettingAttribute(SettingReach reach) : Attribute
{
    /// <summary>
    /// When a change of the setting takes effect.
    /// </summary>
    public SettingReach Reach { get; } = reach;
}

/// <summary>
/// Says that a property of a settings type decides what a program may do to the machine it runs on - call native code, automate another application
/// (<see cref="SettingsSchema"/>): a client changes it only for its user, and says what changing it means before it does.
/// </summary>
/// <remarks>
/// What a property says of a section, it says of every setting in it.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class TrustSensitiveSettingAttribute : Attribute;
