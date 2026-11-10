using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Raffica.Pdf.Platform.MacOS;

/// <summary>
/// LaunchServices through its C API (no Objective-C runtime). <c>LSSetDefaultRoleHandlerForContentType</c> is deprecated
/// since macOS 12; that it still works on macOS 15 is an assumption the owner's check must confirm. Every created
/// <c>CFString</c> and every copied handler is released, also when a call throws.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class LaunchServicesNative : ILaunchServices
{
    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint RolesAll = 0xFFFFFFFF;
    private const uint Utf8 = 0x08000100;

    public string? DefaultHandlerOf(string contentType)
    {
        var type = CreateString(contentType);
        try
        {
            var handler = LSCopyDefaultRoleHandlerForContentType(type, RolesAll);
            if (handler == 0)
            {
                return null;
            }

            try
            {
                return ReadString(handler);
            }
            finally
            {
                CFRelease(handler);
            }
        }
        finally
        {
            CFRelease(type);
        }
    }

    public int SetDefaultHandler(string contentType, string bundleId)
    {
        var type = CreateString(contentType);
        try
        {
            var bundle = CreateString(bundleId);
            try
            {
                return LSSetDefaultRoleHandlerForContentType(type, RolesAll, bundle);
            }
            finally
            {
                CFRelease(bundle);
            }
        }
        finally
        {
            CFRelease(type);
        }
    }

    private static nint CreateString(string value)
    {
        var result = CFStringCreateWithCString(0, value, Utf8);
        if (result == 0)
        {
            throw new InvalidOperationException("CFStringCreateWithCString returned nil.");
        }

        return result;
    }

    private static string? ReadString(nint cfString)
    {
        var buffer = new byte[1024];
        if (!CFStringGetCString(cfString, buffer, buffer.Length, Utf8))
        {
            return null;
        }

        var end = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, end >= 0 ? end : buffer.Length);
    }

    [LibraryImport(CoreServices)]
    private static partial nint LSCopyDefaultRoleHandlerForContentType(nint contentType, uint roles);

    [LibraryImport(CoreServices)]
    private static partial int LSSetDefaultRoleHandlerForContentType(nint contentType, uint roles, nint handler);

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string value, uint encoding);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(nint theString, [Out] byte[] buffer, nint bufferSize, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint cf);
}
