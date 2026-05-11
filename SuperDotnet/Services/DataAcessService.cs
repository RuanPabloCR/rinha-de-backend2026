using System.IO;
using System.IO.MemoryMappedFiles;

namespace SuperDotnet.Services;

public unsafe sealed class DatasetReader : IDisposable
{
    public const int Dimensions = VectorLayout.Dimensions;
    private const int BytesPerValue = 2;
    public const int VectorSizeBytes = Dimensions * BytesPerValue;
    private readonly MemoryMappedFile _vectorMmf;
    private readonly MemoryMappedViewAccessor _vectorAccessor;
    private byte* _vectorPtr;

    private readonly MemoryMappedFile _labelMmf;
    private readonly MemoryMappedViewAccessor _labelAccessor;
    private byte* _labelPtr;

    public int TotalVectors { get; }
    public byte* VectorPointer => _vectorPtr;
    public byte* LabelPointer => _labelPtr;


    public DatasetReader(
        string vectorPath = "data/vectors.bin",
        string labelPath = "data/labels.bin")
    {
        var vectorSize = new FileInfo(vectorPath).Length;

        TotalVectors = (int)(vectorSize / VectorSizeBytes);

        _vectorMmf = MemoryMappedFile.CreateFromFile(
            vectorPath,
            FileMode.Open,
            null,
            0,
            MemoryMappedFileAccess.Read);

        _vectorAccessor = _vectorMmf.CreateViewAccessor(
            0,
            0,
            MemoryMappedFileAccess.Read);

        byte* vectorPtr = null;
        _vectorAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref vectorPtr);
        _vectorPtr = vectorPtr;

        _labelMmf = MemoryMappedFile.CreateFromFile(
            labelPath,
            FileMode.Open,
            null,
            0,
            MemoryMappedFileAccess.Read);

        _labelAccessor = _labelMmf.CreateViewAccessor(
            0,
            0,
            MemoryMappedFileAccess.Read);

        byte* labelPtr = null;
        _labelAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref labelPtr);
        _labelPtr = labelPtr;
    }

    public void ReadVector(int index, Span<float> destination)
    {
        ValidateIndex(index);

        if (destination.Length < Dimensions)
            throw new ArgumentException(
                $"Destination span must be at least {Dimensions} elements.",
                nameof(destination));

        byte* vectorStart = _vectorPtr + ((long)index * VectorSizeBytes);

        for (int i = 0; i < Dimensions; i++)
        {
            short bits = *(short*)(vectorStart + (i * BytesPerValue));
            destination[i] = (float)BitConverter.Int16BitsToHalf(bits);
        }
    }

    public byte ReadLabel(int index)
    {
        ValidateIndex(index);

        return *(_labelPtr + index);
    }

    private void ValidateIndex(int index)
    {
        if ((uint)index >= (uint)TotalVectors)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    public void Dispose()
    {
        _vectorAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
        _vectorAccessor.Dispose();
        _vectorMmf.Dispose();

        _labelAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
        _labelAccessor.Dispose();
        _labelMmf.Dispose();
    }
}
