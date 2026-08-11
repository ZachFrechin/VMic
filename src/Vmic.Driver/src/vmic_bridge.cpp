/*
 * vmic_bridge.cpp
 *
 * Implementation of the render->capture ring buffer. See vmic_bridge.h.
 */

#include <ntddk.h>
#include "vmic_bridge.h"

// The one shared bridge used by both the render and capture streams.
VMIC_BRIDGE g_VmicBridge = { 0 };

NTSTATUS
VmicBridgeInit(
    _Inout_ PVMIC_BRIDGE Bridge,
    _In_    ULONG        BufferSize)
{
    if (Bridge->Initialized)
        return STATUS_SUCCESS;

    KeInitializeSpinLock(&Bridge->Lock);

    // Keep the bridge compatible with the driver's Windows 10 1809 floor.
    // ExAllocatePool2 was introduced later; the pinned SYSVAD revision uses
    // the down-level non-executable pool API for the same reason.
    Bridge->Buffer = static_cast<PUCHAR>(
        ExAllocatePoolWithTag(NonPagedPoolNx, BufferSize, VMIC_BRIDGE_POOL_TAG));
    if (Bridge->Buffer == nullptr)
        return STATUS_INSUFFICIENT_RESOURCES;

    RtlZeroMemory(Bridge->Buffer, BufferSize);
    Bridge->BufferSize  = BufferSize;
    Bridge->WriteOffset = 0;
    Bridge->ReadOffset  = 0;
    Bridge->UsedBytes   = 0;
    Bridge->Initialized = TRUE;

    return STATUS_SUCCESS;
}

VOID
VmicBridgeDestroy(
    _Inout_ PVMIC_BRIDGE Bridge)
{
    if (Bridge->Buffer != nullptr)
    {
        ExFreePoolWithTag(Bridge->Buffer, VMIC_BRIDGE_POOL_TAG);
        Bridge->Buffer = nullptr;
    }
    Bridge->Initialized = FALSE;
    Bridge->BufferSize  = 0;
    Bridge->WriteOffset = 0;
    Bridge->ReadOffset  = 0;
    Bridge->UsedBytes   = 0;
}

VOID
VmicBridgeWrite(
    _Inout_                PVMIC_BRIDGE Bridge,
    _In_reads_(Size) const UCHAR*       Data,
    _In_                   ULONG        Size)
{
    if (!Bridge->Initialized || Size == 0)
        return;

    KLOCK_QUEUE_HANDLE handle;
    KeAcquireInStackQueuedSpinLock(&Bridge->Lock, &handle);

    // If the incoming chunk is larger than the whole ring, keep only the tail.
    if (Size >= Bridge->BufferSize)
    {
        Data += (Size - Bridge->BufferSize);
        Size  = Bridge->BufferSize;
    }

    // Make room: if writing Size bytes would overflow, advance the read cursor
    // (drop the oldest samples the consumer hasn't read yet).
    ULONG freeBytes = Bridge->BufferSize - Bridge->UsedBytes;
    if (Size > freeBytes)
    {
        ULONG drop = Size - freeBytes;
        Bridge->ReadOffset = (Bridge->ReadOffset + drop) % Bridge->BufferSize;
        Bridge->UsedBytes -= drop;
    }

    // Copy in up to two segments (wrap-around).
    ULONG first = min(Size, Bridge->BufferSize - Bridge->WriteOffset);
    RtlCopyMemory(Bridge->Buffer + Bridge->WriteOffset, Data, first);
    if (first < Size)
        RtlCopyMemory(Bridge->Buffer, Data + first, Size - first);

    Bridge->WriteOffset = (Bridge->WriteOffset + Size) % Bridge->BufferSize;
    Bridge->UsedBytes  += Size;

    KeReleaseInStackQueuedSpinLock(&handle);
}

VOID
VmicBridgeRead(
    _Inout_            PVMIC_BRIDGE Bridge,
    _Out_writes_(Size) UCHAR*       Data,
    _In_               ULONG        Size)
{
    if (Size == 0)
        return;

    if (!Bridge->Initialized)
    {
        RtlZeroMemory(Data, Size);
        return;
    }

    KLOCK_QUEUE_HANDLE handle;
    KeAcquireInStackQueuedSpinLock(&Bridge->Lock, &handle);

    ULONG toCopy = min(Size, Bridge->UsedBytes);

    // Copy out up to two segments (wrap-around).
    if (toCopy > 0)
    {
        ULONG first = min(toCopy, Bridge->BufferSize - Bridge->ReadOffset);
        RtlCopyMemory(Data, Bridge->Buffer + Bridge->ReadOffset, first);
        if (first < toCopy)
            RtlCopyMemory(Data + first, Bridge->Buffer, toCopy - first);

        Bridge->ReadOffset = (Bridge->ReadOffset + toCopy) % Bridge->BufferSize;
        Bridge->UsedBytes -= toCopy;
    }

    KeReleaseInStackQueuedSpinLock(&handle);

    // Underrun: pad the remainder with silence.
    if (toCopy < Size)
        RtlZeroMemory(Data + toCopy, Size - toCopy);
}
