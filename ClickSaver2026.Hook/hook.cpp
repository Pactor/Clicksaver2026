// SPDX-License-Identifier: GPL-3.0-or-later
//
// ClickSaver2026 hook. Loaded into the Anarchy Online client by the app; detours
// DataBlockToMessage in MessageProtocol.dll and forwards every block the client decodes
// to the app over a named pipe. All parsing happens in the app.
//
// The approach is ClickSaver's AOHook.dll (Copyright (C) 2002 Morb and later maintainers).

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <cstring>
#include <deque>
#include <mutex>
#include <string>
#include <vector>

#include <detours.h>

#include "protocol.h"

namespace
{
    using DataBlockToMessageFn = void* (__cdecl*)(unsigned int size, void* data);

    // Message_t* DataBlockToMessage(unsigned int, void*): turns one decompressed block from
    // the server into a message, so hooking it sees every message whatever the zone.
    constexpr char DataBlockToMessageExport[] = "?DataBlockToMessage@@YAPAVMessage_t@@IPAX@Z";

    // While the app falls behind, frames beyond this are dropped rather than queued.
    constexpr std::size_t MaxQueuedBytes = 64 * 1024 * 1024;

    HMODULE g_module = nullptr;
    std::wstring g_pipePath;
    HANDLE g_stopEvent = nullptr;
    HANDLE g_initThread = nullptr;
    HANDLE g_writerThread = nullptr;

    DataBlockToMessageFn g_original = nullptr;
    bool g_hooked = false;
    cs::HookStatus g_status = cs::HookStatus::MessageProtocolNotLoaded;

    std::atomic<bool> g_connected{ false };
    std::atomic<bool> g_stopping{ false };
    std::atomic<int> g_inFlight{ 0 };
    std::atomic<std::uint32_t> g_dropped{ 0 };

    std::mutex g_queueLock;
    std::condition_variable g_queueSignal;
    std::deque<std::vector<std::uint8_t>> g_queue;
    std::size_t g_queuedBytes = 0;

    std::int64_t Now()
    {
        FILETIME now;
        GetSystemTimePreciseAsFileTime(&now);
        return (static_cast<std::int64_t>(now.dwHighDateTime) << 32) | now.dwLowDateTime;
    }

    std::vector<std::uint8_t> MakeFrame(cs::FrameKind kind, const void* payload, std::uint32_t length)
    {
        std::vector<std::uint8_t> frame(sizeof(cs::FrameHeader) + length);
        const cs::FrameHeader header{ static_cast<std::uint32_t>(kind), length, Now() };
        std::memcpy(frame.data(), &header, sizeof header);
        if (length != 0)
        {
            std::memcpy(frame.data() + sizeof header, payload, length);
        }
        return frame;
    }

    // Runs on the client's own thread, so it only copies and queues.
    void Enqueue(unsigned int size, void* data)
    {
        try
        {
            auto frame = MakeFrame(cs::FrameKind::IncomingMessage, data, size);
            {
                std::lock_guard lock(g_queueLock);
                if (g_queuedBytes + frame.size() > MaxQueuedBytes)
                {
                    g_dropped.fetch_add(1, std::memory_order_relaxed);
                    return;
                }
                g_queuedBytes += frame.size();
                g_queue.push_back(std::move(frame));
            }
            g_queueSignal.notify_one();
        }
        catch (...)
        {
            g_dropped.fetch_add(1, std::memory_order_relaxed);
        }
    }

    void* __cdecl DataBlockToMessageHook(unsigned int size, void* data)
    {
        g_inFlight.fetch_add(1);
        if (g_connected.load(std::memory_order_relaxed) && data != nullptr && size != 0 && size <= cs::MaxMessageSize)
        {
            Enqueue(size, data);
        }
        void* message = g_original(size, data);
        g_inFlight.fetch_sub(1);
        return message;
    }

    bool WriteAll(HANDLE pipe, const std::uint8_t* data, std::size_t length)
    {
        while (length != 0)
        {
            DWORD written = 0;
            const auto chunk = static_cast<DWORD>(std::min<std::size_t>(length, 1 << 20));
            if (!WriteFile(pipe, data, chunk, &written, nullptr) || written == 0)
            {
                return false;
            }
            data += written;
            length -= written;
        }
        return true;
    }

    void Serve(HANDLE pipe)
    {
        const cs::Hello hello{
            cs::HelloMagic,
            cs::ProtocolVersion,
            static_cast<std::uint16_t>(g_status),
            GetCurrentProcessId(),
            cs::HookVersion,
            0,
        };
        if (!WriteAll(pipe, reinterpret_cast<const std::uint8_t*>(&hello), sizeof hello))
        {
            return;
        }

        g_connected.store(true);
        for (;;)
        {
            std::vector<std::uint8_t> frame;
            {
                std::unique_lock lock(g_queueLock);
                g_queueSignal.wait(lock, [] { return g_stopping.load() || !g_queue.empty(); });
                if (g_stopping.load())
                {
                    break;
                }
                frame = std::move(g_queue.front());
                g_queue.pop_front();
                g_queuedBytes -= frame.size();
            }

            if (const std::uint32_t dropped = g_dropped.exchange(0); dropped != 0)
            {
                const auto notice = MakeFrame(cs::FrameKind::Dropped, &dropped, sizeof dropped);
                if (!WriteAll(pipe, notice.data(), notice.size()))
                {
                    break;
                }
            }
            if (!WriteAll(pipe, frame.data(), frame.size()))
            {
                break;
            }
        }
        g_connected.store(false);

        std::lock_guard lock(g_queueLock);
        g_queue.clear();
        g_queuedBytes = 0;
    }

