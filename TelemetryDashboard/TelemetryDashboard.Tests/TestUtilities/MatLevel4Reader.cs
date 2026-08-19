using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TelemetryDashboard.Tests.TestUtilities;

/// <summary>One matrix decoded from a MATLAB Level 4 MAT-file.</summary>
/// <param name="Name">Variable name, with the trailing NUL removed.</param>
/// <param name="Rows">Row count declared in the matrix header.</param>
/// <param name="Columns">Column count declared in the matrix header.</param>
/// <param name="Values">Values indexed <c>[row, column]</c>, de-interleaved from column-major.</param>
public sealed record MatMatrix(string Name, int Rows, int Columns, double[,] Values);

/// <summary>
/// Independent decoder for the MAT Level 4 format, used to read back what the exporter wrote.
/// </summary>
/// <remarks>
/// Deliberately not written in terms of <c>MatFileWriter</c>: a reader that shared the writer's
/// assumptions would agree with it about a wrong byte order or a transposed matrix just as happily
/// as about a right one. This decodes the format as the specification states it — a 20-byte header
/// of five little-endian int32s, then the NUL-terminated name, then <c>rows * columns</c> float64s
/// in column-major order — so a disagreement means the file is wrong, not that the two halves drifted.
/// </remarks>
public static class MatLevel4Reader
{
    /// <summary>Decodes every matrix in <paramref name="path"/>, in file order.</summary>
    /// <exception cref="InvalidDataException">A header declares an encoding this reader rejects.</exception>
    public static IReadOnlyList<MatMatrix> Read(string path)
    {
        var matrices = new List<MatMatrix>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream);

        while (stream.Position < stream.Length)
        {
            int type = reader.ReadInt32();
            int rows = reader.ReadInt32();
            int columns = reader.ReadInt32();
            int imaginary = reader.ReadInt32();
            int nameLength = reader.ReadInt32();

            // MOPT 0000: little-endian, no reserved digit, double precision, numeric full matrix.
            if (type != 0) throw new InvalidDataException($"Unsupported MOPT type {type}.");
            if (imaginary != 0) throw new InvalidDataException("Complex matrices are not expected here.");

            string name = Encoding.ASCII.GetString(reader.ReadBytes(nameLength)).TrimEnd('\0');

            var values = new double[rows, columns];
            for (int column = 0; column < columns; column++)
            {
                for (int row = 0; row < rows; row++)
                {
                    values[row, column] = reader.ReadDouble();
                }
            }

            matrices.Add(new MatMatrix(name, rows, columns, values));
        }

        return matrices;
    }
}
