namespace TacticalRmm.Application.Dtos;

public class VncCredentialUpsert
{
    public string? Label { get; set; }
    /// <summary>Required on create; on update only sent when the password changes.</summary>
    public string? Password { get; set; }
    public int? Priority { get; set; }
    public bool? Enabled { get; set; }
}

/// <summary>Never contains the password. <see cref="Readable"/> is false when it can no longer be decrypted (the server secret changed).</summary>
public record VncCredentialDto(int Id, string Label, int Priority, bool Enabled, bool IsDefault, bool Readable, DateTime CreatedOn);

public static class VncStatus
{
    public const string Ok = "ok", NoAddress = "no_address", Unreachable = "unreachable", BadPassword = "bad_password",
        TooManyAttempts = "too_many_attempts", Unsupported = "unsupported", NoCredentials = "no_credentials", Error = "error";
}

/// <summary>Result of checking whether an asset's VNC server is reachable and which stored password it accepts.</summary>
public record VncTestDto(string Status, string Message, string? Host, int Port, string? Credential, int Attempts);

/// <summary>A connection file for TightVNC Viewer (contains the working password in VNC's obfuscated form).</summary>
public record VncFileDto(string FileName, string Content, VncTestDto Info);
