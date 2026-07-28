using DevFlow.Application.Common;
using FluentAssertions;
using Xunit;

namespace DevFlow.IntegrationTests.Attachments;

public class SignedBlobUrlTests
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long";

    private static long ExpiresInMinutes(int minutes) => DateTimeOffset.UtcNow.AddMinutes(minutes).ToUnixTimeSeconds();

    [Fact]
    public void Verify_accepts_a_correctly_signed_unexpired_url()
    {
        var expires = ExpiresInMinutes(5);
        var signature = SignedBlobUrl.Sign(SigningKey, "tenant/task/blob", "file.pdf", "application/pdf", expires);

        var isValid = SignedBlobUrl.Verify(SigningKey, "tenant/task/blob", "file.pdf", "application/pdf", expires, signature);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_rejects_an_expired_signature()
    {
        var expires = ExpiresInMinutes(-1); // already in the past
        var signature = SignedBlobUrl.Sign(SigningKey, "tenant/task/blob", "file.pdf", "application/pdf", expires);

        var isValid = SignedBlobUrl.Verify(SigningKey, "tenant/task/blob", "file.pdf", "application/pdf", expires, signature);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_a_tampered_signature()
    {
        var expires = ExpiresInMinutes(5);
        var signature = SignedBlobUrl.Sign(SigningKey, "tenant/task/blob", "file.pdf", "application/pdf", expires);

        var isValid = SignedBlobUrl.Verify(SigningKey, "tenant/task/blob", "file.pdf", "application/pdf", expires, signature + "x");

        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("other-blob-key", "file.pdf", "application/pdf")]
    [InlineData("tenant/task/blob", "renamed.pdf", "application/pdf")]
    [InlineData("tenant/task/blob", "file.pdf", "application/octet-stream")]
    public void Verify_rejects_a_signature_reused_for_different_blob_details(string blobKey, string fileName, string contentType)
    {
        var expires = ExpiresInMinutes(5);
        // Signed for the *original* details...
        var signature = SignedBlobUrl.Sign(SigningKey, "tenant/task/blob", "file.pdf", "application/pdf", expires);

        // ...then presented against different details — proves every field
        // is part of what's actually signed, not just blobKey.
        var isValid = SignedBlobUrl.Verify(SigningKey, blobKey, fileName, contentType, expires, signature);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_a_signature_produced_with_a_different_signing_key()
    {
        var expires = ExpiresInMinutes(5);
        var signature = SignedBlobUrl.Sign(SigningKey, "tenant/task/blob", "file.pdf", "application/pdf", expires);

        var isValid = SignedBlobUrl.Verify("a-completely-different-key-value", "tenant/task/blob", "file.pdf", "application/pdf", expires, signature);

        isValid.Should().BeFalse();
    }
}
