#if !DISABLESTEAMWORKS
using Steamworks;
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Mirror.FizzySteam
{
    public abstract class NextCommon
    {
        protected const int MAX_MESSAGES = 256;
        protected readonly IntPtr[] messagePointers = new IntPtr[MAX_MESSAGES];

        protected EResult SendSocket(HSteamNetConnection conn, ArraySegment<byte> segment, int channelId)
        {
            byte[] data = ArrayPool<byte>.Shared.Rent(segment.Count + 1);
            GCHandle pinnedArray = default;
            try
            {
                Buffer.BlockCopy(segment.Array, segment.Offset, data, 0, segment.Count);
                data[segment.Count] = (byte)channelId;
                pinnedArray = GCHandle.Alloc(data, GCHandleType.Pinned);
                IntPtr pData = pinnedArray.AddrOfPinnedObject();
                int sendFlag = channelId == Channels.Unreliable ? Constants.k_nSteamNetworkingSend_Unreliable : Constants.k_nSteamNetworkingSend_Reliable;
                return SteamTransportDiagnostics.SendObserved(conn, segment, channelId, pData, sendFlag, NativeSend);
            }
            finally
            {
                if (pinnedArray.IsAllocated) pinnedArray.Free();
                ArrayPool<byte>.Shared.Return(data);
            }
        }

        private static EResult NativeSend(HSteamNetConnection connection, IntPtr data, uint bytes, int flags, out long messageNumber)
        {
#if UNITY_SERVER
            return SteamGameServerNetworkingSockets.SendMessageToConnection(connection, data, bytes, flags, out messageNumber);
#else
            return SteamNetworkingSockets.SendMessageToConnection(connection, data, bytes, flags, out messageNumber);
#endif
        }

        protected (ArraySegment<byte>, int) ProcessMessage(IntPtr pointer)
        {
            byte[] buffer = null;
            try
            {
                SteamNetworkingMessage_t data = Marshal.PtrToStructure<SteamNetworkingMessage_t>(pointer);
                if (data.m_cbSize < 2 || data.m_cbSize > Constants.k_cbMaxSteamNetworkingSocketsMessageSizeSend)
                    return (default, 0);
                buffer = ArrayPool<byte>.Shared.Rent(data.m_cbSize);
                Marshal.Copy(data.m_pData, buffer, 0, data.m_cbSize);
                if (data.m_idxLane == 1)
                { var evidence = new ArraySegment<byte>(buffer, 0, data.m_cbSize); buffer = null; return (evidence, 2); }
                if (data.m_idxLane != 0) return (default, 0);
                int channel = buffer[data.m_cbSize - 1];
                if (channel != Channels.Reliable && channel != Channels.Unreliable) return (default, 0);
                var result = new ArraySegment<byte>(buffer, 0, data.m_cbSize - 1);
                SteamTransportDiagnostics.ObserveReceive(data, result, channel);
                buffer = null; // The receiver returns this after the callback or deferred delivery.
                SteamTransportDiagnostics.RecordReceive(data.m_cbSize, channel);
                return (result, channel);
            }
            finally
            {
                if (buffer != null) ArrayPool<byte>.Shared.Return(buffer);
                SteamNetworkingMessage_t.Release(pointer);
            }
        }

        protected static void ReturnMessage(ArraySegment<byte> data)
        { if (data.Array != null) ArrayPool<byte>.Shared.Return(data.Array); }

        protected void ReleasePendingMessages(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (messagePointers[i] != IntPtr.Zero) SteamNetworkingMessage_t.Release(messagePointers[i]);
                messagePointers[i] = IntPtr.Zero;
            }
        }
    }
}
#endif // !DISABLESTEAMWORKS
