// SPDX-License-Identifier: GPL-3.0-or-later
//
// Stand-in for the client's MessageProtocol.dll. Exports DataBlockToMessage under the
// client's exact decorated name: ?DataBlockToMessage@@YAPAVMessage_t@@IPAX@Z.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <atomic>
#include <cstdint>

class Message_t;

namespace
{
    std::atomic<unsigned int> g_calls{ 0 };
}

// Not a real message: the call count, cast, so the host can check every call arrived here.
__declspec(dllexport) Message_t* DataBlockToMessage(unsigned int size, void* data)
{
    static_cast<void>(size);
    static_cast<void>(data);
    return reinterpret_cast<Message_t*>(static_cast<std::uintptr_t>(++g_calls));
}
