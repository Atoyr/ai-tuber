using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Graphics.DirectX.Direct3D11;

namespace Medoz.GameCommentary;

/// <summary>
/// <see cref="WgcWindowCapture"/> が必要とする WinRT / Direct3D の interop。
/// WGC のフレームプールは IDirect3DDevice を要求するが、C# の projection からは直接作れないため
/// D3D11CreateDevice → IDXGIDevice → CreateDirect3D11DeviceFromDXGIDevice の経路を通す。
/// </summary>
[SupportedOSPlatform("windows10.0.19041")]
internal static class Interop
{
    /// <summary>WinRT のアクティベーションファクトリを指定 IID で取得する。</summary>
    public static object GetActivationFactory(string activatableClassId, Guid iid)
    {
        int hr = WindowsCreateString(activatableClassId, activatableClassId.Length, out IntPtr classId);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            hr = RoGetActivationFactory(classId, ref iid, out IntPtr factory);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                return Marshal.GetObjectForIUnknown(factory);
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        finally
        {
            WindowsDeleteString(classId);
        }
    }

    /// <summary>WGC のフレームプール用に D3D11 デバイスを作る。</summary>
    public static IDirect3DDevice CreateDirect3DDevice()
    {
        const int D3D_DRIVER_TYPE_HARDWARE = 1;
        const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20; // WGC は BGRA が必須
        const uint D3D11_SDK_VERSION = 7;

        int hr = D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, D3D11_SDK_VERSION,
            out IntPtr d3dDevice, out _, out IntPtr context);
        Marshal.ThrowExceptionForHR(hr);
        if (context != IntPtr.Zero)
        {
            Marshal.Release(context); // 即時コンテキストは使わない
        }

        try
        {
            Guid dxgiDeviceIid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice
            hr = Marshal.QueryInterface(d3dDevice, in dxgiDeviceIid, out IntPtr dxgiDevice);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out IntPtr inspectable);
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            Marshal.Release(d3dDevice);
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
        out IntPtr device, out int featureLevel, out IntPtr immediateContext);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
