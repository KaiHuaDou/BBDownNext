using System;

namespace BBDown.Tests;

public class LoginTests
{
    // MaskSecret 是 private，借反射覆盖：日志里只露凭据首尾，避免明文泄露 (P0-3)
    private static readonly System.Reflection.MethodInfo MaskSecretMethod =
        typeof(Login).GetMethod("MaskSecret", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    private static string MaskSecret(string? s)
    {
        object?[] args = [s];
        return (string) MaskSecretMethod.Invoke(null, args)!;
    }

    [Theory]
    [InlineData(null, "***")]
    [InlineData("", "***")]
    [InlineData("abc", "***")]
    [InlineData("12345678", "***")]            // 长度恰好 8 → 仍遮罩
    [InlineData("123456789", "1234****6789")]  // 长度 9 → 首尾各 4 位
    [InlineData("abcdefghijklmnop", "abcd****mnop")]
    public void MaskSecret_ShowsOnlyFirstAndLastFour(string? input, string expected)
    {
        Assert.Equal(expected, MaskSecret(input));
    }
}
