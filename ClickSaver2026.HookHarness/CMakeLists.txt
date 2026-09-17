cmake_minimum_required(VERSION 3.25)

# A stand-in for the game client, so the hook can be attached, used and detached in tests
# without Anarchy Online: MessageProtocol.dll exports the same DataBlockToMessage, and
# HookHost.exe calls it the way the client does.
project(ClickSaver2026HookHarness LANGUAGES CXX)

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
    message(FATAL_ERROR "The harness must be built 32-bit, like the client: configure with -A Win32")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

set(CLICKSAVER_OUTPUT_DIR "${CMAKE_BINARY_DIR}" CACHE PATH "Where Release\ and Debug\ outputs go")
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CLICKSAVER_OUTPUT_DIR}/$<CONFIG>")

add_library(MessageProtocol SHARED MessageProtocol.cpp)
target_compile_options(MessageProtocol PRIVATE /W4 /WX /permissive-)

add_executable(HookHost HookHost.cpp)
target_link_libraries(HookHost PRIVATE MessageProtocol)
target_compile_options(HookHost PRIVATE /W4 /WX /permissive-)
