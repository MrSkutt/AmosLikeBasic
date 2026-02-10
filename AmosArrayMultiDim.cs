using System;
using System.Globalization;
using System.Linq;

namespace AmosLikeBasic;

/// <summary>
/// Interface för alla AMOS-arrayer (1D och multi-dim)
/// </summary>
public interface IAmosArray
{
    int Length { get; }
    int[] Dimensions { get; }
    object Get(params int[] indices);
    void Set(object value, params int[] indices);
    int CalculateIndex(params int[] indices);
}

/// <summary>
/// Numerisk array med stöd för multidimensionella index
/// Exempel: DIM A(10,20,5) - 3D array
/// </summary>
public sealed class AmosNumericArray : IAmosArray
{
    public double[] Data { get; }
    public int[] Dimensions { get; }
    
    /// <summary>
    /// Skapa en multidimensionell numerisk array
    /// </summary>
    /// <param name="dimensions">Array av dimensioner, t.ex. [10, 20] för 10x20</param>
    public AmosNumericArray(params int[] dimensions)
    {
        if (dimensions == null || dimensions.Length == 0)
            throw new ArgumentException("Array måste ha minst en dimension");
            
        Dimensions = dimensions.Select(d => d).ToArray(); // +1 för AMOS 0-indexering (0 till N inklusivt)
        
        // Beräkna total storlek
        int totalSize = 1;
        foreach (int dim in Dimensions)
            totalSize *= dim;
            
        Data = new double[totalSize];
    }
    
    public int Length => Data.Length;
    
    /// <summary>
    /// Beräkna flat index från multidimensionella koordinater
    /// Använder row-major order: index = z + y*depth + x*width*depth
    /// </summary>
    public int CalculateIndex(params int[] indices)
    {
        if (indices.Length != Dimensions.Length)
            throw new IndexOutOfRangeException(
                $"Fel antal dimensioner: förväntade {Dimensions.Length}, fick {indices.Length}");
        
        int index = 0;
        int multiplier = 1;
        
        // Bygg index bakifrån (row-major order)
        for (int i = indices.Length - 1; i >= 0; i--)
        {
            if (indices[i] < 0 || indices[i] >= Dimensions[i])
                throw new IndexOutOfRangeException(
                    $"Index {i} utanför gränserna: {indices[i]} (max: {Dimensions[i] - 1})");
                
            index += indices[i] * multiplier;
            multiplier *= Dimensions[i];
        }
        
        return index;
    }
    
    public object Get(params int[] indices)
    {
        return Data[CalculateIndex(indices)];
    }
    
    public void Set(object value, params int[] indices)
    {
        Data[CalculateIndex(indices)] = Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }
    
    public override string ToString()
    {
        var dimStr = string.Join("×", Dimensions.Select(d => d - 1)); // -1 för att visa användbar storlek
        var content = string.Join(", ", Data.Take(5).Select(d => d.ToString("G5", CultureInfo.InvariantCulture)));
        if (Data.Length > 5) content += "...";
        return $"Array({dimStr}) [{content}]";
    }
}

/// <summary>
/// Sträng-array med stöd för multidimensionella index
/// Exempel: DIM A$(5,10,3) - 3D sträng-array
/// </summary>
public sealed class AmosStringArray : IAmosArray
{
    public string[] Data { get; }
    public int[] Dimensions { get; }
    
    public AmosStringArray(params int[] dimensions)
    {
        if (dimensions == null || dimensions.Length == 0)
            throw new ArgumentException("Array måste ha minst en dimension");
            
        Dimensions = dimensions.Select(d => d).ToArray();
        
        int totalSize = 1;
        foreach (int dim in Dimensions)
            totalSize *= dim;
            
        Data = new string[totalSize];
    }
    
    public int Length => Data.Length;
    
    public int CalculateIndex(params int[] indices)
    {
        if (indices.Length != Dimensions.Length)
            throw new IndexOutOfRangeException(
                $"Fel antal dimensioner: förväntade {Dimensions.Length}, fick {indices.Length}");
        
        int index = 0;
        int multiplier = 1;
        
        for (int i = indices.Length - 1; i >= 0; i--)
        {
            if (indices[i] < 0 || indices[i] >= Dimensions[i])
                throw new IndexOutOfRangeException(
                    $"Index {i} utanför gränserna: {indices[i]} (max: {Dimensions[i] - 1})");
                
            index += indices[i] * multiplier;
            multiplier *= Dimensions[i];
        }
        
        return index;
    }
    
    public object Get(params int[] indices)
    {
        return Data[CalculateIndex(indices)] ?? "";
    }
    
    public void Set(object value, params int[] indices)
    {
        Data[CalculateIndex(indices)] = value?.ToString() ?? "";
    }
    
    public override string ToString()
    {
        var dimStr = string.Join("×", Dimensions.Select(d => d - 1));
        var content = string.Join(", ", Data.Take(5).Select(s => "\"" + (s ?? "") + "\""));
        if (Data.Length > 5) content += "...";
        return $"Array$({dimStr}) [{content}]";
    }
}