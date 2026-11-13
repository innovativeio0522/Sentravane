using System;
using System.Runtime.InteropServices;

namespace Sentravane.Sensor.Service.Native;

public static class WfpNative
{
    public const uint RPC_C_AUTHN_WINNT = 10;
    public const uint RPC_C_AUTHZ_NONE = 0;

    public const uint FWPM_SESSION_FLAG_DYNAMIC = 0x00000001;
    public const uint FWP_ACTION_BLOCK = 0x00000001;
    public const uint FWP_ACTION_PERMIT = 0x00000002;

    public static readonly Guid FWPM_LAYER_ALE_AUTH_CONNECT_V4 = new("c38d57d1-05a7-4c33-904f-7fbab093c603");
    public static readonly Guid FWPM_LAYER_ALE_AUTH_CONNECT_V6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_SESSION0
    {
        public Guid sessionKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public uint txnWaitTimeoutInMSec;
        public uint processId;
        public IntPtr sid;
        public IntPtr username;
        public bool kernelMode;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FWPM_DISPLAY_DATA0
    {
        public string name;
        public string description;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_FILTER0
    {
        public Guid filterKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey;
        public FWP_BYTE_BLOB providerData;
        public Guid layerKey;
        public Guid subLayerKey;
        public FWP_VALUE0 weight;
        public uint numFilterConditions;
        public IntPtr filterCondition;
        public FWPM_ACTION0 action;
        public ulong context;
        public IntPtr reserved;
        public ulong filterId;
        public FWP_VALUE0 effectiveWeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_ACTION0
    {
        public uint type;
        public Guid filterType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWP_VALUE0
    {
        public uint type;
        public ulong value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWP_BYTE_BLOB
    {
        public uint size;
        public IntPtr data;
    }

    [DllImport("fwpuclnt.dll", EntryPoint = "FwpmEngineOpen0", ExactSpelling = true)]
    public static extern uint FwpmEngineOpen0(
        [MarshalAs(UnmanagedType.LPWStr)] string? serverName,
        uint authnService,
        IntPtr authIdentity,
        ref FWPM_SESSION0 session,
        out IntPtr engineHandle);

    [DllImport("fwpuclnt.dll", EntryPoint = "FwpmEngineClose0", ExactSpelling = true)]
    public static extern uint FwpmEngineClose0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll", EntryPoint = "FwpmFilterAdd0", ExactSpelling = true)]
    public static extern uint FwpmFilterAdd0(
        IntPtr engineHandle,
        ref FWPM_FILTER0 filter,
        IntPtr sd,
        out ulong id);

    [DllImport("fwpuclnt.dll", EntryPoint = "FwpmFilterDeleteById0", ExactSpelling = true)]
    public static extern uint FwpmFilterDeleteById0(IntPtr engineHandle, ulong id);
}

