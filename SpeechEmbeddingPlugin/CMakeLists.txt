cmake_minimum_required(VERSION 3.10)
project(wordwake LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(wordwake SHARED
    SpeechEmbeddingPlugin.cpp
)

set(ONNXRUNTIME_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/include/onnxruntime")
set(ONNXRUNTIME_LIB_DIR     "${CMAKE_CURRENT_SOURCE_DIR}/libs/arm64-v8a")

target_compile_definitions(wordwake PRIVATE USE_ONNXRUNTIME)

include_directories("${ONNXRUNTIME_INCLUDE_DIR}")
target_link_libraries(wordwake PRIVATE "${ONNXRUNTIME_LIB_DIR}/libonnxruntime.so")


