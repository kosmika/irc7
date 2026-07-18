namespace Irc.Security;

/// <summary>
/// Global, read-only holder for the permission profiles loaded from DefaultPermissions.json.
/// Populated once at startup so validation logic (e.g. nickname prefix checks) can access
/// the profiles without repeatedly loading/parsing the file.
/// </summary>
public static class DefaultPermissions
{
    /// <summary>Key used for the anonymous fallback profile in DefaultPermissions.json.</summary>
    public const string AnonProfileKey = "ANON";

    /// <summary>
    /// Fallback prefix used when the ANON profile is not present in DefaultPermissions.json.
    /// Anonymous users have no nickname prefix by default.
    /// </summary>
    private const string DefaultAnonPrefix = "";

    private static Dictionary<string, PermissionProfile> _profiles =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, PermissionProfile> Profiles => _profiles;

    /// <summary>
    /// Registers the loaded permission profiles globally. Should be called once at startup.
    /// </summary>
    public static void Initialize(Dictionary<string, PermissionProfile>? profiles)
    {
        _profiles = profiles != null
            ? new Dictionary<string, PermissionProfile>(profiles, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, PermissionProfile>(StringComparer.OrdinalIgnoreCase);
    }

    public static PermissionProfile? GetProfile(string key)
    {
        return _profiles.TryGetValue(key, out var profile) ? profile : null;
    }

    /// <summary>
    /// The nickname prefix for anonymous users. Falls back to <see cref="DefaultAnonPrefix"/>
    /// when the ANON profile is missing from DefaultPermissions.json.
    /// </summary>
    public static string AnonPrefix => GetProfile(AnonProfileKey)?.Prefix ?? DefaultAnonPrefix;
}

