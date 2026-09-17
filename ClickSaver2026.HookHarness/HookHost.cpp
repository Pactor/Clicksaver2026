// SPDX-License-Identifier: GPL-3.0-or-later
//
// Stand-in for the game client. Calls DataBlockToMessage every 20 ms with a 64-byte block
// "HookHost" + call number - or, given a file, with that file's bytes - and checks every call
// still reaches MessageProtocol.dll, before, during and after the hook. Exits 0 when stdin
// closes, 2 if a call went missing, 3 after two minutes, 4 if the file cannot be read.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>

class Message_t;
__declspec(dllimport) Message_t* DataBlockToMessage(unsigned int size, void* data);

namespace
{
    std::atomic<bool> g_stdinClosed{ false };

    DWORD WINAPI WatchStdin(LPVOID)
    {
        char buffer[64];
        DWORD read = 0;
        while (ReadFile(GetStdHandle(STD_INPUT_HANDLE), buffer, sizeof buffer, &read, nullptr) && read != 0)
        {
        }
        g_stdinClosed.store(true);
        return 0;
    }
}

int main(int argc, char** argv)
{
    std::vector<std::uint8_t> file;
    if (argc > 1)
    {
        std::ifstream input(argv[1], std::ios::binary);
        file.assign(std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>());
        if (file.empty())
        {
            return 4;
        }
    }

    CloseHandle(CreateThread(nullptr, 0, WatchStdin, nullptr, 0, nullptr));
    std::printf("ready %lu\n", GetCurrentProcessId());
    std::fflush(stdout);

    for (std::uint32_t call = 1; call <= 6000 && !g_stdinClosed.load(); ++call)
    {
        std::uint8_t numbered[64]{};
        std::memcpy(numbered, "HookHost", 8);
        std::memcpy(numbered + 8, &call, sizeof call);

        std::uint8_t* block = file.empty() ? numbered : file.data();
        auto size = static_cast<unsigned int>(file.empty() ? sizeof numbered : file.size());
        auto result = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(DataBlockToMessage(size, block)));
        if (result != call)
        {
            std::printf("call %u returned %u\n", call, result);
            return 2;
        }
        Sleep(20);
    }
    return g_stdinClosed.load() ? 0 : 3;
}
