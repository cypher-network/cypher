using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Toolkit.HighPerformance;

namespace CypherNetwork.Helper;

public sealed class Alloc: IDisposable
{
    public IntPtr Ptr { get; private set; }
    public int Length { get; private set; }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="size"></param>
    /// <returns></returns>
    public static Alloc Create(int size)
    {
        var ptr = Marshal.AllocHGlobal(size);
        return ptr == IntPtr.Zero ? null : new Alloc { Ptr = ptr, Length = size };
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="ptr"></param>
    /// <param name="size"></param>
    /// <returns></returns>
    public static Alloc Create(IntPtr ptr, int size)
    {
        return ptr == IntPtr.Zero ? null : new Alloc { Ptr = ptr, Length = size };
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public Span<byte> AsSpan()
    {
        unsafe
        {
            return new Span<byte>(Ptr.ToPointer(), Length);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public void Take()
    {
        Ptr = default;
        Length = default;
    }

    /// <summary>
    /// 
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="disposing"></param>
    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            try
            {
                if (Ptr != IntPtr.Zero) Marshal.FreeHGlobal(Ptr);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        _disposed = true;
    }

    bool _disposed;
}