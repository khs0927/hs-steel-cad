# HsSteel.Arx - C++ ObjectARX module (P5). Not part of the default build.
# Configure:  cmake -S . -B build -A x64 -DARX_SDK=C:/ObjectARX_2027
cmake_minimum_required(VERSION 3.25)
project(HsSteelArx LANGUAGES CXX)
if(NOT ARX_SDK)
  message(FATAL_ERROR "Set -DARX_SDK=<ObjectARX 2027 SDK path> (download from Autodesk Platform Services).")
endif()
set(CMAKE_CXX_STANDARD 20)
add_library(HsSteelArx SHARED src/HsMember.cpp src/Entry.cpp)
set_target_properties(HsSteelArx PROPERTIES SUFFIX ".arx")
target_include_directories(HsSteelArx PRIVATE include ${ARX_SDK}/inc ${ARX_SDK}/inc-x64)
target_link_directories(HsSteelArx PRIVATE ${ARX_SDK}/lib-x64)
target_link_libraries(HsSteelArx PRIVATE rxapi accore acdb26 acge26 acgiapi)
target_compile_definitions(HsSteelArx PRIVATE _AFXEXT UNICODE _UNICODE)
