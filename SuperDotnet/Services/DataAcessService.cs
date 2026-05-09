using System.IO;

namespace SuperDotnet.Services;

public sealed class DatasetReader : IDisposable
{
    private const int Dimensions = 14;
    private const int BytesPerValue = 2;
    private const int VectorSizeBytes = Dimensions * BytesPerValue; //28!

    private readonly FileStream _vectorStream;
    private readonly BinaryReader _vectorReader;

    private readonly FileStream _labelStream;
    private readonly BinaryReader _labelReader;

    public int TotalVectors { get; }

    public DatasetReader(
        string vectorPath = "data/vectors.bin",
        string labelPath = "data/labels.bin")
    {
        _vectorStream = File.OpenRead(vectorPath);
        _vectorReader = new BinaryReader(_vectorStream);

        _labelStream = File.OpenRead(labelPath);
        _labelReader = new BinaryReader(_labelStream);

        var vectorFileSize = new FileInfo(vectorPath).Length;
        TotalVectors = (int)(vectorFileSize / VectorSizeBytes);
    }

    public float[] ReadVector(int index)
    {
        ValidateIndex(index);

        long offset = (long)index * VectorSizeBytes;
        _vectorStream.Seek(offset, SeekOrigin.Begin);

        var values = new float[Dimensions];

        for (int i = 0; i < Dimensions; i++)
        {
            short bits = _vectorReader.ReadInt16();
            values[i] = (float)BitConverter.Int16BitsToHalf(bits);
        }

        return values;
    }

    public byte ReadLabel(int index)
    {
        ValidateIndex(index);

        _labelStream.Seek(index, SeekOrigin.Begin);
        return _labelReader.ReadByte();
    }

    private void ValidateIndex(int index)
    {
        if (index < 0 || index >= TotalVectors)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    public void Dispose()
    {
        _vectorReader.Dispose();
        _vectorStream.Dispose();

        _labelReader.Dispose();
        _labelStream.Dispose();
    }
}