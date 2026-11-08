using System.Text.RegularExpressions;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Logic;

public static partial class AccountRules
{
    [GeneratedRegex(@"^[A-Za-z0-9_.@+\-]{3,150}$")] private static partial Regex UsernamePattern();

    public static string? ValidateUsername(string? u) =>
        u is not null && UsernamePattern().IsMatch(u) ? null : "username must be 3-150 characters: letters, digits and _ . @ + -";

    /// <summary>At least 12 characters, must not contain the username, at least 3 of 4 character classes.</summary>
    public static string? ValidatePassword(string? password, string username)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 12) return "Password must be at least 12 characters";
        if (password.Length > 256) return "Password is too long";
        if (username.Length >= 3 && password.Contains(username, StringComparison.OrdinalIgnoreCase)) return "Password must not contain the username";
        var classes = (password.Any(char.IsLower) ? 1 : 0) + (password.Any(char.IsUpper) ? 1 : 0)
                    + (password.Any(char.IsDigit) ? 1 : 0) + (password.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
        return classes >= 3 ? null : "Password must mix at least 3 of: lowercase, uppercase, digits, symbols";
    }

    /// <summary>A non-superuser must not touch a superuser (prevents escalation/takeover).</summary>
    public static bool CanManageUser(User caller, User target) => PermissionEvaluator.IsSuper(caller) || !PermissionEvaluator.IsSuper(target);

    /// <summary>A non-superuser may only grant roles that are not superuser and whose permissions are a subset of their own.</summary>
    public static bool CanGrant(User caller, bool roleIsSuperuser, IEnumerable<string> rolePermissions)
    {
        if (PermissionEvaluator.IsSuper(caller)) return true;
        if (roleIsSuperuser) return false;
        var mine = caller.Role?.Permissions ?? [];
        return rolePermissions.All(mine.Contains);
    }

    public static string? ValidateRole(string? name, IEnumerable<string>? permissions)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) return "name is required (max 120 characters)";
        var unknown = (permissions ?? []).Where(p => !Perm.All.Contains(p)).ToList();
        return unknown.Count == 0 ? null : $"Unknown permission(s): {string.Join(", ", unknown)}";
    }
}
