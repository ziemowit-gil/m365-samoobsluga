using M365Samoobsluga.Core.Models;
namespace M365Samoobsluga.Core.Services;

/// <summary>Tożsamość zalogowanego użytkownika wyłącznie z tokenu/ciasteczka uwierzytelnienia.</summary>
public interface ICurrentUser { UserIdentity Current { get; } }
