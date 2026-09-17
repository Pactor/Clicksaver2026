cmake_minimum_required(VERSION 3.25)

project(ClickSaver2026Hook LANGUAGES CXX)

# The hook is loaded into the Anarchy Online client, which is a 32-bit process.
if(NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
    message(FATAL_ERROR "The hook must be built 32-bit: configure with -A Win32")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Static CRT: nothing extra has to be present in the client's folder.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_library(detours STATIC
    external/detours/detours.cpp
    external/detours/disasm.cpp
    external/detours/image.cpp
    external/detours/modules.cpp
)
target_include_directories(detours PUBLIC external/detours)
target_compile_definitions(detours PRIVATE WIN32_LEAN_AND_MEAN _WIN32_WINNT=0x0A00)

add_library(ClickSaver2026.Hook SHARED hook.cpp hook.def)
target_link_libraries(ClickSaver2026.Hook PRIVATE detours)
target_compile_definitions(ClickSaver2026.Hook PRIVATE UNICODE _UNICODE _WIN32_WINNT=0x0A00)
target_compile_options(ClickSaver2026.Hook PRIVATE /W4 /WX /permissive- /utf-8 /guard:cf)
target_link_options(ClickSaver2026.Hook PRIVATE /DYNAMICBASE /NXCOMPAT /SAFESEH /GUARD:CF)
