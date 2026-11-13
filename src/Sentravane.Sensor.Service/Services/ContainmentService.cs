using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Sentravane.Sensor.Service.Native;
using Sentravane.Shared.Models;

namespace Sentravane.Sensor.Service.Services;

public interface IContainmentService
{
    ContainmentResult SuspendProcessTree(int rootPid, string reason);
    bool IsolateEndpointNetwork();
    bool RestoreEndpointNetwork();
}

public record ContainmentResult(bool Success, List<int> SuspendedPids, string Message);

public class ContainmentService : IContainmentService, IDisposable
{
    private readonly ILogger<ContainmentService> _logger;
    private IntPtr _wfpEngineHandle = IntPtr.Zero;
    private readonly List<ulong> _activeFilterIds = new();

    public ContainmentService(ILogger<ContainmentService> logger)
    {
        _logger = logger;
    }

    public ContainmentResult SuspendProcessTree(int rootPid, string reason)
    {
        _logger.LogWarning("Initiating atomic suspension for process tree rooted at PID {Pid}. Reason: {Reason}", rootPid, reason);

        var pidsToSuspend = new List<int>();
        try
        {
            var tree = BuildProcessTree(rootPid);
            pidsToSuspend.AddRange(tree);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fully enumerate process tree for PID {Pid}", rootPid);
            pidsToSuspend.Add(rootPid);
        }

        var successfullySuspended = new List<int>();
        foreach (var pid in pidsToSuspend)
        {
            IntPtr hProcess = Kernel32Native.OpenProcess(Kernel32Native.PROCESS_SUSPEND_RESUME, false, (uint)pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    int status = Ntdll.NtSuspendProcess(hProcess);
                    if (status == 0)
                    {
                        successfullySuspended.Add(pid);
                        _logger.LogInformation("Successfully suspended PID {Pid}", pid);
                    }
                    else
                    {
                        _logger.LogError("NtSuspendProcess failed on PID {Pid} with NTSTATUS {Status}", pid, status);
                    }
                }
                finally
                {
                    Kernel32Native.CloseHandle(hProcess);
                }
            }
            else
            {
                _logger.LogWarning("Failed to open process handle for PID {Pid}. Error: {Error}", pid, Marshal.GetLastWin32Error());
            }
        }

