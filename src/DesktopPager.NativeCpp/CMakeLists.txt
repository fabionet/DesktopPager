cmake_minimum_required(VERSION 3.20)
project(DesktopPagerNativeCpp LANGUAGES CXX RC)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
add_executable(DesktopPagerNative WIN32
  main.cpp
  resources.rc
)
target_compile_definitions(DesktopPagerNative PRIVATE
  UNICODE
  _UNICODE
  WIN32_LEAN_AND_MEAN
  NOMINMAX
)
target_link_libraries(DesktopPagerNative PRIVATE
  user32
  shell32
  gdi32
  advapi32
  comctl32
)
if(MINGW)
  target_link_options(DesktopPagerNative PRIVATE -municode)
endif()
