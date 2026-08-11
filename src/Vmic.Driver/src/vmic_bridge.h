/*
 * vmic_bridge.h
 *
 * Vmic render->capture bridge. A fixed-size, non-paged ring buffer shared by the
 * render stream (producer) and the capture stream (consumer) of the SYSVAD-based
 * virtual device. The Host app plays the mixed audio into the render endpoint;
 * this bridge carries it to the capture endpoint, which other applications read
 * as a microphone. Underrun reads return silence.
 *
 * This file is self-contained and is added to the SYSVAD sample (see README.md).
 */

#pragma once

// Pool tag: 'ViMc' reversed for little-endian display in poolmon.
#define VMIC_BRIDGE_POOL_TAG 'cMiV'

// Default ring size: ~1.5 s of 48 kHz / 16-bit / stereo audio. Generous enough to
// absorb the app's buffering without adding noticeable latency.
#define VMIC_BRIDGE_DEFAULT_SIZE (48000 * 2 * 2 * 3 / 2)   // ~288 KB

typedef struct _VMIC_BRIDGE
{
    KSPIN_LOCK  Lock;          // guards the cursor fields
    PUCHAR      Buffer;        // non-paged ring storage
    ULONG       BufferSize;    // capacity in bytes
    ULONG       WriteOffset;   // next byte to write
    ULONG       ReadOffset;    // next byte to read
    ULONG       UsedBytes;     // bytes currently available to read
    BOOLEAN     Initialized;
} VMIC_BRIDGE, *PVMIC_BRIDGE;

// Allocates the ring. Call once from the adapter's Init.
NTSTATUS
VmicBridgeInit(
    _Inout_ PVMIC_BRIDGE Bridge,
    _In_    ULONG        BufferSize);

// Frees the ring. Call from the adapter's cleanup.
VOID
VmicBridgeDestroy(
    _Inout_ PVMIC_BRIDGE Bridge);

// Producer (render stream): copies samples into the ring, dropping the oldest
// data if the consumer falls behind. Never blocks.
VOID
VmicBridgeWrite(
    _Inout_                PVMIC_BRIDGE Bridge,
    _In_reads_(Size) const UCHAR*       Data,
    _In_                   ULONG        Size);

// Consumer (capture stream): copies samples out of the ring, padding with silence
// when the producer hasn't supplied enough (underrun). Never blocks.
VOID
VmicBridgeRead(
    _Inout_                 PVMIC_BRIDGE Bridge,
    _Out_writes_(Size)      UCHAR*       Data,
    _In_                    ULONG        Size);

// Single instance shared by both streams; defined in vmic_bridge.cpp.
extern VMIC_BRIDGE g_VmicBridge;