        return new ContainmentResult(successfullySuspended.Count > 0, successfullySuspended, $"Suspended {successfullySuspended.Count}/{pidsToSuspend.Count} processes in tree");
    }

    private static List<int> BuildProcessTree(int rootPid)
    {
        var result = new List<int> { rootPid };
        var parentChildMap = new Dictionary<int, List<int>>();

        IntPtr snapshot = Kernel32Native.CreateToolhelp32Snapshot(Kernel32Native.TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return result;
        }

        try
        {
            var entry = new Kernel32Native.PROCESSENTRY32();
            entry.dwSize = (uint)Marshal.SizeOf(typeof(Kernel32Native.PROCESSENTRY32));

            if (Kernel32Native.Process32First(snapshot, ref entry))
            {
                do
                {
                    int parentId = (int)entry.th32ParentProcessID;
                    int procId = (int)entry.th32ProcessID;

                    if (!parentChildMap.ContainsKey(parentId))
                    {
                        parentChildMap[parentId] = new List<int>();
                    }
                    parentChildMap[parentId].Add(procId);
                }
                while (Kernel32Native.Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            Kernel32Native.CloseHandle(snapshot);
        }

        var queue = new Queue<int>();
        queue.Enqueue(rootPid);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (parentChildMap.TryGetValue(current, out var children))
            {
                foreach (var child in children)
                {
                    if (!result.Contains(child))
                    {
                        result.Add(child);
                        queue.Enqueue(child);
                    }
                }
            }
        }

        return result;
    }

    public bool IsolateEndpointNetwork()
    {
        _logger.LogWarning("Activating Windows Filtering Platform ephemeral dynamic network quarantine");

        lock (_activeFilterIds)
        {
            if (_wfpEngineHandle != IntPtr.Zero)
            {
                _logger.LogInformation("WFP dynamic quarantine is already active");
                return true;
            }

            var session = new WfpNative.FWPM_SESSION0
            {
                flags = WfpNative.FWPM_SESSION_FLAG_DYNAMIC,
                displayData = new WfpNative.FWPM_DISPLAY_DATA0
                {
                    name = "Sentravane Dynamic Quarantine Session",
                    description = "Dynamic block-all network quarantine session during active security incident"
                }
            };

            uint result = WfpNative.FwpmEngineOpen0(null, WfpNative.RPC_C_AUTHN_WINNT, IntPtr.Zero, ref session, out _wfpEngineHandle);
            if (result != 0)
            {
                _logger.LogError("Failed to open WFP engine. Error code: {Error}", result);
                return false;
            }

            try
            {
                var filterV4 = new WfpNative.FWPM_FILTER0
                {
                    layerKey = WfpNative.FWPM_LAYER_ALE_AUTH_CONNECT_V4,
                    displayData = new WfpNative.FWPM_DISPLAY_DATA0
                    {
                        name = "Sentravane Ephemeral IPv4 Block",
                        description = "Blocks all outbound IPv4 connections during quarantine"
                    },
                    action = new WfpNative.FWPM_ACTION0
                    {
                        type = WfpNative.FWP_ACTION_BLOCK
                    },
                    weight = new WfpNative.FWP_VALUE0
                    {
                        type = 0,
                        value = 0xFFFFFFFFFFFFFFFF
                    }
                };

                uint addV4Res = WfpNative.FwpmFilterAdd0(_wfpEngineHandle, ref filterV4, IntPtr.Zero, out ulong filterIdV4);
                if (addV4Res == 0)
                {
                    _activeFilterIds.Add(filterIdV4);
                }

                var filterV6 = new WfpNative.FWPM_FILTER0
                {
                    layerKey = WfpNative.FWPM_LAYER_ALE_AUTH_CONNECT_V6,
                    displayData = new WfpNative.FWPM_DISPLAY_DATA0
                    {
                        name = "Sentravane Ephemeral IPv6 Block",
                        description = "Blocks all outbound IPv6 connections during quarantine"
                    },
                    action = new WfpNative.FWPM_ACTION0
                    {
                        type = WfpNative.FWP_ACTION_BLOCK
                    },
                    weight = new WfpNative.FWP_VALUE0
                    {
                        type = 0,
                        value = 0xFFFFFFFFFFFFFFFF
                    }
                };

                uint addV6Res = WfpNative.FwpmFilterAdd0(_wfpEngineHandle, ref filterV6, IntPtr.Zero, out ulong filterIdV6);
                if (addV6Res == 0)
                {
                    _activeFilterIds.Add(filterIdV6);
                }

                _logger.LogInformation("Dynamic WFP network quarantine established with {Count} filter layers", _activeFilterIds.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception setting up WFP quarantine filters");
                RestoreEndpointNetwork();
                return false;
            }
        }
    }

    public bool RestoreEndpointNetwork()
    {
        _logger.LogInformation("Releasing dynamic network quarantine filters");

        lock (_activeFilterIds)
        {
            if (_wfpEngineHandle != IntPtr.Zero)
            {
                try
                {
                    foreach (var filterId in _activeFilterIds)
                    {
                        WfpNative.FwpmFilterDeleteById0(_wfpEngineHandle, filterId);
                    }
                    _activeFilterIds.Clear();
                    WfpNative.FwpmEngineClose0(_wfpEngineHandle);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing WFP quarantine session");
                }
                finally
                {
                    _wfpEngineHandle = IntPtr.Zero;
                }
            }
            return true;
        }
    }

    public void Dispose()
    {
        RestoreEndpointNetwork();
    }
}
