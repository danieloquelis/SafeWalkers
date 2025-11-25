// Minimal C++ native plugin for Unity to bridge LocalWake-style speech embeddings.
// This file contains:
//   - A placeholder CPU implementation (always available).
//   - Optional ONNX Runtime integration when USE_ONNXRUNTIME is defined.

#include <cmath>
#include <cstdint>
#include <vector>
#include <cstring>

#if defined(USE_ONNXRUNTIME)
#include <onnxruntime_c_api.h>
#endif

// Basic configuration/state. Adjust as needed to match local-wake.
namespace
{
    int   gSampleRate     = 16000;
    int   gEmbeddingDim   = 96;
    int   gWindowSamples  = 16000 * 2; // 2 seconds by default
    int   gTimeSteps      = 64;        // arbitrary default, must match Unity side
    bool  gInitialized    = false;

    // Simple RMS-based VAD threshold. For normalized audio in [-1, 1],
    // voiced speech is typically >= 0.01 RMS; values below this are
    // treated as silence/background.
    float gVadRmsThreshold = 0.005f;

#if defined(USE_ONNXRUNTIME)
    const OrtApi* gOrt = nullptr;
    OrtEnv*       gEnv = nullptr;
    OrtSession*   gSession = nullptr;
    OrtSessionOptions* gSessionOptions = nullptr;
    bool          gUseOnnx = false;

    void ReleaseOnnx()
    {
        if (!gOrt)
            return;
        if (gSession)
        {
            gOrt->ReleaseSession(gSession);
            gSession = nullptr;
        }
        if (gSessionOptions)
        {
            gOrt->ReleaseSessionOptions(gSessionOptions);
            gSessionOptions = nullptr;
        }
        if (gEnv)
        {
            gOrt->ReleaseEnv(gEnv);
            gEnv = nullptr;
        }
        gUseOnnx = false;
    }

    int InitOnnxFromBuffer(const void* modelData, size_t modelSize)
    {
        if (!modelData || modelSize == 0)
            return 0;

        gOrt = OrtGetApiBase()->GetApi(ORT_API_VERSION);
        if (!gOrt)
            return 0;

        OrtStatus* status = nullptr;

        status = gOrt->CreateEnv(ORT_LOGGING_LEVEL_WARNING, "LocalWake", &gEnv);
        if (status)
        {
            gOrt->ReleaseStatus(status);
            return 0;
        }

        status = gOrt->CreateSessionOptions(&gSessionOptions);
        if (status)
        {
            gOrt->ReleaseStatus(status);
            ReleaseOnnx();
            return 0;
        }

        // Enable basic optimizations.
        (void)gOrt->SetIntraOpNumThreads(gSessionOptions, 1);
        (void)gOrt->SetSessionGraphOptimizationLevel(gSessionOptions, ORT_ENABLE_BASIC);

        status = gOrt->CreateSessionFromArray(
            gEnv,
            modelData,
            modelSize,
            gSessionOptions,
            &gSession);

        if (status)
        {
            gOrt->ReleaseStatus(status);
            ReleaseOnnx();
            return 0;
        }

        gUseOnnx = true;
        return 1;
    }

