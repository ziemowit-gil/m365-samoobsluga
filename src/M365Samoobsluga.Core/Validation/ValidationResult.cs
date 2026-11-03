namespace M365Samoobsluga.Core.Validation;

/// <summary>Wynik walidacji z komunikatem po polsku.</summary>
public sealed record ValidationResult(bool IsValid, string? Error = null, string? Normalized = null)
{
    public static ValidationResult Ok(string normalized) => new(true, null, normalized);
    public static ValidationResult Fail(string error) => new(false, error);
}
