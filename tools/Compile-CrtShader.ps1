$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class CrtShaderCompiler {
    [DllImport("d3dcompiler_47.dll", CallingConvention=CallingConvention.StdCall, CharSet=CharSet.Ansi)]
    static extern int D3DCompile(byte[] source, UIntPtr size, string name, IntPtr defines, IntPtr include,
        string entry, string target, uint flags, uint flags2, out IntPtr code, out IntPtr errors);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr BufferPointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate UIntPtr BufferSize(IntPtr self);
    static byte[] Bytes(IntPtr blob) {
        var table = Marshal.ReadIntPtr(blob);
        var pointer = (BufferPointer)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, 3 * IntPtr.Size), typeof(BufferPointer));
        var size = (BufferSize)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, 4 * IntPtr.Size), typeof(BufferSize));
        var bytes = new byte[checked((int)size(blob).ToUInt64())];
        Marshal.Copy(pointer(blob), bytes, 0, bytes.Length); return bytes;
    }
    public static void Compile(string sourcePath, string destination) {
        byte[] source = File.ReadAllBytes(sourcePath);
        IntPtr code = IntPtr.Zero, errors = IntPtr.Zero;
        try {
            int result = D3DCompile(source, (UIntPtr)source.Length, sourcePath, IntPtr.Zero, IntPtr.Zero,
                "main", "ps_2_0", (1u << 12) | (1u << 15), 0, out code, out errors);
            if (result < 0) throw new InvalidOperationException(errors == IntPtr.Zero ? "D3DCompile failed" : System.Text.Encoding.UTF8.GetString(Bytes(errors)));
            File.WriteAllBytes(destination, Bytes(code));
        } finally { if (code != IntPtr.Zero) Marshal.Release(code); if (errors != IntPtr.Zero) Marshal.Release(errors); }
    }
}
'@
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets'
[CrtShaderCompiler]::Compile((Join-Path $assets 'CrtDisplay.hlsl'), (Join-Path $assets 'CrtDisplay.ps'))
Write-Output 'Compiled Assets/CrtDisplay.ps from HLSL using the Windows shader compiler.'