    int ComputeEmbeddingOnnx(const float* audioSamples,
                             int          numSamples,
                             float*       outEmbedding,
                             int          outLength)
    {
        if (!gUseOnnx || !gSession || !gOrt)
            return 0;

        // Prepare input tensor (assume 1D: [1, numSamples])
        OrtAllocator* allocator = nullptr;
        OrtStatus* status = gOrt->GetAllocatorWithDefaultOptions(&allocator);
        if (status)
        {
            gOrt->ReleaseStatus(status);
            return 0;
        }

        size_t numInputNodes = 0;
        status = gOrt->SessionGetInputCount(gSession, &numInputNodes);
        if (status || numInputNodes == 0)
        {
            if (status) gOrt->ReleaseStatus(status);
            return 0;
        }

        char* inputName = nullptr;
        status = gOrt->SessionGetInputName(gSession, 0, allocator, &inputName);
        if (status)
        {
            gOrt->ReleaseStatus(status);
            return 0;
        }

        std::vector<int64_t> inputShape = {1, numSamples};
        OrtMemoryInfo* memInfo = nullptr;
        status = gOrt->CreateCpuMemoryInfo(OrtArenaAllocator, OrtMemTypeDefault, &memInfo);
        if (status)
        {
            gOrt->ReleaseStatus(status);
            allocator->Free(allocator, inputName);
            return 0;
        }

        OrtValue* inputTensor = nullptr;
        status = gOrt->CreateTensorWithDataAsOrtValue(
            memInfo,
            const_cast<float*>(audioSamples),
            sizeof(float) * static_cast<size_t>(numSamples),
            inputShape.data(),
            inputShape.size(),
            ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT,
            &inputTensor);

        gOrt->ReleaseMemoryInfo(memInfo);

        if (status)
        {
            gOrt->ReleaseStatus(status);
            allocator->Free(allocator, inputName);
            return 0;
        }

        const char* inputNames[] = { inputName };

        size_t numOutputNodes = 0;
        status = gOrt->SessionGetOutputCount(gSession, &numOutputNodes);
        if (status || numOutputNodes == 0)
        {
            if (status) gOrt->ReleaseStatus(status);
            gOrt->ReleaseValue(inputTensor);
            allocator->Free(allocator, inputName);
            return 0;
        }

        char* outputName = nullptr;
        status = gOrt->SessionGetOutputName(gSession, 0, allocator, &outputName);
        if (status)
        {
            gOrt->ReleaseStatus(status);
            gOrt->ReleaseValue(inputTensor);
            allocator->Free(allocator, inputName);
            return 0;
        }

        const char* outputNames[] = { outputName };

        OrtValue* outputTensor = nullptr;
        status = gOrt->Run(
            gSession,
            nullptr,
            inputNames,
            (const OrtValue* const*)&inputTensor,
            1,
            outputNames,
            1,
            &outputTensor);

        allocator->Free(allocator, inputName);
        allocator->Free(allocator, outputName);
        gOrt->ReleaseValue(inputTensor);

        if (status)
        {
            gOrt->ReleaseStatus(status);
            if (outputTensor)
                gOrt->ReleaseValue(outputTensor);
            return 0;
        }

        // Read output tensor shape.
        OrtTensorTypeAndShapeInfo* info = nullptr;
        status = gOrt->GetTensorTypeAndShape(outputTensor, &info);
        if (status)
        {
            gOrt->ReleaseStatus(status);
            gOrt->ReleaseValue(outputTensor);
            return 0;
        }

        size_t dimCount = 0;
        (void)gOrt->GetDimensionsCount(info, &dimCount);
        std::vector<int64_t> dims(dimCount);
        (void)gOrt->GetDimensions(info, dims.data(), dimCount);
        gOrt->ReleaseTensorTypeAndShapeInfo(info);

        // Flatten dims to total elements.
        int64_t totalElements = 1;
        for (size_t i = 0; i < dimCount; ++i)
            totalElements *= dims[i];

        if (totalElements > outLength)
        {
            gOrt->ReleaseValue(outputTensor);
            return 0;
        }

        float* outData = nullptr;
        status = gOrt->GetTensorMutableData(outputTensor, (void**)&outData);
        if (status)
        {
            gOrt->ReleaseStatus(status);
            gOrt->ReleaseValue(outputTensor);
            return 0;
        }

        std::memcpy(outEmbedding, outData, sizeof(float) * static_cast<size_t>(totalElements));
        gOrt->ReleaseValue(outputTensor);

        return 1;
    }
#endif // USE_ONNXRUNTIME

    int ComputeEmbeddingFallback(const float* audioSamples,
                                 int          numSamples,
                                 float*       outEmbedding,
                                 int          outLength)
    {
        const int expectedOut = gEmbeddingDim * gTimeSteps;
        if (outLength < expectedOut)
            return 0;

        const int samplesPerStep = (numSamples > 0 && gTimeSteps > 0)
            ? (numSamples / gTimeSteps)
            : 0;

        if (samplesPerStep <= 0)
        {
            // Fallback: zero embedding.
            for (int i = 0; i < expectedOut; ++i)
                outEmbedding[i] = 0.0f;
            return 1;
        }

        for (int t = 0; t < gTimeSteps; ++t)
        {
            const int start = t * samplesPerStep;
            const int end   = (t == gTimeSteps - 1)
                ? numSamples
                : start + samplesPerStep;

            float sum = 0.0f;
            float energy = 0.0f;
            int count = 0;
            for (int i = start; i < end && i < numSamples; ++i)
            {
                const float v = audioSamples[i];
                sum += v;
                energy += v * v;
                ++count;
            }

            const float mean    = (count > 0) ? (sum / count) : 0.0f;
            const float rms     = (count > 0) ? std::sqrt(energy / count) : 0.0f;
            const float energyN = (count > 0) ? (energy / count) : 0.0f;

            for (int d = 0; d < gEmbeddingDim; ++d)
            {
                const int idx = d * gTimeSteps + t; // [d, t] flattened in column-major by time.

                float v = 0.0f;
                switch (d % 3)
                {
                    case 0: v = mean; break;
                    case 1: v = rms; break;
                    case 2: v = energyN; break;
                }
                v *= 1.0f + 0.001f * static_cast<float>(d);
                outEmbedding[idx] = v;
            }
        }

        return 1;
    }
}