    // Keeps trying to reach the app, so the app can be started before or after the client.
    DWORD WINAPI WriterThread(LPVOID)
    {
        while (!g_stopping.load())
        {
            HANDLE pipe = CreateFileW(g_pipePath.c_str(), GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
            if (pipe == INVALID_HANDLE_VALUE)
            {
                WaitForSingleObject(g_stopEvent, 1000);
                continue;
            }
            Serve(pipe);
            CloseHandle(pipe);
        }
        return 0;
    }

    cs::HookStatus InstallHook()
    {
        // Loaded long before the login screen; the wait only matters when attached at client start.
        HMODULE messageProtocol = nullptr;
        for (int attempt = 0; attempt < 600; ++attempt)
        {
            messageProtocol = GetModuleHandleW(L"MessageProtocol.dll");
            if (messageProtocol != nullptr || WaitForSingleObject(g_stopEvent, 100) == WAIT_OBJECT_0)
            {
                break;
            }
        }
        if (messageProtocol == nullptr)
        {
            return cs::HookStatus::MessageProtocolNotLoaded;
        }

        g_original = reinterpret_cast<DataBlockToMessageFn>(GetProcAddress(messageProtocol, DataBlockToMessageExport));
        if (g_original == nullptr)
        {
            return cs::HookStatus::ExportNotFound;
        }

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        if (DetourAttach(reinterpret_cast<PVOID*>(&g_original), reinterpret_cast<PVOID>(DataBlockToMessageHook)) != NO_ERROR)
        {
            DetourTransactionAbort();
            return cs::HookStatus::DetourFailed;
        }
        if (DetourTransactionCommit() != NO_ERROR)
        {
            return cs::HookStatus::DetourFailed;
        }
        g_hooked = true;
        return cs::HookStatus::Hooked;
    }

    // Hooking and pipe work stay out of DllMain and its loader lock.
    std::wstring PipePath()
    {
        wchar_t name[128];
        const DWORD length = GetEnvironmentVariableW(cs::PipeNameVariable, name, static_cast<DWORD>(std::size(name)));
        const bool overridden = length != 0 && length < std::size(name);
        return std::wstring(L"\\\\.\\pipe\\") + (overridden ? std::wstring(name, length) : std::wstring(cs::PipeName));
    }

    DWORD WINAPI InitThread(LPVOID)
    {
        g_pipePath = PipePath();
        g_status = InstallHook();
        if (!g_stopping.load())
        {
            // Started even when hooking failed, so the app learns why from the hello.
            g_writerThread = CreateThread(nullptr, 0, WriterThread, nullptr, 0, nullptr);
        }
        return 0;
    }
}

// Started by the app as a remote thread to detach: removes the detour, stops the writer and
// unloads the DLL. Exit code 0 when unloaded, 1 when the writer would not stop (left loaded).
extern "C" DWORD WINAPI ClickSaverShutdown(LPVOID)
{
    g_stopping.store(true);
    SetEvent(g_stopEvent);
    WaitForSingleObject(g_initThread, INFINITE);

    if (g_hooked)
    {
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourDetach(reinterpret_cast<PVOID*>(&g_original), reinterpret_cast<PVOID>(DataBlockToMessageHook));
        DetourTransactionCommit();

        // A call may have entered the hook just before it was removed; let it leave.
        do
        {
            Sleep(50);
        } while (g_inFlight.load() != 0);
    }

    {
        std::lock_guard lock(g_queueLock);
    }
    g_queueSignal.notify_all();

    if (g_writerThread != nullptr)
    {
        // The writer may be blocked in CreateFile or WriteFile; keep cancelling until it exits.
        int attempts = 0;
        while (WaitForSingleObject(g_writerThread, 100) == WAIT_TIMEOUT)
        {
            CancelSynchronousIo(g_writerThread);
            if (++attempts == 50)
            {
                return 1;
            }
        }
        CloseHandle(g_writerThread);
    }

    CloseHandle(g_initThread);
    CloseHandle(g_stopEvent);
    FreeLibraryAndExitThread(g_module, 0);
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
        g_stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (g_stopEvent == nullptr)
        {
            return FALSE;
        }
        g_initThread = CreateThread(nullptr, 0, InitThread, nullptr, 0, nullptr);
        if (g_initThread == nullptr)
        {
            CloseHandle(g_stopEvent);
            return FALSE;
        }
    }
    return TRUE;
}
