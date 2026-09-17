// SPDX-License-Identifier: GPL-3.0-or-later
//
// The wire format between the hook DLL and the ClickSaver2026 app.
// Mirrored in ClickSaver2026.Core/Hook/HookProtocol.cs - change both together.

#pragma once

#include <cstdint>

namespace cs
{
    inline constexpr wchar_t PipeName[] = L"ClickSaver2026";

    // Set in the client's environment to use another pipe name; the tests use it to stay
    // apart from a running app.
    inline constexpr wchar_t PipeNameVariable[] = L"CLICKSAVER2026_PIPE";

    inline constexpr std::uint32_t HelloMagic = 0x36325343; // "CS26" read little-endian
    inline constexpr std::uint16_t ProtocolVersion = 1;
    inline constexpr std::uint32_t HookVersion = 1;

    // Anything larger is not forwarded.
    inline constexpr std::uint32_t MaxMessageSize = 4 * 1024 * 1024;

    enum class HookStatus : std::uint16_t
    {
        Hooked = 0,
        MessageProtocolNotLoaded = 1,
        ExportNotFound = 2,
        DetourFailed = 3,
    };

    enum class FrameKind : std::uint32_t
    {
        // Payload: the data block exactly as the client passed it to DataBlockToMessage.
        IncomingMessage = 1,
        // Payload: uint32 count of messages the hook discarded because the app fell behind.
        Dropped = 2,
    };

#pragma pack(push, 1)
    // Sent once, as soon as the hook connects.
    struct Hello
    {
        std::uint32_t magic;
        std::uint16_t protocolVersion;
        std::uint16_t status;
        std::uint32_t processId;
        std::uint32_t hookVersion;
        std::uint32_t reserved;
    };

    // Precedes every payload.
    struct FrameHeader
    {
        std::uint32_t kind;
        std::uint32_t length;
        std::int64_t timestamp; // FILETIME, UTC
    };
#pragma pack(pop)

    static_assert(sizeof(Hello) == 20);
    static_assert(sizeof(FrameHeader) == 16);
}
