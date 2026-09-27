cmake_minimum_required(VERSION 3.20)
project(okcore C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

# The simulation must give the same bits everywhere, so no fast-math and
# no contraction of a*b+c into one rounding.
if(MSVC)
    add_compile_options(/fp:precise /W4)
else()
    add_compile_options(-ffp-contract=off -fno-fast-math -Wall -Wextra)
endif()

set(OKCORE_SOURCES
    src/ok_sim.c
    src/trig.c
    src/sha256.c)

# Static for IL2CPP and WebGL builds, which link the plugin in.
add_library(okcore_static STATIC ${OKCORE_SOURCES})
target_include_directories(okcore_static PUBLIC include)

# Shared for the Unity editor and Mono players, which load it by name.
add_library(okcore SHARED ${OKCORE_SOURCES})
target_include_directories(okcore PUBLIC include)
target_compile_definitions(okcore PRIVATE OK_SIM_BUILD PUBLIC OK_SIM_SHARED)
if(NOT MSVC)
    target_link_libraries(okcore PRIVATE m)
    target_link_libraries(okcore_static PUBLIC m)
endif()

include(CTest)
if(BUILD_TESTING)
    foreach(t test_ok_sim test_trig test_bytes)
        add_executable(${t} tests/${t}.c)
        target_include_directories(${t} PRIVATE tests)
        target_link_libraries(${t} PRIVATE okcore_static)
        add_test(NAME ${t} COMMAND ${t})
    endforeach()
endif()
