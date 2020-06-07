using Nito.AsyncEx;
using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace AdvancedRpcLib.Channels.NamedPipe
{
#if NETSTANDARD
    partial class NamedPipeRpcServerChannel 
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public unsafe byte* lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [Flags]
        enum PipeOpenModeFlags : uint
        {
            PIPE_ACCESS_DUPLEX = 0x00000003,
            PIPE_ACCESS_INBOUND = 0x00000001,
            PIPE_ACCESS_OUTBOUND = 0x00000002,
            FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000,
            FILE_FLAG_WRITE_THROUGH = 0x80000000,
            FILE_FLAG_OVERLAPPED = 0x40000000,
            WRITE_DAC = (uint)0x00040000L,
            WRITE_OWNER = (uint)0x00080000L,
            ACCESS_SYSTEM_SECURITY = (uint)0x01000000L
        }

        [Flags]
        enum PipeModeFlags : uint
        {
            //One of the following type modes can be specified. The same type mode must be specified for each instance of the pipe.
            PIPE_TYPE_BYTE = 0x00000000,
            PIPE_TYPE_MESSAGE = 0x00000004,
            //One of the following read modes can be specified. Different instances of the same pipe can specify different read modes
            PIPE_READMODE_BYTE = 0x00000000,
            PIPE_READMODE_MESSAGE = 0x00000002,
            //One of the following wait modes can be specified. Different instances of the same pipe can specify different wait modes.
            PIPE_WAIT = 0x00000000,
            PIPE_NOWAIT = 0x00000001,
            //One of the following remote-client modes can be specified. Different instances of the same pipe can specify different remote-client modes.
            PIPE_ACCEPT_REMOTE_CLIENTS = 0x00000000,
            PIPE_REJECT_REMOTE_CLIENTS = 0x00000008
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern SafePipeHandle CreateNamedPipe(string lpName, PipeOpenModeFlags dwOpenMode,
            PipeModeFlags dwPipeMode, uint nMaxInstances, uint nOutBufferSize, uint nInBufferSize,
            uint nDefaultTimeOut, SECURITY_ATTRIBUTES lpSecurityAttributes);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern SafePipeHandle CreateNamedPipe(string lpName, PipeOpenModeFlags dwOpenMode,
            PipeModeFlags dwPipeMode, uint nMaxInstances, uint nOutBufferSize, uint nInBufferSize,
            uint nDefaultTimeOut, IntPtr lpSecurityAttributes);

        private NamedPipeServerStream CreatePipe(string pipeName, PipeSecurity pipeSecurity)
        {
            if (pipeSecurity == null)
            {
                return new NamedPipeServerStream(_pipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }

            // some more work needed for .NET Core - works on Windows only!
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                throw new NotSupportedException("Named pipe security not supported on non Windows systems.");
            }

            

            var secAttr = GetSecAttrs(pipeSecurity, out var pinningHandle);

            try
            {
                var pipeHandle = CreateNamedPipe(Path.GetFullPath(@"\\.\pipe\" + pipeName),
                    PipeOpenModeFlags.PIPE_ACCESS_DUPLEX | PipeOpenModeFlags.FILE_FLAG_OVERLAPPED,
                    PipeModeFlags.PIPE_TYPE_BYTE | PipeModeFlags.PIPE_WAIT | PipeModeFlags.PIPE_ACCEPT_REMOTE_CLIENTS | PipeModeFlags.PIPE_READMODE_BYTE,
                    255,
                    0, 0, 0, secAttr);

                if (pipeHandle.IsInvalid)
                {
                    throw new IOException("Failed to create pipe.", Marshal.GetLastWin32Error());
                }

                return new NamedPipeServerStream(PipeDirection.InOut, true, false, pipeHandle);
            }
            finally
            {
                if (pinningHandle.IsAllocated)
                {
                    pinningHandle.Free();
                }
            }

        }
            
        private static unsafe SECURITY_ATTRIBUTES GetSecAttrs(PipeSecurity pipeSecurity, out GCHandle pinningHandle)
        {
            SECURITY_ATTRIBUTES secAttrs = new SECURITY_ATTRIBUTES();
            secAttrs.nLength = Marshal.SizeOf(secAttrs);
            byte[] sd = pipeSecurity.GetSecurityDescriptorBinaryForm();
            pinningHandle = GCHandle.Alloc(sd, GCHandleType.Pinned);
            fixed (byte* pSecDescriptor = sd)
                secAttrs.lpSecurityDescriptor = pSecDescriptor;
            return secAttrs;
        }
    }

#endif
}
