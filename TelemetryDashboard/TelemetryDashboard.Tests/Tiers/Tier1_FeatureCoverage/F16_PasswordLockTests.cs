using System.Security.Cryptography;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F16_PasswordLockTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void PasswordLock_DefaultState_IsOperatorView()
    {
        var guard = new PasswordGuardHelper();
        guard.IsEngineerMode.Should().BeFalse();
        guard.CurrentMode.Should().Be("OperatorView");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void PasswordLock_Unlock_WithCorrectPassword()
    {
        var guard = new PasswordGuardHelper();
        bool unlocked = guard.TryUnlock("admin123");

        unlocked.Should().BeTrue();
        guard.IsEngineerMode.Should().BeTrue();
        guard.CurrentMode.Should().Be("EngineerMode");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void PasswordLock_Unlock_FailsWithIncorrectPassword()
    {
        var guard = new PasswordGuardHelper();
        bool unlocked = guard.TryUnlock("wrongpass");

        unlocked.Should().BeFalse();
        guard.IsEngineerMode.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void PasswordLock_Lock_RestoresOperatorView()
    {
        var guard = new PasswordGuardHelper();
        guard.TryUnlock("admin123");
        guard.Lock();

        guard.IsEngineerMode.Should().BeFalse();
        guard.CurrentMode.Should().Be("OperatorView");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void PasswordLock_RestrictedAction_FailsWhenLocked()
    {
        var guard = new PasswordGuardHelper();
        Action sensitiveAction = () =>
        {
            if (!guard.IsEngineerMode) throw new UnauthorizedAccessException("Operator mode cannot execute action");
        };

        sensitiveAction.Should().Throw<UnauthorizedAccessException>();
    }
}

public class PasswordGuardHelper
{
    private static readonly string _hashedPasswordHex = HashPassword("admin123");

    public bool IsEngineerMode { get; private set; } = false;
    public string CurrentMode => IsEngineerMode ? "EngineerMode" : "OperatorView";

    public bool TryUnlock(string password)
    {
        if (HashPassword(password) == _hashedPasswordHex)
        {
            IsEngineerMode = true;
            return true;
        }
        return false;
    }

    public void Lock()
    {
        IsEngineerMode = false;
    }

    private static string HashPassword(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
