namespace Profiscal.Domain.Enums;

public enum AuthEvent
{
    Register,
    LoginSucceeded,
    LoginFailed,
    AccountLockedOut,
    TokenRefreshed,
    TokenReuseDetected,
    Logout,
    LogoutEverywhere,
    SessionRevoked,
    PasswordChanged,
    RoleChanged,
    AccountLocked,
    AccountUnlocked
}