extern "C"
{
    // Initialize the embedding engine without a model (pure fallback).
    // Returns 1 on success, 0 on failure.
    __attribute__((visibility("default")))
    int LW_Init(int sampleRate, int embeddingDim, int windowSamples, int timeSteps)
    {
        if (sampleRate <= 0 || embeddingDim <= 0 || windowSamples <= 0 || timeSteps <= 0)
            return 0;

        gSampleRate    = sampleRate;
        gEmbeddingDim  = embeddingDim;
        gWindowSamples = windowSamples;
        gTimeSteps     = timeSteps;
        gInitialized   = true;
        return 1;
    }

    // Initialize the embedding engine with an ONNX model (if USE_ONNXRUNTIME is enabled).
    // modelData / modelSize: in-memory ONNX model.
    // Returns 1 on success, 0 on failure.
    __attribute__((visibility("default")))
    int LW_InitFromOnnxBytes(const uint8_t* modelData,
                             int           modelSize,
                             int           sampleRate,
                             int           embeddingDim,
                             int           windowSamples,
                             int           timeSteps)
    {
        if (sampleRate <= 0 || embeddingDim <= 0 || windowSamples <= 0 || timeSteps <= 0)
            return 0;

        gSampleRate    = sampleRate;
        gEmbeddingDim  = embeddingDim;
        gWindowSamples = windowSamples;
        gTimeSteps     = timeSteps;

#if defined(USE_ONNXRUNTIME)
        if (!InitOnnxFromBuffer(modelData, static_cast<size_t>(modelSize)))
        {
            gInitialized = false;
            return 0;
        }
        gInitialized = true;
        return 1;
#else
        (void)modelData;
        (void)modelSize;
        gInitialized = true;
        return 1;
#endif
    }

    // Compute an embedding for a mono audio window.
    //
    // audioSamples: pointer to float samples (mono) of length numSamples.
    // numSamples:   length of the input buffer (should match windowSamples from LW_Init*).
    // outEmbedding: pointer to float buffer to be filled by the plugin.
    // outLength:    number of floats available at outEmbedding (embeddingDim * timeSteps).
    //
    // Returns 1 on success, 0 on failure.
    __attribute__((visibility("default")))
    int LW_ComputeEmbedding(const float* audioSamples,
                            int          numSamples,
                            float*       outEmbedding,
                            int          outLength)
    {
        if (!gInitialized || !audioSamples || !outEmbedding)
            return 0;

        // ---------------------------------------------------------------------
        // Simple VAD: if the window RMS is below a small threshold, treat it
        // as silence and return a zero embedding. This prevents pure silence
        // (or near-silence) from ever matching a spoken wake-word reference.
        // ---------------------------------------------------------------------
        if (numSamples <= 0)
            return 0;

        double energy = 0.0;
        for (int i = 0; i < numSamples; ++i)
        {
            const double v = static_cast<double>(audioSamples[i]);
            energy += v * v;
        }
        const double rms = std::sqrt(energy / static_cast<double>(numSamples));

        if (rms < static_cast<double>(gVadRmsThreshold))
        {
            const int expectedOut = gEmbeddingDim * gTimeSteps;
            const int n = (outLength < expectedOut) ? outLength : expectedOut;
            for (int i = 0; i < n; ++i)
                outEmbedding[i] = 0.0f;
            return 1;
        }

#if defined(USE_ONNXRUNTIME)
        if (gUseOnnx && gSession && gOrt)
        {
            if (ComputeEmbeddingOnnx(audioSamples, numSamples, outEmbedding, outLength))
                return 1;
            // If ONNX fails, fall back.
        }
#endif
        return ComputeEmbeddingFallback(audioSamples, numSamples, outEmbedding, outLength);
    }

    // Shutdown and free resources.
    __attribute__((visibility("default")))
    void LW_Shutdown()
    {
#if defined(USE_ONNXRUNTIME)
        ReleaseOnnx();
#endif
        gInitialized = false;
    }
}

