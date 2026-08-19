namespace TelemetryDashboard.Core.Interfaces;

public interface ISecurityProvider
{
    byte[] EncryptPayload(byte[] plainData, byte[] key);
    byte[] DecryptPayload(byte[] encryptedData, byte[] key);
    byte[] SignData(byte[] data, byte[] privateKey);
    bool VerifySignature(byte[] data, byte[] signature, byte[] publicKey);
}
