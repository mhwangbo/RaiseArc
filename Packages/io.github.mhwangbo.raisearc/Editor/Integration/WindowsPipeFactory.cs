using System;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PrincessStudio.Editor
{
    /// <summary>Unity Mono omits WindowsIdentity.User; create the owner-only ACL through supported Win32 APIs.</summary>
    internal static class WindowsPipeFactory
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            internal int length;
            internal IntPtr descriptor;
            internal int inherit;
        }
        internal static NamedPipeServerStream Create(string name)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("The named-pipe MCP host currently supports Windows Editor only.");
            var sddl = "D:P(A;;GA;;;" + CurrentSid() + ")";
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var security = new SecurityAttributes { length = Marshal.SizeOf<SecurityAttributes>(), descriptor = descriptor };
                // Duplex, overlapped, first instance; reject remote clients. ACL grants only the process owner.
                var native = CreateNamedPipe("\\\\.\\pipe\\" + name, 0x40080003, 0x8, 1, 4096, 4096, 5000, ref security);
                if (native == new IntPtr(-1))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var handle = new SafePipeHandle(native, true);
                try
                {
                    return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
                }
                catch { handle.Dispose(); throw; }
            }
            finally { LocalFree(descriptor); }
        }
        private static string CurrentSid()
        {
            if (!OpenProcessToken(GetCurrentProcess(), 8, out var token))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                GetTokenInformation(token, 1, IntPtr.Zero, 0, out var length);
                if (length <= 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var buffer = Marshal.AllocHGlobal(length);
                try
                {
                    if (!GetTokenInformation(token, 1, buffer, length, out _))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (!ConvertSidToStringSid(Marshal.ReadIntPtr(buffer), out var text))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    try
                    {
                        return Marshal.PtrToStringUni(text);
                    }
                    finally { LocalFree(text); }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { CloseHandle(token); }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateNamedPipeW")]
        private static extern IntPtr CreateNamedPipe(string name, uint openMode, uint pipeMode, uint instances, uint outBuffer, uint inBuffer, uint timeout, ref SecurityAttributes security);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW")]
        private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr buffer, int size, out int requiredSize);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertSidToStringSidW")]
        private static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr text);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    }
}
