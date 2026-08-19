namespace TelemetryDashboard.Core.Interfaces;

public interface ITelemetryCompressor
{
    byte[] CompressFloatStream(double[] samples);
    double[] DecompressFloatStream(byte[] compressedBytes);
    byte[] CompressDoubles(double[] samples);
    double[] DecompressDoubles(byte[] compressedBytes);
    byte[] CompressTimeStamps(long[] timestamps);
    long[] DecompressTimeStamps(byte[] compressedBytes);
}
