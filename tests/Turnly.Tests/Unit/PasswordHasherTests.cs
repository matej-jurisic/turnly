using Turnly.Core.Auth;

namespace Turnly.Tests.Unit;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new PasswordHasher();

    [Fact]
    public void Verify_returns_true_for_correct_password()
    {
        var hash = _hasher.Hash("correct horse battery");
        Assert.True(_hasher.Verify(hash, "correct horse battery"));
    }

    [Fact]
    public void Verify_returns_false_for_wrong_password()
    {
        var hash = _hasher.Hash("correct horse battery");
        Assert.False(_hasher.Verify(hash, "wrong password"));
    }

    [Fact]
    public void Hash_is_salted_so_same_password_yields_different_hashes()
    {
        var a = _hasher.Hash("same-password");
        var b = _hasher.Hash("same-password");
        Assert.NotEqual(a, b);
        Assert.True(_hasher.Verify(a, "same-password"));
        Assert.True(_hasher.Verify(b, "same-password"));
    }
}
